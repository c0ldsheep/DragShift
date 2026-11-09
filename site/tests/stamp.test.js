// The published copy (tools/stamp.mjs): every script and style address carries the same version, each one points at
// a real file, nothing else changes, and any change to the site gives a new version.
import { test } from "node:test";
import assert from "node:assert/strict";
import { cpSync, existsSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { createHash } from "node:crypto";
import { files, stamp, versionOf } from "../tools/stamp.mjs";

const site = join(dirname(fileURLToPath(import.meta.url)), "..");
const out = mkdtempSync(join(tmpdir(), "dragshift-stamp-"));
const v = stamp(site, out);

test("every import and every script and style tag carries this version, and points at a real file", () => {
  assert.match(v, /^[0-9a-f]{10}$/);
  const html = readFileSync(join(out, "index.html"), "utf8");
  const tags = [...html.matchAll(/\b(?:src|href)="(assets\/[^"]+\.(?:js|css)[^"]*)"/g)].map((m) => m[1]);
  assert.ok(tags.length >= 3, "boot.js, main.js and the stylesheet");
  for (const t of tags) {
    assert.ok(t.endsWith(`?v=${v}`), t);
    assert.ok(existsSync(join(out, t.split("?")[0])), t);
  }
  let n = 0;
  for (const p of files(out, "assets").filter((f) => f.endsWith(".js"))) {
    const src = readFileSync(join(out, p), "utf8");
    for (const m of src.matchAll(/(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s+|\bnew URL\(\s*)["'](\.{1,2}\/[^"']+)["']/g)) {
      n++;
      assert.ok(m[1].endsWith(`.js?v=${v}`), `${p}: ${m[1]}`);
      assert.ok(existsSync(join(out, dirname(p), m[1].split("?")[0])), `${p}: ${m[1]} is missing`);
    }
  }
  assert.ok(n >= 9, `found ${n} imports`); // main → film, ring, haptics, gl/scene; scene → film, engine, paint, shaders; paint → film
});

test("stamping changes nothing but the addresses", () => {
  for (const p of ["index.html", ...files(site, "assets")]) {
    const a = readFileSync(join(site, p)), b = readFileSync(join(out, p));
    if (!/\.(js|html)$/.test(p)) { assert.ok(a.equals(b), p); continue; }
    const back = b.toString("utf8").replaceAll(`?v=${v}`, "").replace(`\n  <meta name="dragshift-version" content="${v}">`, "");
    assert.equal(back, a.toString("utf8"), p);
  }
});

test("the same site gives the same version; any change gives a new one", () => {
  assert.equal(versionOf(site), v);
  const copy = mkdtempSync(join(tmpdir(), "dragshift-copy-"));
  cpSync(join(site, "index.html"), join(copy, "index.html"));
  cpSync(join(site, "assets"), join(copy, "assets"), { recursive: true });
  assert.equal(versionOf(copy), v);
  const f = join(copy, "assets/js/haptics.js");
  writeFileSync(f, readFileSync(f, "utf8") + "\n");
  assert.notEqual(versionOf(copy), v);
});

test("the published copy keeps itself on the device only with every file checked against its fingerprint", () => {
  const html = readFileSync(join(out, "index.html"), "utf8");
  assert.match(html, new RegExp(`<meta name="dragshift-version" content="${v}">`));
  const sw = readFileSync(join(out, "sw.js"), "utf8");
  const kept = JSON.parse(sw.match(/const FILES = (\{[\s\S]*?\});/)[1]);
  // the page, every script and style at its stamped address, the photo and the icon; nothing else
  const scripts = files(out, "assets").filter((f) => /\.(js|css)$/.test(f)).map((f) => `${f}?v=${v}`);
  assert.deepEqual(Object.keys(kept).sort(), ["./", "assets/icon.svg", "assets/img/cat.jpg", ...scripts].sort());
  for (const [url, hash] of Object.entries(kept)) {
    const file = url === "./" ? "index.html" : url.split("?")[0];
    assert.equal(createHash("sha256").update(readFileSync(join(out, file))).digest("base64"), hash, url);
  }
  // a mismatch keeps nothing; the page itself prefers the network; old versions are deleted
  assert.match(sw, /isn't version/);
  assert.match(sw, /await caches\.delete\(CACHE\);\s*throw e;/);
  assert.match(sw, /Promise\.race\(\[fetch\(req\)/);
  assert.match(sw, /k\.startsWith\("dragshift-"\) && k !== CACHE/);
  // and only a published copy registers it (the source page has no version tag)
  assert.doesNotMatch(readFileSync(join(site, "index.html"), "utf8"), /dragshift-version/);
  assert.match(readFileSync(join(site, "assets/js/boot.js"), "utf8"), /meta\[name="dragshift-version"\]'\) && "serviceWorker" in navigator/);
});
