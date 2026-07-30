using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using System.Windows.Media.Imaging;
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
using System.Windows.Controls;
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
    private readonly DispatcherTimer _liveMonitorTimer = new() { Interval = TimeSpan.FromSeconds(2) };
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
    private bool _liveMonitorSyncRunning;
    private string _lastRemoteCommandId = "";
    private bool _modalDialogOpen;
    private System.Windows.Controls.Grid? _activeOverlay;
    private bool _compilationAllowed = true;
    private UdpClient? _teacherDiscoveryUdp;
    private CancellationTokenSource? _teacherDiscoveryCts;
    private const int TeacherDiscoveryPort = 5051;
    private IHighlightingDefinition? _cppHighlighting;
    private bool _editorAssistanceEnabled;
    private readonly Dictionary<TextEditor, CompletionWindow> _completionWindows = new();
    private CancellationTokenSource? _googleDriveOperationCts;
    private bool _googleDriveOperationRunning;
    private readonly HashSet<string> _installedCppExtensions = new(StringComparer.OrdinalIgnoreCase);
    private string CppExtensionsSettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CVPlus", "cpp-extensions.json");

    private const string DefaultCode = "#include <iostream>\nusing namespace std;\n\nint main()\n{\n    \n    return 0;\n}\n";

    private string BundledCompilerRoot => Path.Combine(AppContext.BaseDirectory, "compiler", "ucrt64");
    private string BundledCompilerBin => Path.Combine(BundledCompilerRoot, "bin");
    private string BundledCompilerPath => Path.Combine(BundledCompilerBin, "g++.exe");

    public MainWindow()
    {
        InitializeComponent();
        // Privacy: ogni nuova sessione parte senza credenziali Google OAuth locali residue.
        GoogleDriveExerciseService.ClearLocalAuthorizationCache();
        // Sicurezza predefinita: la gestione dei file .h resta bloccata finché il server non la abilita.
        ApplyHeaderManagementPermission(false);
        ConfigureCppHighlighting();
        ConfigureEditorAssistance(Editor);
        ConfigureEditorAssistance(HeaderEditor);
        ClearSessionCppAddons();
        LoadCppExtensions();
        ResetClientStateOnStartup();
        StartTeacherDiscoveryListener();
        Closed += (_, _) => { _liveMonitorTimer.Stop(); StopTeacherDiscoveryListener(); };
        if (!File.Exists(BundledCompilerPath))
            OutputBox.Text = "Installazione incompleta: compilatore C++17 incorporato assente. Reinstallare il programma.";
        ActivateExercise(GetTaskType(), GetExerciseNumber());

        _clockTimer.Tick += (_, _) => UpdateExerciseClock();
        _clockTimer.Start();
        _modeTimer.Tick += async (_, _) => await RefreshServerModeAsync(false);
        _modeTimer.Start();
        _liveMonitorTimer.Tick += async (_, _) => await SyncLiveMonitorAsync();
        _liveMonitorTimer.Start();

        StudentNameBox.TextChanged += (_, _) => UpdateWindowTitle();

        Loaded += async (_, _) =>
        {
            UpdateLocalIpText();
            UpdateTaskSummary();
            UpdateWindowTitle();
            await RefreshServerModeAsync(false);
        };
    }

    private void UpdateWindowTitle()
    {
        string studentName = StudentNameBox.Text.Trim();
        Title = string.IsNullOrWhiteSpace(studentName)
            ? "CV+ Compilatore Alunno"
            : $"CV+ Compilatore Alunno: {studentName}";
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

        StudentIdBox.Text = "";
        StudentNameBox.Text = "";
        ClassBox.Text = "";
        TaskTypeBox.Text = "";
        ExerciseBox.Text = "1";
        ServerBox.Text = "";
        SessionBox.Text = "";
        SetTeacherConnectionFieldsLocked(false);
        Editor.Text = DefaultCode;
        HeaderEditor.Text = "";
        HeaderTab.Visibility = Visibility.Collapsed;
        OutputBox.Text = "";
        StatusText.Text = "Pronto - nuova sessione";
        UpdateTaskSummary();
        UpdateWindowTitle();
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
                bool headerManagementAllowed = ReadHeaderManagementAllowed(root);
                bool editorAssistanceAllowed = ReadEditorAssistanceAllowed(root);
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
                    SetTeacherConnectionFieldsLocked(true);
                    ApplySessionMode(mode);
                    ApplyCompilationPermission(compileAllowed);
                    ApplyHeaderManagementPermission(headerManagementAllowed);
                    ApplyEditorAssistancePermission(editorAssistanceAllowed);
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


    private void SetTeacherConnectionFieldsLocked(bool locked)
    {
        // I valori ricevuti automaticamente dal programma docente restano visibili,
        // ma l'alunno non può modificarli accidentalmente o manualmente.
        ServerBox.IsReadOnly = locked;
        SessionBox.IsReadOnly = locked;
        ServerBox.IsTabStop = !locked;
        SessionBox.IsTabStop = !locked;
        ServerBox.Focusable = !locked;
        SessionBox.Focusable = !locked;

        string background = locked ? "#162235" : "#0A1526";
        string border = locked ? "#40516A" : "#2A3A52";
        string foreground = locked ? "#B9C7D8" : "#F5F8FC";

        ServerBox.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(background)!;
        SessionBox.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(background)!;
        ServerBox.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(border)!;
        SessionBox.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(border)!;
        ServerBox.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(foreground)!;
        SessionBox.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(foreground)!;

        string? tooltip = locked
            ? "Valore ricevuto automaticamente dal programma docente e non modificabile."
            : null;
        ServerBox.ToolTip = tooltip;
        SessionBox.ToolTip = tooltip;
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

        ConfigureEditorAssistance(popupEditor);

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

        void ApplyChanges()
        {
            sourceEditor.Text = popupEditor.Text;
            SaveCurrentExercise();
            StatusText.Text = $"Modifiche di {displayName} applicate";
        }

        applyButton.Click += (_, _) => ApplyChanges();

        ShowFullscreenOverlay(
            $"{displayName} — Tipologia {GetTaskType()} — Esercizio {GetExerciseNumber()} — C++17",
            popupEditor,
            new[] { applyButton },
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
            "Programma terminato. Premi Chiudi o ESC per tornare all'editor.\r\n" +
            "Terminale integrato nella modalità verifica.";

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

    private string BuildConsoleHeader(string modeName)
    {
        string localIp = GetLocalIpv4Address();
        if (string.IsNullOrWhiteSpace(localIp))
            localIp = "non disponibile";

        string elapsed = FormatDuration(GetElapsedForActive());
        string taskType = EscapeBatchEcho(GetTaskType());
        string exercise = GetExerciseNumber().ToString();
        string safeMode = EscapeBatchEcho(modeName.ToUpperInvariant());

        return
            "powershell -NoProfile -Command \"Write-Host ('=' * [Console]::WindowWidth)\"\r\n" +
            $"echo CV+ MICROSOFT OUTPUT - {safeMode}\r\n" +
            "echo Copyright Alessandro Barazzuol\r\n" +
            "powershell -NoProfile -Command \"Write-Host ('-' * [Console]::WindowWidth)\"\r\n" +
            $"echo IP: {EscapeBatchEcho(localIp)}   ^|   Tempo esercizio: {EscapeBatchEcho(elapsed)}\r\n" +
            $"echo Compilatore: G++ C++17 - MinGW-w64 UCRT64   ^|   Tipologia: {taskType}   ^|   Esercizio: {exercise}\r\n" +
            "powershell -NoProfile -Command \"Write-Host ('=' * [Console]::WindowWidth)\"\r\n" +
            "echo.\r\n";
    }

    private static string BuildConsoleSeparator(char character = '=')
    {
        return $"powershell -NoProfile -Command \"Write-Host ('{character}' * [Console]::WindowWidth)\"\r\n";
    }

    private static string EscapeBatchEcho(string value)
    {
        return (value ?? string.Empty)
            .Replace("^", "^^")
            .Replace("&", "^&")
            .Replace("|", "^|")
            .Replace("<", "^<")
            .Replace(">", "^>")
            .Replace("%", "%%");
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
            await ShowCompilationErrorConsoleAsync(compilation.CompileOutput);
            return;
        }

        if (_verificationMode)
        {
            await RunInVerificationConsoleAsync(
                compilation.ExePath,
                compilation.CompileOutput
            );
            return;
        }

        string bat = Path.Combine(Path.GetTempPath(), "cppstudent_run_" + Guid.NewGuid().ToString("N") + ".bat");
        File.WriteAllText(bat,
            $"@echo off\r\n" +
            "chcp 65001 >nul\r\n" +
            "color 0A\r\n" +
            "title CV+ Microsoft Output - Modalita esercitazione\r\n" +
            BuildConsoleHeader("ESERCITAZIONE") +
            $"set \"PATH={BundledCompilerBin};%PATH%\"\r\n" +
            "color 0F\r\n" +
            $"\"{compilation.ExePath}\"\r\n" +
            "color 0A\r\n" +
            "echo.\r\n" +
            BuildConsoleSeparator() +
            "echo Programma terminato.\r\n" +
            "pause\r\n",
            Encoding.Default);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Maximized
        });
        _programOutput = "Esecuzione aperta nella finestra CMD.";
        SaveCurrentExerciseResult(compilation.CompileOutput, _programOutput);
    }

    private async Task ShowCompilationErrorConsoleAsync(string compileOutput)
    {
        string modeName = _verificationMode ? "VERIFICA" : "ESERCITAZIONE";
        string token = Guid.NewGuid().ToString("N");
        string outputFile = Path.Combine(Path.GetTempPath(), $"cppstudent_compile_error_{token}.txt");
        string bat = Path.Combine(Path.GetTempPath(), $"cppstudent_compile_error_{token}.bat");

        File.WriteAllText(outputFile, compileOutput, new UTF8Encoding(false));
        File.WriteAllText(
            bat,
            "@echo off\r\n" +
            "chcp 65001 >nul\r\n" +
            "color 0A\r\n" +
            $"title CV+ Microsoft Output - Modalita {modeName.ToLowerInvariant()}\r\n" +
            BuildConsoleHeader(modeName) +
            "echo ERRORE DI COMPILAZIONE - CODICE DI USCITA DIVERSO DA ZERO\r\n" +
            "echo.\r\n" +
            "color 0F\r\n" +
            $"type \"{outputFile}\"\r\n" +
            "color 0A\r\n" +
            "echo.\r\n" +
            BuildConsoleSeparator() +
            "echo Premi un tasto per chiudere.\r\n" +
            "pause >nul\r\n",
            Encoding.Default
        );

        _modalDialogOpen = true;
        bool oldTopmost = Topmost;
        if (_verificationMode)
            Topmost = false;

        try
        {
            var startInfo = new ProcessStartInfo("cmd.exe", $"/d /c \"{bat}\"")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Maximized
            };

            using Process? console = Process.Start(startInfo);
            if (console != null)
                await console.WaitForExitAsync();
        }
        finally
        {
            try { File.Delete(bat); } catch { }
            try { File.Delete(outputFile); } catch { }
            _modalDialogOpen = false;

            if (_verificationMode)
            {
                Topmost = true;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                Activate();
                Focus();
            }
            else
            {
                Topmost = oldTopmost;
            }
        }
    }

    private async Task RunInVerificationConsoleAsync(string exePath, string compileOutput)
    {
        string bat = Path.Combine(
            Path.GetTempPath(),
            "cppstudent_verify_" + Guid.NewGuid().ToString("N") + ".bat"
        );

        File.WriteAllText(
            bat,
            "@echo off\r\n" +
            "chcp 65001 >nul\r\n" +
            "color 0A\r\n" +
            "title CV+ Microsoft Output - Modalita verifica\r\n" +
            BuildConsoleHeader("VERIFICA") +
            $"set \"PATH={BundledCompilerBin};%PATH%\"\r\n" +
            "color 0F\r\n" +
            $"\"{exePath}\"\r\n" +
            "color 0A\r\n" +
            "echo.\r\n" +
            BuildConsoleSeparator() +
            "echo Programma terminato. Premi un tasto per chiudere.\r\n" +
            "pause >nul\r\n",
            Encoding.Default
        );

        _modalDialogOpen = true;
        bool oldTopmost = Topmost;
        Topmost = false;
        StatusText.Text = "Programma in esecuzione nella console";
        _programOutput = "Esecuzione aperta nella finestra CMD della modalita verifica.";
        OutputBox.Text = compileOutput + Environment.NewLine + Environment.NewLine + _programOutput;
        SaveCurrentExerciseResult(compileOutput, _programOutput);

        try
        {
            var startInfo = new ProcessStartInfo("cmd.exe", $"/d /c \"{bat}\"")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Maximized,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Path.GetTempPath()
            };

            using Process? console = Process.Start(startInfo);
            if (console == null)
                throw new InvalidOperationException("Impossibile aprire la finestra CMD.");

            await console.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            _programOutput = "ERRORE APERTURA CMD\n\n" + ex.Message;
            OutputBox.Text = compileOutput + Environment.NewLine + Environment.NewLine + _programOutput;
            SaveCurrentExerciseResult(compileOutput, _programOutput);
        }
        finally
        {
            try { File.Delete(bat); } catch { }
            _modalDialogOpen = false;

            if (_verificationMode)
            {
                Topmost = true;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                Activate();
                Focus();
                StatusText.Text = "Modalita verifica attiva";
            }
            else
            {
                Topmost = oldTopmost;
            }
        }
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

            IReadOnlyList<InstalledCppLibrary> installedLibraries = CppLibraryManager.LoadInstalled();
            string libraryArguments = CppLibraryManager.BuildCompilerArguments(installedLibraries);
            string arguments =
                $"-std=c++17 -Wall -Wextra -Wpedantic " +
                $"-fdiagnostics-color=never -I\"{dir}\" -I\"{CppExtensionsIncludePath}\" " +
                $"-o \"{exe}\" \"{cpp}\" {libraryArguments}";
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
            if (success) CppLibraryManager.CopyRuntimeFiles(installedLibraries, dir);

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

            string explanatoryAnalysis = CppErrorAnalyzer.Analyze(sourceCode, diagnostic);
            if (!string.IsNullOrWhiteSpace(explanatoryAnalysis))
                resultText += Environment.NewLine + Environment.NewLine + explanatoryAnalysis;

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

    private async Task SyncLiveMonitorAsync()
    {
        if (_liveMonitorSyncRunning || string.IsNullOrWhiteSpace(ServerBox.Text))
            return;

        _liveMonitorSyncRunning = true;
        try
        {
            string studentId = StudentIdBox.Text.Trim();
            string clientIp = GetLocalIpv4Address();
            string commandUrl = NormalizeServerAddress(ServerBox.Text) +
                "/client-command?studentId=" + Uri.EscapeDataString(studentId) +
                "&clientIp=" + Uri.EscapeDataString(clientIp);

            using HttpResponseMessage commandResponse = await _http.GetAsync(commandUrl);
            if (commandResponse.IsSuccessStatusCode)
            {
                string json = await commandResponse.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                string commandId = root.TryGetProperty("commandId", out JsonElement idEl)
                    ? idEl.GetString() ?? "" : "";
                string action = root.TryGetProperty("action", out JsonElement actionEl)
                    ? actionEl.GetString() ?? "" : "";

                if (!string.IsNullOrWhiteSpace(commandId) &&
                    !commandId.Equals(_lastRemoteCommandId, StringComparison.OrdinalIgnoreCase) &&
                    action.Equals("compile", StringComparison.OrdinalIgnoreCase))
                {
                    _lastRemoteCommandId = commandId;
                    CompilationResult compilation = await CompileSourceAsync(
                        Editor.Text,
                        false,
                        HeaderEditor.Text,
                        GetCurrentHeaderFileName());

                    ExecutionResult execution = compilation.Success && !string.IsNullOrWhiteSpace(compilation.ExePath)
                        ? await RunCapturedAsync(compilation.ExePath, 5)
                        : new ExecutionResult(false,
                            "Programma non eseguito perché la compilazione non è riuscita.",
                            null,
                            false);

                    _compileOutput = compilation.CompileOutput;
                    _programOutput = execution.Output;
                    SaveCurrentExerciseResult(_compileOutput, _programOutput);
                    await PostLiveStateAsync(commandId, "Risposta alla richiesta di compilazione del docente");
                    return;
                }
            }

            await PostLiveStateAsync("", "Aggiornamento automatico");
        }
        catch
        {
            // Il monitor è una funzione aggiuntiva: un errore di rete non deve disturbare l'alunno.
        }
        finally
        {
            _liveMonitorSyncRunning = false;
        }
    }

    private async Task PostLiveStateAsync(string commandId, string status)
    {
        if (string.IsNullOrWhiteSpace(ServerBox.Text))
            return;

        string clientIp = GetLocalIpv4Address();
        var payload = new
        {
            studentId = StudentIdBox.Text.Trim(),
            studentName = StudentNameBox.Text.Trim(),
            className = ClassBox.Text.Trim(),
            assignmentType = GetTaskType(),
            exerciseId = GetExerciseNumber().ToString(),
            sessionCode = SessionBox.Text.Trim(),
            mode = _verificationMode ? "verifica" : "esercitazione",
            clientIp,
            code = Editor.Text,
            headerFileName = GetCurrentHeaderFileName(),
            headerCode = HeaderEditor.Text,
            compileOutput = _compileOutput,
            programOutput = _programOutput,
            commandId,
            status,
            sentAtUtc = DateTime.UtcNow,
            isOnline = true
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await _http.PostAsync(
            NormalizeServerAddress(ServerBox.Text) + "/live",
            content,
            timeout.Token);
    }

    private void NotifyServerClientClosed()
    {
        if (string.IsNullOrWhiteSpace(ServerBox.Text))
            return;

        try
        {
            string clientIp = GetLocalIpv4Address();
            var payload = new
            {
                studentId = StudentIdBox.Text.Trim(),
                studentName = StudentNameBox.Text.Trim(),
                className = ClassBox.Text.Trim(),
                assignmentType = GetTaskType(),
                exerciseId = GetExerciseNumber().ToString(),
                clientIp,
                remoteAddress = clientIp,
                status = "Client chiuso",
                sentAtUtc = DateTime.UtcNow,
                isOnline = false
            };

            using var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            _http.PostAsync(
                NormalizeServerAddress(ServerBox.Text) + "/live",
                content,
                timeout.Token).GetAwaiter().GetResult();
        }
        catch
        {
            // In chiusura non mostrare errori: il server eliminerà comunque
            // il client tramite il controllo automatico dell'ultimo aggiornamento.
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
            bool teacherConnectionReceived = false;
            string serverIp = Get(root, "serverIp", "");
            if (!string.IsNullOrWhiteSpace(serverIp))
            {
                int serverPort = root.TryGetProperty("serverPort", out JsonElement portEl) && portEl.TryGetInt32(out int parsedPort) ? parsedPort : 5050;
                ServerBox.Text = $"{serverIp}:{serverPort}";
                teacherConnectionReceived = true;
            }
            string receivedSession = Get(root, "sessionCode", Get(root, "code", Get(root, "session", "")));
            if (!string.IsNullOrWhiteSpace(receivedSession))
            {
                SetSessionCode(receivedSession);
                teacherConnectionReceived = true;
            }
            if (teacherConnectionReceived) SetTeacherConnectionFieldsLocked(true);
            ApplyCompilationPermission(ReadCompilationAllowed(root));
            ApplyHeaderManagementPermission(ReadHeaderManagementAllowed(root));
            ApplyEditorAssistancePermission(ReadEditorAssistanceAllowed(root));
        }
        catch
        {
            if (body.Contains("verifica", StringComparison.OrdinalIgnoreCase)) mode = "verifica";
        }
        ApplySessionMode(mode);
    }


    private static bool ReadEditorAssistanceAllowed(JsonElement root)
    {
        foreach (string name in new[] { "editorAssistanceEnabled", "allowCppAutocomplete", "cppAutocompleteEnabled", "intellisenseEnabled" })
        {
            if (root.TryGetProperty(name, out JsonElement value) &&
                (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
                return value.GetBoolean();
        }
        return false;
    }

    private void ApplyEditorAssistancePermission(bool enabled)
    {
        _editorAssistanceEnabled = enabled;
        if (!enabled)
        {
            foreach (CompletionWindow window in _completionWindows.Values.ToList())
                try { window.Close(); } catch { }
            _completionWindows.Clear();
        }
        StatusText.Text = enabled
            ? "Aiuto scrittura C++ abilitato dal docente"
            : (_verificationMode ? "Modalità verifica attiva" : "Pronto");
    }

    private void ConfigureEditorAssistance(TextEditor editor)
    {
        editor.TextArea.TextEntered += (_, e) => Editor_TextEntered(editor, e.Text);
        editor.TextArea.PreviewKeyDown += (_, e) => EditorAssistance_PreviewKeyDown(editor, e);
    }

    private void EditorAssistance_PreviewKeyDown(TextEditor editor, KeyEventArgs e)
    {
        if (!_editorAssistanceEnabled) return;

        if (e.Key == Key.Tab && !_completionWindows.ContainsKey(editor))
        {
            var currentLine = editor.Document.GetLineByOffset(editor.CaretOffset);
string line = editor.Document.GetText(currentLine.Offset, currentLine.Length).Trim();
            string? snippet = line switch
            {
                "for" => "for (int i = 0; i < n; i++)\n{\n    \n}",
                "while" => "while (condizione)\n{\n    \n}",
                "if" => "if (condizione)\n{\n    \n}",
                "else" => "else\n{\n    \n}",
                "switch" => "switch (valore)\n{\n    case 0:\n        break;\n    default:\n        break;\n}",
                "main" => "int main()\n{\n    \n    return 0;\n}",
                _ => null
            };
            if (snippet != null)
            {
                DocumentLine dl = editor.Document.GetLineByOffset(editor.CaretOffset);
                string indent = editor.Document.GetText(dl.Offset, dl.Length).TakeWhile(char.IsWhiteSpace).Aggregate("", (a,c) => a+c);
                string formatted = string.Join("\n", snippet.Split('\n').Select((x,i) => i == 0 ? indent + x : indent + x));
                editor.Document.Replace(dl.Offset, dl.Length, formatted);
                editor.CaretOffset = dl.Offset + formatted.IndexOf("    \n", StringComparison.Ordinal) + 4;
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter)
        {
            DocumentLine line = editor.Document.GetLineByOffset(editor.CaretOffset);
            string before = editor.Document.GetText(line.Offset, Math.Max(0, editor.CaretOffset - line.Offset));
            string baseIndent = new string(before.TakeWhile(char.IsWhiteSpace).ToArray());
            string extra = before.TrimEnd().EndsWith("{") ? "    " : "";
            editor.Document.Insert(editor.CaretOffset, Environment.NewLine + baseIndent + extra);
            editor.CaretOffset += Environment.NewLine.Length + baseIndent.Length + extra.Length;
            e.Handled = true;
        }
    }

    private void Editor_TextEntered(TextEditor editor, string text)
    {
        if (!_editorAssistanceEnabled || string.IsNullOrEmpty(text) || !char.IsLetterOrDigit(text[0]) && text[0] != '_') return;
        if (_completionWindows.TryGetValue(editor, out CompletionWindow? old)) { old.Close(); _completionWindows.Remove(editor); }

        string prefix = GetCurrentWord(editor);
        if (prefix.Length < 2) return;
        var matches = CppCompletions.Concat(GetInstalledExtensionCompletions()).Concat(GetInstalledLibraryCompletions()).Where(c => c.Trigger.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Take(24).ToList();
        if (matches.Count == 0) return;

        var window = new CompletionWindow(editor.TextArea) { StartOffset = editor.CaretOffset - prefix.Length };
        foreach (var item in matches) window.CompletionList.CompletionData.Add(new CppCompletionData(item.Display, item.Insert, item.Description));
        window.Closed += (_, _) => _completionWindows.Remove(editor);
        _completionWindows[editor] = window;
        window.Show();
    }

    private static string GetCurrentWord(TextEditor editor)
    {
        int offset = editor.CaretOffset, start = offset;
        while (start > 0)
        {
            char c = editor.Document.GetCharAt(start - 1);
            if (!char.IsLetterOrDigit(c) && c != '_' && c != ':') break;
            start--;
        }
        return editor.Document.GetText(start, offset - start);
    }

    private static readonly (string Trigger, string Display, string Insert, string Description)[] CppCompletions =
    {
        ("for", "for — ciclo con indice", "for (int i = 0; i < n; i++)\n{\n    \n}", "Ciclo for C++17"),
        ("foreach", "for — range based", "for (const auto& elemento : contenitore)\n{\n    \n}", "Ciclo for-each C++17"),
        ("while", "while", "while (condizione)\n{\n    \n}", "Ciclo while"),
        ("do", "do...while", "do\n{\n    \n} while (condizione);", "Ciclo do-while"),
        ("if", "if", "if (condizione)\n{\n    \n}", "Condizione if"),
        ("ifelse", "if...else", "if (condizione)\n{\n    \n}\nelse\n{\n    \n}", "Condizione completa"),
        ("switch", "switch", "switch (valore)\n{\n    case 0:\n        break;\n    default:\n        break;\n}", "Selezione multipla"),
        ("cout", "cout", "cout << valore << endl;", "Output standard"),
        ("cin", "cin", "cin >> variabile;", "Input standard"),
        ("vector", "std::vector", "vector<int> valori;", "Contenitore vector"),
        ("string", "std::string", "string testo;", "Stringa standard"),
        ("sort", "std::sort", "sort(contenitore.begin(), contenitore.end());", "Ordinamento"),
        ("find", "std::find", "find(contenitore.begin(), contenitore.end(), valore)", "Ricerca"),
        ("push_back", "push_back", "push_back(valore);", "Inserimento in coda"),
        ("begin", "begin()", "begin()", "Primo iteratore"),
        ("end", "end()", "end()", "Iteratore oltre l'ultimo"),
        ("size", "size()", "size()", "Numero di elementi"),
        ("include", "#include", "#include <iostream>", "Inclusione libreria"),
        ("main", "main", "int main()\n{\n    \n    return 0;\n}", "Funzione principale")
    };

    private sealed class CppCompletionData : ICompletionData
    {
        public CppCompletionData(string text, string insertion, string description) { Text = text; _insertion = insertion; Description = description; }
        private readonly string _insertion;
        public ImageSource? Image => null;
        public string Text { get; }
        public object Content => Text;
        public object Description { get; }
        public double Priority => 0;
        public void Complete(ICSharpCode.AvalonEdit.Editing.TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            string indent = new string(textArea.Document.GetText(textArea.Document.GetLineByOffset(completionSegment.Offset).Offset,
                completionSegment.Offset - textArea.Document.GetLineByOffset(completionSegment.Offset).Offset).TakeWhile(char.IsWhiteSpace).ToArray());
            string value = string.Join(Environment.NewLine, _insertion.Split('\n').Select((line, i) => i == 0 ? line : indent + line));
            textArea.Document.Replace(completionSegment, value);
        }
    }


    private sealed record CppExtensionDefinition(string Id, string Name, string Description, string GuideFile, (string Trigger, string Display, string Insert, string Description)[] Completions);

    private static readonly CppExtensionDefinition[] CppExtensionCatalog =
    {
        new("stl-snippets", "C++ STL Essentials", "Snippet e completamenti per vector, list, map, set, stack e queue.", "Guida_CPP_STL_Essentials.pdf", new[]
        {
            ("vecfor", "vector + ciclo", "vector<int> valori;\nfor (const int valore : valori)\n{\n    cout << valore << endl;\n}", "Vector e ciclo range-based"),
            ("mapfor", "map + ciclo", "map<string, int> valori;\nfor (const auto& [chiave, valore] : valori)\n{\n    cout << chiave << \": \" << valore << endl;\n}", "Map e structured binding"),
            ("queue", "queue completa", "queue<int> coda;\ncoda.push(valore);\nint primo = coda.front();\ncoda.pop();", "Operazioni principali su queue")
        }),
        new("algorithms", "C++ Algorithms", "Completamenti per sort, find, count, transform e accumulate.", "Guida_CPP_Algorithms.pdf", new[]
        {
            ("accumulate", "std::accumulate", "int somma = accumulate(valori.begin(), valori.end(), 0);", "Somma degli elementi; richiede <numeric>"),
            ("transform", "std::transform", "transform(valori.begin(), valori.end(), valori.begin(), [](int x) { return x * 2; });", "Trasforma gli elementi"),
            ("countif", "std::count_if", "int quanti = count_if(valori.begin(), valori.end(), [](int x) { return x > 0; });", "Conta gli elementi che rispettano una condizione")
        }),
        new("cpp-math", "C++ Math", "Snippet per cmath, numeri casuali e semplici calcoli.", "Guida_CPP_Math.pdf", new[]
        {
            ("random", "random C++17", "random_device rd;\nmt19937 gen(rd());\nuniform_int_distribution<int> distribuzione(minimo, massimo);\nint casuale = distribuzione(gen);", "Generatore casuale C++17; richiede <random>"),
            ("distance2d", "distanza 2D", "double distanza = hypot(x2 - x1, y2 - y1);", "Distanza euclidea; richiede <cmath>")
        }),
        new("cvplus-header", "CV+ Utility Header", "Installa una libreria header-only locale con funzioni didattiche sicure.", "Guida_CVPlus_Utility_Header.pdf", new[]
        {
            ("cvread", "cvplus::leggi", "int valore = cvplus::leggi<int>(\"Inserisci valore: \");", "Input controllato dalla libreria CV+"),
            ("cvprint", "cvplus::stampa", "cvplus::stampa(valore);", "Output semplice dalla libreria CV+")
        })
    };

    private void LoadCppExtensions()
    {
        try
        {
            if (File.Exists(CppExtensionsSettingsPath))
            {
                string[]? ids = JsonSerializer.Deserialize<string[]>(File.ReadAllText(CppExtensionsSettingsPath));
                if (ids != null) foreach (string id in ids) _installedCppExtensions.Add(id);
            }
            EnsureCvPlusHeader();
        }
        catch { _installedCppExtensions.Clear(); }
    }

    private void SaveCppExtensions()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CppExtensionsSettingsPath)!);
        File.WriteAllText(CppExtensionsSettingsPath, JsonSerializer.Serialize(_installedCppExtensions.OrderBy(x => x), new JsonSerializerOptions { WriteIndented = true }));
        EnsureCvPlusHeader();
    }

    private IEnumerable<(string Trigger, string Display, string Insert, string Description)> GetInstalledExtensionCompletions() =>
        CppExtensionCatalog.Where(x => _installedCppExtensions.Contains(x.Id)).SelectMany(x => x.Completions);

    private string CppExtensionsIncludePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CVPlus", "CppExtensions", "include");

    private void EnsureCvPlusHeader()
    {
        if (!_installedCppExtensions.Contains("cvplus-header")) return;
        Directory.CreateDirectory(CppExtensionsIncludePath);
        string headerText = string.Join(Environment.NewLine, new[]
        {
            "#pragma once",
            "#include <iostream>",
            "#include <limits>",
            "#include <string>",
            "namespace cvplus {",
            "template<class T> T leggi(const std::string& messaggio) {",
            "    T valore{};",
            "    while (true) {",
            "        std::cout << messaggio;",
            "        if (std::cin >> valore) return valore;",
            "        std::cin.clear();",
            "        std::cin.ignore(std::numeric_limits<std::streamsize>::max(), '\\n');",
            "        std::cout << \"Valore non valido. Riprova.\\n\";",
            "    }",
            "}",
            "template<class T> void stampa(const T& valore) { std::cout << valore << std::endl; }",
            "}",
            string.Empty
        });
        File.WriteAllText(Path.Combine(CppExtensionsIncludePath, "cvplus_utils.hpp"), headerText, new UTF8Encoding(false));
    }

    private string CppGuidesDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "CppGuides");

    private IEnumerable<(string Trigger, string Display, string Insert, string Description)> GetInstalledLibraryCompletions() =>
        CppLibraryManager.LoadInstalled().SelectMany(x => x.Manifest.Completions.Select(c => (c.Trigger, c.Display, c.Insert, c.Description)));

    private void OpenPdfGuide(string filePath)
    {
        if (!File.Exists(filePath))
        {
            MessageBox.Show("Guida PDF non trovata:\n" + filePath, "CV+", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
    }

    private void InstallLocalCppLibrary()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Seleziona una libreria CV+",
            Filter = "Pacchetti CV+ (*.cvplus;*.zip)|*.cvplus;*.zip|Tutti i file (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            InstalledCppLibrary installed = CppLibraryManager.InstallPackage(dialog.FileName);
            StatusText.Text = $"Libreria installata: {installed.Manifest.Name} {installed.Manifest.Version}";
            MessageBox.Show($"Libreria installata correttamente.\n\n{installed.Manifest.Name} {installed.Manifest.Version}\nTipo: {installed.Manifest.Type}", "CV+", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Installazione non riuscita:\n" + ex.Message, "CV+", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportLooseCppLibrary()
    {
        var libraryDialog = new Microsoft.Win32.OpenFileDialog { Title="Seleziona libreria statica o dinamica", Filter="Librerie MinGW (*.a;*.dll;*.lib)|*.a;*.dll;*.lib|Tutti i file (*.*)|*.*" };
        if (libraryDialog.ShowDialog(this) != true) return;
        var headersDialog = new Microsoft.Win32.OpenFileDialog { Title="Seleziona uno o più header della libreria", Filter="Header C++ (*.h;*.hpp)|*.h;*.hpp", Multiselect=true };
        if (headersDialog.ShowDialog(this) != true) return;
        var guideDialog = new Microsoft.Win32.OpenFileDialog { Title="Seleziona la guida PDF (facoltativa)", Filter="Guide PDF (*.pdf)|*.pdf" };
        string? guide = guideDialog.ShowDialog(this) == true ? guideDialog.FileName : null;
        try
        {
            InstalledCppLibrary installed = CppLibraryManager.InstallLooseLibrary(libraryDialog.FileName, headersDialog.FileNames, guide);
            MessageBox.Show($"Libreria importata correttamente.\n\n{installed.Manifest.Name}\nTipo: {installed.Manifest.Type}", "CV+", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = "Libreria locale importata";
        }
        catch (Exception ex) { MessageBox.Show("Importazione non riuscita:\n" + ex.Message, "CV+", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void InstallDeterminantSample()
    {
        try
        {
            string source = Path.Combine(AppContext.BaseDirectory, "Assets", "SampleLibraries", "Determinante");
            string temp = Path.Combine(Path.GetTempPath(), "CVPlus_Determinante_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            string package = Path.Combine(temp, "cvplus-determinante.cvplus");
            string created = CppLibraryManager.CreatePackage(source, package, "CV+ Determinante", "1.0.0", "static", "Calcolo del determinante di una matrice quadrata", BundledCompilerPath);
            // Inserisce guida e completamento nel pacchetto appena creato.
            string unpack = Path.Combine(temp, "package");
            System.IO.Compression.ZipFile.ExtractToDirectory(created, unpack);
            string guideDir = Path.Combine(unpack, "guides"); Directory.CreateDirectory(guideDir);
            File.Copy(Path.Combine(CppGuidesDirectory, "Guida_Libreria_Determinante.pdf"), Path.Combine(guideDir, "Guida_Libreria_Determinante.pdf"), true);
            string manifestPath = Path.Combine(unpack, "manifest.json");
            CppLibraryManifest manifest = JsonSerializer.Deserialize<CppLibraryManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            manifest.GuideFiles.Add("guides/Guida_Libreria_Determinante.pdf");
            manifest.Completions.Add(new CppLibraryCompletion { Trigger = "determinante", Display = "cvplus::determinante", Insert = "double det = cvplus::determinante(matrice);", Description = "Calcola il determinante di una matrice quadrata" });
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            File.Delete(created); System.IO.Compression.ZipFile.CreateFromDirectory(unpack, created);
            InstalledCppLibrary installed = CppLibraryManager.InstallPackage(created);
            StatusText.Text = "Libreria statica Determinante installata";
            MessageBox.Show("Esempio installato.\n\nUsa:\n#include <cvplus_determinante.hpp>\n\nPoi:\ncvplus::determinante(matrice)", "CV+", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Creazione/installazione dell'esempio non riuscita:\n" + ex.Message, "CV+", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenCppExtensions_Click(object sender, RoutedEventArgs e)
    {
        if (_verificationMode)
        {
            StatusText.Text = "Estensioni C++ disabilitate in modalità verifica";
            return;
        }

        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock { Text = "ESTENSIONI C++ PER CV+", Foreground = Brushes.White, FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,8) });
        panel.Children.Add(new TextBlock { Text = "Snippet, librerie header-only, statiche MinGW (.a) e dinamiche Windows (.dll + .dll.a). Installare solo pacchetti attendibili.", Foreground = new SolidColorBrush(Color.FromRgb(180,180,180)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,14) });

        var actions = new WrapPanel { Margin = new Thickness(0,0,0,14) };
        Button installLocal = new() { Content = "INSTALLA LIBRERIA LOCALE", Margin = new Thickness(0,0,8,8), Padding = new Thickness(12,7,12,7), Background = new SolidColorBrush(Color.FromRgb(14,99,156)), Foreground = Brushes.White };
        installLocal.Click += (_, _) => { InstallLocalCppLibrary(); CloseActiveOverlay(); OpenCppExtensions_Click(sender,e); };
        Button importFiles = new() { Content = "IMPORTA .A / .DLL + HEADER + PDF", Margin = new Thickness(0,0,8,8), Padding = new Thickness(12,7,12,7), Background = new SolidColorBrush(Color.FromRgb(104,75,145)), Foreground = Brushes.White };
        importFiles.Click += (_, _) => { ImportLooseCppLibrary(); CloseActiveOverlay(); OpenCppExtensions_Click(sender,e); };
        Button generalGuide = new() { Content = "GUIDA LIBRERIE PDF", Margin = new Thickness(0,0,8,8), Padding = new Thickness(12,7,12,7), Background = new SolidColorBrush(Color.FromRgb(70,70,70)), Foreground = Brushes.White };
        generalGuide.Click += (_, _) => OpenPdfGuide(Path.Combine(CppGuidesDirectory, "Guida_Librerie_CVPlus.pdf"));
        Button sample = new() { Content = "INSTALLA ESEMPIO DETERMINANTE", Margin = new Thickness(0,0,8,8), Padding = new Thickness(12,7,12,7), Background = new SolidColorBrush(Color.FromRgb(19,130,85)), Foreground = Brushes.White };
        sample.Click += (_, _) => { InstallDeterminantSample(); CloseActiveOverlay(); OpenCppExtensions_Click(sender,e); };
        Button importHeader = new()
        {
            Content = "IMPORTA .H / .HPP",
            Margin = new Thickness(0,0,8,8),
            Padding = new Thickness(12,7,12,7),
            Background = new SolidColorBrush(Color.FromRgb(15,118,110)),
            Foreground = Brushes.White,
            IsEnabled = ImportHeaderButton.IsEnabled,
            ToolTip = ImportHeaderButton.IsEnabled
                ? "Importa un file header locale nell'editor C++."
                : "Gestione dei file header disabilitata dal docente."
        };
        importHeader.Click += (_, _) =>
        {
            CloseActiveOverlay();
            ImportHeader_Click(importHeader, new RoutedEventArgs());
        };
        actions.Children.Add(installLocal);
        actions.Children.Add(importFiles);
        actions.Children.Add(generalGuide);
        actions.Children.Add(sample);
        actions.Children.Add(importHeader);
        panel.Children.Add(actions);

        foreach (CppExtensionDefinition extension in CppExtensionCatalog)
        {
            var row = new Grid { Margin = new Thickness(0,0,0,9), Background = new SolidColorBrush(Color.FromRgb(37,37,38)) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Margin = new Thickness(12,9,12,9) };
            text.Children.Add(new TextBlock { Text = extension.Name, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 15 });
            text.Children.Add(new TextBlock { Text = extension.Description, Foreground = new SolidColorBrush(Color.FromRgb(190,190,190)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,3,0,0) });
            row.Children.Add(text);
            var guide = new Button { Content = "GUIDA", Margin = new Thickness(8), Padding = new Thickness(12,6,12,6), MinWidth = 82, Background = new SolidColorBrush(Color.FromRgb(70,70,70)), Foreground = Brushes.White };
            guide.Click += (_, _) => OpenPdfGuide(Path.Combine(CppGuidesDirectory, extension.GuideFile)); Grid.SetColumn(guide,1); row.Children.Add(guide);
            bool installed = _installedCppExtensions.Contains(extension.Id);
            var button = new Button { Content = installed ? "RIMUOVI" : "INSTALLA", Tag = extension.Id, Margin = new Thickness(8), Padding = new Thickness(12,6,12,6), MinWidth = 92, Background = new SolidColorBrush(installed ? Color.FromRgb(90,90,90) : Color.FromRgb(14,99,156)), Foreground = Brushes.White };
            Grid.SetColumn(button, 2);
            button.Click += (_, _) => { string id=(string)button.Tag; if (_installedCppExtensions.Contains(id)) _installedCppExtensions.Remove(id); else _installedCppExtensions.Add(id); SaveCppExtensions(); StatusText.Text="Estensioni C++ aggiornate"; CloseActiveOverlay(); OpenCppExtensions_Click(sender,e); };
            row.Children.Add(button); panel.Children.Add(row);
        }

        panel.Children.Add(new TextBlock { Text = "LIBRERIE INSTALLATE", Foreground = Brushes.White, FontSize = 19, FontWeight = FontWeights.Bold, Margin = new Thickness(0,18,0,8) });
        var libraries = CppLibraryManager.LoadInstalled();
        if (libraries.Count == 0) panel.Children.Add(new TextBlock { Text = "Nessuna libreria aggiuntiva installata.", Foreground = new SolidColorBrush(Color.FromRgb(180,180,180)), Margin = new Thickness(4,4,4,10) });
        foreach (InstalledCppLibrary library in libraries)
        {
            var row = new Grid { Margin = new Thickness(0,0,0,9), Background = new SolidColorBrush(Color.FromRgb(37,37,38)) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            var text = new StackPanel { Margin=new Thickness(12,9,12,9) }; text.Children.Add(new TextBlock { Text=$"{library.Manifest.Name} {library.Manifest.Version}", Foreground=Brushes.White, FontWeight=FontWeights.SemiBold }); text.Children.Add(new TextBlock { Text=$"{library.Manifest.Type} - {library.Manifest.Description}", Foreground=new SolidColorBrush(Color.FromRgb(190,190,190)), TextWrapping=TextWrapping.Wrap }); row.Children.Add(text);
            var guides=CppLibraryManager.GetGuideFiles(library); var g=new Button { Content="GUIDA", IsEnabled=guides.Count>0, Margin=new Thickness(8), Padding=new Thickness(12,6,12,6), Foreground=Brushes.White, Background=new SolidColorBrush(Color.FromRgb(70,70,70)) }; g.Click += (_,_) => { if(guides.Count>0) OpenPdfGuide(guides[0]); }; Grid.SetColumn(g,1); row.Children.Add(g);
            var remove=new Button { Content="RIMUOVI", Margin=new Thickness(8), Padding=new Thickness(12,6,12,6), Foreground=Brushes.White, Background=new SolidColorBrush(Color.FromRgb(120,60,60)) }; remove.Click += (_,_) => { CppLibraryManager.Uninstall(library.Manifest.Id); StatusText.Text="Libreria rimossa"; CloseActiveOverlay(); OpenCppExtensions_Click(sender,e); }; Grid.SetColumn(remove,2); row.Children.Add(remove);
            panel.Children.Add(row);
        }
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        ShowFullscreenOverlay("Estensioni C++", scroll);
    }

    private static bool ReadHeaderManagementAllowed(JsonElement root)
    {
        if (root.TryGetProperty("allowHeaderFileManagement", out JsonElement allow) &&
            (allow.ValueKind == JsonValueKind.True || allow.ValueKind == JsonValueKind.False))
            return allow.GetBoolean();

        if (root.TryGetProperty("headerFileManagementEnabled", out JsonElement enabled) &&
            (enabled.ValueKind == JsonValueKind.True || enabled.ValueKind == JsonValueKind.False))
            return enabled.GetBoolean();

        if (root.TryGetProperty("disableHeaderFileManagement", out JsonElement disabled) &&
            (disabled.ValueKind == JsonValueKind.True || disabled.ValueKind == JsonValueKind.False))
            return !disabled.GetBoolean();

        // Stato sicuro predefinito: senza un'autorizzazione esplicita del server i pulsanti restano disabilitati.
        return false;
    }

    private void ApplyHeaderManagementPermission(bool allowed)
    {
        AddHeaderButton.IsEnabled = allowed;
        RenameHeaderButton.IsEnabled = allowed;
        DeleteHeaderButton.IsEnabled = allowed;
        ImportHeaderButton.IsEnabled = allowed && !_verificationMode;

        string? tooltip = allowed
            ? null
            : "Gestione dei file header disabilitata dal docente.";
        AddHeaderButton.ToolTip = tooltip;
        RenameHeaderButton.ToolTip = tooltip;
        DeleteHeaderButton.ToolTip = tooltip;
        ImportHeaderButton.ToolTip = _verificationMode
            ? "Importazione header disabilitata in modalità verifica."
            : tooltip;
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
        GuideButton.IsEnabled = false;
        CppExtensionsButton.IsEnabled = false;
        CppExtensionsButton.ToolTip = "Estensioni C++ disabilitate in modalità verifica.";
        ImportHeaderButton.IsEnabled = false;
        ImportHeaderButton.ToolTip = "Importazione header disabilitata in modalità verifica.";
        GoogleDriveButton.IsEnabled = false;
        GoogleDriveButton.ToolTip = "Google Drive è disabilitato in modalità verifica.";
        GuideButton.ToolTip = "La guida è disponibile soltanto in modalità esercitazione.";
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
        GuideButton.IsEnabled = true;
        CppExtensionsButton.IsEnabled = true;
        CppExtensionsButton.ToolTip = "Installa componenti compatibili per editor e librerie C++.";
        ImportHeaderButton.IsEnabled = AddHeaderButton.IsEnabled;
        ImportHeaderButton.ToolTip = AddHeaderButton.IsEnabled ? "Importa un file header locale nell'editor C++." : "Gestione dei file header disabilitata dal docente.";
        GoogleDriveButton.IsEnabled = true;
        GoogleDriveButton.ToolTip = "Salva l'esercizio nel tuo Google Drive";
        GuideButton.ToolTip = "Apri la guida visuale del compilatore";
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
                new ProcessStartInfo(updaterScript)
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath()
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

    private void ImportHeader_Click(object sender, RoutedEventArgs e)
    {
        if (_verificationMode)
        {
            ShowVerificationSafeMessage(
                "L'importazione dei file header è disabilitata in modalità verifica.",
                "Operazione non disponibile",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                MessageBoxResult.OK);
            return;
        }

        if (!ImportHeaderButton.IsEnabled)
            return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importa un file header C++",
            Filter = "Header C++ (*.h;*.hpp)|*.h;*.hpp|Tutti i file (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            string headerName = NormalizeHeaderFileName(Path.GetFileName(dialog.FileName));
            string headerCode = File.ReadAllText(dialog.FileName);

            if (!_exerciseStates.TryGetValue(_activeKey, out ExerciseState? state))
                return;

            if (!string.IsNullOrWhiteSpace(state.HeaderCode))
            {
                MessageBoxResult replace = ShowVerificationSafeMessage(
                    $"Questo esercizio contiene già {GetCurrentHeaderFileName()}. Vuoi sostituirlo con {headerName}?",
                    "Sostituisci header",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);
                if (replace != MessageBoxResult.Yes)
                    return;

                string oldInclude = $"#include \"{GetCurrentHeaderFileName()}\"";
                Editor.Text = Editor.Text.Replace(oldInclude, "", StringComparison.OrdinalIgnoreCase).TrimStart();
            }

            state.HeaderFileName = headerName;
            state.HeaderCode = headerCode;
            HeaderTab.Header = headerName;
            HeaderTab.Visibility = Visibility.Visible;
            HeaderEditor.Text = headerCode;
            HeaderTab.IsSelected = true;

            string includeLine = $"#include \"{headerName}\"";
            if (!Editor.Text.Contains(includeLine, StringComparison.Ordinal))
                Editor.Text = includeLine + Environment.NewLine + Editor.Text;

            SaveCurrentExercise();
            StatusText.Text = $"Header importato: {headerName}";
        }
        catch (Exception ex)
        {
            ShowVerificationSafeMessage(
                "Impossibile importare il file header.\n\n" + ex.Message,
                "Importazione non riuscita",
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                MessageBoxResult.OK);
        }
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
    private void Guide_Click(object sender, RoutedEventArgs e)
    {
        if (_verificationMode)
        {
            ShowVerificationSafeMessage(
                "La guida è disponibile soltanto in modalità esercitazione.",
                "Guida non disponibile",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var guideWindow = new Window
        {
            Title = "Guida visuale — CV+ Compilatore Alunno",
            Owner = this,
            Width = 920,
            Height = 720,
            MinWidth = 760,
            MinHeight = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(7, 18, 34)),
            Foreground = Brushes.White,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false
        };

        var content = new System.Windows.Controls.StackPanel { Margin = new Thickness(22) };
        content.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "GUIDA VISUALE DEL COMPILATORE",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(183, 243, 255))
        });
        content.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "I pulsanti qui sotto sono esempi non cliccabili. Colore, nome e descrizione corrispondono ai comandi presenti nel programma.",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(168, 181, 199)),
            Margin = new Thickness(0, 6, 0, 18)
        });

        AddGuideSection(content, "EDITOR E COMPILAZIONE");
        AddGuideItem(content, "Compila e apri CMD", "#059669", "Compila il codice C++17 ed esegue il programma nella console. Gli errori di compilazione vengono mostrati nell'area output.");
        AddGuideItem(content, "main.cpp", "#102540", "È il file principale. Con SHIFT + clic sull'editor si apre l'editor grande a tutto schermo.");
        AddGuideItem(content, "Aggiungi esercizio.h", "#7C3AED", "Crea un file header .h. Il comando può essere abilitato o disabilitato dal docente.");
        AddGuideItem(content, "Rinomina .h", "#9333EA", "Rinomina il file header aperto mantenendo l'estensione .h.");
        AddGuideItem(content, "Elimina .h", "#B91C1C", "Elimina il file header corrente dopo la conferma.");

        AddGuideSection(content, "SALVATAGGIO E INVIO");
        AddGuideItem(content, "Invia al docente", "#0E8FE8", "Invia codice, dati dell'alunno, esercizio e risultati al server del docente.");
        AddGuideItem(content, "Google Drive", "#FFFFFF", "Disponibile solo in modalità esercitazione. Se esiste soltanto main.cpp salva un file .cpp con il nome scelto e aggiunge in testa i dati dell’alunno, data, ora e compilatore. Se esiste anche un file .h salva uno ZIP con il nome scelto. Il file si trova in Il mio Drive → CV+ Compilatore Alunno. Alla chiusura di CV+ l’account viene disconnesso.", "#1F2937");
        AddGuideItem(content, "Test server", "#5B4FE8", "Controlla se il server docente indicato nel campo IP e porta è raggiungibile.");

        AddGuideSection(content, "DATI DELL'ESERCIZIO");
        AddGuideItem(content, "N° registro / Nome / Classe", "#24344D", "Identificano l'alunno. Compilali prima di inviare o salvare l'esercizio.");
        AddGuideItem(content, "Tipologia / N° esercizio", "#24344D", "Indicano il tipo di attività e il numero dell'esercizio attualmente aperto.");
        AddGuideItem(content, "IP docente : porta", "#24344D", "Indirizzo del computer del docente e porta del server. Può essere rilevato automaticamente sulla rete.");
        AddGuideItem(content, "◀  ▶", "#0E78C7", "Passano all'esercizio precedente o successivo salvando lo stato dell'editor.");

        AddGuideSection(content, "MODALITÀ E ASSISTENZA");
        AddGuideItem(content, "STANDARD C++17", "#0F3550", "Il compilatore usa lo standard C++17 e include la toolchain GCC nell'installazione.");
        AddGuideItem(content, "ESERCITAZIONE", "#102D25", "Modalità normale: consente guida, aggiornamenti e strumenti autorizzati dal docente.");
        AddGuideItem(content, "VERIFICA", "#4B260F", "Modalità controllata dal docente: alcune funzioni vengono bloccate e la finestra resta a schermo intero.");
        AddGuideItem(content, "Aiuto scrittura C++", "#155E75", "Quando il docente lo abilita, propone completamenti C++, costrutti e rientri automatici nell'editor.");

        AddGuideSection(content, "OUTPUT");
        AddGuideItem(content, "Output compilazione", "#10243A", "Mostra messaggi del compilatore ed eventuali errori. L'output prodotto dal programma viene visualizzato in bianco.");
        AddGuideItem(content, "Stato", "#24344D", "In basso comunica operazioni in corso, collegamento al server, salvataggio e risultato dei comandi.");

        var closeButton = new System.Windows.Controls.Button
        {
            Content = "Chiudi guida",
            Width = 150,
            Padding = new Thickness(14, 9, 14, 9),
            Margin = new Thickness(0, 20, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromRgb(14, 120, 199)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(68, 183, 255)),
            FontWeight = FontWeights.SemiBold
        };
        closeButton.Click += (_, _) => guideWindow.Close();
        content.Children.Add(closeButton);

        guideWindow.Content = new System.Windows.Controls.ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled
        };
        guideWindow.ShowDialog();
    }

    private static void AddGuideSection(System.Windows.Controls.Panel panel, string title)
    {
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 209, 102)),
            Margin = new Thickness(0, 16, 0, 7)
        });
    }

    private static void AddGuideItem(
        System.Windows.Controls.Panel panel,
        string buttonText,
        string backgroundHex,
        string description,
        string foregroundHex = "#FFFFFF")
    {
        var row = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(215) });
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sample = new System.Windows.Controls.Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString(backgroundHex)!,
            BorderBrush = new SolidColorBrush(Color.FromRgb(72, 98, 132)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 14, 0),
            Child = new System.Windows.Controls.TextBlock
            {
                Text = buttonText,
                Foreground = (Brush)new BrushConverter().ConvertFromString(foregroundHex)!,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            }
        };
        System.Windows.Controls.Grid.SetColumn(sample, 0);
        row.Children.Add(sample);

        var text = new System.Windows.Controls.TextBlock
        {
            Text = description,
            Foreground = new SolidColorBrush(Color.FromRgb(218, 229, 242)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Controls.Grid.SetColumn(text, 1);
        row.Children.Add(text);
        panel.Children.Add(row);
    }

    private async void GoogleDrive_Click(object sender, RoutedEventArgs e)
    {
        if (_verificationMode)
        {
            MessageBox.Show(
                "Il salvataggio su Google Drive è disponibile soltanto in modalità esercitazione.",
                "Google Drive disabilitato",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Se l'autorizzazione è già in corso, il pulsante funziona come ANNULLA.
        // Questo evita che resti bloccato quando l'utente chiude la finestra del browser.
        if (_googleDriveOperationRunning)
        {
            GoogleDriveButton.Content = "Annullamento...";
            GoogleDriveButton.IsEnabled = false;
            _googleDriveOperationCts?.Cancel();
            return;
        }

        bool hasHeader = !string.IsNullOrWhiteSpace(HeaderEditor.Text);
        string defaultName = hasHeader
            ? $"{GetTaskType()}-Esercizio-{GetExerciseNumber()}"
            : $"{GetTaskType()}-Esercizio-{GetExerciseNumber()}.cpp";
        string requestedName = Microsoft.VisualBasic.Interaction.InputBox(
            hasHeader
                ? "Scegli il nome dell'archivio ZIP da salvare su Google Drive."
                : "Scegli il nome del file C++ da salvare su Google Drive.",
            hasHeader ? "Nome archivio Google Drive" : "Nome file C++ Google Drive",
            defaultName);

        if (string.IsNullOrWhiteSpace(requestedName))
            return;

        _googleDriveOperationCts?.Dispose();
        _googleDriveOperationCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        CancellationToken cancellationToken = _googleDriveOperationCts.Token;
        _googleDriveOperationRunning = true;

        try
        {
            // Il pulsante resta premibile: un secondo clic annulla l'accesso rimasto in attesa.
            GoogleDriveButton.IsEnabled = true;
            GoogleDriveButton.Content = "Annulla accesso Google";
            StatusText.Text = "Apertura autorizzazione Google Drive...";

            var snapshot = new GoogleDriveExerciseSnapshot(
                StudentIdBox.Text.Trim(),
                StudentNameBox.Text.Trim(),
                ClassBox.Text.Trim(),
                GetTaskType(),
                GetExerciseNumber(),
                Editor.Text,
                HeaderEditor.Text,
                GetCurrentHeaderFileName(),
                _compileOutput,
                _programOutput,
                DateTime.Now,
                requestedName.Trim(),
                "GCC g++ - standard C++17");

            GoogleDriveSaveResult result = await GoogleDriveExerciseService.SaveExerciseAsync(snapshot, cancellationToken);
            StatusText.Text = "Esercizio salvato su Google Drive";
            GoogleDriveButton.Content = "Salvato su Drive ✓";

            MessageBox.Show(
                $"Esercizio salvato nel Google Drive dell'account autorizzato.\n\n" +
                $"Cartella: Il mio Drive → {GoogleDriveExerciseService.DriveFolderDisplayName}\n" +
                $"File: {result.FileName}",
                "Google Drive", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Accesso a Google annullato";
            // Elimina anche eventuali dati parziali creati durante il flusso OAuth.
            GoogleDriveExerciseService.ClearLocalAuthorizationCache();
        }
        catch (FileNotFoundException ex)
        {
            StatusText.Text = "Configurazione Google Drive mancante";
            MessageBox.Show(ex.Message, "Google Drive non configurato", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Google.GoogleApiException ex)
        {
            StatusText.Text = "Errore Google Drive";
            MessageBox.Show($"Google Drive ha restituito un errore:\n{ex.Message}", "Google Drive", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Salvataggio su Drive non riuscito";
            MessageBox.Show($"Impossibile salvare l'esercizio su Google Drive.\n\n{ex.Message}", "Google Drive", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _googleDriveOperationRunning = false;
            _googleDriveOperationCts?.Dispose();
            _googleDriveOperationCts = null;
            GoogleDriveButton.IsEnabled = !_verificationMode;
            if (GoogleDriveButton.Content?.ToString()?.Contains("✓") != true)
                GoogleDriveButton.Content = "Google Drive";
        }
    }

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

    private void ClearSessionCppAddons()
    {
        CppLibraryManager.UninstallAll();
        _installedCppExtensions.Clear();
        try
        {
            if (File.Exists(CppExtensionsSettingsPath))
                File.Delete(CppExtensionsSettingsPath);
        }
        catch
        {
            // La pulizia delle preferenze non deve impedire avvio o chiusura.
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_verificationMode && !_allowClose)
        {
            e.Cancel = true;
            Activate();
            return;
        }

        // Annulla subito un eventuale login/upload Google ancora in corso.
        try { _googleDriveOperationCts?.Cancel(); } catch { }

        // Privacy: revoca l'autorizzazione dell'app e cancella sempre i token locali.
        // Un eventuale errore di rete non deve impedire la chiusura del programma.
        try { GoogleDriveExerciseService.DisconnectAsync().GetAwaiter().GetResult(); } catch { }

        NotifyServerClientClosed();
        ClearSessionCppAddons();
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
