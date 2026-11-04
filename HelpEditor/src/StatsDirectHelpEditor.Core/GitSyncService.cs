using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StatsDirect.HelpEditor.Core;

public record GitFileChange(string Status, string Path)
{
    public string? OriginalPath { get; init; }
    internal bool? SourceClassification { get; init; }
    public bool IsSource => SourceClassification ?? (GitSyncService.IsSourcePath(Path) || OriginalPath is not null && GitSyncService.IsSourcePath(OriginalPath));
}

public record GitAuthorIdentity(string Name, string Email);

public record GitStatus(bool IsRepository, string Branch, string? Remote, int Ahead, int Behind, IReadOnlyList<GitFileChange> Changes)
{
    /// <summary>When the local checkout was inspected. This is not a remote check.</summary>
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>The configured upstream; it may no longer exist on the remote.</summary>
    public string? TrackingBranch { get; init; }
    /// <summary>The local remote-tracking ref used for comparison, if one is available.</summary>
    public string? ComparisonBranch { get; init; }
    /// <summary>Whether Ahead and Behind were calculated successfully, rather than defaulting to zero.</summary>
    public bool HasComparison { get; init; }
    /// <summary>The last successful editor fetch for this origin, not a promise that the remote is unchanged.</summary>
    public DateTimeOffset? LastSuccessfulFetchUtc { get; init; }
    public bool HasTrackingBranch => TrackingBranch is not null;
    public int SourceChangeCount => Changes.Count(change => change.IsSource);
    public int OtherChangeCount => Changes.Count - SourceChangeCount;
}

/// <summary>Explicit Git commands; default-branch publication requires the dedicated reviewed workflow.</summary>
public sealed partial class GitSyncService
{
    private static readonly string[] SourcePaths = ["Content", "Project", "RCode", "StatsDirect.flprj"];
    private const string RecoveryDirectory = ".help-editor-recovery";
    private const string FetchStateKey = "statsdirectHelpEditor.lastSuccessfulFetch";


    public static async Task<HelpProject> CloneAsync(string url, string dest, CancellationToken ct = default)
    {
        var source = ValidateCloneSource(url);
        var destination = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar);
        SafePaths.CheckNoLinks(destination);
        WorkspaceProtectionSettings.Load().RequireUnprotected(destination, "Clone help");
        if (SafePaths.Same(destination, Path.GetPathRoot(destination)!))
            throw new InvalidOperationException("Choose a new folder for an independent help working copy.");
        if (File.Exists(destination) || Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new InvalidOperationException("Clone needs a new or empty destination folder.");
        var parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException("Choose a destination folder.", nameof(dest));
        Directory.CreateDirectory(parent);
        await RunAsync(parent, ["clone", "--no-hardlinks", "--", source, destination], ct, timeoutSeconds: 180);
        string prefix = CloneSourcePrefix(destination);
        string sourceRoot = prefix.Length == 0 ? destination : SafePaths.Resolve(destination, prefix);
        var sha = await RunAsync(destination, ["rev-parse", "HEAD"], ct);
        string marker = Path.Combine(sourceRoot, HelpProject.MarkerFile);
        if (!File.Exists(marker)) SafePaths.AtomicWrite(marker, JsonSerializer.Serialize(new { prototypeWorkingCopy = true, originalPath = Redact(source), copiedAtUtc = DateTimeOffset.UtcNow, sourceCommit = sha.Output.Trim(), clonedBy = "StatsDirectHelpEditor" }, new JsonSerializerOptions { WriteIndented = true }));
        // The marker and deleted-page recovery copies stay local to this authoring copy.
        var exclude = Path.Combine(destination, ".git", "info", "exclude");
        Directory.CreateDirectory(Path.GetDirectoryName(exclude)!);
        var current = File.Exists(exclude) ? File.ReadAllText(exclude) : "";
        foreach (string localPath in new[] { "/" + (prefix.Length == 0 ? "" : prefix + "/") + HelpProject.MarkerFile, "/" + (prefix.Length == 0 ? "" : prefix + "/") + RecoveryDirectory + "/" })
            if (!current.Split('\n').Any(line => line.Trim() == localPath))
                File.AppendAllText(exclude, Environment.NewLine + localPath + Environment.NewLine, new UTF8Encoding(false));
        return HelpProject.Open(sourceRoot);
    }

