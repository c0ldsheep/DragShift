// The DragShift ring, drawn the way the app draws it: a white disc, pale petals with round 9-point corners and
// 3-point gaps (outer 148, inner 64, hub 56), bold labels, the hub with the file, the page tabs and the hint
// underneath. The lit slice takes the orange-to-pink gradient. Sizes are in desktop points.

const NS = "http://www.w3.org/2000/svg";
const OUTER = 148;
const INNER = 64;
const HUB = 56;
const DISC = 158;

export const ICONS = {
  photo: '<rect x="-9" y="-7" width="18" height="14" rx="2.5"/><circle cx="-3" cy="-2" r="2"/><path d="M-8 6l6-6 4 4 3-3 4 5"/>',
  pdf: '<path d="M-6-9h8l5 5v13h-13z"/><path d="M2-9v5h5"/><path d="M-3 1h6M-3 4h6"/>',
  scale: '<path d="M-8 7h16l-3-11h-10z"/><circle cx="0" cy="-7" r="2.6"/>',
  person: '<rect x="-8" y="-9" width="16" height="18" rx="2.5"/><circle cx="0" cy="-2" r="3.2"/><path d="M-5 7c1-3 9-3 10 0"/>',
  cutout: '<rect x="-9" y="-8" width="18" height="16" rx="2.5" stroke-dasharray="3 2.4"/><circle cx="0" cy="-2" r="3"/><path d="M-5 7c1-3 9-3 10 0"/>',
  shrink: '<path d="M-8-8l5 5M-3-7v4h-4M8 8l-5-5M3 7v-4h4"/>',
  zip: '<rect x="-6" y="-9" width="12" height="18" rx="2"/><path d="M0-9v3M0-4v2M0 0v2"/><rect x="-2" y="3" width="4" height="4" rx="1"/>',
  airdrop: '<path d="M-7 3a9 9 0 1 1 14 0M-4 1a5 5 0 1 1 8 0"/><path d="M0 2l-5 8h10z"/>',
  split: '<rect x="-9" y="-7" width="5" height="14" rx="1.2"/><rect x="-2.5" y="-7" width="5" height="14" rx="1.2"/><rect x="4" y="-7" width="5" height="14" rx="1.2"/>',
  stack: '<rect x="-7" y="-4" width="14" height="13" rx="2"/><path d="M-5-7h10M-3-10h6"/>',
  lock: '<rect x="-7" y="-2" width="14" height="11" rx="2"/><path d="M-4-2v-3a4 4 0 0 1 8 0v3"/>',
  text: '<path d="M-8-7h16M-8-2h16M-8 3h11M-8 8h7"/>',
  printer: '<path d="M-6-4v-5h12v5"/><rect x="-10" y="-4" width="20" height="10" rx="2.5"/><rect x="-6" y="2" width="12" height="7" rx="1"/>',
  camera: '<rect x="-9" y="-5" width="18" height="13" rx="2.5"/><path d="M-4-5l2-3h4l2 3"/><circle cx="0" cy="1.5" r="3.2"/>',
  globe: '<circle r="8.5"/><path d="M-8.5 0h17M0-8.5c-4 4-4 13 0 17M0-8.5c4 4 4 13 0 17"/>',
  sparkle: '<path d="M0-9l2 6 6 2-6 2-2 6-2-6-6-2 6-2z"/>',
  scissors: '<circle cx="-5" cy="6" r="2.6"/><circle cx="5" cy="6" r="2.6"/><path d="M-3.5 4l9-13M3.5 4l-9-13"/>',
  portrait: '<rect x="-5.5" y="-9" width="11" height="18" rx="2"/>',
  square: '<rect x="-8" y="-8" width="16" height="16" rx="2"/>',
  forward: '<path d="M-9-6l8 6-8 6zM0-6l8 6-8 6z"/>',
  mute: '<path d="M-9-3h4l5-4v14l-5-4h-4z"/><path d="M3-3l5 6M8-3l-5 6"/>',
  music: '<path d="M-3 6v-13l9-2v12"/><circle cx="-5.5" cy="6" r="2.5"/><circle cx="3.5" cy="4" r="2.5"/>',
  film: '<rect x="-9" y="-7" width="18" height="14" rx="2.5"/><path d="M-2-3l5 3-5 3z"/>',
  rotate: '<path d="M7 -2a7 7 0 1 1-2.5-5.4"/><path d="M5-11v4h4"/>',
  unlock: '<rect x="-7" y="-1" width="14" height="10" rx="2"/><path d="M-4-1v-4a4 4 0 0 1 7.6-1.8"/>',
  more: '<circle cx="0" cy="0" r="9"/><circle cx="-4" cy="0" r="0.9" class="dot"/><circle cx="0" cy="0" r="0.9" class="dot"/><circle cx="4" cy="0" r="0.9" class="dot"/>',
};

const el = (name, attrs = {}) => {
  const n = document.createElementNS(NS, name);
  for (const [k, v] of Object.entries(attrs)) n.setAttribute(k, v);
  return n;
};
const rad = (a) => (a * Math.PI) / 180;
const pt = (r, a) => [r * Math.sin(rad(a)), -r * Math.cos(rad(a))];

