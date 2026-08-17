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
    private readonly DispatcherTimer _quizAssignmentTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _liveMonitorTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _quizIdentityBlinkTimer = new() { Interval = TimeSpan.FromMilliseconds(520) };
    private bool _quizIdentityBlinkOn;
    private readonly Dictionary<string, ExerciseState> _exerciseStates = new(StringComparer.OrdinalIgnoreCase);

    private string _compileOutput = "";
    private string _programOutput = "";
    private string? _exePath;
    private string _activeKey = "";
    private DateTime _activeStartedUtc = DateTime.UtcNow;
    private bool _loadingExercise;
    private bool _refreshingExerciseList;
    private bool _verificationMode;
    private bool _quizVerificationMode;
    private string _lastQuizAssignmentId = "";
    private bool _quizAssignmentCheckRunning;
    private QuizVerificationWindow? _activeQuizWindow;
    private Window? _quizWaitingWindow;
    private DateTime _activeQuizOpenedUtc = DateTime.MinValue;
    // Connessione Verifiche Quiz separata dal server normale Compiti alunni.
    private string _quizServerBase = "";
    private string _quizSessionCode = "";
    private DateTime _lastQuizServerSeenUtc = DateTime.MinValue;
    private bool _allowClose;
    private bool _serverModeCheckRunning;
    private bool _liveMonitorSyncRunning;
    private string _lastRemoteCommandId = "";
    private bool _modalDialogOpen;
    private bool _startupUpdateChecked;
    private System.Windows.Controls.Grid? _activeOverlay;
    private bool _compilationAllowed = true;
    private bool _headerManagementAllowed;
    private UdpClient? _teacherDiscoveryUdp;
    private CancellationTokenSource? _teacherDiscoveryCts;
    private const int TeacherDiscoveryPort = 5051;
    private IHighlightingDefinition? _cppHighlighting;
    private bool _editorAssistanceEnabled;
    private readonly Dictionary<TextEditor, CompletionWindow> _completionWindows = new();
    private CancellationTokenSource? _googleDriveOperationCts;
    private bool _googleDriveOperationRunning;
    private readonly HashSet<string> _installedCppExtensions = new(StringComparer.OrdinalIgnoreCase);
    private Process? _shellProcess;
    private bool _shellVisible;
    private readonly List<string> _shellCommandHistory = new();
    private int _shellHistoryIndex = 0;
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
        Closed += (_, _) => { _liveMonitorTimer.Stop(); _quizAssignmentTimer.Stop(); _quizIdentityBlinkTimer.Stop(); StopTeacherDiscoveryListener(); StopShell(); };
        if (!File.Exists(BundledCompilerPath))
            OutputBox.Text = "Installazione incompleta: compilatore C++17 incorporato assente. Reinstallare il programma.";
        ActivateExercise(GetTaskType(), GetExerciseNumber());
        RefreshExerciseList();

        _clockTimer.Tick += (_, _) => UpdateExerciseClock();
        _clockTimer.Start();
        _modeTimer.Tick += async (_, _) => await RefreshServerModeAsync(false);
        _modeTimer.Start();
        _quizAssignmentTimer.Tick += async (_, _) => await CheckQuizAssignmentAsync();
        _quizAssignmentTimer.Start();
        _liveMonitorTimer.Tick += async (_, _) => await SyncLiveMonitorAsync();
        _liveMonitorTimer.Start();
        _quizIdentityBlinkTimer.Tick += (_, _) => UpdateQuizIdentityBlink();

        StudentNameBox.TextChanged += (_, _) => UpdateWindowTitle();

        Loaded += async (_, _) =>
        {
            UpdateLocalIpText();
            UpdateTaskSummary();
            UpdateWindowTitle();
            await RefreshServerModeAsync(false);

            // Controllo aggiornamenti automatico una sola volta a ogni avvio.
            // Se non c'è nulla di nuovo resta silenzioso; se trova una Release
            // più recente chiede all'utente se desidera installarla.
            if (!_startupUpdateChecked && !_verificationMode)
            {
                _startupUpdateChecked = true;
                await Task.Delay(700);
                await CheckUpdatesAsync(silentWhenCurrent: true, automaticCheck: true);
            }
        };
    }

    private void Shell_Click(object sender, RoutedEventArgs e)
    {
        if (_verificationMode)
        {
            StatusText.Text = "Shell disabilitata in modalità verifica";
            return;
        }

        if (_shellVisible)
        {
            HideShell();
            return;
        }

        ShowShell();
    }

    private void ShowShell()
    {
        _shellVisible = true;
        EditorTabs.Visibility = Visibility.Collapsed;
        ShellPanel.Visibility = Visibility.Visible;
        OutputPanel.Visibility = Visibility.Collapsed;
        OutputRow.Height = new GridLength(0);
        ShellButton.Content = "C++ EDITOR";
        ShellButton.ToolTip = "Torna all'editor C++";
        ApplyShellUiLock();

        if (_shellProcess == null || _shellProcess.HasExited)
            StartShell();

        ShellInputBox.Focus();
        StatusText.Text = "Shell CMD — Documenti — G++ nel PATH";
    }

    private void HideShell()
    {
        _shellVisible = false;
        ShellPanel.Visibility = Visibility.Collapsed;
        EditorTabs.Visibility = Visibility.Visible;
        OutputPanel.Visibility = Visibility.Visible;
        OutputRow.Height = new GridLength(125);
        ShellButton.Content = ">_ SHELL";
        ShellButton.ToolTip = "Apri la shell CMD nella cartella Documenti con G++ già nel PATH";
        ApplyShellUiLock();
        Editor.Focus();
        StatusText.Text = _verificationMode ? "Modalità verifica attiva" : "Editor C++ attivo";
    }

    private void ApplyShellUiLock()
    {
        // La Shell ha priorità assoluta sullo stato dei controlli: il server CPPVisual
        // può aggiornare i permessi, ma non può riattivare questi pulsanti finché
        // la Shell è visibile. In modalità verifica la Shell è sempre disabilitata.
        bool editorActionsEnabled = !_shellVisible && !_quizVerificationMode;

        RunButton.IsEnabled = editorActionsEnabled && _compilationAllowed;
        AddHeaderButton.IsEnabled = editorActionsEnabled && _headerManagementAllowed;
        RenameHeaderButton.IsEnabled = editorActionsEnabled && _headerManagementAllowed;
        DeleteHeaderButton.IsEnabled = editorActionsEnabled && _headerManagementAllowed;
        ImportHeaderButton.IsEnabled = editorActionsEnabled && _headerManagementAllowed && !_verificationMode;
        SendButton.IsEnabled = editorActionsEnabled;
        GoogleDriveButton.IsEnabled = editorActionsEnabled && !_verificationMode && !_googleDriveOperationRunning;
        TestServerButton.IsEnabled = editorActionsEnabled;
        UpdateButton.IsEnabled = editorActionsEnabled && !_verificationMode;
        GuideButton.IsEnabled = editorActionsEnabled && !_verificationMode;
        PreviousExerciseButton.IsEnabled = editorActionsEnabled;
        NextExerciseButton.IsEnabled = editorActionsEnabled;

        CppExtensionsButton.IsEnabled = editorActionsEnabled && !_verificationMode;
        ShellButton.IsEnabled = !_verificationMode && !_quizVerificationMode;
    }

    private void StartShell()
    {
        StopShell();
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents) || !Directory.Exists(documents))
            documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        try
        {
            var psi = new ProcessStartInfo("cmd.exe")
            {
                Arguments = "/Q /D",
                WorkingDirectory = documents,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            ConfigureCompilerEnvironment(psi);

            _shellProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _shellProcess.OutputDataReceived += Shell_OutputDataReceived;
            _shellProcess.ErrorDataReceived += Shell_OutputDataReceived;
            _shellProcess.Exited += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                AppendShellText("\r\n[Shell terminata. Premi SHELL/C++ EDITOR e riaprila per una nuova sessione.]\r\n");
            });
            _shellProcess.Start();
            _shellProcess.BeginOutputReadLine();
            _shellProcess.BeginErrorReadLine();

            ShellOutputBox.Clear();
            AppendShellText("Microsoft Windows CMD integrato in CV+\r\n");
            AppendShellText($"Cartella iniziale: {documents}\r\n");
            AppendShellText($"Compilatore: {BundledCompilerPath}\r\n");
            AppendShellText("G++ è già aggiunto al PATH. Digita help per l'elenco dei comandi DOS.\r\n\r\n");
            WriteShellCommand("prompt $P$G");
            WriteShellCommand("cd /d \"" + documents + "\"");
        }
        catch (Exception ex)
        {
            AppendShellText("Impossibile avviare CMD: " + ex.Message + "\r\n");
            StatusText.Text = "Errore avvio Shell";
        }
    }

    private void Shell_OutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;
        Dispatcher.BeginInvoke(() => AppendShellText(e.Data + Environment.NewLine));
    }

    private void AppendShellText(string text)
    {
        ShellOutputBox.AppendText(text);
        ShellOutputBox.ScrollToEnd();
    }

    private bool HandleShellHistoryKey(Key key)
    {
        if (!_shellVisible || _shellCommandHistory.Count == 0)
            return false;

        if (key == Key.Up)
        {
            if (_shellHistoryIndex < 0 || _shellHistoryIndex > _shellCommandHistory.Count)
                _shellHistoryIndex = _shellCommandHistory.Count;
            if (_shellHistoryIndex > 0)
                _shellHistoryIndex--;

            ShellInputBox.Text = _shellCommandHistory[_shellHistoryIndex];
            ShellInputBox.CaretIndex = ShellInputBox.Text.Length;
            ShellInputBox.Focus();
            return true;
        }

        if (key == Key.Down)
        {
            if (_shellHistoryIndex < 0)
                _shellHistoryIndex = _shellCommandHistory.Count;

            if (_shellHistoryIndex < _shellCommandHistory.Count - 1)
            {
                _shellHistoryIndex++;
                ShellInputBox.Text = _shellCommandHistory[_shellHistoryIndex];
            }
            else
            {
                _shellHistoryIndex = _shellCommandHistory.Count;
                ShellInputBox.Clear();
            }

            ShellInputBox.CaretIndex = ShellInputBox.Text.Length;
            ShellInputBox.Focus();
            return true;
        }

        return false;
    }

    private void ShellInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if ((key == Key.Up || key == Key.Down) && HandleShellHistoryKey(key))
        {
            e.Handled = true;
            return;
        }

        if (key != Key.Enter && key != Key.Return) return;
        e.Handled = true;
        string command = ShellInputBox.Text;
        ShellInputBox.Clear();

        if (string.IsNullOrWhiteSpace(command))
        {
            WriteShellCommand("");
            return;
        }

        _shellCommandHistory.Add(command);
        _shellHistoryIndex = _shellCommandHistory.Count;

        AppendShellText("> " + command + Environment.NewLine);

        if (command.Trim().Equals("cls", StringComparison.OrdinalIgnoreCase))
        {
            ShellOutputBox.Clear();
            return;
        }

        if (command.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            StopShell();
            AppendShellText("[Shell terminata]\r\n");
            return;
        }

        WriteShellCommand(command);
    }

    private void WriteShellCommand(string command)
    {
        try
        {
            if (_shellProcess == null || _shellProcess.HasExited)
                StartShell();
            _shellProcess?.StandardInput.WriteLine(command);
            _shellProcess?.StandardInput.Flush();
        }
        catch (Exception ex)
        {
            AppendShellText("Errore Shell: " + ex.Message + Environment.NewLine);
        }
    }

    private void StopShell()
    {
        try
        {
            if (_shellProcess != null && !_shellProcess.HasExited)
            {
                _shellProcess.StandardInput.WriteLine("exit");
                _shellProcess.StandardInput.Flush();
                if (!_shellProcess.WaitForExit(300))
                    _shellProcess.Kill(true);
            }
        }
        catch { }
        finally
        {
            _shellProcess?.Dispose();
            _shellProcess = null;
        }
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
        SetTeacherConnectionFieldsLocked(true);
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

                    // Notifica immediata di una nuova verifica Quiz assegnata dal docente.
                    // Non contiene il PDF: serve solo a puntare con certezza al server Quiz
                    // e a forzare subito il controllo dell'assegnazione, senza attendere il timer.
                    if (command.Equals("quizAssigned", StringComparison.OrdinalIgnoreCase) ||
                        command.Equals("quizReady", StringComparison.OrdinalIgnoreCase))
                    {
                        _quizServerBase = NormalizeServerAddress($"{ip}:{port}");
                        _quizSessionCode = session;
                        _lastQuizServerSeenUtc = DateTime.UtcNow;
                        _quizVerificationMode = true;
                        StatusText.Text = $"Nuova verifica Quiz assegnata - ricezione da {ip}:{port}...";
                        _ = CheckQuizAssignmentAsync();
                        return;
                    }

                    // Comando esplicito inviato dal server Verifiche Quiz quando il docente preme "Ferma server".
                    // Va gestito prima del filtro che ignora i broadcast del server normale.
                    if (command.Equals("quizStopped", StringComparison.OrdinalIgnoreCase) ||
                        command.Equals("stopQuiz", StringComparison.OrdinalIgnoreCase))
                    {
                        // Un vecchio datagramma UDP di arresto può arrivare subito dopo l'apertura
                        // di una nuova verifica e chiuderla istantaneamente. Ignoriamo quindi
                        // gli stop arrivati nei primissimi secondi di una nuova assegnazione.
                        if (_activeQuizWindow != null &&
                            (DateTime.UtcNow - _activeQuizOpenedUtc) < TimeSpan.FromSeconds(4))
                        {
                            return;
                        }

                        _activeQuizWindow?.ForceCloseFromServer();
                        _activeQuizWindow = null;
                        _activeQuizOpenedUtc = DateTime.MinValue;
                        _quizServerBase = "";
                        _quizSessionCode = "";
                        _lastQuizServerSeenUtc = DateTime.MinValue;
                        _lastQuizAssignmentId = "";
                        ExitQuizWaitingMode();

                        // Arrestando il server Quiz il client torna completamente
                        // alla normale modalità C++ precedente.
                        StatusText.Text = "Server Verifiche Quiz fermato - modalità normale ripristinata";
                        return;
                    }

                    bool isQuizPacket = mode.Equals("quiz_verifica", StringComparison.OrdinalIgnoreCase) ||
                                        mode.Equals("verifica_quiz", StringComparison.OrdinalIgnoreCase) ||
                                        mode.Equals("quiz", StringComparison.OrdinalIgnoreCase) ||
                                        (root.TryGetProperty("quizVerification", out JsonElement qv) && qv.ValueKind == JsonValueKind.True);

                    if (isQuizPacket)
                    {
                        _quizServerBase = NormalizeServerAddress($"{ip}:{port}");
                        _quizSessionCode = session;
                        _lastQuizServerSeenUtc = DateTime.UtcNow;
                        // Durante il Quiz il collegamento visibile segue il server quiz, ma viene
                        // conservato anche in un canale dedicato che non può essere sovrascritto
                        // dai broadcast del normale server Compiti alunni.
                        ServerBox.Text = $"{ip}:{port}";
                        SetSessionCode(session);
                        SetTeacherConnectionFieldsLocked(true);
                        ApplySessionMode("quiz_verifica");
                        StatusText.Text = $"Verifica Quiz: collegato a {ip}:{port} - attendi verifica";
                    }
                    else
                    {
                        // Se il server Quiz è attivo, un broadcast del server normale non deve
                        // far uscire il client dalla verifica né spostare il polling sulla porta sbagliata.
                        if (_quizVerificationMode && (DateTime.UtcNow - _lastQuizServerSeenUtc) < TimeSpan.FromSeconds(8))
                            return;

                        ServerBox.Text = $"{ip}:{port}";
                        SetSessionCode(session);
                        SetTeacherConnectionFieldsLocked(true);
                        ApplySessionMode(mode);
                        ApplyCompilationPermission(compileAllowed);
                        ApplyHeaderManagementPermission(headerManagementAllowed);
                        ApplyEditorAssistancePermission(editorAssistanceAllowed);
                        StatusText.Text = $"Docente rilevato: {ip}:{port}";
                    }
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


    private void ServerBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // ServerBox resta il valore tecnico usato dal programma (IP:porta).
        // In interfaccia lo mostriamo in due campi separati, entrambi di sola lettura.
        if (ServerIpDisplay == null || ServerPortDisplay == null) return;

        string raw = (ServerBox.Text ?? string.Empty).Trim();
        string ip = raw;
        string port = string.Empty;

        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(7);
        else if (raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(8);

        int slash = raw.IndexOf('/');
        if (slash >= 0) raw = raw.Substring(0, slash);

        int colon = raw.LastIndexOf(':');
        if (colon > 0 && colon < raw.Length - 1)
        {
            ip = raw.Substring(0, colon);
            port = raw.Substring(colon + 1);
        }
        else
        {
            ip = raw;
        }

        ServerIpDisplay.Text = ip;
        ServerPortDisplay.Text = port;
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
        RunButton.IsEnabled = allowed && !_shellVisible;
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

    private const double EditorMinFontSize = 10;
    private const double EditorMaxFontSize = 32;
    private const double EditorZoomStep = 1;

    private void EditorZoomOut_Click(object sender, RoutedEventArgs e)
    {
        SetMainEditorsFontSize(Editor.FontSize - EditorZoomStep);
    }

    private void EditorZoomIn_Click(object sender, RoutedEventArgs e)
    {
        SetMainEditorsFontSize(Editor.FontSize + EditorZoomStep);
    }

    private void SetMainEditorsFontSize(double requestedSize)
    {
        double size = Math.Max(EditorMinFontSize, Math.Min(EditorMaxFontSize, requestedSize));
        Editor.FontSize = size;
        HeaderEditor.FontSize = size;
        StatusText.Text = $"Zoom codice: {size:0} pt";
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
            FontSize = Math.Max(16, Math.Min(34, sourceEditor.FontSize + 5)),
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

        var zoomOutButton = new System.Windows.Controls.Button
        {
            Content = "−",
            Width = 48,
            Height = 40,
            Margin = new Thickness(8),
            Background = new SolidColorBrush(Color.FromRgb(124, 58, 237)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(167, 139, 250)),
            Foreground = Brushes.White,
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            ToolTip = "Riduci la dimensione del codice"
        };

        var zoomInButton = new System.Windows.Controls.Button
        {
            Content = "+",
            Width = 48,
            Height = 40,
            Margin = new Thickness(8),
            Background = new SolidColorBrush(Color.FromRgb(2, 132, 199)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            Foreground = Brushes.White,
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            ToolTip = "Aumenta la dimensione del codice"
        };

        zoomOutButton.Click += (_, _) =>
            popupEditor.FontSize = Math.Max(EditorMinFontSize, popupEditor.FontSize - EditorZoomStep);
        zoomInButton.Click += (_, _) =>
            popupEditor.FontSize = Math.Min(EditorMaxFontSize + 6, popupEditor.FontSize + EditorZoomStep);

        ShowFullscreenOverlay(
            $"{displayName} — Tipologia {GetTaskType()} — Esercizio {GetExerciseNumber()} — C++17",
            popupEditor,
            new[] { zoomOutButton, zoomInButton, applyButton },
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
            "echo Copyright Prof. Alessandro Barazzuol\r\n" +
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

        // Le librerie grafiche (per esempio CV+ Output Window) vengono avviate
        // direttamente, senza file BAT e senza cmd.exe. Il linker -mwindows
        // impedisce inoltre la creazione della console Windows.
        if (UsesGraphicalOutputLibrary())
        {
            var graphicalStartInfo = new ProcessStartInfo(compilation.ExePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(compilation.ExePath) ?? Path.GetTempPath()
            };
            ConfigureCompilerEnvironment(graphicalStartInfo);
            Process.Start(graphicalStartInfo);
            _programOutput = "Esecuzione avviata nella finestra grafica CV+ (CMD disattivato).";
            SaveCurrentExerciseResult(compilation.CompileOutput, _programOutput);
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
        var knownNumericValues = new Dictionary<string, double>(StringComparer.Ordinal);
        string[] lines = sourceCode.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = System.Text.RegularExpressions.Regex.Replace(
                lines[i],
                "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|//.*$",
                string.Empty).Trim();
            int lineNumber = i + 1;

            var declaration = System.Text.RegularExpressions.Regex.Match(
                line,
                @"\b(?:const\s+)?(?:int|long|short|double|float|auto|size_t)\s+([A-Za-z_]\w*)\s*=\s*([-+]?(?:\d+(?:\.\d*)?|\.\d+))\s*;");
            if (declaration.Success &&
                double.TryParse(declaration.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double declaredValue))
            {
                knownNumericValues[declaration.Groups[1].Value] = declaredValue;
            }

            var assignment = System.Text.RegularExpressions.Regex.Match(
                line,
                @"^\s*([A-Za-z_]\w*)\s*=\s*([-+]?(?:\d+(?:\.\d*)?|\.\d+))\s*;");
            if (assignment.Success &&
                double.TryParse(assignment.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double assignedValue))
            {
                knownNumericValues[assignment.Groups[1].Value] = assignedValue;
            }

            foreach (System.Text.RegularExpressions.Match input in
                     System.Text.RegularExpressions.Regex.Matches(line, @"cin\s*>>\s*([A-Za-z_]\w*)"))
            {
                knownNumericValues.Remove(input.Groups[1].Value);
            }

            foreach (System.Text.RegularExpressions.Match operation in
                     System.Text.RegularExpressions.Regex.Matches(line, @"\b([A-Za-z_]\w*)\s*(?:\+\+|--|[+\-*/%]=)"))
            {
                knownNumericValues.Remove(operation.Groups[1].Value);
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(
                    line,
                    @"(?:/|%)\s*[-+]?0(?:\.0+)?(?:\D|$)"))
            {
                findings.Add(
                    $"ERRORE GRAVE - Riga {lineNumber}: divisione o modulo per zero. " +
                    "Il programma può compilare correttamente, ma durante l'esecuzione può terminare in modo anomalo. " +
                    "Usa un divisore diverso da zero oppure controlla il divisore prima dell'operazione.");
            }

            foreach (System.Text.RegularExpressions.Match division in
                     System.Text.RegularExpressions.Regex.Matches(line, @"(?:/|%)\s*([A-Za-z_]\w*)\b"))
            {
                string divisor = division.Groups[1].Value;
                if (knownNumericValues.TryGetValue(divisor, out double value) && Math.Abs(value) < double.Epsilon)
                {
                    findings.Add(
                        $"ERRORE GRAVE - Riga {lineNumber}: divisione o modulo per zero tramite la variabile '{divisor}'. " +
                        $"In questo punto '{divisor}' vale 0. Il codice compila, ma l'operazione può causare un errore durante l'esecuzione. " +
                        $"Assegna a '{divisor}' un valore diverso da zero oppure verifica '{divisor} != 0' prima di eseguire l'operazione.");
                }
            }

            var loopMatch = System.Text.RegularExpressions.Regex.Match(
                line,
                @"for\s*\([^;]*;[^;]*;[^)]*\)");
            if (loopMatch.Success &&
                line.Contains("/ i", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add($"Riga {lineNumber}: controlla che il divisore i non possa valere zero.");
            }

            if (line.Contains("while(true)", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("for(;;)", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add($"Riga {lineNumber}: possibile ciclo infinito.");
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
            TestServerButton.IsEnabled = !_shellVisible;
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
            ApplyShellUiLock();
        }
        catch
        {
            if (body.Contains("verifica", StringComparison.OrdinalIgnoreCase)) mode = "verifica";
        }
        ApplySessionMode(mode);
    }


    private async Task CheckQuizAssignmentAsync()
    {
        if (_quizAssignmentCheckRunning) return;

        // Il Quiz usa un indirizzo dedicato: non dipende più dal ServerBox condiviso
        // con Compiti alunni. Se il discovery Quiz è arrivato, continuiamo a cercare
        // la verifica anche se un altro server trasmette contemporaneamente.
        string baseAddress = _quizServerBase;
        if (string.IsNullOrWhiteSpace(baseAddress))
        {
            if (!_quizVerificationMode || string.IsNullOrWhiteSpace(ServerBox.Text)) return;
            baseAddress = NormalizeServerAddress(ServerBox.Text);
        }
        if (string.IsNullOrWhiteSpace(StudentIdBox.Text) ||
            string.IsNullOrWhiteSpace(StudentNameBox.Text) ||
            string.IsNullOrWhiteSpace(ClassBox.Text))
        {
            await Dispatcher.InvokeAsync(() =>
            {
                StatusText.Text = "Verifica Quiz: compila N° registro, nome e cognome e classe";
                ShowQuizWaitingWindow();
            });
            return;
        }

        _quizAssignmentCheckRunning = true;
        try
        {
            string id = Uri.EscapeDataString(StudentIdBox.Text.Trim());
            string ip = Uri.EscapeDataString(GetLocalIpv4Address());
            string sessionRaw = !string.IsNullOrWhiteSpace(_quizSessionCode) ? _quizSessionCode : SessionBox.Text.Trim();
            string session = Uri.EscapeDataString(sessionRaw);

            // Heartbeat dedicato Verifiche Quiz: mantiene l'alunno nella Vista alunni
            // senza riutilizzare il monitor /live del server normale.
            try
            {
                var live = new
                {
                    studentId = StudentIdBox.Text.Trim(),
                    studentName = StudentNameBox.Text.Trim(),
                    className = ClassBox.Text.Trim(),
                    clientIp = GetLocalIpv4Address(),
                    sessionCode = sessionRaw,
                    isOnline = true
                };
                using var liveContent = new StringContent(JsonSerializer.Serialize(live), Encoding.UTF8, "application/json");
                using var liveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using HttpResponseMessage liveResponse = await _http.PostAsync(baseAddress + "/quiz-live", liveContent, liveTimeout.Token);
                if (liveResponse.IsSuccessStatusCode)
                {
                    _lastQuizServerSeenUtc = DateTime.UtcNow;
                    if (!_quizVerificationMode)
                        await Dispatcher.InvokeAsync(() => ApplySessionMode("quiz_verifica"));
                }
            }
            catch { }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using HttpResponseMessage response = await _http.GetAsync(
                $"{baseAddress}/quiz-assignment?studentId={id}&clientIp={ip}&sessionCode={session}", timeout.Token);
            if (!response.IsSuccessStatusCode) return;
            _lastQuizServerSeenUtc = DateTime.UtcNow;
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("available", out JsonElement available) || available.ValueKind != JsonValueKind.True) return;
            string assignmentId = Get(root, "assignmentId", "");
            if (string.IsNullOrWhiteSpace(assignmentId) || assignmentId == _lastQuizAssignmentId) return;
            string pdfBase64 = Get(root, "pdfBase64", "");
            if (string.IsNullOrWhiteSpace(pdfBase64)) return;
            string type = Get(root, "verificationType", "A");
            int minutes = root.TryGetProperty("durationMinutes", out JsonElement dur) && dur.TryGetInt32(out int parsedMinutes) ? parsedMinutes : 60;
            byte[] pdfBytes = Convert.FromBase64String(pdfBase64);
            string tempFolder = Path.Combine(Path.GetTempPath(), "CVPlus", "VerificheQuiz");
            Directory.CreateDirectory(tempFolder);
            string pdfPath = Path.Combine(tempFolder, $"{assignmentId}.pdf");
            File.WriteAllBytes(pdfPath, pdfBytes);

            await Dispatcher.InvokeAsync(() =>
            {
                _quizVerificationMode = true;
                CloseQuizWaitingWindow();
                StatusText.Text = "Verifica Quiz ricevuta - apertura modulo...";
            });

            try
            {
                var ack = new { assignmentId, studentId = StudentIdBox.Text.Trim(), studentName = StudentNameBox.Text.Trim(), className = ClassBox.Text.Trim(), clientIp = GetLocalIpv4Address() };
                using var ackContent = new StringContent(JsonSerializer.Serialize(ack), Encoding.UTF8, "application/json");
                using var ackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                await _http.PostAsync(baseAddress + "/quiz-received", ackContent, ackTimeout.Token);
            }
            catch { }

            bool opened = false;
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    // Il Quiz è una finestra indipendente: MainWindow resta normale dietro.
                    if (_activeQuizWindow != null)
                    {
                        try { _activeQuizWindow.Activate(); } catch { }
                        opened = true;
                        return;
                    }

                    var quiz = new QuizVerificationWindow(
                        _http, baseAddress, assignmentId, pdfPath, type, minutes,
                        StudentIdBox.Text.Trim(), StudentNameBox.Text.Trim(), ClassBox.Text.Trim(), GetLocalIpv4Address());

                    _activeQuizWindow = quiz;
                    _activeQuizOpenedUtc = DateTime.UtcNow;
                    _modalDialogOpen = true;

                    quiz.Closed += (_, _) =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (ReferenceEquals(_activeQuizWindow, quiz))
                            {
                                _activeQuizWindow = null;
                                _activeQuizOpenedUtc = DateTime.MinValue;
                            }
                            _modalDialogOpen = false;
                            if (_quizVerificationMode)
                                StatusText.Text = "Verifica Quiz terminata";
                        }));
                    };

                    quiz.Show();
                    quiz.WindowState = WindowState.Maximized;
                    quiz.Topmost = true;
                    quiz.Activate();
                    quiz.Focus();
                    opened = true;
                });
            }
            catch (Exception openEx)
            {
                // NON memorizziamo assignmentId: il timer deve riprovare finché il form
                // non viene realmente creato. Rendiamo anche visibile l'errore, invece di
                // inghiottirlo silenziosamente.
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text = "Verifica ricevuta ma apertura non riuscita - nuovo tentativo automatico";
                    OutputBox.Text = "Errore apertura Verifica Quiz: " + openEx.Message;
                });
                return;
            }

            if (opened)
                _lastQuizAssignmentId = assignmentId;
        }
        catch (Exception ex)
        {
            // Manteniamo il polling automatico, ma lasciamo una traccia diagnostica
            // visibile: così un problema di rete/PDF non sembra una mancata assegnazione.
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_quizVerificationMode)
                        StatusText.Text = "Verifiche Quiz: nuovo tentativo di ricezione in corso";
                    OutputBox.Text = "Ricezione Verifica Quiz: " + ex.Message;
                });
            }
            catch { }
        }
        finally { _quizAssignmentCheckRunning = false; }
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
        new("cvplus-grafici3d", "CV+ Grafici 3D", "Estensione didattica predefinita: disegna superfici z=f(x,y) in un grafico 3D interattivo, senza installare componenti esterni.", "Guida_CVPlus_Grafici3D.html", new[]
        {
            ("grafico3d", "cvplus3d::grafico", "cvplus3d::grafico([](double x, double y) { return x * pow(y, 5) + x * log(y) + x * y; }, -2.0, 2.0, 0.25, 1.5, \"z = x*y^5 + x*log(y) + x*y\");", "Disegna una superficie 3D z=f(x,y); richiede #include <cvplus_3d.hpp> e <cmath>"),
            ("grafico3dinclude", "include CV+ Grafici 3D", "#include <cvplus_3d.hpp>\n#include <cmath>", "Include necessari per i grafici 3D")
        })
    };

    private void LoadCppExtensions()
    {
        // L'estensione 3D è integrata e sempre disponibile in esercitazione.
        // Non è possibile installare, rimuovere o caricare estensioni aggiuntive.
        _installedCppExtensions.Clear();
        _installedCppExtensions.Add("cvplus-grafici3d");
        EnsureCvPlus3DHeader();
    }

    private void SaveCppExtensions()
    {
        // Catalogo bloccato: CV+ Grafici 3D rimane sempre installata.
        _installedCppExtensions.Clear();
        _installedCppExtensions.Add("cvplus-grafici3d");
        EnsureCvPlus3DHeader();
    }

    private IEnumerable<(string Trigger, string Display, string Insert, string Description)> GetInstalledExtensionCompletions() =>
        CppExtensionCatalog.SelectMany(x => x.Completions);

    private string CppExtensionsIncludePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CVPlus", "CppExtensions", "include");

    private void EnsureCvPlus3DHeader()
    {
        Directory.CreateDirectory(CppExtensionsIncludePath);
        string headerText = """
#pragma once
#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstdlib>
#include <fstream>
#include <iomanip>
#include <limits>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

// CV+ Grafici 3D
// Copyright (c) Alessandro Barazzuol
// Estensione didattica inclusa in CV+ Compilatore Alunno.
namespace cvplus3d {

inline std::string escape_js(const std::string& s) {
    std::string r; r.reserve(s.size()+16);
    for (char c : s) {
        if (c == '\\' || c == '\"') { r += '\\'; r += c; }
        else if (c == '\n') r += "\\n";
        else if (c != '\r') r += c;
    }
    return r;
}

class Expression {
    std::string src; size_t p = 0; double xv = 0, yv = 0;
    void ws(){ while(p<src.size() && std::isspace((unsigned char)src[p])) ++p; }
    bool eat(char c){ ws(); if(p<src.size() && src[p]==c){++p; return true;} return false; }
    std::string ident(){ ws(); size_t b=p; while(p<src.size() && (std::isalpha((unsigned char)src[p]) || src[p]=='_')) ++p; return src.substr(b,p-b); }
    double number(){ ws(); size_t b=p; bool dot=false; while(p<src.size() && (std::isdigit((unsigned char)src[p]) || src[p]=='.' || src[p]=='e' || src[p]=='E' || ((src[p]=='+'||src[p]=='-') && p>b && (src[p-1]=='e'||src[p-1]=='E')))){ if(src[p]=='.') dot=true; ++p; } if(b==p) throw std::runtime_error("Numero atteso"); return std::stod(src.substr(b,p-b)); }
    double primary(){
        ws();
        if(eat('(')){ double v=expr(); if(!eat(')')) throw std::runtime_error("Parentesi ) mancante"); return v; }
        if(eat('+')) return primary();
        if(eat('-')) return -primary();
        if(p<src.size() && (std::isdigit((unsigned char)src[p]) || src[p]=='.')) return number();
        std::string id=ident();
        if(id=="x" || id=="X") return xv;
        if(id=="y" || id=="Y") return yv;
        if(id=="pi" || id=="PI") return 3.14159265358979323846;
        if(id=="e") return 2.71828182845904523536;
        if(id.empty()) throw std::runtime_error("Espressione non valida");
        if(!eat('(')) throw std::runtime_error("Funzione senza parentesi: "+id);
        double a=expr(); double b=0; bool hasB=false; if(eat(',')){ b=expr(); hasB=true; }
        if(!eat(')')) throw std::runtime_error("Parentesi ) mancante dopo "+id);
        if(id=="sin") return std::sin(a); if(id=="cos") return std::cos(a); if(id=="tan") return std::tan(a);
        if(id=="log" || id=="ln") return std::log(a); if(id=="log10") return std::log10(a);
        if(id=="sqrt") return std::sqrt(a); if(id=="abs") return std::abs(a); if(id=="exp") return std::exp(a);
        if(id=="pow" && hasB) return std::pow(a,b);
        throw std::runtime_error("Funzione non riconosciuta: "+id);
    }
    double power(){ double a=primary(); if(eat('^')) return std::pow(a,power()); return a; }
    double term(){ double a=power(); for(;;){ if(eat('*')) a*=power(); else if(eat('/')) a/=power(); else break; } return a; }
    double expr(){ double a=term(); for(;;){ if(eat('+')) a+=term(); else if(eat('-')) a-=term(); else break; } return a; }
public:
    explicit Expression(std::string e):src(std::move(e)){
        size_t eq=src.find('='); if(eq!=std::string::npos) src=src.substr(eq+1);
    }
    double eval(double x,double y){ xv=x; yv=y; p=0; double v=expr(); ws(); if(p!=src.size()) throw std::runtime_error("Carattere inatteso nella formula"); return v; }
    const std::string& text() const { return src; }
};

inline bool contains_ci(std::string a, std::string b){
    std::transform(a.begin(),a.end(),a.begin(),[](unsigned char c){return (char)std::tolower(c);});
    std::transform(b.begin(),b.end(),b.begin(),[](unsigned char c){return (char)std::tolower(c);});
    return a.find(b)!=std::string::npos;
}

inline void grafico(const std::string& formula,
                    double xmin = std::numeric_limits<double>::quiet_NaN(),
                    double xmax = std::numeric_limits<double>::quiet_NaN(),
                    double ymin = std::numeric_limits<double>::quiet_NaN(),
                    double ymax = std::numeric_limits<double>::quiet_NaN(),
                    int campioni = 52)
{
    Expression ex(formula);
    bool autoX = !std::isfinite(xmin) || !std::isfinite(xmax) || !(xmin < xmax);
    bool autoY = !std::isfinite(ymin) || !std::isfinite(ymax) || !(ymin < ymax);
    if(autoX){ xmin=-3.5; xmax=3.5; if(contains_ci(formula,"log(x") || contains_ci(formula,"ln(x") || contains_ci(formula,"sqrt(x")){ xmin=0.12; xmax=4.5; } }
    if(autoY){ ymin=-3.5; ymax=3.5; if(contains_ci(formula,"log(y") || contains_ci(formula,"ln(y") || contains_ci(formula,"sqrt(y")){ ymin=0.12; ymax=4.5; } }
    campioni=std::max(24,std::min(campioni,90));

    std::vector<double> valori; valori.reserve((size_t)campioni*(size_t)campioni);
    std::vector<double> finiteVals;
    for(int j=0;j<campioni;++j){
        double y=ymin+(ymax-ymin)*j/(campioni-1.0);
        for(int i=0;i<campioni;++i){
            double x=xmin+(xmax-xmin)*i/(campioni-1.0), z;
            try { z=ex.eval(x,y); } catch(...) { z=std::numeric_limits<double>::quiet_NaN(); }
            if(!std::isfinite(z) || std::abs(z)>1e12) z=std::numeric_limits<double>::quiet_NaN();
            valori.push_back(z); if(std::isfinite(z)) finiteVals.push_back(z);
        }
    }
    if(finiteVals.empty()) throw std::runtime_error("La funzione non produce valori reali nell'intervallo scelto");
    std::sort(finiteVals.begin(),finiteVals.end());
    // Vista robusta: taglia solo gli estremi anomali, cosi' la superficie resta leggibile.
    size_t lo=(size_t)(finiteVals.size()*0.02), hi=(size_t)(finiteVals.size()*0.98); if(hi>=finiteVals.size()) hi=finiteVals.size()-1;
    double zmin=finiteVals[lo], zmax=finiteVals[hi];
    if(std::abs(zmax-zmin)<1e-9){ zmin-=1; zmax+=1; }
    double pad=(zmax-zmin)*0.08; zmin-=pad; zmax+=pad;

    std::ofstream out("cvplus_grafico_3d.html",std::ios::binary);
    if(!out) throw std::runtime_error("Impossibile creare cvplus_grafico_3d.html");
    out << R"CVHTML(<!doctype html><html lang='it'><head><meta charset='utf-8'><title>CV+ Grafico 3D</title>
<style>*{box-sizing:border-box}html,body{margin:0;height:100%;background:radial-gradient(circle at 50% 38%,#153153 0,#07111f 52%,#030812 100%);color:#edf7ff;font-family:Segoe UI,Arial,sans-serif;overflow:hidden}#bar{height:76px;display:flex;align-items:center;justify-content:space-between;padding:0 24px;background:rgba(8,22,39,.95);border-bottom:1px solid #315b82;box-shadow:0 4px 18px #0008}#title{font-size:22px;font-weight:750}.hint{font-size:12px;color:#a7c9e8;margin-top:3px}.copy{font-size:12px;color:#72d6ff;text-align:right}canvas{display:block;width:100%;height:calc(100% - 76px);cursor:grab}canvas:active{cursor:grabbing}</style></head><body><div id='bar'><div><div id='title'></div><div class='hint'>Trascina: ruota · rotellina: zoom · doppio clic: vista iniziale</div></div><div class='copy'>CV+ Grafici 3D<br>© Alessandro Barazzuol</div></div><canvas id='c'></canvas><script>
const N=)CVHTML" << campioni << R"CVHTML(,xmin=)CVHTML" << std::setprecision(17) << xmin << R"CVHTML(,xmax=)CVHTML" << xmax << R"CVHTML(,ymin=)CVHTML" << ymin << R"CVHTML(,ymax=)CVHTML" << ymax << R"CVHTML(,zmin=)CVHTML" << zmin << R"CVHTML(,zmax=)CVHTML" << zmax << R"CVHTML(;const Z=[)CVHTML";
    for(size_t k=0;k<valori.size();++k){ if(k)out<<','; if(std::isfinite(valori[k])) out<<std::setprecision(17)<<valori[k]; else out<<"null"; }
    out << R"CVHTML(];document.getElementById('title').textContent=')CVHTML" << escape_js("z = "+ex.text()) << R"CVHTML(';
const c=document.getElementById('c'),ctx=c.getContext('2d');let ax=-.68,ay=.72,zoom=1,drag=false,lx=0,ly=0;
function resize(){c.width=innerWidth*devicePixelRatio;c.height=(innerHeight-76)*devicePixelRatio;draw()} addEventListener('resize',resize);
function P(x,y,z){let X=(x-(xmin+xmax)/2)/((xmax-xmin)/2),Y=(y-(ymin+ymax)/2)/((ymax-ymin)/2),Zz=(z-(zmin+zmax)/2)/((zmax-zmin)/2);Zz=Math.max(-1.35,Math.min(1.35,Zz));let ca=Math.cos(ax),sa=Math.sin(ax),cb=Math.cos(ay),sb=Math.sin(ay);let x1=X*cb-Zz*sb,z1=X*sb+Zz*cb,y1=Y;let y2=y1*ca-z1*sa,z2=y1*sa+z1*ca;let sc=Math.min(c.width,c.height)*.34*zoom;return[c.width/2+x1*sc,c.height/2-y2*sc,z2]}
function seg(a,b,col,w=1){ctx.strokeStyle=col;ctx.lineWidth=w*devicePixelRatio;ctx.beginPath();ctx.moveTo(a[0],a[1]);ctx.lineTo(b[0],b[1]);ctx.stroke()}
function poly(ps,col){ctx.fillStyle=col;ctx.beginPath();ctx.moveTo(ps[0][0],ps[0][1]);for(let i=1;i<ps.length;i++)ctx.lineTo(ps[i][0],ps[i][1]);ctx.closePath();ctx.fill();ctx.strokeStyle='rgba(185,230,255,.15)';ctx.lineWidth=.45*devicePixelRatio;ctx.stroke()}
function label(t,p,col='#eaf7ff'){ctx.fillStyle=col;ctx.font=`bold ${14*devicePixelRatio}px Segoe UI`;ctx.shadowColor='#000';ctx.shadowBlur=4*devicePixelRatio;ctx.fillText(t,p[0]+7*devicePixelRatio,p[1]-7*devicePixelRatio);ctx.shadowBlur=0}
function draw(){ctx.clearRect(0,0,c.width,c.height);let g=ctx.createRadialGradient(c.width*.5,c.height*.4,10,c.width*.5,c.height*.5,Math.max(c.width,c.height)*.7);g.addColorStop(0,'#17385d');g.addColorStop(.55,'#081625');g.addColorStop(1,'#030711');ctx.fillStyle=g;ctx.fillRect(0,0,c.width,c.height);
// piano di riferimento e griglia
for(let q=-4;q<=4;q++){let t=q/4;let xx=(xmin+xmax)/2+t*(xmax-xmin)/2,yy=(ymin+ymax)/2+t*(ymax-ymin)/2;seg(P(xx,ymin,0),P(xx,ymax,0),'rgba(120,170,210,.17)',.7);seg(P(xmin,yy,0),P(xmax,yy,0),'rgba(120,170,210,.17)',.7)}
let faces=[];for(let j=0;j<N-1;j++)for(let i=0;i<N-1;i++){let a=Z[j*N+i],b=Z[j*N+i+1],d=Z[(j+1)*N+i],e=Z[(j+1)*N+i+1];if(a===null||b===null||d===null||e===null)continue;let x1=xmin+(xmax-xmin)*i/(N-1),x2=xmin+(xmax-xmin)*(i+1)/(N-1),y1=ymin+(ymax-ymin)*j/(N-1),y2=ymin+(ymax-ymin)*(j+1)/(N-1);let ps=[P(x1,y1,a),P(x2,y1,b),P(x2,y2,e),P(x1,y2,d)],avg=(a+b+d+e)/4,t=(avg-zmin)/(zmax-zmin);t=Math.max(0,Math.min(1,t));faces.push({ps,d:(ps[0][2]+ps[1][2]+ps[2][2]+ps[3][2])/4,t})}faces.sort((A,B)=>A.d-B.d);for(const f of faces){let h=210-170*f.t,light=42+16*f.t;poly(f.ps,`hsla(${h},88%,${light}%,.88)`)}
let O=P(0,0,0),X=P(xmax,0,0),Y=P(0,ymax,0),ZZ=P(0,0,zmax);seg(P(xmin,0,0),X,'#ff6969',2.8);seg(P(0,ymin,0),Y,'#58e6a9',2.8);seg(P(0,0,zmin),ZZ,'#62b9ff',3.1);label('X',X,'#ff8d8d');label('Y',Y,'#79f1bd');label('Z',ZZ,'#83caff');label('0',O,'#d9eaff');}
c.onmousedown=e=>{drag=true;lx=e.clientX;ly=e.clientY};onmouseup=()=>drag=false;onmousemove=e=>{if(!drag)return;ay+=(e.clientX-lx)*.008;ax+=(e.clientY-ly)*.008;lx=e.clientX;ly=e.clientY;draw()};c.onwheel=e=>{e.preventDefault();zoom*=e.deltaY<0?1.1:.9;zoom=Math.max(.35,Math.min(3.2,zoom));draw()};c.ondblclick=()=>{ax=-.68;ay=.72;zoom=1;draw()};resize();
</script></body></html>)CVHTML";
    out.close();
#ifdef _WIN32
    std::system("start \"\" \"cvplus_grafico_3d.html\"");
#endif
}

} // namespace cvplus3d
""";
        File.WriteAllText(Path.Combine(CppExtensionsIncludePath, "cvplus_3d.hpp"), headerText, new UTF8Encoding(false));
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


    private const string GitLibrariesApiUrl = "https://api.github.com/repos/0-29654/compilatore/contents/librerie?ref=main";
    private const string GitLibrariesPassword = "20242lbg";

    private bool RequestGitLibrariesPassword()
    {
        var dialog = new Window
        {
            Title = "Accesso librerie Git",
            Owner = this,
            Width = 430,
            Height = 245,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            ShowInTaskbar = false
        };

        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock
        {
            Text = "Inserisci la password per accedere alla cartella librerie di GitHub.",
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var password = new PasswordBox
        {
            Height = 34,
            FontSize = 16,
            Padding = new Thickness(7, 4, 7, 4),
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(password);
        panel.Children.Add(new TextBlock
        {
            Text = "Aiuto: 20……..g",
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            Margin = new Thickness(0, 0, 0, 15)
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "ANNULLA", MinWidth = 95, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
        var confirm = new Button { Content = "ACCEDI", MinWidth = 95, Padding = new Thickness(12, 7, 12, 7), IsDefault = true };
        cancel.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
        confirm.Click += (_, _) =>
        {
            if (!string.Equals(password.Password, GitLibrariesPassword, StringComparison.Ordinal))
            {
                MessageBox.Show(dialog, "Password non corretta.", "CV+", MessageBoxButton.OK, MessageBoxImage.Warning);
                password.Clear();
                password.Focus();
                return;
            }
            dialog.DialogResult = true;
            dialog.Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => password.Focus();
        return dialog.ShowDialog() == true;
    }

    private async Task InstallCppLibraryFromGitAsync()
    {
        if (!RequestGitLibrariesPassword()) return;

        try
        {
            StatusText.Text = "Connessione alla cartella librerie GitHub...";
            using var request = new HttpRequestMessage(HttpMethod.Get, GitLibrariesApiUrl);
            request.Headers.UserAgent.ParseAdd("CVPlus-Compilatore/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var libraries = document.RootElement.EnumerateArray()
                .Where(item => item.TryGetProperty("type", out JsonElement type) && type.GetString() == "file")
                .Select(item => new
                {
                    Name = item.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? "" : "",
                    DownloadUrl = item.TryGetProperty("download_url", out JsonElement url) ? url.GetString() ?? "" : ""
                })
                .Where(item => (item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || item.Name.EndsWith(".cvplus", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(item.DownloadUrl))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (libraries.Count == 0)
            {
                MessageBox.Show("Nella cartella GitHub 'librerie' non sono presenti pacchetti .zip o .cvplus installabili.", "CV+", MessageBoxButton.OK, MessageBoxImage.Information);
                StatusText.Text = "Nessuna libreria Git disponibile";
                return;
            }

            var selection = new Window
            {
                Title = "Scegli libreria da GitHub",
                Owner = this,
                Width = 650,
                Height = 430,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                ShowInTaskbar = false
            };
            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = new TextBlock { Text = "Seleziona la libreria da installare", Foreground = Brushes.White, FontSize = 19, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) };
            root.Children.Add(title);
            var list = new ListBox { ItemsSource = libraries.Select(x => x.Name).ToList(), FontSize = 14, Margin = new Thickness(0, 0, 0, 14) };
            if (libraries.Count > 0) list.SelectedIndex = 0;
            Grid.SetRow(list, 1); root.Children.Add(list);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "ANNULLA", MinWidth = 100, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
            var install = new Button { Content = "INSTALLA", MinWidth = 105, Padding = new Thickness(12, 7, 12, 7), IsDefault = true };
            cancel.Click += (_, _) => { selection.DialogResult = false; selection.Close(); };
            install.Click += (_, _) =>
            {
                if (list.SelectedIndex < 0) return;
                selection.DialogResult = true;
                selection.Close();
            };
            actions.Children.Add(cancel); actions.Children.Add(install);
            Grid.SetRow(actions, 2); root.Children.Add(actions);
            selection.Content = root;

            if (selection.ShowDialog() != true || list.SelectedIndex < 0) return;
            var selected = libraries[list.SelectedIndex];

            StatusText.Text = "Download libreria: " + selected.Name;
            string tempDirectory = Path.Combine(Path.GetTempPath(), "CVPlusGitLibrary_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            string packagePath = Path.Combine(tempDirectory, Path.GetFileName(selected.Name));
            try
            {
                using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, selected.DownloadUrl);
                downloadRequest.Headers.UserAgent.ParseAdd("CVPlus-Compilatore/1.0");
                using HttpResponseMessage downloadResponse = await _http.SendAsync(downloadRequest);
                downloadResponse.EnsureSuccessStatusCode();
                await using (FileStream output = File.Create(packagePath))
                    await downloadResponse.Content.CopyToAsync(output);

                InstalledCppLibrary installedLibrary = CppLibraryManager.InstallPackage(packagePath);
                StatusText.Text = $"Libreria installata da GitHub: {installedLibrary.Manifest.Name} {installedLibrary.Manifest.Version}";
                MessageBox.Show($"Libreria installata correttamente da GitHub.\n\n{installedLibrary.Manifest.Name} {installedLibrary.Manifest.Version}\nTipo: {installedLibrary.Manifest.Type}", "CV+", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                try { if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true); } catch { }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Installazione da GitHub non riuscita";
            MessageBox.Show("Impossibile caricare o installare la libreria da GitHub:\n" + ex.Message, "CV+", MessageBoxButton.OK, MessageBoxImage.Error);
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
        if (_verificationMode || _quizVerificationMode)
        {
            StatusText.Text = "Grafico 3D disabilitato in modalità verifica";
            return;
        }

        EnsureCvPlus3DHeader();
        var panel = new StackPanel { Margin = new Thickness(24), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = "CV+ GRAFICO 3D", Foreground = Brushes.White, FontSize = 27, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "© Alessandro Barazzuol", Foreground = new SolidColorBrush(Color.FromRgb(105,195,255)), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,4,0,18) });
        panel.Children.Add(new TextBlock { Text = "Scrivi direttamente una formula z = f(x,y). Gli intervalli e la scala del grafico vengono scelti automaticamente per rendere la superficie ben visibile.", Foreground = new SolidColorBrush(Color.FromRgb(205,220,238)), FontSize = 15, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0,0,0,16) });

        var example = new TextBox
        {
            Text = "#include <cvplus_3d.hpp>\n\nint main()\n{\n    cvplus3d::grafico(\"z = x*y + log(x)*x^2*y^4\");\n    return 0;\n}",
            IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 16,
            Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(18,31,49)), BorderBrush = new SolidColorBrush(Color.FromRgb(47,115,166)), Padding = new Thickness(14), Height = 155,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        panel.Children.Add(example);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,18,0,8) };
        var insert = new Button { Content = "INSERISCI ESEMPIO", Padding = new Thickness(18,10,18,10), Margin = new Thickness(6), Background = new SolidColorBrush(Color.FromRgb(14,120,199)), Foreground = Brushes.White, FontWeight = FontWeights.Bold };
        insert.Click += (_, _) => { Editor.Text = example.Text; EditorTabs.SelectedIndex = 0; SaveCurrentExercise(); CloseActiveOverlay(); StatusText.Text = "Esempio Grafico 3D inserito in main.cpp"; };
        var guide = new Button { Content = "GUIDA", Padding = new Thickness(24,10,24,10), Margin = new Thickness(6), Background = new SolidColorBrush(Color.FromRgb(26,137,90)), Foreground = Brushes.White, FontWeight = FontWeights.Bold };
        guide.Click += (_, _) => OpenPdfGuide(Path.Combine(CppGuidesDirectory, "Guida_CVPlus_Grafici3D.html"));
        buttons.Children.Add(insert); buttons.Children.Add(guide); panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock { Text = "Vista manuale facoltativa: cvplus3d::grafico(\"z = ...\", xmin, xmax, ymin, ymax);", Foreground = new SolidColorBrush(Color.FromRgb(160,185,210)), FontSize = 13, TextAlignment = TextAlignment.Center, Margin = new Thickness(0,6,0,0) });

        ShowFullscreenOverlay("Grafico 3D", panel);
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
        _headerManagementAllowed = allowed;
        AddHeaderButton.IsEnabled = allowed && !_shellVisible;
        RenameHeaderButton.IsEnabled = allowed && !_shellVisible;
        DeleteHeaderButton.IsEnabled = allowed && !_shellVisible;
        ImportHeaderButton.IsEnabled = allowed && !_verificationMode && !_shellVisible;

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

    private void UpdateQuizIdentityBlink()
    {
        if (!_quizVerificationMode)
        {
            _quizIdentityBlinkTimer.Stop();
            return;
        }
        _quizIdentityBlinkOn = !_quizIdentityBlinkOn;
        var bright = new SolidColorBrush(Color.FromRgb(255, 176, 32));
        var normal = new SolidColorBrush(Color.FromRgb(42, 58, 82));
        foreach (var box in new[] { StudentIdBox, StudentNameBox, ClassBox })
        {
            bool missing = string.IsNullOrWhiteSpace(box.Text);
            box.BorderThickness = missing && _quizIdentityBlinkOn ? new Thickness(3) : new Thickness(1);
            box.BorderBrush = missing && _quizIdentityBlinkOn ? bright : normal;
            box.Background = missing && _quizIdentityBlinkOn
                ? new SolidColorBrush(Color.FromRgb(50, 38, 18))
                : new SolidColorBrush(Color.FromRgb(10, 21, 38));
        }
    }

    private void ResetQuizIdentityBlink()
    {
        _quizIdentityBlinkTimer.Stop();
        _quizIdentityBlinkOn = false;
        foreach (var box in new[] { StudentIdBox, StudentNameBox, ClassBox })
        {
            box.BorderThickness = new Thickness(1);
            box.BorderBrush = new SolidColorBrush(Color.FromRgb(42, 58, 82));
            box.Background = new SolidColorBrush(Color.FromRgb(10, 21, 38));
        }
    }

    private void EnterQuizWaitingMode()
    {
        _quizVerificationMode = true;

        if (_shellVisible)
            HideShell();

        // Durante l'attesa della verifica l'alunno può compilare soltanto i dati
        // identificativi richiesti dal docente. Tutto il resto del compilatore è bloccato.
        StudentIdBox.IsEnabled = true;
        StudentNameBox.IsEnabled = true;
        ClassBox.IsEnabled = true;

        TaskTypeBox.IsEnabled = false;
        ExerciseBox.IsEnabled = false;
        PreviousExerciseButton.IsEnabled = false;
        NextExerciseButton.IsEnabled = false;
        EditorZoomOutButton.IsEnabled = false;
        EditorZoomInButton.IsEnabled = false;
        ExerciseListBox.IsEnabled = false;
        RenameExerciseButton.IsEnabled = false;
        DeleteExerciseButton.IsEnabled = false;
        Editor.IsReadOnly = true;
        HeaderEditor.IsReadOnly = true;

        RunButton.IsEnabled = false;
        AddHeaderButton.IsEnabled = false;
        RenameHeaderButton.IsEnabled = false;
        DeleteHeaderButton.IsEnabled = false;
        ImportHeaderButton.IsEnabled = false;
        SendButton.IsEnabled = false;
        GoogleDriveButton.IsEnabled = false;
        TestServerButton.IsEnabled = false;
        UpdateButton.IsEnabled = false;
        GuideButton.IsEnabled = false;
        CppExtensionsButton.IsEnabled = false;
        ShellButton.IsEnabled = false;

        UpdateQuizIdentityBlink();
        _quizIdentityBlinkTimer.Start();
        ShowQuizWaitingWindow();
    }

    private void ExitQuizWaitingMode()
    {
        _quizVerificationMode = false;
        ResetQuizIdentityBlink();
        CloseQuizWaitingWindow();

        TaskTypeBox.IsEnabled = true;
        ExerciseBox.IsEnabled = true;
        EditorZoomOutButton.IsEnabled = true;
        EditorZoomInButton.IsEnabled = true;
        ExerciseListBox.IsEnabled = true;
        RenameExerciseButton.IsEnabled = true;
        DeleteExerciseButton.IsEnabled = true;
        Editor.IsReadOnly = false;
        HeaderEditor.IsReadOnly = false;

        ApplyCompilationPermission(_compilationAllowed);
        ApplyHeaderManagementPermission(_headerManagementAllowed);
        ApplyShellUiLock();
        UpdateButton.IsEnabled = !_verificationMode;
        GuideButton.IsEnabled = !_verificationMode;
        CppExtensionsButton.IsEnabled = !_verificationMode;
        GoogleDriveButton.IsEnabled = !_verificationMode && !_googleDriveOperationRunning;
        TestServerButton.IsEnabled = true;
    }

    private void ShowQuizWaitingWindow()
    {
        if (_quizWaitingWindow != null)
            return;

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = "Attendi verifica",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brushes.White
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Compila N° registro, nome e cognome e classe.\nIl resto del programma rimane bloccato fino alla verifica.",
            Margin = new Thickness(0, 12, 0, 0),
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 210, 232))
        });

        var waiting = new Window
        {
            Title = "Attendi verifica",
            Width = 430,
            Height = 175,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Topmost = true,
            Background = new SolidColorBrush(Color.FromRgb(10, 24, 42)),
            Content = panel,
            ShowInTaskbar = false
        };
        waiting.Closing += (_, e) =>
        {
            if (_quizVerificationMode && !Equals(waiting.Tag, "force-close") && _activeQuizWindow == null)
                e.Cancel = true;
        };
        _quizWaitingWindow = waiting;
        waiting.Show();
    }

    private void CloseQuizWaitingWindow()
    {
        if (_quizWaitingWindow == null) return;
        var window = _quizWaitingWindow;
        _quizWaitingWindow = null;
        try
        {
            window.Tag = "force-close";
            window.Hide();
            window.Close();
        }
        catch { }
    }

    private void ApplySessionMode(string mode)
    {
        bool quiz = mode.Equals("quiz_verifica", StringComparison.OrdinalIgnoreCase) ||
                    mode.Equals("verifica_quiz", StringComparison.OrdinalIgnoreCase) ||
                    mode.Equals("quiz", StringComparison.OrdinalIgnoreCase);

        // IMPORTANTE: Verifiche Quiz e verifica C++ tradizionale sono due modalita'
        // completamente separate. Il Quiz NON deve trasformare MainWindow nella
        // vecchia modalita' verifica fullscreen: il solo QuizVerificationWindow
        // occupa lo schermo quando il PDF viene realmente ricevuto.
        if (quiz)
        {
            EnterQuizWaitingMode();
            StatusText.Text = "Verifica Quiz collegata - attendi verifica";
            return;
        }

        // Se arriva una modalita' del server C++ tradizionale, non cancelliamo il
        // canale Quiz dedicato: i due server possono convivere.
        bool verify = mode.Equals("verifica", StringComparison.OrdinalIgnoreCase) ||
                      mode.Equals("test", StringComparison.OrdinalIgnoreCase);
        bool verificationChanged = verify != _verificationMode;
        if (!verificationChanged) return;

        _verificationMode = verify;
        if (verify)
        {
            EnterVerificationMode();
            StatusText.Text = "Modalità verifica attiva";
        }
        else
        {
            ExitVerificationMode();
        }
    }

    private void EnterVerificationMode()
    {
        if (_shellVisible)
            HideShell();

        SaveCurrentExercise();

        // Ogni nuova verifica tradizionale deve partire sempre dall'esercizio 1.
        // Nella modalità Verifiche Quiz l'interfaccia PDF sostituisce invece gli esercizi C++.
        if (!_quizVerificationMode)
        {
            ExerciseBox.Text = "1";
            ActivateExercise(GetTaskType(), 1);
        }

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
        ShellButton.IsEnabled = false;
        ShellButton.ToolTip = "Shell disabilitata in modalità verifica.";
        ImportHeaderButton.IsEnabled = false;
        ImportHeaderButton.ToolTip = "Importazione header disabilitata in modalità verifica.";
        GoogleDriveButton.IsEnabled = false;
        GoogleDriveButton.ToolTip = "Google Drive è disabilitato in modalità verifica.";
        GuideButton.ToolTip = "La guida è disponibile soltanto in modalità esercitazione.";
        ApplyShellUiLock();
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
        CppExtensionsButton.ToolTip = "Apri l’estensione didattica predefinita per grafici 3D.";
        ShellButton.IsEnabled = true;
        ShellButton.ToolTip = "Apri la shell CMD nella cartella Documenti con G++ già nel PATH";
        ImportHeaderButton.IsEnabled = AddHeaderButton.IsEnabled;
        ImportHeaderButton.ToolTip = AddHeaderButton.IsEnabled ? "Importa un file header locale nell'editor C++." : "Gestione dei file header disabilitata dal docente.";
        GoogleDriveButton.IsEnabled = true;
        GoogleDriveButton.ToolTip = "Salva l'esercizio nel tuo Google Drive";
        ApplyShellUiLock();
        GuideButton.ToolTip = "Apri la guida visuale del compilatore";
        Activate();
    }

    private async void CheckUpdates_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CheckUpdatesAsync(silentWhenCurrent: false, automaticCheck: false);
    }

    private async Task CheckUpdatesAsync(bool silentWhenCurrent, bool automaticCheck)
    {
        if (_verificationMode)
        {
            if (!automaticCheck)
            {
                ShowVerificationSafeMessage(
                    "La ricerca degli aggiornamenti è disponibile soltanto in modalità esercitazione.",
                    "Aggiornamenti non disponibili",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    MessageBoxResult.OK
                );
            }
            return;
        }

        UpdateButton.IsEnabled = false;
        StatusText.Text = "Ricerca aggiornamenti...";

        try
        {
            // NON usiamo l'API REST di GitHub per il controllo aggiornamenti.
            // L'endpoint api.github.com ha un limite anonimo piuttosto basso e,
            // soprattutto nelle reti scolastiche dove molti PC condividono lo stesso IP,
            // può rispondere 403 "rate limit exceeded" anche se il programma funziona.
            // La pagina pubblica /releases/latest non consuma quel limite: GitHub la
            // reindirizza direttamente alla Release più recente e dal relativo URL
            // ricaviamo il tag/versione.
            Version runningVersion =
                Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 9, 0);

            using var latestRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "https://github.com/0-29654/compilatore/releases/latest"
            );

            latestRequest.Headers.UserAgent.ParseAdd(
                $"CVPlusCompilatoreAlunno/{runningVersion.Major}.{runningVersion.Minor}.{runningVersion.Build}"
            );

            using HttpResponseMessage latestResponse =
                await _http.SendAsync(latestRequest, HttpCompletionOption.ResponseHeadersRead);

            latestResponse.EnsureSuccessStatusCode();

            Uri? finalReleaseUri = latestResponse.RequestMessage?.RequestUri;
            string finalReleaseUrl = finalReleaseUri?.AbsoluteUri ?? "";

            Match tagMatch = Regex.Match(
                finalReleaseUrl,
                @"/releases/tag/([^/?#]+)",
                RegexOptions.IgnoreCase
            );

            if (!tagMatch.Success)
            {
                throw new InvalidOperationException(
                    "GitHub non ha restituito il collegamento alla Release più recente. Riprova tra qualche istante."
                );
            }

            string tag = Uri.UnescapeDataString(tagMatch.Groups[1].Value);

            Version currentVersion =
                Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 9, 0);

            Version? latestVersion = ExtractVersionFromTag(tag);
            if (latestVersion == null)
            {
                throw new InvalidOperationException(
                    $"La Release più recente ha un numero di versione non riconoscibile ({tag})."
                );
            }

            string updateStateDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CVPlus");

            string installedTagMarker = Path.Combine(
                updateStateDirectory,
                "installed-release-tag.txt");

            string installedTag = File.Exists(installedTagMarker)
                ? File.ReadAllText(installedTagMarker).Trim()
                : "";

            bool newerSemanticVersion = latestVersion > currentVersion;

            // Se l'eseguibile ha già la stessa versione, non forziamo un nuovo download.
            // Il marker del tag serve soltanto a ricordare quale Release è stata installata
            // dal meccanismo automatico, senza dover interrogare l'API GitHub.
            if (!newerSemanticVersion)
            {
                StatusText.Text = "Il programma è aggiornato";

                if (!silentWhenCurrent)
                {
                    ShowVerificationSafeMessage(
                        $"La versione installata ({currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}) corrisponde all'ultima Release pubblicata ({tag}).",
                        "Nessun aggiornamento",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information,
                        MessageBoxResult.OK
                    );
                }

                return;
            }

            // Il workflow pubblica sempre l'installer con questo nome.
            // Usiamo il link pubblico della Release, non api.github.com.
            string downloadUrl =
                "https://github.com/0-29654/compilatore/releases/download/" +
                Uri.EscapeDataString(tag) +
                "/CppStudentClient_Setup.exe";

            MessageBoxResult answer =
                ShowVerificationSafeMessage(
                    $"È disponibile una Release più recente ({tag}).\n\n" +
                    "Vuoi installarla adesso? Dopo il download CV+ verrà chiuso e comparirà soltanto la barra di avanzamento dell'aggiornamento.",
                    "Aggiornamento disponibile",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes
                );

            if (answer != MessageBoxResult.Yes)
            {
                StatusText.Text = "Aggiornamento annullato";
                return;
            }

            StatusText.Text = "Download aggiornamento...";

            string installerPath = Path.Combine(
                Path.GetTempPath(),
                "CppStudentClient_Update_" + latestVersion + ".exe"
            );

            using (HttpRequestMessage downloadRequest = new(HttpMethod.Get, downloadUrl))
            {
                downloadRequest.Headers.UserAgent.ParseAdd(
                    $"CVPlusCompilatoreAlunno/{runningVersion.Major}.{runningVersion.Minor}.{runningVersion.Build}"
                );

                using HttpResponseMessage download =
                    await _http.SendAsync(downloadRequest, HttpCompletionOption.ResponseHeadersRead);

                if (download.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new InvalidOperationException(
                        "La Release è stata trovata, ma l'installer CppStudentClient_Setup.exe non è ancora disponibile. Riprova tra qualche minuto."
                    );
                }

                download.EnsureSuccessStatusCode();

                await using Stream source = await download.Content.ReadAsStreamAsync();
                await using FileStream destination = new(
                    installerPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None
                );

                await source.CopyToAsync(destination);
                await destination.FlushAsync();
            }

            if (!File.Exists(installerPath) ||
                new FileInfo(installerPath).Length < 1024 * 1024)
            {
                throw new InvalidOperationException(
                    "Il file di aggiornamento scaricato è incompleto."
                );
            }

            StatusText.Text = "Preparazione aggiornamento...";

            Directory.CreateDirectory(updateStateDirectory);

            string updaterScript = Path.Combine(
                Path.GetTempPath(),
                "CVPlus_Aggiorna_" + Guid.NewGuid().ToString("N") + ".cmd"
            );

            int currentProcessId = Environment.ProcessId;
            string escapedTag = tag.Replace("%", "%%").Replace("\"", "");

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
                "/SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE " +
                "/CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /RESTARTAPPLICATIONS\r\n" +
                "set \"SETUP_EXIT=%ERRORLEVEL%\"\r\n" +
                "if %SETUP_EXIT% EQU 0 (\r\n" +
                $"  >\"{installedTagMarker}\" echo {escapedTag}\r\n" +
                ")\r\n" +
                "del /f /q \"%INSTALLER%\" >NUL 2>&1\r\n" +
                "del /f /q \"%~f0\" >NUL 2>&1\r\n" +
                "exit /b %SETUP_EXIT%\r\n";

            File.WriteAllText(updaterScript, script, Encoding.Default);

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
        catch (HttpRequestException ex)
        {
            StatusText.Text = "Ricerca aggiornamenti non riuscita";

            if (!automaticCheck)
            {
                string detail = ex.StatusCode.HasValue
                    ? $"GitHub ha risposto con codice {(int)ex.StatusCode.Value} ({ex.StatusCode.Value})."
                    : "Non è stato possibile raggiungere GitHub.";

                ShowVerificationSafeMessage(
                    "Non è stato possibile verificare o scaricare l'aggiornamento.\n\n" +
                    detail + "\nControlla la connessione Internet e riprova.",
                    "Errore aggiornamenti",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning,
                    MessageBoxResult.OK
                );
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ricerca aggiornamenti non riuscita";

            if (!automaticCheck)
            {
                ShowVerificationSafeMessage(
                    "Non è stato possibile verificare o scaricare l'aggiornamento.\n\n" +
                    ex.Message,
                    "Errore aggiornamenti",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning,
                    MessageBoxResult.OK
                );
            }
        }
        finally
        {
            if (IsVisible)
                UpdateButton.IsEnabled = !_shellVisible && !_verificationMode;
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
        // La tipologia può arrivare dal server; se il campo non è ancora valorizzato
        // GetTaskType() usa correttamente il valore predefinito A. Non deve quindi
        // bloccare l'invio quando registro, nome e numero esercizio sono presenti.
        if (string.IsNullOrWhiteSpace(StudentIdBox.Text) || string.IsNullOrWhiteSpace(StudentNameBox.Text) || string.IsNullOrWhiteSpace(ExerciseBox.Text))
        {
            ShowVerificationSafeMessage(
                "Compila N° registro, nome e cognome e N° esercizio.",
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

    private static string HeaderGuardFromFileName(string? fileName)
    {
        string normalized = NormalizeHeaderFileName(fileName);
        string guard = System.Text.RegularExpressions.Regex.Replace(
            normalized.ToUpperInvariant(),
            @"[^A-Z0-9_]",
            "_"
        );
        if (string.IsNullOrWhiteSpace(guard)) guard = "ESERCIZIO_H";
        if (char.IsDigit(guard[0])) guard = "_" + guard;
        return guard;
    }

    private static string RenameHeaderGuard(string headerCode, string oldFileName, string newFileName)
    {
        if (string.IsNullOrWhiteSpace(headerCode)) return headerCode;

        string oldGuard = HeaderGuardFromFileName(oldFileName);
        string newGuard = HeaderGuardFromFileName(newFileName);
        if (oldGuard.Equals(newGuard, StringComparison.Ordinal)) return headerCode;

        // Cambia soltanto il simbolo della guardia del file, senza toccare
        // eventuali altre occorrenze casuali nel codice.
        string result = System.Text.RegularExpressions.Regex.Replace(
            headerCode,
            @"(?m)^(\s*#ifndef\s+)" + System.Text.RegularExpressions.Regex.Escape(oldGuard) + @"(\s*)$",
            "$1" + newGuard + "$2"
        );
        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            @"(?m)^(\s*#define\s+)" + System.Text.RegularExpressions.Regex.Escape(oldGuard) + @"(\s*)$",
            "$1" + newGuard + "$2"
        );
        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            @"(?m)^(\s*#endif\s*//\s*)" + System.Text.RegularExpressions.Regex.Escape(oldGuard) + @"(\s*)$",
            "$1" + newGuard + "$2"
        );
        return result;
    }

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

            string renamedHeaderCode = RenameHeaderGuard(
                HeaderEditor.Text,
                oldName,
                newName
            );
            HeaderEditor.Text = renamedHeaderCode;
            state.HeaderFileName = newName;
            state.HeaderCode = renamedHeaderCode;
            state.IsEmptySlot = false;
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
        state.IsEmptySlot = false;

        HeaderEditor.Text = "";
        HeaderTab.Header = "esercizio.h";
        HeaderTab.Visibility = Visibility.Collapsed;
        Editor.Text = DefaultCode;
        EditorTabs.SelectedIndex = 0;

        SaveCurrentExercise();
        StatusText.Text =
            $"{headerName} eliminato; main.cpp ripristinato";
    }

    private void HeaderEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_loadingExercise || string.IsNullOrWhiteSpace(_activeKey))
            return;

        if (_exerciseStates.TryGetValue(_activeKey, out ExerciseState? state))
        {
            bool wasEmpty = state.IsEmptySlot;
            state.HeaderCode = HeaderEditor.Text;
            if (!string.IsNullOrWhiteSpace(HeaderEditor.Text)) state.IsEmptySlot = false;
            if (wasEmpty != state.IsEmptySlot) RefreshExerciseList();
        }
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
        RefreshExerciseList();

    }

    private void TaskIdentity_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveCurrentExercise();
        ActivateExercise(GetTaskType(), GetExerciseNumber());
        SaveSettings();
        RefreshExerciseList();
    }

    private int GetActiveExerciseNumber()
    {
        if (!string.IsNullOrWhiteSpace(_activeKey))
        {
            int separator = _activeKey.LastIndexOf('|');
            if (separator >= 0 && separator < _activeKey.Length - 1 &&
                int.TryParse(_activeKey[(separator + 1)..], out int activeNumber) && activeNumber > 0)
                return activeNumber;
        }
        return GetExerciseNumber();
    }

    private void ExerciseNumber_LostFocus(object sender, RoutedEventArgs e)
    {
        int oldNumber = GetActiveExerciseNumber();
        string entered = ExerciseBox.Text.Trim();
        if (!int.TryParse(entered, out int newNumber) || newNumber <= 0)
        {
            MessageBox.Show(this, "Il numero dell'esercizio deve essere un intero maggiore di zero.",
                "Numero esercizio", MessageBoxButton.OK, MessageBoxImage.Information);
            ExerciseBox.Text = oldNumber.ToString();
            return;
        }

        if (newNumber == oldNumber)
        {
            ExerciseBox.Text = oldNumber.ToString();
            SaveSettings();
            return;
        }

        RenameExerciseNumber(oldNumber, newNumber, showConfirmation: true);
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
        Editor.Text = state.IsEmptySlot ? "" : (string.IsNullOrWhiteSpace(state.Code) ? DefaultCode : state.Code);
        HeaderEditor.Text = state.IsEmptySlot ? "" : (state.HeaderCode ?? "");
        HeaderTab.Header = string.IsNullOrWhiteSpace(state.HeaderFileName)
            ? "esercizio.h"
            : state.HeaderFileName;
        HeaderTab.Visibility = string.IsNullOrWhiteSpace(state.HeaderCode)
            ? Visibility.Collapsed
            : Visibility.Visible;
        _loadingExercise = false;
        _activeStartedUtc = DateTime.UtcNow;
        StatusText.Text = state.IsEmptySlot
            ? $"Tipologia {type} - esercizio {number} (vuoto)"
            : $"Tipologia {type} - esercizio {number}";
        UpdateExerciseClock();
        RefreshExerciseList();
    }

    private void SaveCurrentExercise()
    {
        if (string.IsNullOrWhiteSpace(_activeKey)) return;
        if (!_exerciseStates.TryGetValue(_activeKey, out ExerciseState? state)) state = _exerciseStates[_activeKey] = new ExerciseState();
        state.Code = Editor.Text;
        state.HeaderCode = HeaderEditor.Text;
        state.IsEmptySlot = string.IsNullOrWhiteSpace(Editor.Text) && string.IsNullOrWhiteSpace(HeaderEditor.Text);
        if (string.IsNullOrWhiteSpace(state.HeaderFileName))
            state.HeaderFileName = "esercizio.h";
        state.Elapsed += DateTime.UtcNow - _activeStartedUtc;
        _activeStartedUtc = DateTime.UtcNow;
        SaveExerciseStates();
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_loadingExercise || string.IsNullOrWhiteSpace(_activeKey)) return;
        if (_exerciseStates.TryGetValue(_activeKey, out ExerciseState? state))
        {
            bool wasEmpty = state.IsEmptySlot;
            state.Code = Editor.Text;
            if (!string.IsNullOrWhiteSpace(Editor.Text)) state.IsEmptySlot = false;
            if (wasEmpty != state.IsEmptySlot) RefreshExerciseList();
        }
    }

    private void RefreshExerciseList()
    {
        if (ExerciseListBox == null) return;
        string type = GetTaskType();
        string prefix = $"{SessionBox.Text.Trim().ToUpperInvariant()}|{type.Trim().ToUpperInvariant()}|";
        int current = GetExerciseNumber();
        int max = current;

        foreach (string key in _exerciseStates.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string numberText = key.Substring(prefix.Length);
            if (int.TryParse(numberText, out int n) && n > max) max = n;
        }

        var items = new List<ExerciseListItem>();
        foreach (var pair in _exerciseStates)
        {
            if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string numberText = pair.Key.Substring(prefix.Length);
            if (!int.TryParse(numberText, out int n)) continue;
            if (pair.Value == null || pair.Value.IsEmptySlot) continue;
            items.Add(new ExerciseListItem { Number = n, Label = $"Esercizio {n}" });
        }
        items = items.OrderBy(i => i.Number).ToList();

        _refreshingExerciseList = true;
        ExerciseListBox.ItemsSource = items;
        ExerciseListBox.SelectedItem = items.FirstOrDefault(i => i.Number == current);
        if (ExerciseListBox.SelectedItem != null) ExerciseListBox.ScrollIntoView(ExerciseListBox.SelectedItem);
        _refreshingExerciseList = false;
    }

    private void ExerciseListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingExerciseList || ExerciseListBox.SelectedItem is not ExerciseListItem item) return;
        int current = GetExerciseNumber();
        if (item.Number == current) return;

        SaveCurrentExercise();
        ExerciseBox.Text = item.Number.ToString();
        ActivateExercise(GetTaskType(), item.Number);
        SaveSettings();
    }

    private void RenameSelectedExercise_Click(object sender, RoutedEventArgs e)
    {
        if (ExerciseListBox.SelectedItem is not ExerciseListItem item)
        {
            MessageBox.Show(this, "Seleziona prima un esercizio nell'elenco.", "Rinomina esercizio",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int? requestedNumber = ShowExerciseNumberPrompt(item.Number);
        if (requestedNumber == null) return;
        RenameExerciseNumber(item.Number, requestedNumber.Value, showConfirmation: true);
    }

    private int? ShowExerciseNumberPrompt(int currentNumber)
    {
        var dialog = new Window
        {
            Title = "Rinomina esercizio",
            Owner = this,
            Width = 390,
            Height = 205,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(11, 23, 41)),
            ShowInTaskbar = false
        };

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Nuovo numero per Esercizio {currentNumber}:",
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Il numero deve essere positivo e non può essere già usato da un altro esercizio.",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 180, 207)),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var input = new TextBox { Text = currentNumber.ToString(), MinWidth = 120 };
        input.SelectAll();
        panel.Children.Add(input);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "Rinomina", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Annulla", Width = 90, IsCancel = true };
        int? result = null;
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(input.Text.Trim(), out int n) || n <= 0)
            {
                MessageBox.Show(dialog, "Inserisci un numero intero maggiore di zero.", "Numero non valido",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                input.Focus();
                input.SelectAll();
                return;
            }
            result = n;
            dialog.DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => input.Focus();
        dialog.ShowDialog();
        return result;
    }

    private bool RenameExerciseNumber(int oldNumber, int newNumber, bool showConfirmation)
    {
        if (oldNumber <= 0 || newNumber <= 0) return false;
        if (oldNumber == newNumber)
        {
            ExerciseBox.Text = newNumber.ToString();
            RefreshExerciseList();
            return true;
        }

        string type = GetTaskType();
        string oldKey = BuildExerciseKey(type, oldNumber);
        string newKey = BuildExerciseKey(type, newNumber);

        // Salva prima il contenuto corrente usando _activeKey, così la modifica del TextBox
        // non crea accidentalmente un secondo esercizio con lo stesso contenuto.
        if (_activeKey.Equals(oldKey, StringComparison.OrdinalIgnoreCase))
            SaveCurrentExercise();

        if (!_exerciseStates.TryGetValue(oldKey, out ExerciseState? sourceState))
        {
            sourceState = new ExerciseState
            {
                Code = "",
                HeaderCode = "",
                HeaderFileName = "esercizio.h",
                Elapsed = TimeSpan.Zero,
                CompileOutput = "",
                ProgramOutput = "",
                IsEmptySlot = true
            };
        }

        if (_exerciseStates.TryGetValue(newKey, out ExerciseState? destinationState) &&
            destinationState != null && !destinationState.IsEmptySlot)
        {
            MessageBox.Show(this,
                $"Esiste già un Esercizio {newNumber} con del contenuto.\n\n" +
                "Per evitare esercizi con numeri duplicati, scegli un numero libero oppure elimina prima il contenuto dell'esercizio di destinazione.",
                "Numero esercizio già utilizzato", MessageBoxButton.OK, MessageBoxImage.Warning);
            ExerciseBox.Text = oldNumber.ToString();
            RefreshExerciseList();
            return false;
        }

        if (showConfirmation)
        {
            MessageBoxResult answer = MessageBox.Show(this,
                $"Vuoi cambiare il numero da Esercizio {oldNumber} a Esercizio {newNumber}?\n\n" +
                $"La posizione {oldNumber} resterà vuota e l'esercizio verrà spostato nella posizione {newNumber}.",
                "Rinomina esercizio", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                ExerciseBox.Text = oldNumber.ToString();
                RefreshExerciseList();
                return false;
            }
        }

        _exerciseStates[newKey] = sourceState;
        _exerciseStates.Remove(oldKey);

        _activeKey = newKey;
        ExerciseBox.Text = newNumber.ToString();
        _loadingExercise = true;
        Editor.Text = sourceState.IsEmptySlot ? "" : (string.IsNullOrWhiteSpace(sourceState.Code) ? DefaultCode : sourceState.Code);
        HeaderEditor.Text = sourceState.IsEmptySlot ? "" : (sourceState.HeaderCode ?? "");
        HeaderTab.Header = string.IsNullOrWhiteSpace(sourceState.HeaderFileName) ? "esercizio.h" : sourceState.HeaderFileName;
        HeaderTab.Visibility = string.IsNullOrWhiteSpace(sourceState.HeaderCode) ? Visibility.Collapsed : Visibility.Visible;
        _loadingExercise = false;
        _activeStartedUtc = DateTime.UtcNow;

        SaveExerciseStates();
        SaveSettings();
        RefreshExerciseList();
        StatusText.Text = sourceState.IsEmptySlot
            ? $"Esercizio {oldNumber} rinominato in {newNumber} (vuoto)"
            : $"Esercizio {oldNumber} rinominato in {newNumber}";
        return true;
    }

    private void DeleteSelectedExercise_Click(object sender, RoutedEventArgs e)
    {
        if (ExerciseListBox.SelectedItem is not ExerciseListItem item)
        {
            MessageBox.Show(this, "Seleziona prima un esercizio nell'elenco.", "Elimina esercizio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBoxResult answer = MessageBox.Show(this,
            $"Vuoi eliminare definitivamente Esercizio {item.Number}?\n\n" +
            "L'esercizio verrà rimosso dall'elenco. Gli altri esercizi manterranno il loro numero e non verranno rinumerati.",
            "Conferma eliminazione esercizio", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        string type = GetTaskType();
        string key = BuildExerciseKey(type, item.Number);
        _exerciseStates.Remove(key);

        // Cerca un altro esercizio realmente esistente senza rinumerare nulla.
        string prefix = $"{SessionBox.Text.Trim().ToUpperInvariant()}|{type.Trim().ToUpperInvariant()}|";
        var remaining = _exerciseStates
            .Where(p => p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Value != null && !p.Value.IsEmptySlot)
            .Select(p => int.TryParse(p.Key.Substring(prefix.Length), out int n) ? n : -1)
            .Where(n => n > 0)
            .OrderBy(n => n)
            .ToList();

        int? next = remaining.FirstOrDefault(n => n > item.Number);
        if (next == 0) next = remaining.LastOrDefault(n => n < item.Number);
        if (next == 0) next = null;

        _loadingExercise = true;
        if (next.HasValue)
        {
            ExerciseBox.Text = next.Value.ToString();
            _loadingExercise = false;
            ActivateExercise(type, next.Value);
        }
        else
        {
            // Nessun esercizio rimasto: prepara una nuova posizione senza crearla nell'elenco.
            ExerciseBox.Text = "1";
            Editor.Text = "";
            HeaderEditor.Text = "";
            HeaderTab.Visibility = Visibility.Collapsed;
            OutputBox.Text = "";
            _loadingExercise = false;
            _activeKey = BuildExerciseKey(type, 1);
            _activeStartedUtc = DateTime.UtcNow;
        }

        SaveExerciseStates();
        SaveSettings();
        RefreshExerciseList();
        StatusText.Text = $"Esercizio {item.Number} eliminato definitivamente";
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
        AddGuideItem(content, "Shell", "#166534", "Apre una shell CMD reale integrata nella cartella Documenti. G++ è già disponibile nel PATH; puoi usare i normali comandi Windows/DOS, help, creare cartelle e file, aprire Notepad e compilare sorgenti C++ e header.");
        AddGuideItem(content, "Estensioni C++", "#0F766E", "Apre CV+ Grafici 3D, l’estensione didattica preinstallata per disegnare superfici z=f(x,y). Le estensioni esterne non possono essere caricate, installate o rimosse.");

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
            GoogleDriveButton.IsEnabled = !_shellVisible;
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
            GoogleDriveButton.IsEnabled = !_verificationMode && !_shellVisible;
            if (GoogleDriveButton.Content?.ToString()?.Contains("✓") != true)
                GoogleDriveButton.Content = "Google Drive";
        }
    }

    private int GetExerciseNumber() => int.TryParse(ExerciseBox.Text.Trim(), out int n) && n > 0 ? n : 1;
    private string BuildExerciseKey(string type, int number) => $"{SessionBox.Text.Trim().ToUpperInvariant()}|{type.Trim().ToUpperInvariant()}|{number}";

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // La finestra intercetta per prima le frecce quando la Shell è visibile.
        // In questo modo la cronologia funziona anche se WPF sposta momentaneamente
        // il focus o un controllo interno consuma KeyDown/PreviewKeyDown.
        if (_shellVisible)
        {
            Key shellKey = e.Key == Key.System ? e.SystemKey : e.Key;
            if (shellKey == Key.Up || shellKey == Key.Down)
            {
                if (HandleShellHistoryKey(shellKey))
                {
                    e.Handled = true;
                    return;
                }

                // Anche senza cronologia, evita che le frecce spostino il focus.
                e.Handled = true;
                ShellInputBox.Focus();
                return;
            }
        }

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

    private static bool UsesGraphicalOutputLibrary()
    {
        try
        {
            return CppLibraryManager.LoadInstalled().Any(library =>
                library.Manifest.Id.Equals("cvplus-output-window", StringComparison.OrdinalIgnoreCase) ||
                library.Manifest.LinkerOptions.Any(option =>
                    option.Contains("-mwindows", StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            return false;
        }
    }

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
        public bool IsEmptySlot { get; set; }
    }

    public sealed class ExerciseListItem
    {
        public int Number { get; set; }
        public string Label { get; set; } = "";
    }
}
