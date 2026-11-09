#!/bin/zsh
# Makes the two files the website's download buttons point at, ready to upload to a GitHub Release:
#   build/release/DragShift-mac.zip   (universal app, signed ad-hoc: macOS asks once, see the site's
#                                      "First time opening it?")
#   build/release/DragShift.exe       (Windows, built by windows/build.sh; needs `brew install dotnet`)
# The site links to releases/latest/download/<these names>, so keep the names as they are.
# For a Developer ID signed and notarized Mac build, use scripts/release.sh instead.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build/release
./scripts/build-app.sh
rm -f build/release/DragShift-mac.zip
ditto -c -k --keepParent build/DragShift.app build/release/DragShift-mac.zip
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
if os.path.exists("build/release/DragShift-mac.zip"):
    real["mac"] = f'{os.path.getsize("build/release/DragShift-mac.zip") / 1e6:.1f} MB'
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
