using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StatsDirect.HelpEditor.Core;

public record GitFileChange(string Status, string Path)
{
    public string? OriginalPath { get; init; }
    public bool IsSource => GitSyncService.IsSourcePath(Path) || OriginalPath is not null && GitSyncService.IsSourcePath(OriginalPath);
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

/// <summary>Explicit Git commands on a marked authoring copy. Never publishes the default branch.</summary>
public sealed partial class GitSyncService(HelpProject project)
{
    private static readonly string[] SourcePaths = ["Content", "Project", "RCode", "StatsDirect.flprj"];
    private const string RecoveryDirectory = ".help-editor-recovery";
    private const string FetchStateKey = "statsdirectHelpEditor.lastSuccessfulFetch";
    private readonly HelpProject project = project ?? throw new ArgumentNullException(nameof(project));

    public static async Task<HelpProject> CloneAsync(string url, string dest, CancellationToken ct = default)
    {
        var source = ValidateCloneSource(url);
        var destination = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar);
        SafePaths.CheckNoLinks(destination);
        if (SafePaths.IsWithin(destination, @"C:\Develop\SD3Help\StatsDirect") || SafePaths.IsWithin(destination, @"C:\Users\ieb21\source\repos\statsdirect") || SafePaths.IsWithin(destination, @"C:\temp") || SafePaths.Same(destination, Path.GetPathRoot(destination)!))
            throw new InvalidOperationException("Choose a new folder for an independent help working copy.");
        if (File.Exists(destination) || Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new InvalidOperationException("Clone needs a new or empty destination folder.");
        var parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException("Choose a destination folder.", nameof(dest));
        Directory.CreateDirectory(parent);
        await RunAsync(parent, ["clone", "--no-hardlinks", "--", source, destination], ct, timeoutSeconds: 180);
        if (!Directory.Exists(Path.Combine(destination, "Content")) || !File.Exists(Path.Combine(destination, "Project", "TOCs", "STATSDIRECT.fltoc")))
            throw new InvalidDataException("The cloned repository is not a StatsDirect Flare help project. Choose iain-buchan/statisticalhelp.");
        var sha = await RunAsync(destination, ["rev-parse", "HEAD"], ct);
        SafePaths.AtomicWrite(Path.Combine(destination, HelpProject.MarkerFile), JsonSerializer.Serialize(new { prototypeWorkingCopy = true, originalPath = Redact(source), copiedAtUtc = DateTimeOffset.UtcNow, sourceCommit = sha.Output.Trim(), clonedBy = "StatsDirectHelpEditor" }, new JsonSerializerOptions { WriteIndented = true }));
        // The marker and deleted-page recovery copies stay local to this authoring copy.
        var exclude = Path.Combine(destination, ".git", "info", "exclude");
        Directory.CreateDirectory(Path.GetDirectoryName(exclude)!);
        var current = File.Exists(exclude) ? File.ReadAllText(exclude) : "";
        foreach (string localPath in new[] { "/" + HelpProject.MarkerFile, "/" + RecoveryDirectory + "/" })
            if (!current.Split('\n').Any(line => line.Trim() == localPath))
                File.AppendAllText(exclude, Environment.NewLine + localPath + Environment.NewLine, new UTF8Encoding(false));
        return HelpProject.Open(destination);
    }

