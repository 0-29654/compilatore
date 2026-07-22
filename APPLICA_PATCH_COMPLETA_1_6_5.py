#!/usr/bin/env python3
from pathlib import Path
import re
import shutil
import sys

ROOT = Path(__file__).resolve().parent
REPO = (
    Path(sys.argv[1]).resolve()
    if len(sys.argv) > 1
    else Path.cwd().resolve()
)

setup = REPO / "setup_student.iss"
main_cs = REPO / "MainWindow.xaml.cs"
assets = REPO / "Assets"

for required in (setup, main_cs):
    if not required.exists():
        raise SystemExit(f"ERRORE: file non trovato: {required}")

assets.mkdir(parents=True, exist_ok=True)

shutil.copy2(
    ROOT / "Assets" / "wizard_dog.bmp",
    assets / "wizard_dog.bmp"
)
shutil.copy2(
    ROOT / "Assets" / "wizard_dog_small.bmp",
    assets / "wizard_dog_small.bmp"
)
shutil.copy2(
    ROOT / "CONDIZIONI_USO_PRIVACY.txt",
    REPO / "CONDIZIONI_USO_PRIVACY.txt"
)

for target in (setup, main_cs):
    backup = target.with_suffix(
        target.suffix + ".prima_patch_1_6_5"
    )
    if not backup.exists():
        shutil.copy2(target, backup)

# ---------------------------------------------------------------
# INSTALLER
# ---------------------------------------------------------------
iss = setup.read_text(encoding="utf-8-sig")

iss = re.sub(
    r'#define MyAppVersion\s+"[^"]+"',
    '#define MyAppVersion "1.6.5"',
    iss,
    count=1
)

iss = iss.replace(
    "Alessandro Barazuol",
    "Alessandro Barazzuol"
)

def set_setup_directive(text, name, value):
    pattern = rf'(?im)^\s*{re.escape(name)}\s*=.*$'
    replacement = f"{name}={value}"
    if re.search(pattern, text):
        return re.sub(pattern, replacement, text, count=1)
    return text.replace(
        "[Setup]",
        f"[Setup]\n{replacement}",
        1
    )

iss = set_setup_directive(
    iss,
    "WizardImageFile",
    r"Assets\wizard_dog.bmp"
)
iss = set_setup_directive(
    iss,
    "WizardSmallImageFile",
    r"Assets\wizard_dog_small.bmp"
)
iss = set_setup_directive(
    iss,
    "LicenseFile",
    "CONDIZIONI_USO_PRIVACY.txt"
)

setup.write_text(iss, encoding="utf-8")

# ---------------------------------------------------------------
# CODICE C#
# ---------------------------------------------------------------
source = main_cs.read_text(encoding="utf-8-sig")
source = source.replace(
    "Alessandro Barazuol",
    "Alessandro Barazzuol"
)

