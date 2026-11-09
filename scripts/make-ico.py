#!/usr/bin/env python3
"""Packs the Mac app icon (build/AppIcon.iconset, made by build-app.sh) into windows/src/DragShift/AppIcon.ico.
PNG-in-ICO entries, which Windows Vista and newer read. Run on a Mac (uses sips for the 24 and 48 px sizes)."""
import os, struct, subprocess, sys, tempfile

root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
iconset = os.path.join(root, "build", "AppIcon.iconset")
out = os.path.join(root, "windows", "src", "DragShift", "AppIcon.ico")
if not os.path.isdir(iconset):
    sys.exit("Run ./scripts/build-app.sh first (it makes build/AppIcon.iconset).")

def png(size):
    direct = {16: "icon_16x16.png", 32: "icon_32x32.png", 64: "icon_32x32@2x.png", 128: "icon_128x128.png", 256: "icon_256x256.png"}
    if size in direct:
        return open(os.path.join(iconset, direct[size]), "rb").read()
    tmp = os.path.join(tempfile.mkdtemp(), "i.png")
    subprocess.run(["sips", "-z", str(size), str(size), os.path.join(iconset, "icon_256x256.png"), "--out", tmp],
                   check=True, capture_output=True)
    return open(tmp, "rb").read()

sizes = [16, 24, 32, 48, 64, 128, 256]
images = [png(s) for s in sizes]
data = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
for s, img in zip(sizes, images):
    data += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(img), offset)
    offset += len(img)
data += b"".join(images)
open(out, "wb").write(data)
print("wrote", out, len(data), "bytes")