/** The app's petal: the slice inset by the corner radius, stroked round with twice that width. */
export function petalPath(start, end, inner = INNER, outer = OUTER, gap = 3, corner = 9) {
  const ro = outer - corner;
  const ri = inner + corner;
  const off = gap / 2 + corner;
  const ins = (r) => (Math.asin(Math.min(1, off / r)) * 180) / Math.PI;
  const a1o = start + ins(ro);
  const a2o = end - ins(ro);
  const a1i = start + ins(ri);
  const a2i = end - ins(ri);
  const [x1, y1] = pt(ro, a1o);
  const [x2, y2] = pt(ro, a2o);
  const [x3, y3] = pt(ri, a2i);
  const [x4, y4] = pt(ri, a1i);
  const large = a2o - a1o > 180 ? 1 : 0;
  return `M${x1.toFixed(2)} ${y1.toFixed(2)}A${ro} ${ro} 0 ${large} 1 ${x2.toFixed(2)} ${y2.toFixed(2)}L${x3.toFixed(2)} ${y3.toFixed(2)}A${ri} ${ri} 0 ${large} 0 ${x4.toFixed(2)} ${y4.toFixed(2)}Z`;
}

function pageGroup(slices) {
  const g = el("g", { class: "page" });
  const span = 360 / slices.length;
  slices.forEach(([title, icon], i) => {
    const p = el("g", { class: "petal" });
    const d = petalPath(i * span - span / 2, i * span + span / 2);
    const [x, y] = pt((INNER + OUTER) / 2 + 2, i * span);
    const small = title.length > 9 ? " small" : "";
    p.innerHTML = `<path class="body" d="${d}"/><path class="hot" d="${d}"/>
      <g transform="translate(${x.toFixed(1)} ${y.toFixed(1)})">
        <g class="icon" transform="translate(0 -9)">${ICONS[icon] || ""}</g>
        <text class="name${small}" y="14">${title.toUpperCase()}</text>
      </g>`;
    g.append(p);
  });
  return g;
}

/**
 * host: an element placed at the ring's centre. pages: { name: slices }. tabs: tab names.
 * Returns update(state) where state = { open, page, page2, mix, hot, tab, title, sub, thumb, count }.
 */
export function createRing(host, pages, tabs) {
  const svg = el("svg", { viewBox: "-180 -180 360 360", class: "ring-svg", "aria-hidden": "true" });
  svg.innerHTML = `<defs><linearGradient id="hot" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ff8c38"/><stop offset="1" stop-color="#ff3d6e"/></linearGradient></defs>
    <circle class="disc" r="${DISC}"/>`;
  const groups = {};
  for (const [name, slices] of Object.entries(pages)) {
    groups[name] = pageGroup(slices);
    svg.append(groups[name]);
  }
  const hub = el("g", { class: "hub" });
  hub.innerHTML = `<circle r="${HUB}" class="hub-ring"/><circle r="${HUB - 3}" class="hub-face"/>
    <image class="hub-thumb" x="-17" y="-34" width="34" height="25" preserveAspectRatio="xMidYMid slice"/>
    <text class="hub-title" y="7"></text><text class="hub-sub" y="22"></text>`;
  svg.append(hub);
  const bar = document.createElement("div");
  bar.className = "ring-tabs";
  bar.innerHTML = tabs.map((t) => `<span>${t === "Faves" ? "★ Faves" : t}</span>`).join("");
  const hint = document.createElement("div");
  hint.className = "ring-hint";
  hint.textContent = "Let go on a slice, or in the middle to keep it open";
  host.append(svg, bar, hint);
  const tabEls = [...bar.children];
  const title = hub.querySelector(".hub-title");
  const sub = hub.querySelector(".hub-sub");
  const img = hub.querySelector(".hub-thumb");
  let last = "";

  return function update(s) {
    host.style.opacity = s.open.toFixed(3);
    host.style.transform = `translate(-50%, -50%) scale(${((s.k || 1) * (0.82 + 0.18 * s.open)).toFixed(4)})`;
    host.style.visibility = s.open > 0.001 ? "visible" : "hidden";
    for (const [name, g] of Object.entries(groups)) {
      const a = name === s.page ? 1 - (s.mix || 0) : name === s.page2 ? s.mix || 0 : 0;
      g.style.opacity = a.toFixed(3);
      g.style.display = a > 0.001 ? "" : "none";
    }
    const key = [s.page, s.page2, s.mix > 0.5, s.hot, s.tab, s.title, s.sub, s.thumb].join("|");
    if (key === last) return;
    last = key;
    const shown = s.mix > 0.5 ? groups[s.page2] : groups[s.page];
    for (const g of Object.values(groups)) [...g.children].forEach((p, i) => p.classList.toggle("on", g === shown && i === s.hot));
    tabEls.forEach((t, i) => t.classList.toggle("on", i === s.tab));
    title.textContent = s.title;
    sub.textContent = s.sub;
    if (s.thumb) img.setAttribute("href", s.thumb);
    img.style.display = s.thumb ? "" : "none";
  };
}
