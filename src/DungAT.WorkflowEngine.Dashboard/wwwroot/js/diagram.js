// Renders a workflow definition as an SVG state diagram (left-to-right layered layout).
// Colors come from CSS classes in app.css so the diagram follows the light/dark theme.
(function () {
  const NODE_W = 156, NODE_H = 48, MIN_GAP_X = 110, GAP_Y = 38, PAD = 28;
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const clip = (s, n) => (String(s).length > n ? String(s).slice(0, n - 1) + '…' : String(s));
  const key = (s) => String(s).toUpperCase();

  function layout(def) {
    const states = def.states || [];
    const index = new Map(states.map((s, i) => [key(s.code), i]));
    const edgeMap = new Map();
    for (const t of def.transitions || []) {
      const f = index.get(key(t.fromStateCode)), to = index.get(key(t.toStateCode));
      if (f === undefined || to === undefined) continue;
      const k = f + '>' + to;
      if (!edgeMap.has(k)) edgeMap.set(k, { from: f, to, labels: [] });
      const cmd = (def.commands || []).find((c) => key(c.code) === key(t.commandCode));
      edgeMap.get(k).labels.push(cmd?.name || t.commandCode);
    }
    const edges = [...edgeMap.values()];

    // Columns are spaced so the widest edge label fits between them without touching the nodes.
    const labelWidth = (text) => Math.min(text.length, 28) * 6.4 + 16;
    const gapX = Math.max(MIN_GAP_X, ...edges.filter((e) => e.from !== e.to).map((e) => labelWidth(e.labels.join(' · ')) + 56));

    // Layer = shortest distance from the initial state; unreachable states go to the last layer.
    const start = Math.max(0, states.findIndex((s) => s.type === 'Initial'));
    const depth = new Array(states.length).fill(-1);
    if (states.length) {
      depth[start] = 0;
      const queue = [start];
      while (queue.length) {
        const n = queue.shift();
        for (const e of edges) if (e.from === n && e.to !== n && depth[e.to] < 0) { depth[e.to] = depth[n] + 1; queue.push(e.to); }
      }
    }
    const maxDepth = Math.max(0, ...depth);
    depth.forEach((d, i) => { if (d < 0) depth[i] = maxDepth + 1; });

    const layers = [];
    depth.forEach((d, i) => (layers[d] = layers[d] || []).push(i));
    const rank = new Array(states.length).fill(0);
    layers.forEach((layer) => layer && layer.forEach((n, i) => (rank[n] = i)));
    // One barycenter sweep keeps edges from crossing needlessly.
    for (let d = 1; d < layers.length; d++) {
      const layer = layers[d]; if (!layer) continue;
      const bary = (n) => {
        const preds = edges.filter((e) => e.to === n && depth[e.from] < d).map((e) => rank[e.from]);
        return preds.length ? preds.reduce((a, b) => a + b, 0) / preds.length : rank[n];
      };
      layer.sort((a, b) => bary(a) - bary(b));
      layer.forEach((n, i) => (rank[n] = i));
    }

    const tallest = Math.max(1, ...layers.map((l) => (l ? l.length : 0)));
    const colHeight = tallest * (NODE_H + GAP_Y) - GAP_Y;
    const hasLoop = edges.some((e) => e.from === e.to);
    const top = PAD + (hasLoop ? 34 : 0);
    const pos = states.map((_, i) => {
      const layer = layers[depth[i]];
      const offset = (colHeight - (layer.length * (NODE_H + GAP_Y) - GAP_Y)) / 2;
      return { x: PAD + 22 + depth[i] * (NODE_W + gapX), y: top + offset + rank[i] * (NODE_H + GAP_Y) };
    });
    return { states, edges, depth, pos, colHeight, top };
  }

  function bezierMid(p0, p1, p2, p3) {
    const f = (a, b, c, d) => (a + 3 * b + 3 * c + d) / 8;
    return { x: f(p0.x, p1.x, p2.x, p3.x), y: f(p0.y, p1.y, p2.y, p3.y) };
  }

  function label(text, x, y, cls) {
    const shown = clip(text, 28);
    const w = shown.length * 6.4 + 16;
    return `<g class="dg-label ${cls}"><title>${esc(text)}</title><rect x="${x - w / 2}" y="${y - 10}" width="${w}" height="20" rx="10"/><text x="${x}" y="${y + 4}" text-anchor="middle">${esc(shown)}</text></g>`;
  }

  function render(def, opts = {}) {
    if (!def || !(def.states || []).length) return '<div class="muted dg-empty">Add a state to see the diagram.</div>';
    const { states, edges, depth, pos, colHeight, top } = layout(def);
    const current = opts.current ? key(opts.current) : null;
    const currentIdx = current ? states.findIndex((s) => key(s.code) === current) : -1;

    const maxX = Math.max(...pos.map((p) => p.x)) + NODE_W + PAD;
    const baseY = top + colHeight + 14;
    let backCount = 0, backBottom = baseY;
    let paths = '', labels = '';

    for (const e of edges) {
      const a = pos[e.from], b = pos[e.to];
      const active = e.from === currentIdx ? 'out' : '';
      const marker = active ? 'dg-arrow-out' : 'dg-arrow';
      const text = e.labels.join(' · ');
      if (e.from === e.to) {
        const cx = a.x + NODE_W / 2;
        paths += `<path class="dg-edge ${active}" marker-end="url(#${marker})" d="M${cx - 16},${a.y} C${cx - 34},${a.y - 38} ${cx + 34},${a.y - 38} ${cx + 16},${a.y}"/>`;
        labels += label(text, cx, a.y - 34, active);
      } else if (depth[e.to] > depth[e.from]) {
        const p0 = { x: a.x + NODE_W, y: a.y + NODE_H / 2 }, p3 = { x: b.x, y: b.y + NODE_H / 2 };
        const dx = (p3.x - p0.x) * 0.5;
        const p1 = { x: p0.x + dx, y: p0.y }, p2 = { x: p3.x - dx, y: p3.y };
        paths += `<path class="dg-edge ${active}" marker-end="url(#${marker})" d="M${p0.x},${p0.y} C${p1.x},${p1.y} ${p2.x},${p2.y} ${p3.x},${p3.y}"/>`;
        const m = bezierMid(p0, p1, p2, p3);
        labels += label(text, m.x, m.y, active);
      } else {
        // Backward or same-layer edge: loop underneath the nodes so it never crosses them.
        const p0 = { x: a.x + NODE_W / 2, y: a.y + NODE_H }, p3 = { x: b.x + NODE_W / 2, y: b.y + NODE_H };
        const drop = 46 + Math.min(Math.abs(p3.x - p0.x) / 5, 70) + backCount * 22;
        const lowest = Math.max(p0.y, p3.y);
        const c1 = { x: p0.x, y: lowest + drop }, c2 = { x: p3.x, y: lowest + drop };
        paths += `<path class="dg-edge ${active}" marker-end="url(#${marker})" d="M${p0.x},${p0.y} C${c1.x},${c1.y} ${c2.x},${c2.y} ${p3.x},${p3.y}"/>`;
        const m = bezierMid(p0, c1, c2, p3);
        labels += label(text, m.x, m.y, active);
        backBottom = Math.max(backBottom, m.y + 20);
        backCount++;
      }
    }

    let nodes = '';
    states.forEach((s, i) => {
      const p = pos[i];
      const cls = ['dg-node', 't-' + String(s.type || 'Intermediate').toLowerCase(), i === currentIdx ? 'current' : ''].join(' ');
      const name = clip(s.name || s.code, 20);
      const showCode = s.name && key(s.name) !== key(s.code);
      nodes += `<g class="${cls}" transform="translate(${p.x},${p.y})"><title>${esc(s.name || s.code)} (${esc(s.code)}) · ${esc(s.type || 'Intermediate')}</title>` +
        `<rect class="dg-box" width="${NODE_W}" height="${NODE_H}" rx="12"/>` +
        (s.type === 'Final' ? `<rect class="dg-inner" x="4" y="4" width="${NODE_W - 8}" height="${NODE_H - 8}" rx="9"/>` : '') +
        `<rect class="dg-bar" x="0" y="10" width="4" height="${NODE_H - 20}" rx="2"/>` +
        `<text class="dg-name" x="${NODE_W / 2}" y="${showCode ? 21 : 29}" text-anchor="middle">${esc(name)}</text>` +
        (showCode ? `<text class="dg-code" x="${NODE_W / 2}" y="37" text-anchor="middle">${esc(clip(s.code, 24))}</text>` : '') +
        '</g>';
      if (s.type === 'Initial') {
        paths += `<circle class="dg-start" cx="${p.x - 22}" cy="${p.y + NODE_H / 2}" r="4.5"/>` +
          `<path class="dg-edge" marker-end="url(#dg-arrow)" d="M${p.x - 17},${p.y + NODE_H / 2} L${p.x - 2},${p.y + NODE_H / 2}"/>`;
      }
    });

    const width = Math.max(maxX, 320), height = Math.max(backBottom + PAD, top + colHeight + PAD);
    // Scales down to fit its container (never above natural size); very wide graphs keep a readable minimum and scroll.
    return `<svg class="dg" viewBox="0 0 ${width} ${height}" style="width:100%;max-width:${width}px;min-width:${Math.min(width, 440)}px;height:auto" role="img" aria-label="Workflow diagram" xmlns="http://www.w3.org/2000/svg">` +
      '<defs>' +
      '<marker id="dg-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto"><path class="dg-head" d="M0,1 L10,5 L0,9 z"/></marker>' +
      '<marker id="dg-arrow-out" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto"><path class="dg-head out" d="M0,1 L10,5 L0,9 z"/></marker>' +
      `</defs>${paths}${nodes}${labels}</svg>`;
  }

  window.Diagram = { render };
})();