COMPILE_METHODS = 'private async Task<bool> CompileAsync()\n    {\n        SaveCurrentExercise();\n\n        string currentCode = Editor.Text;\n        CompilationResult result = await CompileCodeAsync(currentCode, true);\n\n        _compileOutput = result.CompileOutput;\n        _exePath = result.ExePath;\n\n        if (_exerciseStates.TryGetValue(_activeKey, out ExerciseState? state))\n        {\n            state.CompileOutput = _compileOutput;\n            if (!result.Success)\n                state.ProgramOutput = "";\n            SaveExerciseStates();\n        }\n\n        return result.Success;\n    }\n\n    private async Task<CompilationResult> CompileCodeAsync(\n        string sourceCode,\n        bool updateOutputBox)\n    {\n        if (!_compilationAllowed)\n        {\n            const string denied =\n                "Il docente ha temporaneamente inibito la compilazione sui client.";\n\n            if (updateOutputBox)\n                OutputBox.Text = denied;\n\n            return new CompilationResult(false, denied, null);\n        }\n\n        if (updateOutputBox)\n            OutputBox.Text = "Compilazione C++17 in corso...";\n\n        try\n        {\n            string gpp = BundledCompilerPath;\n            if (!File.Exists(gpp))\n            {\n                string missing =\n                    "Installazione incompleta: il compilatore C++17 incorporato " +\n                    "non è stato trovato.\\n\\nReinstalla CV+ Compilatore Alunno " +\n                    "dalla Release ufficiale.";\n\n                if (updateOutputBox)\n                    OutputBox.Text = missing;\n\n                return new CompilationResult(false, missing, null);\n            }\n\n            string dir = Path.Combine(\n                Path.GetTempPath(),\n                "CppStudentClient"\n            );\n            Directory.CreateDirectory(dir);\n\n            string stem = "compito_" + Guid.NewGuid().ToString("N");\n            string cpp = Path.Combine(dir, stem + ".cpp");\n            string exe = Path.Combine(dir, stem + ".exe");\n\n            File.WriteAllText(cpp, sourceCode, new UTF8Encoding(false));\n\n            string arguments =\n                $"-std=c++17 -Wall -Wextra -pedantic " +\n                $"-fdiagnostics-color=never " +\n                $"-o \\"{exe}\\" \\"{cpp}\\"";\n\n            var psi = new ProcessStartInfo(gpp, arguments)\n            {\n                UseShellExecute = false,\n                RedirectStandardOutput = true,\n                RedirectStandardError = true,\n                CreateNoWindow = true,\n                WorkingDirectory = dir,\n                StandardOutputEncoding = Encoding.UTF8,\n                StandardErrorEncoding = Encoding.UTF8\n            };\n\n            ConfigureCompilerEnvironment(psi);\n\n            using var process = Process.Start(psi)\n                ?? throw new InvalidOperationException(\n                    "Impossibile avviare il compilatore C++17 incorporato."\n                );\n\n            // Legge i due flussi contemporaneamente: gli errori GCC vengono\n            // normalmente scritti su stderr, non su stdout.\n            Task<string> stdoutTask =\n                process.StandardOutput.ReadToEndAsync();\n            Task<string> stderrTask =\n                process.StandardError.ReadToEndAsync();\n\n            await Task.WhenAll(\n                stdoutTask,\n                stderrTask,\n                process.WaitForExitAsync()\n            );\n\n            string stdout = (await stdoutTask).Trim();\n            string stderr = (await stderrTask).Trim();\n\n            var parts = new List<string>();\n            if (!string.IsNullOrWhiteSpace(stderr))\n                parts.Add(stderr);\n            if (!string.IsNullOrWhiteSpace(stdout))\n                parts.Add(stdout);\n\n            string diagnostics = string.Join(\n                Environment.NewLine + Environment.NewLine,\n                parts\n            ).Trim();\n\n            bool success =\n                process.ExitCode == 0 &&\n                File.Exists(exe);\n\n            if (success)\n            {\n                string successText =\n                    string.IsNullOrWhiteSpace(diagnostics)\n                    ? "Compilazione riuscita in C++17. Nessun errore o avviso."\n                    : "Compilazione riuscita in C++17.\\n\\n" + diagnostics;\n\n                if (updateOutputBox)\n                    OutputBox.Text = successText;\n\n                return new CompilationResult(\n                    true,\n                    successText,\n                    exe\n                );\n            }\n\n            if (string.IsNullOrWhiteSpace(diagnostics))\n            {\n                diagnostics =\n                    "Il compilatore ha restituito il codice di errore " +\n                    process.ExitCode +\n                    " senza un messaggio diagnostico.";\n            }\n\n            string errorText =\n                $"Compilazione C++17 non riuscita " +\n                $"(codice {process.ExitCode}).\\n\\n{diagnostics}";\n\n            if (updateOutputBox)\n                OutputBox.Text = errorText;\n\n            return new CompilationResult(\n                false,\n                errorText,\n                null\n            );\n        }\n        catch (Exception ex)\n        {\n            string errorText =\n                "Errore durante la compilazione C++17:\\n" +\n                ex.GetType().Name + ": " + ex.Message;\n\n            if (updateOutputBox)\n                OutputBox.Text = errorText;\n\n            return new CompilationResult(\n                false,\n                errorText,\n                null\n            );\n        }\n    }'
RUN_METHOD = 'private async void Run_Click(object sender, RoutedEventArgs e)\n    {\n        if (!_compilationAllowed)\n        {\n            MessageBox.Show(\n                "La compilazione è stata inibita dal docente.",\n                "Compilazione non disponibile",\n                MessageBoxButton.OK,\n                MessageBoxImage.Information\n            );\n            return;\n        }\n\n        if (_verificationMode)\n        {\n            MessageBox.Show(\n                "Durante una verifica l\'esecuzione in una finestra CMD " +\n                "separata è disabilitata.",\n                "Modalità verifica",\n                MessageBoxButton.OK,\n                MessageBoxImage.Information\n            );\n            return;\n        }\n\n        if (!await CompileAsync() || string.IsNullOrWhiteSpace(_exePath))\n            return;\n\n        string bat = Path.Combine(\n            Path.GetTempPath(),\n            "cppstudent_run_" + Guid.NewGuid().ToString("N") + ".bat"\n        );\n\n        File.WriteAllText(\n            bat,\n            $"@echo off\\r\\n" +\n            $"set \\"PATH={BundledCompilerBin};%PATH%\\"\\r\\n" +\n            $"\\"{_exePath}\\"\\r\\n" +\n            $"echo.\\r\\n" +\n            $"echo Programma terminato.\\r\\n" +\n            $"pause\\r\\n",\n            Encoding.Default\n        );\n\n        Process.Start(\n            new ProcessStartInfo(\n                "cmd.exe",\n                $"/c \\"{bat}\\""\n            )\n            {\n                UseShellExecute = true\n            }\n        );\n\n        _programOutput =\n            "Il programma è stato compilato correttamente ed eseguito " +\n            "in una finestra CMD separata.";\n\n        if (_exerciseStates.TryGetValue(\n                _activeKey,\n                out ExerciseState? state))\n        {\n            state.ProgramOutput = _programOutput;\n            state.CompileOutput = _compileOutput;\n            SaveExerciseStates();\n        }\n    }'
SEND_METHODS = 'private async void Send_Click(object sender, RoutedEventArgs e)\n    {\n        SaveCurrentExercise();\n\n        if (!ValidateSubmission(\n                out int registerNumber,\n                out int activeExerciseNumber))\n            return;\n\n        string countText =\n            Microsoft.VisualBasic.Interaction.InputBox(\n                "Quanti esercizi vuoi inviare?\\n\\n" +\n                "Inserendo 3 verranno inviati gli esercizi 1, 2 e 3 " +\n                "della tipologia selezionata.",\n                "Numero di esercizi da inviare",\n                activeExerciseNumber.ToString()\n            );\n\n        if (string.IsNullOrWhiteSpace(countText))\n        {\n            StatusText.Text = "Invio annullato";\n            return;\n        }\n\n        if (!int.TryParse(countText.Trim(), out int exerciseCount) ||\n            exerciseCount <= 0 ||\n            exerciseCount > 100)\n        {\n            MessageBox.Show(\n                "Inserisci un numero intero compreso tra 1 e 100.",\n                "Numero di esercizi non valido",\n                MessageBoxButton.OK,\n                MessageBoxImage.Warning\n            );\n            return;\n        }\n\n        string type = GetTaskType();\n        string modeLabel =\n            _verificationMode ? "VERIFICA" : "ESERCITAZIONE";\n\n        var missingExercises = new List<int>();\n        for (int number = 1; number <= exerciseCount; number++)\n        {\n            string key = BuildExerciseKey(type, number);\n            if (!_exerciseStates.TryGetValue(\n                    key,\n                    out ExerciseState? state) ||\n                string.IsNullOrWhiteSpace(state.Code))\n            {\n                missingExercises.Add(number);\n            }\n        }\n\n        if (missingExercises.Count > 0)\n        {\n            MessageBoxResult continueResult = MessageBox.Show(\n                "I seguenti esercizi non risultano compilati nell\'editor " +\n                "e verranno inviati con il modello di codice disponibile:\\n\\n" +\n                string.Join(", ", missingExercises) +\n                "\\n\\nVuoi continuare?",\n                "Esercizi non ancora compilati",\n                MessageBoxButton.YesNo,\n                MessageBoxImage.Warning,\n                MessageBoxResult.No\n            );\n\n            if (continueResult != MessageBoxResult.Yes)\n            {\n                StatusText.Text = "Invio annullato";\n                return;\n            }\n        }\n\n        MessageBoxResult confirmation = MessageBox.Show(\n            $"Confermi l\'invio?\\n\\n" +\n            $"Modalità: {modeLabel}\\n" +\n            $"N° registro alunno: {registerNumber}\\n" +\n            $"Nome e cognome: {StudentNameBox.Text.Trim()}\\n" +\n            $"Classe: {ClassBox.Text.Trim()}\\n" +\n            $"Tipologia: {type}\\n" +\n            $"Esercizi inviati: da 1 a {exerciseCount}\\n\\n" +\n            "Prima dell\'invio ogni esercizio verrà ricompilato. " +\n            "Al docente verrà trasmesso l\'output della compilazione " +\n            "oppure il messaggio completo degli errori.",\n            "Conferma consegna multipla",\n            MessageBoxButton.YesNo,\n            MessageBoxImage.Question,\n            MessageBoxResult.No\n        );\n\n        if (confirmation != MessageBoxResult.Yes)\n        {\n            StatusText.Text = "Invio annullato";\n            return;\n        }\n\n        try\n        {\n            SaveSettings();\n\n            string address =\n                NormalizeServerAddress(ServerBox.Text) + "/submit";\n\n            int sent = 0;\n            var failed = new List<int>();\n\n            for (int number = 1; number <= exerciseCount; number++)\n            {\n                StatusText.Text =\n                    $"Compilazione esercizio {number} di {exerciseCount}...";\n\n                string key = BuildExerciseKey(type, number);\n                if (!_exerciseStates.TryGetValue(\n                        key,\n                        out ExerciseState? state))\n                {\n                    state = new ExerciseState\n                    {\n                        Code = DefaultCode,\n                        Elapsed = TimeSpan.Zero\n                    };\n                    _exerciseStates[key] = state;\n                }\n\n                string code =\n                    string.IsNullOrWhiteSpace(state.Code)\n                    ? DefaultCode\n                    : state.Code;\n\n                CompilationResult compilation =\n                    await CompileCodeAsync(code, false);\n\n                state.CompileOutput = compilation.CompileOutput;\n\n                // Se la compilazione non riesce, non si invia un vecchio\n                // output di esecuzione appartenente a una compilazione diversa.\n                if (!compilation.Success)\n                    state.ProgramOutput = "";\n\n                string transmittedProgramOutput =\n                    compilation.Success\n                    ? (\n                        string.IsNullOrWhiteSpace(state.ProgramOutput)\n                        ? "Compilazione riuscita. Il programma non è stato " +\n                          "eseguito prima dell\'invio."\n                        : state.ProgramOutput\n                    )\n                    : "";\n\n                var timings = _exerciseStates.ToDictionary(\n                    pair => pair.Key,\n                    pair => (long)pair.Value.Elapsed.TotalSeconds\n                );\n\n                var payload = new\n                {\n                    studentId = registerNumber.ToString(),\n                    registerNumber,\n                    studentName = StudentNameBox.Text.Trim(),\n                    className = ClassBox.Text.Trim(),\n                    taskType = type,\n                    exerciseId = number.ToString(),\n                    exerciseNumber = number,\n                    totalExercises = exerciseCount,\n                    sessionCode = SessionBox.Text.Trim(),\n                    sessionMode =\n                        _verificationMode\n                        ? "verifica"\n                        : "esercitazione",\n                    exerciseTimeSeconds =\n                        (long)state.Elapsed.TotalSeconds,\n                    exerciseTimes = timings,\n                    code,\n                    compilationSucceeded = compilation.Success,\n                    compileOutput = compilation.CompileOutput,\n                    programOutput = transmittedProgramOutput,\n                    output = compilation.Success\n                        ? transmittedProgramOutput\n                        : compilation.CompileOutput\n                };\n\n                using var content = new StringContent(\n                    JsonSerializer.Serialize(payload),\n                    Encoding.UTF8,\n                    "application/json"\n                );\n\n                StatusText.Text =\n                    $"Invio esercizio {number} di {exerciseCount}...";\n\n                using HttpResponseMessage response =\n                    await _http.PostAsync(address, content);\n\n                string message =\n                    await response.Content.ReadAsStringAsync();\n\n                if (!response.IsSuccessStatusCode)\n                {\n                    failed.Add(number);\n                    continue;\n                }\n\n                sent++;\n            }\n\n            SaveExerciseStates();\n\n            if (failed.Count > 0)\n            {\n                StatusText.Text =\n                    $"Inviati {sent} esercizi su {exerciseCount}";\n\n                MessageBox.Show(\n                    $"Sono stati inviati {sent} esercizi su " +\n                    $"{exerciseCount}.\\n\\nInvio non riuscito per: " +\n                    string.Join(", ", failed),\n                    "Invio completato parzialmente",\n                    MessageBoxButton.OK,\n                    MessageBoxImage.Warning\n                );\n                return;\n            }\n\n            StatusText.Text =\n                $"Consegnati {sent} esercizi: " +\n                DateTime.Now.ToString("HH:mm:ss");\n\n            MessageBox.Show(\n                $"Sono stati inviati correttamente gli esercizi " +\n                $"da 1 a {exerciseCount}.\\n\\n" +\n                "Per ogni esercizio sono stati trasmessi il codice e " +\n                "l\'esito della compilazione: output in caso di successo, " +\n                "errori completi in caso di compilazione non riuscita.",\n                "Consegna completata",\n                MessageBoxButton.OK,\n                MessageBoxImage.Information\n            );\n\n            if (_verificationMode)\n            {\n                ClearLocalVerificationData();\n                _allowClose = true;\n                Close();\n            }\n        }\n        catch (Exception ex)\n        {\n            StatusText.Text = "Invio fallito";\n\n            MessageBox.Show(\n                BuildNetworkError(ex),\n                "Impossibile inviare il compito",\n                MessageBoxButton.OK,\n                MessageBoxImage.Warning\n            );\n        }\n    }'
EXTRA_TYPES = 'private sealed record CompilationResult(\n        bool Success,\n        string CompileOutput,\n        string? ExePath\n    );\n\n    public sealed class ExerciseState\n    {\n        public string Code { get; set; } = DefaultCode;\n        public TimeSpan Elapsed { get; set; } = TimeSpan.Zero;\n        public string CompileOutput { get; set; } = "";\n        public string ProgramOutput { get; set; } = "";\n    }'

