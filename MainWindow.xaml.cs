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
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private bool _modalDialogOpen;
    private System.Windows.Controls.Grid? _activeOverlay;
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
        ResetClientStateOnStartup();
        StartTeacherDiscoveryListener();
        Closed += (_, _) => StopTeacherDiscoveryListener();
        if (!File.Exists(BundledCompilerPath))
            OutputBox.Text = "Installazione incompleta: compilatore C++17 incorporato assente. Reinstallare il programma.";
        ActivateExercise(GetTaskType(), GetExerciseNumber());

        _clockTimer.Tick += (_, _) => UpdateExerciseClock();
        _clockTimer.Start();
        _modeTimer.Tick += async (_, _) => await RefreshServerModeAsync(false);
        _modeTimer.Start();

        Loaded += async (_, _) =>
        {
            UpdateLocalIpText();
            UpdateTaskSummary();
            await RefreshServerModeAsync(false);
        };
    }

    private void UpdateLocalIpText()
    {
        string localIp =
            GetLocalIpv4Addresses().FirstOrDefault() ??
            "non disponibile";

        LocalIpText.Text = "IP: " + localIp;
    }

    private void UpdateTaskSummary()
    {
        string type =
            string.IsNullOrWhiteSpace(TaskTypeBox.Text)
            ? "—"
            : TaskTypeBox.Text.Trim().ToUpperInvariant();

        CurrentTaskSummaryText.Text =
            $"Tipologia {type} • esercizio {GetExerciseNumber()}";
    }

    private void ResetClientStateOnStartup()
    {
        _exerciseStates.Clear();
        try { if (File.Exists(SettingsPath)) File.Delete(SettingsPath); } catch { }
        try { if (File.Exists(ExerciseStatePath)) File.Delete(ExerciseStatePath); } catch { }

        StudentIdBox.Text = "1";
        StudentNameBox.Text = "";
        ClassBox.Text = "";
        TaskTypeBox.Text = "";
        ExerciseBox.Text = "1";
        ServerBox.Text = "";
        SessionBox.Text = "";
        Editor.Text = DefaultCode;
        HeaderEditor.Text = "";
        HeaderTab.Visibility = Visibility.Collapsed;
        OutputBox.Text = "";
        StatusText.Text = "Pronto - nuova sessione";
        UpdateTaskSummary();
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
                string command = Get(root, "command", "");

                await Dispatcher.InvokeAsync(() =>
                {
                    if (command.Equals("closeClients", StringComparison.OrdinalIgnoreCase))
                    {
                        _allowClose = true;
                        ClearLocalVerificationData();
                        Close();
                        return;
                    }
                    ServerBox.Text = $"{ip}:{port}";
                    SetSessionCode(session);
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


    private void SetSessionCode(string newSession)
    {
        newSession = (newSession ?? string.Empty).Trim();
        string currentSession = SessionBox.Text.Trim();

        if (currentSession.Equals(newSession, StringComparison.OrdinalIgnoreCase))
            return;

        SaveCurrentExercise();

        string oldKey = _activeKey;
        ExerciseState? oldState = null;
        if (!string.IsNullOrWhiteSpace(oldKey))
            _exerciseStates.TryGetValue(oldKey, out oldState);

        SessionBox.Text = newSession;

        string newKey = BuildExerciseKey(GetTaskType(), GetExerciseNumber());

        if (oldState != null && !_exerciseStates.ContainsKey(newKey))
            _exerciseStates[newKey] = oldState;

        if (!string.IsNullOrWhiteSpace(oldKey) &&
            !oldKey.Equals(newKey, StringComparison.OrdinalIgnoreCase))
        {
            _exerciseStates.Remove(oldKey);
        }

        _activeKey = newKey;
        SaveExerciseStates();
    }

    private static bool ReadCompilationAllowed(JsonElement root)
    {
        bool globallyAllowed = true;

        foreach (string name in new[] { "compileEnabled", "compilationEnabled", "allowCompile" })
        {
            if (root.TryGetProperty(name, out JsonElement value) &&
                (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                globallyAllowed = value.GetBoolean();
                break;
            }
        }

        if (root.TryGetProperty("compilationDisabled", out JsonElement disabled) &&
            (disabled.ValueKind == JsonValueKind.True || disabled.ValueKind == JsonValueKind.False))
        {
            globallyAllowed = !disabled.GetBoolean();
        }

        if (!globallyAllowed)
            return false;

        foreach (string propertyName in new[] { "disabledClientIps", "compilationDisabledClientIps" })
        {
            if (!root.TryGetProperty(propertyName, out JsonElement list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var disabledIps = list
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string localIp in GetLocalIpv4Addresses())
            {
                if (disabledIps.Contains(localIp))
                    return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> GetLocalIpv4Addresses()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .Where(address =>
                    address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address))
                .Select(address => address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private void ApplyCompilationPermission(bool allowed)
    {
        _compilationAllowed = allowed;
        RunButton.IsEnabled = allowed;
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
            HeaderEditor.SyntaxHighlighting = _cppHighlighting;
        }
        catch (Exception ex)
        {
            // Non ricadere sulla tavolozza C++ predefinita (blu poco leggibile).
            _cppHighlighting = null;
            Editor.SyntaxHighlighting = null;
            HeaderEditor.SyntaxHighlighting = null;
            OutputBox.Text = "Colorazione C++ ad alto contrasto non caricata: " + ex.Message;
        }
    }



    private void CloseActiveOverlay()
    {
        if (_activeOverlay == null)
            return;

        RootLayout.Children.Remove(_activeOverlay);
        _activeOverlay = null;
        _modalDialogOpen = false;

        if (_verificationMode)
        {
            Topmost = true;
            WindowState = WindowState.Maximized;
            Activate();
            Focus();
        }
    }

    private void ShowFullscreenOverlay(
        string title,
        FrameworkElement content,
        IEnumerable<System.Windows.Controls.Button>? buttons = null,
        Action? closingAction = null)
    {
        CloseActiveOverlay();

        _modalDialogOpen = true;

        var overlay = new System.Windows.Controls.Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(252, 3, 9, 18))
        };

        overlay.RowDefinitions.Add(
            new System.Windows.Controls.RowDefinition { Height = new GridLength(72) });
        overlay.RowDefinitions.Add(
            new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        overlay.RowDefinitions.Add(
            new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

        var heading = new System.Windows.Controls.TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(22, 0, 22, 0)
        };

        var closeTop = new System.Windows.Controls.Button
        {
            Content = "✕ Chiudi",
            MinWidth = 120,
            Padding = new Thickness(16, 9, 16, 9),
            Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold
        };

        var header = new System.Windows.Controls.Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(11, 23, 41))
        };
        header.Children.Add(heading);
        header.Children.Add(closeTop);

        System.Windows.Controls.Grid.SetRow(header, 0);
        System.Windows.Controls.Grid.SetRow(content, 1);
        overlay.Children.Add(header);
        overlay.Children.Add(content);

        var footer = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromRgb(11, 23, 41)),
            Margin = new Thickness(12)
        };

        if (buttons != null)
        {
            foreach (var button in buttons)
                footer.Children.Add(button);
        }

        if (footer.Children.Count > 0)
        {
            System.Windows.Controls.Grid.SetRow(footer, 2);
            overlay.Children.Add(footer);
        }

        void CloseOverlay()
        {
            closingAction?.Invoke();
            CloseActiveOverlay();
        }

        closeTop.Click += (_, _) => CloseOverlay();

        overlay.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                CloseOverlay();
            }
        };

        System.Windows.Controls.Grid.SetRowSpan(overlay, RootLayout.RowDefinitions.Count);
        System.Windows.Controls.Panel.SetZIndex(overlay, 10000);

        _activeOverlay = overlay;
        RootLayout.Children.Add(overlay);
        overlay.Focusable = true;
        overlay.Focus();
    }

    private void OutputBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            return;

        e.Handled = true;
        OpenFullscreenOutput();
    }

    private void OpenFullscreenOutput()
    {
        var fullOutput = new System.Windows.Controls.TextBox
        {
            Text = OutputBox.Text,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 19,
            Background = new SolidColorBrush(Color.FromRgb(5, 11, 20)),
            Foreground = new SolidColorBrush(Color.FromRgb(231, 244, 255)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(20)
        };

        var copyButton = new System.Windows.Controls.Button
        {
            Content = "Copia tutto",
            MinWidth = 130,
            Padding = new Thickness(18, 10, 18, 10),
            Margin = new Thickness(6),
            Background = new SolidColorBrush(Color.FromRgb(36, 52, 77)),
            Foreground = Brushes.White
        };

        copyButton.Click += (_, _) =>
        {
            Clipboard.SetText(fullOutput.Text ?? string.Empty);
            StatusText.Text = "Output copiato negli appunti";
        };

        ShowFullscreenOverlay(
            $"Compilazione ed esecuzione — Tipologia {GetTaskType()} — Esercizio {GetExerciseNumber()}",
            fullOutput,
            new[] { copyButton }
        );
    }

    private void Editor_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            return;

        e.Handled = true;
        OpenFullscreenCodeEditor(Editor, "main.cpp");
    }

    private void HeaderEditor_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            return;

        e.Handled = true;
        OpenFullscreenCodeEditor(
            HeaderEditor,
            GetCurrentHeaderFileName()
        );
    }

    private void OpenFullscreenCodeEditor(
        TextEditor sourceEditor,
        string displayName)
    {
        SaveCurrentExercise();

        var popupEditor = new TextEditor
        {
            Text = sourceEditor.Text,
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

        var applyButton = new System.Windows.Controls.Button
        {
            Content = "Applica modifiche",
            MinWidth = 160,
            Padding = new Thickness(18, 10, 18, 10),
            Margin = new Thickness(6),
            Background = new SolidColorBrush(Color.FromRgb(14, 143, 232)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold
        };

        var compileButton = new System.Windows.Controls.Button
        {
            Content = "Compila ed esegui",
            MinWidth = 170,
            Padding = new Thickness(18, 10, 18, 10),
            Margin = new Thickness(6),
            Background = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold
        };

        void ApplyChanges()
        {
            sourceEditor.Text = popupEditor.Text;
            SaveCurrentExercise();
            StatusText.Text = $"Modifiche di {displayName} applicate";
        }

        applyButton.Click += (_, _) => ApplyChanges();

        compileButton.Click += async (_, _) =>
        {
            ApplyChanges();

            if (!_compilationAllowed)
            {
                OutputBox.Text =
                    "La compilazione è stata inibita dal docente.";
                return;
            }

            CompilationResult compilation =
                await CompileSourceAsync(
                    Editor.Text,
                    true,
                    HeaderEditor.Text,
                    GetCurrentHeaderFileName()
                );

            _compileOutput = compilation.CompileOutput;
            _exePath = compilation.ExePath;

            if (compilation.Success &&
                !string.IsNullOrWhiteSpace(compilation.ExePath))
            {
                ExecutionResult execution =
                    await RunCapturedAsync(compilation.ExePath, 5);

                _programOutput = execution.Output;
                OutputBox.Text =
                    compilation.CompileOutput +
                    Environment.NewLine +
                    Environment.NewLine +
                    execution.Output;

                SaveCurrentExerciseResult(
                    compilation.CompileOutput,
                    execution.Output
                );
            }
            else
            {
                SaveCurrentExerciseResult(
                    compilation.CompileOutput,
                    ""
                );
            }

            StatusText.Text = compilation.Success
                ? "Compilazione completata"
                : "Compilazione non riuscita: controlla l'output";
        };

        ShowFullscreenOverlay(
            $"{displayName} — Tipologia {GetTaskType()} — Esercizio {GetExerciseNumber()} — C++17",
            popupEditor,
            new[] { applyButton, compileButton },
            ApplyChanges
        );
    }

    private void ShowVerificationTerminal(
        string compileOutput,
        ExecutionResult execution)
    {
        string terminalText =
            "Microsoft Windows [Versione modalità verifica CV+]\r\n" +
            "(c) CV+ Compilatore Alunno\r\n\r\n" +
            "C:\\CVPlus\\Esercizio> g++ main.cpp -std=c++17 -o esercizio.exe\r\n\r\n" +
            compileOutput +
            "\r\n\r\n" +
            "C:\\CVPlus\\Esercizio> esercizio.exe\r\n\r\n" +
            execution.Output +
            "\r\n\r\n" +
            "Programma terminato. Premi Chiudi o ESC per tornare all'editor.";

        var terminal = new System.Windows.Controls.TextBox
        {
            Text = terminalText,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility =
                System.Windows.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility =
                System.Windows.Controls.ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 18,
            Background = Brushes.Black,
            Foreground = new SolidColorBrush(
                Color.FromRgb(220, 255, 220)),
            CaretBrush = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(20)
        };

        var copyButton = new System.Windows.Controls.Button
        {
            Content = "Copia output",
            MinWidth = 140,
            Padding = new Thickness(18, 10, 18, 10),
            Margin = new Thickness(6),
            Background = new SolidColorBrush(
                Color.FromRgb(31, 41, 55)),
            Foreground = Brushes.White
        };

        copyButton.Click += (_, _) =>
        {
            Clipboard.SetText(terminal.Text ?? string.Empty);
            StatusText.Text = "Output terminale copiato";
        };

        ShowFullscreenOverlay(
            $"CMD C++17 — Tipologia {GetTaskType()} — Esercizio {GetExerciseNumber()}",
            terminal,
            new[] { copyButton }
        );
    }

    private async Task RunInVisibleCmdAsync(string exePath)
    {
        string batchPath =
            Path.Combine(
                Path.GetTempPath(),
                "cvplus_verifica_" +
                Guid.NewGuid().ToString("N") +
                ".bat"
            );

        string compilerBin =
            Path.Combine(
                AppContext.BaseDirectory,
                "compiler",
                "ucrt64",
                "bin"
            );

        string batch =
            "@echo off\r\n" +
            "title CV+ Compilatore Alunno - Esecuzione C++17\r\n" +
            "color 0A\r\n" +
            "set \"PATH=" + compilerBin + ";%PATH%\"\r\n" +
            "cls\r\n" +
            "echo ================================================\r\n" +
            "echo   CV+ - ESECUZIONE PROGRAMMA C++17\r\n" +
            "echo ================================================\r\n" +
            "echo.\r\n" +
            "echo Compilatore runtime: " + compilerBin + "\r\n" +
            "echo.\r\n" +
            $"\"{exePath}\"\r\n" +
            "set \"CVPLUS_EXIT=%ERRORLEVEL%\"\r\n" +
            "echo.\r\n" +
            "echo Codice di uscita: %CVPLUS_EXIT%\r\n" +
            "echo ================================================\r\n" +
            "echo Programma terminato. Premi un tasto per chiudere.\r\n" +
            "pause >nul\r\n";

        File.WriteAllText(
            batchPath,
            batch,
            Encoding.Default
        );

        bool oldTopmost = Topmost;
        Topmost = false;

        try
        {
            using Process? process =
                Process.Start(
                    new ProcessStartInfo(
                        "cmd.exe",
                        $"/c \"{batchPath}\""
                    )
                    {
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Normal
                    }
                );

            if (process == null)
                throw new InvalidOperationException(
                    "Impossibile aprire la finestra CMD."
                );

            await process.WaitForExitAsync();
        }
        finally
        {
            try { File.Delete(batchPath); }
            catch { }

            if (_verificationMode)
            {
                Topmost = oldTopmost;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                ShowInTaskbar = false;
                Activate();
                Focus();
            }
        }
    }

private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (!_compilationAllowed)
        {
            MessageBox.Show(this, "La compilazione è stata inibita dal docente.", "Compilazione non disponibile", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CompilationResult compilation = await CompileSourceAsync(Editor.Text, true, HeaderEditor.Text, GetCurrentHeaderFileName());
        _compileOutput = compilation.CompileOutput;
        _exePath = compilation.ExePath;

        if (!compilation.Success || string.IsNullOrWhiteSpace(compilation.ExePath))
        {
            SaveCurrentExercise();
            return;
        }

        if (_verificationMode)
        {
            await RunInVisibleCmdAsync(
                compilation.ExePath
            );

            _programOutput =
                "Esecuzione completata nella finestra CMD visibile.";

            OutputBox.Text =
                compilation.CompileOutput +
                Environment.NewLine +
                Environment.NewLine +
                _programOutput;

            SaveCurrentExerciseResult(
                compilation.CompileOutput,
                _programOutput
            );

            return;
        }

        string bat = Path.Combine(Path.GetTempPath(), "cppstudent_run_" + Guid.NewGuid().ToString("N") + ".bat");
        File.WriteAllText(bat,
            $"@echo off\r\n" +
            $"set \"PATH={BundledCompilerBin};%PATH%\"\r\n" +
            $"\"{compilation.ExePath}\"\r\n" +
            "echo.\r\n" +
            "echo Programma terminato.\r\n" +
            "pause\r\n",
            Encoding.Default);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"") { UseShellExecute = true });
        _programOutput = "Esecuzione aperta nella finestra CMD.";
        SaveCurrentExerciseResult(compilation.CompileOutput, _programOutput);
    }

    private async Task<bool> CompileAsync()
    {
        CompilationResult result = await CompileSourceAsync(Editor.Text, true);
        _compileOutput = result.CompileOutput;
        _exePath = result.ExePath;
        SaveCurrentExerciseResult(result.CompileOutput, result.Success ? _programOutput : "");
        return result.Success;
    }

    private async Task<CompilationResult> CompileSourceAsync(string sourceCode, bool updateOutputBox, string? headerCode = null, string headerFileName = "esercizio.h")
    {
        if (!_compilationAllowed)
        {
            const string denied = "Il docente ha temporaneamente inibito la compilazione sui client.";
            if (updateOutputBox) OutputBox.Text = denied;
            return new CompilationResult(false, denied, null);
        }

        if (updateOutputBox) OutputBox.Text = "Compilazione in corso...";

        try
        {
            if (!File.Exists(BundledCompilerPath))
            {
                const string missing = "Installazione incompleta: compilatore C++17 incorporato non trovato.";
                if (updateOutputBox) OutputBox.Text = missing;
                return new CompilationResult(false, missing, null);
            }

            string dir = Path.Combine(Path.GetTempPath(), "CppStudentClient");
            Directory.CreateDirectory(dir);
            string stem = "compito_" + Guid.NewGuid().ToString("N");
            string cpp = Path.Combine(dir, stem + ".cpp");
            string exe = Path.Combine(dir, stem + ".exe");
            File.WriteAllText(cpp, sourceCode, new UTF8Encoding(false));
            string safeHeaderName = NormalizeHeaderFileName(headerFileName);
            if (!string.IsNullOrWhiteSpace(headerCode))
            {
                File.WriteAllText(
                    Path.Combine(dir, safeHeaderName),
                    headerCode,
                    new UTF8Encoding(false)
                );
            }

            string arguments =
                $"-std=c++17 -Wall -Wextra -Wpedantic " +
                $"-fdiagnostics-color=never -I\"{dir}\" " +
                $"-o \"{exe}\" \"{cpp}\"";
            var psi = new ProcessStartInfo(BundledCompilerPath, arguments)
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

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Impossibile avviare il compilatore C++.");
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());

            string Normalize(string value) => string.IsNullOrWhiteSpace(value)
                ? ""
                : value.Replace(cpp, "main.cpp", StringComparison.OrdinalIgnoreCase)
                       .Replace(cpp.Replace("\\", "/"), "main.cpp", StringComparison.OrdinalIgnoreCase)
                       .Trim();

            string stderr = Normalize(await stderrTask);
            string stdout = Normalize(await stdoutTask);
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(stderr)) parts.Add(stderr);
            if (!string.IsNullOrWhiteSpace(stdout)) parts.Add(stdout);
            string diagnostic = string.Join(Environment.NewLine + Environment.NewLine, parts);
            bool success = process.ExitCode == 0 && File.Exists(exe);

            string resultText;
            if (success)
            {
                resultText = string.IsNullOrWhiteSpace(diagnostic)
                    ? "COMPILAZIONE RIUSCITA\nNessun errore o avviso."
                    : "COMPILAZIONE RIUSCITA CON AVVISI\n\n" + diagnostic;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(diagnostic))
                    diagnostic = $"Il compilatore ha restituito il codice {process.ExitCode} senza messaggi diagnostici.";
                resultText = "COMPILAZIONE NON RIUSCITA\n\n" + diagnostic;
            }

            string runtimeAnalysis = AnalyzeRuntimeRisks(sourceCode);
            if (!string.IsNullOrWhiteSpace(runtimeAnalysis))
                resultText += Environment.NewLine + Environment.NewLine + runtimeAnalysis;

            if (updateOutputBox) OutputBox.Text = resultText;
            return new CompilationResult(success, resultText, success ? exe : null);
        }
        catch (Exception ex)
        {
            string error = "ERRORE DURANTE LA COMPILAZIONE\n\n" + ex.GetType().Name + ": " + ex.Message;
            if (updateOutputBox) OutputBox.Text = error;
            return new CompilationResult(false, error, null);
        }
    }

    private static string AnalyzeRuntimeRisks(string sourceCode)
    {
        var findings = new List<string>();
        string[] lines = sourceCode.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (System.Text.RegularExpressions.Regex.IsMatch(
                    line,
                    @"(?:/|%)\s*0(?:\D|$)"))
            {
                findings.Add($"Riga {i + 1}: divisione o modulo per zero certo.");
            }

            var loopMatch = System.Text.RegularExpressions.Regex.Match(
                line,
                @"for\s*\([^;]*;[^;]*;[^)]*\)");
            if (loopMatch.Success &&
                line.Contains("/ i", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add($"Riga {i + 1}: controlla che il divisore i non possa valere zero.");
            }

            if (line.Contains("while(true)", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("for(;;)", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add($"Riga {i + 1}: possibile ciclo infinito.");
            }
        }

        return findings.Count == 0
            ? ""
            : "ANALISI PREVENTIVA\n" + string.Join(Environment.NewLine, findings.Distinct());
    }

    private async Task<ExecutionResult> RunCapturedAsync(string exePath, int timeoutSeconds)
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
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Path.GetTempPath(),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            ConfigureCompilerEnvironment(psi);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Impossibile avviare il programma compilato.");
            process.StandardInput.Close();
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            Task waitTask = process.WaitForExitAsync();
            Task completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));

            if (completed != waitTask)
            {
                try { process.Kill(true); } catch { }
                await process.WaitForExitAsync();
                string partial = (await stdoutTask).Trim();
                string text = $"ESECUZIONE INTERROTTA DOPO {timeoutSeconds} SECONDI\nPossibile ciclo infinito o programma in attesa di input.";
                if (!string.IsNullOrWhiteSpace(partial)) text += "\n\nOUTPUT PARZIALE\n" + partial;
                return new ExecutionResult(false, text, null, true);
            }

            string stdout = (await stdoutTask).Trim();
            string stderr = (await stderrTask).Trim();
            var sections = new List<string>
            {
                process.ExitCode == 0 ? "ESECUZIONE TERMINATA CORRETTAMENTE" : "ESECUZIONE TERMINATA IN MODO ANOMALO",
                $"Codice di uscita: {process.ExitCode}",
                string.IsNullOrWhiteSpace(stdout) ? "OUTPUT PROGRAMMA\nNessun testo prodotto." : "OUTPUT PROGRAMMA\n" + stdout
            };
            if (!string.IsNullOrWhiteSpace(stderr)) sections.Add("ERRORI DI ESECUZIONE\n" + stderr);
            if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stderr))
                sections.Add("Possibile errore runtime: controlla divisioni o modulo per zero e accessi non validi alla memoria.");
            return new ExecutionResult(process.ExitCode == 0, string.Join(Environment.NewLine + Environment.NewLine, sections), process.ExitCode, false);
        }
        catch (Exception ex)
        {
            return new ExecutionResult(false, "ERRORE DURANTE L'ESECUZIONE\n\n" + ex.Message, null, false);
        }
    }

    private void SaveCurrentExerciseResult(string compileOutput, string programOutput)
    {
        SaveCurrentExercise();
        if (_exerciseStates.TryGetValue(_activeKey, out ExerciseState? state))
        {
            state.CompileOutput = compileOutput;
            state.ProgramOutput = programOutput;
            SaveExerciseStates();
        }
    }

    private async void TestServer_Click(
        object sender,
        RoutedEventArgs e)
    {
        TestServerButton.IsEnabled = false;
        ServerTestProgress.Visibility =
            Visibility.Visible;
        StatusText.Text = "Verifica server...";

        try
        {
            string baseAddress =
                NormalizeServerAddress(ServerBox.Text);

            using var timeout =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(5)
                );

            using HttpResponseMessage response =
                await _http.GetAsync(
                    baseAddress + "/ping",
                    timeout.Token
                );

            string message =
                await response.Content.ReadAsStringAsync();

            response.EnsureSuccessStatusCode();
            await RefreshServerModeAsync(true);

            StatusText.Text = "Server raggiungibile";

            ShowVerificationSafeMessage(
                message +
                $"\n\nModalità: {(_verificationMode ? "VERIFICA" : "ESERCITAZIONE")}",
                "Connessione al docente",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                MessageBoxResult.OK
            );
        }
        catch (Exception ex)
        {
            StatusText.Text =
                "Server non raggiungibile";

            ShowVerificationSafeMessage(
                BuildNetworkError(ex),
                "Connessione non riuscita",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                MessageBoxResult.OK
            );
        }
        finally
        {
            ServerTestProgress.Visibility =
                Visibility.Collapsed;
            TestServerButton.IsEnabled = true;
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
            if (!string.IsNullOrWhiteSpace(receivedSession)) SetSessionCode(receivedSession);
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
        SendButton.Content = "Invia esercizi";
        RunButton.Content = "Compila ed esegui";
        RunButton.IsEnabled = _compilationAllowed;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Topmost = true;
        ShowInTaskbar = false;
        UpdateButton.IsEnabled = false;
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
        RunButton.Content = "Compila e apri CMD";
        ApplyCompilationPermission(_compilationAllowed);
        Topmost = false;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        WindowState = WindowState.Maximized;
        ShowInTaskbar = true;
        UpdateButton.IsEnabled = true;
        Activate();
    }

    private async void CheckUpdates_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_verificationMode)
        {
            ShowVerificationSafeMessage(
                "La ricerca degli aggiornamenti è disponibile soltanto in modalità esercitazione.",
                "Aggiornamenti non disponibili",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                MessageBoxResult.OK
            );
            return;
        }

        UpdateButton.IsEnabled = false;
        StatusText.Text = "Ricerca aggiornamenti...";

        try
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    "https://api.github.com/repos/0-29654/compilatore/releases/latest"
                );

            request.Headers.UserAgent.ParseAdd(
                "CVPlusCompilatoreAlunno/1.9.3"
            );

            using HttpResponseMessage response =
                await _http.SendAsync(request);

            response.EnsureSuccessStatusCode();

            string body =
                await response.Content.ReadAsStringAsync();

            using JsonDocument document =
                JsonDocument.Parse(body);

            JsonElement release =
                document.RootElement;

            string tag =
                release.TryGetProperty(
                    "tag_name",
                    out JsonElement tagElement)
                ? tagElement.GetString() ?? ""
                : "";

            Version currentVersion =
                Assembly.GetExecutingAssembly()
                    .GetName()
                    .Version ??
                new Version(1, 9, 0);

            Version? latestVersion =
                ExtractVersionFromTag(tag);

            if (latestVersion == null ||
                latestVersion <= currentVersion)
            {
                StatusText.Text =
                    "Il programma è aggiornato";

                ShowVerificationSafeMessage(
                    $"La versione installata ({currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}) è già aggiornata.",
                    "Nessun aggiornamento",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    MessageBoxResult.OK
                );

                return;
            }

            string? downloadUrl = null;

            if (release.TryGetProperty(
                    "assets",
                    out JsonElement assets) &&
                assets.ValueKind == JsonValueKind.Array)
            {
                foreach (
                    JsonElement asset
                    in assets.EnumerateArray())
                {
                    string name =
                        asset.TryGetProperty(
                            "name",
                            out JsonElement nameElement)
                        ? nameElement.GetString() ?? ""
                        : "";

                    if (!name.EndsWith(
                            ".exe",
                            StringComparison.OrdinalIgnoreCase) ||
                        !name.Contains(
                            "Setup",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    downloadUrl =
                        asset.TryGetProperty(
                            "browser_download_url",
                            out JsonElement urlElement)
                        ? urlElement.GetString()
                        : null;

                    if (!string.IsNullOrWhiteSpace(downloadUrl))
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
                throw new InvalidOperationException(
                    "La Release più recente non contiene un installer .exe."
                );

            MessageBoxResult answer =
                ShowVerificationSafeMessage(
                    $"È disponibile la versione {latestVersion}.\n\n" +
                    "Vuoi scaricarla e avviare l'installazione?",
                    "Aggiornamento disponibile",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes
                );

            if (answer != MessageBoxResult.Yes)
            {
                StatusText.Text =
                    "Aggiornamento annullato";
                return;
            }

            StatusText.Text =
                "Download aggiornamento...";

            string installerPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "CppStudentClient_Update_" +
                    latestVersion +
                    ".exe"
                );

            using (
                HttpResponseMessage download =
                    await _http.GetAsync(
                        downloadUrl,
                        HttpCompletionOption.ResponseHeadersRead
                    ))
            {
                download.EnsureSuccessStatusCode();

                await using Stream source =
                    await download.Content.ReadAsStreamAsync();

                await using (
                    FileStream destination =
                        new FileStream(
                            installerPath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None))
                {
                    await source.CopyToAsync(destination);
                    await destination.FlushAsync();
                }
            }

            if (!File.Exists(installerPath) ||
                new FileInfo(installerPath).Length < 1024 * 1024)
            {
                throw new InvalidOperationException(
                    "Il file di aggiornamento scaricato è incompleto."
                );
            }

            StatusText.Text =
                "Installazione automatica dell'aggiornamento...";

            string updaterScript =
                Path.Combine(
                    Path.GetTempPath(),
                    "CVPlus_Aggiorna_" +
                    Guid.NewGuid().ToString("N") +
                    ".cmd"
                );

            int currentProcessId =
                Environment.ProcessId;

            string script =
                "@echo off\r\n" +
                "setlocal\r\n" +
                $"set \"INSTALLER={installerPath}\"\r\n" +
                $"set \"APP_PID={currentProcessId}\"\r\n" +
                ":WAIT_APP\r\n" +
                "tasklist /FI \"PID eq %APP_PID%\" 2>NUL | find \"%APP_PID%\" >NUL\r\n" +
                "if not errorlevel 1 (\r\n" +
                "  timeout /t 1 /nobreak >NUL\r\n" +
                "  goto WAIT_APP\r\n" +
                ")\r\n" +
                "timeout /t 1 /nobreak >NUL\r\n" +
                "start \"\" /wait \"%INSTALLER%\" " +
                "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART " +
                "/CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /RESTARTAPPLICATIONS\r\n" +
                "set \"SETUP_EXIT=%ERRORLEVEL%\"\r\n" +
                "del /f /q \"%INSTALLER%\" >NUL 2>&1\r\n" +
                "del /f /q \"%~f0\" >NUL 2>&1\r\n" +
                "exit /b %SETUP_EXIT%\r\n";

            File.WriteAllText(
                updaterScript,
                script,
                Encoding.Default
            );

            Process.Start(
                new ProcessStartInfo(
                    "cmd.exe",
                    $"/c start \"\" /min \"{updaterScript}\""
                )
                {
                    UseShellExecute = true,
                    WindowStyle =
                        ProcessWindowStyle.Hidden
                }
            );

            _allowClose = true;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            StatusText.Text =
                "Ricerca aggiornamenti non riuscita";

            ShowVerificationSafeMessage(
                "Non è stato possibile verificare o scaricare l'aggiornamento.\n\n" +
                ex.Message,
                "Errore aggiornamenti",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                MessageBoxResult.OK
            );
        }
        finally
        {
            if (IsVisible)
                UpdateButton.IsEnabled = true;
        }
    }

    private static Version? ExtractVersionFromTag(
        string tag)
    {
        System.Text.RegularExpressions.Match match =
            System.Text.RegularExpressions.Regex.Match(
                tag ?? "",
                @"(?<!\d)(\d+)\.(\d+)\.(\d+)(?!\d)"
            );

        if (!match.Success)
            return null;

        return new Version(
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value)
        );
    }

    private async Task<bool> IsTeacherServerAvailableAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using HttpResponseMessage response = await _http.GetAsync(NormalizeServerAddress(ServerBox.Text) + "/ping", timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private List<int>? ShowExerciseSelectionDialog(int activeExercise)
    {
        _modalDialogOpen = true;
        bool oldTopmost = Topmost;
        try
        {
            string prefix = SessionBox.Text.Trim().ToUpperInvariant() + "|" + GetTaskType().Trim().ToUpperInvariant() + "|";
            var numbers = _exerciseStates.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(k => int.TryParse(k.Substring(prefix.Length), out int n) ? n : 0)
                .Where(n => n > 0)
                .Distinct().OrderBy(n => n).ToList();
            if (!numbers.Contains(activeExercise)) numbers.Add(activeExercise);
            numbers = numbers.Distinct().OrderBy(n => n).ToList();

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(24), MinWidth = 420 };
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"Schede disponibili: {numbers.Count}\nEsercizio attivo: {activeExercise}\n\nSeleziona gli esercizi da inviare:",
                FontSize = 17, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12)
            });
            var checks = new List<System.Windows.Controls.CheckBox>();
            foreach (int number in numbers)
            {
                var check = new System.Windows.Controls.CheckBox
                {
                    Content = number == activeExercise ? $"Esercizio {number} (attivo)" : $"Esercizio {number}",
                    IsChecked = number == activeExercise,
                    Tag = number, FontSize = 16, Margin = new Thickness(4,5,4,5)
                };
                checks.Add(check); panel.Children.Add(check);
            }
            var allButton = new System.Windows.Controls.Button { Content = "Seleziona tutti", MinWidth = 115, Margin = new Thickness(5) };
            var sendButton = new System.Windows.Controls.Button { Content = "Invia selezionati", IsDefault = true, MinWidth = 135, Margin = new Thickness(5) };
            var cancelButton = new System.Windows.Controls.Button { Content = "Annulla", IsCancel = true, MinWidth = 100, Margin = new Thickness(5) };
            var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,14,0,0) };
            buttons.Children.Add(allButton); buttons.Children.Add(sendButton); buttons.Children.Add(cancelButton); panel.Children.Add(buttons);
            var dialog = new Window
            {
                Title = "Esercizi da inviare", Owner = this, Content = panel,
                SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true
            };
            List<int>? selected = null;
            allButton.Click += (_, _) => { foreach (var c in checks) c.IsChecked = true; };
            sendButton.Click += (_, _) =>
            {
                selected = checks.Where(c => c.IsChecked == true).Select(c => (int)c.Tag).OrderBy(n => n).ToList();
                if (selected.Count == 0)
                {
                    MessageBox.Show(dialog, "Seleziona almeno un esercizio.", "Nessun esercizio selezionato", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                dialog.DialogResult = true;
            };
            Topmost = false;
            dialog.ShowDialog();
            return selected;
        }
        finally
        {
            Topmost = oldTopmost;
            _modalDialogOpen = false;
            Activate();
        }
    }


    private static string GetLocalIpv4Address()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .FirstOrDefault(address =>
                    address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address))
                ?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentExercise();
        if (!ValidateSubmission(out int registerNumber, out int activeExercise)) return;

        if (!await IsTeacherServerAvailableAsync())
        {
            StatusText.Text = "Server docente non raggiungibile";
            MessageBox.Show(this, "SERVER DOCENTE NON RAGGIUNGIBILE\n\nNessun esercizio è stato inviato.\nControlla che il server sia avviato e che IP, porta e codice sessione siano corretti.", "Invio non eseguito", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        List<int>? selected = ShowExerciseSelectionDialog(activeExercise);
        if (selected == null || selected.Count == 0) { StatusText.Text = "Invio annullato"; return; }

        string type = GetTaskType();
        string listText = string.Join(", ", selected);
        _modalDialogOpen = true;
        bool oldTopmost = Topmost;
        MessageBoxResult confirmation;
        try
        {
            Topmost = false;
            confirmation = MessageBox.Show(this,
                $"Confermi l'invio?\n\nModalità: {(_verificationMode ? "VERIFICA" : "ESERCITAZIONE")}\nEsercizi: {listText}",
                "Conferma consegna", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        }
        finally { Topmost = oldTopmost; _modalDialogOpen = false; Activate(); }
        if (confirmation != MessageBoxResult.Yes) { StatusText.Text = "Invio annullato"; return; }

        int sent = 0;
        var failed = new List<int>();
        try
        {
            foreach (int exerciseNumber in selected)
            {
                string key = BuildExerciseKey(type, exerciseNumber);
                if (!_exerciseStates.TryGetValue(key, out ExerciseState? state) || string.IsNullOrWhiteSpace(state.Code))
                { failed.Add(exerciseNumber); continue; }

                StatusText.Text = $"Compilazione esercizio {exerciseNumber}...";
                CompilationResult compilation = await CompileSourceAsync(
                    state.Code,
                    exerciseNumber == activeExercise,
                    state.HeaderCode,
                    NormalizeHeaderFileName(state.HeaderFileName)
                );
                ExecutionResult execution = compilation.Success && !string.IsNullOrWhiteSpace(compilation.ExePath)
                    ? await RunCapturedAsync(compilation.ExePath, 5)
                    : new ExecutionResult(false, "Programma non eseguito perché la compilazione non è riuscita.", null, false);
                state.CompileOutput = compilation.CompileOutput;
                state.ProgramOutput = execution.Output;

                string normalizedStudentName =
                    StudentNameBox.Text.Trim().ToUpperInvariant();
                string clientIp = GetLocalIpv4Address();

                var timings = _exerciseStates.ToDictionary(
                    pair => pair.Key,
                    pair => (long)pair.Value.Elapsed.TotalSeconds
                );

                var payload = new
                {
                    studentId = registerNumber.ToString(),
                    registerNumber,
                    studentName = StudentNameBox.Text.Trim(),
                    normalizedStudentName,
                    className = ClassBox.Text.Trim(),

                    // Nomi moderni e nomi storici: il client resta compatibile
                    // con entrambe le versioni del server docente.
                    assignmentType = type,
                    taskType = type,
                    tipologia = type,
                    type,

                    exerciseId = exerciseNumber.ToString(),
                    exerciseNumber,
                    totalExercises = selected.Count,

                    sessionCode = SessionBox.Text.Trim(),
                    sessionMode = _verificationMode ? "verifica" : "esercitazione",

                    clientIp,
                    studentIp = clientIp,
                    ipAddress = clientIp,
                    submissionKey =
                        normalizedStudentName + "|" +
                        clientIp + "|" +
                        SessionBox.Text.Trim(),

                    exerciseTimeSeconds = (long)state.Elapsed.TotalSeconds,
                    exerciseTimes = timings,

                    code = state.Code,
                    headerFileName = NormalizeHeaderFileName(state.HeaderFileName),
                    headerCode = state.HeaderCode,
                    hasHeader = !string.IsNullOrWhiteSpace(state.HeaderCode),
                    compilationSucceeded = compilation.Success,
                    compileOutput = compilation.CompileOutput,
                    executionSucceeded = execution.Success,
                    executionExitCode = execution.ExitCode,
                    executionTimedOut = execution.TimedOut,
                    programOutput = execution.Output,
                    output =
                        compilation.CompileOutput +
                        Environment.NewLine +
                        Environment.NewLine +
                        execution.Output
                };

                using var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                );

                using var timeout =
                    new CancellationTokenSource(TimeSpan.FromSeconds(6));

                using HttpResponseMessage response =
                    await _http.PostAsync(
                        NormalizeServerAddress(ServerBox.Text) + "/submit",
                        content,
                        timeout.Token
                    );

                string serverMessage =
                    await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    sent++;
                }
                else
                {
                    failed.Add(exerciseNumber);
                    OutputBox.Text =
                        $"INVIO ESERCIZIO {exerciseNumber} NON RIUSCITO\n\n" +
                        $"Risposta server: {(int)response.StatusCode} " +
                        $"{response.ReasonPhrase}\n\n" +
                        (string.IsNullOrWhiteSpace(serverMessage)
                            ? "Il server non ha restituito dettagli."
                            : serverMessage);
                }
            }
            SaveExerciseStates();
            if (failed.Count > 0)
            {
                StatusText.Text = $"Inviati {sent}; non inviati {failed.Count}";
                ShowVerificationSafeMessage(
                    $"Invio parziale.\n\n" +
                    $"Inviati: {sent}\n" +
                    $"Non inviati: {string.Join(", ", failed)}\n\n" +
                    "La risposta dettagliata del server è visibile nella casella Compilazione ed esecuzione.",
                    "Consegna parziale",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning,
                    MessageBoxResult.OK
                );
                return;
            }
            StatusText.Text = "Consegna completata: " + DateTime.Now.ToString("HH:mm:ss");
            ShowVerificationSafeMessage(
                $"Esercizi inviati correttamente: {listText}",
                "Consegna completata",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                MessageBoxResult.OK
            );
            if (_verificationMode) { ClearLocalVerificationData(); _allowClose = true; Close(); }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Server docente non raggiungibile";
            ShowVerificationSafeMessage(
                "Il server non ha risposto. L'invio non è stato confermato.",
                "Invio interrotto",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                MessageBoxResult.OK
            );
        }
        catch (Exception ex)
        {
            StatusText.Text = "Invio fallito";
            ShowVerificationSafeMessage(
                BuildNetworkError(ex),
                "Impossibile inviare il compito",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                MessageBoxResult.OK
            );
        }
    }

    private MessageBoxResult ShowVerificationSafeMessage(
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage icon,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        _modalDialogOpen = true;
        bool oldTopmost = Topmost;

        try
        {
            // In modalità verifica la finestra principale è Topmost e tende
            // a riprendersi il focus. La sospendiamo finché il popup è aperto.
            Topmost = false;

            return MessageBox.Show(
                this,
                message,
                title,
                buttons,
                icon,
                defaultResult
            );
        }
        finally
        {
            Topmost = oldTopmost;
            _modalDialogOpen = false;

            if (_verificationMode && IsVisible)
            {
                WindowState = WindowState.Maximized;
                Activate();
                Focus();
            }
        }
    }

    private bool ValidateSubmission(out int registerNumber, out int exerciseNumber)
    {
        registerNumber = 0; exerciseNumber = 0;
        if (string.IsNullOrWhiteSpace(StudentIdBox.Text) || string.IsNullOrWhiteSpace(StudentNameBox.Text) || string.IsNullOrWhiteSpace(TaskTypeBox.Text) || string.IsNullOrWhiteSpace(ExerciseBox.Text))
        {
            ShowVerificationSafeMessage(
                "Compila N° registro, nome e cognome, tipologia e N° esercizio.",
                "Dati mancanti",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                MessageBoxResult.OK
            );
            return false;
        }
        if (!int.TryParse(StudentIdBox.Text.Trim(), out registerNumber) || registerNumber <= 0)
        {
            ShowVerificationSafeMessage(
                "Il N° registro alunno deve essere un numero intero maggiore di zero.",
                "Numero non valido",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                MessageBoxResult.OK
            );
            StudentIdBox.Focus(); StudentIdBox.SelectAll(); return false;
        }
        if (!int.TryParse(ExerciseBox.Text.Trim(), out exerciseNumber) || exerciseNumber <= 0)
        {
            ShowVerificationSafeMessage(
                "Il N° esercizio deve essere un numero intero maggiore di zero.",
                "Numero non valido",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                MessageBoxResult.OK
            );
            ExerciseBox.Focus(); ExerciseBox.SelectAll(); return false;
        }
        return true;
    }

    private static string NormalizeHeaderFileName(string? value)
    {
        string fileName = Path.GetFileName(
            string.IsNullOrWhiteSpace(value) ? "esercizio.h" : value.Trim()
        );

        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "esercizio.h";

        if (!fileName.EndsWith(".h", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".h";
        }

        return fileName;
    }

    private string GetCurrentHeaderFileName()
    {
        if (_exerciseStates.TryGetValue(
                _activeKey,
                out ExerciseState? state) &&
            !string.IsNullOrWhiteSpace(state.HeaderFileName))
        {
            return NormalizeHeaderFileName(state.HeaderFileName);
        }

        return "esercizio.h";
    }

    private const string DefaultHeaderCode =
        "#ifndef ESERCIZIO_H\n#define ESERCIZIO_H\n\n// Dichiarazioni e funzioni dell'esercizio\n\n#endif // ESERCIZIO_H\n";

    private void AddHeader_Click(object sender, RoutedEventArgs e)
    {
        if (!_exerciseStates.TryGetValue(
                _activeKey,
                out ExerciseState? state))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(state.HeaderFileName))
            state.HeaderFileName = "esercizio.h";

        if (string.IsNullOrWhiteSpace(state.HeaderCode))
            state.HeaderCode = DefaultHeaderCode;

        HeaderTab.Header = state.HeaderFileName;
        HeaderTab.Visibility = Visibility.Visible;
        HeaderEditor.Text = state.HeaderCode;
        HeaderTab.IsSelected = true;

        string includeLine =
            $"#include \"{state.HeaderFileName}\"";

        if (!Editor.Text.Contains(
                includeLine,
                StringComparison.Ordinal))
        {
            MessageBoxResult addInclude =
                ShowVerificationSafeMessage(
                    $"Vuoi aggiungere automaticamente {includeLine} nel main.cpp?",
                    "Collega header al main",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes
                );

            if (addInclude == MessageBoxResult.Yes)
                Editor.Text = includeLine + "\n" + Editor.Text;
        }

        SaveCurrentExercise();
    }

    private void RenameHeader_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_exerciseStates.TryGetValue(
                _activeKey,
                out ExerciseState? state) ||
            string.IsNullOrWhiteSpace(state.HeaderCode))
        {
            ShowVerificationSafeMessage(
                "Prima aggiungi un file header all'esercizio.",
                "Nessun header presente",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                MessageBoxResult.OK
            );
            return;
        }

        string oldName = string.IsNullOrWhiteSpace(state.HeaderFileName)
            ? "esercizio.h"
            : state.HeaderFileName;

        var nameBox = new System.Windows.Controls.TextBox
        {
            Text = oldName,
            FontSize = 20,
            MinWidth = 360,
            Margin = new Thickness(28),
            HorizontalContentAlignment =
                HorizontalAlignment.Center
        };

        var saveButton = new System.Windows.Controls.Button
        {
            Content = "Rinomina",
            MinWidth = 140,
            Padding = new Thickness(18, 10, 18, 10),
            Margin = new Thickness(6),
            Background = new SolidColorBrush(Color.FromRgb(14, 143, 232)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold
        };

        saveButton.Click += (_, _) =>
        {
            string newName = Path.GetFileName(
                nameBox.Text.Trim()
            );

            if (string.IsNullOrWhiteSpace(newName))
            {
                StatusText.Text = "Inserisci un nome valido";
                return;
            }

            if (!newName.EndsWith(
                    ".h",
                    StringComparison.OrdinalIgnoreCase))
            {
                newName += ".h";
            }

            if (newName.IndexOfAny(
                    Path.GetInvalidFileNameChars()) >= 0)
            {
                StatusText.Text =
                    "Il nome del file header contiene caratteri non validi";
                return;
            }

            newName = NormalizeHeaderFileName(newName);
            oldName = NormalizeHeaderFileName(oldName);

            string oldInclude =
                $"#include \"{oldName}\"";
            string newInclude =
                $"#include \"{newName}\"";

            Editor.Text = Editor.Text.Replace(
                oldInclude,
                newInclude,
                StringComparison.OrdinalIgnoreCase
            );

            state.HeaderFileName = newName;
            state.HeaderCode = HeaderEditor.Text;
            HeaderTab.Header = newName;
            SaveCurrentExercise();

            StatusText.Text =
                $"Header rinominato in {newName}";

            CloseActiveOverlay();
        };

        ShowFullscreenOverlay(
            "Rinomina file header",
            nameBox,
            new[] { saveButton }
        );

        nameBox.Focus();
        nameBox.SelectAll();
    }

    private void DeleteHeader_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_exerciseStates.TryGetValue(
                _activeKey,
                out ExerciseState? state) ||
            string.IsNullOrWhiteSpace(state.HeaderCode))
        {
            ShowVerificationSafeMessage(
                "Questo esercizio non contiene un file header.",
                "Nessun file .h",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                MessageBoxResult.OK
            );
            return;
        }

        string headerName = NormalizeHeaderFileName(
            string.IsNullOrWhiteSpace(state.HeaderFileName)
                ? "esercizio.h"
                : state.HeaderFileName
        );

        MessageBoxResult confirmation =
            ShowVerificationSafeMessage(
                $"Vuoi eliminare definitivamente {headerName} da questo esercizio?\n\n" +
                "Verrà rimossa anche la relativa direttiva #include dal main.cpp.",
                "Elimina file header",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No
            );

        if (confirmation != MessageBoxResult.Yes)
            return;

        string includePattern =
            @"(?m)^[ \t]*#include[ \t]*[\""<]" +
            System.Text.RegularExpressions.Regex.Escape(headerName) +
            @"[\"">][ \t]*\r?\n?";

        Editor.Text =
            System.Text.RegularExpressions.Regex.Replace(
                Editor.Text,
                includePattern,
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

        state.HeaderCode = "";
        state.HeaderFileName = "";
        state.Code = DefaultCode;

        HeaderEditor.Text = "";
        HeaderTab.Header = "esercizio.h";
        HeaderTab.Visibility = Visibility.Collapsed;
        Editor.Text = DefaultCode;

        SaveCurrentExercise();
        StatusText.Text =
            $"{headerName} eliminato; main.cpp ripristinato";
    }

    private void HeaderEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_loadingExercise || string.IsNullOrWhiteSpace(_activeKey))
            return;

        if (_exerciseStates.TryGetValue(_activeKey, out ExerciseState? state))
            state.HeaderCode = HeaderEditor.Text;
    }

    private void PreviousExercise_Click(object sender, RoutedEventArgs e) => SwitchExercise(-1);
    private void NextExercise_Click(object sender, RoutedEventArgs e) => SwitchExercise(1);

    private void SwitchExercise(int delta)
    {
        int current = GetExerciseNumber();
        int next = Math.Max(1, current + delta);
        if (next == current) return;

        _modalDialogOpen = true;
        bool oldTopmost = Topmost;
        MessageBoxResult answer;

        try
        {
            // In modalità verifica la finestra principale è Topmost e tenta di
            // riprendersi il focus quando viene disattivata. Durante il popup
            // sospendiamo questo comportamento, altrimenti i pulsanti del
            // MessageBox non ricevono correttamente il clic.
            Topmost = false;

            answer = MessageBox.Show(
                this,
                $"Vuoi salvare le modifiche dell'esercizio {current} prima di passare all'esercizio {next}?\n\n" +
                "Sì = salva e cambia\n" +
                "No = scarta le modifiche e cambia\n" +
                "Annulla = resta nell'esercizio corrente",
                "Cambia esercizio",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel
            );
        }
        finally
        {
            Topmost = oldTopmost;
            _modalDialogOpen = false;

            if (_verificationMode)
            {
                WindowState = WindowState.Maximized;
                Activate();
                Focus();
            }
        }

        if (answer == MessageBoxResult.Cancel) return;
        if (answer == MessageBoxResult.Yes) SaveCurrentExercise();
        else if (_exerciseStates.TryGetValue(_activeKey, out ExerciseState? oldState))
        {
            _loadingExercise = true;
            Editor.Text = string.IsNullOrWhiteSpace(oldState.Code) ? DefaultCode : oldState.Code;
            _loadingExercise = false;
        }

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
        HeaderEditor.Text = state.HeaderCode ?? "";
        HeaderTab.Header = string.IsNullOrWhiteSpace(state.HeaderFileName)
            ? "esercizio.h"
            : state.HeaderFileName;
        HeaderTab.Visibility = string.IsNullOrWhiteSpace(state.HeaderCode)
            ? Visibility.Collapsed
            : Visibility.Visible;
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
        state.HeaderCode = HeaderEditor.Text;
        if (string.IsNullOrWhiteSpace(state.HeaderFileName))
            state.HeaderFileName = "esercizio.h";
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

    private void UpdateExerciseClock()
    {
        ExerciseTimeText.Text =
            "Tempo esercizio: " +
            FormatDuration(GetElapsedForActive());

        UpdateTaskSummary();
    }
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
        if (!_verificationMode || _modalDialogOpen)
            return;

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                // Ricontrolla il flag quando il callback viene realmente eseguito:
                // un popup potrebbe essere stato aperto dopo l'evento Deactivated.
                if (!_verificationMode || _modalDialogOpen)
                    return;

                Topmost = true;
                WindowState = WindowState.Maximized;
                Activate();
            }),
            DispatcherPriority.ApplicationIdle
        );
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

    private sealed record CompilationResult(bool Success, string CompileOutput, string? ExePath);
    private sealed record ExecutionResult(bool Success, string Output, int? ExitCode, bool TimedOut);

    public sealed class ExerciseState
    {
        public string Code { get; set; } = DefaultCode;
        public TimeSpan Elapsed { get; set; } = TimeSpan.Zero;
        public string CompileOutput { get; set; } = "";
        public string ProgramOutput { get; set; } = "";
        public string HeaderFileName { get; set; } = "esercizio.h";
        public string HeaderCode { get; set; } = "";
    }
}