    public async Task<GitStatus> StatusAsync(CancellationToken ct = default)
    {
        var top = await RunAsync(RepositoryRoot, ["rev-parse", "--show-toplevel"], ct, allowFailure: true);
        if (top.ExitCode != 0 || !SafePaths.Same(top.Output.Trim(), RepositoryRoot)) return new GitStatus(false, "", null, 0, 0, []);
        var branchResult = await RunAsync(RepositoryRoot, ["symbolic-ref", "--quiet", "--short", "HEAD"], ct, allowFailure: true);
        var branch = branchResult.ExitCode == 0 ? branchResult.Output.Trim() : "(detached HEAD)";
        var remoteResult = await RunAsync(RepositoryRoot, ["remote", "get-url", "origin"], ct, allowFailure: true);
        var rawRemote = remoteResult.ExitCode == 0 ? remoteResult.Output.Trim() : null;
        var remote = rawRemote is not null ? Redact(rawRemote) : null;
        var tracking = await ConfiguredTrackingBranchAsync(branch, ct);
        var comparison = tracking ?? await MatchingOriginBranchAsync(branch, ct);
        var ahead = 0; var behind = 0;
        var hasComparison = false;
        if (comparison is not null)
        {
            var counts = await RunAsync(RepositoryRoot, ["rev-list", "--left-right", "--count", "HEAD..." + comparison], ct, allowFailure: true);
            var fields = counts.Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (counts.ExitCode == 0 && fields.Length == 2 && int.TryParse(fields[0], out ahead) && int.TryParse(fields[1], out behind)) hasComparison = true;
        }
        var status = await RunAsync(RepositoryRoot, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], ct);
        var parts = status.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var changes = new List<GitFileChange>();
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length < 4) continue;
            var code = parts[i][..2];
            var path = parts[i][3..].Replace('\\', '/');
            string? originalPath = null;
            if ((code.Contains('R') || code.Contains('C')) && i + 1 < parts.Length) originalPath = parts[++i].Replace('\\', '/'); // v1 -z has the old path next
            // Older working copies may not have the local exclude rule yet.
            // Hide only untracked recovery data: staged/tracked copies and
            // renames must remain visible and subject to the commit guard.
            if (code == "??" && IsRecoveryPath(path)) continue;
            changes.Add(new GitFileChange(code, path) { OriginalPath = originalPath, SourceClassification = IsScopedSourcePath(path) || originalPath is not null && IsScopedSourcePath(originalPath) });
        }
        return new GitStatus(true, branch, remote, ahead, behind, changes)
        {
            TrackingBranch = tracking,
            ComparisonBranch = comparison,
            HasComparison = hasComparison,
            LastSuccessfulFetchUtc = await ReadFetchTimeAsync(rawRemote, ct)
        };
    }

    public async Task<GitAuthorIdentity> ReadAuthorIdentityAsync(CancellationToken ct = default)
    {
        await RequireRepositoryAsync(ct);
        var name = await RunAsync(RepositoryRoot, ["config", "--get", "user.name"], ct, allowFailure: true);
        var email = await RunAsync(RepositoryRoot, ["config", "--get", "user.email"], ct, allowFailure: true);
        return new(name.ExitCode == 0 ? name.Output.Trim() : "", email.ExitCode == 0 ? email.Output.Trim() : "");
    }

    public async Task<string> FetchAsync(CancellationToken ct = default)
    {
        await RequireRepositoryAsync(ct);
        var origin = await RequireOriginAsync(ct);
        var result = await RunAsync(RepositoryRoot, ["fetch", "--prune", "origin"], ct, timeoutSeconds: 120, environment: TransportEnvironment);
        // A successful explicit fetch updates local metadata only. Status never
        // fetches, and a failed/cancelled attempt must not replace this receipt.
        var state = JsonSerializer.Serialize(new FetchState(OriginIdentity(origin), DateTimeOffset.UtcNow));
        await RunAsync(RepositoryRoot, ["config", "--local", "--replace-all", FetchStateKey, state], ct);
        return Message(result, "Fetched origin. The working files were not changed.");
    }

    public async Task<string> PullAsync(CancellationToken ct = default)
    {
        var status = await RequireRepositoryAsync(ct);
        if (status.Changes.Count != 0) throw new InvalidOperationException("Save and commit or otherwise resolve all working-copy changes before pulling. Nothing was stashed or reset.");
        if (status.Branch == "(detached HEAD)") throw new InvalidOperationException("Switch to a working branch before pulling.");
        await FetchAsync(ct);
        var tracking = await TrackingBranchAsync(status.Branch, ct);
        if (tracking is null) throw new InvalidOperationException("This working branch has not been pushed yet. Commit and push it first; there is no matching remote branch to pull.");
        var merged = await RunAsync(RepositoryRoot, ["merge", "--ff-only", tracking], ct);
        return Message(merged, "The working branch is up to date.");
    }

    public async Task<string> CreateBranchAsync(string branch, CancellationToken ct = default)
    {
        await RequireRepositoryAsync(ct);
        await ValidateBranchAsync(branch, ct);
        if (branch.Equals("main", StringComparison.OrdinalIgnoreCase) || branch.Equals("master", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use a working branch, for example help-editor/20261101.");
        return Message(await RunAsync(RepositoryRoot, ["switch", "-c", branch], ct), "Created working branch " + branch + ".");
    }

    public Task<string> CommitAsync(string message, string authorName, string authorEmail, CancellationToken ct = default) =>
        CommitScopedAsync(message, authorName, authorEmail, includeWebsite: false, ct);

    private async Task<string> CommitScopedAsync(string message, string authorName, string authorEmail, bool includeWebsite, CancellationToken ct)
    {
        await RequireRepositoryAsync(ct);
        authorEmail ??= "";
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Enter a commit message.", nameof(message));
        if (string.IsNullOrWhiteSpace(authorName) || authorName.IndexOfAny(['\r', '\n', '<', '>']) >= 0 || !Regex.IsMatch(authorEmail, @"^[^\s<>@]+@[^\s<>@]+$"))
            throw new ArgumentException("Enter a valid author name and email address.");
        await RejectOutsideStagedChangesAsync(ct, includeWebsite);
        var scope = includeWebsite ? WebsiteCommitScope : ScopedSourcePaths;
        var tracked = await RunAsync(RepositoryRoot, ["ls-tree", "-r", "--name-only", "-z", "HEAD", "--", .. scope], ct);
        var trackedPaths = tracked.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var paths = scope.Where(p => Directory.Exists(Path.Combine(RepositoryRoot, p)) || File.Exists(Path.Combine(RepositoryRoot, p)) || trackedPaths.Any(t => t == p || t.StartsWith(p + "/", StringComparison.Ordinal))).ToArray();
        if (paths.Length == 0) throw new InvalidOperationException("This working copy has no help-source paths to commit.");
        return await CommitReviewedTreeAsync(CommitPathSpecs(paths), message.Trim(), authorName.Trim(), authorEmail.Trim(), includeWebsite, ct);
    }

    public Task<string> PushAsync(CancellationToken ct = default) => PushCoreAsync(null, null, ct);

    private async Task<string> PushCoreAsync(string? verifiedCommit, string? verifiedBranch, CancellationToken ct, bool allowDefaultBranchPublication = false)
    {
        var status = await RequireRepositoryAsync(ct);
        if (verifiedBranch != null && status.Branch != verifiedBranch)
            throw new InvalidOperationException("The branch changed during website verification. Review it before pushing.");
        if (status.Branch == "(detached HEAD)") throw new InvalidOperationException("Create or switch to a working branch before pushing.");
        await ValidateBranchAsync(status.Branch, ct);
        await RequireOriginAsync(ct);
        if (!allowDefaultBranchPublication && (status.Branch.Equals("main", StringComparison.OrdinalIgnoreCase) || status.Branch.Equals("master", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This prototype does not push main or master. Create a help-editor working branch and review it before merging on GitHub.");
        var remoteHead = await RunAsync(RepositoryRoot, ["ls-remote", "--symref", "origin", "HEAD"], ct, timeoutSeconds: 120, environment: TransportEnvironment);
        var match = Regex.Match(remoteHead.Output, @"(?m)^ref:\s+refs/heads/([^\r\n\t]+)\s+HEAD\s*$");
        if (!allowDefaultBranchPublication && match.Success && status.Branch.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected branch is the repositoryâ€™s default branch. Push a separate working branch instead.");
        if (allowDefaultBranchPublication)
        {
            if (!publicationLayout || verifiedCommit == null || verifiedBranch == null) throw new InvalidOperationException("Default-branch publication requires a reviewed committed website.");
            var currentRemote = await RunAsync(RepositoryRoot, ["ls-remote", "origin", "refs/heads/" + status.Branch], ct, timeoutSeconds: 120, environment: TransportEnvironment);
            string remoteCommit = currentRemote.Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var clonedRemote = await RunAsync(RepositoryRoot, ["rev-parse", "refs/remotes/origin/" + status.Branch], ct, allowFailure: true);
            if (remoteCommit.Length == 0 || remoteCommit != verifiedCommit && (clonedRemote.ExitCode != 0 || remoteCommit != clonedRemote.Output.Trim()))
                throw new InvalidOperationException("The publication branch changed on the remote after review. Start with a fresh clone before publishing.");
        }
        var pushed = await RunAsync(RepositoryRoot, ["push", "--set-upstream", "origin", (verifiedCommit ?? "HEAD") + ":refs/heads/" + status.Branch], ct, timeoutSeconds: 120, environment: TransportEnvironment);
        // A SHA refspec has no local branch name from which Git can infer tracking.
        if (verifiedCommit != null)
            await RunAsync(RepositoryRoot, ["branch", "--set-upstream-to=origin/" + status.Branch, status.Branch], ct);
        return Message(pushed, allowDefaultBranchPublication ? "Published the reviewed help commit to origin/" + status.Branch + "." : "Pushed the working branch to origin. Nothing was merged or published.");
    }

    private async Task<GitStatus> RequireRepositoryAsync(CancellationToken ct)
    {
        var status = await StatusAsync(ct);
        if (!status.IsRepository) throw new InvalidOperationException("This is a copied folder, not an independent Git checkout. Clone the GitHub help repository into a new working copy to synchronise it.");
        if (expectedPublicationBranch != null && status.Branch != expectedPublicationBranch)
            throw new InvalidOperationException("The selected publication branch changed after cloning. Start with a fresh reviewed clone before publishing.");
        return status;
    }

    private async Task<string> RequireOriginAsync(CancellationToken ct)
    {
        var result = await RunAsync(RepositoryRoot, ["remote", "get-url", "origin"], ct, allowFailure: true);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output)) throw new InvalidOperationException("This repository has no origin remote.");
        string origin = result.Output.Trim();
        if (expectedPublicationOrigin != null && !origin.Equals(expectedPublicationOrigin, StringComparison.Ordinal))
            throw new InvalidOperationException("The selected publication origin changed after cloning. Review the repository URL and start again.");
        if (localTestRemote != null && (!Uri.TryCreate(origin, UriKind.Absolute, out var localUri) || !localUri.IsFile || !SafePaths.Same(localUri.LocalPath, localTestRemote)))
            throw new InvalidOperationException("The isolated test publication remote was changed; network access is refused.");
        return origin;
    }

    private async Task<string?> TrackingBranchAsync(string branch, CancellationToken ct)
    {
        return await ConfiguredTrackingBranchAsync(branch, ct) ?? await MatchingOriginBranchAsync(branch, ct);
    }

    private async Task<string?> ConfiguredTrackingBranchAsync(string branch, CancellationToken ct)
    {
        if (branch == "(detached HEAD)") return null;
        var upstream = await RunAsync(RepositoryRoot, ["for-each-ref", "--format=%(upstream:short)", "--", "refs/heads/" + branch], ct, allowFailure: true);
        return upstream.ExitCode == 0 && !string.IsNullOrWhiteSpace(upstream.Output) ? upstream.Output.Trim() : null;
    }

    private async Task<string?> MatchingOriginBranchAsync(string branch, CancellationToken ct)
    {
        if (branch == "(detached HEAD)") return null;
        var matching = "refs/remotes/origin/" + branch;
        var exists = await RunAsync(RepositoryRoot, ["show-ref", "--verify", "--quiet", matching], ct, allowFailure: true);
        return exists.ExitCode == 0 ? matching : null;
    }

    private async Task RejectOutsideStagedChangesAsync(CancellationToken ct, bool includeWebsite = false)
    {
        var result = await RunAsync(RepositoryRoot, ["diff", "--cached", "--name-only", "--no-renames", "-z"], ct);
        var outside = result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(p => !IsScopedSourcePath(p) && !(includeWebsite && IsWebsiteCommitPath(p))).ToArray();
        if (outside.Length != 0)
            throw new InvalidOperationException("Other files are already staged. Unstage them before committing help source:" + Environment.NewLine + string.Join(Environment.NewLine, outside.Take(15)));
    }

    private bool IsRecoveryPath(string path) => TrySourceRelative(path, out var relative) &&
        (relative.Equals(RecoveryDirectory, StringComparison.OrdinalIgnoreCase) || relative.StartsWith(RecoveryDirectory + "/", StringComparison.OrdinalIgnoreCase));

    public static bool IsSourcePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        path = path.Replace('\\', '/');
        return SourcePaths.Any(prefix => path.Equals(prefix, StringComparison.Ordinal) || path.StartsWith(prefix + "/", StringComparison.Ordinal));
    }

    private async Task<DateTimeOffset?> ReadFetchTimeAsync(string? origin, CancellationToken ct)
    {
        if (origin is null) return null;
        var result = await RunAsync(RepositoryRoot, ["config", "--local", "--get", FetchStateKey], ct, allowFailure: true);
        if (result.ExitCode != 0) return null;
        try
        {
            var state = JsonSerializer.Deserialize<FetchState>(result.Output.Trim());
            return state?.OriginIdentity == OriginIdentity(origin) && state.FetchedAtUtc > DateTimeOffset.UnixEpoch && state.FetchedAtUtc <= DateTimeOffset.UtcNow.AddMinutes(1) ? state.FetchedAtUtc : null;
        }
        catch (JsonException) { return null; }
    }

    private static string OriginIdentity(string origin) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(origin)));
    private record FetchState(string OriginIdentity, DateTimeOffset FetchedAtUtc);

    private async Task ValidateBranchAsync(string branch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.StartsWith('-') || branch.Contains('\r') || branch.Contains('\n')) throw new ArgumentException("Enter a valid working-branch name.", nameof(branch));
        var result = await RunAsync(RepositoryRoot, ["check-ref-format", "--branch", branch], ct, allowFailure: true);
        if (result.ExitCode != 0) throw new ArgumentException("That branch name is not valid. Try help-editor/20261101.", nameof(branch));
    }

    private static string ValidateCloneSource(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("Enter the GitHub repository URL.");
        input = input.Trim();
        if (Directory.Exists(input)) { var local = Path.GetFullPath(input); SafePaths.CheckNoLinks(local); return local; }
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !Regex.IsMatch(uri.AbsolutePath, @"^/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$"))
            throw new ArgumentException("Use an https://github.com/owner/repository URL without credentials, tokens, a query or a fragment.");
        return uri.AbsoluteUri.TrimEnd('/');
    }

    private static string Redact(string text)
    {
        text = Regex.Replace(text, @"(?i)(https?://)[^/\s@]+@", "$1[credentials]@");
        return Regex.Replace(text, @"(?i)(https?://[^\s?#]+)\?[^\s#]+", "$1?[redacted]");
    }
    private static string Message(GitRunResult result, string fallback)
    {
        var text = (result.Output + Environment.NewLine + result.Error).Trim();
        return text.Length == 0 ? fallback : Redact(text);
    }

    private static async Task<GitRunResult> RunAsync(string cwd, IReadOnlyList<string> args, CancellationToken ct, bool allowFailure = false, int timeoutSeconds = 30,
        IReadOnlyDictionary<string, string>? environment = null, string? input = null)
    {
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(ResolveGitExecutable()) { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("credential.interactive=never");
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("core.quotepath=false");
        // Generated artwork can exceed MAX_PATH in an otherwise valid authoring
        // folder. Enable Git for Windows long paths for this process only.
        if (OperatingSystem.IsWindows()) { start.ArgumentList.Add("-c"); start.ArgumentList.Add("core.longpaths=true"); }
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GCM_INTERACTIVE"] = "Never";
        if (environment != null) foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        using var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw new InvalidOperationException("Git did not start."); }
        catch (System.ComponentModel.Win32Exception ex) { throw new InvalidOperationException("Git is not installed or could not start. Install Git for Windows and sign in using your normal GitHub credentials.", ex); }
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            if (input != null) await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); } catch (InvalidOperationException) { }
            if (ct.IsCancellationRequested) throw new OperationCanceledException("The Git operation was cancelled.", ct);
            throw new TimeoutException("Git did not complete in time. Check the connection and your GitHub sign-in, then try again.");
        }
        var result = new GitRunResult(process.ExitCode, await output, await error);
        if (result.ExitCode != 0 && !allowFailure)
            throw new InvalidOperationException("Git could not complete the operation:" + Environment.NewLine + Redact((result.Error + Environment.NewLine + result.Output).Trim()));
        return result;
    }

    private record GitRunResult(int ExitCode, string Output, string Error);

    private static string ResolveGitExecutable()
    {
        string name = OperatingSystem.IsWindows() ? "git.exe" : "git";
        // Only explicit absolute PATH entries participate. Never search the
        // application's or parent's current directory implicitly, nor run a
        // batch wrapper through a shell. Tests may supply an isolated PATH.
        foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string directory = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(directory)) continue;
            string candidate = Path.GetFullPath(Path.Combine(directory, name));
            if (!File.Exists(candidate)) continue;
            SafePaths.CheckNoLinks(candidate);
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(candidate) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0) continue;
            return candidate;
        }
        throw new InvalidOperationException("Git was not found in an absolute PATH directory. Install Git and add its executable directory to PATH.");
    }
}
