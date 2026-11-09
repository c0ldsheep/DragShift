// The local server (scripts/serve-site.py, also reachable on the Wi-Fi with --lan): security headers, no version,
// no folder listings, and nothing but the public site's files.
import { test, before, after } from "node:test";
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../..", import.meta.url));
const port = 47000 + Math.floor(Math.random() * 900);
const base = `http://127.0.0.1:${port}`;
let server;

before(async () => {
  server = spawn("python3", ["scripts/serve-site.py", "site", String(port)], { cwd: root, stdio: "ignore" });
  for (let i = 0; i < 50; i++) {
    try { await fetch(base + "/"); return; } catch { await new Promise((r) => setTimeout(r, 100)); }
  }
  throw new Error("serve-site.py didn't start");
});
after(() => server?.kill());

test("the page and its files come with the security headers, and no version", async () => {
  for (const p of ["/", "/assets/js/main.js", "/assets/css/site.css"]) {
    const r = await fetch(base + p);
    assert.equal(r.status, 200, p);
    const h = r.headers;
    assert.match(h.get("content-security-policy"), /default-src 'none'.*frame-ancestors 'none'/, p);
    assert.equal(h.get("x-content-type-options"), "nosniff");
    assert.equal(h.get("x-frame-options"), "DENY");
    assert.equal(h.get("referrer-policy"), "no-referrer");
    assert.equal(h.get("cache-control"), "no-cache");
    assert.doesNotMatch(h.get("server") ?? "", /Python|SimpleHTTP|\d/);
  }
});

test("only the public site is served: no folder listings, tests, tools, notes or ../", async () => {
  for (const p of ["/assets/", "/assets/js/", "/tests/page.test.js", "/tools/stamp.mjs", "/DESIGN.md",
    "/assets/../DESIGN.md", "/assets/%2e%2e/DESIGN.md", "/.DS_Store", "/assets/js/nope.js"]) {
    const r = await fetch(base + p);
    assert.equal(r.status, 404, p);
    assert.doesNotMatch(await r.text(), /Directory listing|Traceback|\/Users\//, p);
  }
});
