using System.Text.Json.Nodes;

namespace StatsDirect.HelpEditor.App;

internal static partial class AuthoringPreferences
{
    private const string TutorExportDirectoryKey = "tutorCourseExportDirectory";

    internal static string ReadTutorExportDirectory(string workspaceRoot)
    {
        try
        {
            if (Read(workspaceRoot)[TutorExportDirectoryKey] is JsonValue value && value.TryGetValue<string>(out string? directory)
                && !string.IsNullOrWhiteSpace(directory) && Path.IsPathFullyQualified(directory) && Directory.Exists(directory))
                return Path.GetFullPath(directory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException) { }
        return DefaultDirectory;
    }

    internal static void SaveTutorExportDirectory(string workspaceRoot, string exportPath)
    {
        if (!Path.IsPathFullyQualified(exportPath) || !Path.GetExtension(exportPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a complete tutor course JSON filename.", nameof(exportPath));
        var preferences = Read(workspaceRoot);
        preferences[TutorExportDirectoryKey] = Path.GetDirectoryName(Path.GetFullPath(exportPath));
        WritePreferences(workspaceRoot, preferences);
    }
}
