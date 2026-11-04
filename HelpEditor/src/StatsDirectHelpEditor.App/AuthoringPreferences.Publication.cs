using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

internal static partial class AuthoringPreferences
{
    internal static HelpPublicationOptions ReadHelpPublication(string workspaceRoot)
    {
        JsonObject? saved = null;
        try { saved = Read(workspaceRoot)["helpPublication"] as JsonObject; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { }
        string Value(string key, string fallback = "") => saved?[key] is JsonValue value &&
            value.TryGetValue<string>(out var text) ? text : fallback;
        bool Flag(string key) => saved?[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
        return new HelpPublicationOptions
        {
            WebsiteZipPath = ReadWebsiteZipPath(workspaceRoot, out _),
            StatsDirectSource = ReadStatsDirectSourceRoot(workspaceRoot),
            GitHubRepositoryUrl = Value("repositoryUrl", "https://github.com/iain-buchan/statisticalhelp"),
            GitHubBranch = Value("branch", "master"),
            AuthorName = Value("authorName"), AuthorEmail = Value("authorEmail"),
            AllowDefaultBranchPublication = Flag("allowDefaultBranchPublication"),
            PushToGitHub = true,
            JournalDirectory = Path.Combine(workspaceRoot, "Publishing")
        };
    }

    internal static void SaveHelpPublication(string workspaceRoot, HelpPublicationOptions options)
    {
        var preferences = Read(workspaceRoot);
        var saved = preferences["helpPublication"] as JsonObject ?? new JsonObject();
        saved["repositoryUrl"] = options.GitHubRepositoryUrl;
        saved["branch"] = options.GitHubBranch;
        saved["authorName"] = options.AuthorName;
        saved["authorEmail"] = options.AuthorEmail;
        saved["allowDefaultBranchPublication"] = options.AllowDefaultBranchPublication;
        preferences["helpPublication"] = saved;
        preferences[WebsiteZipKey] = Path.GetFullPath(options.WebsiteZipPath);
        preferences["statsDirectSourceRoot"] = Path.GetFullPath(options.StatsDirectSource);
        string path = Path.Combine(workspaceRoot, RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, ".authoring-preferences-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temporary, preferences.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) +
            Environment.NewLine, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }
}
