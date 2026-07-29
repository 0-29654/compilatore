#!/bin/bash
set -e
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
echo "Trascina qui la cartella locale della repository CV+ e premi Invio:"
read -r REPO
REPO="${REPO%/}"
REPO="${REPO#\'}"; REPO="${REPO%\'}"
REPO="${REPO#\"}"; REPO="${REPO%\"}"

if [ ! -f "$REPO/CppStudentClient.csproj" ] || [ ! -f "$REPO/setup_student.iss" ]; then
  echo "ERRORE: la cartella scelta non sembra la radice della repository CV+."
  echo "Devono esserci CppStudentClient.csproj e setup_student.iss."
  read -p "Premi Invio per chiudere..."
  exit 1
fi

mkdir -p "$REPO/Assets"
cp "$REPO/setup_student.iss" "$REPO/setup_student.iss.backup-prima-installer-moderno-2" || true
cp "$SCRIPT_DIR/setup_student.iss" "$REPO/setup_student.iss"

for FILE in app.ico wizard.bmp wizard_small.bmp installing_a.bmp A.png installer_header.bmp installer_splash.bmp; do
  cp "$SCRIPT_DIR/Assets/$FILE" "$REPO/Assets/$FILE"
done

# Rimuove soltanto eventuali cartelle Mac che interferiscono con la build Windows.
rm -rf "$REPO/CVPlus.Mac" "$REPO/macos"
rm -f "$REPO/.github/workflows/build-macos-arm64.yml" \
      "$REPO/.github/workflows/macos-auto.yml" \
      "$REPO/.github/workflows/repair-macos-dmg.yml"

echo
 echo "Patch applicata correttamente. File Assets/app.ico ripristinato."
echo "Ora, dalla repository, esegui:"
echo "  git add -A"
echo "  git commit -m \"Correzione installer moderno e icona Windows\""
echo "  git push"
read -p "Premi Invio per chiudere..."
