// Sidebar / theme / bootstrap. Loads /meta before the first route renders.
document.addEventListener('alpine:init', () => {
  const icons = {
    overview: '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="9" rx="1.5"/><rect x="14" y="3" width="7" height="5" rx="1.5"/><rect x="14" y="12" width="7" height="9" rx="1.5"/><rect x="3" y="16" width="7" height="5" rx="1.5"/></svg>',
    definitions: '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="6" cy="6" r="3"/><circle cx="18" cy="18" r="3"/><path d="M9 6h4a4 4 0 0 1 4 4v5"/></svg>',
    instances: '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M8 6h13M8 12h13M8 18h13"/><circle cx="3.5" cy="6" r="1"/><circle cx="3.5" cy="12" r="1"/><circle cx="3.5" cy="18" r="1"/></svg>',
  };

  Alpine.store('app', { meta: null });

  Alpine.data('shell', () => ({
    navOpen: false,
    theme: document.documentElement.dataset.theme || 'auto',
    title: window.__WORKFLOW__.title,
    nav: [
      { key: 'overview', label: 'Overview', href: '#/', icon: icons.overview },
      { key: 'definitions', label: 'Workflows', href: '#/definitions', icon: icons.definitions },
      { key: 'instances', label: 'Instances', href: '#/instances', icon: icons.instances },
    ],

    get isDark() { return this.theme === 'dark' || (this.theme === 'auto' && matchMedia('(prefers-color-scheme: dark)').matches); },
    get initials() { return fmt.initials(this.$store.app.meta?.userId); },
    get roles() { return (this.$store.app.meta?.userRoles || []).join(', ') || 'No roles'; },

    async init() {
      window.addEventListener('hashchange', () => { this.navOpen = false; });
      try {
        this.$store.app.meta = await api.get('/meta');
        this.title = this.$store.app.meta.title;
      } catch (e) {
        document.getElementById('view').innerHTML =
          `<div class="empty"><h2>Cannot open the dashboard</h2><p class="muted">${String(e.message).replace(/</g, '&lt;')}</p></div>`;
        return;
      }
      await this.$store.router.start();
    },

    toggleTheme() {
      this.theme = this.isDark ? 'light' : 'dark';
      document.documentElement.dataset.theme = this.theme;
      try { localStorage.setItem('wf-theme', this.theme); } catch { /* storage unavailable */ }
    },
  }));
});
