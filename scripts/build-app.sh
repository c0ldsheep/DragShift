#!/bin/zsh
# Builds DragShift.app into ./build: one app that runs on Apple Silicon and Intel Macs, macOS 13+.
#   ./scripts/build-app.sh            universal build
#   ./scripts/build-app.sh --native   this Mac's chip only (faster while developing)
#   ./scripts/build-app.sh --install  also copy to ~/Applications and open it
set -euo pipefail
cd "$(dirname "$0")/.."

APP=build/DragShift.app
ARCHS=(arm64 x86_64)
[[ " $* " == *" --native "* ]] && ARCHS=($(uname -m))

mkdir -p build
SLICES=()
for arch in $ARCHS; do
  triple="$arch-apple-macosx13.0"
  swift build -c release --product DragShift --triple "$triple"
  cp "$(swift build -c release --triple "$triple" --show-bin-path)/DragShift" "build/DragShift-$arch"
  SLICES+=("build/DragShift-$arch")
done

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
lipo -create $SLICES -output "$APP/Contents/MacOS/DragShift"
cp Resources/Info.plist "$APP/Contents/Info.plist"

if [[ ! -f build/AppIcon.icns || scripts/make-icon.swift -nt build/AppIcon.icns ]]; then
  rm -rf build/AppIcon.iconset
  swift scripts/make-icon.swift build/AppIcon.iconset >/dev/null
  iconutil -c icns build/AppIcon.iconset -o build/AppIcon.icns
fi
cp build/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

# Leave out the debugging notes: they hold the full paths of the folders this was built in, which nobody who downloads
# the app needs to see.
strip -S "$APP/Contents/MacOS/DragShift" 2>/dev/null

# Ad-hoc signature with the hardened runtime on, exactly like a notarized release
# (scripts/release.sh re-signs with a Developer ID). If it runs like this, notarization won't break it.
codesign --force --options runtime --entitlements Resources/DragShift.entitlements --sign - --timestamp=none "$APP"
echo "Built $APP ($(lipo -archs "$APP/Contents/MacOS/DragShift"))"

if [[ " $* " == *" --install "* ]]; then
  DEST="$HOME/Applications"
  mkdir -p "$DEST"
  pkill -x DragShift 2>/dev/null || true
  sleep 0.5
  rm -rf "$DEST/DragShift.app"
  cp -R "$APP" "$DEST/"
  /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$DEST/DragShift.app"
  open "$DEST/DragShift.app"
  echo "Installed to $DEST/DragShift.app and launched"
fi
