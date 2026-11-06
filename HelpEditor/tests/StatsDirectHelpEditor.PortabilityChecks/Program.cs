using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args[0]);
string evidence = Path.Combine(root, "evidence", "ReviewImprovements-20261104", "Portability");
string fixture = Path.Combine(evidence, "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
var checks = new List<object>(); int failed = 0;
void Check(bool success, string name) { checks.Add(new { name, passed = success }); Console.WriteLine((success ? "PASS " : "FAIL ") + name); if (!success) failed++; }
void Reject(Action action, string name)
{ try { action(); Check(false, name); } catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { Check(true, name); } }
object? Call(Type type, string name, params object[] values)
{ try { return type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values); } catch (TargetInvocationException error) { ExceptionDispatchInfo.Capture(error.InnerException!).Throw(); throw; } }
string configuration = Path.Combine(fixture, "protection.json");
string? oldConfiguration = Environment.GetEnvironmentVariable(WorkspaceProtectionSettings.ConfigurationEnvironmentVariable);
string? originalPath = Environment.GetEnvironmentVariable("PATH"); string originalDirectory = Environment.CurrentDirectory;
string protectedRoot = Path.Combine(fixture, "Protected"), source = Path.Combine(fixture, "Source");
void WritePolicy(params string[] folders) => File.WriteAllText(configuration, JsonSerializer.Serialize(new { schemaVersion = 1, protectedPaths = folders }));
var safePaths = typeof(HelpProject).Assembly.GetType("StatsDirect.HelpEditor.Core.SafePaths")!;
string GitPath()
{
    foreach (string part in (originalPath ?? "").Split(Path.PathSeparator))
    {
        string folder = part.Trim().Trim('"'); if (!Path.IsPathFullyQualified(folder)) continue;
        string file = Path.Combine(folder, OperatingSystem.IsWindows() ? "git.exe" : "git"); if (File.Exists(file)) return Path.GetFullPath(file);
    }
    throw new IOException("A real Git executable is required for the isolated local-repository check.");
}
async Task Run(string executable, string directory, params string[] arguments)
{
    var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string item in arguments) start.ArgumentList.Add(item);
    start.Environment["GIT_TERMINAL_PROMPT"] = "0"; start.Environment["GCM_INTERACTIVE"] = "Never";
    using var child = Process.Start(start)!; var output = child.StandardOutput.ReadToEndAsync(); var errors = child.StandardError.ReadToEndAsync();
    await child.WaitForExitAsync(); if (child.ExitCode != 0) throw new IOException(await errors); await output;
}
try
{
    Environment.SetEnvironmentVariable(WorkspaceProtectionSettings.ConfigurationEnvironmentVariable, configuration);
    Check(WorkspaceProtectionSettings.Load().ProtectedPaths.Count == 0, "An absent local policy adds no personal machine paths");
    Directory.CreateDirectory(protectedRoot); WritePolicy(protectedRoot);
    var policy = WorkspaceProtectionSettings.Load();
    Check(policy.ProtectedPaths.Count == 1 && policy.IsProtected(Path.Combine(protectedRoot, "nested", "output")) && !policy.IsProtected(protectedRoot + "-sibling"), "Configured protection covers descendants without blocking similarly named siblings");
    Reject(() => policy.RequireUnprotected(Path.Combine(protectedRoot, "output"), "Build"), "A protected destination fails before a mutation");
    string volume = Path.GetPathRoot(fixture)!; WritePolicy(volume); Check(WorkspaceProtectionSettings.Load().IsProtected(fixture), "A configured drive/root protection retains its absolute identity");
    foreach (string invalid in new[] { "{}", "{broken", "{\"schemaVersion\":\"1\",\"protectedPaths\":[]}", "{\"schemaVersion\":1,\"protectedPaths\":[\"relative\"]}", "{\"schemaVersion\":1,\"protectedPaths\":[],\"unexpected\":true}", "{\"schemaVersion\":1,\"protectedPaths\":\"not an array\"}" })
    { File.WriteAllText(configuration, invalid); Reject(() => WorkspaceProtectionSettings.Load(), "An invalid existing protection policy fails closed"); }
    WritePolicy(protectedRoot, protectedRoot); Reject(() => WorkspaceProtectionSettings.Load(), "Duplicate protected paths are rejected instead of silently normalised");
    WritePolicy(protectedRoot);
    using (var locked = new FileStream(configuration, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Reject(() => WorkspaceProtectionSettings.Load(), "An unreadable protection policy fails closed");
    policy = WorkspaceProtectionSettings.Load();
    Check(!policy.IsProtected(policy.FindSafeTemporaryParent(source)), "Temporary staging is selected outside protected/source paths without creating a workspace");
    var allowedClouds = Enumerable.Range(0, 16).Select(value => 0x9000001Au + (uint)value * 0x1000u).Concat(new[] { 0x80000015u, 0x80000021u }).ToArray();
    Check(allowedClouds.All(tag => (bool)Call(safePaths, "AllowedCloudTag", tag)!), "Only documented Cloud0-F/legacy OneDrive placeholder tags are admitted by the policy");
    Check(new uint[] { 0, 0xA0000003, 0xA000000C, 0xA000001A, 0x8000001B, 0x80000017, 0x900000FF }.All(tag => !(bool)Call(safePaths, "AllowedCloudTag", tag)!), "Junction/symlink/name-surrogate and unknown tags remain blocked");
    string missing = Path.Combine(fixture, "not-created", "future.txt"); Call(safePaths, "CheckNoLinks", missing); Check(!File.Exists(missing), "Ancestor checks accept an absent output without creating it");
    if (OperatingSystem.IsWindows()) Reject(() => Call(safePaths, "ReadReparseTag", missing), "A failed native reparse query fails closed");
    Directory.CreateDirectory(Path.Combine(source, "Content")); Directory.CreateDirectory(Path.Combine(source, "Project", "TOCs"));
    File.WriteAllText(Path.Combine(source, HelpProject.MarkerFile), "{}"); File.WriteAllText(Path.Combine(source, "Project", "TOCs", "STATSDIRECT.fltoc"), "<CatapultToc><TocEntry Title='Probe' Link='/Content/probe.htm'/></CatapultToc>");
    File.WriteAllText(Path.Combine(source, "Content", "probe.htm"), "<html><head><title>Portability probe</title></head><body><p>Isolated fixture only.</p></body></html>");
    var project = HelpProject.Open(source); string assets = Path.Combine(root, "src", "StatsDirectHelpEditor.App", "Web");
    Reject(() => HelpBuilder.Build(project, Path.Combine(protectedRoot, "Output"), assets), "Help builds honour the local protected-folder policy");
    Check(!Directory.Exists(Path.Combine(protectedRoot, "Output")), "Rejected protected builds leave no output folder");
    var built = HelpBuilder.Build(project, Path.Combine(fixture, "Output"), assets);
    Check(built.TopicCount == 1 && File.Exists(Path.Combine(built.OutputDirectory, "index.html")), "An ordinary marked fixture still builds outside protected folders");
    Reject(() => HelpBuilder.Build(project, Path.GetPathRoot(fixture)!, assets), "The generic drive-root/output-ancestor guard remains active");
    try { await GitSyncService.CloneAsync(source, Path.Combine(protectedRoot, "Clone")); Check(false, "Clone protection"); }
    catch (InvalidOperationException) { Check(true, "Clone destination protection is checked before Git is launched"); }
    var archive = WebsiteZipExporter.Export(project, Path.Combine(protectedRoot, "Transfer.zip"), assets);
    Check(archive.TopicCount == 1 && File.Exists(archive.ZipPath), "An explicit transfer ZIP may be saved in a protected artifact folder while its staging stays separate");
    Reject(() => WebsiteZipExporter.Export(project, archive.ZipPath, assets), "An existing ZIP in a protected release folder cannot be replaced");
    string protectedZip = Path.Combine(fixture, "Reserved release.zip");
    WritePolicy(protectedRoot, protectedZip);
    Reject(() => WebsiteZipExporter.Export(project, protectedZip, assets), "A protected release filename cannot be used for a new ZIP");
    Check(!File.Exists(protectedZip), "Refusing a protected filename leaves it untouched");
    string executableName = OperatingSystem.IsWindows() ? "git.exe" : "git";
    string fakeBin = Path.Combine(fixture, "ExplicitTestPath"); Directory.CreateDirectory(fakeBin); File.WriteAllText(Path.Combine(fakeBin, executableName), "Not executed; resolver-only fixture.");
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(fakeBin, executableName), UnixFileMode.UserRead | UnixFileMode.UserExecute);
    Environment.SetEnvironmentVariable("PATH", fakeBin);
    Check((string)Call(typeof(GitSyncService), "ResolveGitExecutable")! == Path.GetFullPath(Path.Combine(fakeBin, executableName)), "An explicitly supplied absolute test PATH still selects its requested executable");
    File.WriteAllText(Path.Combine(fixture, executableName), "Never execute this current-directory fixture.");
    Environment.CurrentDirectory = fixture; Environment.SetEnvironmentVariable("PATH", "." + Path.PathSeparator);
    Reject(() => Call(typeof(GitSyncService), "ResolveGitExecutable"), "Implicit current-directory and relative PATH Git lookup is refused");
    string realGit = GitPath(); Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(realGit));
    Check((string)Call(typeof(GitSyncService), "ResolveGitExecutable")! == realGit, "Git resolves to its actual absolute PATH executable instead of the current-directory fixture");
    await Run(realGit, source, "init", "--initial-branch=help-editor-test");
    var status = await new GitSyncService(project).StatusAsync();
    Check(status.IsRepository && status.Branch == "help-editor-test", "The actual Git runner uses the absolute executable and preserves normal local status behaviour");
    if (OperatingSystem.IsWindows())
    {
        string target = Path.Combine(fixture, "JunctionTarget"), junction = Path.Combine(fixture, "Junction"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "sentinel.txt"), "Do not change");
        await Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), fixture, "/d", "/c", "mklink", "/J", junction, target);
        Reject(() => Call(safePaths, "CheckNoLinks", Path.Combine(junction, "missing", "output.txt")), "A real Windows junction ancestor is rejected even when the final file does not exist");
        Reject(() => WorkspaceProtectionSettings.Load(Path.Combine(junction, "config.json")), "A policy read through a junction fails closed before treating the file as absent");
        Reject(() => Call(safePaths, "AtomicWrite", Path.Combine(junction, "new.txt"), "blocked"), "Mutation rechecks reject a real linked destination");
        Check(File.ReadAllText(Path.Combine(target, "sentinel.txt")) == "Do not change" && !File.Exists(Path.Combine(target, "new.txt")), "Rejected linked writes preserve the target folder");
    }
}
catch (Exception error) { Check(false, "Unexpected portability check failure: " + error); }
finally { Environment.CurrentDirectory = originalDirectory; Environment.SetEnvironmentVariable("PATH", originalPath); Environment.SetEnvironmentVariable(WorkspaceProtectionSettings.ConfigurationEnvironmentVariable, oldConfiguration); }
var report = new { checkedAtUtc = DateTimeOffset.UtcNow, passed = failed == 0, failed, count = checks.Count, fixture, checks, noActualOneDriveChanges = true, noUserPolicyChanges = true };
File.WriteAllText(Path.Combine(evidence, "checks.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { report.passed, report.failed, report.count, fixture })); return failed == 0 ? 0 : 1;
