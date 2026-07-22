# Patch CV+ Compilatore Alunno — sincronizzazione docente

- i campi **IP DOCENTE : PORTA** e **CODICE SESSIONE** sono sempre vuoti all'avvio;
- ascolto automatico UDP sulla porta 5051;
- ricezione automatica di IP, porta, codice sessione e modalità dal server docente;
- lettura dello stato `compileEnabled`/`compilationDisabled`;
- disabilitazione di **Compila C++17** e **Compila e apri CMD** quando il docente inibisce la compilazione;
- il comportamento vale sia in esercitazione sia in verifica.
