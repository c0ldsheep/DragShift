# DragShift

Change any file while you're dragging it. Start dragging a file, press **Shift** (or touch **a second finger**
down on the trackpad), and a ring opens right under the file. Push the file toward a slice and let go, or let go in
the middle: the ring stays open, so you can lift your fingers, glide to a slice and tap it.

Everything runs on your computer. No internet, no account, no tracking.
**Mac:** macOS 13 Ventura or newer, Apple Silicon and Intel. **Windows:** Windows 10 or 11, one small `.exe`
(see [Windows](#windows) below).

## Using it

| You want to… | Do this |
|---|---|
| Open the ring | Drag a file, press **⇧ Shift**. Or hold Shift first, then grab the file. |
| Open it with the trackpad | Drag with one finger, **touch a second finger** down. |
| Open it with a mouse | Hold the file with the left button and **click the wheel** (or a side button). Shift works with a mouse too. |
| Pick a slice | Push the file *toward* it and let go. Direction is enough; you don't have to land on it. |
| Pick calmly | Let go in the **middle**: the ring stays open. Glide to a slice, tap it. Esc or a click elsewhere closes it. |
| More slices | Press **Shift** again, tap the extra finger again, or hover a page name (★ Faves · Convert · Edit · More · Files). |
| Do the last thing again | **Double-tap** Shift (or the extra finger) while dragging, then let go anywhere. |
| Your usual | The slice you pick most for that kind of file has a **soft pulsing edge** when the ring opens (after three picks in a row; a new habit takes over within a few picks). If it's on another page, that page's name glows. Off in Settings → General. |
| Print | The ring's **Print** page: drop the file on a preset and it prints, no window. Or Files page → **Print**: a short list of your presets opens; click one (or Return) and it prints; **More options…** opens everything else. ★ a preset to put it in Favorites too. Double-tap Shift prints it again the same way. |
| Favorites without any key | Push the dragged file against the **right edge** of the screen: your favorites slide out. Drop on one. |
| Add/remove a favorite | Right-click a slice when the ring is open (or Settings → Favorites). |
| Clipboard | **⌃⌥V** opens whatever you copied (Finder files, a screenshot, a link, text) in the ring. |
| Without dragging | Menu bar icon → **Convert Files…**, Finder → right-click → **Open With → DragShift**, or **Quick Actions → Open in DragShift Ring**. |

Optional triggers (Settings → Triggers): shake the file, or hold it still. A resting palm is ignored.

## What it can do

**Images** (JPG, PNG, HEIC, WebP, AVIF, TIFF, GIF, BMP, RAW, PSD…)
- Convert: JPG · PNG · HEIC · WebP · AVIF · PDF · TIFF · TEXT (reads the words in the picture)
- Edit: Crop · Annotate (arrows, boxes, highlighter, pen, text, numbered steps) · Resize · Rotate · Cut Out (removes the
  background) · Add BG · Enhance · Compress
- More: **Passport** (3.5 × 4.5 cm on white, under 50 KB) · **Under KB** (20 · 50 · 100 · 200 · 500 KB · 1 MB or any
  size) · Shop Photo (white square for Amazon/Flipkart) · Redact (finds emails, phones, UPI, Aadhaar, PAN, cards, faces) ·
  Clean Info (removes location) · Watermark · Read QR · Translate · Make GIF (several pictures) ·
  **Stitch** (several pictures become one long JPG, top to bottom)

**Under ___ KB** (pictures and PDFs, for exam, job and government portals): the sharpest file that fits. It tries the
best JPG quality first and only makes the picture smaller when it has to, aiming just under the limit (1 KB = 1,000
bytes, so it passes on sites that count 1,024 too, and doesn't fall below their minimums). PDFs are first compressed
normally; if that's not enough, pages are redrawn as pictures (the bubble says so). Pin a size you use a lot to your
favourites or put it in a recipe.

**Passport photo**: finds the face, frames it the way passport rules ask (head about 72% of the height, a little room
above the hair), puts the person on white (or light blue), and saves a 300 dpi JPG that prints at exactly
3.5 × 4.5 cm and stays under 50 KB. Settings → Saving has US visa 2 × 2 in and square sizes, other limits, and "keep
the background".

**PDF**: to JPG/PNG pages, Text, Word, RTF, HTML, Markdown · Compress · **Under KB** · **Searchable** (adds hidden text to scans) ·
Split · Merge · Rotate · Lock · Unlock.

**Documents** (Word, RTF, ODT, HTML, TXT, Markdown): to PDF, Word, RTF, Text, HTML, Markdown, ODT, DOC · **QR Code**.

**Video**: MP4 · MOV · GIF · HEVC · M4A · MP3 · WAV · WebM · Compress · Trim · **9:16** · **1:1** · **Speed 2×** · Mute · Still.

**Audio**: M4A · MP3 · WAV · AIFF · FLAC · CAF · Trim · Compress.

**Folders**: Zip · every image → JPG/PNG · images → one PDF · shrink every image · Make GIF. **Zips**: Extract.

**Files page (everything)**: Copy · AirDrop · Mail · Messages · Zip · **Rename** (numbers, dates, find/replace, case) ·
Copy Path · Share…. For pictures, PDFs and documents, **Print** takes Messages' place (Share… still reaches Messages).

**Print**: your presets are a page of the ring (drop a picture, PDF or document on one and it prints), and the Print
window opens as a short list of them (the last one used is highlighted and marked); click one, or use the arrow keys
and Return, and it prints with no dialog. The presets you saved in the macOS print window (Presets → Save Current
Settings as Preset…) are there too, and print exactly as macOS saved them: pages per sheet, orientation, quality,
paper type. Too many? **Choose…** ticks which ones show. **More options…** grows the window into the full view only
when you want it: printer, paper, copies, colour or black & white, two-sided, how pictures sit (Fit · Fill · Actual
size) and a preview of the first sheet ("System dialog…" there opens macOS's own, for page ranges, trays or Save as PDF). Built-in presets: Everyday, Black &
white (two-sided), **Passport 4×6** (eight 3.5 × 4.5 cm photos on one 4 × 6 sheet, with cut lines) and Passport A4
(thirty). Edit them, add your own, or ★ one in Settings → Printing to drop files straight on it from the ring.
Documents become PDFs first. Black & white pages are made grey before they're sent.

**Recipes** (one drop, several steps; Settings → Recipes): Shop Ready, Insta Post 4:5, Insta Story 9:16, WhatsApp Small,
Web Image, Watermark, Email PDF, or your own.

**Watch Folders** (Settings → Watch): e.g. every HEIC that lands in Downloads becomes a JPG by itself.

## Easy by default

- Originals are never changed (except Rename, which you can undo). New files are named like Finder does:
  `Page 001.jpg`, `Page 001 Cropped.jpg`, `Page 001 2.jpg`.
- A bubble shows progress, then **Show** and **Undo**. Compress tells you what it saved and never keeps a bigger file.
- Text it reads (OCR, PDF text, QR codes) goes on the clipboard.
- A light trackpad tick on each slice; a soft sound when done. Light and dark mode.
- Idle cost: four tiny checks a second, batched by macOS. It only works hard while a mouse button is down.

## Build, test, install

Needs only the Xcode Command Line Tools.

```sh
./scripts/test.sh                          # 81 engine tests on real files (photos, PDFs, Word, video, audio, WebP)
./scripts/build-app.sh                     # universal DragShift.app in ./build (Apple Silicon + Intel)
./scripts/build-app.sh --native --install  # quick build for this Mac, copied to ~/Applications and opened
DragShift --snapshot <folder> [file]       # renders the ring, shelf, bubbles and windows to PNGs
```

Optional, used only if already installed with Homebrew: `cwebp` (smaller lossy WebP; lossless WebP is built in) and
`ffmpeg` (MP3, FLAC, WebM).

Problems? Menu bar icon → Settings → About → **Open Log** (`~/Library/Logs/DragShift.log`).

**Check it on any Mac** (Intel, Apple silicon, MacBook, iMac, Mac mini, Mac Studio): every slice the ring offers runs on
realistic files it makes itself, and each result is opened and checked. The report is `selftest.txt` in a new folder.

```sh
/Applications/DragShift.app/Contents/MacOS/DragShift --selftest ~/Desktop [a photo of yours]
arch -x86_64 /Applications/DragShift.app/Contents/MacOS/DragShift --selftest ~/Desktop   # as Intel, on Apple silicon
```

## Signing and sharing

Every build is already signed the way a release must be (hardened runtime, no special entitlements), so notarization
won't change how it behaves. With an Apple Developer account:

```sh
xcrun notarytool store-credentials dragshift-notary --apple-id <Apple ID> --team-id <TEAMID>   # once
DEVELOPER_ID="Developer ID Application: <Name> (<TEAMID>)" ./scripts/release.sh                 # each release
```

That makes a notarized `.zip` and `.dmg` in `build/`, ready for GitHub Releases or a website. DragShift is meant for
direct download, not the Mac App Store: the App Store doesn't allow reading the trackpad's finger count or watching
drags in other apps.

Before there's a Developer ID, `./scripts/package-release.sh` makes the two files the website's buttons download:
`build/release/DragShift-mac.zip` (universal, signed ad-hoc, so macOS asks once) and `build/release/DragShift.exe`.
Upload both to a GitHub Release with exactly those names; the site links to `releases/latest/download/<name>`.

## Website

`site/` is DragShift's one-page site, "The Form": a short 3D film you scroll through. A government form on a desk,
closing at midnight. The photo is too big and gets rejected, you crop it and it's rejected again, then DragShift does
it in one drag: the photo goes under 50 KB, a locked bank PDF gets unlocked, three scans become one PDF, and a round
green SUBMITTED seal comes down from your side of the screen (phones give a short buzz). The film only moves while
you scroll, a little heavier at the moments that matter; Shift or the arrow keys jump to the next part. The last
part has the downloads.

- Our own code, no libraries and no downloads: the 3D is our own WebGL2 renderer (soft shadows, depth of field, a
  peach-to-pink studio backdrop), the papers are drawn in code, and the fonts are the system's. The page makes no
  requests after it loads (its security policy forbids them), uses no cookies and no trackers.
- Without WebGL or JavaScript, the page shows the same story as five still pictures.
- `./scripts/serve-site.py`, then open http://127.0.0.1:8000 (`./scripts/serve-site.py site 48780 --lan` also serves
  your phone on the same Wi-Fi). It always serves the newest files. ES modules don't run from `file://`.
- `cd site && node --test tests/*.test.js`: the film's timeline, words, scroll weights, the felt moments, the page's links and
  safety rules, and the version stamping.
- `cd site && swift tools/webkit-check.swift http://127.0.0.1:8000`: the page in Safari's engine, including a visit
  that opens behind another window, a very slow device and a file that fails to load.
- Test switches (only with `?test` or `?rec`): `&t=8.1` holds the film at one moment, `?rec` lets a recorder step it
  frame by frame. `site/tools/` has the recorder, the still-picture maker and an overlap checker for 4 screen sizes.
- `.github/workflows/site.yml` tests it, stamps every script address with the version (`site/tools/stamp.mjs`, so
  nobody runs half an old version from the cache) and publishes it with GitHub Pages on every push that touches `site/`
  (one-time: repo Settings → Pages → Source: GitHub Actions). The address is https://c0ldsheep.github.io/DragShift/.
- `site/DESIGN.md` has the story, the copy, the colours and the rules (no fake numbers, no urgency tricks).

## Windows

`windows/` is the same app for Windows 10 and 11: the same ring (same sizes and look), favourites shelf, recipes,
repeat, "your usual" glow, clipboard shortcut (**Ctrl+Alt+V**), Under KB and Passport. It's one ~456 KB `DragShift.exe` built on
.NET Framework 4.8, which every Windows 10 and 11 PC already has, so there's nothing to install. It never asks for
admin rights.

| You want to… | Do this |
|---|---|
| Open the ring | Drag a file, press **Shift**. Or, with a mouse, hold the file with the left button and click the **right button**. The wheel click and the two side buttons work too; Settings → Triggers lets you tick the ones you want (and leave the others to the app you're dragging from). |
| Touchpad | Drag with one finger, touch a second one down (Precision Touchpads: most laptops since 2016). |
| Pick | Push toward a slice and let go, or let go in the middle and click. Esc closes it. |
| Favourites | Push the dragged file against the right edge of the screen. Right-click a slice to add one. The last tile there is a **gear**: drop the file on it to open Settings. |
| Settings | Open **DragShift** again (double-click `DragShift.exe`), or the tray icon → Settings…. |
| Without dragging | Explorer → right-click → **Send to → DragShift**, the tray icon → Convert Files…, or Ctrl+Alt+V. |

What's in it on Windows: pictures to JPG · PNG · WebP · HEIC (with the free Store extension) · TIFF · BMP · PDF ·
Text (Windows' own OCR) · Under KB · Passport · Compress · Resize · Rotate · Flip · Watermark · Stitch · **Print** (same presets,
Print page, Choose… and passport sheets as the Mac; pictures and PDFs. Windows keeps no shared list of saved printer
presets the way macOS does, so the list is DragShift's own); PDF to JPG/PNG pages ·
Text · Under KB · Compress; video and audio to MP4 · M4A · MP3 · WAV · Compress · Mute · Still; folders to Zip ·
All → JPG · Images → PDF · Shrink All; zips Extract; Copy · Share (Windows' Share window) · Copy Path · Show.
Not on Windows yet: Word/RTF documents; PDF merge, split, rotate, lock, unlock and searchable; Crop, Cut Out, Add BG,
Enhance, Shop Photo, Clean Info, Redact, Annotate and Make GIF; Translate and QR codes; Rename; watch folders; video
trimming, speed and reel crops. On Windows the passport background is changed when it's a plain wall (there's
no built-in "lift the subject" like on the Mac); a busy background is kept and the bubble says so.

```sh
./windows/build.sh                  # tests the shared logic, then builds build/windows/DragShift.exe (macOS, Linux or Windows)
windows\build.cmd                   # the same on Windows
DragShift.exe --selftest <folder>   # on a Windows PC: runs every engine step on made-up files and draws the ring to PNGs
```

Building needs the .NET SDK (`brew install dotnet` on a Mac, `winget install Microsoft.DotNet.SDK.10` on Windows); the
Windows APIs come from Microsoft's `Microsoft.Windows.SDK.Contracts` reference package, and nothing extra ships with
the .exe. Every push to GitHub that touches `windows/` runs `.github/workflows/windows.yml` on a real Windows machine:
the tests, the build and the self-test, and the run page keeps `DragShift.exe` plus pictures of the ring to download.

Unsigned `.exe` files get a SmartScreen warning ("Windows protected your PC" → More info → Run anyway) until they
are code-signed or have been downloaded by enough people.

How it sees drags on Windows: low-level mouse and keyboard hooks notice "left button held + moved" and Shift or a
second mouse button (right, wheel or side, as ticked in Settings; it takes only those, and none while a full-screen game
or video is in front). The ring then appears, almost invisible, under the pointer; Windows only hands it a file list when a file
is really being dragged, so text selections and window moves never open it. While a ring is pinned it never takes
the keyboard focus from the app you're in. Code: `windows/src/Core` (shared logic, tested with `windows/tests`),
`windows/src/DragShift` (`DragTracker`, `Touchpad`, `RingForm` + `RingPainter`, `ShelfForm`, `Hud`, `AppMain`,
`Engine/`).

## How it works (for whoever edits the code)

- `Sources/DragShiftCore`: all file work, no UI. `Catalog.swift` decides the slices and favourites per file type,
  `Engine.swift` runs them (blocking work goes to its own queue), `Recipe.swift` chains steps, `WebP.swift` is the
  built-in lossless encoder. Tests are in `Tests/DragShiftCoreTests`.
- `Sources/DragShift/DragWatcher.swift`: sees drags anywhere without special permissions. Every drag writes to the
  system drag pasteboard, so while the mouse button is down it watches that, the Shift key and the pointer.
- `TouchMonitor.swift`: finger count from macOS's MultitouchSupport framework (read-only), with palm rejection.
- `RingController.swift` / `RingView.swift`: the floating ring; picks slices by direction from the centre.
- `FavoritesShelf.swift`: the edge shelf. It sits on the side edge, not the top: dragging a file to the top edge
  opens Mission Control.
- `HotKey.swift`: the clipboard shortcut, via Carbon hot keys (no Accessibility permission needed).
- `FolderWatcher.swift`: watch folders through kernel folder-change events (no polling).
- `@State` is written `@ViewState`: in the macOS 27 SDK `@State` is a macro whose plugin ships only with full Xcode.

## Buy me a coffee

DragShift is free. If it saves you time, the app's About tab has a coffee cup: drag it, press Shift, and pick a size.
Or send any amount of ETH or USDC to `0x81fE1d0b00B3e6100E5e40E13982A3644e46e09A` (Ethereum; the same address
works on Base, Arbitrum and Polygon). Nothing in the app is locked or unlocked by a tip.

## Licence

MIT, see [LICENSE](LICENSE). Made by c0ldsheep. The licence covers the code. The DragShift name and icon aren't
part of it: if you publish your own build, give it your own name and icon, so nobody mistakes it for the official
download.
