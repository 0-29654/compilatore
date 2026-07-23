using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;

namespace CppStudentClient;

public partial class MainWindow : Window
{
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false, Proxy = null })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _modeTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private readonly Dictionary<string, ExerciseState> _exerciseStates = new(StringComparer.OrdinalIgnoreCase);

    private string _compileOutput = "";
    private string _programOutput = "";
    private string? _exePath;
    private string _activeKey = "";
    private DateTime _activeStartedUtc = DateTime.UtcNow;
    private bool _loadingExercise;
    private bool _verificationMode;
    private bool _allowClose;
    private bool _serverModeCheckRunning;
    private bool _compilationAllowed = true;
    private UdpClient? _teacherDiscoveryUdp;
    private CancellationTokenSource? _teacherDiscoveryCts;
    private const int TeacherDiscoveryPort = 5051;
    private IHighlightingDefinition? _cppHighlighting;

    private const string DefaultCode = "#include <iostream>\nusing namespace std;\n\nint main()\n{\n    \n    return 0;\n}\n";

    private string BundledCompilerRoot => Path.Combine(AppContext.BaseDirectory, "compiler", "ucrt64");
    private string BundledCompilerBin => Path.Combine(BundledCompilerRoot, "bin");
    private string BundledCompilerPath => Path.Combine(BundledCompilerBin, "g++.exe");

    public MainWindow()
    {
        InitializeComponent();
        ConfigureCppHighlighting();
        LoadSettings();
        // IP docente e codice sessione devono essere vuoti ad ogni avvio e vengono ricevuti dal server.
        ServerBox.Text = "";
        SessionBox.Text = "";
        StartTeacherDiscoveryListener();
        Closed += (_, _) => StopTeacherDiscoveryListener();
        if (!File.Exists(BundledCompilerPath))
            OutputBox.Text = "Installazione incompleta: compilatore C++17 incorporato assente. Reinstallare il programma.";
        LoadExerciseStates();
        ActivateExercise(GetTaskType(), GetExerciseNumber());

        _clockTimer.Tick += (_, _) => UpdateExerciseClock();
        _clockTimer.Start();
        _modeTimer.Tick += async (_, _) => await RefreshServerModeAsync(false);
        _modeTimer.Start();

        Loaded += async (_, _) => await RefreshServerModeAsync(false);
    }

    private void StartTeacherDiscoveryListener()
    {
        try
        {
            StopTeacherDiscoveryListener();
            _teacherDiscoveryCts = new CancellationTokenSource();
            _teacherDiscoveryUdp = new UdpClient(AddressFamily.InterNetwork);
            _teacherDiscoveryUdp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _teacherDiscoveryUdp.Client.Bind(new IPEndPoint(IPAddress.Any, TeacherDiscoveryPort));
            _ = TeacherDiscoveryLoopAsync(_teacherDiscoveryCts.Token);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ricezione automatica docente non disponibile";
            OutputBox.Text = "Impossibile ascoltare IP e codice sessione sulla porta UDP 5051:\n" + ex.Message;
        }
    }

    private void StopTeacherDiscoveryListener()
    {
        try { _teacherDiscoveryCts?.Cancel(); _teacherDiscoveryUdp?.Close(); } catch { }
        _teacherDiscoveryUdp?.Dispose();
        _teacherDiscoveryUdp = null;
        _teacherDiscoveryCts?.Dispose();
        _teacherDiscoveryCts = null;
    }

    private async Task TeacherDiscoveryLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _teacherDiscoveryUdp != null)
        {
            try
            {
                UdpReceiveResult packet = await _teacherDiscoveryUdp.ReceiveAsync(token);
                string json = Encoding.UTF8.GetString(packet.Buffer);
                using var document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                string app = Get(root, "app", "");
                if (!app.Equals("CVPlusTeacherDiscovery", StringComparison.OrdinalIgnoreCase) &&
                    !app.Equals("C++ Visual Base", StringComparison.OrdinalIgnoreCase))
                    continue;

                string ip = Get(root, "serverIp", packet.RemoteEndPoint.Address.ToString());
                int port = root.TryGetProperty("serverPort", out JsonElement portElement) && portElement.TryGetInt32(out int parsedPort)
                    ? parsedPort : 5050;
                string session = Get(root, "sessionCode", Get(root, "code", Get(root, "session", "")));
                string mode = Get(root, "mode", Get(root, "sessionMode", "esercitazione"));
                bool compileAllowed = ReadCompilationAllowed(root);

                await Dispatcher.InvokeAsync(() =>
                {
                    ServerBox.Text = $"{ip}:{port}";
                    SessionBox.Text = session;
                    ApplySessionMode(mode);
                    ApplyCompilationPermission(compileAllowed);
                    StatusText.Text = $"Docente rilevato: {ip}:{port}";
                });
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch
            {
                try { await Task.Delay(500, token); } catch { break; }
            }
        }
    }

    private static bool ReadCompilationAllowed(JsonElement root)
    {
        foreach (string name in new[] { "compileEnabled", "compilationEnabled", "allowCompile" })
            if (root.TryGetProperty(name, out JsonElement value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
                return value.GetBoolean();
        if (root.TryGetProperty("compilationDisabled", out JsonElement disabled) &&
            (disabled.ValueKind == JsonValueKind.True || disabled.ValueKind == JsonValueKind.False))
            return !disabled.GetBoolean();
        return true;
    }

    private void ApplyCompilationPermission(bool allowed)
    {
        _compilationAllowed = allowed;
        CompileButton.IsEnabled = allowed;
        RunButton.IsEnabled = allowed && !_verificationMode;
        if (!allowed)
        {
            StatusText.Text = "Compilazione inibita dal docente";
            OutputBox.Text = "Il docente ha temporaneamente inibito la compilazione sui client.";
        }
    }

    private void ConfigureCppHighlighting()
    {
        // Tavolozza ad alto contrasto: nessuna parola chiave usa il blu scuro.
        const string xshd = """
<?xml version="1.0"?>
<SyntaxDefinition name="C++ High Contrast" extensions=".cpp;.h;.hpp" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Comment" foreground="#78F0A4" />
  <Color name="String" foreground="#A7F3D0" />
  <Color name="Char" foreground="#FDE68A" />
  <Color name="Number" foreground="#FDBA74" />
  <Color name="Preprocessor" foreground="#FFF06A" />
  <Color name="Keyword" foreground="#FF83C6" />
  <Color name="Type" foreground="#67E8F9" />
  <Color name="Literal" foreground="#FCA5A5" />
  <RuleSet ignoreCase="false">
    <Span color="Comment" begin="//" end="\n" />
    <Span color="Comment" begin="/\*" end="\*/" />
    <Span color="String" begin="&quot;" end="&quot;" />
    <Span color="Char" begin="'" end="'" />
    <Span color="Preprocessor" begin="#" end="\n" />
    <Keywords color="Keyword">
      <Word>alignas</Word><Word>alignof</Word><Word>asm</Word><Word>auto</Word><Word>break</Word><Word>case</Word><Word>catch</Word><Word>class</Word><Word>const</Word><Word>constexpr</Word><Word>continue</Word><Word>default</Word><Word>delete</Word><Word>do</Word><Word>else</Word><Word>enum</Word><Word>explicit</Word><Word>export</Word><Word>extern</Word><Word>for</Word><Word>friend</Word><Word>goto</Word><Word>if</Word><Word>inline</Word><Word>namespace</Word><Word>new</Word><Word>noexcept</Word><Word>operator</Word><Word>private</Word><Word>protected</Word><Word>public</Word><Word>register</Word><Word>return</Word><Word>sizeof</Word><Word>static</Word><Word>struct</Word><Word>switch</Word><Word>template</Word><Word>this</Word><Word>throw</Word><Word>try</Word><Word>typedef</Word><Word>typename</Word><Word>union</Word><Word>using</Word><Word>virtual</Word><Word>volatile</Word><Word>while</Word>
    </Keywords>
    <Keywords color="Type">
      <Word>bool</Word><Word>char</Word><Word>char16_t</Word><Word>char32_t</Word><Word>double</Word><Word>float</Word><Word>int</Word><Word>long</Word><Word>short</Word><Word>signed</Word><Word>unsigned</Word><Word>void</Word><Word>wchar_t</Word><Word>string</Word><Word>vector</Word><Word>list</Word><Word>map</Word><Word>set</Word><Word>queue</Word><Word>stack</Word>
    </Keywords>
    <Keywords color="Literal"><Word>true</Word><Word>false</Word><Word>nullptr</Word><Word>NULL</Word></Keywords>
    <Rule color="Number">\b(0[xX][0-9a-fA-F]+|[0-9]+(\.[0-9]+)?)\b</Rule>
  </RuleSet>
</SyntaxDefinition>
""";
        try
        {
            using var reader = XmlReader.Create(new StringReader(xshd), new XmlReaderSettings { IgnoreComments = true });
            _cppHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            Editor.SyntaxHighlighting = _cppHighlighting;
        }
        catch (Exception ex)
        {
            // Non ricadere sulla tavolozza C++ predefinita (blu poco leggibile).
            _cppHighlighting = null;
            Editor.SyntaxHighlighting = null;
            OutputBox.Text = "Colorazione C++ ad alto contrasto non caricata: " + ex.Message;
        }
    }


    private void Editor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return;
        e.Handled = true;
        OpenFullscreenEditor();
    }

    private void OpenFullscreenEditor()
    {
        SaveCurrentExercise();

        var popupEditor = new TextEditor
        {
            Text = Editor.Text,
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 21,
            Background = new SolidColorBrush(Color.FromRgb(5, 11, 20)),
            Foreground = Brushes.White,
            LineNumbersForeground = new SolidColorBrush(Color.FromRgb(170, 190, 215)),
            Padding = new Thickness(18),
            SyntaxHighlighting = _cppHighlighting,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
        };

        var closeButton = new System.Windows.Controls.Button
        {
            Content = "Applica e torna al compilatore",
            MinWidth = 230,
            Padding = new Thickness(18, 10, 18, 10),
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromRgb(14, 143, 232)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold
        };

        var title = new System.Windows.Controls.TextBlock
        {
            Text = $"main.cpp — Tipologia {GetTaskType()} — Esercizio {GetExerciseNumber()} — C++17",
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 0)
        };

        var header = new System.Windows.Controls.Grid { Background = new SolidColorBrush(Color.FromRgb(11, 23, 41)) };
        header.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
        header.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(title);
        System.Windows.Controls.Grid.SetColumn(closeButton, 1);
        header.Children.Add(closeButton);

        var layout = new System.Windows.Controls.Grid();
        layout.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(64) });
        layout.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
        layout.Children.Add(header);
        System.Windows.Controls.Grid.SetRow(popupEditor, 1);
        layout.Children.Add(popupEditor);

        var popup = new Window
        {
            Title = "CV+ Editor C++17 a tutto schermo",
            Content = layout,
            Background = new SolidColorBrush(Color.FromRgb(5, 11, 20)),
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowState = WindowState.Maximized,
            Topmost = _verificationMode,
            ShowInTaskbar = !_verificationMode,
            WindowStyle = _verificationMode ? WindowStyle.None : WindowStyle.SingleBorderWindow,
            ResizeMode = _verificationMode ? ResizeMode.NoResize : ResizeMode.CanResize
        };

        bool applied = false;
        void ApplyAndClose()
        {
            applied = true;
            Editor.Text = popupEditor.Text;
            SaveCurrentExercise();
            popup.Close();
            Activate();
        }

        closeButton.Click += (_, _) => ApplyAndClose();
        popup.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && !_verificationMode)
            {
                args.Handled = true;
                ApplyAndClose();
            }
        };
        popup.Closing += (_, _) =>
        {
            if (!applied)
            {
                Editor.Text = popupEditor.Text;
                SaveCurrentExercise();
            }
        };
        popup.ShowDialog();
    }

    private async void Compile_Click(object sender, RoutedEventArgs e) => await CompileAsync();

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (!_compilationAllowed)
        {
            MessageBox.Show("La compilazione è stata inibita dal docente.", "Compilazione non disponibile", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_verificationMode)
        {
            MessageBox.Show("Durante una verifica l'esecuzione in una finestra CMD separata è disabilitata.", "Modalità verifica", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!await CompileAsync()) return;
        string bat = Path.Combine(Path.GetTempPath(), "cppstudent_run_" + Guid.NewGuid().ToString("N") + ".bat");
        File.WriteAllText(bat,
            $"@echo off\r\nset \"PATH={BundledCompilerBin};%PATH%\"\r\n\"{_exePath}\"\r\necho.\r\necho Programma terminato.\r\npause\r\n",
            Encoding.Default);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"") { UseShellExecute = true });
        _programOutput = "Esecuzione aperta nella finestra CMD.";
