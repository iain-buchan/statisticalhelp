using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StatsDirect.HelpEditor.App;

/// <summary>Per-workspace authoring choices, separate from help source and build inputs.</summary>
internal static class AuthoringPreferences
{
    internal const string RelativePath = "Settings/authoring-preferences.json";
    private const string WebsiteZipKey = "websiteZipPath";

    internal static string DefaultWebsiteZipPath => Path.Combine(DefaultDirectory, "StatsDirectHelp-Website.zip");
    internal static string DefaultDirectory => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) is { Length: > 0 } documents
        ? documents : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");

    internal static string ReadWebsiteZipPath(string workspaceRoot, out string? warning)
    {
        warning = null;
        try
        {
            var preferences = Read(workspaceRoot);
            if (preferences[WebsiteZipKey] is null) return DefaultWebsiteZipPath;
            if (preferences[WebsiteZipKey] is JsonValue value && value.TryGetValue<string>(out string? path) &&
                !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(path);
            warning = "The saved Website ZIP location is unavailable; Documents is suggested.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            warning = "The saved export preferences could not be read; Documents is suggested.";
        }
        return DefaultWebsiteZipPath;
    }

    internal static void SaveWebsiteZipPath(string workspaceRoot, string zipPath)
    {
        if (!Path.IsPathFullyQualified(zipPath) || !Path.GetExtension(zipPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a complete ZIP filename.", nameof(zipPath));
        var preferences = Read(workspaceRoot); // Retain settings saved by another editor window during export.
        preferences[WebsiteZipKey] = Path.GetFullPath(zipPath);
        string path = Path.Combine(workspaceRoot, RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, ".authoring-preferences-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temporary, preferences.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
        // Same-directory replacement keeps the old preferences intact if writing
        // fails. An interrupted temporary file is retained for inspection.
        File.Move(temporary, path, overwrite: true);
    }

    private static JsonObject Read(string workspaceRoot)
    {
        string path = Path.Combine(workspaceRoot, RelativePath);
        if (!File.Exists(path)) return new JsonObject();
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException("Expected an object."); }
        catch (JsonException exception) { throw new InvalidDataException("The authoring preferences could not be read. Correct " + path + " before exporting.", exception); }
    }
}
