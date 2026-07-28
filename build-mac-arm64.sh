#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
dotnet restore CVPlus.Mac/CVPlus.Mac.csproj
dotnet publish CVPlus.Mac/CVPlus.Mac.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=false -o publish/osx-arm64
APP="dist/CV+ Compilatore Alunno.app"
rm -rf dist && mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R publish/osx-arm64/* "$APP/Contents/MacOS/"
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>CV+ Compilatore Alunno</string>
<key>CFBundleDisplayName</key><string>CV+ Compilatore Alunno</string>
<key>CFBundleIdentifier</key><string>it.alessandrobarazzuol.cvplus.student</string>
<key>CFBundleVersion</key><string>0.1.0</string>
<key>CFBundleShortVersionString</key><string>0.1.0-beta</string>
<key>CFBundleExecutable</key><string>CVPlus.Mac</string>
<key>LSMinimumSystemVersion</key><string>12.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
chmod +x "$APP/Contents/MacOS/CVPlus.Mac"
hdiutil create -volname "CV+ Compilatore Alunno" -srcfolder dist -ov -format UDZO "CVPlus-Compilatore-Alunno-macOS-AppleSilicon.dmg"
echo "Creato: CVPlus-Compilatore-Alunno-macOS-AppleSilicon.dmg"
