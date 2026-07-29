using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CppStudentClient;

internal sealed record GoogleDriveExerciseSnapshot(
    string StudentId,
    string StudentName,
    string ClassName,
    string TaskType,
    int ExerciseNumber,
    string MainCode,
    string HeaderCode,
    string HeaderFileName,
    string CompileOutput,
    string ProgramOutput,
    DateTime SavedAtLocal,
    string RequestedFileName,
    string CompilerDescription);

internal sealed record GoogleDriveSaveResult(string FileId, string FileName);

internal static class GoogleDriveExerciseService
{
    private const string AppFolderName = "CV+ Compilatore Alunno";
    private const string OAuthFileName = "google_oauth_client.json";
    private static UserCredential? _activeCredential;
    private static readonly SemaphoreSlim DisconnectGate = new(1, 1);

    public static string DriveFolderDisplayName => AppFolderName;

    public static async Task<GoogleDriveSaveResult> SaveExerciseAsync(
        GoogleDriveExerciseSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        string credentialsPath = FindCredentialsPath();

        await using FileStream credentialsStream = File.OpenRead(credentialsPath);
        GoogleClientSecrets secrets = GoogleClientSecrets.FromStream(credentialsStream);

        string tokenDirectory = GetTokenDirectory();
        Directory.CreateDirectory(tokenDirectory);

        UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets.Secrets,
            new[] { DriveService.Scope.DriveFile },
            "cvplus-current-windows-user",
            cancellationToken,
            new FileDataStore(tokenDirectory, true));

        _activeCredential = credential;