def replace_between(text, start_pattern, end_pattern, replacement, label):
    pattern = re.compile(
        start_pattern + r'.*?(?=' + end_pattern + r')',
        re.S
    )
    match = pattern.search(text)
    if not match:
        raise SystemExit(
            f"ERRORE: sezione {label} non trovata. "
            "Controlla che MainWindow.xaml.cs sia la versione corrente."
        )
    return (
        text[:match.start()] +
        replacement +
        "\n\n    " +
        text[match.end():]
    )

# Sostituisce Run_Click e CompileAsync in due passaggi.
source = replace_between(
    source,
    r'private\s+async\s+void\s+Run_Click\s*\([^)]*\)\s*\{',
    r'private\s+async\s+Task(?:<bool>)?\s+CompileAsync\s*\(',
    RUN_METHOD,
    "Run_Click"
)

source = replace_between(
    source,
    r'private\s+async\s+Task(?:<bool>)?\s+CompileAsync\s*\(\s*\)\s*\{',
    r'private\s+async\s+void\s+TestServer_Click\s*\(',
    COMPILE_METHODS,
    "CompileAsync"
)

source = replace_between(
    source,
    r'private\s+async\s+void\s+Send_Click\s*\([^)]*\)\s*\{',
    r'private\s+bool\s+ValidateSubmission\s*\(',
    SEND_METHODS,
    "Send_Click"
)

# Sostituisce la vecchia ExerciseState e aggiunge CompilationResult.
state_pattern = re.compile(
    r'public\s+sealed\s+class\s+ExerciseState\s*\{.*?\}',
    re.S
)
state_match = state_pattern.search(source)
if not state_match:
    raise SystemExit("ERRORE: classe ExerciseState non trovata.")

source = (
    source[:state_match.start()] +
    EXTRA_TYPES +
    source[state_match.end():]
)

main_cs.write_text(source, encoding="utf-8")

print()
print("PATCH COMPLETA 1.6.5 APPLICATA")
print(f"Repository: {REPO}")
print()
print("Modifiche:")
print("- immagine del cane nell'installer")
print("- accettazione obbligatoria condizioni d'uso e privacy")
print("- copyright Alessandro Barazzuol")
print("- cattura affidabile degli errori GCC da stderr e stdout")
print("- visualizzazione errori nella casella Output compilazione")
print("- domanda sul numero di esercizi da inviare")
print("- invio consecutivo degli esercizi da 1 a N")
print("- ricompilazione di ogni esercizio prima dell'invio")
print("- invio dell'output se compila, degli errori se non compila")
print()
print("Ora esegui CREA_INSTALLER.bat o fai commit e push.")
