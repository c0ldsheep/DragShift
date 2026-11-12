# DragShift on Windows

[← Back to the front page](../README.md)

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
