using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CppStudentClient;

internal static class CppErrorAnalyzer
{
    private sealed record Diagnostic(int? Line, int? Column, string Severity, string Message);

    private static readonly Regex GccDiagnosticRegex = new(
        @"^(?<file>.*?):(?<line>\d+):(?<column>\d+):\s*(?<severity>fatal error|error|warning|note):\s*(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Analyze(string sourceCode, string compilerOutput)
    {
        sourceCode ??= string.Empty;
        compilerOutput ??= string.Empty;

        var diagnostics = ParseDiagnostics(compilerOutput);
        var report = new StringBuilder();

        if (diagnostics.Count > 0)
        {
            report.AppendLine("ANALISI AUTOMATICA DEGLI ERRORI");
            report.AppendLine(new string('─', 42));

            foreach (Diagnostic diagnostic in diagnostics.Take(12))
            {
                string location = diagnostic.Line.HasValue
                    ? $"Riga {diagnostic.Line}" + (diagnostic.Column.HasValue ? $", colonna {diagnostic.Column}" : string.Empty)
                    : "Posizione non determinata";

                (string type, string explanation, string suggestion) = ExplainCompilerMessage(diagnostic.Message);

                report.AppendLine($"• {location} — {type}");
                report.AppendLine($"  Descrizione: {explanation}");
                if (!string.IsNullOrWhiteSpace(suggestion))
                    report.AppendLine($"  Possibile correzione: {suggestion}");
                report.AppendLine($"  Messaggio compilatore: {diagnostic.Message.Trim()}");
                report.AppendLine();
            }

            if (diagnostics.Count > 12)
                report.AppendLine($"Sono presenti altri {diagnostics.Count - 12} messaggi del compilatore non mostrati nell'analisi sintetica.");
        }

        List<string> heuristicWarnings = AnalyzeSourceHeuristically(sourceCode);
        if (heuristicWarnings.Count > 0)
        {
            if (report.Length > 0)
                report.AppendLine();

            report.AppendLine("POSSIBILI PROBLEMI LOGICI RILEVATI");
            report.AppendLine(new string('─', 42));
            foreach (string warning in heuristicWarnings.Take(10))
                report.AppendLine("• " + warning);

            report.AppendLine();
            report.AppendLine("Nota: i problemi logici e i loop infiniti sono rilevati con controlli euristici; il programma può segnalare un rischio, ma non può garantire che il codice sia logicamente corretto.");
        }

        return report.ToString().TrimEnd();
    }

    private static List<Diagnostic> ParseDiagnostics(string compilerOutput)
    {
        var result = new List<Diagnostic>();

        foreach (string rawLine in compilerOutput.Replace("\r", string.Empty).Split('\n'))
        {
            Match match = GccDiagnosticRegex.Match(rawLine.Trim());
            if (!match.Success)
                continue;

            int.TryParse(match.Groups["line"].Value, out int line);
            int.TryParse(match.Groups["column"].Value, out int column);
            string severity = match.Groups["severity"].Value.ToLowerInvariant();

            // Le "note" vengono mantenute solo se spiegano un errore principale.
            if (severity == "note" && result.Count >= 12)
                continue;

            result.Add(new Diagnostic(line, column, severity, match.Groups["message"].Value));
        }

        // Alcuni errori del linker non hanno riga/colonna.
        if (result.Count == 0 && !string.IsNullOrWhiteSpace(compilerOutput))
        {
            foreach (string line in compilerOutput.Replace("\r", string.Empty).Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Contains("undefined reference", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("multiple definition", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("ld returned", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new Diagnostic(null, null, "error", trimmed));
                }
            }
        }

        return result;
    }

    private static (string Type, string Explanation, string Suggestion) ExplainCompilerMessage(string message)
    {
        string m = message.ToLowerInvariant();

        if (m.Contains("expected ';'"))
            return ("Errore di sintassi", "Manca probabilmente un punto e virgola alla fine dell'istruzione precedente.", "Controlla la riga indicata e soprattutto quella immediatamente precedente.");

        if (m.Contains("was not declared in this scope") || m.Contains("undeclared identifier"))
            return ("Identificatore non dichiarato", "Il codice usa una variabile, funzione o oggetto che il compilatore non conosce in quel punto.", "Controlla il nome, le maiuscole/minuscole, la dichiarazione e l'ambito della variabile o funzione.");

        if (m.Contains("expected '}'") || m.Contains("expected ‘}’"))
            return ("Errore di sintassi", "Manca una parentesi graffa di chiusura oppure le graffe non sono bilanciate.", "Abbina ogni '{' alla relativa '}' e controlla i blocchi if, for, while e le funzioni.");

        if (m.Contains("expected ')'"))
            return ("Errore di sintassi", "Manca una parentesi tonda di chiusura.", "Controlla condizioni, chiamate di funzione e istruzioni for.");

        if (m.Contains("expected primary-expression"))
            return ("Espressione incompleta", "Il compilatore si aspettava un valore o un'espressione valida, ma ha trovato un simbolo fuori posto.", "Controlla operatori, parentesi, virgole e valori mancanti vicino alla posizione indicata.");

        if (m.Contains("no matching function for call"))
            return ("Chiamata di funzione non compatibile", "La funzione esiste, ma i parametri passati non corrispondono a quelli previsti.", "Verifica numero, ordine e tipo degli argomenti.");

        if (m.Contains("invalid conversion") || m.Contains("cannot convert") || m.Contains("conversion from"))
            return ("Errore di tipo", "Stai assegnando o passando un valore a un tipo non compatibile.", "Correggi il tipo della variabile oppure usa una conversione esplicita solo quando è sicura.");

        if (m.Contains("conflicting declaration"))
            return ("Dichiarazione in conflitto", "Lo stesso nome è stato dichiarato più volte con tipi o firme differenti.", "Rinomina una dichiarazione oppure rendi coerenti i tipi e i parametri.");

        if (m.Contains("redefinition of"))
            return ("Ridefinizione", "Una variabile, funzione o classe è stata definita più di una volta nello stesso ambito.", "Elimina o rinomina la seconda definizione.");

        if (m.Contains("not a member of 'std'") || m.Contains("not a member of ‘std’"))
            return ("Elemento STL non riconosciuto", "Il nome usato non appartiene a std oppure manca l'header necessario.", "Controlla il nome e aggiungi l'include corretto, per esempio <vector>, <string>, <algorithm> o <iostream>.");

        if (m.Contains("no such file or directory"))
            return ("File o libreria non trovata", "Un file incluso con #include non è disponibile o il nome è errato.", "Controlla l'header e usa la forma corretta, per esempio #include <iostream>.");

        if (m.Contains("undefined reference to 'main'") || m.Contains("undefined reference to `main'"))
            return ("Funzione main mancante", "Il linker non trova il punto di ingresso del programma.", "Aggiungi una funzione int main() valida e controlla che non sia scritta dentro un altro blocco.");

        if (m.Contains("undefined reference"))
            return ("Errore di collegamento", "Una funzione o variabile è stata dichiarata o usata, ma non ne è stata trovata la definizione.", "Controlla che la funzione sia implementata e che nome, parametri e tipo restituito coincidano.");

        if (m.Contains("multiple definition"))
            return ("Definizione multipla", "Lo stesso simbolo è stato definito più di una volta.", "Mantieni una sola definizione oppure usa correttamente dichiarazioni extern/header guard.");

        if (m.Contains("control reaches end of non-void function"))
            return ("Valore di ritorno mancante", "Una funzione che deve restituire un valore può terminare senza eseguire return.", "Aggiungi un return appropriato in tutti i percorsi della funzione.");

        if (m.Contains("comparison between signed and unsigned"))
            return ("Confronto tra tipi differenti", "Il confronto usa un intero con segno e uno senza segno; il risultato può essere inatteso.", "Usa tipi coerenti, ad esempio size_t per confrontare con size().");

        if (m.Contains("unused variable"))
            return ("Avviso: variabile non usata", "La variabile è stata dichiarata ma non viene mai utilizzata.", "Usala oppure elimina la dichiarazione se non serve.");

        if (m.Contains("suggest parentheses around assignment used as truth value"))
            return ("Possibile errore logico", "Nella condizione sembra esserci un'assegnazione '=' invece di un confronto '=='.", "Controlla la condizione e sostituisci '=' con '==' se volevi confrontare due valori.");

        return ("Errore di compilazione", "Il compilatore ha rilevato un'istruzione non valida o incoerente.", "Leggi la riga indicata e anche quella precedente: spesso l'origine dell'errore si trova poco prima.");
    }

    private static List<string> AnalyzeSourceHeuristically(string sourceCode)
    {
        var warnings = new List<string>();
        string[] lines = sourceCode.Replace("\r", string.Empty).Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = StripLineComment(lines[i]).Trim();
            int lineNumber = i + 1;

            if (Regex.IsMatch(line, @"\bwhile\s*\(\s*(true|1)\s*\)", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(line, @"\bfor\s*\(\s*;\s*;\s*\)"))
            {
                string block = ReadNearbyBlock(lines, i, 18);
                if (!Regex.IsMatch(block, @"\b(break|return|throw)\b"))
                    warnings.Add($"Riga {lineNumber}: possibile loop infinito; il ciclo non ha una condizione di uscita evidente e nel blocco vicino non compare break o return.");
            }

            Match whileMatch = Regex.Match(line, @"\bwhile\s*\(\s*([A-Za-z_]\w*)\s*(<|<=|>|>=|!=)\s*[^)]+\)");
            if (whileMatch.Success)
            {
                string variable = whileMatch.Groups[1].Value;
                string block = ReadNearbyBlock(lines, i, 18);
                if (!Regex.IsMatch(block, $@"\b{Regex.Escape(variable)}\s*(\+\+|--|[+\-*/%]?=)"))
                    warnings.Add($"Riga {lineNumber}: la variabile '{variable}' controlla il while ma non sembra essere modificata nel blocco vicino; il ciclo potrebbe non terminare.");
            }

            if (Regex.IsMatch(line, @"\b(if|while)\s*\([^)]*(?<![=!<>])=(?!=)[^)]*\)"))
                warnings.Add($"Riga {lineNumber}: possibile assegnazione '=' dentro una condizione; verifica se volevi usare il confronto '=='.");

            if (Regex.IsMatch(line, @"\b(if|while)\s*\(\s*[^)]*;\s*\)"))
                warnings.Add($"Riga {lineNumber}: è presente un punto e virgola dentro o subito dopo una condizione; potrebbe creare un blocco vuoto involontario.");

            if (Regex.IsMatch(line, @"\b(if|for|while)\s*\([^)]*\)\s*;\s*$"))
                warnings.Add($"Riga {lineNumber}: il punto e virgola dopo la condizione rende vuoto il corpo dell'istruzione; il blocco successivo potrebbe essere eseguito sempre.");

            if (Regex.IsMatch(line, @"\b([A-Za-z_]\w*)\s*=\s*\1\s*[+\-]\s*0\s*;"))
                warnings.Add($"Riga {lineNumber}: l'assegnazione non modifica il valore; controlla che l'incremento o decremento sia quello desiderato.");

            if (Regex.IsMatch(line, @"\bcout\s*<<\s*[^;]+$"))
                warnings.Add($"Riga {lineNumber}: l'istruzione cout sembra incompleta o priva del punto e virgola finale.");
        }

        // Propagazione semplice dei valori costanti: permette di riconoscere anche casi
        // come `int n = 0; cout << 7 / n;`, che compilano ma falliscono a runtime.
        var knownNumericValues = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < lines.Length; i++)
        {
            string codeLine = StripStringsAndComments(lines[i]).Trim();
            int lineNumber = i + 1;

            // Dichiarazioni/assegnazioni semplici a costante numerica.
            Match declaration = Regex.Match(codeLine,
                @"\b(?:const\s+)?(?:int|long|short|double|float|auto|size_t)\s+([A-Za-z_]\w*)\s*=\s*([-+]?(?:\d+(?:\.\d*)?|\.\d+))\s*;");
            if (declaration.Success &&
                double.TryParse(declaration.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double declaredValue))
            {
                knownNumericValues[declaration.Groups[1].Value] = declaredValue;
            }

            Match assignment = Regex.Match(codeLine,
                @"^\s*([A-Za-z_]\w*)\s*=\s*([-+]?(?:\d+(?:\.\d*)?|\.\d+))\s*;");
            if (assignment.Success &&
                double.TryParse(assignment.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double assignedValue))
            {
                knownNumericValues[assignment.Groups[1].Value] = assignedValue;
            }

            // Input o modifiche non costanti rendono sconosciuto il valore della variabile.
            foreach (Match input in Regex.Matches(codeLine, @"(?:cin\s*>>|scanf\s*\([^;]*&)\s*([A-Za-z_]\w*)"))
            {
                if (input.Groups.Count > 1 && input.Groups[1].Success)
                    knownNumericValues.Remove(input.Groups[1].Value);
            }

            foreach (Match operation in Regex.Matches(codeLine, @"\b([A-Za-z_]\w*)\s*(?:\+\+|--|[+\-*/%]=)"))
                knownNumericValues.Remove(operation.Groups[1].Value);

            // Divisione/modulo per zero scritto direttamente.
            if (Regex.IsMatch(codeLine, @"(?:/|%)\s*[-+]?0(?:\.0+)?(?:\D|$)"))
            {
                warnings.Add($"Riga {lineNumber}: divisione o modulo per zero certo. Il programma può compilare correttamente, ma durante l'esecuzione può terminare in modo anomalo. Usa un divisore diverso da zero oppure controllane il valore prima dell'operazione.");
            }

            // Divisione/modulo tramite una variabile che in quel punto vale certamente zero.
            foreach (Match division in Regex.Matches(codeLine, @"(?:/|%)\s*([A-Za-z_]\w*)\b"))
            {
                string divisor = division.Groups[1].Value;
                if (knownNumericValues.TryGetValue(divisor, out double value) && Math.Abs(value) < double.Epsilon)
                {
                    warnings.Add($"Riga {lineNumber}: divisione o modulo per zero tramite la variabile '{divisor}'. In questo punto '{divisor}' vale 0: il codice compila, ma l'operazione può causare un errore durante l'esecuzione. Assegna a '{divisor}' un valore diverso da zero oppure verifica '{divisor} != 0' prima di dividere.");
                }
            }
        }

        bool hasVisibleOutput = Regex.IsMatch(sourceCode,
            @"\b(cout|cerr|clog)\s*<<|\bprintf\s*\(",
            RegexOptions.IgnoreCase);
        if (!hasVisibleOutput)
            warnings.Add("Il programma non contiene istruzioni cout, cerr o printf: può compilare ed eseguire correttamente senza mostrare nulla sullo schermo.");

        // Graffe sbilanciate: controllo semplice, ignorando commenti e stringhe in modo prudente.
        int balance = 0;
        foreach (string raw in lines)
        {
            string line = StripStringsAndComments(raw);
            balance += line.Count(c => c == '{');
            balance -= line.Count(c => c == '}');
        }
        if (balance > 0)
            warnings.Add($"Sembrano mancare {balance} parentesi graffe di chiusura '}}'.");
        else if (balance < 0)
            warnings.Add($"Sembrano esserci {-balance} parentesi graffe di chiusura '}}' in eccesso.");

        return warnings.Distinct().ToList();
    }

    private static string ReadNearbyBlock(string[] lines, int start, int maxLines)
    {
        int end = Math.Min(lines.Length, start + maxLines);
        return string.Join("\n", lines.Skip(start).Take(end - start).Select(StripLineComment));
    }

    private static string StripLineComment(string line)
    {
        int index = line.IndexOf("//", StringComparison.Ordinal);
        return index >= 0 ? line[..index] : line;
    }

    private static string StripStringsAndComments(string line)
    {
        string withoutComment = StripLineComment(line);
        return Regex.Replace(withoutComment, "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'", string.Empty);
    }
}
