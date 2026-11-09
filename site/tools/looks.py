# Saves sharp JPG stills of the film's key moments for each look, plus one side-by-side sheet, so a look can be
# picked before it's applied. Dev tool only. Needs browse.py, a headless Chrome helper kept outside this repo (see BROWSE_TOOLS below) (headless Chrome) and ffmpeg.
#   python3 tools/looks.py http://127.0.0.1:48780 ../build/previews/website-form/looks
import os, subprocess, sys, time
sys.path.insert(0, os.path.expanduser(os.environ.get("BROWSE_TOOLS") or sys.exit(
    "Set BROWSE_TOOLS to the folder that holds browse.py (a headless Chrome helper that isn't part of this repo).")))
from browse import Browser

base, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
MOMENTS = [(1.3, "1-photo-slam"), (9.95, "2-shrunk"), (13.6, "3-locked"), (17.3, "4-unlocked"), (26.25, "5-stamp"), (28.25, "6-sealed")]
LOOKS = [("studio", "A-studio"), ("cel", "B-cel")]
for look, name in LOOKS:
    for _ in range(4):
        try:
            b = Browser(1440, 900); break
        except Exception:
            time.sleep(2)
    b.call("Emulation.setDeviceMetricsOverride", width=1440, height=900, deviceScaleFactor=2, mobile=False)
    b.goto(f"{base}/?rec&look={look}", settle=3.0)
    for t, label in MOMENTS:
        b.js(f"(globalThis.__rec({t}, {t}), new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))))")
        png = os.path.join(out, f"{name}-{label}.png")
        b.shot(png)
        subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", png, "-q:v", "2", png[:-4] + ".jpg"], check=True)
        os.remove(png)
    print(name, b.js("JSON.stringify(globalThis.__errs)"))
    b.close()
# side by side: A on the left, B on the right, one row per moment
rows = []
for _, label in MOMENTS:
    rows += ["-i", os.path.join(out, f"A-studio-{label}.jpg"), "-i", os.path.join(out, f"B-cel-{label}.jpg")]
n = len(MOMENTS)
flt = "".join(f"[{2*i}:v]scale=960:-1[a{i}];[{2*i+1}:v]scale=960:-1[b{i}];[a{i}][b{i}]hstack=inputs=2[r{i}];" for i in range(n))
flt += "".join(f"[r{i}]" for i in range(n)) + f"vstack=inputs={n}[o]"
subprocess.run(["ffmpeg", "-loglevel", "error", "-y", *rows, "-filter_complex", flt, "-map", "[o]", "-q:v", "3", os.path.join(out, "compare-A-left-B-right.jpg")], check=True)
print("done", out)
