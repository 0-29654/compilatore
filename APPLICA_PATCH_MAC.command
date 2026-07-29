#!/bin/bash
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
echo 'Trascina qui la cartella locale della repository e premi Invio:'
read -r REPO
REPO="${REPO#\'}"; REPO="${REPO%\'}"; REPO="${REPO#\"}"; REPO="${REPO%\"}"
[[ -d "$REPO/.git" ]] || { echo 'La cartella scelta non contiene .git'; read -r; exit 1; }
mkdir -p "$REPO/.github/workflows" "$REPO/macos/scripts" "$REPO/macos/input"
rm -f "$REPO/.github/workflows/build-macos-arm64.yml" "$REPO/.github/workflows/build-release.yml"
cp "$HERE/PATCH_FILES/workflows/windows-auto.yml" "$REPO/.github/workflows/windows-auto.yml"
cp "$HERE/PATCH_FILES/workflows/macos-auto.yml" "$REPO/.github/workflows/macos-auto.yml"
cp "$HERE/PATCH_FILES/setup_student.iss" "$REPO/setup_student.iss"
cp "$HERE/PATCH_FILES/macos/scripts/rebuild_signed_dmg.sh" "$REPO/macos/scripts/rebuild_signed_dmg.sh"
chmod +x "$REPO/macos/scripts/rebuild_signed_dmg.sh"
echo 'Patch applicata. File workflow presenti:'
ls -la "$REPO/.github/workflows"
echo 'Ora esegui git add -A, commit e push.'
read -r
