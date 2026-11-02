document.addEventListener('alpine:init', () => {
  Alpine.data('instancePage', () => ({
    loading: true,
    error: null,
    detail: null,
    svg: '',
    busy: false,
    running: null,

    get inst() { return this.detail?.instance; },

    async init() {
      await this.load();
      this.loading = false;
    },

    async load() {
      const { id } = this.$store.router.params;
      try {
        this.detail = await api.get('/instances/' + encodeURIComponent(id));
        this.svg = Diagram.render(this.detail.definition, { current: this.inst.currentStateCode });
        this.error = null;
      } catch (e) {
        if (!this.detail) this.error = e.status === 404 ? 'This instance does not exist.' : e.message;
        else this.$store.ui.toast(e.message, 'error');
      }
    },

    async refresh() {
      this.busy = true;
      await this.load();
      this.busy = false;
    },

    get historyNewestFirst() { return [...this.detail.history].reverse(); },
    get prettyData() {
      try { return JSON.stringify(JSON.parse(this.detail.data), null, 2); } catch { return this.detail.data; }
    },
    get emptyActionsText() {
      if (!this.detail.canExecute) return 'The dashboard is read-only.';
      if (this.inst.stateType === 'Final') return 'This workflow has finished. No further actions are possible.';
      return 'You have no actions available in this state. Another role may be able to continue it.';
    },

    stateName(code) { return this.detail.definition.states.find((s) => s.code.toUpperCase() === code.toUpperCase())?.name || code; },
    needsComment(c) {
      return !!this.detail.definition.transitions.find((t) =>
        t.fromStateCode.toUpperCase() === this.inst.currentStateCode.toUpperCase() && t.commandCode.toUpperCase() === c.code.toUpperCase())?.requireComment;
    },

    async run(command) {
      let comment = null;
      if (this.needsComment(command)) {
        comment = await this.$store.ui.prompt({
          title: command.name, label: 'Comment (required)', required: true, confirmText: command.name,
          message: `This will move ${this.inst.entityType} #${this.inst.entityId} out of "${this.inst.currentStateName}".`,
        });
        if (comment === null) return;
      }
      this.busy = true;
      this.running = command.code;
      try {
        const res = await api.post(`/instances/${this.inst.id}/commands/${encodeURIComponent(command.code)}`, { comment });
        this.$store.ui.toast(`${command.name} done.`, 'success');
        // The transition is saved even when an after-action failed, so this is a warning, not an error.
        (res.warnings || []).forEach((w) => this.$store.ui.toast(w, 'warn', 9000));
        await this.load();
      } catch (e) {
        this.$store.ui.toast(e.message, 'error');
        if (e.status === 409 || e.status === 422 || e.status === 403) await this.load(); // state or permissions changed under us
      } finally {
        this.busy = false;
        this.running = null;
      }
    },
  }));
});
