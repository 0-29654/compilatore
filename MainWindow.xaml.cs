using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace CppStudentClient;

public partial class MainWindow : Window
{
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        UseProxy = false,
        Proxy = null
    })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private string _compileOutput = "";
    private string _programOutput = "";
    private string? _exePath;

    public MainWindow()
    {
        InitializeComponent();
        try { Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C++"); } catch { }
        Editor.Text = "#include <iostream>\nusing namespace std;\n\nint main()\n{\n    \n    return 0;\n}\n";
        LoadSettings();
        Closed += (_, _) => SaveSettings();
    }

    private async void Compile_Click(object sender, RoutedEventArgs e) => await CompileAsync();

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (!await CompileAsync()) return;
        string bat = Path.Combine(Path.GetTempPath(), "cppstudent_run_" + Guid.NewGuid().ToString("N") + ".bat");
        File.WriteAllText(bat,
            $"@echo off\r\n\"{_exePath}\"\r\necho.\r\necho Programma terminato.\r\npause\r\n",
            Encoding.Default);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"") { UseShellExecute = true });
        _programOutput = "Esecuzione aperta nella finestra CMD. L'output interattivo non viene catturato automaticamente.";
    }

    private async Task<bool> CompileAsync()
    {
        try
        {
            string gpp = FindGpp();
            if (string.IsNullOrWhiteSpace(gpp))
            {
                OutputBox.Text = "Compilatore C++ non trovato. Premi 'Scegli g++.exe' e seleziona il compilatore installato (per esempio C:\\msys64\\ucrt64\\bin\\g++.exe).";
                return false;
            }

            CompilerPathBox.Text = gpp;
            SaveSettings();

            string dir = Path.Combine(Path.GetTempPath(), "CppStudentClient");
            Directory.CreateDirectory(dir);
            string stem = "compito_" + Guid.NewGuid().ToString("N");
            string cpp = Path.Combine(dir, stem + ".cpp");
            _exePath = Path.Combine(dir, stem + ".exe");
            File.WriteAllText(cpp, Editor.Text, new UTF8Encoding(false));

            // Prova progressivamente gli standard: alcuni vecchi MinGW non riconoscono -std=c++17.
            string[] standards = { "-std=c++17", "-std=gnu++17", "-std=c++14", "-std=gnu++14", "-std=c++11", "" };
            string lastOutput = "";
            int lastExitCode = -1;

            foreach (string standard in standards)
            {
                string arguments = string.IsNullOrEmpty(standard)
                    ? $"-Wall -Wextra -o \"{_exePath}\" \"{cpp}\""
                    : $"{standard} -Wall -Wextra -o \"{_exePath}\" \"{cpp}\"";

                var psi = new ProcessStartInfo(gpp, arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = dir
                };

                using var process = Process.Start(psi) ?? throw new InvalidOperationException("Impossibile avviare il compilatore selezionato.");
                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                lastExitCode = process.ExitCode;
                lastOutput = (stdout + stderr).Trim();

                if (process.ExitCode == 0 && File.Exists(_exePath))
                {
                    _compileOutput = lastOutput;
                    string usedStandard = string.IsNullOrEmpty(standard) ? "predefinito del compilatore" : standard;
                    OutputBox.Text = $"Compilazione riuscita ({usedStandard}).\n{_compileOutput}";
                    return true;
                }

                // Riprova soltanto se il problema è l'opzione -std; gli errori reali del codice non cambiano.
                bool unsupportedStandard = lastOutput.Contains("unrecognized command line option", StringComparison.OrdinalIgnoreCase)
                    || lastOutput.Contains("unrecognized option", StringComparison.OrdinalIgnoreCase)
                    || lastOutput.Contains("invalid argument", StringComparison.OrdinalIgnoreCase);
                if (!unsupportedStandard) break;
            }

            _compileOutput = lastOutput;
            OutputBox.Text = $"Compilazione non riuscita (codice {lastExitCode}).\nCompilatore: {gpp}\n\n{_compileOutput}";
            return false;
        }
        catch (Exception ex)
        {
            OutputBox.Text = "Errore durante la compilazione:\n" + ex.Message;
            return false;
        }
    }

    private void ChooseCompiler_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Seleziona il compilatore C++ g++.exe",
            Filter = "Compilatore C++ (g++.exe)|g++.exe|Eseguibili (*.exe)|*.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            CompilerPathBox.Text = dialog.FileName;
            SaveSettings();
            OutputBox.Text = "Compilatore selezionato:\n" + dialog.FileName;
        }
    }

    private async void TestServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string baseAddress = NormalizeServerAddress(ServerBox.Text);
            StatusText.Text = "Verifica...";
            using HttpResponseMessage response = await _http.GetAsync(baseAddress + "/ping");
            string message = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            StatusText.Text = "Server raggiungibile";
            MessageBox.Show(message, "Connessione al docente", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Server non raggiungibile";
            MessageBox.Show(BuildNetworkError(ex), "Connessione non riuscita", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(StudentIdBox.Text) ||
            string.IsNullOrWhiteSpace(StudentNameBox.Text) ||
            string.IsNullOrWhiteSpace(ExerciseBox.Text))
        {
            MessageBox.Show("Compila ID, nome e ID esercizio.");
            return;
        }

        try
        {
            SaveSettings();
            string address = NormalizeServerAddress(ServerBox.Text) + "/submit";
            var payload = new
            {
                studentId = StudentIdBox.Text.Trim(),
                studentName = StudentNameBox.Text.Trim(),
                className = ClassBox.Text.Trim(),
                exerciseId = ExerciseBox.Text.Trim(),
                sessionCode = SessionBox.Text.Trim(),
                code = Editor.Text,
                compileOutput = _compileOutput,
                programOutput = _programOutput
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            StatusText.Text = "Invio...";
            using HttpResponseMessage response = await _http.PostAsync(address, content);
            string message = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Il server ha risposto {(int)response.StatusCode}: {message}");

            StatusText.Text = "Consegnato: " + DateTime.Now.ToString("HH:mm:ss");
            MessageBox.Show("Compito inviato al docente.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Invio fallito";
            MessageBox.Show(BuildNetworkError(ex), "Impossibile inviare il compito", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string NormalizeServerAddress(string value)
    {
        string address = value.Trim();
        if (string.IsNullOrWhiteSpace(address)) throw new InvalidOperationException("Inserisci IP e porta del docente.");
        if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            address = "http://" + address;
        return address.TrimEnd('/');
    }

    private static string BuildNetworkError(Exception ex) =>
        "Non riesco a raggiungere il PC docente.\n\n" + ex.Message +
        "\n\nControlla che:\n" +
        "• il server sia avviato nella scheda Compiti alunni;\n" +
        "• IP, porta e codice sessione siano identici;\n" +
        "• i due PC siano nella stessa rete;\n" +
        "• il firewall di Windows consenta C++ Visual Base sulla rete privata;\n" +
        "• se il docente usa una macchina virtuale, la rete sia in modalità Bridge e non NAT.";

    private string FindGpp()
    {
        string selected = CompilerPathBox.Text.Trim().Trim('"');
        if (File.Exists(selected)) return selected;

        string[] candidates =
        {
            @"C:\msys64\ucrt64\bin\g++.exe",
            @"C:\msys64\mingw64\bin\g++.exe",
            @"C:\msys64\clang64\bin\g++.exe",
            @"C:\mingw64\bin\g++.exe",
            @"C:\MinGW\bin\g++.exe",
            @"C:\Program Files\CodeBlocks\MinGW\bin\g++.exe",
            @"C:\Program Files (x86)\CodeBlocks\MinGW\bin\g++.exe",
            @"C:\Program Files (x86)\Dev-Cpp\MinGW64\bin\g++.exe",
            @"C:\Program Files\Dev-Cpp\MinGW64\bin\g++.exe"
        };

        foreach (string candidate in candidates)
            if (File.Exists(candidate)) return candidate;

        try
        {
            using var process = Process.Start(new ProcessStartInfo("where.exe", "g++.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            string first = process?.StandardOutput.ReadLine()?.Trim() ?? "";
            process?.WaitForExit(3000);
            if (File.Exists(first)) return first;
        }
        catch { }

        return "";
    }

    private string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CppStudentClient", "settings.json");

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new
            {
                studentId = StudentIdBox.Text,
                studentName = StudentNameBox.Text,
                className = ClassBox.Text,
                exerciseId = ExerciseBox.Text,
                server = ServerBox.Text,
                sessionCode = SessionBox.Text,
                compilerPath = CompilerPathBox.Text
            }), Encoding.UTF8);
        }
        catch { }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            JsonElement root = document.RootElement;
            StudentIdBox.Text = Get(root, "studentId", "");
            StudentNameBox.Text = Get(root, "studentName", "");
            ClassBox.Text = Get(root, "className", "");
            ExerciseBox.Text = Get(root, "exerciseId", "");
            ServerBox.Text = Get(root, "server", ServerBox.Text);
            SessionBox.Text = Get(root, "sessionCode", "");
            CompilerPathBox.Text = Get(root, "compilerPath", "");
        }
        catch { }
    }

    private static string Get(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? fallback : fallback;
}
