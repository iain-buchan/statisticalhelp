using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StatsDirect.HelpEditor.Core;

if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "git", StringComparison.OrdinalIgnoreCase))
{
    string forwardedGit = Environment.GetEnvironmentVariable("STATSDIRECT_DEEP_GIT_REAL") ?? @"C:\Program Files\Git\cmd\git.exe";
    async Task GateAsync()
    {
        string gate = Environment.GetEnvironmentVariable("STATSDIRECT_DEEP_GIT_GATE")!;
        File.WriteAllText(gate + ".ready", "ready");
        var until = Stopwatch.StartNew();
        while (!File.Exists(gate + ".release") && until.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(10);
    }
    bool hasGate = Environment.GetEnvironmentVariable("STATSDIRECT_DEEP_GIT_GATE") != null;
    bool afterUpdate = Environment.GetEnvironmentVariable("STATSDIRECT_DEEP_GIT_PHASE") == "after-update-ref";
    if (hasGate && !afterUpdate && (args.Contains("commit") || args.Contains("commit-tree"))) await GateAsync();
    var proxyStart = new ProcessStartInfo(forwardedGit) { UseShellExecute = false };
    foreach (var arg in args) proxyStart.ArgumentList.Add(arg);
    proxyStart.Environment.Remove("STATSDIRECT_DEEP_GIT_REAL");
    using var proxy = Process.Start(proxyStart)!;
    await proxy.WaitForExitAsync();
    if (hasGate && afterUpdate && args.Contains("update-ref") && proxy.ExitCode == 0) await GateAsync();
    Environment.ExitCode = proxy.ExitCode; return;
}

string root = Path.GetFullPath(args[0]);
string fixture = Path.Combine(root, "evidence", "DeepSourceFixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var checks = new List<object>(); var issues = new List<object>();
string reportPath = Path.Combine(root, "evidence", "deep-source-checks.json");
File.WriteAllText(reportPath, JsonSerializer.Serialize(new { startedAtUtc = DateTimeOffset.UtcNow, completed = false, passed = 0, failed = 0 }));
AppDomain.CurrentDomain.UnhandledException += (_, failure) => File.WriteAllText(reportPath,
    JsonSerializer.Serialize(new { checkedAtUtc = DateTimeOffset.UtcNow, completed = false, passed = checks.Count(x => JsonSerializer.SerializeToElement(x).GetProperty("passed").GetBoolean()), failed = 1, error = failure.ExceptionObject.ToString(), checks }));
void Check(bool value, string name, object? detail = null) { checks.Add(new { name, passed = value, detail }); Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}"); }
string realGit = @"C:\Program Files\Git\cmd\git.exe";
async Task<string> Git(string cwd, params string[] arguments)
{
    var start = new ProcessStartInfo(realGit) { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in new[] { "-c", "user.name=Deep checks", "-c", "user.email=deep@example.invalid", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false" }.Concat(arguments)) start.ArgumentList.Add(arg);
    start.Environment.Remove("STATSDIRECT_DEEP_GIT_REAL");
    using var p = Process.Start(start)!; var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
    await p.WaitForExitAsync(); string text = await output, errors = await error;
    if (p.ExitCode != 0) throw new InvalidOperationException(errors); return text.Trim();
}
void Write(string relative, string content)
{
    string path = Path.Combine(fixture, relative.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, new UTF8Encoding(false));
}
var actual = HelpProject.Open(Path.Combine(root, "HelpSource"));
var timings = new List<double>();
for (int i = 0; i < 5; i++) { var sw = Stopwatch.StartNew(); var pages = actual.ListTopics(); timings.Add(sw.Elapsed.TotalMilliseconds); Check(pages.Count == 404, "Read-only real corpus catalog returns every page", new { milliseconds = sw.Elapsed.TotalMilliseconds }); }
Write(HelpProject.MarkerFile, "{}");
Write("StatsDirect.flprj", "<CatapultProject />");
Write("Content/contents.htm", "<html><head><title>Contents</title></head><body><h1>Contents</h1><p><a href='basics/example.htm'>Example</a></p></body></html>");
const string original = "<html><head><title>Example</title></head><body><h1>Example</h1><p>Alpha wording.</p></body></html>";
Write("Content/basics/example.htm", original);
Write("Content/resources/stylesheets/statsdirect.css", "body { font: 10pt Arial; }");
Write("Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title='Contents' Link='/Content/contents.htm'><TocEntry Title='Example' Link='/Content/basics/example.htm' /></TocEntry></CatapultToc>");
await Git(fixture, "init", "--initial-branch=help-editor/deep");
await Git(fixture, "add", "."); await Git(fixture, "commit", "-m", "Seed");
var project = HelpProject.Open(fixture); var sync = new GitSyncService(project);
await sync.PrepareWebsiteAsync(Path.Combine(root, "src", "StatsDirectHelpEditor.App", "Web"));
var statusStart = Stopwatch.StartNew();
var receiptPath = Path.Combine(fixture, "Docs", "build-info.json"); byte[] receipt = File.ReadAllBytes(receiptPath);
for (int i = 0; i < 3; i++) _ = await sync.StatusAsync();
Check(File.ReadAllBytes(receiptPath).SequenceEqual(receipt), "Read-only Git status never regenerates output", new { millisecondsForThree = statusStart.Elapsed.TotalMilliseconds });

string self = Environment.ProcessPath!, proxyGit = Path.Combine(AppContext.BaseDirectory, "git.exe"); File.Copy(self, proxyGit, true);
string oldPath = Environment.GetEnvironmentVariable("PATH") ?? "";
string gatePath = Path.Combine(fixture, ".git", "commit-gate");
try
{
    Environment.SetEnvironmentVariable("PATH", AppContext.BaseDirectory + Path.PathSeparator + oldPath);
    Environment.SetEnvironmentVariable("STATSDIRECT_DEEP_GIT_REAL", realGit);
    Environment.SetEnvironmentVariable("STATSDIRECT_DEEP_GIT_GATE", gatePath);
    var commit = sync.CommitWebsiteAsync("Verified source and website", "Iain Buchan", "buchan@liverpool.ac.uk");
    var wait = Stopwatch.StartNew(); while (!File.Exists(gatePath + ".ready") && !commit.IsCompleted && wait.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(10);
    Check(File.Exists(gatePath + ".ready"), "Concurrent-save test pauses the actual final commit after validation");
    Write("Content/basics/example.htm", original.Replace("Alpha", "Later"));
    File.WriteAllText(gatePath + ".release", "release");
    string? failure = null;
    try { await commit; } catch (Exception ex) { failure = ex.Message; }
    string committedSource = await Git(fixture, "show", "HEAD:Content/basics/example.htm");
    bool safe = failure == null && committedSource == original;
    Check(safe, "A save after website verification cannot enter an unverified commit", new { failure, committedSource });
    if (!safe) issues.Add(new { name = "Concurrent save enters website commit after validation", trigger = "Page saved by another window between validation and git commit", impact = "Commit contains newer source paired with old generated website/receipt" });
    Check((await Git(fixture, "diff", "--cached", "--name-only")).Length == 0, "Successful verified commit refreshes unchanged staging without an apparent source revert");
    Check(File.ReadAllText(Path.Combine(fixture, "Content/basics/example.htm")).Contains("Later wording"), "Later worktree save remains intact and uncommitted");
    Check(await Git(fixture, "log", "-1", "--format=%an <%ae>|%cn <%ce>") == "Iain Buchan <buchan@liverpool.ac.uk>|Iain Buchan <buchan@liverpool.ac.uk>", "Pinned tree preserves the requested author and committer");

    async Task<string?> Race(string title, Func<Task> mutation, string phase = "before-commit-tree")
    {
        gatePath = Path.Combine(fixture, ".git", "gate-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("STATSDIRECT_DEEP_GIT_GATE", gatePath);
        Environment.SetEnvironmentVariable("STATSDIRECT_DEEP_GIT_PHASE", phase);
        var operation = sync.CommitAsync(title, "Iain Buchan", "buchan@liverpool.ac.uk");
        var elapsed = Stopwatch.StartNew();
        while (!File.Exists(gatePath + ".ready") && !operation.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(10);
        Check(File.Exists(gatePath + ".ready"), "Reached deterministic commit race: " + title);
        if (File.Exists(gatePath + ".ready")) await mutation();
        File.WriteAllText(gatePath + ".release", "release");
        try { return await operation; } catch (Exception ex) { return "ERROR: " + ex.Message; }
        finally { Environment.SetEnvironmentVariable("STATSDIRECT_DEEP_GIT_GATE", null); Environment.SetEnvironmentVariable("STATSDIRECT_DEEP_GIT_PHASE", null); }
    }
    string priorHead = await Git(fixture, "rev-parse", "HEAD");
    Write("Content/basics/example.htm", original.Replace("Alpha", "Second"));
    await Race("Outside staging", async () => { Write("README.md", "Late staged outside source"); await Git(fixture, "add", "README.md"); });
    Check((await Git(fixture, "ls-tree", "--name-only", "HEAD", "--", "README.md")).Length == 0 && await Git(fixture, "diff", "--cached", "--name-only") == "README.md", "A late outside staged edit stays staged and does not enter the source commit");
    Check(await Git(fixture, "rev-parse", "HEAD^") == priorHead, "Pinned commit has exactly the reviewed parent");
    await Git(fixture, "restore", "--staged", "README.md");

    Write("Content/basics/example.htm", original.Replace("Alpha", "Third"));
    await Race("Source staging", async () => { Write("Content/basics/example.htm", original.Replace("Alpha", "Later staged")); await Git(fixture, "add", "Content"); });
    Check((await Git(fixture, "show", "HEAD:Content/basics/example.htm")).Contains("Third wording"), "A late source staging operation cannot replace the reviewed source tree");
    Check((await Git(fixture, "show", ":Content/basics/example.htm")).Contains("Later staged wording") && File.ReadAllText(Path.Combine(fixture, "Content/basics/example.htm")).Contains("Later staged wording"), "A newer staged source edit and its worktree bytes are preserved");
    await Git(fixture, "restore", "--staged", "Content");

    Write("Content/basics/example.htm", original.Replace("Alpha", "Fourth"));
    string externalHead = "";
    string? headFailure = await Race("Concurrent HEAD advance", async () => { await Git(fixture, "commit", "--allow-empty", "-m", "External commit"); externalHead = await Git(fixture, "rev-parse", "HEAD"); });
    Check(headFailure?.StartsWith("ERROR:") == true && await Git(fixture, "rev-parse", "HEAD") == externalHead, "Concurrent HEAD advance is refused without overwriting the external commit");

    Write("Content/basics/example.htm", original.Replace("Alpha", "Fifth"));
    string pinnedBranchHead = await Git(fixture, "rev-parse", "HEAD");
    string? branchFailure = await Race("Switch before ref update", async () => { await Git(fixture, "switch", "-c", "switched-before"); });
    Check(branchFailure?.StartsWith("ERROR:") == true && await Git(fixture, "symbolic-ref", "--short", "HEAD") == "switched-before" && await Git(fixture, "rev-parse", "refs/heads/help-editor/deep") == pinnedBranchHead,
        "Switching to a same-commit branch before update does not update either branch");
    await Git(fixture, "switch", "help-editor/deep");

    Write("Content/basics/example.htm", original.Replace("Alpha", "Sixth"));
    string stagedAfterSwitch = "";
    string? afterNotice = await Race("Switch after ref update", async () => { await Git(fixture, "switch", "-c", "switched-after"); stagedAfterSwitch = await Git(fixture, "ls-files", "--stage"); }, "after-update-ref");
    Check(afterNotice?.Contains("staging could not be refreshed") == true && await Git(fixture, "ls-files", "--stage") == stagedAfterSwitch,
        "A branch switch after successful commit preserves the new branch staging and reports refresh-only notice");
    await Git(fixture, "restore", "--staged", ".");

    Environment.SetEnvironmentVariable("STATSDIRECT_DEEP_GIT_GATE", null);
    Write(".gitattributes", "*.htm filter=alter-text\n"); await Git(fixture, "config", "filter.alter-text.clean", "sed s/Sixth/Filtered/");
    Write("Content/basics/example.htm", original.Replace("Alpha", "Seventh"));
    string filterHead = await Git(fixture, "rev-parse", "HEAD"); string? filterFailure = null;
    try { await sync.CommitAsync("Filtered source", "Iain Buchan", "buchan@liverpool.ac.uk"); } catch (Exception ex) { filterFailure = ex.Message; }
    Check(filterFailure?.Contains("content filters") == true && await Git(fixture, "rev-parse", "HEAD") == filterHead, "Custom clean filters cannot silently change verified source in Git");
    File.Delete(Path.Combine(fixture, ".gitattributes"));
    Write("RCode/example.R", "mean(c(1, 2))\n"); await sync.CommitAsync("Add R recipe", "Iain Buchan", "buchan@liverpool.ac.uk");
    await Git(fixture, "rm", "RCode/example.R");
    await sync.CommitAsync("Remove the last R recipe", "Iain Buchan", "buchan@liverpool.ac.uk");
    Check((await Git(fixture, "ls-tree", "-r", "--name-only", "HEAD", "--", "RCode")).Length == 0 && (await Git(fixture, "diff", "--cached", "--name-only")).Length == 0,
        "Removing the final staged file in a source folder commits the deletion without requiring the folder to exist");
}
finally
{
    Environment.SetEnvironmentVariable("PATH", oldPath);
    Environment.SetEnvironmentVariable("STATSDIRECT_DEEP_GIT_REAL", null);
    Environment.SetEnvironmentVariable("STATSDIRECT_DEEP_GIT_GATE", null);
    Environment.SetEnvironmentVariable("STATSDIRECT_DEEP_GIT_PHASE", null);
    if (File.Exists(proxyGit)) File.Delete(proxyGit);
}

Directory.CreateDirectory(Path.Combine(root, "evidence"));
File.WriteAllText(reportPath, JsonSerializer.Serialize(new { checkedAtUtc = DateTimeOffset.UtcNow, completed = true, passed = checks.Count(x => JsonSerializer.SerializeToElement(x).GetProperty("passed").GetBoolean()), failed = checks.Count(x => !JsonSerializer.SerializeToElement(x).GetProperty("passed").GetBoolean()), issues, listTopicsMilliseconds = timings, checks }, new JsonSerializerOptions { WriteIndented = true }));
Environment.ExitCode = checks.Any(x => !JsonSerializer.SerializeToElement(x).GetProperty("passed").GetBoolean()) ? 1 : 0;
