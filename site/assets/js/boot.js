// Runs before the first paint. Every visit starts at the top with the film. Until the 3D is ready a loading bar
// shows real progress (each step the page reports, with its name), shaped the way waits feel shortest:
//   · it starts fast and keeps moving, so it never looks stuck (0 → 90 % quickly, ahead of the real steps);
//   · 90 → 98 % creeps while the last real work finishes, but never stops;
//   · the moment everything is ready it runs to 100 % in a quick dash, then lifts.
//   · it's drawn as the app's ring: seven petals light up in turn, a sheen runs backwards over them (backward
//     motion makes a wait feel shorter), and the hub turns green with a tick at 100 %.
//   · the petals and the sheen are CSS animations (site.css), run by the compositor, so they stay smooth while the
//     page is busy building the 3D. This script only writes the number (from the same curve) and finishes them.
// If there's no 3D, or nothing progresses for 9 seconds of the page really running, the story shows as words and
// still pictures instead.
//
// Rules learned the hard way (2026-11-09: Safari opened behind another window, came back after 12 s and gave up on
// a 3D that was ready a moment later):
//   · the "stuck" clock counts only time the page is running. A hidden or covered page gets no frames, so its
//     loading pauses too; that is waiting, not being stuck.
//   · giving up because it was slow is never final: if the 3D finishes while the visitor is still at the top, the
//     film comes back.
//   · if one of the page's own files fails to load, reload once (a dropped connection, or a half-updated cache),
//     then fall back to the story.
(() => {
  globalThis.__errs = [];
  const root = document.documentElement;
  const RETRY = "dragshift-retried";
  // a file of ours that failed (capture: a <script> or <link> error doesn't bubble), or an error from our scripts
  // before the page script ran (e.g. a module that no longer has the export another one imports)
  addEventListener("error", (e) => {
    const el = e.target;
    const file = el && el !== window && (el.tagName === "SCRIPT" || el.tagName === "LINK") ? el.src || el.href : "";
    globalThis.__errs.push(file ? `failed to load ${file}` : String(e.message));
    const ours = file || String(e.filename || "").includes("/assets/js/");
    if (ours && !globalThis.__mainRan && !L.done && !L.story) broken();
  }, true);
  addEventListener("unhandledrejection", (e) => globalThis.__errs.push(String(e.reason)));
  root.classList.add("js", "is-loading");
  if ("scrollRestoration" in history) history.scrollRestoration = "manual";

  // Don't run inside someone else's page (GitHub Pages can't send the header that forbids framing).
  if (window.top !== window.self) {
    root.classList.add("framed");
    try { window.top.location.replace(window.self.location.href); } catch { /* blocked: the page shows a link instead */ }
  }

  // run = seconds the page has been running (frames), stall = running seconds since the last real step
  const L = { real: 0.04, shown: 0, label: "Setting the desk", done: false, doneAt: 0, start: performance.now(), run: 0, stall: 0, gone: false, story: null };
  globalThis.__loadStep = (p, label) => {
    if (p > L.real) L.real = p;
    if (label) L.label = label;
    L.stall = 0;
  };
  globalThis.__loadDone = () => {
    L.real = 1; L.done = true; L.doneAt = performance.now(); L.stall = 0;
    try { sessionStorage.removeItem(RETRY); } catch { /* storage off: nothing to clear */ }
    // A published copy (tools/stamp.mjs tags it with its version) keeps itself on the device, so the next visit
    // loads at once and works offline. Started after the film is ready, so it never competes with loading. The
    // local test server's page has no tag, so nothing is kept while developing.
    if (document.querySelector('meta[name="dragshift-version"]') && "serviceWorker" in navigator && location.protocol !== "file:") {
      setTimeout(() => navigator.serviceWorker.register("sw.js").catch((e) => globalThis.__errs.push(`service worker: ${e}`)), 1500);
    }
  };

  /** One of our files didn't load: reload once, then show the story. */
  function broken() {
    let tried = true;
    try { tried = sessionStorage.getItem(RETRY) === "1"; sessionStorage.setItem(RETRY, "1"); } catch { /* storage off: don't risk a loop */ }
    if (!tried) { L.gone = true; location.reload(); return; }
    globalThis.__showStory("broken");
  }

  /** The 3D finished after the loader gave up for being slow: back to the film, if the visitor is still at the top. */
  globalThis.__backToFilm = () => {
    if (L.story !== "slow" || scrollY > 80) return false;
    root.classList.remove("no-gl");
    L.story = null;
    return true;
  };

  /** No 3D here: put the still pictures into the story and show it instead of the film.
   *  why: "no-3d" (no WebGL 2 or the scene failed), "lost" (the browser took the 3D away), "slow", "broken". */
  globalThis.__showStory = (why = "no-3d") => {
    if (L.story) return;
    L.story = why;
    root.classList.add("no-gl");
    root.classList.remove("is-loading");
    L.gone = true;
    for (const f of document.querySelectorAll("[data-still]")) {
      if (f.querySelector("img")) continue;
      const ns = f.querySelector("noscript");
      const alt = ns ? (ns.textContent.match(/alt="([^"]*)"/) || [])[1] || "" : "";
      const img = new Image(1200, 750);
      img.src = `assets/img/stills/${f.dataset.still}.jpg`;
      img.alt = alt;
      img.loading = "lazy";
      f.append(img);
    }
  };

  /** How far the loader shows at t seconds: fast and continuous to 90 % (a little ahead of the real steps), then a
   *  slow creep toward 98.5 % that never stops. The petals' CSS timings come from this same curve. */
  const shownAt = (t) => {
    const u = Math.max(0, t - 0.1);
    const fast = 0.9 * (1 - Math.exp(-u / 0.55));
    const t90 = 2.85; // where the fast part reaches 89.5 %
    return u < t90 ? fast : 0.985 - (0.985 - 0.895) * Math.exp(-(u - t90) / 3.65);
  };

  let pct = null, step = null, box = null, prev = performance.now(), finish = null;
  const frame = (now) => {
    if (L.gone) return;
    if (!box) {
      box = document.querySelector("[data-loader]");
      pct = document.querySelector("[data-load-pct]");
      step = document.querySelector("[data-load-step]");
    }
    // (a frame's timestamp can be a hair before the clock we started from: never let time run backwards)
    // (no frames come while the page is hidden or covered, and the first one back counts at most 0.1 s)
    const dt = Math.min(0.1, Math.max(0, (now - prev) / 1000));
    prev = Math.max(prev, now);
    L.run += dt;
    if (!document.hidden) L.stall += dt;
    // (the CSS petals run on the page's clock since it started, hidden time included, so the number does too)
    const t = (now - L.start) / 1000;
    if (L.done && !finish) {
      // ready: every petal runs to full from wherever its animation is, then the number dashes to 100
      finish = { from: L.shown, at: now };
      for (const el of document.querySelectorAll(".lr-petal")) {
        const o = parseFloat(getComputedStyle(el).opacity) || 0;
        el.style.animation = "none";
        el.style.opacity = "1";
        el.animate?.([{ opacity: o }, { opacity: 1 }], { duration: 180, easing: "ease-out" });
      }
    }
    L.shown = finish ? finish.from + (1 - finish.from) * Math.min(1, (now - finish.at) / 180) : Math.max(L.shown, shownAt(t));
    if (box && pct) {
      const n = Math.max(0, Math.min(100, Math.floor(L.shown * 100)));
      if (pct.textContent !== `${n}%`) { pct.textContent = `${n}%`; box.setAttribute("aria-valuenow", String(n)); }
      if (step && step.textContent !== (L.done ? "Ready" : `${L.label}…`)) step.textContent = L.done ? "Ready" : `${L.label}…`;
    }
    if (L.shown >= 1 && finish) {
      // the hub goes green with a tick for a moment, then the ring lifts away
      box?.classList.add("done");
      if (now - L.doneAt > 520) { root.classList.remove("is-loading"); L.gone = true; return; }
    }
    // nothing has moved for 9 seconds of running and no 3D yet: show the still story (the film can still come back)
    if (!L.done && L.stall > 9) { globalThis.__showStory("slow"); return; }
    requestAnimationFrame(frame);
  };
  requestAnimationFrame(frame);
})();
