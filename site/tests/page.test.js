// Checks on the page itself: what search engines and link previews read, nothing loaded from other sites, every
// local file it points at exists, and the honest small print is there.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, existsSync } from "node:fs";

const html = readFileSync(new URL("../index.html", import.meta.url), "utf8");
const here = (p) => new URL(`../${p}`, import.meta.url);

test("title, description, canonical and share card are set (DragShift everywhere)", () => {
  assert.match(html, /<title>DragShift · [^<]{10,60}<\/title>/);
  const desc = html.match(/<meta name="description" content="([^"]+)"/)[1];
  assert.ok(desc.length > 80 && desc.length <= 230, `description is ${desc.length} characters`);
  for (const p of ["og:title", "og:description", "og:image", "og:url", "og:image:alt"]) assert.match(html, new RegExp(`property="${p}"`), p);
  assert.match(html, /name="twitter:card" content="summary_large_image"/);
  assert.match(html, /rel="canonical" href="https:\/\/c0ldsheep\.github\.io\/DragShift\/"/);
});

test("the structured data is valid JSON and says it's free", () => {
  const ld = JSON.parse(html.match(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/)[1]);
  assert.equal(ld["@type"], "SoftwareApplication");
  assert.equal(ld.offers.price, "0");
  assert.equal(ld.isAccessibleForFree, true);
});

test("nothing is loaded from another site, and the CSP says so", () => {
  for (const m of html.matchAll(/<(script|link|img)\b[^>]*(src|href)="([^"]+)"/g)) {
    if (m[1] === "link" && /rel="canonical"/.test(m[0])) continue;
    assert.ok(!/^https?:/.test(m[3]), `${m[1]} loads ${m[3]}`);
  }
  // deny by default; only scripts, styles and pictures from this site (data: for the loader's masks)
  const csp = html.match(/Content-Security-Policy" content="([^"]+)"/)[1];
  const rules = Object.fromEntries(csp.split(";").map((r) => r.trim().split(/\s+/)).map(([k, ...v]) => [k, v.join(" ")]));
  assert.deepEqual(rules, {
    "default-src": "'none'", "script-src": "'self'", "style-src": "'self'", "img-src": "'self' data:",
    "connect-src": "'none'", "worker-src": "'self'", "frame-src": "'none'", "object-src": "'none'", "base-uri": "'none'", "form-action": "'none'",
  });
  assert.match(html, /<meta name="referrer" content="no-referrer">/);
});

test("links go to the GitHub repo, its releases, its privacy and licence pages, or this site itself", () => {
  for (const m of html.matchAll(/<a\b[^>]*href="(https?:[^"]+)"/g)) {
    if (m[1] === "https://c0ldsheep.github.io/DragShift/") continue;
    assert.match(m[1], /^https:\/\/github\.com\/c0ldsheep\/DragShift(\/releases\/latest(\/download\/DragShift(-mac\.zip|\.dmg|\.exe))?|\/blob\/main\/(PRIVACY\.md|LICENSE))?$/, m[1]);
  }
  // the privacy statement and the licence the footer links to are in the repository
  for (const f of ["PRIVACY.md", "LICENSE"]) assert.ok(existsSync(new URL(`../../${f}`, import.meta.url)), f);
  assert.match(html, /blob\/main\/PRIVACY\.md/);
});

test("every local file the page uses exists", () => {
  const files = new Set(["assets/og.jpg", "assets/icon.svg", "assets/img/cat.jpg"]);
  for (const m of html.matchAll(/(?:src|href)="(assets\/[^"]+)"/g)) files.add(m[1]);
  for (const m of html.matchAll(/data-still="(\d)"/g)) files.add(`assets/img/stills/${m[1]}.jpg`);
  for (const f of files) assert.ok(existsSync(here(f)), `missing ${f}`);
});

test("the honest small print: requirements and what differs on Windows", () => {
  assert.match(html, /macOS 13/);
  assert.match(html, /Windows 10 and 11/);
  assert.match(html, /On Windows, Unlock and Merge save the pages as pictures/);
  assert.doesNotMatch(html, /Mac only/);
  assert.match(html, /no cookies and no trackers/);
});

test("there is one way to skip the film and every still has a description", () => {
  assert.match(html, /class="skip" href="#get-it"/);
  assert.match(html, /id="get-it"/);
  const stills = [...html.matchAll(/<figure data-still="\d"><noscript><img [^>]*alt="([^"]+)"/g)];
  assert.equal(stills.length, 5);
});

