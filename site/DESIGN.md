# DragShift site: "The Form"

One page. A desk at 11:41 PM under a warm lamp, in real 3D (our own WebGL2, no libraries). An application form closes
at 11:59. The film moves only while you scroll: no snapping and no auto-play. The heavy parts need a little more
scroll, and the results hold for a moment, so nobody rushes past them.

## The story (`assets/js/film.js`)

| Part | Heading | What happens |
|---|---|---|
| 0 | The form closes at midnight. | Low close shot of the empty form; the clock reads 11:41 PM. |
| 1 | Rejected. | The photo (3.1 MB) slams into its box and is flung off the table: "File size must be less than 50 KB". 11:44. |
| 1b | (crop) | "So you crop it." The photo comes back to the camera, a crop frame closes in, the edges are cut off and fly away. |
| 1c | Rejected. Again. | "You cropped it. Still 1.2 MB." The cropped photo is flung off too. 11:47. Each heading appears only when its event happens. |
| 2 | Drag it. Press Shift. | The photo comes back, Shift, the ring, Under 50 KB; the size counts down to 48.6 KB and turns green; it lands. |
| 3 | Locked PDF? Unlock it. | The bank statement is thrown back ("This PDF has a password"), Shift, Edit page, Unlock, the app's real password window, return; the brass padlock springs open and falls off. |
| 4 | Three scans. One PDF. | Three scans fan in, are stapled into one PDF, land in Documents. |
| 5 | No cyber café. No queue. | A walnut stamp flies out of the screen at the form, slams (camera, words and phone shake), and leaves a round green seal: SUBMITTED, 11:53 PM. "Everything done in 2 clicks. Free forever for Mac and Windows." |

## Look ("A · Studio", his pick)

- **Lighting:** physically based. One warm lamp spot plus a cool fill, GGX highlights, and a procedural room (the
  lamp shade, a tall window, the desk) reflected in the brass, chrome, photo gloss and varnish.
- **Shadows:** soft lamp shadows from a top-down map; they sharpen as things come down.
- **Lens:** blur away from the focus; grain; the scene fades into the page under the words.
- **Colour:** the app's own palette. Page `#fff5f0`, ink `#24131c`. The buttons use the app's orange-to-pink gradient
  (`#ff8c38` → `#ff3d6e`); the big headings run from ink into pink. Red = rejected, green = done.
- **Desk:** a seamless studio backdrop (a "sweep"), peach running into pink, like product photos on coloured paper.
  Quiet enough that the form stays the hero. (Linen, cutting mat, blinds and several glass looks were tried and dropped.)
- **Labels:** pills with tinted icon tiles (a file, a lock that opens, stacked pages).
- `?look=cel` keeps the flat cel look as a dev switch only.

## Scroll (`film.js` `scrollRate` / `timeAt`)

- **Speed:** 0.2 screen heights per film second.
- **Heavier stretches:**
  - the rejections ×1.35
  - the countdown ×1.7
  - the refusal ×1.4
  - the password and padlock ×1.5
  - the seal ×2.4
  - the scans ×0.85
- **Holds:** after each "✓ Uploaded" and after the seal.
- **Feel:** the picture glides toward the scroll position (rate 7/s). Shift or the arrows jump to the next part.

## Page

- **Name:** DragShift (the app and the site). Address: https://c0ldsheep.github.io/DragShift/.
- **Header:** brand, GitHub, and Download, which jumps past the film to `#get-it`.
- **After the film:** the download section, then the footer. The download section has:
  - each button with its file's real size, as Finder or Explorer shows it; `scripts/package-release.sh` rewrites them
  - a trust line: ✓ Open source · ✓ Works without internet · ✓ No account (the apps have no network code)
  - the requirements, with the note that on Windows Unlock and Merge save the pages as pictures
- **Footer:** made by c0ldsheep, open source, no cookies, no trackers.
- **The story as words and five stills:** visually hidden for screen readers, and shown with pictures when there's no
  3D or no script.
- **Calm mode** (reduced motion): no idle camera drift, no shakes, no haptics.
- **Felt moments** (`film.js` `FEELS`, `haptics.js`): a short thud on each refusal (both photo tries and the locked
  PDF) and a light tick each time a fixed file lands (under 50 KB, unlocked, 1 PDF). Nothing on the seal: he tried
  a ~1 s buzz there and it didn't feel good. Android uses the Vibration API, and only after one tap on the page (a
  scroll doesn't count). iPhone (iOS 18+) uses a script-toggled switch: 1 tap = tick, 2 = thud. Test page:
  `?test&buzz` (Thud / Tick buttons + a line naming each buzz).
- **No outside requests:** CSP `default-src 'none'` with only this site's scripts, styles and pictures (and data:
  for the loader masks), `no-referrer`, and links only to GitHub. See `SECURITY.md` for the full audit.

## Loading

