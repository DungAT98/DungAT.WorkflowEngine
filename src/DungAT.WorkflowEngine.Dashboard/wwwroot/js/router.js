// Hash router. Each route is a template in pages/ plus an Alpine component registered by js/pages/*.js.
document.addEventListener('alpine:init', () => {
  const routes = [
    { pattern: /^\/?$/, section: 'overview', page: 'overview', title: 'Overview' },
    { pattern: /^\/definitions\/?$/, section: 'definitions', page: 'definitions', title: 'Workflows' },
    { pattern: /^\/definitions\/new\/?$/, section: 'definitions', page: 'editor', title: 'New workflow', params: { mode: 'new' } },
    { pattern: /^\/definitions\/([^/]+)\/edit\/?$/, section: 'definitions', page: 'editor', title: 'Edit workflow', keys: ['code'], params: { mode: 'edit' } },
    { pattern: /^\/definitions\/([^/]+)\/?$/, section: 'definitions', page: 'definition', title: 'Workflow', keys: ['code'] },
    { pattern: /^\/instances\/?$/, section: 'instances', page: 'instances', title: 'Instances' },
    { pattern: /^\/instances\/([^/]+)\/?$/, section: 'instances', page: 'instance', title: 'Instance', keys: ['id'] },
  ];
  const templates = new Map();
  let mounted = null;
  let token = 0;

  const parse = () => {
    const raw = location.hash.replace(/^#/, '');
    const [path, query = ''] = raw.split('?');
    return { path: path || '/', query: Object.fromEntries(new URLSearchParams(query)) };
  };

  async function template(page) {
    if (!templates.has(page)) {
      const res = await fetch(`pages/${page}.html`, { credentials: 'same-origin' });
      if (!res.ok) throw new Error(`Could not load page '${page}' (${res.status}).`);
      templates.set(page, await res.text());
    }
    return templates.get(page);
  }

  Alpine.store('router', {
    section: 'overview',
    params: {},
    query: {},

    async start() {
      window.addEventListener('hashchange', () => this.resolve());
      await this.resolve();
    },

    async resolve() {
      const mine = ++token;
      const { path, query } = parse();
      let route = null, match = null;
      for (const r of routes) { match = r.pattern.exec(path); if (match) { route = r; break; } }

      const view = document.getElementById('view');
      // Removing the old page lets Alpine's MutationObserver run its destroy() hooks (timers etc.).
      if (mounted) { mounted.remove(); mounted = null; }

      if (!route) {
        view.innerHTML = '<div class="empty"><h2>Page not found</h2><p class="muted">That page does not exist.</p><a class="btn" href="#/">Back to overview</a></div>';
        this.section = '';
        return;
      }

      const params = { ...(route.params || {}) };
      (route.keys || []).forEach((k, i) => { params[k] = decodeURIComponent(match[i + 1]); });
      this.section = route.section;
      this.params = params;
      this.query = query;
      document.title = `${route.title} · ${window.__WORKFLOW__.title}`;

      let html;
      try { html = await template(route.page); }
      catch (e) { view.innerHTML = `<div class="empty"><h2>Something went wrong</h2><p class="muted">${String(e.message).replace(/</g, '&lt;')}</p></div>`; return; }
      if (mine !== token) return; // a newer navigation superseded this one

      const wrapper = document.createElement('div');
      wrapper.className = 'page';
      wrapper.innerHTML = html;
      view.replaceChildren(wrapper);
      mounted = wrapper; // Alpine's MutationObserver initializes the x-data inside
      window.scrollTo(0, 0);
    },

    go(path, query) {
      location.hash = '#' + path + api.qs(query || {});
    },

    // Updates the URL query (for deep-linkable filters) without re-rendering the page.
    replaceQuery(query) {
      const { path } = parse();
      this.query = { ...query };
      history.replaceState(null, '', '#' + path + api.qs(query));
    },
  });
});
