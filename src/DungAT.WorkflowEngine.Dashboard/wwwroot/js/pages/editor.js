document.addEventListener('alpine:init', () => {
  let uidCounter = 0;
  const uid = () => 'u' + ++uidCounter;

  Alpine.data('editorPage', () => ({
    loading: true,
    ready: false,
    error: null,
    isNew: true,
    baseVersion: 0,
    form: null,
    errors: [],
    validating: false,
    touched: false,
    saving: false,
    svg: '',
    timer: null,
    seq: 0,

    // Only components that are generic or made for the chosen entity type can be picked (see [ForEntity] on the server).
    get guards() { return this.usable(this.$store.app.meta.guards); },
    get effects() { return this.usable(this.$store.app.meta.effects); },
    get entities() {
      const registered = this.$store.app.meta.entities;
      const current = this.form.entityType.trim();
      // Keep a value that is not a registered entity (e.g. an older workflow) selectable instead of silently dropping it.
      return current && !registered.some((e) => e.toLowerCase() === current.toLowerCase()) ? [...registered, current] : registered;
    },
    get postActions() { return this.usable(this.$store.app.meta.postActions); },
    get allPostActions() { return this.$store.app.meta.postActions.length; },
    get allGuards() { return this.$store.app.meta.guards.length; },
    get allEffects() { return this.$store.app.meta.effects.length; },
    get commandCodes() {
      return [...new Set(this.form.transitions.map((t) => t.command.trim()).filter(Boolean))];
    },
    get canPublish() {
      return this.touched && !this.validating && !this.saving && this.errors.length === 0 && !!this.form.code.trim();
    },

    async init() {
      const { mode, code } = this.$store.router.params;
      const { from, v } = this.$store.router.query;
      this.isNew = mode === 'new';
      try {
        if (this.$store.app.meta.readOnly) throw new Error('The dashboard is read-only.');
        if (mode === 'edit') {
          const detail = await api.get('/definitions/' + encodeURIComponent(code));
          if (!detail.editable) throw new Error(`'${code}' is defined in code and cannot be edited here. Duplicate it to make a copy.`);
          this.baseVersion = detail.definition.version;
          this.form = this.fromDefinition(detail.definition);
        } else if (from) {
          const detail = await api.get('/definitions/' + encodeURIComponent(from) + api.qs({ version: v }));
          this.form = this.fromDefinition(detail.definition);
          this.form.code = '';
          this.form.name = detail.definition.name + ' (copy)';
        } else {
          this.form = this.starter();
        }
        this.ready = true;
        // Re-validate (debounced) whenever anything in the form changes.
        this.$watch(() => JSON.stringify(this.form), () => this.schedule());
        this.preview();
        if (!this.isNew) this.touched = false;
      } catch (e) {
        this.error = e.message;
      } finally {
        this.loading = false;
      }
    },
    destroy() { clearTimeout(this.timer); },

    // ----- model <-> definition -----
    starter() {
      const draft = { uid: uid(), code: 'DRAFT', name: 'Draft', type: 'Initial', postActions: [] };
      const submitted = { uid: uid(), code: 'SUBMITTED', name: 'Submitted', type: 'Intermediate', postActions: [] };
      const approved = { uid: uid(), code: 'APPROVED', name: 'Approved', type: 'Final', postActions: [] };
      return {
        code: '', name: '', description: '', entityType: '', commandNames: { SUBMIT: 'Submit', APPROVE: 'Approve' },
        states: [draft, submitted, approved],
        transitions: [
          this.newTransition({ fromUid: draft.uid, command: 'SUBMIT', toUid: submitted.uid }),
          this.newTransition({ fromUid: submitted.uid, command: 'APPROVE', toUid: approved.uid, roles: 'Manager' }),
        ],
      };
    },
    newTransition(init = {}) {
      return { uid: uid(), fromUid: '', command: '', toUid: '', roles: '', guards: [], effects: [], postActions: [], requireComment: false, ...init };
    },
    fromDefinition(d) {
      const states = d.states.map((s) => ({ uid: uid(), code: s.code, name: s.name, type: s.type, postActions: [...(s.postActionNames || [])] }));
      const byCode = (c) => states.find((s) => s.code.toUpperCase() === c.toUpperCase())?.uid || '';
      return {
        code: d.code, name: d.name, description: d.description || '', entityType: d.entityType || '',
        commandNames: Object.fromEntries(d.commands.map((c) => [c.code, c.name])),
        states,
        transitions: d.transitions.map((t) => this.newTransition({
          fromUid: byCode(t.fromStateCode), toUid: byCode(t.toStateCode), command: t.commandCode,
          roles: t.allowedRoles.join(', '), guards: [...t.guardNames], effects: [...t.effectNames], postActions: [...(t.postActionNames || [])], requireComment: t.requireComment,
        })),
      };
    },
    payload() {
      const codeOf = (u) => (this.form.states.find((s) => s.uid === u)?.code || '').trim();
      return {
        code: this.form.code.trim(),
        name: this.form.name.trim(),
        description: this.form.description.trim() || null,
        entityType: this.form.entityType.trim() || null,
        states: this.form.states.map((s, i) => ({ code: s.code.trim(), name: (s.name || s.code).trim(), type: s.type, order: i, postActionNames: [...s.postActions] })),
        commands: this.commandCodes.map((c) => ({ code: c, name: (this.form.commandNames[c] || c).trim() })),
        transitions: this.form.transitions.map((t) => ({
          fromStateCode: codeOf(t.fromUid), commandCode: t.command.trim(), toStateCode: codeOf(t.toUid),
          allowedRoles: t.roles.split(',').map((r) => r.trim()).filter(Boolean),
          guardNames: [...t.guards], effectNames: [...t.effects], postActionNames: [...t.postActions], requireComment: t.requireComment,
        })),
      };
    },

    // ----- editing -----
    usable(components) {
      const entity = this.form.entityType.trim().toLowerCase();
      return components
        .filter((c) => c.appliesToAnyEntity || (entity && c.entityNames.some((n) => n.toLowerCase() === entity)))
        .map((c) => c.name);
    },
    available(all, chosen) { return all.filter((x) => !chosen.includes(x)); },
    addState() { this.form.states.push({ uid: uid(), code: '', name: '', type: 'Intermediate', postActions: [] }); },
    removeState(i) {
      const [removed] = this.form.states.splice(i, 1);
      // Transitions pointing at a removed state would be dangling, so drop them.
      this.form.transitions = this.form.transitions.filter((t) => t.fromUid !== removed.uid && t.toUid !== removed.uid);
    },
    move(i, delta) {
      const j = i + delta;
      if (j < 0 || j >= this.form.states.length) return;
      const [s] = this.form.states.splice(i, 1);
      this.form.states.splice(j, 0, s);
    },
    addTransition() { this.form.transitions.push(this.newTransition()); },
    removeTransition(i) { this.form.transitions.splice(i, 1); },

    // ----- validation & preview -----
    schedule() {
      this.touched = true;
      this.validating = true;
      clearTimeout(this.timer);
      this.timer = setTimeout(() => this.validate(), 400);
      this.preview();
    },
    preview() {
      const p = this.payload();
      const known = new Set(p.states.filter((s) => s.code).map((s) => s.code.toUpperCase()));
      this.svg = Diagram.render({
        ...p,
        states: p.states.filter((s) => s.code),
        transitions: p.transitions.filter((t) => known.has(t.fromStateCode.toUpperCase()) && known.has(t.toStateCode.toUpperCase())),
      });
    },
    async validate() {
      const mine = ++this.seq;
      try {
        const res = await api.post('/definitions/validate', this.payload());
        if (mine === this.seq) this.errors = res.errors;
      } catch (e) {
        if (mine === this.seq) this.errors = [e.message];
      } finally {
        if (mine === this.seq) this.validating = false;
      }
    },

    async publish() {
      const p = this.payload();
      const ok = await this.$store.ui.confirm({
        title: this.isNew ? `Publish "${p.name}"?` : `Publish version ${this.baseVersion + 1}?`,
        message: this.isNew
          ? 'The workflow becomes available for new instances right away.'
          : `New instances will use version ${this.baseVersion + 1}. Running instances stay on version ${this.baseVersion}.`,
        confirmText: 'Publish',
      });
      if (!ok) return;
      this.saving = true;
      try {
        const created = await api.post('/definitions', p);
        this.$store.ui.toast(`Published ${created.definition.name} v${created.definition.version}.`, 'success');
        this.$store.router.go('/definitions/' + encodeURIComponent(created.definition.code));
      } catch (e) {
        if (e.status === 400 && e.errors.length) this.errors = e.errors;
        this.$store.ui.toast(e.message, 'error');
      } finally {
        this.saving = false;
      }
    },
  }));
});
