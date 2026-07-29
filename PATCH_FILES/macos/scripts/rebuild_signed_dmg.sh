#!/bin/bash
set -euo pipefail
SRC="${1:?DMG sorgente mancante}"
OUT="${2:?DMG destinazione mancante}"
IDENTITY="${3:?Identità Developer ID mancante}"
WORK="$(mktemp -d)"
MOUNT="$WORK/mount"
STAGE="$WORK/stage"
mkdir -p "$MOUNT" "$STAGE"
cleanup(){ hdiutil detach "$MOUNT" -force >/dev/null 2>&1 || true; rm -rf "$WORK"; }
trap cleanup EXIT
hdiutil attach "$SRC" -nobrowse -readonly -mountpoint "$MOUNT"
ditto "$MOUNT" "$STAGE"
hdiutil detach "$MOUNT"
APP="$(find "$STAGE" -maxdepth 3 -type d -name '*.app' | head -n 1 || true)"
[[ -n "$APP" ]] || { echo 'Nessuna applicazione .app trovata nel DMG.'; exit 1; }
EXEC_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")"
EXEC="$APP/Contents/MacOS/$EXEC_NAME"
[[ -f "$EXEC" ]] || { echo "Eseguibile assente: $EXEC"; exit 1; }
chmod +x "$EXEC"
xattr -cr "$APP" || true
ARCHS="$(lipo -archs "$EXEC" 2>/dev/null || file "$EXEC")"
echo "Architetture: $ARCHS"
[[ "$ARCHS" == *arm64* ]] || { echo 'Il programma non contiene arm64 Apple Silicon.'; exit 1; }
while IFS= read -r item; do
  codesign --force --timestamp --options runtime --sign "$IDENTITY" "$item"
done < <(find "$APP/Contents" -type f \( -perm -111 -o -name '*.dylib' -o -name '*.so' \) ! -path "$EXEC" | sort)
codesign --force --timestamp --options runtime --deep --sign "$IDENTITY" "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"
spctl --assess --type execute -v "$APP"
rm -f "$OUT"
hdiutil create -volname 'CV+ Compilatore Alunno' -srcfolder "$STAGE" -ov -format UDZO "$OUT"
hdiutil verify "$OUT"
codesign --force --timestamp --sign "$IDENTITY" "$OUT"
codesign --verify --verbose=2 "$OUT"
