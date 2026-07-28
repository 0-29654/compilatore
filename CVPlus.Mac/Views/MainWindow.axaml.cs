using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using CVPlus.Mac.Models;
using CVPlus.Mac.Services;

namespace CVPlus.Mac.Views;

public partial class MainWindow : Window
{
    private readonly CompilerService _compiler = new();
    private readonly TeacherDiscoveryService _discovery = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly CancellationTokenSource _lifetime = new();
    private string _headerName = "esercizio.h";

    public MainWindow()
    {
        InitializeComponent();
        Editor.Text = "#include <iostream>\nusing namespace std;\n\nint main() {\n    cout << \"Hello World\" << endl;\n    return 0;\n}\n";
        HeaderEditor.Text = "#pragma once\n\n";
        ConfigureCodeEditor(Editor);
        ConfigureCodeEditor(HeaderEditor);
        ApplyHeaderPermission(false);
        Editor.PointerPressed += Editor_PointerPressed;
        _discovery.ServerDiscovered += OnServerDiscovered;
        Opened += async (_, _) => await _discovery.StartAsync(_lifetime.Token);
        Closing += (_, _) => _lifetime.Cancel();
    }

    private static void ConfigureCodeEditor(TextEditor editor)
    {
        editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C++");
        editor.ShowLineNumbers = true;
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 4;
        editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.Parse("#EEF6FF"));
        editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.Parse("#C7DDF5")));
    }

    private void OnServerDiscovered(ServerState s) => Dispatcher.UIThread.Post(() =>
    {
        ServerBox.Text = $"{s.Ip}:{s.Port}"; SessionBox.Text = s.SessionCode;
        ServerBox.IsReadOnly = true; SessionBox.IsReadOnly = true;
        ModeText.Text = s.Mode.Equals("verifica", StringComparison.OrdinalIgnoreCase) ? "VERIFICA" : "ESERCITAZIONE";
        ApplyHeaderPermission(s.HeaderManagementAllowed);
        StatusText.Text = $"Docente rilevato: {s.Ip}:{s.Port}";
    });

    private void ApplyHeaderPermission(bool allowed)
    {
        AddHeaderButton.IsEnabled = allowed; RenameHeaderButton.IsEnabled = allowed; DeleteHeaderButton.IsEnabled = allowed;
        HeaderLockText.IsVisible = !allowed; HeaderEditor.IsReadOnly = !allowed;
    }

    private async void Compile_Click(object? sender, RoutedEventArgs e)
    {
        CompileButton.IsEnabled = false;
        WriteGreen("CV+ COMPILATORE ALUNNO — macOS ARM64\nCompilatore: Apple clang++ / C++17\n----------------------------------------\n");
        CompileResult c = await _compiler.CompileAsync(Editor.Text ?? "", HeaderEditor.Text ?? "", _headerName, _lifetime.Token);
        if (!c.Success) { WriteWhite(c.CompilerOutput + "\n"); CompileButton.IsEnabled = true; return; }
        WriteGreen("Compilazione completata. Output del programma:\n");
        RunResult r = await _compiler.RunAsync(c.ExecutablePath!, "", _lifetime.Token);
        WriteWhite(r.StandardOutput);
        if (!string.IsNullOrWhiteSpace(r.StandardError)) WriteWhite(r.StandardError);
        WriteGreen($"\n----------------------------------------\nProcesso terminato con codice {r.ExitCode}.\n");
        CompileButton.IsEnabled = true;
    }

    private async void Send_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ServerBox.Text)) { StatusText.Text = "Server docente non disponibile."; return; }
        try
        {
            var payload = new { registerNumber = RegisterBox.Text, studentName = NameBox.Text, studentClass = ClassBox.Text, exerciseId = (int)(ExerciseBox.Value ?? 1), sessionCode = SessionBox.Text, sourceCode = Editor.Text, headerCode = HeaderEditor.Text, headerFileName = _headerName, platform = "macOS-arm64" };
            HttpResponseMessage response = await _http.PostAsJsonAsync(Normalize(ServerBox.Text!) + "/submit", payload, _lifetime.Token);
            StatusText.Text = response.IsSuccessStatusCode ? "Esercizio inviato al docente." : $"Invio non riuscito: {(int)response.StatusCode}.";
        }
        catch (Exception ex) { StatusText.Text = "Invio non riuscito: " + ex.Message; }
    }

    private void ClearConsole_Click(object? sender, RoutedEventArgs e) => ConsolePanel.Children.Clear();
    private void WriteGreen(string text) => Append(text, Color.Parse("#39FF73"));
    private void WriteWhite(string text) => Append(text, Colors.White);
    private void Append(string text, Color color)
    {
        ConsolePanel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(color),
            FontFamily = new FontFamily("Menlo, monospace"),
            TextWrapping = TextWrapping.Wrap
        });
        Dispatcher.UIThread.Post(() => ConsoleScroll.ScrollToEnd());
    }

    private async void Editor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        var big = new Window
        {
            Title = "main.cpp — Editor",
            Width = 1050,
            Height = 720,
            Background = new SolidColorBrush(Color.Parse("#F3F6FA"))
        };
        var edit = new TextEditor
        {
            Text = Editor.Text,
            FontFamily = new FontFamily("Menlo, SFMono-Regular, monospace"),
            FontSize = 15,
            Background = new SolidColorBrush(Colors.White),
            Foreground = new SolidColorBrush(Color.Parse("#1F2328")),
            ShowLineNumbers = true,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        ConfigureCodeEditor(edit);
        big.Content = new Border
        {
            Margin = new Thickness(12),
            BorderBrush = new SolidColorBrush(Color.Parse("#AAB7C4")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = edit
        };
        big.Closing += (_, _) => Editor.Text = edit.Text;
        await big.ShowDialog(this);
    }

    private async void AddHeader_Click(object? sender, RoutedEventArgs e) { _headerName = "esercizio.h"; HeaderEditor.Text = "#pragma once\n\n"; await Message("File header creato."); }
    private async void RenameHeader_Click(object? sender, RoutedEventArgs e) { _headerName = _headerName == "esercizio.h" ? "funzioni.h" : "esercizio.h"; await Message("Nome corrente: " + _headerName); }
    private async void DeleteHeader_Click(object? sender, RoutedEventArgs e) { HeaderEditor.Text = ""; await Message("Contenuto header eliminato."); }
    private async Task Message(string text) { var w = new Window { Title = "CV+", Width = 350, Height = 130, Content = new TextBlock { Text = text, Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap } }; await w.ShowDialog(this); }
    private static string Normalize(string address) => address.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? address.TrimEnd('/') : "http://" + address.TrimEnd('/');
}
