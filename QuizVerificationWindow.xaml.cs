using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace CppStudentClient;

public partial class QuizVerificationWindow : Window
{
    private readonly HttpClient _http;
    private readonly string _serverBase;
    private readonly string _studentId, _studentName, _className, _clientIp, _type, _assignmentId;
    private readonly int _durationMinutes;
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<QuizQuestion> _questions;
    private readonly Dictionary<int, FrameworkElement> _answerControls = new();
    private bool _submitted;

    public QuizVerificationWindow(HttpClient http, string serverBase, string assignmentId, string pdfPath, string type, int durationMinutes, string studentId, string studentName, string className, string clientIp)
    {
        InitializeComponent();
        _http = http; _serverBase = serverBase; _assignmentId = assignmentId; _type = type; _durationMinutes = Math.Max(1, durationMinutes);
        _studentId = studentId; _studentName = studentName; _className = className; _clientIp = clientIp;
        TitleText.Text = $"Verifica - Tipologia {_type}";
        StudentText.Text = $"N° registro {_studentId}   {_studentName}   Classe {_className}";
        _questions = ParseQuestions(pdfPath);
        BuildForm();
        _timer.Tick += Timer_Tick;
        _timer.Start();
        Closing += (_, e) => { if (!_submitted) e.Cancel = true; };
        PreviewKeyDown += (_, e) => { if ((Keyboard.Modifiers & (System.Windows.Input.ModifierKeys.Alt | System.Windows.Input.ModifierKeys.Control)) != 0 && e.Key == System.Windows.Input.Key.F4) e.Handled = true; };
        Timer_Tick(this, EventArgs.Empty);
    }

