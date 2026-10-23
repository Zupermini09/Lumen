#!/usr/bin/env bash
# Wraps a self-contained osx publish folder into Lumen.app and a drag-to-Applications .dmg.
# Usage (from the repository root): packaging/macos/build-dmg.sh <version> <arm64|x64> <publish-dir> <output-dir>
# The app is ad-hoc signed (required to run on Apple Silicon). Without a Developer ID + notarization, users must
# right-click > Open the first time (see README).
set -euo pipefail

version="$1"
arch="$2"
publish_dir="$3"
out_dir="$(mkdir -p "$4" && cd "$4" && pwd)"

# CFBundleVersion must be numeric (1.2.3); drop any pre-release suffix like -ci.42.
bundle_version="${version%%-*}"

staging="$(mktemp -d)"
app="$staging/Lumen.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish_dir/." "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/Lumen"
sed "s/@VERSION@/$bundle_version/g" packaging/macos/Info.plist > "$app/Contents/Info.plist"
cp packaging/assets/lumen.icns "$app/Contents/Resources/lumen.icns"

# Downloaded archives can carry quarantine/resource-fork attributes that break signing.
xattr -cr "$app"
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"

ln -s /Applications "$staging/Applications"
dmg="$out_dir/Lumen-$version-macos-$arch.dmg"
rm -f "$dmg"
hdiutil create -volname "Lumen" -srcfolder "$staging" -ov -format UDZO "$dmg"

# Keep the .app next to the dmg for the smoke test.
rm -rf "$out_dir/Lumen.app"
cp -R "$app" "$out_dir/Lumen.app"
echo "Built $dmg"
