document.addEventListener('alpine:init', () => {
  Alpine.data('overviewPage', () => ({
    loading: true,
    error: null,
    stats: null,
    activity: [],
    timer: null,

    async init() {
      await this.refresh();
      // Poll while the tab is visible; background tabs stay quiet.
      this.timer = setInterval(() => { if (!document.hidden) this.refresh(true); }, 5000);
    },
    destroy() { clearInterval(this.timer); },

    async refresh(quiet = false) {
      try {
        const [stats, activity] = await Promise.all([api.get('/stats'), api.get('/activity?take=15')]);
        this.stats = stats;
        this.activity = activity;
        this.error = null;
      } catch (e) {
        if (!quiet) this.error = e.message;
      } finally {
        this.loading = false;
      }
    },

    open(a) { location.hash = '#/instances/' + a.entry.instanceId; },
  }));
});
