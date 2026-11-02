document.addEventListener('alpine:init', () => {
  const PAGE_SIZE = 25;

  Alpine.data('instancesPage', () => ({
    loading: true,
    error: null,
    result: null,
    definitions: [],
    filters: { q: '', definition: '', state: '', page: 1 },
    showStart: false,
    starting: false,
    form: { definitionCode: '', entityType: '', entityId: '', data: '' },
    seq: 0,
    stateOptions: [], // sorted state codes of the chosen workflow, or of all workflows

    async init() {
      const q = this.$store.router.query;
      this.filters = { q: q.q || '', definition: q.definition || '', state: q.state || '', page: Number(q.page) || 1 };
      try { this.definitions = await api.get('/definitions'); } catch { /* the filter dropdown just stays empty */ }
      await this.load();
    },

    get hasFilters() { return !!(this.filters.q || this.filters.definition || this.filters.state); },
    get pages() { return this.result ? Math.max(1, Math.ceil(this.result.total / this.result.pageSize)) : 1; },
    get rangeText() {
      if (!this.result || this.result.total === 0) return '';
      const from = (this.result.page - 1) * this.result.pageSize + 1;
      return `${from}–${from + this.result.items.length - 1} of ${this.result.total.toLocaleString()}`;
    },

    apply() { this.filters.page = 1; this.load(); },
    go(page) { this.filters.page = page; this.load(); },
    clear() { this.filters = { q: '', definition: '', state: '', page: 1 }; this.load(); },
    open(i) { location.hash = '#/instances/' + i.id; },

    async load() {
      const mine = ++this.seq; // ignore out-of-order responses while typing
      this.loading = true;
      const f = this.filters;
      this.$store.router.replaceQuery({ q: f.q || undefined, definition: f.definition || undefined, state: f.state || undefined, page: f.page > 1 ? f.page : undefined });
      try {
        const res = await api.get('/instances' + api.qs({ q: f.q, definition: f.definition, state: f.state, page: f.page, pageSize: PAGE_SIZE }));
        if (mine !== this.seq) return;
        this.result = res;
        this.error = null;
        this.refreshStates();
      } catch (e) {
        if (mine === this.seq) this.error = e.message;
      } finally {
        if (mine === this.seq) this.loading = false;
      }
    },

    // State dropdown options come from the workflow definitions (latest versions).
    async refreshStates() {
      const codes = this.filters.definition ? [this.filters.definition] : this.definitions.map((d) => d.code);
      const details = await Promise.all(codes.map((c) => api.get('/definitions/' + encodeURIComponent(c)).catch(() => null)));
      this.stateOptions = [...new Set(details.filter(Boolean).flatMap((d) => d.definition.states.map((s) => s.code)))].sort();
    },

    pickDefinition() {
      const d = this.definitions.find((x) => x.code === this.form.definitionCode);
      if (d?.entityType && !this.form.entityType) this.form.entityType = d.entityType;
    },

    async start() {
      this.starting = true;
      try {
        const created = await api.post('/instances', {
          definitionCode: this.form.definitionCode, entityType: this.form.entityType,
          entityId: this.form.entityId, data: this.form.data || null,
        });
        this.$store.ui.toast('Instance started.', 'success');
        (created.warnings || []).forEach((w) => this.$store.ui.toast(w, 'warn', 9000));
        this.$store.router.go('/instances/' + created.instance.id);
      } catch (e) {
        this.$store.ui.toast(e.message, 'error');
      } finally {
        this.starting = false;
      }
    },
  }));
});
