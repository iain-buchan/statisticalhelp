using System.Diagnostics;
using System.Text.Json;
using StatsDirect.HelpEditor.Core;

// Git launches this same already-built test executable as a deliberately slow
// upload-pack. Signal readiness before any fixture setup or shell dependency.
if (args.Length >= 2 && args[0] == "--slow-upload-pack")
{
    string temporary = args[1] + ".ready-" + Environment.ProcessId;
    File.WriteAllText(temporary, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    File.Move(temporary, args[1]);
    await Task.Delay(TimeSpan.FromSeconds(30));
    return 0;
}

string root = Path.GetFullPath(args[0]);
string evidence = Path.Combine(root, "evidence");
string fixtures = Path.Combine(evidence, "GitFixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtures);
var checks = new List<object>(); int passed = 0, failed = 0;
object? slowFetchEvidence = null;
void Check(bool value, string name) { if (value) passed++; else failed++; checks.Add(new { name, passed = value }); Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}"); }
async Task Reject(Func<Task> operation, string name) { try { await operation(); Check(false, name); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OperationCanceledException) { Check(true, name); } }
async Task<string> Git(string cwd, params string[] arguments)
{
    var p = new ProcessStartInfo("git") { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    foreach (var a in new[] { "-c", "user.name=Iain Buchan", "-c", "user.email=buchan@liverpool.ac.uk", "-c", "commit.gpgsign=false" }.Concat(arguments)) p.ArgumentList.Add(a);
    p.Environment["GIT_TERMINAL_PROMPT"] = "0";
    using var process = Process.Start(p)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync(); string text = await stdout, errors = await stderr;
    if (process.ExitCode != 0) throw new InvalidOperationException(string.Join(' ', arguments.Take(2)) + ": " + errors);
    return text.Trim();
}
try
{
    // The maintained HelpEditor/HelpSource is itself a valid nested checkout in
    // CI. Exercise an actual independent copy rather than assuming it is not.
    string disconnectedRoot = Path.Combine(fixtures, "independent-source");
    Directory.CreateDirectory(Path.Combine(disconnectedRoot, "Content"));
    Directory.CreateDirectory(Path.Combine(disconnectedRoot, "Project/TOCs"));
    File.WriteAllText(Path.Combine(disconnectedRoot, HelpProject.MarkerFile), "{}");
    File.WriteAllText(Path.Combine(disconnectedRoot, "Project/TOCs/STATSDIRECT.fltoc"), "<CatapultToc/>");
    var disconnected = await new GitSyncService(HelpProject.Open(disconnectedRoot)).StatusAsync();
    Check(!disconnected.IsRepository, "Independent help source is not treated as a Git checkout");
    Check(disconnected.Remote is null && !disconnected.HasTrackingBranch && !disconnected.HasComparison && disconnected.LastSuccessfulFetchUtc is null && disconnected.SourceChangeCount == 0 && disconnected.OtherChangeCount == 0, "A plain source copy reports no remote, comparison or successful remote check");
    string seed = Path.Combine(fixtures, "seed"), remote = Path.Combine(fixtures, "origin.git"); Directory.CreateDirectory(seed);
    string sourceFixture = Directory.GetDirectories(evidence, "CoreFixture-*").OrderByDescending(Directory.GetLastWriteTimeUtc).First();
    foreach (string folder in new[] { "Content", "Project" })
    foreach (string file in Directory.GetFiles(Path.Combine(sourceFixture, folder), "*", SearchOption.AllDirectories))
    { string relative = Path.GetRelativePath(sourceFixture, file), dest = Path.Combine(seed, relative); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest); }
    File.Copy(Path.Combine(sourceFixture, "StatsDirect.flprj"), Path.Combine(seed, "StatsDirect.flprj"));
    Directory.CreateDirectory(Path.Combine(seed, "Docs")); File.WriteAllText(Path.Combine(seed, "Docs", "index.html"), "Published help remains unchanged.");
    Directory.CreateDirectory(Path.Combine(seed, "RCode")); File.WriteAllText(Path.Combine(seed, "RCode", "example.R"), "mean(c(1, 2))\n");
    await Git(fixtures, "init", "--bare", "--initial-branch=master", remote);
    await Git(seed, "init", "--initial-branch=master"); await Git(seed, "add", "."); await Git(seed, "commit", "-m", "Seed local fixture"); await Git(seed, "remote", "add", "origin", remote); await Git(seed, "push", "-u", "origin", "master");
    string masterBefore = await Git(remote, "rev-parse", "refs/heads/master");
    string copy = Path.Combine(fixtures, "authoring");
    var project = await GitSyncService.CloneAsync(remote, copy);
    var sync = new GitSyncService(project); var status = await sync.StatusAsync();
    await Git(copy, "config", "--local", "user.name", "Fixture Author");
    await Git(copy, "config", "--local", "user.email", "author@example.test");
    Check(await sync.ReadAuthorIdentityAsync() == new GitAuthorIdentity("Fixture Author", "author@example.test"), "Commit identity follows the selected repository's Git configuration");
    await Git(copy, "config", "--local", "user.name", "");
    await Git(copy, "config", "--local", "user.email", "");
    Check(await sync.ReadAuthorIdentityAsync() == new GitAuthorIdentity("", ""), "Missing Git identity stays blank rather than attributing a public user's work to Iain");
    await Git(copy, "config", "--local", "--unset", "user.name");
    await Git(copy, "config", "--local", "--unset", "user.email");
    Check(status.IsRepository && status.Branch == "master" && status.Changes.Count == 0, "Clone creates a clean independent marked checkout");
    Check(status.HasTrackingBranch && status.TrackingBranch == "origin/master" && status.ComparisonBranch == "origin/master" && status.HasComparison && status.Ahead == 0 && status.Behind == 0, "A clean checkout reports its actual upstream and a valid cached comparison");
    Check(status.LastSuccessfulFetchUtc is null && DateTimeOffset.UtcNow - status.ObservedAtUtc < TimeSpan.FromSeconds(5), "Local status has an observation time without inventing a successful editor fetch");
    Check(File.Exists(Path.Combine(copy, HelpProject.MarkerFile)) && !File.Exists(Path.Combine(copy, ".git", "objects", "info", "alternates")), "Working-copy marker is local and clone has independent Git storage");
    string excludeFile = Path.Combine(copy, ".git", "info", "exclude");
    Check(File.ReadAllText(excludeFile).Split('\n').Any(line => line.Trim() == "/.help-editor-recovery/"), "Clone excludes local deleted-page recovery data from ordinary Git staging");
    // Simulate an older checkout without the new local ignore rule.
    File.WriteAllText(excludeFile, string.Join('\n', File.ReadAllText(excludeFile).Split('\n').Where(line => line.Trim() != "/.help-editor-recovery/")));
    string recoveryFile = Path.Combine(copy, ".help-editor-recovery", "deleted-pages", "fixture", "metadata.json");
    Directory.CreateDirectory(Path.GetDirectoryName(recoveryFile)!);
    File.WriteAllText(recoveryFile, "{\"purpose\":\"Owned local recovery fixture\"}");
    Check((await sync.StatusAsync()).Changes.Count == 0 && File.Exists(recoveryFile), "Untracked recovery copies stay local and do not make an older checkout appear dirty");
    await Reject(() => sync.PushAsync(), "Prototype cannot push the default branch");
    await sync.CreateBranchAsync("help-editor/test");
    Check((await sync.StatusAsync()).Branch == "help-editor/test", "Creates a separate help-editor branch");
    var noUpstream = await sync.StatusAsync();
    Check(!noUpstream.HasTrackingBranch && noUpstream.TrackingBranch is null && noUpstream.ComparisonBranch is null && !noUpstream.HasComparison, "An unpushed branch distinguishes missing upstream from a synchronized zero-zero comparison");
    string unpushedHead = await Git(copy, "rev-parse", "HEAD");
    await Reject(() => sync.PullAsync(), "A new unpushed branch reports that it has no matching branch to pull");
    Check(await Git(copy, "rev-parse", "HEAD") == unpushedHead && (await sync.StatusAsync()).Changes.Count == 0, "Missing-upstream pull preserves the clean working copy");
    await Git(copy, "remote", "set-url", "origin", "https://fake-user:fake-secret@github.com/iain-buchan/statisticalhelp.git?access_token=fake-query");
    string displayedRemote = (await sync.StatusAsync()).Remote ?? "";
    Check(!displayedRemote.Contains("fake-user") && !displayedRemote.Contains("fake-secret") && !displayedRemote.Contains("fake-query"), "Status redacts configured remote credentials and query tokens without contacting the remote");
    await Git(copy, "remote", "set-url", "origin", remote);
    await Git(copy, "mv", "--", "Content/basics/example.htm", "Content/basics/renamed page α.htm");
    var renamed = await sync.StatusAsync();
    Check(renamed.Changes.Any(c => c.Status.Contains('R') && c.Path == "Content/basics/renamed page α.htm"), "Status preserves spaces and Unicode in renamed topic paths");
    await Git(copy, "mv", "--", "Content/basics/renamed page α.htm", "Content/basics/example.htm");
    await Git(copy, "mv", "--", "Content/basics/example.htm", "Docs/moved source.htm");
    var movedOutsideSource = await sync.StatusAsync();
    Check(movedOutsideSource.Changes.Single().OriginalPath == "Content/basics/example.htm" && movedOutsideSource.SourceChangeCount == 1 && movedOutsideSource.OtherChangeCount == 0, "Moving a topic out of source retains its original path and source-change classification");
    await Git(copy, "mv", "--", "Docs/moved source.htm", "Content/basics/example.htm");
    string topicPath = "basics/example.htm", topic = project.ReadTopic(topicPath);
    project.SaveTopic(topicPath, topic.Replace("Example topic", "Git-edited topic"));
    File.WriteAllText(Path.Combine(copy, "Docs", "index.html"), "Unstaged generated-output edit.");
    var mixedChanges = await sync.StatusAsync();
    Check(mixedChanges.SourceChangeCount == 1 && mixedChanges.OtherChangeCount == 1 && mixedChanges.Changes.Count == 2, "Working status separates editable source changes from generated or other files");
    await sync.CommitAsync("Edit source in local fixture", "Iain Buchan", "buchan@liverpool.ac.uk");
    Check((await Git(copy, "log", "-1", "--format=%an <%ae>|%cn <%ce>")) == "Iain Buchan <buchan@liverpool.ac.uk>|Iain Buchan <buchan@liverpool.ac.uk>", "Commit records Iain as author and committer");
    Check((await Git(copy, "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")) == "Content/basics/example.htm", "Commit includes only edited source");
    Check(!(await Git(copy, "ls-tree", "-r", "--name-only", "HEAD")).Contains(".help-editor-recovery/") && File.Exists(recoveryFile), "Source commits exclude deleted-page recovery archives and retain the local copy");
    Check((await Git(copy, "show", "HEAD:Docs/index.html")) == "Published help remains unchanged.", "Generated help is excluded from the source commit");
    Check((await sync.StatusAsync()).Changes.Any(c => c.Path == "Docs/index.html"), "Unstaged generated-output edits remain visible for review");
    var onlyGenerated = await sync.StatusAsync();
    Check(onlyGenerated.SourceChangeCount == 0 && onlyGenerated.OtherChangeCount == 1, "A source-only commit leaves only the generated change in status");
    await Git(copy, "add", "--", "Docs/index.html"); string headBefore = await Git(copy, "rev-parse", "HEAD");
    await Reject(() => sync.CommitAsync("Should be refused", "Iain Buchan", "buchan@liverpool.ac.uk"), "Refuses a commit when unrelated output is already staged");
    Check(await Git(copy, "rev-parse", "HEAD") == headBefore, "Rejected commit leaves history unchanged");
    await Git(copy, "restore", "--staged", "--", "Docs/index.html"); await Git(copy, "restore", "--", "Docs/index.html");
    await sync.PushAsync();
    Check(await Git(remote, "rev-parse", "refs/heads/help-editor/test") == headBefore, "Push updates only the working branch in the local test remote");
    Check(await Git(remote, "rev-parse", "refs/heads/master") == masterBefore, "Default branch and published source remain unchanged");
    status = await sync.StatusAsync(); Check(status.HasTrackingBranch && status.HasComparison && status.Ahead == 0 && status.Behind == 0 && status.Changes.Count == 0, "Status reports a clean branch matching its cached upstream ref");
    string recoveryHead = await Git(copy, "rev-parse", "HEAD");
    await Git(copy, "add", "-f", "--", ".help-editor-recovery");
    var stagedRecovery = await sync.StatusAsync();
    Check(stagedRecovery.OtherChangeCount == 1 && stagedRecovery.Changes.Single().Path.StartsWith(".help-editor-recovery/"), "Manually staged recovery data remains visible instead of being hidden by local-status filtering");
    await Reject(() => sync.CommitAsync("Must not include recovery data", "Iain Buchan", "buchan@liverpool.ac.uk"), "The commit guard refuses manually staged recovery archives");
    await Reject(() => sync.PullAsync(), "Pull refuses manually staged recovery data without overwriting the archive");
    Check(await Git(copy, "rev-parse", "HEAD") == recoveryHead && File.Exists(recoveryFile), "Rejected recovery commit and pull preserve history and the recovery file");
    await Git(copy, "restore", "--staged", "--", ".help-editor-recovery");
    string movedRecovery = ".help-editor-recovery/moved-source.htm";
    await Git(copy, "mv", "--", "Content/basics/example.htm", movedRecovery);
    var renamedRecovery = await sync.StatusAsync();
    Check(renamedRecovery.Changes.Single().OriginalPath == "Content/basics/example.htm" && renamedRecovery.SourceChangeCount == 1, "A tracked topic moved into recovery is still visible as a source change");
    await Reject(() => sync.CommitAsync("Must not move source into recovery", "Iain Buchan", "buchan@liverpool.ac.uk"), "The commit guard refuses a staged source rename into recovery");
    await Git(copy, "mv", "--", movedRecovery, "Content/basics/example.htm");
    await Git(copy, "branch", "--unset-upstream");
    var matchingWithoutTracking = await sync.StatusAsync();
    Check(!matchingWithoutTracking.HasTrackingBranch && matchingWithoutTracking.ComparisonBranch == "refs/remotes/origin/help-editor/test" && matchingWithoutTracking.HasComparison, "A matching cached origin ref does not pretend that upstream tracking is configured");
    await Git(copy, "branch", "--set-upstream-to", "origin/help-editor/test");
    await Git(copy, "update-ref", "-d", "refs/remotes/origin/help-editor/test");
    var goneTracking = await sync.StatusAsync();
    Check(goneTracking.HasTrackingBranch && goneTracking.TrackingBranch == "origin/help-editor/test" && !goneTracking.HasComparison, "A missing upstream ref remains distinguishable from a valid zero-zero comparison");
    await sync.FetchAsync();
    File.Delete(Path.Combine(copy, "RCode", "example.R")); Directory.Delete(Path.Combine(copy, "RCode"));
    await sync.CommitAsync("Remove a retired source-script directory", "Iain Buchan", "buchan@liverpool.ac.uk");
    var aheadStatus = await sync.StatusAsync();
    Check(aheadStatus.HasComparison && aheadStatus.Ahead == 1 && aheadStatus.Behind == 0, "An unpushed local commit reports ahead-only status");
    Check((await Git(copy, "diff-tree", "--no-commit-id", "--name-status", "-r", "HEAD")).Contains("D\tRCode/example.R"), "Commit includes tracked source deletions even when the entire directory is gone");
    Check(await Git(copy, "show", "HEAD:Docs/index.html") == "Published help remains unchanged.", "Committing a source-directory deletion still excludes published output");
    await sync.PushAsync();
    string colleague = Path.Combine(fixtures, "colleague"); await Git(fixtures, "clone", "--no-hardlinks", remote, colleague); await Git(colleague, "switch", "--track", "origin/help-editor/test");
    string colleagueFile = Path.Combine(colleague, "Content", "basics", "example.htm");
    File.WriteAllText(colleagueFile, File.ReadAllText(colleagueFile).Replace("Before &amp; after", "Collaboratorchange Before &amp; after"));
    await Git(colleague, "add", "--", "Content"); await Git(colleague, "commit", "-m", "Colleague source edit"); await Git(colleague, "push");
    var beforeRemoteCheck = await sync.StatusAsync();
    Check(beforeRemoteCheck.Behind == 0 && beforeRemoteCheck.LastSuccessfulFetchUtc is not null, "Offline status retains cached counts and the older fetch time until an explicit check");
    var fetchStarted = DateTimeOffset.UtcNow;
    await sync.FetchAsync(); status = await sync.StatusAsync(); Check(status.HasComparison && status.Ahead == 0 && status.Behind == 1, "Fetch reports a new remote source commit");
    Check(status.LastSuccessfulFetchUtc >= fetchStarted && status.LastSuccessfulFetchUtc <= status.ObservedAtUtc && status.LastSuccessfulFetchUtc > beforeRemoteCheck.LastSuccessfulFetchUtc, "Only successful fetching advances the remote-check receipt time");
    var reopenedStatus = await new GitSyncService(project).StatusAsync();
    Check(reopenedStatus.LastSuccessfulFetchUtc == status.LastSuccessfulFetchUtc && reopenedStatus.Changes.Count == 0, "Reopening the service preserves the local fetch receipt without changing source or working status");
    await sync.PullAsync(); Check(project.ReadTopic(topicPath).Contains("Collaboratorchange"), "Pull fast-forwards the working branch and updates source");
    string saved = project.ReadTopic(topicPath); project.SaveTopic(topicPath, saved.Replace("Collaboratorchange", "Unsavedlocalwork"));
    await Reject(() => sync.PullAsync(), "Pull refuses a dirty working copy without stashing or resetting");
    Check(project.ReadTopic(topicPath).Contains("Unsavedlocalwork"), "Refused pull preserves local work");
    project.SaveTopic(topicPath, saved);
    project.SaveTopic(topicPath, saved.Replace("Collaboratorchange", "Localbranchchange")); await sync.CommitAsync("Local divergent commit", "Iain Buchan", "buchan@liverpool.ac.uk");
    File.WriteAllText(colleagueFile, File.ReadAllText(colleagueFile).Replace("Collaboratorchange", "Remotechangedagain")); await Git(colleague, "add", "Content"); await Git(colleague, "commit", "-m", "Remote divergent commit"); await Git(colleague, "push");
    string divergentHead = await Git(copy, "rev-parse", "HEAD"); await Reject(() => sync.PullAsync(), "Divergent pull requires explicit resolution and never creates an automatic merge");
    var divergedStatus = await sync.StatusAsync();
    Check(divergedStatus.HasComparison && divergedStatus.Ahead == 1 && divergedStatus.Behind == 1, "A diverged branch reports both local and remote commits");
    Check(await Git(copy, "rev-parse", "HEAD") == divergentHead && project.ReadTopic(topicPath).Contains("Localbranchchange"), "Divergent pull preserves the local commit and source");
    await Reject(() => sync.PushAsync(), "Non-fast-forward push is refused without forcing the remote");
    await Reject(() => sync.CreateBranchAsync("main"), "Refuses a new main branch as an editor working branch");
    await Reject(() => GitSyncService.CloneAsync(remote, copy), "Clone refuses to overwrite an existing working copy");
    var lastSuccessfulFetch = (await sync.StatusAsync()).LastSuccessfulFetchUtc;
    await Git(copy, "remote", "set-url", "origin", Path.Combine(fixtures, "missing-remote.git"));
    Check((await sync.StatusAsync()).LastSuccessfulFetchUtc is null, "Changing origin hides the old remote's fetch receipt");
    await Reject(() => sync.FetchAsync(), "An unavailable origin reports fetch failure");
    await Git(copy, "remote", "set-url", "origin", remote);
    Check((await sync.StatusAsync()).LastSuccessfulFetchUtc == lastSuccessfulFetch, "A failed check for a changed origin does not erase the previous origin's successful receipt");
    await Git(copy, "config", "remote.origin.uploadpack", "does-not-exist-statsdirect-upload-pack");
    await Reject(() => sync.FetchAsync(), "A failed fetch from the same origin is reported to the caller");
    Check((await sync.StatusAsync()).LastSuccessfulFetchUtc == lastSuccessfulFetch, "A failed fetch from the same origin does not falsely advance its successful-check time");
    await Git(copy, "config", "--unset", "remote.origin.uploadpack");
    string goodReceipt = await Git(copy, "config", "--local", "--get", "statsdirectHelpEditor.lastSuccessfulFetch");
    await Git(copy, "config", "--local", "statsdirectHelpEditor.lastSuccessfulFetch", "malformed-cache");
    Check((await sync.StatusAsync()).LastSuccessfulFetchUtc is null, "Malformed local receipt metadata cannot claim that the remote was checked");
    await Git(copy, "config", "--local", "statsdirectHelpEditor.lastSuccessfulFetch", goodReceipt);
    using var canceled = new CancellationTokenSource(); canceled.Cancel(); await Reject(() => sync.FetchAsync(canceled.Token), "Cancellation is honored before a Git operation starts");
    string slowFlag = Path.Combine(fixtures, "slow-upload-started.txt");
    string QuoteGitShell(string value) => "'" + value.Replace('\\', '/').Replace("'", "'\\''") + "'";
    string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The current test host path is unavailable.");
    string helperCommand = QuoteGitShell(processPath);
    if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        helperCommand += " " + QuoteGitShell(typeof(LongPathChecks).Assembly.Location);
    helperCommand += " --slow-upload-pack " + QuoteGitShell(slowFlag);
    await Git(copy, "config", "remote.origin.uploadpack", helperCommand);
    string cancellationHead = await Git(copy, "rev-parse", "HEAD"), cancellationSource = project.ReadTopic(topicPath);
    using (var duringFetch = new CancellationTokenSource())
    {
        Task operation = sync.FetchAsync(duringFetch.Token);
        var startedAt = Stopwatch.StartNew();
        int helperPid = 0;
        while (helperPid == 0 && !operation.IsCompleted && startedAt.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (File.Exists(slowFlag)) int.TryParse(File.ReadAllText(slowFlag), out helperPid);
            if (helperPid == 0) await Task.Delay(30);
        }
        bool helperStarted = helperPid > 0;
        Check(helperStarted, "The local slow-fetch helper started before cancellation was tested");
        duringFetch.Cancel();
        bool actualCancellation = false, helperExited = false; string? cancellationFailure = null;
        try { await operation.WaitAsync(TimeSpan.FromSeconds(5)); cancellationFailure = "Fetch completed without an OperationCanceledException."; }
        catch (OperationCanceledException) { actualCancellation = true; }
        catch (Exception error) { cancellationFailure = error.ToString(); }
        if (helperStarted)
        {
            try { using var helper = Process.GetProcessById(helperPid); await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); helperExited = helper.HasExited; }
            catch (ArgumentException) { helperExited = true; }
            catch (TimeoutException) { }
        }
        slowFetchEvidence = new { helperPid, helperStarted, startupMilliseconds = startedAt.Elapsed.TotalMilliseconds, actualCancellation, helperExited, cancellationFailure, helperHost = processPath };
        if (cancellationFailure != null) Console.Error.WriteLine(cancellationFailure);
        Check(helperStarted && actualCancellation && helperExited, "Cancellation stops an already running Git fetch promptly");
        Check(await Git(copy, "rev-parse", "HEAD") == cancellationHead && project.ReadTopic(topicPath) == cancellationSource, "Cancelling an active fetch preserves the current commit and authoring source");
        Check((await sync.StatusAsync()).LastSuccessfulFetchUtc == lastSuccessfulFetch, "Cancelling an active fetch preserves the previous successful-check timestamp");
    }
    await Git(copy, "config", "--unset", "remote.origin.uploadpack");
    Check(await Git(remote, "rev-parse", "refs/heads/master") == masterBefore, "All fixture operations leave the default release branch unchanged");
    if (OperatingSystem.IsWindows()) await LongPathChecks.RunAsync(root, fixtures, Check);
}
catch (Exception ex) { failed++; checks.Add(new { name = "Unexpected Git check failure", passed = false, error = ex.ToString() }); Console.Error.WriteLine(ex); }
File.WriteAllText(Path.Combine(evidence, "git-checks.json"), JsonSerializer.Serialize(new { passed, failed, fixtures, remoteType = "Local bare repository only; no GitHub writes", slowFetch = slowFetchEvidence, results = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{passed} Git checks passed; {failed} failed");
return failed == 0 ? 0 : 1;