        using var drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "CV+ Compilatore Alunno"
        });

        string parentFolderId = await GetOrCreateFolderAsync(drive, AppFolderName, cancellationToken);
        bool hasHeader = !string.IsNullOrWhiteSpace(snapshot.HeaderCode);
        string fileName = BuildRequestedFileName(snapshot.RequestedFileName, hasHeader);

        Google.Apis.Drive.v3.Data.File metadata = new()
        {
            Name = fileName,
            Parents = new[] { parentFolderId },
            Description = $"Esercizio {snapshot.ExerciseNumber} - {snapshot.StudentName} - {snapshot.ClassName}"
        };

        FilesResource.CreateMediaUpload upload;
        if (hasHeader)
        {
            await using MemoryStream archive = BuildArchive(snapshot);
            upload = drive.Files.Create(metadata, archive, "application/zip");
            upload.Fields = "id,name";
            Google.Apis.Upload.IUploadProgress progress = await upload.UploadAsync(cancellationToken);
            EnsureUploadCompleted(progress, upload.ResponseBody);
        }
        else
        {
            string mainWithMetadata = AddExerciseHeaderComments(snapshot.MainCode, snapshot);
            await using MemoryStream cppStream = new(Encoding.UTF8.GetBytes(mainWithMetadata));
            upload = drive.Files.Create(metadata, cppStream, "text/x-c++src");
            upload.Fields = "id,name";
            Google.Apis.Upload.IUploadProgress progress = await upload.UploadAsync(cancellationToken);
            EnsureUploadCompleted(progress, upload.ResponseBody);
        }

        return new GoogleDriveSaveResult(
            upload.ResponseBody!.Id,
            upload.ResponseBody.Name ?? fileName);
    }

    private static void EnsureUploadCompleted(
        Google.Apis.Upload.IUploadProgress progress,
        Google.Apis.Drive.v3.Data.File? response)
    {
        if (progress.Status != Google.Apis.Upload.UploadStatus.Completed || response is null)
            throw new IOException(progress.Exception?.Message ?? "Il caricamento su Google Drive non è stato completato.");
    }

    public static async Task DisconnectAsync()
    {
        await DisconnectGate.WaitAsync();
        try
        {
            UserCredential? credential = _activeCredential;
            _activeCredential = null;

            if (credential is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await credential.RevokeTokenAsync(timeout.Token);
                }
                catch
                {
                    // La revoca remota è best-effort: la cache locale viene comunque eliminata.
                }
            }

            DeleteLocalTokenCache();
        }
        finally
        {
            DisconnectGate.Release();
        }
    }

    public static void DeleteLocalTokenCache()
    {
        string tokenDirectory = GetTokenDirectory();
        try
        {
            if (Directory.Exists(tokenDirectory))
                Directory.Delete(tokenDirectory, recursive: true);
        }
        catch
        {
            // Non bloccare l'avvio o la chiusura dell'applicazione.
        }
    }

    private static string GetTokenDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CVPlusCompilatoreAlunno",
        "GoogleDriveAuth");

    private static string FindCredentialsPath()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", OAuthFileName),
            Path.Combine(AppContext.BaseDirectory, OAuthFileName)
        };

        string? path = candidates.FirstOrDefault(File.Exists);
        if (path is not null) return path;

        throw new FileNotFoundException(
            "Google Drive non è ancora configurato dallo sviluppatore. " +
            "Inserire il file OAuth desktop scaricato da Google Cloud con il nome " +
            "Assets\\google_oauth_client.json, quindi ricostruire il setup.");
    }

    private static async Task<string> GetOrCreateFolderAsync(
        DriveService drive,
        string folderName,
        CancellationToken cancellationToken)
    {
        string escapedName = folderName.Replace("'", "\\'");
        FilesResource.ListRequest list = drive.Files.List();
        list.Q = $"name = '{escapedName}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        list.Spaces = "drive";
        list.Fields = "files(id,name)";
        list.PageSize = 10;

        Google.Apis.Drive.v3.Data.FileList existing = await list.ExecuteAsync(cancellationToken);
        string? existingId = existing.Files?.FirstOrDefault()?.Id;
        if (!string.IsNullOrWhiteSpace(existingId)) return existingId;

        Google.Apis.Drive.v3.Data.File folder = new()
        {
            Name = folderName,
            MimeType = "application/vnd.google-apps.folder"
        };
        FilesResource.CreateRequest create = drive.Files.Create(folder);
        create.Fields = "id";
        Google.Apis.Drive.v3.Data.File created = await create.ExecuteAsync(cancellationToken);
        return created.Id ?? throw new IOException("Google Drive non ha restituito l'ID della cartella creata.");
    }

    private static MemoryStream BuildArchive(GoogleDriveExerciseSnapshot snapshot)
    {
        MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(zip, "main.cpp", AddExerciseHeaderComments(snapshot.MainCode, snapshot));

            string safeHeader = SanitizeFileName(
                string.IsNullOrWhiteSpace(snapshot.HeaderFileName) ? "esercizio.h" : snapshot.HeaderFileName);
            AddText(zip, EnsureExtension(safeHeader, ".h"), AddExerciseHeaderComments(snapshot.HeaderCode, snapshot));

            AddText(zip, "output_compilazione.txt", snapshot.CompileOutput);
            AddText(zip, "output_programma.txt", snapshot.ProgramOutput);

            string metadata = JsonSerializer.Serialize(new
            {
                numeroRegistro = snapshot.StudentId,
                nomeCognome = snapshot.StudentName,
                classe = snapshot.ClassName,
                tipologia = snapshot.TaskType,
                numeroEsercizio = snapshot.ExerciseNumber,
                dataOra = snapshot.SavedAtLocal.ToString("O"),
                compilatore = snapshot.CompilerDescription
            }, new JsonSerializerOptions { WriteIndented = true });
            AddText(zip, "informazioni_esercizio.json", metadata);
        }
        stream.Position = 0;
        return stream;
    }

    private static string AddExerciseHeaderComments(string code, GoogleDriveExerciseSnapshot snapshot)
    {
        string info =
            "// ================================================\n" +
            "// CV+ Compilatore Alunno - Dati esercizio\n" +
            $"// Numero registro: {SafeComment(snapshot.StudentId)}\n" +
            $"// Nome e cognome: {SafeComment(snapshot.StudentName)}\n" +
            $"// Classe: {SafeComment(snapshot.ClassName)}\n" +
            $"// Tipologia: {SafeComment(snapshot.TaskType)}\n" +
            $"// Numero esercizio: {snapshot.ExerciseNumber}\n" +
            $"// Data: {snapshot.SavedAtLocal:dd/MM/yyyy}\n" +
            $"// Ora: {snapshot.SavedAtLocal:HH:mm:ss}\n" +
            $"// Compilatore: {SafeComment(snapshot.CompilerDescription)}\n" +
            "// ================================================\n\n";
        return info + (code ?? string.Empty);
    }

    private static string SafeComment(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Replace("\r", " ").Replace("\n", " ").Trim();

    private static void AddText(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream entryStream = entry.Open();
        using StreamWriter writer = new(entryStream, new UTF8Encoding(false));
        writer.Write(content ?? string.Empty);
    }

    private static string BuildRequestedFileName(string requested, bool hasHeader)
    {
        string fallback = hasHeader ? "esercizio-cvplus.zip" : "esercizio.cpp";
        string safe = SanitizeFileName(string.IsNullOrWhiteSpace(requested) ? fallback : requested.Trim());
        return EnsureExtension(safe, hasHeader ? ".zip" : ".cpp");
    }

    private static string EnsureExtension(string value, string extension)
    {
        if (!value.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) value += extension;
        return value;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        value = value.Trim();
        return string.IsNullOrWhiteSpace(value) ? "esercizio" : value;
    }
}
