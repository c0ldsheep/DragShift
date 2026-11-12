<p align="center">
  <img src="site/assets/apple-touch-icon.png" width="84" alt="DragShift icon">
</p>

<h1 align="center">Wrong format? File too big? PDF locked?<br>Fix it in one drag.</h1>

<p align="center">
  <b>DragShift is a free file converter for Mac and Windows.</b><br>
  Drag a file, press <b>Shift</b>, drop it on what you need. A new file appears next to the old one.
</p>

<p align="center">
  <a href="https://github.com/c0ldsheep/DragShift/releases/latest/download/DragShift.dmg"><b>Download for Mac</b></a>
  &nbsp;·&nbsp;
  <a href="https://github.com/c0ldsheep/DragShift/releases/latest/download/DragShift.exe"><b>Download for Windows</b></a>
  &nbsp;·&nbsp;
  <a href="https://c0ldsheep.github.io/DragShift/"><b>Watch it work</b></a>
</p>

<p align="center">
  Free forever &nbsp;·&nbsp; No uploads &nbsp;·&nbsp; No account &nbsp;·&nbsp; No ads &nbsp;·&nbsp; Open source
</p>

<p align="center">
  <img src="docs/img/ring-convert.jpg" width="400" alt="The DragShift ring open under a HEIC photo, with slices for JPG, WebP, AVIF, TIFF, PDF and Text">
  <img src="docs/img/ring-more-dark.jpg" width="400" alt="The DragShift ring in dark mode, with slices for Passport photo, Under KB, Shop Photo, Redact, Clean Info, Watermark, Read QR and Translate">
</p>

## You have been here

It is 11:40 at night. The form closes at midnight... and it just said *"photo must be under 50 KB"*.

So you search for a converter. You upload your ID to a website you have never heard of. You wait. You close three
ads. And the file still comes back wrong.

I noticed that nobody really wants a "file converter". They want the form to say yes.

So I made DragShift. One drag, and the file is the right type, the right size, or unlocked. Your original is never
touched. Nothing leaves your computer. And there is no price... not now, not later.

## What it fixes

| Your problem | Drop the file on | Mac | Windows |
|---|---|:-:|:-:|
| The form says "photo must be under 20 KB, 50 KB or 100 KB" | **Under KB** (20 KB to 1 MB, or any size) | ✓ | ✓ |
| You need a passport size photo | **Passport** (3.5 × 4.5 cm on white, ready to print) | ✓ | ✓ |
| An iPhone photo (HEIC) won't open or upload | **JPG** or **PNG** | ✓ | ✓ |
| The PDF is too big to email | **Compress** | ✓ | ✓ |
| The site wants a PDF and you have a picture | **PDF** | ✓ | ✓ |
| The site wants MP4 and you have MOV | **MP4** | ✓ | ✓ |
| You need the words out of a picture or a scan | **Text** | ✓ | ✓ |
| The bank PDF asks for a password every time | **Unlock** | ✓ | ✓ |
| Many PDFs, one upload box | **Merge** | ✓ | ✓ |
| A phone number or ID number shows in a screenshot | **Redact** | ✓ | ✓ |

It also resizes, rotates, watermarks, zips and prints. On a Mac it crops, removes backgrounds, trims video and makes
GIFs too.

Two honest notes for Windows: HEIC photos need Microsoft's free HEIF extension from the Store, and Merge and Unlock
save the pages as pictures, so their text can't be selected.

