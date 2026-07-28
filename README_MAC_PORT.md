# CV+ Compilatore Alunno — porting macOS Apple Silicon (BETA iniziale)

Questa è una prima conversione nativa `osx-arm64` con Avalonia/.NET 8. Non sostituisce ancora la versione Windows completa.

## Funzioni già portate
- Interfaccia principale simile al client Windows.
- Editor `main.cpp` e file `.h`.
- Compilazione C++17 con `/usr/bin/xcrun clang++`.
- Console interna: intestazioni verdi e **solo output del programma bianco**.
- Rilevamento UDP del docente sulla porta 5051.
- IP, porta e codice sessione bloccati quando ricevuti dal server.
- Pulsanti Aggiungi/Rinomina/Elimina `.h` disabilitati di default e abilitati soltanto dal server.
- Invio HTTP a `/submit`.
- Shift+clic sull'editor apre l'editor grande; chiudendo applica le modifiche e non presenta “Compila ed esegui”.
- Workflow GitHub Actions per creare un DMG Apple Silicon.

## Requisiti Mac
- macOS 12 o successivo, Apple Silicon.
- Xcode Command Line Tools. Sul Mac eseguire una volta: `xcode-select --install`.

## Build locale
```bash
./build-mac-arm64.sh
```

## Limiti della beta
Non sono ancora portati integralmente: modalità verifica/kiosk, input interattivo prolungato via `cin`, updater, tutte le finestre secondarie, installer con licenza pre-avvio, replica pixel-per-pixel della UI WPF e tutte le API Windows. Il DMG non è firmato/notarizzato.
