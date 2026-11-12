# Using DragShift on a Mac

[← Back to the front page](../README.md)

Every way to open the ring, and everything it can do. For Windows, see [WINDOWS.md](WINDOWS.md).

## Opening the ring

| You want to… | Do this |
|---|---|
| Open the ring | Drag a file, press **⇧ Shift**. Or hold Shift first, then grab the file. |
| Open it with the trackpad | Drag with one finger, **touch a second finger** down. |
| Open it with a mouse | Hold the file with the left button and **click the wheel** (or a side button). Settings → Triggers ticks which buttons count: Wheel, Back, Forward. Shift works with a mouse too. |
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
3.5 × 4.5 cm and stays under 50 KB. Tall hair, a turban or a hat stays in the photo: on macOS 14 or newer the top of
the hair is measured, not guessed. Settings → Saving has US visa 2 × 2 in and square sizes, other limits, and "keep
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
- **Cancel** (the × in the bubble) stops the work at once, a long video too, and leaves nothing half-made.
- A copied screenshot, or a picture dragged out of a browser, Mail or Photos, is worked on from a temporary copy.
  The copy is deleted when the action ends. Copy, Copy Path and sharing save it in Downloads first, so the file
  still works afterwards.
- Text it reads (OCR, PDF text, QR codes) goes on the clipboard.
- A light trackpad tick on each slice; a soft sound when done. Light and dark mode.
- Idle cost: four tiny checks a second, batched by macOS. It only works hard while a mouse button is down.

Problems? Menu bar icon → Settings → About → **Open Log** (`~/Library/Logs/DragShift.log`).
