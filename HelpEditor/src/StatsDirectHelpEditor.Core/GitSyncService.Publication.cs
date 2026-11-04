using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StatsDirect.HelpEditor.Core;

public sealed record GitPublicationLayout(string GitRoot, string SourceRelativePrefix)
{
    internal string? LocalTestRemote { get; init; }
    internal string? ExpectedOriginUrl { get; init; }
    internal string? ExpectedBranch { get; init; }
    internal string? BaseCommit { get; init; }
}
public sealed record GitPublicationCheckout(HelpProject Project, GitPublicationLayout Layout, string Branch, string BaseCommit);

public sealed partial class GitSyncService
{
    private readonly HelpProject project;
    private readonly string RepositoryRoot;
    private readonly string SourcePrefix;
    private readonly bool publicationLayout;
    private bool ScopedSourcePrivacy => publicationLayout || SourcePrefix.Length != 0;
    private readonly string? localTestRemote;
    private readonly string? expectedPublicationOrigin;
    private readonly string? expectedPublicationBranch;
    private readonly string? publicationBaseCommit;
    private string? reviewedPublicationCommit;
    private IReadOnlyDictionary<string, string>? TransportEnvironment => localTestRemote == null ? null : new Dictionary<string, string> { ["GIT_ALLOW_PROTOCOL"] = "file" };
    private string[] ScopedSourcePaths => SourcePaths.Select(path => SourcePrefix.Length == 0 ? path : SourcePrefix + "/" + path).ToArray();

