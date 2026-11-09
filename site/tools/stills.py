# Renders the pictures the page needs besides the film: the five stills for the no-3D story
# (assets/img/stills/1..5.jpg, 1200 × 750, scene only) and the share card (assets/og.jpg, 1200 × 630).
# Dev tool only. Needs browse.py, a headless Chrome helper kept outside this repo (see BROWSE_TOOLS below) (headless Chrome) and ffmpeg. Serve the site first.
#   python3 tools/stills.py http://127.0.0.1:48780
import os, subprocess, sys, time, json
sys.path.insert(0, os.path.expanduser(os.environ.get("BROWSE_TOOLS") or sys.exit(
    "Set BROWSE_TOOLS to the folder that holds browse.py (a headless Chrome helper that isn't part of this repo).")))
from browse import Browser

base = sys.argv[1]
root = os.path.join(os.path.dirname(__file__), "..")
os.makedirs(os.path.join(root, "assets/img/stills"), exist_ok=True)

def open_page(w, h, query):
    for _ in range(4):
        try:
            b = Browser(w, h); break
        except Exception:
            time.sleep(2)
    b.call("Emulation.setDeviceMetricsOverride", width=w, height=h, deviceScaleFactor=2, mobile=False)
    b.goto(f"{base}/?rec&{query}", settle=3.0)
    return b

def grab(b, t, out, q):
    b.js(f"(globalThis.__rec({t}, {t}), new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))))")
    png = out + ".png"
    b.shot(png)
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", png, "-vf", f"scale={q}", "-q:v", "4", out], check=True)
    os.remove(png)

b = open_page(1200, 750, "still")
times = json.loads(b.js("""(() => { const F = globalThis.__F; return JSON.stringify([F.A2.hit + 0.58, F.J1.place1 + 0.6, F.LOCK.pop + 0.12, F.J3.place1 + 0.6, F.DURATION]); })()"""))
for i, t in enumerate(times, 1):
    grab(b, t, os.path.join(root, f"assets/img/stills/{i}.jpg"), "1200:750")
print("stills", b.js("JSON.stringify(globalThis.__errs)"))
b.close()

b = open_page(1200, 630, "og")
grab(b, float(b.js("globalThis.__F.DURATION")), os.path.join(root, "assets/og.jpg"), "1200:630")
print("og", b.js("JSON.stringify(globalThis.__errs)"))
b.close()
