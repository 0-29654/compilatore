PATCH macOS APPLE SILICON - EDITOR A COLORI E DMG SU GITHUB

MODIFICHE:
1. Editor main.cpp sostituito con AvaloniaEdit.
2. Evidenziazione sintattica C++ con numeri di riga.
3. Tema chiaro stile Xcode/Visual Studio, testo scuro e leggibile.
4. Stesso editor colorato anche per i file .h e per l'editor grande Shift+clic.
5. Workflow GitHub Actions aggiornato:
   - a ogni push su main crea il DMG e lo conserva negli Artifacts;
   - avviando manualmente il workflow crea anche una GitHub Release prerelease con il DMG allegato.

USO GITHUB:
- Copiare tutta la cartella nella repository.
- Aprire Actions > Build macOS Apple Silicon DMG > Run workflow.
- Al termine il DMG compare sia negli Artifacts sia nella sezione Releases.

NOTA:
Il DMG è non firmato e non notarizzato. macOS può richiedere clic destro > Apri al primo avvio.
