using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StatsDirect.HelpEditor.Core;

public sealed record WebsiteZipExportResult(string ZipPath, int TopicCount, int SearchDocumentCount, int FileCount, long ZipBytes, IReadOnlyList<string> Warnings);

/// <summary>Rebuild the Website target and atomically export its reader files as a flat ZIP.</summary>
public static class WebsiteZipExporter
{
    public static WebsiteZipExportResult Export(HelpProject project, string zipPath, string runtimeAssetsDirectory,
        IReadOnlyDictionary<string, string>? topicOverrides = null, CancellationToken cancellationToken = default,
        string? publicationHistoryDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(zipPath) || !Path.GetExtension(zipPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a ZIP filename for the website export.", nameof(zipPath));
        string destination = Path.GetFullPath(zipPath), assets = Path.GetFullPath(runtimeAssetsDirectory);
        if (SafePaths.IsWithin(destination, project.RootPath) || SafePaths.IsWithin(destination, assets))
            throw new InvalidOperationException("Save the website ZIP outside its help source and application assets.");
        var protection = WorkspaceProtectionSettings.Load();
        CheckDestinationLinks(destination);
        if (Directory.Exists(destination)) throw new IOException("The ZIP filename is occupied by a folder.");
        cancellationToken.ThrowIfCancellationRequested();
        string defaultHistory = HelpPublicationPublisher.DefaultHistoryDirectory;
        var histories = new[] { defaultHistory, Path.Combine(Path.GetDirectoryName(defaultHistory)!, "Publishing"),
            Path.Combine(Path.GetDirectoryName(project.RootPath)!, "Publishing"), publicationHistoryDirectory }
            .OfType<string>().Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        RequireStandaloneDestination(destination, histories, protection);
        string? originalZipSha256 = File.Exists(destination) ? HelpPublicationPublisher.Hash(destination) : null;
        CheckContent(project.ContentPath);
        var drafts = topicOverrides?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        string inputs = InputStamp(project, assets, cancellationToken);
        string parent = Path.GetDirectoryName(destination)!;
        string tempParent = protection.FindSafeTemporaryParent(project.RootPath, assets);
        SafePaths.CheckNoLinks(tempParent);
        Directory.CreateDirectory(parent);
        CheckDestinationLinks(destination);
        string stage = Path.Combine(tempParent, "sdhe-website-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        string temporary = Path.Combine(parent, ".sdhe-website-" + Guid.NewGuid().ToString("N") + ".tmp");
        var warnings = new List<string>();
        bool committed = false;
        try
        {
            var built = HelpBuilder.Build(project, Path.Combine(stage, "Website"), assets, HelpBuildTarget.Public, drafts);
            warnings.AddRange(built.Warnings);
            cancellationToken.ThrowIfCancellationRequested();
            CheckContent(project.ContentPath);
            var files = CheckedFiles(built.OutputDirectory).Where(path => IncludeReaderFile(Path.GetRelativePath(built.OutputDirectory, path))).Order(StringComparer.Ordinal).ToArray();
            if (!File.Exists(Path.Combine(built.OutputDirectory, "index.html")) || built.TopicCount == 0) throw new InvalidDataException("The website build has no reader pages.");
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                byte[] buffer = new byte[81_920];
                foreach (string file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relative = Path.GetRelativePath(built.OutputDirectory, file).Replace('\\', '/');
                    var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                    using var input = File.OpenRead(file);
                    using var content = entry.Open();
                    int read;
                    while ((read = input.Read(buffer)) != 0) { cancellationToken.ThrowIfCancellationRequested(); content.Write(buffer, 0, read); }
                }
            }
            long bytes = new FileInfo(temporary).Length;
            var result = new WebsiteZipExportResult(destination, built.TopicCount, built.SearchDocumentCount, files.Length, bytes, warnings.AsReadOnly());
            if (inputs != InputStamp(project, assets, cancellationToken)) throw new InvalidOperationException("Saved help, Contents, settings or runtime assets changed during export. Retry to create a consistent website ZIP; the previous ZIP was preserved.");
            using var destinationLock = LockDestination(destination);
            RequireStandaloneDestination(destination, histories, WorkspaceProtectionSettings.Load());
            if ((File.Exists(destination) ? HelpPublicationPublisher.Hash(destination) : null) != originalZipSha256)
                throw new IOException("The selected ZIP changed during export. It was preserved.");
            cancellationToken.ThrowIfCancellationRequested();
            // Same-directory rename commits the complete archive. Nothing after
            // this point may turn a successful replacement into a reported failure.
            File.Move(temporary, destination, overwrite: originalZipSha256 != null);
            committed = true;
            return result;
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) { CheckDestinationLinks(temporary); File.Delete(temporary); }
                DeleteOwnedStage(stage, tempParent);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (committed) warnings.Add("The ZIP was saved, but temporary export files could not be removed: " + error.Message);
            }
        }
    }

    private static readonly HashSet<string> PrivateNames = new(".git .gitignore .gitattributes .gitmodules .help-editor-recovery .editor-profile .app-help-preview-profile .preview-profile auth.json credentials.json authoring-preferences.json".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static void CheckContent(string directory)
    {
        SafePaths.CheckNoLinks(directory);
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (PrivateNames.Contains(Path.GetFileName(entry))) throw new InvalidDataException("Move private authoring data outside Content before exporting: " + Path.GetFileName(entry));
            SafePaths.CheckNoLinks(entry);
            if (Directory.Exists(entry)) CheckContent(entry);
        }
    }
    private static IEnumerable<string> CheckedFiles(string directory, bool skipPersonal = false)
    {
        SafePaths.CheckNoLinks(directory);
        foreach (string file in Directory.EnumerateFiles(directory)) { SafePaths.CheckNoLinks(file); yield return file; }
        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            if (skipPersonal && (Path.GetFileName(child) is "Users" or "Destinations")) continue;
            foreach (string file in CheckedFiles(child, skipPersonal)) yield return file;
        }
    }
    private static string InputStamp(HelpProject project, string assets, CancellationToken cancellation)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (label, directory) in new[] { ("Content", project.ContentPath), ("Project", Path.Combine(project.RootPath, "Project")), ("Runtime", assets) })
        {
            if (!Directory.Exists(directory)) continue;
            foreach (string path in CheckedFiles(directory, skipPersonal: label == "Project").Order(StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                digest.AppendData(Encoding.UTF8.GetBytes(label + "/" + Path.GetRelativePath(directory, path).Replace('\\', '/') + "\0"));
                using var input = File.OpenRead(path);
                digest.AppendData(SHA256.HashData(input));
            }
        }
        return Convert.ToHexString(digest.GetHashAndReset());
    }
    private static bool IncludeReaderFile(string relative)
    {
        if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(PrivateNames.Contains)) return false;
        string name = Path.GetFileName(relative), extension = Path.GetExtension(relative);
        return name != ".statsdirect-help-output.json" && !extension.StartsWith(".fl", StringComparison.OrdinalIgnoreCase)
            && extension.ToLowerInvariant() is not (".props" or ".mcdic" or ".expect");
    }
    private static void RequireStandaloneDestination(string destination, string[] histories, WorkspaceProtectionSettings protection)
    {
        CheckReplacement(destination, protection);
        if (File.Exists(destination) && ContainsPublicationRecord(destination))
            throw new IOException("This is a publication ZIP. Choose another filename for the standalone export.");

        // Older journals also reserve a successfully delivered path whose
        // archive was moved or replaced. A planned target alone reserves nothing.
        foreach (string history in histories)
        {
            SafePaths.CheckNoLinks(history);
            if (!Directory.Exists(history)) continue;
            foreach (string file in Directory.EnumerateFiles(history, "*.json"))
            {
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _)) continue;
                SafePaths.CheckNoLinks(file);
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                using var journal = JsonDocument.Parse(stream);
                if (journal.RootElement.ValueKind == JsonValueKind.Object &&
                    journal.RootElement.TryGetProperty("Result", out var result) && result.ValueKind == JsonValueKind.Object &&
                    result.TryGetProperty("WebsiteZip", out var zip) && zip.ValueKind == JsonValueKind.Object &&
                    zip.TryGetProperty("ZipPath", out var path) && path.ValueKind == JsonValueKind.String &&
                    Path.IsPathFullyQualified(path.GetString()!) && SafePaths.Same(destination, path.GetString()!))
                    throw new IOException("This filename is recorded for a publication ZIP. Choose another filename for the standalone export.");
            }
        }
    }

    private static bool ContainsPublicationRecord(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.GetEntry(HelpPublicationPublisher.PublicationInfo) != null;
        }
        catch (InvalidDataException)
        {
            // An ordinary pre-existing file may not be a readable ZIP.
            return false;
        }
    }

    // Explicit transfers can create a new file in an artifact folder, but
    // cannot replace an existing protected artifact or use a protected filename.
    internal static void CheckReplacement(string destination, WorkspaceProtectionSettings protection)
    {
        SafePaths.CheckNoLinks(destination);
        if (File.Exists(destination) || protection.ProtectedPaths.Any(path => SafePaths.Same(path, destination)))
            protection.RequireUnprotected(destination, "Save website ZIP");
    }

    // Both writers recheck and rename under one lock. Reuse the file rather
    // than unlinking it while another process could be opening it.
    internal static FileStream LockDestination(string destination)
    {
        string path = Path.Combine(Path.GetDirectoryName(destination)!, ".statsdirect-help-zip.lock");
        SafePaths.CheckNoLinks(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static void CheckDestinationLinks(string path)
    {
        SafePaths.CheckNoLinks(path);
    }
    private static void DeleteOwnedStage(string stage, string expectedParent)
    {
        string name = Path.GetFileName(stage);
        if (!SafePaths.Same(Path.GetDirectoryName(stage)!, expectedParent) || !name.StartsWith("sdhe-website-", StringComparison.Ordinal)
            || !Guid.TryParseExact(name[13..], "N", out _)) throw new IOException("Temporary export ownership could not be verified.");
        // Walk this freshly created GUID tree only; never follow a linked entry.
        void Remove(string directory)
        {
            SafePaths.CheckNoLinks(directory);
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                SafePaths.CheckNoLinks(file);
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                File.Delete(file);
            }
            foreach (string child in Directory.EnumerateDirectories(directory)) Remove(child);
            Directory.Delete(directory);
        }
        if (Directory.Exists(stage)) Remove(stage);
    }
}
