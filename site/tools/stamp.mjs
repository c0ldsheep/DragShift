// Makes the copy of the site that gets published, with every script and style address stamped with this version:
// assets/js/main.js → assets/js/main.js?v=1a2b3c4d5e, and the same inside every import.
//
// Why: GitHub Pages lets browsers keep each file for 10 minutes. Without the stamp, a visitor who comes back right
// after an update can get the new main.js with the old haptics.js; one missing export and the whole 3D never starts.
// With it, a new version has new addresses, so a browser can only ever run one whole version.
//
// It also writes sw.js, a service worker that keeps this exact version on the visitor's device: repeat visits load
// from it at once, and the film works offline. Safe by design:
//   · it keeps whole versions only: every file is checked against its SHA-256 from here before it's kept, so a
//     half-updated host can't put a mix on the device (install fails, the site simply loads from the network);
//   · the page itself comes from the network whenever that answers within 2.5 s (always the newest version), and
//     from the device only when offline or slow; a newer version's files are fetched, never mixed in;
//   · a new version replaces the old one once every tab of the old one is closed, then the old copy is deleted.
//   · To remove it from everyone's devices one day: publish an sw.js that only calls self.registration.unregister().
//   node site/tools/stamp.mjs site _site      (the Pages workflow runs this)
import { createHash } from "node:crypto";
import { cpSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from "node:fs";
import { join, relative } from "node:path";
import { pathToFileURL } from "node:url";

/** Every file under dir, as paths relative to it, sorted. */
export function files(dir, sub = "") {
  return readdirSync(join(dir, sub)).sort().flatMap((n) => {
    const p = join(sub, n);
    return statSync(join(dir, p)).isDirectory() ? files(dir, p) : [p];
  });
}

/** The version: a hash of everything visitors get (index.html and assets/), so any change gives a new one. */
export function versionOf(site) {
  const h = createHash("sha256");
  for (const p of ["index.html", ...files(site, "assets")]) h.update(p).update("\0").update(readFileSync(join(site, p))).update("\0");
  return h.digest("hex").slice(0, 10);
}

// a script's own imports: from "./x.js", import("./gl/x.js"), import "./x.js", and a worker's new URL("./x.js", …)
const IMPORTS = /(\bfrom\s*|\bimport\s*\(\s*|\bimport\s+|\bnew URL\(\s*)(["'])(\.{1,2}\/[^"'?]+\.js)\2/g;
// the page's script and style tags
const TAGS = /\b(src|href)="(assets\/[^"?]+\.(?:js|css))"/g;

export function stamp(site, out) {
  const v = versionOf(site);
  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });
  cpSync(join(site, "index.html"), join(out, "index.html"));
  cpSync(join(site, "assets"), join(out, "assets"), { recursive: true });
  const html = join(out, "index.html");
  // (the version tag also tells boot.js this is a published copy, which may keep itself on the device)
  writeFileSync(html, readFileSync(html, "utf8").replace(TAGS, (_, a, p) => `${a}="${p}?v=${v}"`)
    .replace('<meta charset="utf-8">', `<meta charset="utf-8">\n  <meta name="dragshift-version" content="${v}">`));
  for (const p of files(out, "assets").filter((f) => f.endsWith(".js"))) {
    const f = join(out, p);
    writeFileSync(f, readFileSync(f, "utf8").replace(IMPORTS, (_, k, q, s) => `${k}${q}${s}?v=${v}${q}`));
  }
  // what the device keeps: the page, every script and style (at their stamped addresses), the photo and the icon
  const keep = { "./": "index.html" };
  for (const p of files(out, "assets")) {
    const url = `assets/${p.split("\\").join("/").replace(/^assets\//, "")}`;
    if (/\.(js|css)$/.test(p)) keep[`${url}?v=${v}`] = p;
    else if (/^assets\/(img\/cat\.jpg|icon\.svg)$/.test(url)) keep[url] = p;
  }
  const hashes = Object.fromEntries(Object.entries(keep).map(([url, p]) => [url, createHash("sha256").update(readFileSync(join(out, p))).digest("base64")]));
  writeFileSync(join(out, "sw.js"), serviceWorker(v, hashes));
  return v;
}

/** The service worker for one version (see the top of this file). */
export function serviceWorker(version, hashes) {
  return `// DragShift ${version}: keeps this version of the site on the device (written by tools/stamp.mjs).
const VERSION = ${JSON.stringify(version)};
const FILES = ${JSON.stringify(hashes, null, 2)};
const CACHE = "dragshift-" + VERSION;
const b64 = (buf) => btoa(String.fromCharCode(...new Uint8Array(buf)));

// keep every file of this version, each checked against its fingerprint; any mismatch and nothing is kept
self.addEventListener("install", (event) => event.waitUntil((async () => {
  const cache = await caches.open(CACHE);
  try {
    for (const [url, hash] of Object.entries(FILES)) {
      const res = await fetch(url, { cache: "reload" });
      if (!res.ok) throw new Error(url + ": " + res.status);
      const body = await res.arrayBuffer();
      if (b64(await crypto.subtle.digest("SHA-256", body)) !== hash) throw new Error(url + " isn't version " + VERSION);
      await cache.put(url, new Response(body, { headers: { "Content-Type": res.headers.get("Content-Type") || "application/octet-stream" } }));
    }
  } catch (e) {
    await caches.delete(CACHE);
    throw e;
  }
})()));

// once this version is in charge, older copies go
self.addEventListener("activate", (event) => event.waitUntil(caches.keys().then((keys) =>
  Promise.all(keys.filter((k) => k.startsWith("dragshift-") && k !== CACHE).map((k) => caches.delete(k))))));

self.addEventListener("fetch", (event) => {
  const req = event.request;
  if (req.method !== "GET") return;
  const url = new URL(req.url);
  if (url.origin !== location.origin) return;
  const root = new URL("./", self.registration.scope).pathname;
  if (req.mode === "navigate") {
    if (url.pathname !== root && url.pathname !== root + "index.html") return;
    // the page: the network's (newest) when it answers within 2.5 s, else this version's copy (offline, slow)
    event.respondWith((async () => {
      const kept = await (await caches.open(CACHE)).match("./");
      if (!kept) return fetch(req);
      try {
        return await Promise.race([fetch(req), new Promise((_, no) => setTimeout(() => no(new Error("slow")), 2500))]);
      } catch {
        return kept;
      }
    })());
    return;
  }
  // scripts, styles, the photo: this version's copy if it has the exact address, else the network
  event.respondWith(caches.open(CACHE).then((c) => c.match(req)).then((hit) => hit || fetch(req)));
});
`;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [site, out] = process.argv.slice(2);
  if (!site || !out) { console.error("usage: node stamp.mjs <site folder> <out folder>"); process.exit(2); }
  console.log(`Stamped ${relative(process.cwd(), out) || out} with version ${stamp(site, out)}`);
}