- **Smooth on every device:** each petal is its own layer, and the petals and the sheen are CSS animations of
  opacity and transform only, which the browser runs on the compositor. So the ring keeps moving while the page is
  busy building the 3D. (Before 2026-11-09 the script repainted the whole ring every frame, and it froze for 90–230 ms
  at every loading step on a Mac.) The script writes only the number, from the same curve (`boot.js` `shownAt`).
  Loading also yields after each paint (`requestAnimationFrame` + `setTimeout`), compiles shaders in parallel
  (`KHR_parallel_shader_compile`), decodes the photo off the main thread, and preloads the 3D's files.
  `page.test.js` refuses SVG animation and any loader keyframe that isn't opacity or transform.
- **Papers painted in the background:** `gl/paint-worker.js` paints every texture (the same `paint.js`, on an
  OffscreenCanvas) and sends plain pixels back. That removes the 90 ms form freeze and the notes' freezes. Where
  workers or OffscreenCanvas are missing, or a worker fails or goes quiet for 8 s, the papers are painted on the page
  as before. Checked identical: bit-for-bit in Chrome; in WebKit at most 1/255 on under 1 % of pixels (rounding at
  transparent text edges). `?test&painthere` forces the old way, to compare.
- **Instant repeat visits and offline:** the published copy has `sw.js` (written by `tools/stamp.mjs`). It keeps
  that exact version on the device, checking every file against its SHA-256 (a mismatch keeps nothing). The page
  comes from the network when that answers within 2.5 s, and from the device offline. Old versions are deleted
  once no tab uses them. It's registered after the film is ready, and only in a published copy.
  `tools/sw-check.py` checks first visit, offline, a new version, and a half-updated host.
- **The ring:** the app's own ring, drawn in SVG. Its seven petals light up in turn as loading goes on, the hub shows
  the percent, and at 100 % the hub turns green with a tick.
- **Real progress:** each heavy step reports in with its name ("Developing the photo", "Printing the
  form", "Inking the seal", "Warming up the lamp"). That's the labour illusion: visible work feels worth the wait.
- **Shape:**
  - fast and continuous to 90 %
  - a creep from 90 to 98 % that never stops (stalls near the end feel worst)
  - a quick dash to 100 % the moment it's truly ready
- **Sheen:** a soft sheen runs backwards over the lit petals (backward motion, Harrison et al. 2010: waits feel about
  11 % shorter).
- **Fallback:** the still story shows when there's no 3D, the browser takes the 3D away, or loading makes no
  progress for 9 s **of the page actually running**.
  - A hidden or covered page gets no frames, so its loading pauses; that is waiting, not being stuck. (Until
    2026-11-09 this counted clock time: Safari opened behind another window, came back after 12 s and showed the
    story although the 3D was ready a moment later.)
  - Giving up for being slow isn't final: if the 3D finishes while the visitor is still at the top, the film comes
    back. The page is never "story and film" at once.
  - If one of the page's own files fails to load, it reloads once, then shows the story.
- **One version at a time:** the published copy stamps every script and style address with a hash of the site
  (`tools/stamp.mjs`, run by the Pages workflow), so a returning visitor can't get half an old version from the
  10-minute cache. The local server (`scripts/serve-site.py`) sends `Cache-Control: no-cache`.

## Safety

- **Strict CSP:** no outside requests; links go only to GitHub and the site itself.
- **Framing:** the page refuses to run inside another site's frame (GitHub Pages can't send X-Frame-Options); it
  shows an "Open DragShift" link instead.
- **Test switches** (`?rec`, `?t`, `?scale`, `?msaa`, `?look`) work only with `?rec` or `?test`, and are clamped.
- **WebGL context loss** shows the still story and reloads when the 3D comes back.
- **No refused draws:** untextured draws bind a blank texture. Once the scene drew into the texture still bound
  from the last frame; WebGL refused those draws and the padlock and stamp vanished in Safari. `tools/overlap.py` now
  checks `gl.getError()` every frame, with MSAA on and off.
- **Shader rule:** no reversed `smoothstep` edges (undefined on Apple GPUs).

## Checks

- `node --test tests/*.test.js` (film, feel, page, stamp)
- `swift tools/webkit-check.swift http://127.0.0.1:48780`: Safari's engine, the visits that go wrong in real life:
  first visit (laptop, phone size), opened behind another window for 12 s, a very slow device (`?test&stall=10`),
  a file that fails to load. Run it before calling the site done; headless Chrome alone missed the 12 s bug.
- `tools/overlap.py`: overlapping text and WebGL errors at 4 widths, MSAA on and off
- `tools/stills.py`: the fallback stills and `og.jpg`
- `tools/rec-all.sh`: videos
- `tools/looks.py`: look comparison
- GPU cost: `__gpu(n)` at Retina and phone 2.75x (≈9.5 ms / 3.7 ms on his Mac)
