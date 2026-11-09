#!/usr/bin/env python3
# Serves the website on this computer while you work on it:
#   ./scripts/serve-site.py                    http://127.0.0.1:8000 (this Mac only)
#   ./scripts/serve-site.py site 48780 --lan   also your phone on the same Wi-Fi (prints the address)
# Unlike `python3 -m http.server`:
#   - it tells the browser to check for a newer copy of every file on every visit (Cache-Control: no-cache), so a
#     test never runs a mix of old and new files;
#   - it serves only what the public site has (index.html and assets/): no folder listings, no tests, tools or notes,
#     which matters with --lan, where anyone on the same Wi-Fi can reach it;
#   - it sends the security headers the published page relies on (and doesn't say which Python it runs);
#   - it queues more connections (browsers open six at once and the stock server's queue of five drops one).
# The page's scripts are modules, so file:// won't do.
import functools, http.server, os, subprocess, sys

class Server(http.server.ThreadingHTTPServer):
    request_queue_size = 64
    daemon_threads = True

# the same rules as the page's own CSP, plus the ones only a header can carry (GitHub Pages can't send these)
HEADERS = {
    "Cache-Control": "no-cache",
    "Content-Security-Policy": "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; "
                               "connect-src 'none'; worker-src 'self'; frame-src 'none'; object-src 'none'; base-uri 'none'; "
                               "form-action 'none'; frame-ancestors 'none'",
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "DENY",
    "Referrer-Policy": "no-referrer",
    "Permissions-Policy": "camera=(), microphone=(), geolocation=(), payment=(), usb=()",
}

class Handler(http.server.SimpleHTTPRequestHandler):
    server_version = "DragShift-dev"
    sys_version = ""

    def end_headers(self):
        for k, v in HEADERS.items():
            if k == "Content-Security-Policy" and self.path.split("?")[0] == "/sw.js":
                # the offline worker's own rules: it may fetch this site's files to keep them, nothing else
                v = "default-src 'none'; connect-src 'self'"
            self.send_header(k, v)
        super().end_headers()

    def send_head(self):
        # only the published files: "/", "/index.html" and real files under /assets/ (no folders, no dotfiles)
        path = self.path.split("?", 1)[0].split("#", 1)[0]
        public = path in ("/", "/index.html", "/sw.js") or (
            path.startswith("/assets/") and not path.endswith("/") and "/." not in path and ".." not in path)
        if not public or (path != "/" and os.path.isdir(self.translate_path(path))):
            self.send_error(404, "Not found")
            return None
        return super().send_head()

    def list_directory(self, path):
        self.send_error(404, "Not found")
        return None
    def log_message(self, fmt, *args):
        # quiet, except for files that weren't served
        if len(args) > 1 and str(args[1])[:1] in "45":
            sys.stderr.write("%s %s\n" % (args[1], args[0]))

flags = [a for a in sys.argv[1:] if a.startswith("--")]
rest = [a for a in sys.argv[1:] if not a.startswith("--")]
root = rest[0] if rest else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "site")
port = int(rest[1]) if len(rest) > 1 else 8000
lan = "--lan" in flags
print(f"DragShift site: http://127.0.0.1:{port}  (Ctrl+C stops it)")
if lan:
    ip = subprocess.run(["ipconfig", "getifaddr", "en0"], capture_output=True, text=True).stdout.strip()
    print(f"On your phone (same Wi-Fi): http://{ip or '<this Mac’s Wi-Fi address>'}:{port}")
sys.stdout.flush()
Server(("0.0.0.0" if lan else "127.0.0.1", port), functools.partial(Handler, directory=root)).serve_forever()
