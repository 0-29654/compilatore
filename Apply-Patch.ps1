param(
    [Parameter(Mandatory = $false)]
    [string]$RepositoryPath = "."
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path $RepositoryPath).Path
$mainFile = Join-Path $repo "MainWindow.xaml.cs"
$analyzerSource = Join-Path $PSScriptRoot "CppErrorAnalyzer.cs"
$analyzerDestination = Join-Path $repo "CppErrorAnalyzer.cs"

if (-not (Test-Path $mainFile)) {
    throw "MainWindow.xaml.cs non trovato in: $repo"
}

if (-not (Test-Path $analyzerSource)) {
    throw "CppErrorAnalyzer.cs non trovato accanto allo script."
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
Copy-Item $mainFile "$mainFile.backup_$timestamp" -Force
Copy-Item $analyzerSource $analyzerDestination -Force

$content = Get-Content $mainFile -Raw -Encoding UTF8

$oldFailure = 'OutputBox.Text = $"Compilazione C++17 non riuscita (codice {process.ExitCode}).\n\n{_compileOutput}"; return false;'
$newFailure = 'string analysis = CppErrorAnalyzer.Analyze(Editor.Text, _compileOutput); OutputBox.Text = $"Compilazione C++17 non riuscita (codice {process.ExitCode}).\n\n{_compileOutput}" + (string.IsNullOrWhiteSpace(analysis) ? "" : "\n\n" + analysis); return false;'

$oldSuccess = 'OutputBox.Text = "Compilazione riuscita in C++17.\n" + _compileOutput; return true;'
$newSuccess = 'string analysis = CppErrorAnalyzer.Analyze(Editor.Text, _compileOutput); OutputBox.Text = "Compilazione riuscita in C++17.\n" + _compileOutput + (string.IsNullOrWhiteSpace(analysis) ? "" : "\n\n" + analysis); return true;'

$changed = $false

if ($content.Contains($oldFailure)) {
    $content = $content.Replace($oldFailure, $newFailure)
    $changed = $true
} elseif (-not $content.Contains('CppErrorAnalyzer.Analyze(Editor.Text, _compileOutput)')) {
    throw "Punto di inserimento dell'errore di compilazione non trovato. La versione del repository potrebbe essere cambiata."
}

if ($content.Contains($oldSuccess)) {
    $content = $content.Replace($oldSuccess, $newSuccess)
    $changed = $true
}

if ($changed) {
    Set-Content $mainFile -Value $content -Encoding UTF8 -NoNewline
}

Write-Host "Patch applicata correttamente." -ForegroundColor Green
Write-Host "Aggiunto: CppErrorAnalyzer.cs"
Write-Host "Modificato: MainWindow.xaml.cs"
Write-Host "Backup: $mainFile.backup_$timestamp"
Write-Host "Ora esegui: dotnet build"
