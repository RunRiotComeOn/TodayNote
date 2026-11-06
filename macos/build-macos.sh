#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
app="build/TodayNote.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
for arch in arm64 x86_64; do
  xcrun clang -arch "$arch" -mmacosx-version-min=11.0 -fobjc-arc -fblocks -O2 \
    -framework Cocoa -framework WebKit src/Native.m -o "build/TodayNote-$arch"
done
xcrun lipo -create build/TodayNote-arm64 build/TodayNote-x86_64 -output "$app/Contents/MacOS/TodayNote"
cp src/Info.plist "$app/Contents/Info.plist"
cp src/index.html src/style.css src/core.js src/ui.js src/TodayNote.icns "$app/Contents/Resources/"
chmod 755 "$app/Contents/MacOS/TodayNote"
codesign --force --deep --sign - "$app"
printf 'Built %s\n' "$app"