    public async Task<GitStatus> StatusAsync(CancellationToken ct = default)
    {
        var top = await RunAsync(project.RootPath, ["rev-parse", "--show-toplevel"], ct, allowFailure: true);
        if (top.ExitCode != 0 || !SafePaths.Same(top.Output.Trim(), project.RootPath)) return new GitStatus(false, "", null, 0, 0, []);
        var branchResult = await RunAsync(project.RootPath, ["symbolic-ref", "--quiet", "--short", "HEAD"], ct, allowFailure: true);
        var branch = branchResult.ExitCode == 0 ? branchResult.Output.Trim() : "(detached HEAD)";
        var remoteResult = await RunAsync(project.RootPath, ["remote", "get-url", "origin"], ct, allowFailure: true);
        var rawRemote = remoteResult.ExitCode == 0 ? remoteResult.Output.Trim() : null;
        var remote = rawRemote is not null ? Redact(rawRemote) : null;
        var tracking = await ConfiguredTrackingBranchAsync(branch, ct);
        var comparison = tracking ?? await MatchingOriginBranchAsync(branch, ct);
        var ahead = 0; var behind = 0;
        var hasComparison = false;
        if (comparison is not null)
        {
            var counts = await RunAsync(project.RootPath, ["rev-list", "--left-right", "--count", "HEAD..." + comparison], ct, allowFailure: true);
            var fields = counts.Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (counts.ExitCode == 0 && fields.Length == 2 && int.TryParse(fields[0], out ahead) && int.TryParse(fields[1], out behind)) hasComparison = true;
        }
        var status = await RunAsync(project.RootPath, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], ct);
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
            changes.Add(new GitFileChange(code, path) { OriginalPath = originalPath });
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
        var name = await RunAsync(project.RootPath, ["config", "--get", "user.name"], ct, allowFailure: true);
        var email = await RunAsync(project.RootPath, ["config", "--get", "user.email"], ct, allowFailure: true);
        return new(name.ExitCode == 0 ? name.Output.Trim() : "", email.ExitCode == 0 ? email.Output.Trim() : "");
    }

    public async Task<string> FetchAsync(CancellationToken ct = default)
    {
        await RequireRepositoryAsync(ct);
        var origin = await RequireOriginAsync(ct);
        var result = await RunAsync(project.RootPath, ["fetch", "--prune", "origin"], ct, timeoutSeconds: 120);
        // A successful explicit fetch updates local metadata only. Status never
        // fetches, and a failed/cancelled attempt must not replace this receipt.
        var state = JsonSerializer.Serialize(new FetchState(OriginIdentity(origin), DateTimeOffset.UtcNow));
        await RunAsync(project.RootPath, ["config", "--local", "--replace-all", FetchStateKey, state], ct);
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
        var merged = await RunAsync(project.RootPath, ["merge", "--ff-only", tracking], ct);
        return Message(merged, "The working branch is up to date.");
    }

