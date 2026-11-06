using System.Text.Json;

namespace StatsDirect.HelpEditor.Core;

/// <summary>Separates installed program files from each author's writable work.</summary>
public sealed record HelpEditorWorkspace(string RootPath, string DefaultProjectPath, string? SeedProjectPath)
{
    public const string InstallMarkerFile = ".statsdirect-help-editor-install.json";
    public const string WorkspaceLocationFile = "Settings/workspace-location.json";

    public static HelpEditorWorkspace Resolve(string applicationDirectory, string? localApplicationData = null)
    {
        string application = Path.GetFullPath(applicationDirectory);
        string marker = Path.Combine(application, InstallMarkerFile);
        if (File.Exists(marker))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(marker));
            if (!document.RootElement.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != 1)
                throw new InvalidDataException("The Help Editor installation layout is not supported. Repair the installation.");
            string local = localApplicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) throw new IOException("The current user's local application-data folder is unavailable.");
            string workspace = Path.Combine(Path.GetFullPath(local), "StatsDirect", "HelpEditor");
            return ReadInstalledWorkspaceLocation(workspace, application)
                ?? new(workspace, Path.Combine(workspace, "HelpSource"), Path.Combine(application, "HelpSource"));
        }
        // Developer and isolated-test layouts retain their nearest source root.
        for (DirectoryInfo? directory = new(application); directory != null; directory = directory.Parent)
            foreach (string folder in new[] { "HelpSource", "SampleProject" })
                if (Directory.Exists(Path.Combine(directory.FullName, folder)))
                    return new(directory.FullName, Path.Combine(directory.FullName, folder), null);
        string fallback = Path.GetFullPath(Path.Combine(application, "..", "..", "..", "..", ".."));
        return new(fallback, Path.Combine(fallback, "SampleProject"), null);
    }

    private static HelpEditorWorkspace? ReadInstalledWorkspaceLocation(string defaultWorkspace, string application)
    {
        string path = Path.Combine(defaultWorkspace, WorkspaceLocationFile);
        SafePaths.CheckNoLinks(path);
        string text;
        try { text = File.ReadAllText(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        try
        {
            using var json = JsonDocument.Parse(text);
            var settings = json.RootElement;
            if (settings.ValueKind != JsonValueKind.Object || settings.EnumerateObject().Count() != 2 ||
                !settings.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out int schema) || schema != 1 ||
                !settings.TryGetProperty("workspaceRoot", out var value) || value.ValueKind != JsonValueKind.String ||
                !Path.IsPathFullyQualified(value.GetString()!))
                throw new JsonException("Expected schema version 1 and an absolute workspaceRoot.");
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.GetString()!));
            if (SafePaths.IsWithin(root, application) || SafePaths.IsWithin(application, root))
                throw new InvalidDataException("The authoring workspace must be separate from the installed program.");
            SafePaths.CheckNoLinks(Path.Combine(root, "TutorSource"));
            var project = HelpProject.Open(Path.Combine(root, "HelpSource"));
            return new(root, project.RootPath, null);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException("The workspace location is invalid. Check " + path + " for an absolute workspaceRoot.", error);
        }
    }

    /// <summary>First run copies the installed seed once; upgrades never replace an author's work.</summary>
    public void PrepareDefaultProject()
    {
        if (SeedProjectPath == null) return;
        SafePaths.CheckNoLinks(RootPath);
        SafePaths.CheckNoLinks(DefaultProjectPath);
        if (Directory.Exists(DefaultProjectPath)) return;
        if (File.Exists(DefaultProjectPath)) throw new IOException("The default help working-copy path is occupied by a file.");
        var seed = HelpProject.Open(SeedProjectPath);
        _ = seed.LoadContents();
        _ = seed.ListTopics();
        Directory.CreateDirectory(RootPath);
        string staging = Path.Combine(RootPath, ".seed-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            // Never seed Git credentials/configuration, recovery archives, output,
            // browser profiles or arbitrary files beside the authored help.
            foreach (string name in new[] { "Content", "Project", "RCode" })
            {
                string from = SafePaths.Resolve(SeedProjectPath, name);
                if (Directory.Exists(from)) CopyFolder(from, Path.Combine(staging, name), name == "Project");
            }
            foreach (string name in new[] { HelpProject.MarkerFile, "StatsDirect.flprj", "LICENSE", "README.md" })
            {
                string from = SafePaths.Resolve(SeedProjectPath, name);
                if (File.Exists(from)) CopyFile(from, Path.Combine(staging, name));
            }
            _ = HelpProject.Open(staging);
            try { Directory.Move(staging, DefaultProjectPath); }
            catch (IOException) when (Directory.Exists(DefaultProjectPath))
            {
                // Another first-run instance completed its atomic copy first.
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static void CopyFolder(string source, string destination, bool projectRoot = false)
    {
        SafePaths.CheckNoLinks(source);
        Directory.CreateDirectory(destination);
        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            if (projectRoot && (Path.GetFileName(entry).Equals("Users", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(entry).Equals("Destinations", StringComparison.OrdinalIgnoreCase))) continue;
            SafePaths.CheckNoLinks(entry);
            string target = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry)) CopyFolder(entry, target);
            else CopyFile(entry, target);
        }
    }

    private static void CopyFile(string source, string destination)
    {
        File.Copy(source, destination, false);
        File.SetAttributes(destination, File.GetAttributes(destination) & ~FileAttributes.ReadOnly);
    }
}
