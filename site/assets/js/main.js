// The page. The film moves only while you scroll: the scroll position is a film time (film.js timeAt, with heavier
// stretches and short holds), and the picture glides there. Shift or the arrow keys jump to the next part.
// Every frame the 3D scene draws itself and says where the ring, the pointer and the things are, and the words
// layer (clock, heading, the size readout, the ring) is placed on top.
// Test hooks, only with ?rec or ?test: ?t=12.3 holds one moment; globalThis.__rec(t, clock) sets the time;
// __gpu(n) measures GPU time per frame; ?scale= multiplies the pixel ratio (0.5 to 2); ?nogl shows the still story.

import * as F from "./film.js";
import { createRing } from "./ring.js";
import { initHaptics, feel } from "./haptics.js";

// every import loaded and linked: from here on a failure is the 3D's, not a missing file (boot.js)
globalThis.__mainRan = true;

const params = new URLSearchParams(location.search);
const TESTING = params.has("rec") || params.has("test");
const RECORDING = params.has("rec");
const num = (k, lo, hi) => { const v = Number(params.get(k)); return TESTING && params.has(k) && Number.isFinite(v) ? Math.min(hi, Math.max(lo, v)) : null; };
const FIXED = num("t", 0, F.DURATION);
const STALL = num("stall", 0, 30); // ?test&stall=10: the 3D starts loading 10 s late, like a very slow phone (webkit-check)
const SCALE = num("scale", 0.5, 2) ?? 1;
const REC = { t: null, clock: 0 };
if (TESTING) {
  globalThis.__rec = (t, clock) => { REC.t = t; REC.clock = clock ?? t; };
  globalThis.__stops = F.STOPS;
}
const load = (p, label) => globalThis.__loadStep?.(p, label);
// resolves after the next frame is painted (inside requestAnimationFrame would be before it: the loader would freeze)
const nextFrame = () => new Promise((r) => requestAnimationFrame(() => setTimeout(r, 0)));
load(0.14, "Setting the desk");
const MOTION = !matchMedia("(prefers-reduced-motion: reduce)").matches;
if (params.has("still") || params.has("og")) document.documentElement.classList.add("shot", params.has("og") ? "og" : "still");
const root = document.documentElement;
const $ = (s) => document.querySelector(s);

const words = $(".words");
const hEl = $("[data-h]");
const pEl = $("[data-p]");
const getEl = $("[data-get]");
const keyEl = $("[data-key]");
const keyGlyph = $("[data-key-glyph]");
const keyName = $("[data-key-name]");
const pwEl = $("[data-pw]");
const pwDots = $("[data-pw-dots]");
const pwGo = $("[data-pw-go]");
const ptrEl = $("[data-pointer]");
const clockEl = $("[data-clock]");
const timeEl = $("[data-time]");
const noteEl = $("[data-note]");
const meterEl = $("[data-meter]");
const sizeEl = $("[data-size]");
const limitEl = $("[data-limit]");
const miEl = $("[data-mi]");
// The meter's icons, drawn on a 24-point grid with one fine line.
const ICON = {
  file: '<svg viewBox="0 0 24 24"><path d="M6.5 3h7.5l4.5 4.5V20a1 1 0 0 1-1 1h-11a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z"/><path d="M14 3v4.5h4.5"/><path d="M12 10.5v6.5M9.3 14.4 12 17l2.7-2.6"/></svg>',
  lock: '<svg viewBox="0 0 24 24"><path class="shackle" d="M8.5 11V8a3.5 3.5 0 0 1 7 0v3"/><rect x="5.5" y="11" width="13" height="10" rx="2.6"/><circle class="fill" cx="12" cy="15.3" r="1.4"/><path d="M12 16.2v2"/></svg>',
  pages: '<svg viewBox="0 0 24 24"><path d="M8 6.5V4.8a.8.8 0 0 1 .8-.8h9.4a.8.8 0 0 1 .8.8v12.4a.8.8 0 0 1-.8.8H16.5"/><rect x="5" y="7" width="11.5" height="14" rx="1"/><path d="M8 11.5h5.5M8 14.5h5.5M8 17.5h3.5"/></svg>',
};
ICON.unlock = ICON.lock;
const ringHost = $("[data-ring]");
const updateRing = createRing(ringHost, F.PAGES, F.TABS);
const track = $("[data-track]");
const hintEl = $("[data-hint]");
const cropEl = $("[data-crop]");
const crop = { dim: $("[data-crop-dim]"), flash: $("[data-crop-flash]"), grid: $("[data-crop-grid]"), frame: $("[data-crop-frame]"), handles: $("[data-crop-handles]") };

