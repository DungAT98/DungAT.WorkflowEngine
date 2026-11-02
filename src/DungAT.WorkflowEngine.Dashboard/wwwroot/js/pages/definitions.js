document.addEventListener('alpine:init', () => {
  Alpine.data('definitionsPage', () => ({
    loading: true,
    error: null,
    items: [],

    async init() {
      try { this.items = await api.get('/definitions'); }
      catch (e) { this.error = e.message; }
      finally { this.loading = false; }
    },
  }));
});
