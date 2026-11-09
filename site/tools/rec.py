# Records the film frame by frame through the page's ?rec hook, then makes an MP4 with ffmpeg.
# Dev tool only (not part of the site). Needs browse.py, a headless Chrome helper kept outside this repo (see BROWSE_TOOLS below) (headless Chrome) and ffmpeg.
#   python3 tools/rec.py "http://127.0.0.1:48780/?rec" 1440 900 /tmp/frames out.mp4
#   python3 tools/rec.py "http://127.0.0.1:48780/?rec" 390 844 /tmp/frames-p out-phone.mp4
# Widths under 760 record as a phone. The idle camera drift keeps running, as it does live.
import os, subprocess, sys, json, time, math
sys.path.insert(0, os.path.expanduser(os.environ.get("BROWSE_TOOLS") or sys.exit(
    "Set BROWSE_TOOLS to the folder that holds browse.py (a headless Chrome helper that isn't part of this repo).")))
from browse import Browser

url, w, h, outdir, mp4 = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4], sys.argv[5]
FPS = 30
os.makedirs(outdir, exist_ok=True)
for _try in range(4):
    try:
        b = Browser(w, h, mobile=w < 760); break
    except (FileNotFoundError, IndexError, OSError):
        time.sleep(2)
b.goto(url, settle=3.0)
# Someone scrolling at a steady speed: the film moves only with the scroll, glides a little behind it, and the
# heavy stretches (rejections, countdown, padlock, seal) and the short holds play slower, as they do live.
total = float(b.js("globalThis.__scrollTotal"))
SECONDS = 34.0
speed = (total + 0.15) / SECONDS
u, T, times = 0.0, 0.0, []
times += [0.0] * int(1.5 * FPS)
while u < total + 0.15 or abs(target - T) > 0.002:
    u = min(total + 0.15, u + speed / FPS)
    target = float(b.js(f"globalThis.__timeAt({u:.5f})"))
    T += (target - T) * (1 - math.exp(-7 / FPS))
    times.append(T)
times += [times[-1]] * int(3.0 * FPS)
for i, t in enumerate(times):
    b.js(f"(globalThis.__rec({t:.4f}, {i / FPS:.4f}), new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))))")
    b.shot(os.path.join(outdir, f"f{i:04d}.png"))
errs = b.js("JSON.stringify(globalThis.__errs || [])")
b.close()
subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-framerate", str(FPS), "-i", os.path.join(outdir, "f%04d.png"),
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "17", "-movflags", "+faststart", mp4], check=True)
print("frames", len(times), "->", mp4, "errors", errs)
