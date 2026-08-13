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
    private bool _forceCloseAllowed;

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
        Closing += (_, e) => { if (!_submitted && !_forceCloseAllowed) e.Cancel = true; };
        PreviewKeyDown += (_, e) => { if ((Keyboard.Modifiers & (System.Windows.Input.ModifierKeys.Alt | System.Windows.Input.ModifierKeys.Control)) != 0 && e.Key == System.Windows.Input.Key.F4) e.Handled = true; };
        Timer_Tick(this, EventArgs.Empty);
    }

    public void ForceCloseFromServer()
    {
        _forceCloseAllowed = true;
        _timer.Stop();
        try
        {
            Dispatcher.InvokeAsync(() =>
            {
                StatusText.Text = "Verifica terminata dal docente.";
                Close();
            });
        }
        catch { }
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

            // Numero esercizio separato dal contenuto: i blocchi [CODICE] vengono
            // visualizzati come vero codice monospaziato e non come testo continuo.
            stack.Children.Add(new TextBlock
            {
                Text = $"Esercizio {q.Number}",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                Margin = new Thickness(0, 0, 0, 10)
            });

            if (q.Parts.Count > 0)
            {
                foreach (var part in q.Parts)
                {
                    if (part.IsCode)
                    {
                        var codeText = new TextBox
                        {
                            Text = part.Text,
                            IsReadOnly = true,
                            AcceptsReturn = true,
                            AcceptsTab = true,
                            TextWrapping = TextWrapping.NoWrap,
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 14,
                            Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                            Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                            BorderThickness = new Thickness(1),
                            Padding = new Thickness(14),
                            Margin = new Thickness(0, 4, 0, 14),
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            MinHeight = 42
                        };
                        stack.Children.Add(codeText);
                    }
                    else if (!string.IsNullOrWhiteSpace(part.Text))
                    {
                        stack.Children.Add(new TextBlock
                        {
                            Text = part.Text,
                            FontSize = 16,
                            Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 12)
                        });
                    }
                }
            }
            else
            {
                stack.Children.Add(new TextBlock { Text = q.Text, FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,14) });
            }

            if (q.Options.Count >= 2)
            {
                var optionPanel = new StackPanel();
                foreach (string option in q.Options)
                {
                    optionPanel.Children.Add(new RadioButton { Content = option, GroupName = "q" + q.Number, FontSize = 15, Margin = new Thickness(0,6,0,6), Tag = option });
                }
                stack.Children.Add(optionPanel); _answerControls[q.Number] = optionPanel;
            }
            else if (q.AnswerKind == QuizAnswerKind.Code)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "Scrivi il codice qui sotto:",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                    Margin = new Thickness(0, 4, 0, 6)
                });
                var box = new TextBox
                {
                    AcceptsReturn = true,
                    AcceptsTab = true,
                    TextWrapping = TextWrapping.NoWrap,
                    MinHeight = 260,
                    FontSize = 15,
                    FontFamily = new FontFamily("Consolas"),
                    Padding = new Thickness(12),
                    Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                    Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(190, 194, 198)),
                    BorderThickness = new Thickness(1),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                box.PreviewKeyDown += CodeAnswerBox_PreviewKeyDown;
                stack.Children.Add(box); _answerControls[q.Number] = box;
            }
            else
            {
                var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 110, FontSize = 15, Padding = new Thickness(10), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                stack.Children.Add(box); _answerControls[q.Number] = box;
            }
            card.Child = stack; QuestionsPanel.Children.Add(card);
        }
    }

    private static void CodeAnswerBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        // TAB resta dentro l'editor e non sposta il focus su altri controlli.
        if (e.Key == Key.Tab)
        {
            int start = box.SelectionStart;
            box.SelectedText = "    ";
            box.SelectionStart = start + 4;
            box.SelectionLength = 0;
            e.Handled = true;
            return;
        }

        // ENTER mantiene l'indentazione della riga corrente e aggiunge un livello
        // dopo una graffa aperta, come in un normale editor C++.
        if (e.Key == Key.Enter)
        {
            int caret = box.SelectionStart;
            string text = box.Text ?? "";
            int lineStart = caret > 0 ? text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1 : 0;
            string beforeCaret = text.Substring(lineStart, Math.Max(0, caret - lineStart)).TrimEnd('\r');
            string indent = new string(beforeCaret.TakeWhile(c => c == ' ' || c == '\t').ToArray());
            string trimmed = beforeCaret.TrimEnd();
            if (trimmed.EndsWith("{", StringComparison.Ordinal)) indent += "    ";
            box.SelectedText = Environment.NewLine + indent;
            box.SelectionStart = caret + Environment.NewLine.Length + indent.Length;
            box.SelectionLength = 0;
            e.Handled = true;
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
        if (control is TextBox tb)
            return q.AnswerKind == QuizAnswerKind.Code ? (tb.Text ?? "").TrimEnd() : (tb.Text ?? "").Trim();
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
            var rows = _questions.Select(q => new PdfAnswerRow(q.Number, q.Text, q.Options, GetAnswer(q), q.AnswerKind, q.Parts.ToList())).ToList();
            byte[] pdf = SimplePdf.Create($"VERIFICA - TIPOLOGIA {_type}", new[]
            {
                $"Numero di registro: {_studentId}", $"Nome e cognome: {_studentName}", $"Classe: {_className}",
                $"Tempo impiegato: {TimeSpan.FromSeconds(elapsed):hh\\:mm\\:ss}", automatic ? "Consegna: automatica per tempo scaduto" : "Consegna: manuale"
            }, rows);
            bool containsCodeAnswers = _questions.Any(q => q.AnswerKind == QuizAnswerKind.Code);
            var payload = new { assignmentId = _assignmentId, studentId = _studentId, studentName = _studentName, className = _className, clientIp = _clientIp, verificationType = _type, elapsedSeconds = elapsed, autoSubmitted = automatic, answerFormat = "structured-code-v2", containsCodeAnswers, pdfBase64 = Convert.ToBase64String(pdf) };
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
        // IMPORTANTE: non comprimiamo subito gli spazi. Nei blocchi [CODICE]
        // i ritorni a capo devono sopravvivere all'estrazione dal PDF.
        var rawLines = new List<string>();
        using (PdfDocument doc = PdfDocument.Open(pdfPath))
        {
            foreach (var page in doc.GetPages())
            {
                string text = ContentOrderTextExtractor.GetText(page) ?? "";
                foreach (string raw in Regex.Split(text, "\\r?\\n"))
                    rawLines.Add(raw.Replace("\t", "    ").TrimEnd());
            }
        }

        // Prima tentiamo il formato strutturato. Riconosce anche:
        // [CODICE]
        //   ...codice C++...
        // [/CODICE]
        var structured = ParseStructuredQuestions(rawLines);
        if (structured.Count > 0) return structured;

        // Compatibilità con vecchi PDF non strutturati: qui invece possiamo
        // normalizzare gli spazi perché non esistono marcatori di codice.
        var lines = rawLines
            .Select(x => Regex.Replace(x, "\\s+", " ").Trim())
            .Where(x => x.Length > 0)
            .ToList();

        var result = new List<QuizQuestion>();
        QuizQuestion? current = null;
        int autoNumber = 1;
        var qrx = new Regex(@"^\s*(\d{1,3})\s*[.\)\-:]\s*(.+)$");
        var orx = new Regex(@"^\s*([A-Ha-h])\s*[.\)\-:]\s*(.+)$");
        foreach (string line in lines)
        {
            Match qm = qrx.Match(line);
            if (qm.Success)
            {
                if (current != null) result.Add(current);
                int n = int.TryParse(qm.Groups[1].Value, out int parsed) ? parsed : autoNumber;
                current = new QuizQuestion { Number = n, Text = qm.Groups[2].Value.Trim() };
                current.Parts.Add(new QuizPart(false, current.Text));
                autoNumber = Math.Max(autoNumber, n + 1);
                continue;
            }
            Match om = orx.Match(line);
            if (om.Success && current != null)
            {
                current.AnswerKind = QuizAnswerKind.Multiple;
                current.Options.Add(om.Groups[1].Value.ToUpperInvariant() + ") " + om.Groups[2].Value.Trim());
                continue;
            }
            if (current != null)
            {
                if (current.Options.Count == 0)
                {
                    current.Text += " " + line;
                    if (current.Parts.Count > 0)
                        current.Parts[current.Parts.Count - 1] = new QuizPart(false, current.Text);
                }
                else current.Options[current.Options.Count - 1] += " " + line;
            }
        }
        if (current != null) result.Add(current);
        return result.Where(q => !string.IsNullOrWhiteSpace(q.Text)).ToList();
    }

    private static List<QuizQuestion> ParseStructuredQuestions(List<string> rawLines)
    {
        var result = new List<QuizQuestion>();
        QuizQuestion? current = null;
        QuizAnswerKind currentKind = QuizAnswerKind.Open;
        bool inCode = false;
        var code = new List<string>();
        var normalText = new List<string>();
        var header = new Regex(@"^\s*\[DOMANDA\s+(\d{1,3})\]\s*\[(MULTIPLA|APERTA|CODICE)\]\s*$", RegexOptions.IgnoreCase);
        var option = new Regex(@"^\s*\[([A-H])\]\s*(.+)$", RegexOptions.IgnoreCase);

        void FlushNormal()
        {
            if (current == null || normalText.Count == 0) return;
            string text = string.Join(" ", normalText.Select(x => Regex.Replace(x, "\\s+", " ").Trim()).Where(x => x.Length > 0));
            normalText.Clear();
            if (string.IsNullOrWhiteSpace(text)) return;
            current.Parts.Add(new QuizPart(false, text));
            current.Text = string.IsNullOrWhiteSpace(current.Text) ? text : current.Text + " " + text;
        }

        void FlushCode()
        {
            if (current == null) { code.Clear(); return; }
            while (code.Count > 0 && string.IsNullOrWhiteSpace(code[0])) code.RemoveAt(0);
            while (code.Count > 0 && string.IsNullOrWhiteSpace(code[^1])) code.RemoveAt(code.Count - 1);
            if (code.Count == 0) return;

            // Molti PDF perdono gli spazi iniziali. Ricostruiamo una normale
            // indentazione C++ dalle parentesi graffe, lasciando intatte le righe.
            string formatted = ReindentCpp(code);
            current.Parts.Add(new QuizPart(true, formatted));
            current.Text = string.IsNullOrWhiteSpace(current.Text) ? formatted : current.Text + "\n" + formatted;
            code.Clear();
        }

        foreach (string rawLine in rawLines)
        {
            string trimmed = rawLine.Trim();
            Match hm = header.Match(trimmed);
            if (!inCode && hm.Success)
            {
                if (current != null)
                {
                    FlushNormal();
                    if (!string.IsNullOrWhiteSpace(current.Text)) result.Add(current);
                }
                string kindText = hm.Groups[2].Value;
                currentKind = kindText.Equals("MULTIPLA", StringComparison.OrdinalIgnoreCase) ? QuizAnswerKind.Multiple
                    : kindText.Equals("CODICE", StringComparison.OrdinalIgnoreCase) ? QuizAnswerKind.Code
                    : QuizAnswerKind.Open;
                current = new QuizQuestion { Number = int.Parse(hm.Groups[1].Value), Text = "", AnswerKind = currentKind };
                normalText.Clear(); code.Clear(); inCode = false;
                continue;
            }

            if (current == null) continue;

            if (!inCode && trimmed.Equals("[CODICE]", StringComparison.OrdinalIgnoreCase))
            {
                FlushNormal();
                inCode = true;
                code.Clear();
                continue;
            }

            if (inCode)
            {
                if (trimmed.Equals("[/CODICE]", StringComparison.OrdinalIgnoreCase))
                {
                    FlushCode();
                    inCode = false;
                }
                else
                {
                    code.Add(rawLine);
                }
                continue;
            }

            if (trimmed.Equals("[FINE]", StringComparison.OrdinalIgnoreCase))
            {
                FlushNormal();
                if (current != null && !string.IsNullOrWhiteSpace(current.Text)) result.Add(current);
                current = null;
                currentKind = QuizAnswerKind.Open;
                continue;
            }

            Match om = option.Match(trimmed);
            if (currentKind == QuizAnswerKind.Multiple && om.Success)
            {
                FlushNormal();
                current.Options.Add(om.Groups[1].Value.ToUpperInvariant() + ") " + om.Groups[2].Value.Trim());
                continue;
            }

            // Una continuazione dopo un'opzione appartiene all'opzione; altrimenti
            // fa parte del testo normale della domanda.
            if (currentKind == QuizAnswerKind.Multiple && current.Options.Count > 0)
                current.Options[current.Options.Count - 1] += " " + Regex.Replace(trimmed, "\\s+", " ");
            else if (trimmed.Length > 0)
                normalText.Add(rawLine);
        }

        if (inCode) FlushCode();
        FlushNormal();
        if (current != null && !string.IsNullOrWhiteSpace(current.Text)) result.Add(current);
        return result;
    }

    private static string ReindentCpp(List<string> sourceLines)
    {
        var output = new List<string>();
        int indent = 0;

        foreach (string original in sourceLines)
        {
            string line = original.Trim();
            if (line.Length == 0)
            {
                output.Add("");
                continue;
            }

            // Chiude prima di stampare la riga quando la riga inizia con '}'.
            int leadingClosers = 0;
            for (int i = 0; i < line.Length && line[i] == '}'; i++) leadingClosers++;
            int effectiveIndent = Math.Max(0, indent - leadingClosers);
            output.Add(new string(' ', effectiveIndent * 4) + line);

            // Conteggio semplice ma robusto per i normali esercizi didattici C++.
            // Ignora le graffe dentro stringhe/caratteri e commenti //.
            int opens = 0, closes = 0;
            bool inString = false, inChar = false, escaped = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (!inString && !inChar && c == '/' && i + 1 < line.Length && line[i + 1] == '/') break;
                if (escaped) { escaped = false; continue; }
                if ((inString || inChar) && c == '\\') { escaped = true; continue; }
                if (!inChar && c == '"') { inString = !inString; continue; }
                if (!inString && c == '\'') { inChar = !inChar; continue; }
                if (inString || inChar) continue;
                if (c == '{') opens++;
                else if (c == '}') closes++;
            }
            indent = Math.Max(0, indent + opens - closes);
        }

        return string.Join(Environment.NewLine, output);
    }

    private enum QuizAnswerKind
    {
        Open,
        Multiple,
        Code
    }

    private sealed class QuizQuestion
    {
        public int Number { get; set; }
        public string Text { get; set; } = "";
        public QuizAnswerKind AnswerKind { get; set; } = QuizAnswerKind.Open;
        public List<string> Options { get; } = new();
        public List<QuizPart> Parts { get; } = new();
    }

    private sealed record QuizPart(bool IsCode, string Text);
    private sealed record PdfAnswerRow(int Number, string Question, List<string> Options, string Answer, QuizAnswerKind AnswerKind, List<QuizPart> Parts);
    private sealed record PdfLine(string Text, bool IsCode = false);

    private static class SimplePdf
    {
        public static byte[] Create(string title, IEnumerable<string> header, IEnumerable<PdfAnswerRow> answers)
        {
            var lines = new List<PdfLine> { new(title), new("") };
            lines.AddRange(header.Select(x => new PdfLine(x)));
            lines.Add(new PdfLine(""));

            foreach (var a in answers)
            {
                lines.Add(new PdfLine($"ESERCIZIO {a.Number}"));

                // Ricostruiamo il testo della consegna distinguendo gli eventuali
                // blocchi di codice forniti dal docente.
                if (a.Parts.Count > 0)
                {
                    foreach (var part in a.Parts)
                    {
                        if (part.IsCode)
                        {
                            foreach (string codeLine in SplitLines(part.Text))
                                lines.Add(new PdfLine(codeLine, true));
                        }
                        else
                        {
                            foreach (string textLine in Wrap(Clean(part.Text), 92))
                                lines.Add(new PdfLine(textLine));
                        }
                    }
                }
                else
                {
                    foreach (string textLine in Wrap(Clean(a.Question), 92))
                        lines.Add(new PdfLine(textLine));
                }

                if (a.Options.Count > 0)
                {
                    foreach (string o in a.Options)
                        lines.Add(new PdfLine((o == a.Answer ? "[X] " : "[ ] ") + o));
                }
                else if (a.AnswerKind == QuizAnswerKind.Code)
                {
                    lines.Add(new PdfLine("RISPOSTA CODICE:"));
                    if (string.IsNullOrWhiteSpace(a.Answer))
                    {
                        lines.Add(new PdfLine("(nessuna risposta)", true));
                    }
                    else
                    {
                        foreach (string codeLine in SplitLines(a.Answer.Replace("\t", "    ")))
                            foreach (string physicalLine in WrapCode(CleanCode(codeLine), 100))
                                lines.Add(new PdfLine(physicalLine, true));
                    }
                }
                else
                {
                    lines.Add(new PdfLine("RISPOSTA:"));
                    string answer = string.IsNullOrWhiteSpace(a.Answer) ? "(nessuna risposta)" : a.Answer;
                    foreach (string answerLine in SplitLines(answer))
                        foreach (string wrapped in Wrap(Clean(answerLine), 92))
                            lines.Add(new PdfLine(wrapped));
                }
                lines.Add(new PdfLine(""));
            }

            // Le righe di codice sono leggermente più compatte, ma ogni pagina
            // conserva una quantità prevedibile di righe.
            var pages = lines.Chunk(48).ToList();
            var objects = new List<byte[]>();
            int pageCount = pages.Count;
            int helveticaObj = 3 + pageCount * 2;
            int courierObj = helveticaObj + 1;
            objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
            var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{3 + i*2} 0 R"));
            objects.Add(Ascii($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>"));

            for (int i = 0; i < pageCount; i++)
            {
                int pageObj = 3 + i * 2, contentObj = pageObj + 1;
                objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 {helveticaObj} 0 R /F2 {courierObj} 0 R >> >> /Contents {contentObj} 0 R >>"));
                var sb = new StringBuilder("BT\n50 790 Td\n14 TL\n");
                bool? lastCode = null;
                foreach (PdfLine line in pages[i])
                {
                    if (lastCode != line.IsCode)
                    {
                        sb.Append(line.IsCode ? "/F2 8.5 Tf\n" : "/F1 10 Tf\n");
                        lastCode = line.IsCode;
                    }
                    sb.Append('(').Append(Escape(line.Text)).Append(") Tj\nT*\n");
                }
                sb.Append("ET");
                byte[] stream = Ascii(sb.ToString());
                objects.Add(Ascii($"<< /Length {stream.Length} >>\nstream\n").Concat(stream).Concat(Ascii("\nendstream")).ToArray());
            }

            objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
            objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>"));

            using var ms = new MemoryStream();
            ms.Write(Ascii("%PDF-1.4\n%CVPLUS\n"));
            var offsets = new List<long> { 0 };
            for (int i = 0; i < objects.Count; i++)
            {
                offsets.Add(ms.Position);
                ms.Write(Ascii($"{i + 1} 0 obj\n"));
                ms.Write(objects[i]);
                ms.Write(Ascii("\nendobj\n"));
            }
            long xref = ms.Position;
            ms.Write(Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
            foreach (long off in offsets.Skip(1)) ms.Write(Ascii($"{off:0000000000} 00000 n \n"));
            ms.Write(Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF"));
            return ms.ToArray();
        }

        private static IEnumerable<string> SplitLines(string text) => Regex.Split(text ?? "", "\\r?\\n");

        private static IEnumerable<string> Wrap(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) { yield return ""; yield break; }
            while (text.Length > max)
            {
                int p = text.LastIndexOf(' ', max);
                if (p < 20) p = max;
                yield return text[..p];
                text = text[p..].TrimStart();
            }
            yield return text;
        }

        private static IEnumerable<string> WrapCode(string text, int max)
        {
            if (text.Length <= max) { yield return text; yield break; }
            string leading = new string(text.TakeWhile(c => c == ' ').ToArray());
            string remaining = text;
            bool first = true;
            while (remaining.Length > max)
            {
                yield return remaining[..max];
                remaining = (first ? leading : leading) + remaining[max..];
                first = false;
            }
            yield return remaining;
        }

        private static string Clean(string s) => new string((s ?? "").Select(c => c >= 32 && c <= 126 ? c : c switch { 'à'=>'a','è'=>'e','é'=>'e','ì'=>'i','ò'=>'o','ù'=>'u','À'=>'A','È'=>'E','É'=>'E','Ì'=>'I','Ò'=>'O','Ù'=>'U', _=>' ' }).ToArray());
        private static string CleanCode(string s) => new string((s ?? "").Select(c => c == '\t' ? ' ' : c >= 32 && c <= 126 ? c : c switch { 'à'=>'a','è'=>'e','é'=>'e','ì'=>'i','ò'=>'o','ù'=>'u','À'=>'A','È'=>'E','É'=>'E','Ì'=>'I','Ò'=>'O','Ù'=>'U', _=>' ' }).ToArray());
        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
    }

}
