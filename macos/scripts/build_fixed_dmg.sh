#!/bin/bash
set -euo pipefail
SRC="${1:?DMG sorgente mancante}"
OUT="${2:?DMG destinazione mancante}"
WORK="$(mktemp -d)"
MOUNT="$WORK/mount"
STAGE="$WORK/stage"
mkdir -p "$MOUNT" "$STAGE"
cleanup() {
  hdiutil detach "$MOUNT" -force >/dev/null 2>&1 || true
  rm -rf "$WORK"
}
trap cleanup EXIT

hdiutil attach "$SRC" -nobrowse -readonly -mountpoint "$MOUNT"
ditto "$MOUNT" "$STAGE"
hdiutil detach "$MOUNT"

APP="$(find "$STAGE" -maxdepth 2 -type d -name '*.app' | head -n 1 || true)"
[[ -n "$APP" ]] || { echo 'Nel DMG non è presente alcuna applicazione .app'; exit 1; }
EXEC_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist" 2>/dev/null || true)"
[[ -n "$EXEC_NAME" ]] || { echo 'CFBundleExecutable assente da Info.plist'; exit 1; }
EXEC="$APP/Contents/MacOS/$EXEC_NAME"
[[ -f "$EXEC" ]] || { echo "Eseguibile dell'app assente: $EXEC"; exit 1; }
chmod +x "$EXEC"
xattr -cr "$APP" || true

IDENTITY="-"
FOUND="$(security find-identity -v -p codesigning 2>/dev/null | awk -F\" '/Developer ID Application/{print $2; exit}')"
if [[ -n "$FOUND" ]]; then IDENTITY="$FOUND"; fi

# Firma prima gli elementi interni, poi il bundle principale.
while IFS= read -r item; do
  codesign --force --timestamp --options runtime --sign "$IDENTITY" "$item" || \
  codesign --force --sign "$IDENTITY" "$item"
done < <(find "$APP/Contents" -type f \( -perm -111 -o -name '*.dylib' -o -name '*.so' \) ! -path "$EXEC" | sort)

if [[ "$IDENTITY" == "-" ]]; then
  codesign --force --deep --sign - "$APP"
else
  codesign --force --deep --timestamp --options runtime --sign "$IDENTITY" "$APP"
fi
codesign --verify --deep --strict --verbose=2 "$APP"

rm -f "$OUT"
hdiutil create -volname 'CV+ Compilatore Alunno' -srcfolder "$STAGE" -ov -format UDZO "$OUT"
hdiutil verify "$OUT"
