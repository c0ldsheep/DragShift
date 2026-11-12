# Building, testing and releasing

[← Back to the front page](../README.md)

For Windows, see [WINDOWS.md](WINDOWS.md). For the website, see [WEBSITE.md](WEBSITE.md).

## Build, test, install (Mac)

Needs only the Xcode Command Line Tools.

```sh
./scripts/test.sh                          # 114 engine tests on real files (photos, PDFs, Word, video, audio, WebP)
./scripts/build-app.sh                     # universal DragShift.app in ./build (Apple Silicon + Intel)
./scripts/build-app.sh --native --install  # quick build for this Mac, copied to ~/Applications and opened
DragShift --snapshot <folder> [file]       # renders the ring, shelf, bubbles and windows to PNGs
DragShift --inbox-check                    # walks a temporary copy through its whole life (kept, let go, swept)
```

Optional, used only if already installed with Homebrew: `cwebp` (smaller lossy WebP; lossless WebP is built in) and
`ffmpeg` (MP3, FLAC, WebM).

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
