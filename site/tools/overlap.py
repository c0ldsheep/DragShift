# Plays the whole film at several screen sizes and checks, every 0.1 s, that nothing the page draws on top of the
# 3D overlaps: the words (time, heading, line, buttons), the size label, the ring and its tabs, the password
# window, the key chip and the scroll hint. Also that nothing pokes out of the screen sideways.
# Every frame also asks WebGL for errors, with edge smoothing on and off (a refused draw once made the padlock and
# the stamp vanish in Safari).
# Dev tool only. Needs browse.py, a headless Chrome helper kept outside this repo (see BROWSE_TOOLS below).  python3 tools/overlap.py http://127.0.0.1:48780
import os, sys, time, json
sys.path.insert(0, os.path.expanduser(os.environ.get("BROWSE_TOOLS") or sys.exit(
    "Set BROWSE_TOOLS to the folder that holds browse.py (a headless Chrome helper that isn't part of this repo).")))
from browse import Browser

base = sys.argv[1]
SIZES = [(320, 640, ""), (390, 844, ""), (768, 1024, ""), (1440, 900, ""), (390, 844, "&msaa=0"), (1440, 900, "&msaa=0")]
PROBE = r"""(() => {
  const vis = (el) => { if (!el) return null; const s = getComputedStyle(el); if (s.visibility === 'hidden' || s.display === 'none' || +s.opacity < 0.08) return null;
    const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0 ? [r.left, r.top, r.right, r.bottom] : null; };
  const q = (s) => document.querySelector(s);
  const words = ['[data-clock]', '[data-h]', '[data-p]'].map((s) => vis(q(s))).filter(Boolean);
  const get = vis(q('[data-get]'));
  if (get) words.push(get);
  const gl = document.querySelector('[data-scene]').getContext('webgl2');
  return JSON.stringify({ glError: gl ? gl.getError() : -1, words, meter: vis(q('[data-meter]')), ring: vis(q('[data-ring] .disc')), tabs: vis(q('.ring-tabs')),
    pw: vis(q('[data-pw]')), key: vis(q('[data-key]')), hint: vis(q('[data-hint]')), crop: vis(q('[data-crop] .crop-frame')), W: innerWidth });
})()"""
def hit(a, b, pad=2):
    return a and b and a[0] < b[2] - pad and b[0] < a[2] - pad and a[1] < b[3] - pad and b[1] < a[3] - pad
bad = []
only = [int(x) for x in sys.argv[2].split(",")] if len(sys.argv) > 2 else range(len(SIZES))
for w, h, extra in [SIZES[i] for i in only]:
    for _ in range(4):
        try:
            b = Browser(w, h, mobile=w < 760); break
        except Exception:
            time.sleep(2)
    b.goto(f"{base}/?rec{extra}", settle=3.0)
    dur = float(b.js("globalThis.__stops[globalThis.__stops.length - 1]"))
    t = 0.0
    while t <= dur + 1e-6:
        b.js(f"(globalThis.__rec({t:.2f}, {t:.2f}), new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))))")
        o = json.loads(b.js(PROBE))
        if o["glError"]: bad.append((f"{w}{extra}", round(t, 1), "WebGL error", str(o["glError"])))
        things = {k: o[k] for k in ("meter", "ring", "tabs", "pw", "key", "hint", "crop") if o[k]}
        for name, r in things.items():
            for wr in o["words"]:
                if hit(r, wr): bad.append((w, round(t, 1), name, "words"))
        names = list(things)
        for i in range(len(names)):
            for j in range(i + 1, len(names)):
                pair = {names[i], names[j]}
                if pair in ({"ring", "tabs"},): continue
                if hit(things[names[i]], things[names[j]]): bad.append((w, round(t, 1), names[i], names[j]))
        for name, r in list(things.items()) + [("words", x) for x in o["words"]]:
            if r[0] < -1 or r[2] > o["W"] + 1: bad.append((w, round(t, 1), name, "off screen"))
        t += 0.1
    b.close()
    print(w, extra or "", "checked", flush=True)
seen = {}
for w, t, a, c in bad:
    seen.setdefault((w, a, c), []).append(t)
for (w, a, c), ts in sorted(seen.items()):
    print(f"{w}px  {a} × {c}  at {ts[0]}–{ts[-1]} s ({len(ts)} frames)")
print("OVERLAPS:", len(bad))