    public async Task<string> CreateBranchAsync(string branch, CancellationToken ct = default)
    {
        await RequireRepositoryAsync(ct);
        await ValidateBranchAsync(branch, ct);
        if (branch.Equals("main", StringComparison.OrdinalIgnoreCase) || branch.Equals("master", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use a working branch, for example help-editor/20261101.");
        return Message(await RunAsync(project.RootPath, ["switch", "-c", branch], ct), "Created working branch " + branch + ".");
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
        var scope = includeWebsite ? SourcePaths.Append(WebsitePath).ToArray() : SourcePaths;
        var tracked = await RunAsync(project.RootPath, ["ls-tree", "-r", "--name-only", "-z", "HEAD", "--", .. scope], ct);
        var trackedPaths = tracked.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var paths = scope.Where(p => Directory.Exists(Path.Combine(project.RootPath, p)) || File.Exists(Path.Combine(project.RootPath, p)) || trackedPaths.Any(t => t == p || t.StartsWith(p + "/", StringComparison.Ordinal))).ToArray();
        if (paths.Length == 0) throw new InvalidOperationException("This working copy has no help-source paths to commit.");
        return await CommitReviewedTreeAsync(paths, message.Trim(), authorName.Trim(), authorEmail.Trim(), includeWebsite, ct);
    }

    public Task<string> PushAsync(CancellationToken ct = default) => PushCoreAsync(null, null, ct);

    private async Task<string> PushCoreAsync(string? verifiedCommit, string? verifiedBranch, CancellationToken ct)
    {
        var status = await RequireRepositoryAsync(ct);
        if (verifiedBranch != null && status.Branch != verifiedBranch)
            throw new InvalidOperationException("The branch changed during website verification. Review it before pushing.");
        if (status.Branch == "(detached HEAD)") throw new InvalidOperationException("Create or switch to a working branch before pushing.");
        await ValidateBranchAsync(status.Branch, ct);
        await RequireOriginAsync(ct);
        if (status.Branch.Equals("main", StringComparison.OrdinalIgnoreCase) || status.Branch.Equals("master", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This prototype does not push main or master. Create a help-editor working branch and review it before merging on GitHub.");
        var remoteHead = await RunAsync(project.RootPath, ["ls-remote", "--symref", "origin", "HEAD"], ct, timeoutSeconds: 120);
        var match = Regex.Match(remoteHead.Output, @"(?m)^ref:\s+refs/heads/([^\r\n\t]+)\s+HEAD\s*$");
        if (match.Success && status.Branch.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected branch is the repository’s default branch. Push a separate working branch instead.");
        var pushed = await RunAsync(project.RootPath, ["push", "--set-upstream", "origin", (verifiedCommit ?? "HEAD") + ":refs/heads/" + status.Branch], ct, timeoutSeconds: 120);
        // A SHA refspec has no local branch name from which Git can infer tracking.
        if (verifiedCommit != null)
            await RunAsync(project.RootPath, ["branch", "--set-upstream-to=origin/" + status.Branch, status.Branch], ct);
        return Message(pushed, "Pushed the working branch to origin. Nothing was merged or published.");
    }

    private async Task<GitStatus> RequireRepositoryAsync(CancellationToken ct)
    {
        var status = await StatusAsync(ct);
        if (!status.IsRepository) throw new InvalidOperationException("This is a copied folder, not an independent Git checkout. Clone the GitHub help repository into a new working copy to synchronise it.");
        return status;
    }

    private async Task<string> RequireOriginAsync(CancellationToken ct)
    {
        var result = await RunAsync(project.RootPath, ["remote", "get-url", "origin"], ct, allowFailure: true);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output)) throw new InvalidOperationException("This repository has no origin remote.");
        return result.Output.Trim();
    }

    private async Task<string?> TrackingBranchAsync(string branch, CancellationToken ct)
    {
        return await ConfiguredTrackingBranchAsync(branch, ct) ?? await MatchingOriginBranchAsync(branch, ct);
    }

    private async Task<string?> ConfiguredTrackingBranchAsync(string branch, CancellationToken ct)
    {
        if (branch == "(detached HEAD)") return null;
        var upstream = await RunAsync(project.RootPath, ["for-each-ref", "--format=%(upstream:short)", "--", "refs/heads/" + branch], ct, allowFailure: true);
        return upstream.ExitCode == 0 && !string.IsNullOrWhiteSpace(upstream.Output) ? upstream.Output.Trim() : null;
    }

    private async Task<string?> MatchingOriginBranchAsync(string branch, CancellationToken ct)
    {
        if (branch == "(detached HEAD)") return null;
        var matching = "refs/remotes/origin/" + branch;
        var exists = await RunAsync(project.RootPath, ["show-ref", "--verify", "--quiet", matching], ct, allowFailure: true);
        return exists.ExitCode == 0 ? matching : null;
    }

    private async Task RejectOutsideStagedChangesAsync(CancellationToken ct, bool includeWebsite = false)
    {
        var result = await RunAsync(project.RootPath, ["diff", "--cached", "--name-only", "--no-renames", "-z"], ct);
        var outside = result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(p => !IsSourcePath(p) && !(includeWebsite && IsWebsitePath(p))).ToArray();
        if (outside.Length != 0)
            throw new InvalidOperationException("Other files are already staged. Unstage them before committing help source:" + Environment.NewLine + string.Join(Environment.NewLine, outside.Take(15)));
    }

    private static bool IsRecoveryPath(string path) => path.Equals(RecoveryDirectory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(RecoveryDirectory + "/", StringComparison.OrdinalIgnoreCase);

    public static bool IsSourcePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        path = path.Replace('\\', '/');
        return SourcePaths.Any(prefix => path.Equals(prefix, StringComparison.Ordinal) || path.StartsWith(prefix + "/", StringComparison.Ordinal));
    }

    private async Task<DateTimeOffset?> ReadFetchTimeAsync(string? origin, CancellationToken ct)
    {
        if (origin is null) return null;
        var result = await RunAsync(project.RootPath, ["config", "--local", "--get", FetchStateKey], ct, allowFailure: true);
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
        var result = await RunAsync(project.RootPath, ["check-ref-format", "--branch", branch], ct, allowFailure: true);
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
        var start = new ProcessStartInfo("git") { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("credential.interactive=never");
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("core.quotepath=false");
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
}