    public GitSyncService(HelpProject project) : this(project, OrdinaryLayout(project), false) { }
    public GitSyncService(HelpProject project, GitPublicationLayout layout) : this(project, layout, true) { }
    private GitSyncService(HelpProject project, GitPublicationLayout layout, bool publicationLayout)
    {
        this.project = project ?? throw new ArgumentNullException(nameof(project));
        ArgumentNullException.ThrowIfNull(layout);
        RepositoryRoot = Path.GetFullPath(layout.GitRoot).TrimEnd(Path.DirectorySeparatorChar);
        SafePaths.CheckNoLinks(RepositoryRoot);
        SourcePrefix = (layout.SourceRelativePrefix ?? throw new ArgumentException("A source-relative prefix is required.")).Replace('\\', '/');
        if (SourcePrefix.Length != 0 && (SourcePrefix.StartsWith('/') || SourcePrefix.EndsWith('/') || SourcePrefix.Contains(':') ||
            SourcePrefix.Split('/').Any(part => part is "" or "." or ".." || part.Equals(".git", StringComparison.OrdinalIgnoreCase)) ||
            SourcePrefix.Equals("Docs", StringComparison.OrdinalIgnoreCase) || SourcePrefix.StartsWith("Docs/", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("The source prefix must be a contained relative directory, outside Docs and .git.");
        string expected = SourcePrefix.Length == 0 ? RepositoryRoot : SafePaths.Resolve(RepositoryRoot, SourcePrefix);
        if (!SafePaths.Same(expected, project.RootPath)) throw new ArgumentException("The marked help project does not match the Git publication source prefix.");
        this.publicationLayout = publicationLayout;
        localTestRemote = layout.LocalTestRemote;
        expectedPublicationOrigin = layout.ExpectedOriginUrl;
        expectedPublicationBranch = layout.ExpectedBranch;
        publicationBaseCommit = layout.BaseCommit;
    }

    private static GitPublicationLayout OrdinaryLayout(HelpProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var directory = new DirectoryInfo(project.RootPath);
        if (directory.Name.Equals("HelpSource", StringComparison.OrdinalIgnoreCase) &&
            directory.Parent?.Name.Equals("HelpEditor", StringComparison.OrdinalIgnoreCase) == true && directory.Parent.Parent is { } root &&
            (Directory.Exists(Path.Combine(root.FullName, ".git")) || File.Exists(Path.Combine(root.FullName, ".git"))))
            return new(root.FullName, Path.GetRelativePath(root.FullName, project.RootPath).Replace('\\', '/'));
        return new(project.RootPath, "");
    }

    private static string CloneSourcePrefix(string destination)
    {
        bool HasProject(string path) => Directory.Exists(Path.Combine(path, "Content")) && File.Exists(Path.Combine(path, "Project/TOCs/STATSDIRECT.fltoc"));
        return HasProject(Path.Combine(destination, "HelpEditor/HelpSource")) ? "HelpEditor/HelpSource" : HasProject(destination) ? "" :
            throw new InvalidDataException("The selected branch has neither HelpEditor/HelpSource nor a root help source.");
    }

    public static async Task<GitAuthorIdentity> ReadDefaultAuthorIdentityAsync(CancellationToken ct = default)
    {
        string cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var name = await RunAsync(cwd, ["config", "--global", "--get", "user.name"], ct, allowFailure: true);
        var email = await RunAsync(cwd, ["config", "--global", "--get", "user.email"], ct, allowFailure: true);
        return new(name.ExitCode == 0 ? name.Output.Trim() : "", email.ExitCode == 0 ? email.Output.Trim() : "");
    }

    private bool TrySourceRelative(string path, out string relative)
    {
        path = path.Replace('\\', '/');
        if (SourcePrefix.Length == 0) { relative = path; return true; }
        if (path.StartsWith(SourcePrefix + "/", StringComparison.Ordinal)) { relative = path[(SourcePrefix.Length + 1)..]; return true; }
        relative = ""; return false;
    }
    private bool IsScopedSourcePath(string path) => TrySourceRelative(path, out var relative) && IsSourcePath(relative) &&
        (!ScopedSourcePrivacy || !PrivateProjectPath(relative));
    private static bool PrivateProjectPath(string relative) => new[] { "Project/Users", "Project/Destinations" }
        .Any(prefix => relative.Equals(prefix, StringComparison.OrdinalIgnoreCase) || relative.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
    private string[] CommitPathSpecs(string[] paths) => !ScopedSourcePrivacy ? paths :
        [.. paths, .. new[] { "Project/Users", "Project/Destinations" }.Select(path => ":(exclude)" + (SourcePrefix.Length == 0 ? path : SourcePrefix + "/" + path) + "/**")];

    public async Task<string> ReadHeadCommitAsync(CancellationToken ct = default)
    {
        await RequireRepositoryAsync(ct);
        return (await RunAsync(RepositoryRoot, ["rev-parse", "HEAD"], ct)).Output.Trim();
    }

    public static Task<GitPublicationCheckout> ClonePublicationAsync(string url, string branch, string destination, CancellationToken ct = default)
    {
        // This public workflow never accepts local paths, embedded credentials,
        // protocol helpers, file URLs or arbitrary network hosts.
        if (Directory.Exists(url)) throw new ArgumentException("Publish help requires an HTTPS GitHub repository URL.");
        return ClonePublicationCoreAsync(ValidateCloneSource(url), branch, destination, ct);
    }

    /// <summary>Explicit isolated-test seam: accepts only an existing local bare remote, never a network URL.</summary>
    public static async Task<GitPublicationCheckout> CloneLocalPublicationForTestsAsync(string bareRemote, string branch, string destination, CancellationToken ct = default)
    {
        if (!Path.IsPathFullyQualified(bareRemote) || !Directory.Exists(bareRemote)) throw new ArgumentException("The test remote must be a local bare repository directory.");
        string local = Path.GetFullPath(bareRemote); SafePaths.CheckNoLinks(local);
        var bare = await RunAsync(local, ["rev-parse", "--is-bare-repository"], ct);
        if (bare.Output.Trim() != "true") throw new ArgumentException("The isolated test remote must be bare.");
        return await ClonePublicationCoreAsync(new Uri(local).AbsoluteUri, branch, destination, ct, local);
    }

    private static async Task<GitPublicationCheckout> ClonePublicationCoreAsync(string source, string branch, string destination, CancellationToken ct, string? localTestRemote = null)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.StartsWith('-') || branch.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("Choose a valid publication branch.");
        destination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        SafePaths.CheckNoLinks(destination); WorkspaceProtectionSettings.Load().RequireUnprotected(destination, "Clone help for publication");
        if (SafePaths.Same(destination, Path.GetPathRoot(destination)!)) throw new ArgumentException("Choose a private clone directory.");
        if (File.Exists(destination) || Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("The private publication clone must be new or empty.");
        string parent = Path.GetDirectoryName(destination)!; Directory.CreateDirectory(parent);
        var valid = await RunAsync(parent, ["check-ref-format", "--branch", branch], ct, allowFailure: true);
        if (valid.ExitCode != 0) throw new ArgumentException("Choose a valid publication branch.");
        await RunAsync(parent, ["clone", "--no-hardlinks", "--depth", "1", "--single-branch", "--branch", branch, "--", source, destination], ct, timeoutSeconds: 180,
            environment: localTestRemote == null ? null : new Dictionary<string, string> { ["GIT_ALLOW_PROTOCOL"] = "file" });
        string prefix = CloneSourcePrefix(destination);
        string helpRoot = prefix.Length == 0 ? destination : SafePaths.Resolve(destination, prefix);
        string head = (await RunAsync(destination, ["rev-parse", "HEAD"], ct)).Output.Trim();
        string remoteBase = (await RunAsync(destination, ["rev-parse", "refs/remotes/origin/" + branch], ct)).Output.Trim();
        if (head != remoteBase) throw new InvalidOperationException("The private clone's HEAD does not match the selected remote branch.");
        string marker = Path.Combine(helpRoot, HelpProject.MarkerFile);
        if (!File.Exists(marker)) SafePaths.AtomicWrite(marker, JsonSerializer.Serialize(new { prototypeWorkingCopy = true, sourceCommit = head, clonedBy = "StatsDirectHelpEditor publication" }));
        string exclude = Path.Combine(destination, ".git/info/exclude"); SafePaths.CheckNoLinks(exclude);
        Directory.CreateDirectory(Path.GetDirectoryName(exclude)!);
        string current = File.Exists(exclude) ? File.ReadAllText(exclude) : "";
        foreach (string local in new[] { HelpProject.MarkerFile, RecoveryDirectory + "/" })
        {
            string entry = "/" + (prefix.Length == 0 ? "" : prefix + "/") + local;
            if (!current.Split('\n').Any(line => line.Trim() == entry)) { File.AppendAllText(exclude, Environment.NewLine + entry + Environment.NewLine, new UTF8Encoding(false)); current += "\n" + entry; }
        }
        return new(HelpProject.Open(helpRoot), new(destination, prefix) { LocalTestRemote = localTestRemote, ExpectedOriginUrl = source, ExpectedBranch = branch, BaseCommit = head }, branch, head);
    }

    /// <summary>Consume an independently verified Public build without another compilation.</summary>
    public async Task<WebsitePublication> PrepareWebsiteFromBuildAsync(string preparedOutputDirectory,
        IReadOnlyDictionary<string, string> expectedOutputFiles, IReadOnlyDictionary<string, string> expectedSourceFiles,
        int topicCount, int searchDocumentCount, IReadOnlyList<string> warnings, CancellationToken ct = default, bool allowWarnings = false)
    {
        if (!publicationLayout) throw new InvalidOperationException("Prepared publication requires an explicit Git publication layout.");
        if (topicCount <= 0 || searchDocumentCount < 0 || searchDocumentCount > topicCount || warnings.Count != 0 && !allowWarnings) throw new InvalidDataException("Review public-build counts and warnings before publishing.");
        if (expectedOutputFiles.Keys.Any(path => Path.GetFileName(path) == ".gitattributes")) throw new InvalidDataException("The prepared reader artifact must not supply Git publication rules.");
        preparedOutputDirectory = Path.GetFullPath(preparedOutputDirectory); SafePaths.CheckNoLinks(preparedOutputDirectory);
        void VerifyInputs()
        {
            var source = SourceFiles().ToDictionary(path => { TrySourceRelative(path, out string relative); return relative; }, path => FileHash(SafePaths.Resolve(RepositoryRoot, path)), StringComparer.Ordinal);
            AssertExpectedFiles(expectedSourceFiles, source, "The frozen source does not match the private publication clone.");
            var output = CheckedFiles(preparedOutputDirectory).ToDictionary(path => Path.GetRelativePath(preparedOutputDirectory, path).Replace('\\', '/'), FileHash, StringComparer.Ordinal);
            AssertExpectedFiles(expectedOutputFiles, output, "The prepared Public artifact changed before publication.");
        }
        VerifyInputs();
        var result = await PrepareWebsiteCoreAsync(async staging =>
        {
            if (Directory.Exists(staging) && Directory.EnumerateFileSystemEntries(staging).Any()) throw new IOException("Prepared website staging is already occupied.");
            VerifyInputs();
            foreach (string relative in expectedOutputFiles.Keys)
            {
                ct.ThrowIfCancellationRequested(); string target = SafePaths.Resolve(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(SafePaths.Resolve(preparedOutputDirectory, relative), target);
            }
            VerifyInputs();
            AssertExpectedFiles(expectedOutputFiles, CheckedFiles(staging).ToDictionary(path => Path.GetRelativePath(staging, path).Replace('\\', '/'), FileHash, StringComparer.Ordinal), "Public staging differs from the verified artifact.");
            await Task.CompletedTask;
            return new BuildResult(staging, topicCount, searchDocumentCount, warnings);
        }, ct, uniqueStaging: true);
        VerifyInputs(); return result;
    }

    private static void AssertExpectedFiles(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual, string message)
    {
        if (expected.Count == 0 || expected.Count != actual.Count || expected.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != expected.Count ||
            expected.Any(pair => !Regex.IsMatch(pair.Value, "\\A[0-9a-fA-F]{64}\\z") || !actual.TryGetValue(pair.Key, out string? observed) || !pair.Value.Equals(observed, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(message);
    }

    private static bool HasPublicationAttributes(string docs)
    {
        string path = SafePaths.Resolve(docs, ".gitattributes");
        return File.Exists(path) && File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal) == "* -text\n";
    }

    private static void RequirePublicationAttributes(string docs)
    {
        if (!Directory.Exists(docs)) return;
        foreach (string path in CheckedFiles(docs).Where(path => Path.GetFileName(path) == ".gitattributes"))
            if (Path.GetRelativePath(docs, path).Replace('\\', '/') != ".gitattributes" ||
                File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal) != "* -text\n")
                throw new InvalidOperationException("Docs contains custom Git attribute rules. Review them separately before publishing; they were not overwritten.");
    }

    private async Task VerifyRawPublicationFilesAsync(CancellationToken ct, IReadOnlyDictionary<string, string>? environment = null, string? commit = null)
    {
        string docs = SafePaths.Resolve(RepositoryRoot, WebsitePath); RequirePublicationAttributes(docs);
        string[] paths = SourceFiles().Concat(WebsiteFiles(docs).Keys.Append(WebsiteReceiptName).Select(path => WebsitePath + "/" + path)).Order(StringComparer.Ordinal).ToArray();
        string Quote(string path) => "\"" + string.Concat(path.Select(character => character switch
        {
            '\\' => "\\\\", '"' => "\\\"", < ' ' or '\u007f' => "\\" + Convert.ToString((int)character, 8).PadLeft(3, '0'), _ => character.ToString()
        })) + "\"";
        var raw = await RunAsync(RepositoryRoot, ["hash-object", "--no-filters", "--stdin-paths"], ct,
            environment: environment, input: string.Join('\n', paths.Select(Quote)) + "\n");
        string[] rawIds = raw.Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, string> indexed;
        if (commit == null)
            indexed = (await IndexEntriesAsync(environment ?? new Dictionary<string, string>(), ct))
                .ToDictionary(pair => pair.Key, pair => pair.Value.Split(' ', 3)[1], StringComparer.Ordinal);
        else
        {
            var tree = await RunAsync(RepositoryRoot, ["ls-tree", "-r", "-z", "--full-tree", commit, "--", .. ScopedSourcePaths, WebsitePath], ct);
            indexed = tree.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => IsScopedSourcePath(line[(line.IndexOf('\t') + 1)..]) || IsWebsitePath(line[(line.IndexOf('\t') + 1)..]))
                .ToDictionary(line => line[(line.IndexOf('\t') + 1)..], line => line[..line.IndexOf('\t')].Split(' ')[2], StringComparer.Ordinal);
        }
        if (rawIds.Length != paths.Length || paths.Where((path, index) => !indexed.TryGetValue(path, out string? oid) || oid != rawIds[index]).Any())
            throw new InvalidOperationException("Git would change the frozen source or website bytes. Preserve authored inputs with -text attributes and rebuild before publishing.");
    }

}
