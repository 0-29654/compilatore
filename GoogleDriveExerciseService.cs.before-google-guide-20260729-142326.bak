using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.IO;

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
    DateTime SavedAtLocal);

internal sealed record GoogleDriveSaveResult(string FileId, string FileName);

internal static class GoogleDriveExerciseService
{
    private const string AppFolderName = "CV+ Compilatore Alunno";
    private const string OAuthFileName = "google_oauth_client.json";

    public static async Task<GoogleDriveSaveResult> SaveExerciseAsync(
        GoogleDriveExerciseSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        string credentialsPath = FindCredentialsPath();

        await using FileStream credentialsStream = File.OpenRead(credentialsPath);
        GoogleClientSecrets secrets = GoogleClientSecrets.FromStream(credentialsStream);

        string tokenDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CVPlusCompilatoreAlunno",
            "GoogleDriveAuth");

        Directory.CreateDirectory(tokenDirectory);

        UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets.Secrets,
            new[] { DriveService.Scope.DriveFile },
            "cvplus-current-windows-user",
            cancellationToken,
            new FileDataStore(tokenDirectory, true));

        using var drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "CV+ Compilatore Alunno"
        });

        string parentFolderId = await GetOrCreateFolderAsync(drive, AppFolderName, cancellationToken);
        string fileName = BuildArchiveFileName(snapshot);
        await using MemoryStream archive = BuildArchive(snapshot);

        var metadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = fileName,
            Parents = new[] { parentFolderId },
            Description = $"Esercizio {snapshot.ExerciseNumber} - {snapshot.StudentName} - {snapshot.ClassName}"
        };

        FilesResource.CreateMediaUpload upload = drive.Files.Create(metadata, archive, "application/zip");
        upload.Fields = "id,name";
        Google.Apis.Upload.IUploadProgress progress = await upload.UploadAsync(cancellationToken);

        if (progress.Status != Google.Apis.Upload.UploadStatus.Completed || upload.ResponseBody is null)
            throw new IOException(progress.Exception?.Message ?? "Il caricamento su Google Drive non è stato completato.");

        return new GoogleDriveSaveResult(upload.ResponseBody.Id, upload.ResponseBody.Name ?? fileName);
    }

    private static string FindCredentialsPath()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", OAuthFileName),
            Path.Combine(AppContext.BaseDirectory, OAuthFileName)
        };

        string? path = candidates.FirstOrDefault(File.Exists);
        if (path is not null)
            return path;

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
        if (!string.IsNullOrWhiteSpace(existingId))
            return existingId;

        var folder = new Google.Apis.Drive.v3.Data.File
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
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(zip, "main.cpp", snapshot.MainCode);

            if (!string.IsNullOrWhiteSpace(snapshot.HeaderCode))
            {
                string safeHeader = SanitizeFileName(
                    string.IsNullOrWhiteSpace(snapshot.HeaderFileName) ? "esercizio.h" : snapshot.HeaderFileName);
                AddText(zip, safeHeader, snapshot.HeaderCode);
            }

            AddText(zip, "output_compilazione.txt", snapshot.CompileOutput);
            AddText(zip, "output_programma.txt", snapshot.ProgramOutput);

            string metadata = JsonSerializer.Serialize(new
            {
                snapshot.StudentId,
                snapshot.StudentName,
                snapshot.ClassName,
                snapshot.TaskType,
                snapshot.ExerciseNumber,
                savedAt = snapshot.SavedAtLocal.ToString("O")
            }, new JsonSerializerOptions { WriteIndented = true });

            AddText(zip, "informazioni_esercizio.json", metadata);
        }

        stream.Position = 0;
        return stream;
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
        writer.Write(content ?? string.Empty);
    }

    private static string BuildArchiveFileName(GoogleDriveExerciseSnapshot snapshot)
    {
        string student = SanitizeFileName(string.IsNullOrWhiteSpace(snapshot.StudentName) ? "Studente" : snapshot.StudentName);
        string className = SanitizeFileName(string.IsNullOrWhiteSpace(snapshot.ClassName) ? "Classe" : snapshot.ClassName);
        string timestamp = snapshot.SavedAtLocal.ToString("yyyyMMdd-HHmmss");
        return $"{className}_{student}_Esercizio-{snapshot.ExerciseNumber}_{timestamp}.zip";
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value.Trim().Replace(' ', '-');
    }
}