**[Download for Mac](https://github.com/c0ldsheep/DragShift/releases/latest/download/DragShift.dmg)** ·
**[Download for Windows](https://github.com/c0ldsheep/DragShift/releases/latest/download/DragShift.exe)**

## How it works

1. **Start dragging a file**, from a folder, the Desktop, a browser or Photos.
2. **Press Shift.** A ring opens under the file.
3. **Drop the file on a slice.** Done.

No Shift key under your finger? On a Mac, touch a second finger on the trackpad or click the mouse wheel. On Windows,
click the right mouse button or touch a second finger on the touchpad.

A small bubble tells you what was saved, with **Show** and **Undo**. Changed your mind? Undo removes the new file.
The old one was never touched in the first place.

<p align="center">
  <img src="docs/img/bubble-done.png" width="360" alt="DragShift's bubble after compressing a picture: Saved Page 001 Compressed.jpg, 1.2 MB to 310 KB, minus 74 percent, with Show and Undo buttons">
</p>

## Why not a converter website, or a paid app?

Because your ID photo and your bank statement are the last things that should travel to a stranger's server.

| | Converter websites | Paid converter apps | **DragShift** |
|---|---|---|---|
| Where your file goes | Uploaded to their server | Depends on the app | **Stays on your computer** |
| Price | Free with limits, or a monthly plan | A price or a subscription | **Free, all of it** |
| Ads, queues, daily limits | Common | Rare | **None** |
| Works with no internet | No | Depends on the app | **Yes** |
| Steps | Search, upload, wait, download, find the file | Open the app, import, export | **One drag** |
| Your original file | A copy goes to their server | Depends on the app | **Never changed, never sent** |
| You can read the code | Rarely | Rarely | **Yes, all of it is on this page** |

One more thing a website can't do: it can't be there when the internet isn't.

## Questions people ask before they download

### Is it really free? What's the catch?

There is no catch. No paid version, no trial, no locked tools, no "3 files a day". There is a coffee cup in the About
tab for anyone who wants to tip, and a tip unlocks nothing, because nothing is locked.

### Does it upload my files anywhere?

No. DragShift has no internet code at all. Switch off the Wi-Fi and it works exactly the same. Reading text and
finding faces are done by tools already built into your Mac or your PC.

### Will it change or delete my original file?

Never. Every tool makes a **new** file next to the old one, with a clear name such as `Photo Compressed.jpg` or
`Statement Unlocked.pdf`. If a file with that name is already there, DragShift picks another name. It does not replace
anything.

### Why does my computer warn me the first time I open it?

Apple and Microsoft show a warning for every new app that has not gone through their paid signing programmes yet.
DragShift has not, for now. The warning is about that, not about something found inside the file.

- **Mac:** open System Settings → Privacy & Security, scroll down and click **Open Anyway**. Once.
- **Windows:** click **More info**, then **Run anyway**. Once.

If you'd rather not trust a stranger... good. That is why the whole code is public, and why the release page shows a
SHA-256 fingerprint beside each file.

### Do I have to install anything?

- **Mac:** open the `.dmg` and drag DragShift onto the Applications folder beside it. That's all.
- **Windows:** it is one small `.exe`, under 1 MB. Nothing to install and no admin rights. Double-click and it runs.

### Will it slow my computer down?

It is a small app. It waits in the menu bar on a Mac, or in the tray on Windows, and works only when you ask it to.

### What does it need?

- **Mac:** macOS 13 Ventura or newer. Apple silicon and Intel.
- **Windows:** Windows 10 or 11. It uses .NET Framework 4.8, which Windows 10 and 11 already have.

### Is the Windows version the same as the Mac one?

The same ring, the same look, and every fix in the table above. The Mac has a few more tools for now, such as Crop,
Translate, QR codes and watch folders. The honest list is in [DragShift on Windows](docs/WINDOWS.md).

### How do I remove it?

Quit it and delete it. The few small things it keeps on your computer (your settings and a small log) are listed in
[PRIVACY.md](PRIVACY.md).

### I found a bug, or I need a tool that isn't there.

[Open an issue](https://github.com/c0ldsheep/DragShift/issues) and say what you dragged and what happened.

## Download

**Mac** (macOS 13 or newer, Apple silicon and Intel): **[DragShift.dmg](https://github.com/c0ldsheep/DragShift/releases/latest/download/DragShift.dmg)**

Open it, drag DragShift onto the Applications folder beside it, then open it from Applications.

**Windows** (10 and 11): **[DragShift.exe](https://github.com/c0ldsheep/DragShift/releases/latest/download/DragShift.exe)**

One small file. Put it anywhere and double-click it.

Older versions, and the fingerprint of every file, are on the [releases page](https://github.com/c0ldsheep/DragShift/releases).

## Everything it can do

| File | Tools |
|---|---|
| **Pictures** | Convert to JPG, PNG, HEIC, WebP, AVIF, TIFF, PDF · Compress · Under KB · Passport photo · Crop · Resize · Rotate · Remove background · Redact · Watermark · Read text · Read QR · Stitch into one long picture |
| **PDF** | To JPG or PNG pages · To Word or Text · Compress · Under KB · Merge · Split · Rotate · Lock · Unlock · Make a scan searchable |
| **Documents** | Word, RTF, ODT, HTML, Markdown and Text, to each other and to PDF · QR code |
| **Video and audio** | MP4, MOV, HEVC, GIF, MP3, M4A, WAV · Compress · Trim · 9:16 and 1:1 · Speed 2× · Mute |
| **Folders and zips** | Zip · Extract · Every picture to JPG · Pictures into one PDF |
| **Anything** | Print with one drop · Rename many files · Copy · AirDrop · Share |

This is the Mac list. Windows has the main ones, and [its own page](docs/WINDOWS.md) says which.

Also: **favourites** on the edge of the screen, **recipes** (several steps in one drop), **watch folders** (every
HEIC that lands in Downloads becomes a JPG by itself) and a **clipboard shortcut** for screenshots.

So if you were searching for a HEIC to JPG converter, a PDF compressor, a way to make a photo under 50 KB, a passport
size photo maker, a PDF merger, a PDF password remover, a JPG to PDF or MOV to MP4 converter, or a tool to copy text
from a picture... it is the same small app, and it works offline.

## Private by design

- Your files stay on your computer. Nothing is uploaded, ever.
- No account, no ads, no tracking, no cookies.
- A password you type to unlock a PDF is used once, in memory. It is never saved or logged.
- Open source under the MIT licence, so anyone can check all of this.
- GitHub builds and tests the Windows app and the website each time they change. The green ticks are on the
  [Actions tab](https://github.com/c0ldsheep/DragShift/actions). The Mac app's tests are in [`Tests/`](Tests/).

More in [PRIVACY.md](PRIVACY.md) and [SECURITY.md](SECURITY.md).

## The website

**https://c0ldsheep.github.io/DragShift/** is a short 3D film you scroll through. A form closes at midnight. The
photo is too big and gets rejected. Then DragShift does it in one drag, and the form is submitted with minutes to spare.

It is our own code with no libraries, no cookies and no trackers, and it works offline after the first visit.
How it is made, tested and published: [docs/WEBSITE.md](docs/WEBSITE.md).

## Guides

- [Using it on a Mac](docs/USING.md): every way to open the ring and every tool
- [DragShift on Windows](docs/WINDOWS.md): what's in it, and how it differs
- [The website](docs/WEBSITE.md): the story, the code, the tests
- [Building, testing and releasing](docs/BUILDING.md): for people who edit the code

## Buy me a coffee

DragShift is free. If it saves you time, the app's About tab has a coffee cup: drag it, press Shift, and pick a size.
Or send any amount of ETH or USDC to `0x81fE1d0b00B3e6100E5e40E13982A3644e46e09A` (Ethereum; the same address
works on Base, Arbitrum and Polygon). Nothing in the app is locked or unlocked by a tip.

If a tip is too much, a star on this page costs nothing, and it helps the next person with a rejected file find it.

## Licence

MIT, see [LICENSE](LICENSE). Made by c0ldsheep. The licence covers the code. The DragShift name and icon aren't
part of it: if you publish your own build, give it your own name and icon, so nobody mistakes it for the official
download.

---

<p align="center">
  <b>The next form will ask for something. Be ready.</b><br><br>
  <a href="https://github.com/c0ldsheep/DragShift/releases/latest/download/DragShift.dmg"><b>Download for Mac</b></a>
  &nbsp;·&nbsp;
  <a href="https://github.com/c0ldsheep/DragShift/releases/latest/download/DragShift.exe"><b>Download for Windows</b></a>
</p>
