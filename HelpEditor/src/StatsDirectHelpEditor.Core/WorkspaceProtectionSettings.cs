using System.Collections.ObjectModel;
using System.Text.Json;

namespace StatsDirect.HelpEditor.Core;

/// <summary>Additional machine-specific folders excluded from ordinary authoring/build operations.</summary>
public sealed class WorkspaceProtectionSettings
{
    public const string ConfigurationEnvironmentVariable = "STATSDIRECT_HELP_PROTECTION_CONFIG";
    public static string DefaultConfigurationPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatsDirect", "HelpEditor", "Settings", "workspace-protection.json");
    public static string CurrentConfigurationPath => Environment.GetEnvironmentVariable(ConfigurationEnvironmentVariable) is { } configured
        ? FullyQualified(configured) : FullyQualified(DefaultConfigurationPath);
    public IReadOnlyList<string> ProtectedPaths { get; }

    private WorkspaceProtectionSettings(IEnumerable<string> paths) => ProtectedPaths = new ReadOnlyCollection<string>(paths.ToArray());

    /// <summary>A missing local file adds no machine-specific restrictions. An invalid existing file always fails closed.</summary>
    public static WorkspaceProtectionSettings Load(string? configurationPath = null)
    {
        string path = FullyQualified(configurationPath ?? CurrentConfigurationPath);
        SafePaths.CheckNoLinks(path);
        if (Directory.Exists(path)) throw new InvalidDataException("The workspace protection configuration path is a directory.");
        string text;
        try { text = File.ReadAllText(path); }
        catch (FileNotFoundException) { return new([]); }
        catch (DirectoryNotFoundException) { return new([]); }
        if (text.Length > 100000) throw new InvalidDataException("The workspace protection configuration is too large.");
        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("schemaVersion", out var version) || !version.TryGetInt32(out int number) || number != 1 ||
                !root.TryGetProperty("protectedPaths", out var paths) || paths.ValueKind != JsonValueKind.Array || paths.GetArrayLength() > 100)
                throw new JsonException();
            var result = new List<string>();
            foreach (var item in paths.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) throw new JsonException();
                string folder = FullyQualified(item.GetString()!);
                if (result.Any(existing => SafePaths.Same(existing, folder))) throw new JsonException();
                result.Add(folder);
            }
            return new(result);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
        { throw new InvalidDataException("The workspace protection configuration is invalid. Restore its schemaVersion and absolute protectedPaths before continuing.", error); }
    }

    public bool IsProtected(string path) => ProtectedPaths.Any(folder => SafePaths.IsWithin(path, folder));
    public void RequireUnprotected(string path, string operation)
    {
        if (IsProtected(path)) throw new InvalidOperationException(operation + ": choose a folder outside this machine's configured protected paths.");
    }

    /// <summary>Choose local staging outside protected/source paths, without creating or modifying it.</summary>
    public string FindSafeTemporaryParent(params string[] additionalExcludedRoots)
    {
        foreach (string candidate in new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp") }.Distinct())
        {
            string parent = FullyQualified(candidate);
            if (IsProtected(parent) || additionalExcludedRoots.Any(root => SafePaths.IsWithin(parent, root))) continue;
            SafePaths.CheckNoLinks(parent);
            return parent;
        }
        throw new InvalidOperationException("Temporary export storage must be outside protected folders, help source and runtime assets.");
    }

    private static string FullyQualified(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32700 || !Path.IsPathFullyQualified(path) || path.Any(char.IsControl))
            throw new ArgumentException("Workspace protection paths must be fully qualified, without control characters.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