const canvas = $("[data-scene]");
const V = { W: 1, H: 1, dpr: 1, phone: false, top: 0, bottomPad: 0, clock: 0, live: !RECORDING && FIXED === null, calm: !MOTION };
let scene = null;
try {
  if (params.has("nogl")) throw new Error("3D switched off for a test (?nogl)");
  if (STALL) await new Promise((r) => setTimeout(r, STALL * 1000));
  const { createScene } = await import("./gl/scene.js");
  load(0.24, "Developing the photo");
  scene = await createScene(canvas, {
    keep: RECORDING || FIXED !== null, look: TESTING && params.get("look") === "cel" ? "cel" : "studio",
    msaa: TESTING && params.has("msaa") ? num("msaa", 0, 4) : null, progress: load,
    paintHere: TESTING && params.has("painthere"), // ?test&painthere: paint the papers on the page, to compare
  });
} catch (e) {
  globalThis.__errs?.push(String(e && e.message ? e.message : e));
  globalThis.__showStory?.("no-3d");
}


function layout() {
  // room for the tallest words of any stop, so the scene never jumps between stops
  // (the end buttons only count for the last stop: the camera makes room for them as they come in)
  const keep = [hEl.textContent, pEl.textContent];
  let tallest = 0, tallestEnd = 0;
  getEl.style.display = "none";
  for (const [h, p] of F.WORDS) {
    hEl.textContent = h;
    pEl.textContent = p;
    tallest = Math.max(tallest, words.offsetHeight);
  }
  getEl.style.display = "";
  tallestEnd = words.offsetHeight;
  [hEl.textContent, pEl.textContent] = keep;
  const stage = $(".stage");
  V.W = stage.clientWidth;
  V.H = stage.clientHeight;
  V.phone = V.W < 760;
  V.dpr = Math.min(V.phone ? 3 : 2, devicePixelRatio || 1) * SCALE;
  V.top = words.offsetTop + tallest + (V.phone ? 10 : 18);
  V.topEnd = words.offsetTop + tallestEnd + (V.phone ? 14 : 24);
  V.bottomPad = (V.phone ? 64 : 78) + (stage.clientHeight - innerHeight > 0 ? stage.clientHeight - innerHeight : 0);
  // the scroll unit is the stage's (large-viewport) height, so a phone's sliding toolbar never shifts the film
  V.unit = stage.clientHeight;
  track.style.height = `${Math.round((F.SCROLL_TOTAL + 1) * V.unit)}px`;
  canvas.width = Math.round(V.W * V.dpr);
  canvas.height = Math.round(V.H * V.dpr);
  scene?.resize(V);
}
let lastW = 0, lastH = 0;
addEventListener("resize", () => {
  const s = $(".stage");
  // phones resize the window as the toolbar slides; only real changes rebuild the buffers
  if (Math.abs(s.clientWidth - lastW) < 2 && Math.abs(s.clientHeight - lastH) < 2) return;
  lastW = s.clientWidth; lastH = s.clientHeight;
  layout();
  draw(T);
});

const mouse = { x: -1, y: -1 };
addEventListener("pointermove", (e) => { if (e.pointerType === "mouse") { mouse.x = e.clientX; mouse.y = e.clientY; } });
V.mouse = mouse;

