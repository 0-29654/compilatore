#!/bin/bash
set -euo pipefail

INPUT_DMG="${1:-macos/input/CVPlus-Compilatore-Alunno.dmg}"
OUTPUT_DMG="${2:-macos/output/CVPlus-Compilatore-Alunno-macOS-AppleSilicon.dmg}"
APP_NAME="${APP_NAME:-CVPlus Compilatore Alunno.app}"

if [[ ! -f "$INPUT_DMG" ]]; then
  echo "ERRORE: DMG sorgente non trovato: $INPUT_DMG" >&2
  exit 2
fi

WORK="$(mktemp -d)"
MOUNT="$WORK/mount"
STAGE="$WORK/stage"
mkdir -p "$MOUNT" "$STAGE" "$(dirname "$OUTPUT_DMG")"
cleanup() {
  hdiutil detach "$MOUNT" -force >/dev/null 2>&1 || true
  rm -rf "$WORK"
}
trap cleanup EXIT

hdiutil attach "$INPUT_DMG" -nobrowse -readonly -mountpoint "$MOUNT"
APP_PATH="$(find "$MOUNT" -maxdepth 2 -name '*.app' -type d | head -n 1 || true)"
if [[ -z "$APP_PATH" ]]; then
  echo "ERRORE: nessuna app trovata nel DMG." >&2
  exit 3
fi

cp -R "$APP_PATH" "$STAGE/$APP_NAME"
APP="$STAGE/$APP_NAME"

# Elimina attributi di quarantena e metadati che possono far apparire l'app danneggiata.
xattr -cr "$APP" || true
find "$APP" -name '._*' -delete || true
find "$APP" -name '.DS_Store' -delete || true

# Ripristina i permessi degli eseguibili nel bundle.
MAIN_EXEC=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")
chmod +x "$APP/Contents/MacOS/$MAIN_EXEC"
find "$APP/Contents" -type f \( -path '*/MacOS/*' -o -path '*/Helpers/*' -o -path '*/Frameworks/*' \) -exec chmod u+x {} \; 2>/dev/null || true

# Firma ad-hoc dal componente più interno verso l'esterno.
while IFS= read -r item; do
  codesign --force --sign - --timestamp=none "$item" || true
done < <(find "$APP/Contents" -depth \( -name '*.framework' -o -name '*.dylib' -o -name '*.so' -o -name '*.xpc' -o -name '*.appex' \) -print)

codesign --force --deep --sign - --timestamp=none "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"
spctl --assess --type execute --verbose=4 "$APP" || true

# Controllo architettura del binario principale.
file "$APP/Contents/MacOS/$MAIN_EXEC"
if ! lipo -info "$APP/Contents/MacOS/$MAIN_EXEC" 2>/dev/null | grep -Eq 'arm64|Non-fat file.*arm64'; then
  echo "AVVISO: il binario principale non risulta Apple Silicon arm64." >&2
fi

ln -s /Applications "$STAGE/Applications"
hdiutil create -volname 'CVPlus Compilatore Alunno' -srcfolder "$STAGE" -ov -format UDZO "$OUTPUT_DMG"
hdiutil verify "$OUTPUT_DMG"

# Verifica finale montando il DMG appena creato.
VERIFY_MOUNT="$WORK/verify"
mkdir -p "$VERIFY_MOUNT"
hdiutil attach "$OUTPUT_DMG" -nobrowse -readonly -mountpoint "$VERIFY_MOUNT"
FINAL_APP="$(find "$VERIFY_MOUNT" -maxdepth 2 -name '*.app' -type d | head -n 1)"
codesign --verify --deep --strict --verbose=2 "$FINAL_APP"
hdiutil detach "$VERIFY_MOUNT"

echo "DMG corretto creato: $OUTPUT_DMG"
