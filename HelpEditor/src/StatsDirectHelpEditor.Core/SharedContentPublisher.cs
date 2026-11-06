using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace StatsDirect.HelpEditor.Core;

public sealed record SharedContentFile([property: JsonRequired] string Path, [property: JsonRequired] long Bytes, [property: JsonRequired] string Sha256);
public sealed record SharedContentProducer([property: JsonRequired] string Version, [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? SourceCommit);
public sealed record SharedContentArtifact
{
    public required string Path { get; init; }
    public required int FileCount { get; init; }
    public required IReadOnlyList<SharedContentFile> Files { get; init; }
    public int? TopicCount { get; init; }
    public int? SearchDocumentCount { get; init; }
    public int? ContextCount { get; init; }
    public int? LessonCount { get; init; }
    public int? QuestionCount { get; init; }
    public int? TrackCount { get; init; }
    public string? BankVersion { get; init; }
    public string? ReviewProofBinding { get; init; }
}
public sealed record SharedContentArtifacts(SharedContentArtifact Website, SharedContentArtifact DesktopHelp, SharedContentArtifact Tutor);
public sealed record SharedPublishedContentManifest
{
    public required int SchemaVersion { get; init; }
    public required string Format { get; init; }
    public required string Publisher { get; init; }
    public required string PublicationId { get; init; }
    public required DateTimeOffset PublishedAtUtc { get; init; }
    public required SharedContentProducer Producer { get; init; }
    public required string HelpSourceSha256 { get; init; }
    public required string TutorSourceSha256 { get; init; }
    public required SharedContentArtifacts Artifacts { get; init; }
}
public sealed record SharedContentVerification(string ManifestPath, string ManifestSha256, SharedPublishedContentManifest Manifest);

/// <summary>Portable, fully inventoried reader help and built-in tutor inputs. Never imports a learner portfolio.</summary>
public static partial class SharedContentPublisher
{
    public const string ManifestRelativePath = "Published/content-manifest.json";
    public const string Format = "statsdirect-published-content-v1";
    public const string ReviewProofBinding = "authoring-source-before-generated-sync-v1";
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    internal static readonly string[] TutorNames = ["bank.mjs", "lessons.json", "questions.json", "tracks.json", "manifest.json", "README.md"];

    /// <summary>The caller pins the Git commit separately; an expected manifest hash binds an offline copy to that pin.</summary>
    public static SharedContentVerification Verify(string repositoryRoot, string? expectedManifestSha256 = null)
    {
        string root = Path.GetFullPath(repositoryRoot); SafePaths.CheckNoLinks(root);
        string path = SafePaths.Resolve(root, ManifestRelativePath);
        if (new FileInfo(path).Length > 20_000_000) throw new InvalidDataException("The shared content manifest is oversized.");
        string hash = FileHash(path);
        Require(expectedManifestSha256 == null || HashValid(expectedManifestSha256.ToLowerInvariant()) && hash.Equals(expectedManifestSha256, StringComparison.OrdinalIgnoreCase), "The shared manifest does not match its pinned hash.");
        string text=ReadText(path); using(var parsed=JsonDocument.Parse(text))
        {
            CheckDuplicateMembers(parsed.RootElement);
            Require(parsed.RootElement.TryGetProperty("publishedAtUtc",out var at) && at.ValueKind==JsonValueKind.String && Regex.IsMatch(at.GetString()!,@"T.*(?:Z|\+00:00)\z"),"Publication time needs an explicit UTC timezone.");
        }
        var manifest = JsonSerializer.Deserialize<SharedPublishedContentManifest>(text, Json) ?? throw new InvalidDataException("The shared manifest is empty.");
        Require(manifest.SchemaVersion == 1 && manifest.Format == Format && manifest.Publisher == "StatsDirectHelpEditor", "Unsupported shared content manifest.");
        Require(Guid.TryParseExact(manifest.PublicationId, "D", out var id) && id.ToString("D") == manifest.PublicationId, "Publication identity must be a canonical lowercase GUID.");
        Require(manifest.PublishedAtUtc != default && manifest.PublishedAtUtc.Offset == TimeSpan.Zero && manifest.Producer != null && !string.IsNullOrWhiteSpace(manifest.Producer.Version), "Publication provenance is missing.");
        Require(manifest.Producer.SourceCommit == null || Regex.IsMatch(manifest.Producer.SourceCommit, "\\A(?:[0-9a-f]{40}|[0-9a-f]{64})\\z"), "Producer source commit is invalid.");
        Require(HashValid(manifest.HelpSourceSha256) && HashValid(manifest.TutorSourceSha256), "Source fingerprints are invalid.");
        Require(manifest.Artifacts != null, "The three shared artifacts are required.");
        VerifyInventory(root, manifest.Artifacts.Website, "Docs"); VerifyInventory(root, manifest.Artifacts.DesktopHelp, "Published/DesktopHelp"); VerifyInventory(root, manifest.Artifacts.Tutor, "Published/Tutor");
        VerifyHelp(root, manifest.Artifacts.Website, "Public", manifest.HelpSourceSha256);
        VerifyHelp(root, manifest.Artifacts.DesktopHelp, "Desktop", manifest.HelpSourceSha256);
        VerifyTutor(SafePaths.Resolve(root, "Published/Tutor"), manifest.Artifacts.Tutor, manifest.TutorSourceSha256,SafePaths.Resolve(root,"Published/DesktopHelp"));
        var published = Files(SafePaths.Resolve(root, "Published"));
        var expected = manifest.Artifacts.DesktopHelp.Files.Select(row => "DesktopHelp/" + row.Path)
            .Concat(manifest.Artifacts.Tutor.Files.Select(row => "Tutor/" + row.Path)).Append("content-manifest.json").ToHashSet(StringComparer.Ordinal);
        if (published.ContainsKey(".gitattributes")) { Require(File.ReadAllText(SafePaths.Resolve(root, "Published/.gitattributes")) == "* -text\n", "Shared Git attributes are not publisher-owned."); expected.Add(".gitattributes"); }
        Require(expected.SetEquals(published.Keys), "Published contains missing, extra or private files.");
        Require(hash == FileHash(path), "The shared manifest changed during verification.");
        return new(path, hash, manifest);
    }

    internal static SharedContentVerification WriteManifest(string root, string publicationId, string helpSha, string tutorSha,
        BuildResult website, DesktopHelpPublishResult desktop, TutorBuiltinPublishResult tutor)
    {
        SharedContentArtifact Artifact(string path) { var rows = Inventory(SafePaths.Resolve(root, path)); return new() { Path = path, FileCount = rows.Count, Files = rows }; }
        string? commit = null; string version = typeof(SharedContentPublisher).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(SharedContentPublisher).Assembly.GetName().Version?.ToString() ?? "unknown";
        var revision = Regex.Match(version, @"(?:\+|\.)([0-9a-f]{40}|[0-9a-f]{64})\z"); if(revision.Success) commit=revision.Groups[1].Value;
        string info = Path.Combine(AppContext.BaseDirectory, "build-info.json");
        if (File.Exists(info)) { using var data = JsonDocument.Parse(File.ReadAllText(info)); foreach (var property in data.RootElement.EnumerateObject()) if (property.Name.Equals("sourceCommit", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String) commit = property.Value.GetString(); }
        var manifest = new SharedPublishedContentManifest
        {
            SchemaVersion = 1, Format = Format, Publisher = "StatsDirectHelpEditor", PublicationId = publicationId, PublishedAtUtc = DateTimeOffset.UtcNow,
            Producer = new(version, commit), HelpSourceSha256 = helpSha, TutorSourceSha256 = tutorSha,
            Artifacts = new(Artifact("Docs") with { TopicCount = website.TopicCount, SearchDocumentCount = website.SearchDocumentCount },
                Artifact("Published/DesktopHelp") with { TopicCount = desktop.TopicCount, SearchDocumentCount = desktop.SearchDocumentCount, ContextCount = desktop.ContextCount },
                Artifact("Published/Tutor") with { LessonCount = tutor.LessonCount, QuestionCount = tutor.QuestionCount, TrackCount = JsonNode.Parse(File.ReadAllText(SafePaths.Resolve(root,"Published/Tutor/tracks.json")))!.AsObject().Count, BankVersion = tutor.BankVersion, ReviewProofBinding = ReviewProofBinding })
        };
        SafePaths.AtomicWriteLf(SafePaths.Resolve(root, ManifestRelativePath), JsonSerializer.Serialize(manifest, Json) + "\n");
        return Verify(root);
    }

    internal static IReadOnlyList<SharedContentFile> Inventory(string root) => Files(root).Select(pair => new SharedContentFile(pair.Key, new FileInfo(SafePaths.Resolve(root, pair.Key)).Length, pair.Value)).ToArray();
    internal static Dictionary<string,string> Files(string root)
    {
        var result = new Dictionary<string,string>(StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        void Walk(string folder) { SafePaths.CheckNoLinks(folder); foreach (string file in Directory.EnumerateFiles(folder)) { string relative = Path.GetRelativePath(root,file).Replace('\\','/'); Require(ValidPath(relative) && seen.Add(relative), "Unsafe or case-colliding shared file."); Require(new FileInfo(file).Length <= 100_000_000, "A shared asset is oversized."); result.Add(relative,FileHash(file)); if (result.Count > 30000) throw new InvalidDataException("Too many shared files."); } foreach(string child in Directory.EnumerateDirectories(folder)) { SafePaths.CheckNoLinks(child); Walk(child); } }
        Walk(root); return result.OrderBy(pair=>pair.Key,StringComparer.Ordinal).ToDictionary(pair=>pair.Key,pair=>pair.Value,StringComparer.Ordinal);
    }
    private static void VerifyInventory(string root, SharedContentArtifact artifact, string expectedPath)
    {
        Require(artifact != null && artifact.Path == expectedPath && artifact.Files != null && artifact.FileCount > 0 && artifact.FileCount == artifact.Files.Count, "Shared artifact identity/count is invalid.");
        var actual = Files(SafePaths.Resolve(root, expectedPath)); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Require(actual.Count == artifact.FileCount, "The shared artifact inventory is incomplete.");
        foreach(var file in artifact.Files) Require(file != null && ValidPath(file.Path) && seen.Add(file.Path) && file.Bytes >= 0 && HashValid(file.Sha256) && actual.TryGetValue(file.Path,out string? hash) && hash == file.Sha256 && new FileInfo(SafePaths.Resolve(root,expectedPath+"/"+file.Path)).Length == file.Bytes, "A shared file differs from its recorded hash/size.");
    }
    private static bool ValidPath(string path) => !string.IsNullOrEmpty(path) && !path.Contains('\\') && !path.Contains(':') && !path.Any(char.IsControl) && path.Split('/').All(part=>part is not ("" or "." or "..") &&
        !part.Contains(".publishing",StringComparison.OrdinalIgnoreCase) && !part.StartsWith(".env",StringComparison.OrdinalIgnoreCase) && !part.Contains("-profile",StringComparison.OrdinalIgnoreCase) &&
        !new[]{".git",".codex","Settings","TutorSource",".help-editor-recovery",".statsdirect-help-editor.json","auth.json","credentials.json","account-state.json","authoring-preferences.json"}.Contains(part,StringComparer.OrdinalIgnoreCase)) &&
        !new[]{".pfx",".p12",".pem",".key",".exe",".dll"}.Contains(System.IO.Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
    private static string ReadText(string path)=>new UTF8Encoding(false,true).GetString(File.ReadAllBytes(path)).TrimStart('\uFEFF');
    private static void CheckDuplicateMembers(JsonElement node)
    {
        if(node.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.Ordinal);foreach(var property in node.EnumerateObject()){Require(names.Add(property.Name),"Duplicate shared manifest JSON member.");CheckDuplicateMembers(property.Value);}}
        else if(node.ValueKind==JsonValueKind.Array)foreach(var item in node.EnumerateArray())CheckDuplicateMembers(item);
    }
    private static bool HashValid(string? text) => text != null && Regex.IsMatch(text,"\\A[0-9a-f]{64}\\z");
    internal static string FileHash(string path) { SafePaths.CheckNoLinks(path); using var stream=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static void Require([DoesNotReturnIf(false)] bool condition,string message) { if(!condition) throw new InvalidDataException(message); }
}