// ---- the size readout: sits next to its thing and keeps its side until it has to move
const meter = { of: "", side: -1, x: 0, y: 0, w: 0, h: 0, text: "" };
function placeMeter(r) {
  const gap = V.phone ? 10 : 16;
  const { w, h } = meter;
  // on a phone it has one fixed place, over the Shift key, so it never sits on the form's own text
  if (V.phone) return [(V.W - w) / 2, V.H - V.bottomPad - h - 2];
  const cx = (r.x0 + r.x1) / 2, cy = (r.y0 + r.y1) / 2;
  const sides = [[cx - w / 2, r.y1 + gap], [r.x1 + gap, cy - h / 2], [r.x0 - gap - w, cy - h / 2], [cx - w / 2, r.y0 - gap - h]];
  const fits = ([x, y]) => x >= 12 && x + w <= V.W - 12 && y >= V.top - 4 && y + h <= V.H - V.bottomPad + 20;
  if (meter.side < 0 || !fits(sides[meter.side])) {
    const i = sides.findIndex(fits);
    meter.side = i < 0 ? 0 : i;
  }
  let [x, y] = sides[meter.side];
  x = Math.min(V.W - w - 12, Math.max(12, x));
  y = Math.min(V.H - V.bottomPad + 20 - h, Math.max(V.top - 4, y));
  return [x, y];
}

let shownWords = null;
let shownTime = "";
function draw(T) {
  const f = F.frame(T);
  if (f.words !== shownWords) {
    shownWords = f.words;
    hEl.textContent = f.words[0];
    pEl.textContent = f.words[1];
    root.dataset.stop = String(f.shown);
  }
  for (const e of [hEl, pEl]) {
    e.style.opacity = f.wordsA.toFixed(3);
    e.style.transform = `translateY(${((1 - f.wordsA) * 10).toFixed(1)}px)`;
  }
  // the seal's knock shakes the words too
  const kx = V.calm ? 0 : 3.2 * F.knock(T - F.E.hit, 7, 6), ky = V.calm ? 0 : 2.4 * F.knock(T - F.E.hit - 0.02, 9, 6);
  words.style.transform = `translate(calc(-50% + ${kx.toFixed(2)}px), ${ky.toFixed(2)}px)`;
  getEl.style.opacity = f.endA.toFixed(3);
  getEl.style.visibility = f.endA > 0.01 ? "visible" : "hidden";
  getEl.inert = f.endA < 0.5;

  hintEl.classList.toggle("gone", T > 0.02 || scrollY > 8);
  const c = F.clockAt(T);
  if (c.time !== shownTime) {
    shownTime = c.time;
    timeEl.textContent = c.time;
    noteEl.textContent = c.note;
    clockEl.classList.toggle("done", c.done);
  }
  const flip = Math.min(1, c.since / 0.35);
  timeEl.style.transform = `translateY(${((1 - flip) * -8).toFixed(1)}px)`;
  timeEl.style.opacity = (0.25 + 0.75 * flip).toFixed(3);

  if (!scene) return;
  const o = scene.draw(T, V) || {};

  const m = F.meterAt(T);
  const r = o.rects[m.of];
  if (m.a > 0.01 && r) {
    if (meterEl.dataset.icon !== m.icon) {
      if (ICON[m.icon] !== ICON[meterEl.dataset.icon]) miEl.innerHTML = ICON[m.icon];
      meterEl.dataset.icon = m.icon;
    }
    if (meter.text !== m.text) {
      meter.text = m.text;
      sizeEl.textContent = m.text;
      limitEl.textContent = m.limit;
      meterEl.classList.toggle("good", m.good);
      meter.w = meterEl.offsetWidth;
      meter.h = meterEl.offsetHeight;
    }
    if (meter.of !== m.of) { meter.of = m.of; meter.side = -1; }
    const [x, y] = placeMeter(r);
    meterEl.style.opacity = m.a.toFixed(3);
    meterEl.style.visibility = "visible";
    meterEl.style.transform = `translate(${x.toFixed(1)}px, ${y.toFixed(1)}px)`;
  } else {
    meterEl.style.opacity = "0";
    meterEl.style.visibility = "hidden";
  }

  const R = o.ring;
  if (R && R.open > 0.001) {
    ringHost.style.left = `${R.x}px`;
    ringHost.style.top = `${R.y}px`;
    updateRing(R);
  } else updateRing({ open: 0, page: "faves", hot: -1, tab: 0, title: "", sub: "" });
  drawCrop(o.crop);
  const Pt = o.pointer;
  ptrEl.style.visibility = Pt && Pt.a > 0.01 ? "visible" : "hidden";
  if (Pt) {
    ptrEl.style.opacity = String(Pt.a);
    ptrEl.style.transform = `translate(${Pt.x.toFixed(1)}px, ${Pt.y.toFixed(1)}px)`;
  }
  keyEl.style.opacity = (o.keyA || 0).toFixed(3);
  keyEl.classList.toggle("down", !!o.shift);
  if (keyEl.dataset.k !== o.key) {
    keyEl.dataset.k = o.key;
    keyGlyph.textContent = o.key === "return" ? "⏎" : "⇧";
    keyName.textContent = o.key === "return" ? "return" : "shift";
  }

  const pw = o.pw;
  if (pw) {
    pwEl.style.visibility = "visible";
    pwEl.style.opacity = pw.a.toFixed(3);
    pwEl.style.transform = `translate(${pw.x.toFixed(1)}px, ${pw.y.toFixed(1)}px) translate(-50%, -50%) scale(${(0.94 + 0.06 * pw.a).toFixed(3)})`;
    const dots = "•".repeat(pw.dots);
    if (pwDots.textContent !== dots) pwDots.textContent = dots;
    pwGo.classList.toggle("on", pw.press);
  } else if (pwEl.style.visibility !== "hidden") {
    pwEl.style.visibility = "hidden";
    pwEl.style.opacity = "0";
  }
}