if (_exerciseStates.TryGetValue(_activeKey, out ExerciseState? activeState))
{
    activeState.ProgramOutput = _programOutput;
    activeState.CompileOutput = _compileOutput;
    SaveExerciseStates();
}
    }

    private async Task<bool> CompileAsync()
{
    SaveCurrentExercise();

    CompilationResult result = await CompileSourceAsync(Editor.Text, true);
    _compileOutput = result.CompileOutput;
    _exePath = result.ExePath;

    if (_exerciseStates.TryGetValue(_activeKey, out ExerciseState? state))
    {
        state.CompileOutput = result.CompileOutput;
        if (!result.Success)
        {
            state.ProgramOutput = "";
            _programOutput = "";
        }
        SaveExerciseStates();
    }

    return result.Success;
}

private async Task<CompilationResult> CompileSourceAsync(string sourceCode, bool updateOutputBox)
{
    if (!_compilationAllowed)
    {
        const string denied = "Il docente ha temporaneamente inibito la compilazione sui client.";
        if (updateOutputBox) OutputBox.Text = denied;
        return new CompilationResult(false, denied, null);
    }

    if (updateOutputBox)
        OutputBox.Text = "Compilazione C++17 in corso...";

    try
    {
        string gpp = BundledCompilerPath;
        if (!File.Exists(gpp))
        {
            string missing =
                "Installazione incompleta: il compilatore C++17 incorporato non è stato trovato.\n\n" +
                "Reinstalla CV+ Compilatore Alunno dalla Release ufficiale.";
            if (updateOutputBox) OutputBox.Text = missing;
            return new CompilationResult(false, missing, null);
        }

        string dir = Path.Combine(Path.GetTempPath(), "CppStudentClient");
        Directory.CreateDirectory(dir);

        string stem = "compito_" + Guid.NewGuid().ToString("N");
        string cpp = Path.Combine(dir, stem + ".cpp");
        string exe = Path.Combine(dir, stem + ".exe");

        File.WriteAllText(cpp, sourceCode, new UTF8Encoding(false));

        string arguments =
            $"-std=c++17 -Wall -Wextra -pedantic -fdiagnostics-color=never " +
            $"-o \"{exe}\" \"{cpp}\"";

        var psi = new ProcessStartInfo(gpp, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = dir,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        ConfigureCompilerEnvironment(psi);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Impossibile avviare il compilatore C++17.");

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());

        string stdout = (await stdoutTask).Trim();
        string stderr = (await stderrTask).Trim();

        var diagnosticParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(stderr)) diagnosticParts.Add(stderr);
        if (!string.IsNullOrWhiteSpace(stdout)) diagnosticParts.Add(stdout);

        string diagnostics = string.Join(
            Environment.NewLine + Environment.NewLine,
            diagnosticParts
        ).Trim();

        bool success = process.ExitCode == 0 && File.Exists(exe);

        if (success)
        {
            string successText = string.IsNullOrWhiteSpace(diagnostics)
                ? "Compilazione riuscita in C++17. Nessun errore o avviso."
                : "Compilazione riuscita in C++17.\n\n" + diagnostics;

            if (updateOutputBox) OutputBox.Text = successText;
            return new CompilationResult(true, successText, exe);
        }

        if (string.IsNullOrWhiteSpace(diagnostics))
        {
            diagnostics =
                "Il compilatore ha restituito il codice di errore " +
                process.ExitCode +
                " senza fornire un messaggio diagnostico.";
        }

        string errorText =
            $"Compilazione C++17 non riuscita (codice {process.ExitCode}).\n\n{diagnostics}";

        if (updateOutputBox) OutputBox.Text = errorText;
        return new CompilationResult(false, errorText, null);
    }
    catch (Exception ex)
    {
        string errorText =
            "Errore durante la compilazione C++17:\n" +
            ex.GetType().Name + ": " + ex.Message;

        if (updateOutputBox) OutputBox.Text = errorText;
        return new CompilationResult(false, errorText, null);
    }
} 

    private async Task<ExecutionResult> RunCapturedAsync(
    string exePath,
    int timeoutSeconds = 5)
{
    try
    {
        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WorkingDirectory =
                Path.GetDirectoryName(exePath) ??
                Path.GetTempPath(),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        ConfigureCompilerEnvironment(psi);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                "Impossibile avviare il programma compilato."
            );

        process.StandardInput.Close();

        Task<string> stdoutTask =
            process.StandardOutput.ReadToEndAsync();

        Task<string> stderrTask =
            process.StandardError.ReadToEndAsync();

        Task completed = await Task.WhenAny(
            process.WaitForExitAsync(),
            Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))
        );

        if (!process.HasExited)
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
            }

            await process.WaitForExitAsync();

            string partialOutput = (await stdoutTask).Trim();
            string partialError = (await stderrTask).Trim();

            string timeoutMessage =
                $"Esecuzione interrotta dopo {timeoutSeconds} secondi. " +
                "Possibile ciclo infinito o programma in attesa di input.";

            if (!string.IsNullOrWhiteSpace(partialOutput))
            {
                timeoutMessage +=
                    "\n\nOUTPUT PARZIALE\n" +
                    partialOutput;
            }

            if (!string.IsNullOrWhiteSpace(partialError))
            {
                timeoutMessage +=
                    "\n\nERRORI DI ESECUZIONE\n" +
                    partialError;
            }

            return new ExecutionResult(
                false,
                timeoutMessage,
                null,
                true
            );
        }

        string stdout = (await stdoutTask).Trim();
        string stderr = (await stderrTask).Trim();

        var sections = new List<string>
        {
            process.ExitCode == 0
                ? "ESECUZIONE TERMINATA CORRETTAMENTE"
                : "ESECUZIONE TERMINATA IN MODO ANOMALO",

            $"Codice di uscita: {process.ExitCode}",

            string.IsNullOrWhiteSpace(stdout)
                ? "OUTPUT PROGRAMMA\nNessun testo prodotto."
                : "OUTPUT PROGRAMMA\n" + stdout
        };

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            sections.Add(
                "ERRORI DI ESECUZIONE\n" +
                stderr
            );
        }

        if (process.ExitCode != 0 &&
            string.IsNullOrWhiteSpace(stderr))
        {
            sections.Add(
                "Possibile errore durante l'esecuzione: " +
                "controlla divisioni o modulo per zero, " +
                "accessi non validi alla memoria e altri errori runtime."
            );
        }

        return new ExecutionResult(
            process.ExitCode == 0,
            string.Join(
                Environment.NewLine +
                Environment.NewLine,
                sections
            ),
            process.ExitCode,
            false
        );
    }
    catch (Exception ex)
    {
        return new ExecutionResult(
            false,
            "Errore durante l'esecuzione:\n" +
            ex.GetType().Name + ": " + ex.Message,
            null,
            false
        );
    }
}
private async void TestServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string baseAddress = NormalizeServerAddress(ServerBox.Text);
            StatusText.Text = "Verifica server...";
            using HttpResponseMessage response = await _http.GetAsync(baseAddress + "/ping");
            string message = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            await RefreshServerModeAsync(true);
            StatusText.Text = "Server raggiungibile";
            MessageBox.Show(message + $"\n\nModalità: {(_verificationMode ? "VERIFICA" : "ESERCITAZIONE")}", "Connessione al docente", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Server non raggiungibile";
            MessageBox.Show(BuildNetworkError(ex), "Connessione non riuscita", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task RefreshServerModeAsync(bool showErrors)
    {
        if (_serverModeCheckRunning || string.IsNullOrWhiteSpace(ServerBox.Text)) return;
        _serverModeCheckRunning = true;
        try
        {
            string baseAddress = NormalizeServerAddress(ServerBox.Text);
            string session = Uri.EscapeDataString(SessionBox.Text.Trim());
            string[] endpoints = { $"/session-status?sessionCode={session}", $"/mode?sessionCode={session}" };
            foreach (string endpoint in endpoints)
            {
                try
                {
                    using HttpResponseMessage response = await _http.GetAsync(baseAddress + endpoint);
                    if (!response.IsSuccessStatusCode) continue;
                    string body = await response.Content.ReadAsStringAsync();
                    ApplyServerModeResponse(body);
                    return;
                }
                catch { }
            }
            // Se il server non espone ancora l'endpoint modalità, mantieni la modalità corrente.
        }
        catch (Exception ex)
        {
            if (showErrors) MessageBox.Show(BuildNetworkError(ex), "Modalità sessione non disponibile", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _serverModeCheckRunning = false; }
    }

    private void ApplyServerModeResponse(string body)
    {
        string mode = "esercitazione";
        try
        {
            using var doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("mode", out JsonElement modeEl)) mode = modeEl.GetString() ?? mode;
            else if (root.TryGetProperty("sessionMode", out JsonElement smEl)) mode = smEl.GetString() ?? mode;
            if (root.TryGetProperty("taskType", out JsonElement typeEl) && !string.IsNullOrWhiteSpace(typeEl.GetString()))
                TaskTypeBox.Text = typeEl.GetString()!;
            string serverIp = Get(root, "serverIp", "");
            if (!string.IsNullOrWhiteSpace(serverIp))
            {
                int serverPort = root.TryGetProperty("serverPort", out JsonElement portEl) && portEl.TryGetInt32(out int parsedPort) ? parsedPort : 5050;
                ServerBox.Text = $"{serverIp}:{serverPort}";
            }
            string receivedSession = Get(root, "sessionCode", Get(root, "code", Get(root, "session", "")));
            if (!string.IsNullOrWhiteSpace(receivedSession)) SessionBox.Text = receivedSession;
            ApplyCompilationPermission(ReadCompilationAllowed(root));
        }
        catch
        {
            if (body.Contains("verifica", StringComparison.OrdinalIgnoreCase)) mode = "verifica";
        }
        ApplySessionMode(mode);
    }

    private void ApplySessionMode(string mode)
    {
        bool verify = mode.Equals("verifica", StringComparison.OrdinalIgnoreCase) || mode.Equals("test", StringComparison.OrdinalIgnoreCase);
        if (verify == _verificationMode) return;
        _verificationMode = verify;
        if (verify) EnterVerificationMode(); else ExitVerificationMode();
    }

    private void EnterVerificationMode()
    {
        SaveCurrentExercise();
        ModeText.Text = "VERIFICA";
        ModeDot.Fill = new SolidColorBrush(Color.FromRgb(255, 184, 76));
        ModeBadge.Background = new SolidColorBrush(Color.FromRgb(75, 38, 15));
        ModeBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(199, 110, 34));
        SendButton.Content = "Invio compito";
        RunButton.IsEnabled = false;
        CompileButton.IsEnabled = _compilationAllowed;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Topmost = true;
        ShowInTaskbar = false;
        StatusText.Text = "Modalità verifica attiva";
        Activate();
    }

    private void ExitVerificationMode()
    {
        ModeText.Text = "ESERCITAZIONE";
        ModeDot.Fill = new SolidColorBrush(Color.FromRgb(52, 211, 153));
        ModeBadge.Background = new SolidColorBrush(Color.FromRgb(16, 45, 37));
        ModeBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(31, 109, 85));
        SendButton.Content = "Invia al docente";
        ApplyCompilationPermission(_compilationAllowed);
        Topmost = false;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
    }

    private int? AskExerciseCount(int suggestedCount)
{
    var input = new System.Windows.Controls.TextBox
    {
        Text = Math.Max(1, suggestedCount).ToString(),
        FontSize = 22,
        MinWidth = 220,
        Margin = new Thickness(0, 16, 0, 16),
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    var sendButton = new System.Windows.Controls.Button
    {
        Content = "Invia",
        IsDefault = true,
        MinWidth = 120,
        Margin = new Thickness(6)
    };

    var cancelButton = new System.Windows.Controls.Button
    {
        Content = "Annulla",
        IsCancel = true,
        MinWidth = 120,
        Margin = new Thickness(6)
    };

    var buttons = new System.Windows.Controls.StackPanel
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center
    };

    buttons.Children.Add(sendButton);
    buttons.Children.Add(cancelButton);

    var panel = new System.Windows.Controls.StackPanel
    {
        Margin = new Thickness(28)
    };

    panel.Children.Add(new System.Windows.Controls.TextBlock
    {
        Text =
            "Quanti esercizi vuoi inviare?\n\n" +
            "Inserendo 5 verranno inviati gli esercizi 1, 2, 3, 4 e 5.",
        FontSize = 17,
        TextWrapping = TextWrapping.Wrap
    });

    panel.Children.Add(input);
    panel.Children.Add(buttons);

    var dialog = new Window
    {
        Title = "Numero di esercizi da inviare",
        Owner = this,
        Content = panel,
        SizeToContent = SizeToContent.WidthAndHeight,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false,
        Topmost = true
    };

    int? result = null;

    sendButton.Click += (_, _) =>
    {
        if (!int.TryParse(input.Text.Trim(), out int count) ||
            count < 1 ||
            count > 100)
        {
            MessageBox.Show(
                dialog,
                "Inserisci un numero intero compreso tra 1 e 100.",
                "Numero non valido",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );

            input.Focus();
            input.SelectAll();
            return;
        }

        result = count;
        dialog.DialogResult = true;
    };

    dialog.Loaded += (_, _) =>
    {
        input.Focus();
        input.SelectAll();
    };

    dialog.ShowDialog();
    Activate();

    return result;
}
private async void Send_Click(object sender, RoutedEventArgs e)
{
    SaveCurrentExercise();

    if (!ValidateSubmission(
            out int registerNumber,
            out int activeExerciseNumber))
    {
        return;
    }

    int? selectedCount =
        AskExerciseCount(activeExerciseNumber);

    if (!selectedCount.HasValue)
    {
        StatusText.Text = "Invio annullato";
        return;
    }

    int exerciseCount = selectedCount.Value;
    string type = GetTaskType();

    MessageBoxResult confirmation = MessageBox.Show(
        this,
        $"Confermi l'invio?\n\n" +
        $"Modalità: {(_verificationMode ? "VERIFICA" : "ESERCITAZIONE")}\n" +
        $"N° registro: {registerNumber}\n" +
        $"Nome: {StudentNameBox.Text.Trim()}\n" +
        $"Classe: {ClassBox.Text.Trim()}\n" +
        $"Tipologia: {type}\n" +
        $"Esercizi: da 1 a {exerciseCount}\n\n" +
        "Ogni esercizio verrà compilato ed eseguito in modo nascosto. " +
        "Saranno inviati codice, errori e output.",
        "Conferma consegna multipla",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question,
        MessageBoxResult.No
    );

    if (confirmation != MessageBoxResult.Yes)
    {
        StatusText.Text = "Invio annullato";
        return;
    }

    try
    {
        SaveSettings();

        string address =
            NormalizeServerAddress(ServerBox.Text) +
            "/submit";

        int sent = 0;
        var failedExercises = new List<int>();

        for (int exerciseNumber = 1;
             exerciseNumber <= exerciseCount;
             exerciseNumber++)
        {
            string key =
                BuildExerciseKey(type, exerciseNumber);

            if (!_exerciseStates.TryGetValue(
                    key,
                    out ExerciseState? state))
            {
                state = new ExerciseState
                {
                    Code = DefaultCode,
                    Elapsed = TimeSpan.Zero
                };

                _exerciseStates[key] = state;
            }

            string code =
                string.IsNullOrWhiteSpace(state.Code)
                ? DefaultCode
                : state.Code;

            StatusText.Text =
                $"Compilazione esercizio {exerciseNumber} di {exerciseCount}...";

            string previousEditorText = Editor.Text;
            string previousActiveKey = _activeKey;

            Editor.Text = code;
            _activeKey = key;

            bool compilationSucceeded =
                await CompileAsync();

            string compileOutput =
                _compileOutput;

            string? exePath =
                _exePath;

            Editor.Text = previousEditorText;
            _activeKey = previousActiveKey;

            ExecutionResult execution;

            if (compilationSucceeded &&
                !string.IsNullOrWhiteSpace(exePath))
            {
                StatusText.Text =
                    $"Esecuzione esercizio {exerciseNumber} di {exerciseCount}...";

                execution = await RunCapturedAsync(
                    exePath,
                    5
                );
            }
            else
            {
                execution = new ExecutionResult(
                    false,
                    "Programma non eseguito perché la compilazione non è riuscita.",
                    null,
                    false
                );
            }

            state.CompileOutput = compileOutput;
            state.ProgramOutput = execution.Output;

            string combinedOutput =
                compileOutput +
                Environment.NewLine +
                Environment.NewLine +
                execution.Output;

            var timings =
                _exerciseStates.ToDictionary(
                    pair => pair.Key,
                    pair => (long)pair.Value.Elapsed.TotalSeconds
                );

            var payload = new
            {
                studentId = registerNumber.ToString(),
                registerNumber,
                studentName = StudentNameBox.Text.Trim(),
                className = ClassBox.Text.Trim(),
                taskType = type,
                exerciseId = exerciseNumber.ToString(),
                exerciseNumber,
                totalExercises = exerciseCount,
                sessionCode = SessionBox.Text.Trim(),
                sessionMode =
                    _verificationMode
                    ? "verifica"
                    : "esercitazione",
                exerciseTimeSeconds =
                    (long)state.Elapsed.TotalSeconds,
                exerciseTimes = timings,
                code,
                compilationSucceeded,
                compileOutput,
                executionSucceeded = execution.Success,
                executionExitCode = execution.ExitCode,
                executionTimedOut = execution.TimedOut,
                programOutput = execution.Output,
                output = combinedOutput
            };

            using var content =
                new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                );

            StatusText.Text =
                $"Invio esercizio {exerciseNumber} di {exerciseCount}...";

            using HttpResponseMessage response =
                await _http.PostAsync(address, content);

            string message =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                failedExercises.Add(exerciseNumber);
                continue;
            }

            sent++;
        }

        SaveExerciseStates();

        if (failedExercises.Count > 0)
        {
            StatusText.Text =
                $"Inviati {sent} esercizi su {exerciseCount}";

            MessageBox.Show(
                this,
                $"Sono stati inviati {sent} esercizi su {exerciseCount}.\n\n" +
                "Invio non riuscito per: " +
                string.Join(", ", failedExercises),
                "Invio completato parzialmente",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );

            return;
        }

        StatusText.Text =
            $"Consegnati {sent} esercizi: {DateTime.Now:HH:mm:ss}";

        MessageBox.Show(
            this,
            $"Sono stati inviati gli esercizi da 1 a {exerciseCount}.\n\n" +
            "Per ogni esercizio sono stati inviati codice, compilazione e output.",
            "Consegna completata",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        if (_verificationMode)
        {
            ClearLocalVerificationData();
            _allowClose = true;
            Close();
        }
    }
    catch (Exception ex)
    {
        StatusText.Text = "Invio fallito";

        MessageBox.Show(
            this,
            BuildNetworkError(ex),
            "Impossibile inviare il compito",
            MessageBoxButton.OK,
            MessageBoxImage.Warning
        );
    }
}  

    private bool ValidateSubmission(out int registerNumber, out int exerciseNumber)
    {
        registerNumber = 0; exerciseNumber = 0;
        if (string.IsNullOrWhiteSpace(StudentIdBox.Text) || string.IsNullOrWhiteSpace(StudentNameBox.Text) || string.IsNullOrWhiteSpace(TaskTypeBox.Text) || string.IsNullOrWhiteSpace(ExerciseBox.Text))
        {
            MessageBox.Show("Compila N° registro, nome e cognome, tipologia e N° esercizio.", "Dati mancanti", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (!int.TryParse(StudentIdBox.Text.Trim(), out registerNumber) || registerNumber <= 0)
        {
            MessageBox.Show("Il N° registro alunno deve essere un numero intero maggiore di zero.", "Numero non valido", MessageBoxButton.OK, MessageBoxImage.Warning);
            StudentIdBox.Focus(); StudentIdBox.SelectAll(); return false;
        }
        if (!int.TryParse(ExerciseBox.Text.Trim(), out exerciseNumber) || exerciseNumber <= 0)
        {
            MessageBox.Show("Il N° esercizio deve essere un numero intero maggiore di zero.", "Numero non valido", MessageBoxButton.OK, MessageBoxImage.Warning);
            ExerciseBox.Focus(); ExerciseBox.SelectAll(); return false;
        }
        return true;
    }

    private void PreviousExercise_Click(object sender, RoutedEventArgs e) => SwitchExercise(-1);
    private void NextExercise_Click(object sender, RoutedEventArgs e) => SwitchExercise(1);

    private void SwitchExercise(int delta)
    {
        SaveCurrentExercise();
        int current = GetExerciseNumber();
        int next = Math.Max(1, current + delta);
        ExerciseBox.Text = next.ToString();
        ActivateExercise(GetTaskType(), next);
        SaveSettings();
    }

    private void TaskIdentity_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveCurrentExercise();
        ActivateExercise(GetTaskType(), GetExerciseNumber());
        SaveSettings();
    }

    private void ActivateExercise(string type, int number)
    {
        string key = BuildExerciseKey(type, number);
        if (_activeKey.Equals(key, StringComparison.OrdinalIgnoreCase)) return;
        _activeKey = key;
        if (!_exerciseStates.TryGetValue(key, out ExerciseState? state))
        {
            state = new ExerciseState { Code = DefaultCode, Elapsed = TimeSpan.Zero };
            _exerciseStates[key] = state;
        }
        _loadingExercise = true;
        Editor.Text = string.IsNullOrWhiteSpace(state.Code) ? DefaultCode : state.Code;
        _loadingExercise = false;
        _activeStartedUtc = DateTime.UtcNow;
        StatusText.Text = $"Tipologia {type} - esercizio {number}";
        UpdateExerciseClock();
    }

    private void SaveCurrentExercise()
    {
        if (string.IsNullOrWhiteSpace(_activeKey)) return;
        if (!_exerciseStates.TryGetValue(_activeKey, out ExerciseState? state)) state = _exerciseStates[_activeKey] = new ExerciseState();
        state.Code = Editor.Text;
        state.Elapsed += DateTime.UtcNow - _activeStartedUtc;
        _activeStartedUtc = DateTime.UtcNow;
        SaveExerciseStates();
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_loadingExercise || string.IsNullOrWhiteSpace(_activeKey)) return;
        if (_exerciseStates.TryGetValue(_activeKey, out ExerciseState? state)) state.Code = Editor.Text;
    }

    private TimeSpan GetElapsedForActive()
    {
        TimeSpan stored = _exerciseStates.TryGetValue(_activeKey, out ExerciseState? state) ? state.Elapsed : TimeSpan.Zero;
        return stored + (DateTime.UtcNow - _activeStartedUtc);
    }

    private void UpdateExerciseClock() => ExerciseTimeText.Text = "Tempo esercizio: " + FormatDuration(GetElapsedForActive());
    private static string FormatDuration(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    private string GetTaskType() => string.IsNullOrWhiteSpace(TaskTypeBox.Text) ? "A" : TaskTypeBox.Text.Trim();
    private int GetExerciseNumber() => int.TryParse(ExerciseBox.Text.Trim(), out int n) && n > 0 ? n : 1;
    private string BuildExerciseKey(string type, int number) => $"{SessionBox.Text.Trim().ToUpperInvariant()}|{type.Trim().ToUpperInvariant()}|{number}";

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_verificationMode) return;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 && e.Key == Key.F4) { e.Handled = true; return; }
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 && e.Key == Key.Tab) { e.Handled = true; Activate(); return; }
        if (e.Key == Key.LWin || e.Key == Key.RWin) { e.Handled = true; return; }

        // Uscita di emergenza riservata al docente: Ctrl+Shift+F12, poi codice sessione.
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.F12)
        {
            e.Handled = true;
            string entered = Microsoft.VisualBasic.Interaction.InputBox("Inserisci il codice sessione docente per uscire dalla modalità verifica.", "Sblocco docente", "");
            if (!string.IsNullOrWhiteSpace(entered) && entered == SessionBox.Text.Trim()) ApplySessionMode("esercitazione");
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_verificationMode) Dispatcher.BeginInvoke(new Action(() => { Topmost = true; Activate(); }), DispatcherPriority.ApplicationIdle);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_verificationMode && !_allowClose)
        {
            e.Cancel = true;
            Activate();
            return;
        }
        SaveCurrentExercise();
        SaveSettings();
    }

    private static string NormalizeServerAddress(string value)
    {
        string address = value.Trim();
        if (string.IsNullOrWhiteSpace(address)) throw new InvalidOperationException("Inserisci IP e porta del docente.");
        if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !address.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) address = "http://" + address;
        return address.TrimEnd('/');
    }

    private static string BuildNetworkError(Exception ex) =>
        "Non riesco a raggiungere il PC docente.\n\n" + ex.Message +
        "\n\nControlla che:\n• il server sia avviato nella scheda Compiti alunni;\n• IP, porta e codice sessione siano identici;\n• i due PC siano nella stessa rete;\n• il firewall consenta il server;\n• con una macchina virtuale sia usata la rete Bridge.";

    private void ConfigureCompilerEnvironment(ProcessStartInfo psi)
    {
        string currentPath = psi.Environment.TryGetValue("PATH", out string? value) ? value ?? "" : Environment.GetEnvironmentVariable("PATH") ?? "";
        psi.Environment["PATH"] = BundledCompilerBin + Path.PathSeparator + currentPath;
    }

    private string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CppStudentClient");
    private string SettingsPath => Path.Combine(DataFolder, "settings.json");
    private string ExerciseStatePath => Path.Combine(DataFolder, "exercise-state.json");

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new
            {
                studentId = StudentIdBox.Text, studentName = StudentNameBox.Text, className = ClassBox.Text,
                taskType = TaskTypeBox.Text, exerciseId = ExerciseBox.Text, server = "",
                sessionCode = ""
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
            TaskTypeBox.Text = Get(root, "taskType", "A");
            ExerciseBox.Text = Get(root, "exerciseId", "1");
            ServerBox.Text = "";
            SessionBox.Text = "";
        }
        catch { }
    }

    private void SaveExerciseStates()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(ExerciseStatePath, JsonSerializer.Serialize(_exerciseStates), Encoding.UTF8);
        }
        catch { }
    }

    private void LoadExerciseStates()
    {
        try
        {
            if (!File.Exists(ExerciseStatePath)) return;
            var states = JsonSerializer.Deserialize<Dictionary<string, ExerciseState>>(File.ReadAllText(ExerciseStatePath));
            if (states == null) return;
            foreach (var pair in states) _exerciseStates[pair.Key] = pair.Value;
        }
        catch { }
    }

    private void ClearLocalVerificationData()
    {
        _exerciseStates.Clear();
        try { if (File.Exists(ExerciseStatePath)) File.Delete(ExerciseStatePath); } catch { }
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                JsonElement root = doc.RootElement;
                Directory.CreateDirectory(DataFolder);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new
                {
                    studentId = Get(root, "studentId", ""), studentName = Get(root, "studentName", ""), className = Get(root, "className", ""),
                    taskType = "A", exerciseId = "1", server = "", sessionCode = ""
                }), Encoding.UTF8);
            }
        }
        catch { }
    }

    private static string Get(JsonElement root, string name, string fallback) => root.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? fallback : fallback;

    private sealed record CompilationResult(
    bool Success,
    string CompileOutput,
    string? ExePath
);

private sealed record ExecutionResult(
    bool Success,
    string Output,
    int? ExitCode,
    bool TimedOut
);

public sealed class ExerciseState
{
    public string Code { get; set; } = DefaultCode;
    public TimeSpan Elapsed { get; set; } = TimeSpan.Zero;
    public string CompileOutput { get; set; } = "";
    public string ProgramOutput { get; set; } = "";
}
}
