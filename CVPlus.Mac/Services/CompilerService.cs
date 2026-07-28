using System.Diagnostics;
using System.Text;

namespace CVPlus.Mac.Services;

public sealed record CompileResult(bool Success, string CompilerOutput, string? ExecutablePath, string WorkingDirectory);
public sealed record RunResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class CompilerService
{
    public async Task<CompileResult> CompileAsync(string source, string header, string headerName, CancellationToken token)
    {
        string dir = Path.Combine(Path.GetTempPath(), "CVPlus", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string main = Path.Combine(dir, "main.cpp");
        await File.WriteAllTextAsync(main, source, Encoding.UTF8, token);
        if (!string.IsNullOrWhiteSpace(header))
            await File.WriteAllTextAsync(Path.Combine(dir, SafeHeaderName(headerName)), header, Encoding.UTF8, token);
        string exe = Path.Combine(dir, "esercizio");
        var psi = new ProcessStartInfo("/usr/bin/xcrun")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("clang++"); psi.ArgumentList.Add("-std=c++17"); psi.ArgumentList.Add("-Wall");
        psi.ArgumentList.Add("-Wextra"); psi.ArgumentList.Add(main); psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(exe);
        try
        {
            using Process p = Process.Start(psi)!;
            string stdout = await p.StandardOutput.ReadToEndAsync(token);
            string stderr = await p.StandardError.ReadToEndAsync(token);
            await p.WaitForExitAsync(token);
            string all = (stdout + stderr).Trim();
            return new CompileResult(p.ExitCode == 0, all, p.ExitCode == 0 ? exe : null, dir);
        }
        catch (Exception ex)
        {
            return new CompileResult(false, "clang++ non disponibile. Installa gli strumenti Apple con: xcode-select --install\n\n" + ex.Message, null, dir);
        }
    }

    public async Task<RunResult> RunAsync(string executable, string input, CancellationToken token)
    {
        var psi = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using Process p = Process.Start(psi)!;
        if (!string.IsNullOrEmpty(input)) { await p.StandardInput.WriteAsync(input); await p.StandardInput.FlushAsync(); }
        p.StandardInput.Close();
        string stdout = await p.StandardOutput.ReadToEndAsync(token);
        string stderr = await p.StandardError.ReadToEndAsync(token);
        await p.WaitForExitAsync(token);
        return new RunResult(p.ExitCode, stdout, stderr);
    }

    private static string SafeHeaderName(string name) => string.IsNullOrWhiteSpace(name) || !name.EndsWith(".h", StringComparison.OrdinalIgnoreCase) ? "esercizio.h" : Path.GetFileName(name);
}