/** The crop frame over the print (its corners come from the 3D, so it follows the print's perspective). */
function drawCrop(c) {
  if (!c || c.a < 0.01) {
    if (cropEl.style.visibility !== "hidden") cropEl.style.visibility = "hidden";
    return;
  }
  const P = (p) => `${p[0].toFixed(1)} ${p[1].toFixed(1)}`;
  const quad = (q) => `M${q.map(P).join("L")}Z`;
  const mix = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t];
  const r = c.rect;
  cropEl.style.visibility = "visible";
  cropEl.style.opacity = c.a.toFixed(3);
  crop.dim.setAttribute("d", quad(c.print) + quad(r));
  crop.dim.style.opacity = String(c.dim);
  crop.frame.setAttribute("d", quad(r));
  crop.flash.setAttribute("d", quad(r));
  crop.flash.style.opacity = (c.flash * 0.55).toFixed(3);
  let g = "";
  for (const t of [1 / 3, 2 / 3]) {
    g += `M${P(mix(r[0], r[1], t))}L${P(mix(r[3], r[2], t))}`;
    g += `M${P(mix(r[0], r[3], t))}L${P(mix(r[1], r[2], t))}`;
  }
  crop.grid.setAttribute("d", g);
  // L-shaped handles, 18 px along each edge from every corner
  let hd = "";
  for (let i = 0; i < 4; i++) {
    const p = r[i], a = r[(i + 1) % 4], b = r[(i + 3) % 4];
    const along = (q) => { const dx = q[0] - p[0], dy = q[1] - p[1]; const l = Math.hypot(dx, dy) || 1; const k = Math.min(18, l / 3) / l; return [p[0] + dx * k, p[1] + dy * k]; };
    hd += `M${P(along(a))}L${P(p)}L${P(along(b))}`;
  }
  crop.handles.setAttribute("d", hd);
}