    private void BuildForm()
    {
        if (_questions.Count == 0)
        {
            QuestionsPanel.Children.Add(new TextBlock { Text = "Il PDF non contiene domande riconoscibili automaticamente. Avvisa il docente.", FontSize = 18, Foreground = Brushes.DarkRed, TextWrapping = TextWrapping.Wrap });
            SubmitButton.IsEnabled = false;
            return;
        }
        foreach (var q in _questions)
        {
            var card = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)), BorderThickness = new Thickness(1), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 18) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = $"{q.Number}. {q.Text}", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,14) });
            if (q.Options.Count >= 2)
            {
                var optionPanel = new StackPanel();
                foreach (string option in q.Options)
                {
                    optionPanel.Children.Add(new RadioButton { Content = option, GroupName = "q" + q.Number, FontSize = 15, Margin = new Thickness(0,6,0,6), Tag = option });
                }
                stack.Children.Add(optionPanel); _answerControls[q.Number] = optionPanel;
            }
            else
            {
                var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 110, FontSize = 15, Padding = new Thickness(10), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                stack.Children.Add(box); _answerControls[q.Number] = box;
            }
            card.Child = stack; QuestionsPanel.Children.Add(card);
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        TimeSpan total = TimeSpan.FromMinutes(_durationMinutes);
        TimeSpan elapsed = DateTime.UtcNow - _startedUtc;
        TimeSpan remaining = total - elapsed;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        TimerText.Text = remaining.TotalHours >= 1 ? remaining.ToString(@"hh\:mm\:ss") : remaining.ToString(@"mm\:ss");
        if (remaining == TimeSpan.Zero && !_submitted) _ = SubmitAsync(true);
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_submitted) return;
        var answer = MessageBox.Show(this, "Confermi la consegna definitiva della verifica?", "Consegna", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) await SubmitAsync(false);
    }

    private string GetAnswer(QuizQuestion q)
    {
        if (!_answerControls.TryGetValue(q.Number, out FrameworkElement? control)) return "";
        if (control is TextBox tb) return tb.Text.Trim();
        if (control is StackPanel sp)
        {
            return sp.Children.OfType<RadioButton>().FirstOrDefault(x => x.IsChecked == true)?.Tag?.ToString() ?? "";
        }
        return "";
    }

    private async Task SubmitAsync(bool automatic)
    {
        if (_submitted) return;
        SubmitButton.IsEnabled = false; StatusText.Text = automatic ? "Tempo scaduto: invio automatico in corso..." : "Invio in corso...";
        try
        {
            long elapsed = Math.Min((long)(DateTime.UtcNow - _startedUtc).TotalSeconds, _durationMinutes * 60L);
            var rows = _questions.Select(q => new PdfAnswerRow(q.Number, q.Text, q.Options, GetAnswer(q))).ToList();
            byte[] pdf = SimplePdf.Create($"VERIFICA - TIPOLOGIA {_type}", new[]
            {
                $"Numero di registro: {_studentId}", $"Nome e cognome: {_studentName}", $"Classe: {_className}",
                $"Tempo impiegato: {TimeSpan.FromSeconds(elapsed):hh\\:mm\\:ss}", automatic ? "Consegna: automatica per tempo scaduto" : "Consegna: manuale"
            }, rows);
            var payload = new { assignmentId = _assignmentId, studentId = _studentId, studentName = _studentName, className = _className, clientIp = _clientIp, verificationType = _type, elapsedSeconds = elapsed, autoSubmitted = automatic, pdfBase64 = Convert.ToBase64String(pdf) };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using HttpResponseMessage response = await _http.PostAsync(_serverBase + "/quiz-submit", content, timeout.Token);
            response.EnsureSuccessStatusCode();
            _submitted = true; _timer.Stop(); StatusText.Text = "Verifica consegnata correttamente.";
            MessageBox.Show(this, automatic ? "Tempo scaduto. La verifica è stata consegnata automaticamente." : "Verifica consegnata correttamente.", "Consegna completata", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            SubmitButton.IsEnabled = true;
            StatusText.Text = "Invio non riuscito. Riproverò alla prossima azione.";
            if (!automatic) MessageBox.Show(this, "Non è stato possibile inviare la verifica al docente.\n\n" + ex.Message, "Invio non riuscito", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static List<QuizQuestion> ParseQuestions(string pdfPath)
    {
        var lines = new List<string>();
        using (PdfDocument doc = PdfDocument.Open(pdfPath))
        {
            foreach (var page in doc.GetPages())
            {
                string text = ContentOrderTextExtractor.GetText(page) ?? "";
                foreach (string line in Regex.Split(text, "\\r?\\n").Select(x => Regex.Replace(x, "\\s+", " ").Trim()).Where(x => x.Length > 0)) lines.Add(line);
            }
        }
        var result = new List<QuizQuestion>(); QuizQuestion? current = null; int autoNumber = 1;
        var qrx = new Regex(@"^\s*(\d{1,3})\s*[\.\)\-:]\s*(.+)$");
        var orx = new Regex(@"^\s*([A-Ha-h])\s*[\.\)\-:]\s*(.+)$");
        foreach (string line in lines)
        {
            Match qm = qrx.Match(line);
            if (qm.Success)
            {
                if (current != null) result.Add(current);
                int n = int.TryParse(qm.Groups[1].Value, out int parsed) ? parsed : autoNumber;
                current = new QuizQuestion { Number = n, Text = qm.Groups[2].Value.Trim() }; autoNumber = Math.Max(autoNumber, n + 1); continue;
            }
            Match om = orx.Match(line);
            if (om.Success && current != null)
            { current.Options.Add(om.Groups[1].Value.ToUpperInvariant() + ") " + om.Groups[2].Value.Trim()); continue; }
            if (current != null)
            {
                if (current.Options.Count == 0) current.Text += " " + line;
                else current.Options[current.Options.Count - 1] += " " + line;
            }
        }
        if (current != null) result.Add(current);
        if (result.Count == 0)
        {
            foreach (string line in lines.Where(x => x.Length > 12 && !x.Equals("VERIFICA", StringComparison.OrdinalIgnoreCase)).Take(30))
                result.Add(new QuizQuestion { Number = autoNumber++, Text = line });
        }
        return result;
    }

    private sealed class QuizQuestion { public int Number { get; set; } public string Text { get; set; } = ""; public List<string> Options { get; } = new(); }
    private sealed record PdfAnswerRow(int Number, string Question, List<string> Options, string Answer);

    private static class SimplePdf
    {
        public static byte[] Create(string title, IEnumerable<string> header, IEnumerable<PdfAnswerRow> answers)
        {
            var lines = new List<string> { title, "" }; lines.AddRange(header); lines.Add("");
            foreach (var a in answers)
            {
                lines.Add($"{a.Number}. {a.Question}");
                if (a.Options.Count > 0)
                    foreach (string o in a.Options) lines.Add((o == a.Answer ? "[X] " : "[ ] ") + o);
                else lines.Add("Risposta: " + (string.IsNullOrWhiteSpace(a.Answer) ? "(nessuna risposta)" : a.Answer));
                lines.Add("");
            }
            var wrapped = lines.SelectMany(x => Wrap(Clean(x), 92)).ToList();
            var pages = wrapped.Chunk(48).ToList();
            var objects = new List<byte[]>();
            int pageCount = pages.Count;
            int fontObj = 3 + pageCount * 2;
            objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
            var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{3 + i*2} 0 R"));
            objects.Add(Ascii($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>"));
            for (int i=0;i<pageCount;i++)
            {
                int pageObj = 3+i*2, contentObj = pageObj+1;
                objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 {fontObj} 0 R >> >> /Contents {contentObj} 0 R >>"));
                var sb = new StringBuilder("BT\n/F1 10 Tf\n50 790 Td\n14 TL\n");
                foreach (string line in pages[i]) sb.Append('(').Append(Escape(line)).Append(") Tj\nT*\n");
                sb.Append("ET"); byte[] stream = Ascii(sb.ToString());
                objects.Add(Ascii($"<< /Length {stream.Length} >>\nstream\n").Concat(stream).Concat(Ascii("\nendstream")).ToArray());
            }
            objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
            using var ms = new MemoryStream(); ms.Write(Ascii("%PDF-1.4\n%CVPLUS\n"));
            var offsets = new List<long> { 0 };
            for (int i=0;i<objects.Count;i++) { offsets.Add(ms.Position); ms.Write(Ascii($"{i+1} 0 obj\n")); ms.Write(objects[i]); ms.Write(Ascii("\nendobj\n")); }
            long xref = ms.Position; ms.Write(Ascii($"xref\n0 {objects.Count+1}\n0000000000 65535 f \n"));
            foreach (long off in offsets.Skip(1)) ms.Write(Ascii($"{off:0000000000} 00000 n \n"));
            ms.Write(Ascii($"trailer\n<< /Size {objects.Count+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF")); return ms.ToArray();
        }
        private static IEnumerable<string> Wrap(string text, int max) { if (string.IsNullOrEmpty(text)) { yield return ""; yield break; } while (text.Length > max) { int p=text.LastIndexOf(' ', max); if (p<20) p=max; yield return text[..p]; text=text[p..].TrimStart(); } yield return text; }
        private static string Clean(string s) => new string(s.Select(c => c >= 32 && c <= 126 ? c : c switch { 'à'=>'a','è'=>'e','é'=>'e','ì'=>'i','ò'=>'o','ù'=>'u','À'=>'A','È'=>'E','É'=>'E','Ì'=>'I','Ò'=>'O','Ù'=>'U', _=>' ' }).ToArray());
        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
    }
}
