using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CppStudentClient;

internal sealed class CppLibraryManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "header-only"; // header-only, static, dynamic
    public string Compiler { get; set; } = "mingw64";
    public string Architecture { get; set; } = "x64";
    public string CppStandard { get; set; } = "c++17";
    public List<string> IncludePaths { get; set; } = new() { "include" };
    public List<string> LibraryPaths { get; set; } = new();
    public List<string> Libraries { get; set; } = new();
    public List<string> LinkerOptions { get; set; } = new();
    public List<string> RuntimeFiles { get; set; } = new();
    public List<string> GuideFiles { get; set; } = new();
    public List<CppLibraryCompletion> Completions { get; set; } = new();
}

internal sealed class CppLibraryCompletion
{
    public string Trigger { get; set; } = "";
    public string Display { get; set; } = "";
    public string Insert { get; set; } = "";
    public string Description { get; set; } = "";
}

internal sealed record InstalledCppLibrary(CppLibraryManifest Manifest, string InstallDirectory);

internal static class CppLibraryManager
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CVPlus", "CppLibraries");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static IReadOnlyList<InstalledCppLibrary> LoadInstalled()
    {
        Directory.CreateDirectory(RootDirectory);
        var result = new List<InstalledCppLibrary>();
        foreach (string directory in Directory.EnumerateDirectories(RootDirectory))
        {
            string manifestPath = Path.Combine(directory, "manifest.json");
            try
            {
                if (!File.Exists(manifestPath)) continue;
                CppLibraryManifest? manifest = JsonSerializer.Deserialize<CppLibraryManifest>(File.ReadAllText(manifestPath), JsonOptions);
                if (manifest is null) continue;
                ValidateManifest(manifest);
                result.Add(new InstalledCppLibrary(manifest, directory));
            }
            catch
            {
                // Un pacchetto danneggiato non deve impedire l'avvio del compilatore.
            }
        }
        return result.OrderBy(x => x.Manifest.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static InstalledCppLibrary InstallPackage(string packagePath)
    {
        if (!File.Exists(packagePath)) throw new FileNotFoundException("Pacchetto non trovato.", packagePath);
        string extension = Path.GetExtension(packagePath);
        if (!extension.Equals(".cvplus", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selezionare un pacchetto .cvplus o .zip.");

        string staging = Path.Combine(Path.GetTempPath(), "CVPlusLibrary_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            ExtractSecurely(packagePath, staging);
            string? manifestPath = Directory.EnumerateFiles(staging, "manifest.json", SearchOption.AllDirectories)
                .OrderBy(x => x.Count(c => c == Path.DirectorySeparatorChar))
                .FirstOrDefault();
            // Compatibilita con i normali ZIP creati dall'utente: se non e presente
            // manifest.json, CV+ riconosce automaticamente header, .a, .dll/.dll.a e PDF.
            if (manifestPath is null)
                return InstallSimpleZip(staging, Path.GetFileNameWithoutExtension(packagePath));

            string packageRoot = Path.GetDirectoryName(manifestPath)!;
            CppLibraryManifest manifest = JsonSerializer.Deserialize<CppLibraryManifest>(File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new InvalidDataException("Manifest non valido.");
            ValidateManifest(manifest);
            ValidateFiles(manifest, packageRoot);

            string destination = Path.Combine(RootDirectory, SafeId(manifest.Id));
            Directory.CreateDirectory(RootDirectory);
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            CopyDirectory(packageRoot, destination);
            return new InstalledCppLibrary(manifest, destination);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
    }


    private static InstalledCppLibrary InstallSimpleZip(string extractedRoot, string fallbackName)
    {
        string[] allFiles = Directory.EnumerateFiles(extractedRoot, "*", SearchOption.AllDirectories).ToArray();
        string[] headers = allFiles.Where(x => x.EndsWith(".h", StringComparison.OrdinalIgnoreCase) ||
                                               x.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (headers.Length == 0)
            throw new InvalidDataException("Lo ZIP non contiene header .h/.hpp. Inserire almeno l'header necessario per usare la libreria.");

        string? dll = allFiles.FirstOrDefault(x => x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        string? staticArchive = allFiles.FirstOrDefault(x => x.EndsWith(".a", StringComparison.OrdinalIgnoreCase) &&
                                                          !x.EndsWith(".dll.a", StringComparison.OrdinalIgnoreCase));
        string? guide = allFiles.FirstOrDefault(x => x.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

        string displayName;
        string type;
        string? mainLibrary;
        string? importLibrary = null;

        if (dll is not null)
        {
            displayName = Path.GetFileNameWithoutExtension(dll);
            type = "dynamic";
            mainLibrary = dll;
            string stem = Path.GetFileNameWithoutExtension(dll);
            string[] expected = { "lib" + stem + ".dll.a", stem + ".dll.a", "lib" + stem + ".a" };
            importLibrary = allFiles.FirstOrDefault(x => expected.Any(e => Path.GetFileName(x).Equals(e, StringComparison.OrdinalIgnoreCase)));
            if (importLibrary is null)
                throw new InvalidDataException($"Nello ZIP e presente {Path.GetFileName(dll)}, ma manca la libreria di importazione MinGW lib{stem}.dll.a.");
        }
        else if (staticArchive is not null)
        {
            displayName = Path.GetFileNameWithoutExtension(staticArchive);
            if (displayName.StartsWith("lib", StringComparison.OrdinalIgnoreCase)) displayName = displayName[3..];
            type = "static";
            mainLibrary = staticArchive;
        }
        else
        {
            displayName = string.IsNullOrWhiteSpace(fallbackName) ? Path.GetFileNameWithoutExtension(headers[0]) : fallbackName;
            type = "header-only";
            mainLibrary = null;
        }

        string id = SafeId(displayName);
        if (id.Length == 0) id = "libreria-locale";
        string canonical = Path.Combine(Path.GetTempPath(), "CVPlusSimpleZip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(canonical);
        try
        {
            string include = Path.Combine(canonical, "include");
            Directory.CreateDirectory(include);
            foreach (string header in headers)
                File.Copy(header, Path.Combine(include, Path.GetFileName(header)), true);

            var manifest = new CppLibraryManifest
            {
                Id = id,
                Name = displayName,
                Version = "1.0.0",
                Description = "Libreria importata automaticamente da ZIP locale",
                Type = type,
                Compiler = "mingw64",
                Architecture = "x64",
                CppStandard = "c++17",
                IncludePaths = new List<string> { "include" }
            };

            if (type == "static" && mainLibrary is not null)
            {
                string libDir = Path.Combine(canonical, "lib", "x64");
                Directory.CreateDirectory(libDir);
                File.Copy(mainLibrary, Path.Combine(libDir, Path.GetFileName(mainLibrary)), true);
                string libName = Path.GetFileNameWithoutExtension(mainLibrary);
                manifest.LibraryPaths.Add("lib/x64");
                manifest.Libraries.Add(libName.StartsWith("lib", StringComparison.OrdinalIgnoreCase) ? libName[3..] : libName);
            }
            else if (type == "dynamic" && mainLibrary is not null && importLibrary is not null)
            {
                string binDir = Path.Combine(canonical, "bin", "x64");
                string libDir = Path.Combine(canonical, "lib", "x64");
                Directory.CreateDirectory(binDir);
                Directory.CreateDirectory(libDir);
                File.Copy(mainLibrary, Path.Combine(binDir, Path.GetFileName(mainLibrary)), true);
                File.Copy(importLibrary, Path.Combine(libDir, Path.GetFileName(importLibrary)), true);
                string stem = Path.GetFileNameWithoutExtension(mainLibrary);
                manifest.LibraryPaths.Add("lib/x64");
                manifest.Libraries.Add(stem);
                manifest.RuntimeFiles.Add("bin/x64/" + Path.GetFileName(mainLibrary));
            }

            if (guide is not null)
            {
                string guideDir = Path.Combine(canonical, "guides");
                Directory.CreateDirectory(guideDir);
                File.Copy(guide, Path.Combine(guideDir, Path.GetFileName(guide)), true);
                manifest.GuideFiles.Add("guides/" + Path.GetFileName(guide));
            }

            File.WriteAllText(Path.Combine(canonical, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            string destination = Path.Combine(RootDirectory, SafeId(manifest.Id));
            Directory.CreateDirectory(RootDirectory);
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            CopyDirectory(canonical, destination);
            return new InstalledCppLibrary(manifest, destination);
        }
        finally
        {
            try { if (Directory.Exists(canonical)) Directory.Delete(canonical, true); } catch { }
        }
    }

    public static InstalledCppLibrary InstallLooseLibrary(string libraryPath, IEnumerable<string> headerPaths, string? guidePath)
    {
        if (!File.Exists(libraryPath)) throw new FileNotFoundException("File libreria non trovato.", libraryPath);
        string ext = Path.GetExtension(libraryPath).ToLowerInvariant();
        if (ext == ".lib") throw new InvalidDataException("I file .lib di Microsoft Visual C++ non sono supportati direttamente. Usare una libreria MinGW .a oppure un pacchetto CV+.");
        if (ext is not (".a" or ".dll")) throw new InvalidDataException("Selezionare una libreria statica .a oppure una DLL .dll.");
        string[] headers = headerPaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (headers.Length == 0) throw new InvalidDataException("Selezionare almeno un file header .h o .hpp necessario per usare la libreria.");

        string displayName = Path.GetFileNameWithoutExtension(libraryPath);
        if (displayName.StartsWith("lib", StringComparison.OrdinalIgnoreCase)) displayName = displayName[3..];
        string id = SafeId(displayName.Replace(".dll", "", StringComparison.OrdinalIgnoreCase));
        string staging = Path.Combine(Path.GetTempPath(), "CVPlusLoose_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            string include = Path.Combine(staging, "include"); Directory.CreateDirectory(include);
            foreach (string header in headers) File.Copy(header, Path.Combine(include, Path.GetFileName(header)), true);
            var manifest = new CppLibraryManifest { Id=id, Name=displayName, Version="1.0.0", Description="Libreria importata da file locale", Type=ext==".dll"?"dynamic":"static", IncludePaths=new List<string>{"include"} };
            string lib = Path.Combine(staging, "lib", "x64"); Directory.CreateDirectory(lib);
            if (ext == ".a")
            {
                File.Copy(libraryPath, Path.Combine(lib, Path.GetFileName(libraryPath)), true);
                string file = Path.GetFileNameWithoutExtension(libraryPath);
                manifest.Libraries.Add(file.StartsWith("lib", StringComparison.OrdinalIgnoreCase) ? file[3..] : file);
            }
            else
            {
                string bin = Path.Combine(staging, "bin", "x64"); Directory.CreateDirectory(bin);
                File.Copy(libraryPath, Path.Combine(bin, Path.GetFileName(libraryPath)), true);
                string directory = Path.GetDirectoryName(libraryPath)!;
                string stem = Path.GetFileNameWithoutExtension(libraryPath);
                string[] candidates = { Path.Combine(directory, "lib" + stem + ".dll.a"), Path.Combine(directory, stem + ".dll.a"), Path.Combine(directory, "lib" + stem + ".a") };
                string? import = candidates.FirstOrDefault(File.Exists);
                if (import is null) throw new InvalidDataException("Per collegare la DLL serve anche la libreria di importazione MinGW (.dll.a) nella stessa cartella.");
                File.Copy(import, Path.Combine(lib, Path.GetFileName(import)), true);
                manifest.Libraries.Add(stem);
                manifest.RuntimeFiles.Add("bin/x64/" + Path.GetFileName(libraryPath));
            }
            manifest.LibraryPaths.Add("lib/x64");
            if (!string.IsNullOrWhiteSpace(guidePath) && File.Exists(guidePath))
            {
                string guides = Path.Combine(staging, "guides"); Directory.CreateDirectory(guides);
                File.Copy(guidePath, Path.Combine(guides, Path.GetFileName(guidePath)), true);
                manifest.GuideFiles.Add("guides/" + Path.GetFileName(guidePath));
            }
            File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            string destination = Path.Combine(RootDirectory, SafeId(manifest.Id));
            Directory.CreateDirectory(RootDirectory); if (Directory.Exists(destination)) Directory.Delete(destination, true);
            CopyDirectory(staging, destination);
            return new InstalledCppLibrary(manifest, destination);
        }
        finally { try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { } }
    }

    public static void Uninstall(string id)
    {
        string destination = Path.Combine(RootDirectory, SafeId(id));
        if (Directory.Exists(destination)) Directory.Delete(destination, true);
    }

    public static void UninstallAll()
    {
        try
        {
            if (Directory.Exists(RootDirectory))
                Directory.Delete(RootDirectory, true);
        }
        catch
        {
            // La pulizia non deve impedire avvio o chiusura dell'applicazione.
        }
    }

    public static string BuildCompilerArguments(IEnumerable<InstalledCppLibrary> libraries)
    {
        var args = new List<string>();
        foreach (InstalledCppLibrary library in libraries)
        {
            foreach (string relative in library.Manifest.IncludePaths)
            {
                string path = ResolveInside(library.InstallDirectory, relative);
                if (Directory.Exists(path)) args.Add($"-I\"{path}\"");
            }
            foreach (string relative in library.Manifest.LibraryPaths)
            {
                string path = ResolveInside(library.InstallDirectory, relative);
                if (Directory.Exists(path)) args.Add($"-L\"{path}\"");
            }
            foreach (string name in library.Manifest.Libraries)
            {
                string clean = name.Trim();
                if (clean.Length == 0) continue;
                args.Add(clean.StartsWith("-l", StringComparison.Ordinal) ? clean : "-l" + clean);
            }
            foreach (string option in library.Manifest.LinkerOptions)
            {
                if (!string.IsNullOrWhiteSpace(option)) args.Add(option.Trim());
            }
        }
        return string.Join(" ", args);
    }

    public static IReadOnlyList<string> GetGuideFiles(InstalledCppLibrary library)
    {
        var result = new List<string>();
        foreach (string relative in library.Manifest.GuideFiles)
        {
            string path = ResolveInside(library.InstallDirectory, relative);
            if (File.Exists(path) && Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) result.Add(path);
        }
        return result;
    }

    public static void CopyRuntimeFiles(IEnumerable<InstalledCppLibrary> libraries, string outputDirectory)
    {
        foreach (InstalledCppLibrary library in libraries)
        {
            foreach (string relative in library.Manifest.RuntimeFiles)
            {
                string source = ResolveInside(library.InstallDirectory, relative);
                if (!File.Exists(source)) continue;
                File.Copy(source, Path.Combine(outputDirectory, Path.GetFileName(source)), true);
            }
        }
    }

    public static string CreatePackage(
        string sourceDirectory,
        string outputFile,
        string name,
        string version,
        string type,
        string description,
        string compilerPath)
    {
        if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException("Cartella sorgente non trovata.");
        string id = SafeId(name);
        if (id.Length == 0) throw new InvalidDataException("Nome libreria non valido.");
        type = type.Trim().ToLowerInvariant();
        if (type is not ("header-only" or "static" or "dynamic")) throw new InvalidDataException("Tipo libreria non valido.");

        string staging = Path.Combine(Path.GetTempPath(), "CVPlusBuild_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            string include = Path.Combine(staging, "include");
            Directory.CreateDirectory(include);
            foreach (string header in Directory.EnumerateFiles(sourceDirectory, "*.*", SearchOption.AllDirectories)
                         .Where(x => x.EndsWith(".h", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)))
                File.Copy(header, Path.Combine(include, Path.GetFileName(header)), true);

            var manifest = new CppLibraryManifest
            {
                Id = id,
                Name = name.Trim(),
                Version = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version.Trim(),
                Description = description.Trim(),
                Type = type,
                Compiler = "mingw64",
                Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86",
                CppStandard = "c++17",
                IncludePaths = new List<string> { "include" }
            };

            if (type != "header-only")
            {
                if (!File.Exists(compilerPath)) throw new FileNotFoundException("Compilatore incorporato non trovato.", compilerPath);
                string[] sources = Directory.EnumerateFiles(sourceDirectory, "*.cpp", SearchOption.AllDirectories).ToArray();
                if (sources.Length == 0) throw new InvalidDataException("Per una libreria statica o dinamica serve almeno un file .cpp.");
                string binDir = Path.GetDirectoryName(compilerPath)!;
                string work = Path.Combine(staging, "build");
                string lib = Path.Combine(staging, "lib", "x64");
                Directory.CreateDirectory(work);
                Directory.CreateDirectory(lib);

                if (type == "static")
                {
                    var objects = new List<string>();
                    foreach (string source in sources)
                    {
                        string obj = Path.Combine(work, Path.GetFileNameWithoutExtension(source) + "_" + objects.Count + ".o");
                        Run(compilerPath, $"-std=c++17 -O2 -I\"{include}\" -c \"{source}\" -o \"{obj}\"", sourceDirectory, binDir);
                        objects.Add(obj);
                    }
                    string archive = Path.Combine(lib, "lib" + id + ".a");
                    string ar = Path.Combine(binDir, "ar.exe");
                    if (!File.Exists(ar)) throw new FileNotFoundException("ar.exe non trovato nel compilatore incorporato.", ar);
                    Run(ar, $"rcs \"{archive}\" " + string.Join(" ", objects.Select(x => $"\"{x}\"")), sourceDirectory, binDir);
                    manifest.LibraryPaths.Add("lib/x64");
                    manifest.Libraries.Add(id);
                }
                else
                {
                    string bin = Path.Combine(staging, "bin", "x64");
                    Directory.CreateDirectory(bin);
                    string dll = Path.Combine(bin, id + ".dll");
                    string import = Path.Combine(lib, "lib" + id + ".dll.a");
                    string sourceArgs = string.Join(" ", sources.Select(x => $"\"{x}\""));
                    Run(compilerPath, $"-std=c++17 -O2 -shared -I\"{include}\" {sourceArgs} -o \"{dll}\" -Wl,--out-implib,\"{import}\"", sourceDirectory, binDir);
                    manifest.LibraryPaths.Add("lib/x64");
                    manifest.Libraries.Add(id);
                    manifest.RuntimeFiles.Add("bin/x64/" + id + ".dll");
                }
            }

            File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            string finalPath = Path.ChangeExtension(outputFile, ".cvplus");
            if (File.Exists(finalPath)) File.Delete(finalPath);
            ZipFile.CreateFromDirectory(staging, finalPath, CompressionLevel.Optimal, false);
            return finalPath;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
    }

    private static void Run(string file, string arguments, string workingDirectory, string compilerBin)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        psi.Environment["PATH"] = compilerBin + Path.PathSeparator + (psi.Environment.TryGetValue("PATH", out string? p) ? p : "");
        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Impossibile avviare lo strumento di compilazione.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException((stderr + Environment.NewLine + stdout).Trim());
    }

    private static void ValidateManifest(CppLibraryManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id)) manifest.Id = SafeId(manifest.Name);
        manifest.Id = SafeId(manifest.Id);
        if (manifest.Id.Length == 0 || string.IsNullOrWhiteSpace(manifest.Name)) throw new InvalidDataException("Il manifest deve contenere id e name.");
        manifest.Type = manifest.Type.Trim().ToLowerInvariant();
        if (manifest.Type is not ("header-only" or "static" or "dynamic")) throw new InvalidDataException("Tipo supportato: header-only, static o dynamic.");
        if (!manifest.Compiler.Equals("mingw64", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Il pacchetto non è compatibile: è richiesto MinGW-w64.");
        if (!manifest.CppStandard.Equals("c++17", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Il pacchetto deve essere compatibile con C++17.");
        if (!manifest.Architecture.Equals("x64", StringComparison.OrdinalIgnoreCase) &&
            !manifest.Architecture.Equals("any", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Il pacchetto deve essere per architettura x64 oppure any.");
    }

    private static void ValidateFiles(CppLibraryManifest manifest, string root)
    {
        foreach (string relative in manifest.IncludePaths.Concat(manifest.LibraryPaths))
            _ = ResolveInside(root, relative);
        foreach (string relative in manifest.RuntimeFiles.Concat(manifest.GuideFiles))
        {
            string path = ResolveInside(root, relative);
            if (!File.Exists(path)) throw new InvalidDataException($"File runtime mancante: {relative}");
        }
    }

    private static string ResolveInside(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Percorso non sicuro nel manifest.");
        return full;
    }

    private static void ExtractSecurely(string zipPath, string destination)
    {
        string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Il pacchetto contiene percorsi non sicuri.");
            if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static string SafeId(string value) => Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9._-]+", "-").Trim('-');
}
