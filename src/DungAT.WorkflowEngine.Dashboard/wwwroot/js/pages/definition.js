document.addEventListener('alpine:init', () => {
  Alpine.data('definitionPage', () => ({
    loading: true,
    error: null,
    detail: null,
    latest: 0,
    svg: '',
    tab: 'transitions',
    changes: null,

    // A getter keeps `def` in step with `detail`, so templates never see one set without the other.
    get def() { return this.detail?.definition; },

    async init() {
      const { code } = this.$store.router.params;
      const version = this.$store.router.query.version;
      try {
        this.detail = await api.get(`/definitions/${encodeURIComponent(code)}` + api.qs({ version }));
        this.latest = Math.max(...this.detail.versions.map((v) => v.version));
        this.svg = Diagram.render(this.def);
        document.title = `${this.def.name} · ${this.$store.app.meta.title}`;
        this.loadChanges();
      } catch (e) {
        this.error = e.status === 404 ? `The workflow '${code}' does not exist.` : e.message;
      } finally {
        this.loading = false;
      }
    },

    selectVersion(v) {
      this.$store.router.go('/definitions/' + encodeURIComponent(this.def.code), { version: Number(v) === this.latest ? undefined : v });
    },

    nameOf(code) { return this.def.states.find((s) => s.code.toUpperCase() === code.toUpperCase())?.name || code; },
    typeOf(code) { return this.def.states.find((s) => s.code.toUpperCase() === code.toUpperCase())?.type; },
    commandName(code) { return this.def.commands.find((c) => c.code.toUpperCase() === code.toUpperCase())?.name || code; },

    // Compares with the previous version so people can see what a new publish changed.
    async loadChanges() {
      if (this.def.version <= 1) return;
      try {
        const prev = (await api.get(`/definitions/${encodeURIComponent(this.def.code)}?version=${this.def.version - 1}`)).definition;
        const lines = [];
        const states = (d) => new Set(d.states.map((s) => `state ${s.code} (${s.type})`));
        const trans = (d) => new Set(d.transitions.map((t) => `${t.fromStateCode} --${t.commandCode}--> ${t.toStateCode}` +
          (t.allowedRoles.length ? ` [roles: ${t.allowedRoles.join(', ')}]` : '') +
          (t.guardNames.length ? ` [guards: ${t.guardNames.join(', ')}]` : '') +
          (t.requireComment ? ' [comment required]' : '')));
        for (const pick of [states, trans]) {
          const a = pick(prev), b = pick(this.def);
          [...b].filter((x) => !a.has(x)).forEach((text) => lines.push({ kind: 'add', text }));
          [...a].filter((x) => !b.has(x)).forEach((text) => lines.push({ kind: 'del', text }));
        }
        this.changes = { lines };
      } catch { /* the diff is a nicety; ignore failures */ }
    },
  }));
});
