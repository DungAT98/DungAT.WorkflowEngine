// Thin fetch wrapper for the dashboard JSON API.
(function () {
  const cfg = window.__WORKFLOW__;

  class ApiError extends Error {
    constructor(status, message, errors) {
      super(message);
      this.status = status;
      this.errors = errors || [];
    }
  }

  async function request(method, path, body) {
    const init = { method, headers: { Accept: 'application/json' }, credentials: 'same-origin' };
    // The custom header makes cross-site form posts impossible (CSRF protection on the server).
    if (method !== 'GET') init.headers['X-Workflow-Dashboard'] = '1';
    if (body !== undefined) {
      init.headers['Content-Type'] = 'application/json';
      init.body = JSON.stringify(body);
    }

    let res;
    try {
      res = await fetch(cfg.apiBase + path, init);
    } catch {
      throw new ApiError(0, 'Could not reach the server. Check your connection and try again.');
    }

    let data = null;
    const text = await res.text();
    if (text) { try { data = JSON.parse(text); } catch { /* non-JSON body */ } }

    if (!res.ok) {
      const message = data?.message || data?.errors?.[0] ||
        (res.status === 401 ? 'You are not authorized to use this dashboard.' : res.statusText) || 'Request failed.';
      throw new ApiError(res.status, message, data?.errors);
    }
    return data;
  }

  const qs = (params) => {
    const p = new URLSearchParams();
    Object.entries(params).forEach(([k, v]) => { if (v !== undefined && v !== null && v !== '') p.set(k, v); });
    const s = p.toString();
    return s ? '?' + s : '';
  };

  window.api = {
    ApiError,
    qs,
    get: (path) => request('GET', path),
    post: (path, body) => request('POST', path, body ?? {}),
  };
})();
