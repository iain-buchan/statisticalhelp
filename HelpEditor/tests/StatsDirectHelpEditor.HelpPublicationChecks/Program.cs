using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args[0]);
string evidence = Path.Combine(root, "evidence/HelpEditor100Release/UnifiedPublicationCore"); Directory.CreateDirectory(evidence);
string fixtures = Path.Combine(Path.GetTempPath(), "sdhe-pubchecks-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(fixtures);
var checks = new List<object>(); int failed = 0;
void Check(bool value, string name, object? detail = null) { checks.Add(new { passed = value, name, detail }); if (!value) failed++; Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
async Task<Fixture> New(bool nested = true) { var f = new Fixture(Path.Combine(fixtures, Guid.NewGuid().ToString("N")[..8]), nested); await f.Initialize(); return f; }
async Task Reject(Func<Task> action, string name)
{ try { await action(); Check(false, name); } catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or OperationCanceledException) { Check(true, name); } }
async Task<HelpPublicationException> Fails(Fixture fixture, Action<HelpPublicationProgress>? callback = null, CancellationToken token = default)
{
    try { await HelpPublicationPublisher.PublishLocalForTestsAsync(HelpProject.Open(fixture.Source), fixture.Assets, fixture.Options, token, callback == null ? null : new InlineProgress(callback)); }
    catch (HelpPublicationException error) { return error; }
    throw new Exception("Expected a failed publication with retained state.");
}
try
{
    if (args.Contains("--shared-races-only"))
        await SharedPublicationRaceChecks.Run(root, fixtures, Check);
    else if (args.Contains("--shared-case-only"))
        await SharedGitChecks.RunCaseOnlyDocs(root, fixtures, Check);
    else
    {
    var f = await New(); var before = f.SourceHashes(); var seedBefore = f.SeedHashes(); string remoteBefore = await f.Head();
    int callerThread = 0, snapshotThread = 0; bool returnedBeforeCompletion = false;
    var started = new TaskCompletionSource<Task<HelpPublicationResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
    var caller = new Thread(() =>
    {
        callerThread = Environment.CurrentManagedThreadId;
        var publication = HelpPublicationPublisher.PublishLocalForTestsAsync(HelpProject.Open(f.Source), f.Assets, f.Options,
            progress: new InlineProgress(value => { if (value.Phase == "Snapshot") snapshotThread = Environment.CurrentManagedThreadId; }));
        returnedBeforeCompletion = !publication.IsCompleted; started.SetResult(publication);
    }) { IsBackground = true };
    caller.Start(); var result = await await started.Task;
    Check(returnedBeforeCompletion && snapshotThread != callerThread, "Snapshot hashing, copying and validation run off the calling UI thread");
    Check(result.Completed && result.GitPushed && result.GitCommit == await f.Head() && result.TargetStates.Values.All(state => state.Contains("Verified", StringComparison.OrdinalIgnoreCase)), "One operation commits/pushes the saved source, ZIP and desktop artifact with verified target states", result);
    Check(result.WebsiteZip?.TopicCount == 2 && result.DesktopHelp?.TopicCount == 2 && result.DesktopHelp.ContextCount == 2 && result.SourceSha256.Length == 64 &&
        result.WebsiteZipSha256 == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Zip))).ToLowerInvariant(),
        "Both target builds contain all saved topics, search entries and application contexts");
    Check(f.SameSource(before) && f.SameSeed(seedBefore), "The maintained source and user's checkout remain byte-for-byte unchanged");
    using (var zip = ZipFile.OpenRead(f.Zip))
    {
        Check(Read(zip, "build-info.json") == await f.Show("Docs/build-info.json") && Read(zip, "basics/example.htm") == await f.Show("Docs/basics/example.htm"), "ZIP and root Docs use the very same Public build, including its build timestamp");
        Check(Read(zip, "basics/example.htm").Contains("PUBLIC_ONLY") && !Read(zip, "basics/example.htm").Contains("DESKTOP_ONLY") &&
            File.ReadAllText(Path.Combine(f.Help, "basics/example.htm")).Contains("DESKTOP_ONLY"), "Public and Desktop preserve their distinct source conditions");
        string metadata = Read(zip, "resources/help-editor-runtime/publication.json");
        Check(metadata == await f.Show("Docs/resources/help-editor-runtime/publication.json") && JsonDocument.Parse(metadata).RootElement.GetProperty("sourceSha256").GetString() == result.SourceSha256 &&
            DesktopHelpPublisher.VerifyDestination(f.Application).PublicationSnapshotSha256 == result.SourceSha256,
            "ZIP, Docs and verified Desktop receipt explicitly identify one common frozen snapshot");
        Check(!zip.Entries.Any(entry => entry.FullName.Contains(".statsdirect-help-") || entry.FullName.Contains("Users/") || entry.FullName.Contains("Destinations/")), "Public ZIP excludes authoring/private ownership state");
    }
    Check(await f.Show("HelpEditor/HelpSource/Content/basics/example.htm") == File.ReadAllText(Path.Combine(f.Source, "Content/basics/example.htm")) &&
        await f.Show("Content/legacy.htm") == "Legacy root source remains intact", "Nested publishing updates the maintained canonical copy and preserves legacy root source");
    Check(await f.Show("Docs/CNAME") == "help.example.org" && await f.Show("HelpEditor/HelpSource/" + HelpProject.MarkerFile) == "{\"publishedMarker\":true}", "Publishing preserves repository site metadata and its original marker bytes");
    Check(result.SnapshotDirectory == null && File.Exists(result.JournalPath) && JsonDocument.Parse(File.ReadAllText(result.JournalPath)).RootElement.GetProperty("Phase").GetString() == "Completed" &&
        result.PagesDeploymentState.Contains("not been verified"), "Success removes temporary build checkout, retains its journal and does not claim Pages deployment passed");

    var legacy = await New(false); result = await HelpPublicationPublisher.PublishLocalForTestsAsync(HelpProject.Open(legacy.Source), legacy.Assets, legacy.Options);
    Check(result.Completed && await legacy.Show("Content/basics/example.htm") == File.ReadAllText(Path.Combine(legacy.Source, "Content/basics/example.htm")) && await legacy.Show("Docs/build-info.json") != "",
        "The same command still supports an explicitly detected legacy root-source repository");

    var prepared = await New(); string preparedHead = await prepared.Head();
    result = await HelpPublicationPublisher.PublishLocalForTestsAsync(HelpProject.Open(prepared.Source), prepared.Assets, prepared.Options with { PushToGitHub = false });
    Check(!result.Completed && !result.GitPushed && result.GitCommit != null && await prepared.Head() == preparedHead && Directory.Exists(result.SnapshotDirectory) && File.Exists(prepared.Zip) && Directory.Exists(prepared.Help),
        "Prepare-only mode delivers local artifacts but retains the private commit and never reports full publication");

    var invalid = await New();
    await Reject(() => HelpPublicationPublisher.PublishAsync(HelpProject.Open(invalid.Source), invalid.Assets, invalid.Options), "The normal publication API rejects filesystem remotes before network publication");
    await Reject(() => HelpPublicationPublisher.PublishLocalForTestsAsync(HelpProject.Open(invalid.Source), invalid.Assets, invalid.Options with { AllowDefaultBranchPublication = false }), "Default-branch publication requires its explicit dedicated opt-in");
    Check(!File.Exists(invalid.Zip) && !Directory.Exists(invalid.Help), "Invalid transport/branch options replace no local target");

    var race = await New(); string raceHead = await race.Head();
    var error = await Fails(race, value => { if (value.Phase == "Build") File.AppendAllText(Path.Combine(race.Source, "Content/contents.htm"), "\n"); });
    Check(!error.Result.Completed && !error.Result.GitPushed && !File.Exists(race.Zip) && !Directory.Exists(race.Help) && await race.Head() == raceHead,
        "A saved-source edit after capture stops publication before any delivery target or remote changes");
    Check(File.Exists(error.Result.JournalPath) && Directory.Exists(error.Result.SnapshotDirectory) && JsonDocument.Parse(File.ReadAllText(error.Result.JournalPath)).RootElement.GetProperty("Phase").GetString() == "Failed",
        "A failed snapshot remains available with truthful journal/target state for inspection and retry");

    var artifact = await New(); string artifactHead = await artifact.Head();
    error = await Fails(artifact, value =>
    {
        if (value.Phase != "PrepareGit") return;
        string history = Directory.GetFiles(artifact.History, "*.json").Single();
        string work = JsonDocument.Parse(File.ReadAllText(history)).RootElement.GetProperty("Result").GetProperty("SnapshotDirectory").GetString()!;
        File.AppendAllText(Path.Combine(work, "Public/contents.htm"), "TAMPERED");
    });
    Check(!File.Exists(artifact.Zip) && !Directory.Exists(artifact.Help) && await artifact.Head() == artifactHead && !error.Result.Completed,
        "A prepared artifact mutation is rejected before ZIP, Desktop or Git push");

    var zipRace = await New(); string zipRaceHead = await zipRace.Head(); File.WriteAllText(zipRace.Zip, "Original ZIP sentinel");
    error = await Fails(zipRace, value => { if (value.Phase == "Build") File.WriteAllText(zipRace.Zip, "External replacement during lengthy build"); });
    Check(File.ReadAllText(zipRace.Zip) == "External replacement during lengthy build" && !Directory.Exists(zipRace.Help) && await zipRace.Head() == zipRaceHead,
        "The ZIP destination hash is pinned before clone/build, preserving an external replacement during preparation");

    var partial = await New(); string partialHead = await partial.Head();
    error = await Fails(partial, value => { if (value.Phase == "WebsiteZip") { Directory.CreateDirectory(partial.Help); File.WriteAllText(Path.Combine(partial.Help, "unrelated.csv"), "Protected,user,data"); } });
    Check(File.Exists(partial.Zip) && error.Result.WebsiteZip != null && error.Result.DesktopHelp == null && !error.Result.GitPushed && await partial.Head() == partialHead,
        "A later desktop refusal reports the completed ZIP without falsely reporting all targets complete");
    Check(File.ReadAllText(Path.Combine(partial.Help, "unrelated.csv")) == "Protected,user,data" && error.Result.TargetStates["WebsiteZip"] == "Verified",
        "Partial failure preserves newer unrelated desktop data and keeps the successful ZIP receipt");

    var changedRemote = await New(); string externalCommit = "";
    error = await Fails(changedRemote, value =>
    {
        if (value.Phase != "DesktopHelp") return;
        externalCommit = changedRemote.AdvanceRemote().GetAwaiter().GetResult();
    });
    Check(!error.Result.Completed && !error.Result.GitPushed && error.Result.WebsiteZip != null && error.Result.DesktopHelp != null && await changedRemote.Head() == externalCommit,
        "Remote advancement rejects a non-fast-forward push without overwriting the remote or discarding verified local results");

    var postPush = await New();
    error = await Fails(postPush, value => { if (value.Phase == "Pushed") File.AppendAllText(Path.Combine(postPush.Help, "contents.htm"), "EXTERNAL_AFTER_PUSH"); });
    Check(error.Result.GitPushed && !error.Result.Completed && error.Result.GitCommit == await postPush.Head() && error.Result.TargetStates["GitHub"].Contains("pushed"),
        "A post-push local edit is reported as partial with the actual pushed commit, never concealed by rollback");

    var cancelled = await New(); string cancelHead = await cancelled.Head(); using var stop = new CancellationTokenSource();
    error = await Fails(cancelled, value => { if (value.Phase == "Build") stop.Cancel(); }, stop.Token);
    Check(!error.Result.Completed && !File.Exists(cancelled.Zip) && !Directory.Exists(cancelled.Help) && await cancelled.Head() == cancelHead && Directory.Exists(error.Result.SnapshotDirectory),
        "Cancellation before target promotion preserves original outputs and the captured diagnostic snapshot");

    var adopt = await New(); adopt.MakeFlare(); string flareSha = DesktopHelpPublisher.InspectDestination(adopt.Application).Sha256;
    await Reject(() => HelpPublicationPublisher.PublishLocalForTestsAsync(HelpProject.Open(adopt.Source), adopt.Assets, adopt.Options with { ExpectedDesktopSha256 = flareSha }), "The unified action does not bypass explicit Flare adoption");
    result = await HelpPublicationPublisher.PublishLocalForTestsAsync(HelpProject.Open(adopt.Source), adopt.Assets,
        adopt.Options with { ExpectedDesktopSha256 = flareSha, AdoptExistingFlareOutput = true, AdoptionReason = "Reviewed fixture Flare output." });
    Check(result.Completed && File.Exists(Path.Combine(adopt.Application, ".statsdirect-desktop-help-adoption.json")), "Reviewed legacy desktop adoption works inside the same one-snapshot publication");

    var privateSource = await New(); string privateHead = await privateSource.Head();
    File.WriteAllText(Path.Combine(privateSource.Source, "Content/auth.json"), "Private account state");
    error = await Fails(privateSource);
    Check(!File.Exists(privateSource.Zip) && !Directory.Exists(privateSource.Help) && await privateSource.Head() == privateHead,
        "Private credential input is rejected before any target or remote publication");
    var unsupported = await New(); string unsupportedHead = await unsupported.Head();
    File.WriteAllText(Path.Combine(unsupported.Source, "Content/private.cs"), "Private C# source");
    error = await Fails(unsupported);
    Check(!File.Exists(unsupported.Zip) && !Directory.Exists(unsupported.Help) && await unsupported.Head() == unsupportedHead,
        "Unsupported source files are rejected by full artifact validation before ZIP or desktop promotion");

    await SharedGitChecks.Run(root, fixtures, Check);
    await SharedPublicationRaceChecks.Run(root, fixtures, Check);

    if (args.Contains("--full-corpus"))
    {
        var full = await New(); string maintained = Path.Combine(root, "HelpSource");
        var maintainedBefore = HelpPublicationPublisher.Files(maintained);
        result = await HelpPublicationPublisher.PublishLocalForTestsAsync(HelpProject.Open(maintained), Path.Combine(root, "src/StatsDirectHelpEditor.App/Web"), full.Options);
        Check(result.Completed && result.WebsiteZip?.TopicCount == 404 && result.WebsiteZip.SearchDocumentCount == 403 &&
            result.DesktopHelp?.TopicCount == 404 && result.DesktopHelp.SearchDocumentCount == 403 && result.DesktopHelp.ContextCount > 100,
            "The full maintained corpus publishes all404 topics,403 search entries and numeric contexts to all three isolated targets", result);
        string archived = await full.ArchiveDocs();
        using var website = ZipFile.OpenRead(full.Zip); using var committed = ZipFile.OpenRead(archived);
        Dictionary<string,string> Fingerprints(ZipArchive zip, bool prefix) => zip.Entries.Where(entry => !entry.FullName.EndsWith('/'))
            .Select(entry => (Path: prefix ? entry.FullName["Docs/".Length..] : entry.FullName, Entry: entry))
            .Where(pair => HelpPublicationPublisher.ReaderFile(pair.Path)).ToDictionary(pair => pair.Path, pair => { using var stream = pair.Entry.Open(); return Convert.ToHexString(SHA256.HashData(stream)); });
        var reader = Fingerprints(website, false); var remoteReader = Fingerprints(committed, true);
        Check(reader.Count == remoteReader.Count && reader.All(pair => remoteReader.GetValueOrDefault(pair.Key) == pair.Value),
            "Every reader file in the full website ZIP matches the committed Git Docs bytes, with deployment metadata excluded", reader.Count);
        Check(DesktopHelpPublisher.VerifyDestination(full.Application).PublicationSnapshotSha256 == result.SourceSha256 &&
            maintainedBefore.OrderBy(pair => pair.Key).SequenceEqual(HelpPublicationPublisher.Files(maintained).OrderBy(pair => pair.Key)),
            "Full desktop output verifies against the shared snapshot and every original saved-source byte is unchanged");
    }
    }
}
catch (Exception error) { Check(false, "Unexpected unified publication test failure", error.ToString()); }
var report = new { checkedAtUtc = DateTimeOffset.UtcNow, passed = failed == 0, count = checks.Count, failed, fixtures, checks };
File.WriteAllText(Path.Combine(evidence, "checks.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { report.passed, report.count, report.failed, report.fixtures })); return failed == 0 ? 0 : 1;

static string Read(ZipArchive zip, string path) { using var reader = new StreamReader(zip.GetEntry(path)!.Open()); return reader.ReadToEnd(); }
sealed class InlineProgress(Action<HelpPublicationProgress> action) : IProgress<HelpPublicationProgress> { public void Report(HelpPublicationProgress value) => action(value); }
sealed class Fixture
{
    public string Root { get; } public string Source => Path.Combine(Root, "CurrentSource"); public string Assets => Path.Combine(Root, "Runtime");
    public string Application => Path.Combine(Root, "Application"); public string Help => Path.Combine(Application, "StatsDirectUI/Assets/Help");
    public string Zip => Path.Combine(Root, "Website.zip"); public string Seed => Path.Combine(Root, "UserCheckout"); public string Remote => Path.Combine(Root, "Remote.git");
    public string History => Path.Combine(Root, "History"); private bool Nested { get; } private bool MixedCaseDocs { get; }
    public HelpPublicationOptions Options => new() { WebsiteZipPath = Zip, GitHubRepositoryUrl = Remote, GitHubBranch = "main", AuthorName = "Fixture Author", AuthorEmail = "fixture@example.org", StatsDirectSource = Application, AllowDefaultBranchPublication = true, JournalDirectory = History };
    public Fixture(string root, bool nested, bool mixedCaseDocs = false) { Root = root; Nested = nested; MixedCaseDocs = mixedCaseDocs; }
    public async Task Initialize()
    {
        Write(Source, HelpProject.MarkerFile, "{\"keepLocalMarker\":true}");
        Write(Source, "StatsDirect.flprj", "<CatapultProject/>");
        Write(Source, "Content/contents.htm", "<html><head><title>Contents</title></head><body><h1>Contents</h1><p>Saved source example.</p></body></html>");
        Write(Source, "Content/basics/example.htm", "<html xmlns:s='" + HelpSourceNamespaces.CurrentUri + "'><head><title>Example</title></head><body><h1>Example</h1><p>Saved teaching page.</p><p s:conditions='Primary.WEBHELP'>PUBLIC_ONLY</p><p s:conditions='Primary.SWHELP'>DESKTOP_ONLY</p></body></html>");
        Write(Source, "Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title='Contents' Link='/Content/contents.htm'/><TocEntry Title='Example' Link='/Content/basics/example.htm'/></CatapultToc>");
        Write(Source, "Project/Advanced/STATSDIRECT.flali", "<CatapultAliasFile><Map Name='CONTENTS' Link='/Content/contents.htm'/><Map Name='EXAMPLE' Link='/Content/basics/example.htm'/></CatapultAliasFile>");
        Write(Source, "Project/Advanced/SD3ContextID.h", "#define CONTENTS 1000\n#define EXAMPLE 1001\n");
        if (MixedCaseDocs)
        {
            Write(Source, "Content/resources/scripts/chm-pictures.js", "window.caseFixture = 'exact byte identity';\r\n");
            Write(Source, "Content/resources/scripts/chm-pictures.js.props", "<props value='case identity'/>\r\n");
        }
        Write(Source, "Project/Users/private.xml", "Personal author state must not be published"); Write(Source, "Project/Destinations/secret.fldes", "Never publish a Flare destination");
        Write(Source, ".help-editor-recovery/deleted.htm", "Preserved user recovery data"); Write(Source, "RCode/example.R", "mean(c(1,2,3))\n");
        Write(Assets, "mathjax/tex-mml-svg.js", "// Isolated offline test renderer\n");
        Write(Application, "StatsDirectUI/StatsDirectUI.csproj", "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><AssemblyName>StatsDirect</AssemblyName><RootNamespace>StatsDirect</RootNamespace></PropertyGroup><ItemGroup><Content Include='Assets/**'/></ItemGroup></Project>");
        Directory.CreateDirectory(Path.Combine(Application, "StatsDirectUI/Assets/Operations"));
        Directory.CreateDirectory(Seed); await Git(Seed, "init", "--initial-branch=main");
        string canonical = Nested ? Path.Combine(Seed, "HelpEditor/HelpSource") : Seed;
        Write(canonical, HelpProject.MarkerFile, "{\"publishedMarker\":true}");
        Write(canonical, "Content/contents.htm", "<html><head><title>Old</title></head><body><p>Prior canonical topic.</p></body></html>");
        Write(canonical, "Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title='Old' Link='/Content/contents.htm'/></CatapultToc>");
        if (Nested) Write(Seed, "Content/legacy.htm", "Legacy root source remains intact");
        Write(Seed, "Docs/index.html", "<html><body>Previous published site.</body></html>"); Write(Seed, "Docs/CNAME", "help.example.org");
        if (MixedCaseDocs)
        {
            Write(Seed, "Docs/Basics/old.htm", "<html><body>Obsolete differently-cased folder.</body></html>");
            Write(Seed, "Docs/resources/Scripts/chm-pictures.js", "window.caseFixture = 'exact byte identity';\r\n");
            Write(Seed, "Docs/resources/Scripts/chm-pictures.js.props", "<props value='case identity'/>\r\n");
        }
        Write(Seed, ".gitattributes", "* -text\n"); await Git(Seed, "add", "--all"); await Git(Seed, "commit", "-m", "Fixture baseline");
        await Git(Root, "clone", "--bare", "--no-hardlinks", Seed, Remote);
    }
    public Task<string> Head() => Git(Remote, "rev-parse", "refs/heads/main");
    public Task<string> Show(string path) => Git(Remote, "show", "refs/heads/main:" + path, trim: false);
    public async Task<string> ArchiveDocs() { string zip=Path.Combine(Root,"CommittedDocs.zip");await Git(Remote,["archive","--format=zip","-o",zip,"refs/heads/main","Docs"]);return zip; }
    public async Task<string> AdvanceRemote() { Write(Seed, "external.txt", "Another writer's change"); await Git(Seed, "add", "external.txt"); await Git(Seed, "commit", "-m", "External advance"); await Git(Seed, "push", Remote, "main"); return await Head(); }
    public Dictionary<string, string> SourceHashes() => Hashes(Source); public Dictionary<string, string> SeedHashes() => Hashes(Seed);
    public bool SameSource(Dictionary<string, string> before) => Same(before, Hashes(Source)); public bool SameSeed(Dictionary<string, string> before) => Same(before, Hashes(Seed));
    private static bool Same(Dictionary<string,string> a,Dictionary<string,string> b) => a.Count==b.Count && a.All(pair=>b.GetValueOrDefault(pair.Key)==pair.Value);
    private static Dictionary<string,string> Hashes(string root) => Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>Path.GetRelativePath(root,path),path=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    public void MakeFlare() { Write(Help,"index.html","<html><body>Old complete Flare help.</body></html>");Write(Help,"index.mcwebhelp","<WebHelpSystem/>");Write(Help,"Data/HelpSystem.xml","<HelpSystem/>");Write(Help,"Data/Alias.xml","<CatapultAliasFile/>"); }
    private static void Write(string root,string relative,string content) { string file=Path.Combine(root,relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,content,new UTF8Encoding(false)); }
    private static Task<string> Git(string cwd,string a,string b,string c,string d,string e,string f) => Git(cwd,[a,b,c,d,e,f]);
    private static Task<string> Git(string cwd,string a,string b,string c,string d) => Git(cwd,[a,b,c,d]);
    private static Task<string> Git(string cwd,string a,string b,string c) => Git(cwd,[a,b,c]);
    private static Task<string> Git(string cwd,string a,string b) => Git(cwd,[a,b]);
    private static Task<string> Git(string cwd,string a,string b,string c,string d,string e) => Git(cwd,[a,b,c,d,e]);
    private static Task<string> Git(string cwd,string a,string b,bool trim) => Git(cwd,[a,b],trim);
    private static async Task<string> Git(string cwd,string[] arguments,bool trim=true)
    {
        var start=new ProcessStartInfo("git"){WorkingDirectory=cwd,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in new[]{"-c","user.name=Fixture Author","-c","user.email=fixture@example.org","-c","commit.gpgsign=false","-c","core.autocrlf=false"}.Concat(arguments))start.ArgumentList.Add(arg);
        start.Environment["GIT_TERMINAL_PROMPT"]="0";using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();string text=await output;if(process.ExitCode!=0)throw new IOException(await error);return trim?text.Trim():text;
    }
}