let T = FIXED ?? 0;
let last = performance.now();
const targetFor = () => F.timeAt(Math.max(0, scrollY) / Math.max(1, V.unit || innerHeight));
// If a phone really can't keep up, drop the edge smoothing once. Only measured while the film is moving, after the
// first two seconds (loading and warm-up are slow everywhere), and not when the phone simply caps the page at
// 30 fps (Low Power Mode): steady ~33 ms frames mean a cap, not a struggle.
const perf = { n: 0, sum: 0, sq: 0, done: false, since: performance.now() + 2000, lastT: -1 };
function watch(raw) {
  const moving = Math.abs(T - perf.lastT) > 1e-4;
  perf.lastT = T;
  if (perf.done || !V.live || document.hidden || raw > 0.25 || !moving || performance.now() < perf.since) return;
  perf.n++; perf.sum += raw; perf.sq += raw * raw;
  if (perf.n < 150) return;
  const mean = perf.sum / perf.n;
  const sd = Math.sqrt(Math.max(0, perf.sq / perf.n - mean * mean));
  const capped = mean > 0.03 && mean < 0.0365 && sd < 0.004;
  if (mean > 1 / 42 && !capped && scene?.lighter(V)) globalThis.__lighter = true;
  perf.done = true;
}
function tick(now) {
  const raw = (now - last) / 1000;
  const dt = Math.min(0.05, raw);
  last = now;
  watch(raw);
  const target = targetFor();
  if (REC.t !== null) { T = REC.t; V.clock = REC.clock; }
  else {
    V.clock = now / 1000;
    if (FIXED !== null) T = FIXED;
    else if (!MOTION) T = target;
    else {
      // glide to where the scroll is (a little weight, never a jump)
      T += (target - T) * (1 - Math.exp(-dt * 7));
      if (Math.abs(target - T) < 0.0005) T = target;
    }
  }
  // a phone feels the refusals and each landing (once each time the film plays forward over them; never on a jump,
  // and not in calm mode)
  if (V.live && !V.calm && T > prevT && T - prevT < 0.5) for (const f of F.FEELS) if (prevT < f.t && T >= f.t) {
    feel(f.kind);
    if (buzzNote) buzzNote.textContent = `${f.why}: ${f.kind} sent while scrolling (${new Date().toLocaleTimeString()})`;
  }
  prevT = T;
  draw(T);
  requestAnimationFrame(tick);
}
let prevT = 0;
let buzzNote = null; // ?test&buzz only: says which buzz was just sent

// Shift (or the arrow keys) scrolls on to the next part, or back to the one before.
addEventListener("keydown", (e) => {
  const next = e.key === "Shift" || e.key === "ArrowDown" || e.key === "ArrowRight";
  const prev = e.key === "ArrowUp" || e.key === "ArrowLeft";
  if ((!next && !prev) || e.repeat) return;
  e.preventDefault();
  const now = targetFor();
  const to = next ? F.STOPS.find((t) => t > now + 0.05) ?? F.DURATION : [...F.STOPS].reverse().find((t) => t < now - 0.05) ?? 0;
  scrollTo({ top: F.scrollAt(to) * V.unit, behavior: MOTION ? "smooth" : "instant" });
});

if (TESTING) {
  globalThis.__timeAt = F.timeAt;
  globalThis.__F = F;
  globalThis.__scrollTotal = F.SCROLL_TOTAL;
  globalThis.__gpu = (n = 30) => (scene ? scene.gpuTime(T, V, n) : -1);
  globalThis.__state = () => ({ T, V: { ...V, mouse: undefined } });
  // ?test&buzz: buttons that buzz right away (to check the phone can buzz from a web page at all, and how each one
  // feels), and a line that says which buzz the film just sent while scrolling. Visitors never see this.
  if (params.has("buzz")) {
    const box = document.createElement("div");
    box.style.cssText = "position:fixed;left:12px;right:12px;bottom:12px;z-index:99;display:flex;flex-wrap:wrap;gap:8px 10px;align-items:center;padding:10px;border-radius:14px;background:#24131c;color:#fff;font:600 14px/1.3 system-ui,sans-serif";
    for (const [kind, name] of [["thud", "Thud"], ["tick", "Tick"]]) {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.textContent = name;
      btn.style.cssText = "flex:none;min-height:44px;padding:0 16px;border:0;border-radius:999px;background:linear-gradient(120deg,#ff8c38,#ff3d6e);color:#fff;font:inherit";
      btn.addEventListener("click", () => { feel(kind); buzzNote.textContent = `${name} sent by the button (${new Date().toLocaleTimeString()})`; });
      box.append(btn);
    }
    buzzNote = document.createElement("span");
    buzzNote.style.cssText = "flex:1 1 200px";
    buzzNote.textContent = "vibrate" in navigator ? "This browser can vibrate. Tap a button once, then scroll the film." : "No vibration API here: using the iPhone switch (iOS 18 or newer).";
    box.append(buzzNote);
    document.body.append(box);
  }
}

