#!/bin/bash
set -e
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
echo "Trascina qui la cartella locale della repository CV+ e premi Invio:"
read -r REPO
REPO="${REPO%/}"
REPO="${REPO#\'}"; REPO="${REPO%\'}"
REPO="${REPO#\"}"; REPO="${REPO%\"}"
if [ ! -f "$REPO/setup_student.iss" ]; then
  echo "ERRORE: setup_student.iss non trovato nella cartella scelta."
  read -p "Premi Invio per chiudere..."
  exit 1
fi
mkdir -p "$REPO/Assets"
cp "$REPO/setup_student.iss" "$REPO/setup_student.iss.backup-prima-installer-moderno"
cp "$SCRIPT_DIR/setup_student.iss" "$REPO/setup_student.iss"
cp "$SCRIPT_DIR/Assets/installer_header.bmp" "$REPO/Assets/installer_header.bmp"
cp "$SCRIPT_DIR/Assets/installer_splash.bmp" "$REPO/Assets/installer_splash.bmp"
echo "Patch applicata. Ora esegui git add -A, git commit e git push."
read -p "Premi Invio per chiudere..."
