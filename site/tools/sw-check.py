# The published copy's offline worker (sw.js, written by tools/stamp.mjs) in a real browser (headless Chrome):
#   1. first visit: the film starts, and the worker keeps this version (every file checked)
#   2. offline: with the server stopped, the page and the film still come up, from the device
#   3. a new version is published: the page and its files come from the network (no mixing)
#   4. a half-updated host (one file isn't the version's): the worker keeps nothing, and the film still starts
# Dev tool only: needs browse.py, a headless Chrome helper kept outside this repo (see BROWSE_TOOLS below) (headless Chrome).  python3 tools/sw-check.py
import os, shutil, socket, subprocess, sys, tempfile, time
sys.path.insert(0, os.path.expanduser(os.environ.get("BROWSE_TOOLS") or sys.exit(
    "Set BROWSE_TOOLS to the folder that holds browse.py (a headless Chrome helper that isn't part of this repo).")))
from browse import Browser

site = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
repo = os.path.dirname(site)
tmp = tempfile.mkdtemp(prefix="dragshift-sw-")
s = socket.socket(); s.bind(("127.0.0.1", 0)); port = s.getsockname()[1]; s.close()
base = f"http://127.0.0.1:{port}/"
server = None

def stamp(src, out):
    subprocess.run(["node", os.path.join(site, "tools/stamp.mjs"), src, out], check=True, capture_output=True)
    return open(os.path.join(out, "index.html")).read().split('name="dragshift-version" content="')[1][:10]

def serve(folder):
    global server
    stop()
    server = subprocess.Popen([sys.executable, os.path.join(repo, "scripts/serve-site.py"), folder, str(port)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    import urllib.request
    for _ in range(80):
        try:
            if urllib.request.urlopen(base, timeout=1).status == 200: return
        except Exception: time.sleep(0.1)
    raise SystemExit("the test server didn't start")

def stop():
    global server
    if server: server.terminate(); server.wait(); server = None

def state(b):
    return b.js("""(async () => ({
      href: location.href,
      film: document.documentElement.classList.contains('gl-on') && !document.documentElement.classList.contains('no-gl'),
      version: document.querySelector('meta[name="dragshift-version"]')?.content,
      controlled: !!navigator.serviceWorker?.controller,
      kept: self.caches ? await Promise.all((await caches.keys()).map(async k => k + ':' + (await (await caches.open(k)).keys()).length)) : [],
    }))()""")

def wait_for(b, cond, secs=12):
    end = time.time() + secs
    while time.time() < end:
        st = state(b)
        if cond(st): return st
        time.sleep(0.5)
    return state(b)

fails = 0
def check(name, ok, st):
    global fails
    fails += not ok
    print("PASS" if ok else "FAIL", name, "" if ok else st)

try:
    v1 = stamp(site, os.path.join(tmp, "v1"))
    serve(os.path.join(tmp, "v1"))
    b = Browser(1200, 750)
    b.goto(base, settle=1)
    st = wait_for(b, lambda s: s["film"] and any(k.startswith(f"dragshift-{v1}:") for k in s["kept"]))
    import json, re
    n = len(json.loads(re.search(r"const FILES = (\{.*?\});", open(os.path.join(tmp, "v1/sw.js")).read(), re.S).group(1)))
    check(f"first visit: the film starts and the device keeps version {v1} ({n} files)", st["film"] and f"dragshift-{v1}:{n}" in st["kept"], st)

    stop()  # offline
    b.goto(base, settle=1)
    st = wait_for(b, lambda s: s["film"])
    check("offline: the page and the film come up from the device", st["film"] and st["controlled"] and st["version"] == v1, st)

    changed = os.path.join(tmp, "src2")
    shutil.copytree(site, changed, ignore=shutil.ignore_patterns("tests", "tools", "*.md"))
    with open(os.path.join(changed, "assets/js/film.js"), "a") as f: f.write("\n// a new version\n")
    v2 = stamp(changed, os.path.join(tmp, "v2"))
    serve(os.path.join(tmp, "v2"))
    b.goto(base, settle=1)
    st = wait_for(b, lambda s: s["film"] and s["version"] == v2)
    check(f"a new version ({v2}) comes from the network, whole", st["film"] and st["version"] == v2, st)
    try: b.close()
    except Exception: pass

    # a half-updated host: version 3's worker, but one of its files is still the old one
    with open(os.path.join(changed, "assets/js/film.js"), "a") as f: f.write("// version 3\n")
    v3 = stamp(changed, os.path.join(tmp, "v3"))
    shutil.copy(os.path.join(tmp, "v2/assets/js/film.js"), os.path.join(tmp, "v3/assets/js/film.js"))
    serve(os.path.join(tmp, "v3"))
    b = Browser(1200, 750)  # a fresh visitor
    b.goto(base, settle=1)
    time.sleep(6)
    st = state(b)
    check("a half-updated host: nothing is kept on the device", not any(k.startswith(f"dragshift-{v3}") for k in st["kept"]), st)
    try: b.close()
    except Exception: pass
finally:
    stop()
    shutil.rmtree(tmp, ignore_errors=True)
print("ALL PASS" if not fails else f"{fails} FAILED")
sys.exit(1 if fails else 0)