// Phones can take the 3D away (switching apps, low memory). Then the page shows the still story, and reloads by
// itself if the browser gives the 3D back.
canvas.addEventListener("webglcontextlost", (e) => { e.preventDefault(); globalThis.__showStory?.("lost"); });
canvas.addEventListener("webglcontextrestored", () => location.reload());

initHaptics();
// the start hint: a keyboard means Shift works too
if (matchMedia("(pointer: fine)").matches && innerWidth >= 760) $("[data-hint-text]").textContent = "Scroll, or press ⇧ Shift";
// Download (in the bar): on to the download section, past the film
for (const a of document.querySelectorAll("[data-to-end]")) a.addEventListener("click", (e) => {
  e.preventDefault();
  $("#get-it").scrollIntoView({ behavior: MOTION ? "smooth" : "auto" });
});
// Downloads: the visitor's own computer first. On Windows the Windows button leads. On a phone or tablet (DragShift
// runs on computers) a "send this page to my computer" button leads: the share sheet, or the link copied.
{
  const platform = navigator.userAgentData?.platform || navigator.platform || "";
  const phone = /Android|iPhone|iPad|iPod|Mobile/i.test(navigator.userAgent) || (/Mac/i.test(platform) && navigator.maxTouchPoints > 1);
  if (!phone && /Win/i.test(platform)) {
    for (const row of document.querySelectorAll("[data-get-row], [data-get]")) {
      const mac = row.querySelector('a[href$="DragShift-mac.zip"]'), win = row.querySelector('a[href$="DragShift.exe"]');
      if (!mac || !win) continue;
      mac.classList.remove("primary");
      win.classList.add("primary");
      row.insertBefore(win, mac);
    }
  }
  if (phone) {
    const share = $("[data-share]");
    $("[data-on-phone]").hidden = false;
    for (const a of document.querySelectorAll("[data-get-row] .btn.primary")) a.classList.remove("primary");
    share.addEventListener("click", async () => {
      const url = document.querySelector('link[rel="canonical"]').href;
      try {
        if (navigator.share) await navigator.share({ title: "DragShift", text: "Free for Mac and Windows", url });
        else { await navigator.clipboard.writeText(url); share.textContent = "Link copied: open it on your computer"; }
      } catch { /* the share sheet was closed */ }
    });
  }
}
// the end buttons inside the film are only clickable once they're showing
getEl.inert = true;
layout();
lastW = $(".stage").clientWidth; lastH = $(".stage").clientHeight;
V.clock = performance.now() / 1000;
// warm up every shader path and texture before the loader lifts, so nothing compiles mid-scroll
if (scene) {
  const warm = [0, F.A1.hit + 0.1, F.J1.hot, F.LOCK.type1, F.LOCK.pop + 0.1, F.J3.work0 + 0.7, F.E.t0 + 0.3, F.E.hit + 0.1];
  for (let i = 0; i < warm.length; i++) {
    draw(warm[i]);
    load(0.84 + (0.15 * (i + 1)) / warm.length, i < 4 ? "Warming up the lamp" : "Sharpening the shadows");
    await nextFrame();
  }
}
// The loader may have given up while this device was slow (boot.js): then the film comes back only if the visitor
// is still at the top; otherwise they keep reading the story and the 3D stays off (no "story and film" mix).
const film = scene && (!root.classList.contains("no-gl") || globalThis.__backToFilm?.());
if (film) {
  // (while the story showed, the film had no size: measure again)
  layout();
  lastW = $(".stage").clientWidth; lastH = $(".stage").clientHeight;
  draw(T);
  root.classList.add("ready", "gl-on");
  globalThis.__loadDone?.();
  requestAnimationFrame((n) => { last = n; tick(n); });
}
