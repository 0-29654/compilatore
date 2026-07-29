#!/bin/bash
set -euo pipefail

INPUT_DMG="$1"
OUTPUT_DMG="$2"
WORK="$(mktemp -d)"
MOUNT="$WORK/mount"
STAGE="$WORK/stage"
KEYCHAIN="$WORK/signing.keychain-db"
mkdir -p "$MOUNT" "$STAGE" "$(dirname "$OUTPUT_DMG")"
cleanup() {
  hdiutil detach "$MOUNT" -force >/dev/null 2>&1 || true
  security delete-keychain "$KEYCHAIN" >/dev/null 2>&1 || true
  rm -rf "$WORK"
}
trap cleanup EXIT

hdiutil attach "$INPUT_DMG" -nobrowse -readonly -mountpoint "$MOUNT"
APP_SRC="$(find "$MOUNT" -maxdepth 3 -name '*.app' -type d | head -n 1 || true)"
[[ -n "$APP_SRC" ]] || { echo 'Nessuna app trovata nel DMG'; exit 3; }
cp -R "$APP_SRC" "$STAGE/"
APP="$STAGE/$(basename "$APP_SRC")"
xattr -cr "$APP" || true
find "$APP" -name '._*' -delete || true
find "$APP" -name '.DS_Store' -delete || true
MAIN_EXEC=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")
chmod +x "$APP/Contents/MacOS/$MAIN_EXEC"

IDENTITY='-'
if [[ -n "${MACOS_CERTIFICATE_P12:-}" && -n "${MACOS_CERTIFICATE_PASSWORD:-}" ]]; then
  echo "$MACOS_CERTIFICATE_P12" | base64 --decode > "$WORK/certificate.p12"
  security create-keychain -p temp-password "$KEYCHAIN"
  security set-keychain-settings -lut 21600 "$KEYCHAIN"
  security unlock-keychain -p temp-password "$KEYCHAIN"
  security import "$WORK/certificate.p12" -k "$KEYCHAIN" -P "$MACOS_CERTIFICATE_PASSWORD" -T /usr/bin/codesign -T /usr/bin/security
  security list-keychains -d user -s "$KEYCHAIN" login.keychain-db
  security set-key-partition-list -S apple-tool:,apple: -s -k temp-password "$KEYCHAIN"
  IDENTITY=$(security find-identity -v -p codesigning "$KEYCHAIN" | awk -F '"' '/Developer ID Application/{print $2; exit}')
  [[ -n "$IDENTITY" ]] || { echo 'Certificato Developer ID Application non trovato'; exit 4; }
fi

while IFS= read -r item; do
  codesign --force --options runtime --timestamp --sign "$IDENTITY" "$item"
done < <(find "$APP/Contents" -depth \( -name '*.framework' -o -name '*.dylib' -o -name '*.so' -o -name '*.xpc' -o -name '*.appex' \) -print)

if [[ "$IDENTITY" == '-' ]]; then
  codesign --force --deep --sign - --timestamp=none "$APP"
else
  codesign --force --deep --options runtime --timestamp --sign "$IDENTITY" "$APP"
fi
codesign --verify --deep --strict --verbose=2 "$APP"

ln -s /Applications "$STAGE/Applications"
hdiutil create -volname 'CVPlus Compilatore Alunno' -srcfolder "$STAGE" -ov -format UDZO "$OUTPUT_DMG"
hdiutil verify "$OUTPUT_DMG"

if [[ "$IDENTITY" != '-' && -n "${APPLE_ID:-}" && -n "${APPLE_APP_PASSWORD:-}" && -n "${APPLE_TEAM_ID:-}" ]]; then
  xcrun notarytool submit "$OUTPUT_DMG" --apple-id "$APPLE_ID" --password "$APPLE_APP_PASSWORD" --team-id "$APPLE_TEAM_ID" --wait
  xcrun stapler staple "$OUTPUT_DMG"
  xcrun stapler validate "$OUTPUT_DMG"
else
  echo 'ATTENZIONE: DMG creato senza notarizzazione Apple. Per evitare “Sposta nel Cestino” ai download pubblici, configura i segreti Apple indicati nel README.'
fi