test("the page refuses to run inside another site's frame, and the test switches are off for visitors", () => {
  const boot = readFileSync(here("assets/js/boot.js"), "utf8");
  const main = readFileSync(here("assets/js/main.js"), "utf8").replace(/\r\n/g, "\n");
  assert.match(boot, /window\.top !== window\.self/);
  assert.match(html, /class="framed-note"[^>]*target="_top"/);
  assert.match(main, /const TESTING = params\.has\("rec"\) \|\| params\.has\("test"\)/);
  assert.match(main, /if \(TESTING\) \{\n  globalThis\.__rec/);
});

test("the loading bar is a real progress bar people and screen readers can follow", () => {
  assert.match(html, /role="progressbar"[^>]*aria-valuemin="0" aria-valuemax="100"/);
  assert.match(html, /data-load-pct/);
  assert.equal((html.match(/<span class="lr-petal">/g) || []).length, 7, "the ring's seven petals");
  // smooth on every device: the loader's motion is CSS opacity/transform (compositor), never SVG animation, which
  // runs on the main thread and froze while the page built the 3D
  assert.doesNotMatch(html, /<animate/);
  const css = readFileSync(here("assets/css/site.css"), "utf8");
  const ring = css.slice(css.indexOf("/* The loader:"), css.indexOf(".loader-line {"));
  for (const [, body] of ring.matchAll(/@keyframes [\w-]+ \{([\s\S]*?)\}\s*\}/g)) {
    for (const [, prop] of body.matchAll(/([\w-]+)\s*:/g)) assert.ok(prop === "opacity" || prop === "transform", `loader keyframes animate only opacity/transform, not ${prop}`);
  }
});

test("the download buttons show real sizes, and the trust line is there", () => {
  const sizes = [...html.matchAll(/<span class="size" data-file="(mac|win)">([^<]+)<\/span>/g)];
  assert.equal(sizes.length, 4, "a size on every Mac and Windows button");
  for (const [, file, text] of sizes) {
    assert.match(text, file === "mac" ? /^\d+\.\d MB$/ : /^\d+ KB$/, text);
    assert.ok(!/^0[ .]/.test(text), `${file} size was never filled in (run scripts/package-release.sh)`);
  }
  const trust = html.match(/<ul class="trust">([\s\S]*?)<\/ul>/)[1];
  assert.deepEqual([...trust.matchAll(/<li>([^<]+)<\/li>/g)].map((m) => m[1]), ["Open source", "Works without internet", "No account"]);
});

test("nothing a visitor or another site controls can reach the page's HTML (no XSS path)", () => {
  const js = ["boot.js", "main.js", "film.js", "ring.js", "haptics.js", "gl/scene.js", "gl/engine.js", "gl/paint.js", "gl/shaders.js", "gl/paint-worker.js"]
    .map((f) => [f, readFileSync(here(`assets/js/${f}`), "utf8")]);
  for (const [f, src] of js) {
    // no other ways in: the fragment, the referrer, the window name, messages from other windows (our own painting
    // worker's messages are fine: only this page can talk to it), storage, cookies
    assert.doesNotMatch(src, /location\.hash|document\.referrer|window\.name|\b(window|parent|top|opener)\.postMessage|addEventListener\(\s*["']message|window\.onmessage|document\.cookie|localStorage/, f);
    // no ways to run text as code or to write HTML other than the reviewed spots below
    assert.doesNotMatch(src, /\beval\(|new Function|document\.write|insertAdjacentHTML|outerHTML/, f);
  }
  // the only HTML written from strings: the size readout's icon (fixed SVGs) and the ring (numbers and fixed labels)
  const sinks = js.flatMap(([f, src]) => [...src.matchAll(/\.innerHTML\s*=/g)].map(() => f));
  assert.deepEqual(sinks, ["main.js", "ring.js", "ring.js", "ring.js", "ring.js"], "a new innerHTML needs a review (use textContent)");
  // the address bar only gives numbers (clamped) and one fixed word
  const main = js.find(([f]) => f === "main.js")[1];
  assert.deepEqual([...main.matchAll(/params\.get\("(\w+)"\)/g)].map((m) => m[1]), ["look"]);
  assert.match(main, /params\.get\("look"\) === "cel"/);
});

test("the GitHub workflows run only actions pinned to an exact commit", () => {
  const dir = new URL("../../.github/workflows/", import.meta.url);
  for (const f of ["site.yml", "windows.yml"]) {
    const y = readFileSync(new URL(f, dir), "utf8");
    const uses = [...y.matchAll(/uses:\s*(\S+)/g)].map((m) => m[1]);
    assert.ok(uses.length > 0, f);
    for (const u of uses) assert.match(u, /^[\w.-]+\/[\w.-]+@[0-9a-f]{40}$/, `${f}: ${u} must be pinned to a commit`);
    // nothing from an event (a PR title, a branch name) is pasted into a shell command
    for (const m of y.matchAll(/run:[^\n]*(\n\s{10,}[^\n]*)*/g)) assert.doesNotMatch(m[0], /\$\{\{\s*github\.event/, f);
  }
});

test("downloads fit the visitor: a phone gets 'send to my computer', and first-time warnings are explained", () => {
  assert.match(html, /<div class="on-phone" data-on-phone hidden>[\s\S]*?<button class="btn primary" type="button" data-share>/);
  assert.match(html, /<details class="first-open">[\s\S]*Open Anyway[\s\S]*Run anyway[\s\S]*<\/details>/);
  assert.match(html, /<link rel="apple-touch-icon" href="assets\/apple-touch-icon\.png">/);
  assert.ok(existsSync(here("assets/apple-touch-icon.png")));
  // GitHub pages open in a new tab (the film keeps its place); the downloads stay in this one
  for (const m of html.matchAll(/<a\b[^>]*href="(https:\/\/github\.com[^"]+)"[^>]*>/g)) {
    if (/\/download\//.test(m[1])) assert.doesNotMatch(m[0], /target=/, m[1]);
    else assert.match(m[0], /target="_blank" rel="noopener"/, m[1]);
  }
});
