#!/bin/zsh
# Makes the two files the website's download buttons point at, ready to upload to a GitHub Release:
#   build/release/DragShift.dmg       (the Mac button: open it, drag the app onto Applications. Universal app,
#                                      signed ad-hoc: macOS asks once, see the site's "First time opening it?")
#   build/release/DragShift-mac.zip   (the same app as a zip, for links made before the disk image existed)
#   build/release/DragShift.exe       (Windows, built by windows/build.sh; needs `brew install dotnet`)
# The site links to releases/latest/download/<these names>, so keep the names as they are.
# For a Developer ID signed and notarized Mac build, use scripts/release.sh instead.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build/release
./scripts/build-app.sh
rm -f build/release/DragShift-mac.zip
ditto -c -k --keepParent build/DragShift.app build/release/DragShift-mac.zip
# The disk image: the app beside a shortcut to Applications, so installing is one drag.
STAGE="$(mktemp -d)"
ditto build/DragShift.app "$STAGE/DragShift.app"
ln -s /Applications "$STAGE/Applications"
rm -f build/release/DragShift.dmg
hdiutil create -volname DragShift -srcfolder "$STAGE" -ov -format UDZO build/release/DragShift.dmg >/dev/null
rm -rf "$STAGE"
if command -v dotnet >/dev/null || [[ -x /opt/homebrew/bin/dotnet ]]; then
  ./windows/build.sh
  cp build/windows/DragShift.exe build/release/DragShift.exe
else
  echo "No dotnet here: skipping DragShift.exe (GitHub's Windows check builds it too)."
fi
ls -lh build/release

# The site shows each file's size on its download button, the way Finder (Mac) and Explorer (Windows) show it.
# Keep it true: write the real sizes into site/index.html (then commit that change with the release).
python3 - <<'PY'
import math, os, re
site, real = "site/index.html", {}
if os.path.exists("build/release/DragShift.dmg"):
    real["mac"] = f'{os.path.getsize("build/release/DragShift.dmg") / 1e6:.1f} MB'
if os.path.exists("build/release/DragShift.exe"):
    real["win"] = f'{math.ceil(os.path.getsize("build/release/DragShift.exe") / 1024)} KB'
html = open(site).read()
new = html
for k, v in real.items():
    new = re.sub(rf'(data-file="{k}">)[^<]*<', rf"\g<1>{v}<", new)
if new != html:
    open(site, "w").write(new)
    print("Updated the sizes on the site's download buttons:", real)
else:
    print("The site's download sizes are already right:", real)
PY
