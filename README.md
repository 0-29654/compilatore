# CV+ Compilatore Alunno

Applicazione WPF leggera per i PC del laboratorio: editor C++, compilazione locale, esecuzione in CMD e invio degli esercizi al server del docente.

## Release automatica
Il workflow GitHub Actions crea `CppStudentClient_Setup.exe` e lo pubblica nella sezione Releases.

## Installazione
L'installer è moderno, per utente corrente e non richiede privilegi amministrativi.

© Alessandro Barazzuol

## Correzione DMG macOS
La cartella `macos/` contiene un workflow macOS che ripara un DMG esistente, ricostruendo permessi, firma ad-hoc e immagine disco. Inserire il DMG sorgente in `macos/input/` e avviare il workflow `Ripara e verifica DMG macOS`.
