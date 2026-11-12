#!/bin/zsh
# Signed + notarized DragShift for sharing (website, GitHub Releases). Needs the Apple Developer
# Program (one membership signs any number of apps).
#
# One-time setup:
#   1. developer.apple.com > Certificates > "+" > Developer ID Application (install it in Keychain)
#   2. An app-specific password at account.apple.com, then:
#        xcrun notarytool store-credentials dragshift-notary --apple-id <your Apple ID> --team-id <TEAMID>
# Each release:
#   DEVELOPER_ID="Developer ID Application: Your Name (TEAMID)" ./scripts/release.sh
set -euo pipefail
cd "$(dirname "$0")/.."
: "${DEVELOPER_ID:?Set DEVELOPER_ID to your 'Developer ID Application: …' certificate name}"
PROFILE="${NOTARY_PROFILE:-dragshift-notary}"

./scripts/build-app.sh
APP=build/DragShift.app
VERSION=$(/usr/libexec/PlistBuddy -c "Print CFBundleShortVersionString" "$APP/Contents/Info.plist")

codesign --force --options runtime --timestamp --entitlements Resources/DragShift.entitlements --sign "$DEVELOPER_ID" "$APP"
codesign --verify --strict --verbose=2 "$APP"

ZIP="build/DragShift-$VERSION.zip"
ditto -c -k --keepParent "$APP" "$ZIP"
xcrun notarytool submit "$ZIP" --keychain-profile "$PROFILE" --wait
xcrun stapler staple "$APP"
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"   # zip again so the stapled ticket is inside

DMG="build/DragShift-$VERSION.dmg"
rm -f "$DMG"
hdiutil create -volname DragShift -srcfolder "$APP" -ov -format UDZO "$DMG"
codesign --sign "$DEVELOPER_ID" --timestamp "$DMG"
xcrun notarytool submit "$DMG" --keychain-profile "$PROFILE" --wait
xcrun stapler staple "$DMG"

spctl --assess --type execute --verbose "$APP"
# The website's Mac button downloads releases/latest/download/DragShift.dmg; the zip stays for older links.
mkdir -p build/release && cp "$ZIP" build/release/DragShift-mac.zip && cp "$DMG" build/release/DragShift.dmg
echo "Ready to upload: build/release/DragShift.dmg and build/release/DragShift-mac.zip"
