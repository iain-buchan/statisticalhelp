using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using StatsDirect.HelpEditor.Core;

if (args.FirstOrDefault() == "--crash")
{
    var project = HelpProject.Open(args[1]);
    DesktopHelpPublisher.Publish(project, args[2], args[3], new() { ExpectedDestinationSha256 = args[4] },
        progress: new InlineProgress(value => { if (value.Phase == args[5]) Environment.Exit(73); }));
    return 74;
}
string root = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--")) ?? Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string evidence = Path.Combine(root, "evidence/ReviewImprovements-20261104/DesktopComparison");
Directory.CreateDirectory(evidence);
string runRoot = Path.Combine(evidence, "Fixtures-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(runRoot);
var checks = new List<object>(); int failed = 0;
void Check(bool value, string name, object? detail = null) { checks.Add(new { name, passed = value, detail }); if (!value) failed++; Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
void Refuses(Action action, string name) { bool refused = false; try { action(); } catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException or System.Xml.XmlException) { refused = true; } Check(refused, name); }
Fixture New() => new(Path.Combine(runRoot, Guid.NewGuid().ToString("N")));
string Digest(string folder) => DesktopHelpPublisher.InspectDestination(folder).Sha256;
void CleanTransaction(Fixture f) => Check(!File.Exists(DesktopHelpPublisher.PendingMarkerPath(f.Repository)) && !File.Exists(Path.Combine(f.Repository, ".statsdirect-desktop-help.lock")) && !Directory.EnumerateDirectories(Path.Combine(f.Repository, "StatsDirectUI/Assets"), ".Help-desktop-*").Any(), "Completed/failed publication leaves no marker, lock or rollback/staging tree");
try
{
    var f = New(); var source = HelpProject.Open(f.Source);
    var published = DesktopHelpPublisher.Publish(source, f.Repository, f.Assets);
    Check(published.TopicCount == 2 && published.SearchDocumentCount == 2 && published.ContextCount == 2, "Desktop publication verifies every topic, search document and numeric context mapping");
    Check(File.Exists(Path.Combine(f.Help, "index.html")) && File.ReadAllText(Path.Combine(f.Help, "Data/Alias.xml")).Contains("ResolvedId=\"1001\""), "The delivered tree contains the entry point and resolved application context IDs");
    Check(DesktopHelpPublisher.VerifyDestination(f.Repository).ReceiptSha256 == published.ReceiptSha256 && Digest(f.Repository) != "", "Full delivered ownership/hash verification succeeds");
    Check(!published.ConsumerGuardInstalled && !File.ReadAllText(published.ReceiptPath).Contains(f.Source), "An unmodified consumer is honestly reported unguarded and public receipt excludes private source paths"); CleanTransaction(f);
    string previous = Digest(f.Repository);
    Refuses(() => DesktopHelpPublisher.Publish(source, f.Repository, f.Assets), "Existing help cannot be replaced without its inspected destination hash");
    Check(Digest(f.Repository) == previous, "Rejected publication preserves the complete previous tree");
    File.WriteAllText(Path.Combine(f.Source, "Content/extra.htm"), "<html><head><title>Extra</title></head><body><p>New topic.</p></body></html>");
    published = DesktopHelpPublisher.Publish(source, f.Repository, f.Assets, new() { ExpectedDestinationSha256 = previous });
    Check(published.TopicCount == 3 && File.Exists(Path.Combine(f.Help, "extra.htm")), "A newly authored topic reaches the actual delivered artifact");
    previous = Digest(f.Repository); File.Delete(Path.Combine(f.Source, "Content/extra.htm"));
    DesktopHelpPublisher.Publish(source, f.Repository, f.Assets, new() { ExpectedDestinationSha256 = previous });
    Check(!File.Exists(Path.Combine(f.Help, "extra.htm")), "A later complete publication removes obsolete owned help files"); CleanTransaction(f);

    var flare = New(); flare.MakeFlare(); string flareHash = Digest(flare.Repository);
    Check(DesktopHelpPublisher.InspectDestination(flare.Repository).Ownership == DesktopHelpOwnership.FlareNeedsAdoption, "Recognised legacy output is classified as requiring explicit Flare adoption");
    Refuses(() => DesktopHelpPublisher.Publish(HelpProject.Open(flare.Source), flare.Repository, flare.Assets, new() { ExpectedDestinationSha256 = flareHash }), "Inspected Flare output still cannot be silently adopted");
    Refuses(() => DesktopHelpPublisher.Publish(HelpProject.Open(flare.Source), flare.Repository, flare.Assets, new() { ExpectedDestinationSha256 = flareHash, AdoptExistingFlareOutput = true }), "Flare adoption needs an attributable reason as well as the exact hash");
    Check(Digest(flare.Repository) == flareHash, "Rejected adoption preserves every legacy file and build record");
    DesktopHelpPublisher.Publish(HelpProject.Open(flare.Source), flare.Repository, flare.Assets, new() { ExpectedDestinationSha256 = flareHash, AdoptExistingFlareOutput = true, AdoptionReason = "Reviewed generated legacy fixture; replace it with the saved editor build." });
    Check(File.Exists(Path.Combine(flare.Repository, ".statsdirect-desktop-help-adoption.json")) && !File.Exists(Path.Combine(flare.Help, "Data/old.output")), "Successful adoption records the previous hash privately and excludes obsolete Flare build records"); CleanTransaction(flare);
    var foreign = New(); foreign.MakeFlare(); File.WriteAllText(Path.Combine(foreign.Help, "notes.txt"), "Unique unrelated user note");
    Check(DesktopHelpPublisher.InspectDestination(foreign.Repository).Ownership == DesktopHelpOwnership.Unrecognised, "Unrecognised user data are not accepted as generated Flare ownership");
    Refuses(() => DesktopHelpPublisher.Publish(HelpProject.Open(foreign.Source), foreign.Repository, foreign.Assets, new() { AdoptExistingFlareOutput = true, ExpectedDestinationSha256 = Digest(foreign.Repository), AdoptionReason = "This must not override unrelated files" }), "An adoption flag cannot override an unowned tree");
    Check(File.ReadAllText(Path.Combine(foreign.Help, "notes.txt")) == "Unique unrelated user note", "Unowned user data remain intact");
    var tampered = New(); DesktopHelpPublisher.Publish(HelpProject.Open(tampered.Source), tampered.Repository, tampered.Assets);
    File.AppendAllText(Path.Combine(tampered.Help, "contents.htm"), "External modification");
    Check(DesktopHelpPublisher.InspectDestination(tampered.Repository).Ownership == DesktopHelpOwnership.Unrecognised, "Modified delivered output no longer passes its ownership manifest");
    Refuses(() => DesktopHelpPublisher.VerifyDestination(tampered.Repository), "Verification detects a modified artifact even without a pending marker");
    File.WriteAllText(Path.Combine(tampered.Help, DesktopHelpPublisher.ReceiptFileName), "{}");
    Check(DesktopHelpPublisher.InspectDestination(tampered.Repository).Ownership == DesktopHelpOwnership.Unrecognised, "An incomplete receipt is reported as unrecognised rather than accepted");
    var malformedBuilder = New(); Directory.CreateDirectory(malformedBuilder.Help); File.WriteAllText(Path.Combine(malformedBuilder.Help, ".statsdirect-help-output.json"), "{}");
    Check(DesktopHelpPublisher.InspectDestination(malformedBuilder.Repository).Ownership == DesktopHelpOwnership.Unrecognised, "Malformed historical builder metadata never establishes output ownership");

    foreach (string phase in new[] { "PreviousRetired", "Activated" })
    {
        var fault = New(); var project = HelpProject.Open(fault.Source); DesktopHelpPublisher.Publish(project, fault.Repository, fault.Assets); string old = Digest(fault.Repository);
        File.AppendAllText(Path.Combine(fault.Source, "Content/basics/example.htm"), "\n");
        Refuses(() => DesktopHelpPublisher.Publish(project, fault.Repository, fault.Assets, new() { ExpectedDestinationSha256 = old }, progress: new InlineProgress(value => { if (value.Phase == phase) throw new IOException("Injected transaction failure"); })), "A failure at " + phase + " is reported");
        Check(Digest(fault.Repository) == old, "A failure at " + phase + " restores the complete previous output"); CleanTransaction(fault);
    }
    var raced = New(); var raceProject = HelpProject.Open(raced.Source);
    Refuses(() => DesktopHelpPublisher.Publish(raceProject, raced.Repository, raced.Assets, progress: new InlineProgress(value => { if (value.Phase == "Built") File.AppendAllText(Path.Combine(raced.Source, "Content/contents.htm"), "\n"); })), "Saved-source changes after the build refuse delivery");
    Check(!Directory.Exists(raced.Help), "A source race never activates a mixed generation"); CleanTransaction(raced);
    var destinationRace = New();
    Refuses(() => DesktopHelpPublisher.Publish(HelpProject.Open(destinationRace.Source), destinationRace.Repository, destinationRace.Assets, progress: new InlineProgress(value => { if (value.Phase == "Staged") { Directory.CreateDirectory(destinationRace.Help); File.WriteAllText(Path.Combine(destinationRace.Help, "retained.csv"), "User,Data"); } })), "Destination changes after inspection refuse activation");
    Check(File.ReadAllText(Path.Combine(destinationRace.Help, "retained.csv")) == "User,Data", "A destination race preserves the newer external file"); CleanTransaction(destinationRace);
    var lateRace = New(); var lateProject = HelpProject.Open(lateRace.Source); DesktopHelpPublisher.Publish(lateProject, lateRace.Repository, lateRace.Assets); string lateHash = Digest(lateRace.Repository);
    Refuses(() => DesktopHelpPublisher.Publish(lateProject, lateRace.Repository, lateRace.Assets, new() { ExpectedDestinationSha256 = lateHash }, progress: new InlineProgress(value => { if (value.Phase == "BeforeActivation") File.AppendAllText(Path.Combine(lateRace.Help, "contents.htm"), "LATE_EXTERNAL_EDIT"); })), "Destination is rechecked after the durable marker and immediately before activation");
    Check(File.ReadAllText(Path.Combine(lateRace.Help, "contents.htm")).EndsWith("LATE_EXTERNAL_EDIT"), "A late destination edit remains at the live destination without a stale publication marker"); CleanTransaction(lateRace);
    var cancelled = New(); using (var stop = new CancellationTokenSource())
    { Refuses(() => DesktopHelpPublisher.Publish(HelpProject.Open(cancelled.Source), cancelled.Repository, cancelled.Assets, cancellationToken: stop.Token, progress: new InlineProgress(value => { if (value.Phase == "Built") stop.Cancel(); })), "Cancellation before activation stops desktop delivery"); }
    Check(!Directory.Exists(cancelled.Help), "Cancelled first publication leaves its previous absence intact"); CleanTransaction(cancelled);
    foreach (string privatePath in new[] { "private.cs", "LICENSE.pfx", ".tutor-preview-profile/account-state.json", "auth.json" })
    {
        var privateSource = New(); string privateFile = Path.Combine(privateSource.Source, "Content", privatePath); Directory.CreateDirectory(Path.GetDirectoryName(privateFile)!); File.WriteAllText(privateFile, "private author source");
        Refuses(() => DesktopHelpPublisher.Publish(HelpProject.Open(privateSource.Source), privateSource.Repository, privateSource.Assets), "Private/source input cannot enter desktop delivery: " + privatePath);
        Check(!Directory.Exists(privateSource.Help), "Private-file rejection occurs before destination creation: " + privatePath);
    }

    var guarded = New(); string guardPath = Path.Combine(guarded.Repository, DesktopHelpPublisher.ConsumerGuardRelativePath); Directory.CreateDirectory(Path.GetDirectoryName(guardPath)!); File.WriteAllText(guardPath, DesktopHelpPublisher.ConsumerGuardXml);
    string csproj = Path.Combine(guarded.Repository, "StatsDirectUI/StatsDirectUI.csproj"); File.WriteAllText(csproj, File.ReadAllText(csproj).Replace("</Project>", "<Import Project='../BuildTools/StatsDirectDesktopHelp.targets'/></Project>"));
    Check(DesktopHelpPublisher.Publish(HelpProject.Open(guarded.Source), guarded.Repository, guarded.Assets).ConsumerGuardInstalled, "Only an explicitly imported exact consumer guard is reported installed");
    File.WriteAllText(DesktopHelpPublisher.PendingMarkerPath(guarded.Repository), "{}");
    Refuses(() => DesktopHelpPublisher.EnsureNoPendingPublication(guarded.Repository), "Pending marker blocks the supplied consumer-check API");
    Refuses(() => DesktopHelpPublisher.VerifyDestination(guarded.Repository), "Full artifact verification also refuses pending output");
    var guardCheck = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    foreach (string arg in new[] { "msbuild", csproj, "-t:CheckStatsDirectDesktopHelpPublication", "-nologo", "-v:q" }) guardCheck.ArgumentList.Add(arg);
    using (var process = Process.Start(guardCheck)!) { string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); Check(process.ExitCode != 0 && output.Contains("Desktop help publication is incomplete"), "The optional guard artifact really blocks MSBuild in an isolated application fixture"); }
    File.Delete(DesktopHelpPublisher.PendingMarkerPath(guarded.Repository));
    File.WriteAllText(DesktopHelpPublisher.PendingMarkerPath(guarded.Repository), "{}");
    Refuses(() => DesktopHelpPublisher.RecoverPending(guarded.Repository), "An incomplete recovery journal is refused without guessing at source or destination ownership");
    Check(File.Exists(Path.Combine(guarded.Help, "index.html")) && File.Exists(DesktopHelpPublisher.PendingMarkerPath(guarded.Repository)), "Malformed recovery keeps the complete current output and its blocking marker");
    File.Delete(DesktopHelpPublisher.PendingMarkerPath(guarded.Repository));

    foreach (string phase in new[] { "BeforeActivation", "PreviousRetired", "Activated" })
    {
        var crash = New(); var project = HelpProject.Open(crash.Source); var oldResult = DesktopHelpPublisher.Publish(project, crash.Repository, crash.Assets); string old = Digest(crash.Repository);
        File.AppendAllText(Path.Combine(crash.Source, "Content/contents.htm"), "\n");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { typeof(Fixture).Assembly.Location, "--crash", crash.Source, crash.Repository, crash.Assets, old, phase }) start.ArgumentList.Add(arg);
        using (var child = Process.Start(start)!) { child.StandardOutput.ReadToEnd(); child.StandardError.ReadToEnd(); child.WaitForExit(); Check(child.ExitCode == 73, "Child process really stops at durable transaction phase " + phase); }
        Refuses(() => DesktopHelpPublisher.VerifyDestination(crash.Repository), "A crashed publication cannot be consumed before recovery at " + phase);
        var recovery = DesktopHelpPublisher.RecoverPending(crash.Repository);
        var verified = DesktopHelpPublisher.VerifyDestination(crash.Repository);
        Check(recovery.PendingMarkerCleared && (phase == "Activated" ? verified.SourceSha256 != oldResult.SourceSha256 : Digest(crash.Repository) == old), "Crash recovery retains the last complete artifact at " + phase, recovery.Outcome); CleanTransaction(crash);
    }

    var left = New(); var right = New(); string l = Path.Combine(left.Source, "Content/basics/example.htm"), r = Path.Combine(right.Source, "Content/basics/example.htm");
    const string legacy = "http://www.madcapsoftware.com/Schemas/MadCap.xsd";
    string sourceXml = "<html xmlns:m='" + legacy + "' m:lastHeight='20'><head><title>Example</title></head><body><p><a href='https://example.org/old'>Reference</a></p><m:equation>x + y</m:equation><pre>x &lt;- 'a b'\nprint(x)</pre></body></html>";
    File.WriteAllText(l, sourceXml); File.WriteAllText(r, sourceXml.Replace(legacy, HelpSourceNamespaces.CurrentUri).Replace("m:", "sdh:").Replace("xmlns:m", "xmlns:sdh"));
    File.Delete(Path.Combine(left.Source, HelpProject.MarkerFile)); File.Delete(Path.Combine(right.Source, HelpProject.MarkerFile));
    string beforeLeft = File.ReadAllText(l), beforeRight = File.ReadAllText(r);
    var compared = HelpSourceComparer.Compare(left.Source, right.Source);
    Check(compared.EquivalentTopicCount == 2 && compared.Differences.Count == 0, "Namespace URI/prefix equivalence works without marking or opening either original source");
    Check(File.ReadAllText(l) == beforeLeft && File.ReadAllText(r) == beforeRight && !File.Exists(Path.Combine(left.Source, HelpProject.MarkerFile)), "Comparison is read-only and never migrates an unmarked source");
    File.WriteAllText(r, beforeRight.Replace("'a b'", "'ab'").Replace("x + y", "x - y")); compared = HelpSourceComparer.Compare(left.Source, right.Source);
    Check(compared.Differences.Single().CodeChanged && compared.Differences.Single().EquationsChanged, "Meaningful R-string whitespace and equation changes are not normalised away");
    File.WriteAllText(r, beforeRight.Replace("https://example.org/old", "https://example.org/new")); compared = HelpSourceComparer.Compare(left.Source, right.Source);
    Check(compared.Differences.Single().UrlsChanged && !compared.Differences.Single().TextChanged && !compared.Differences.Single().OtherMarkupChanged, "A reference URL change is distinguished from text/layout changes");
    File.WriteAllText(r, beforeRight.Replace(" sdh:lastHeight='20'", "")); compared = HelpSourceComparer.Compare(left.Source, right.Source);
    Check(compared.Differences.Single().OtherMarkupChanged && !compared.Differences.Single().UrlsChanged, "Root layout metadata changes remain visible to reconciliation");
    File.WriteAllText(r, beforeRight); File.WriteAllText(Path.Combine(left.Source, "Content/new.html"), "<html><body><p>New HTML topic</p></body></html>");
    compared = HelpSourceComparer.Compare(left.Source, right.Source);
    Check(compared.LeftTopicCount == 3 && compared.RightTopicCount == 2 && compared.Differences.Single().Path == "new.html", "Inventory reports a missing .html topic as well as .htm");
    File.WriteAllText(Path.Combine(right.Source, "Content/new.html"), "<html><body><p>Changed HTML topic</p></body></html>"); compared = HelpSourceComparer.Compare(left.Source, right.Source);
    Check(compared.Differences.Single().TextChanged && compared.Differences.Single().Path == "new.html", "An existing .html topic's changed text is classified correctly");
    string rightToc = Path.Combine(right.Source, "Project/TOCs/STATSDIRECT.fltoc");
    File.WriteAllText(rightToc, File.ReadAllText(rightToc).Replace("Title='Example'", "Title='Revised lesson'").Replace("Link='/Content/basics/example.htm'", "Link='/Content/basics/example.htm#section'"));
    compared = HelpSourceComparer.Compare(left.Source, right.Source);
    Check(compared.ContentsEquivalent == false && compared.Contents.Single().TextChanged && compared.Contents.Single().UrlsChanged, "Contents reconciliation separately identifies changed visible titles and linked targets");
    File.WriteAllText(r, beforeRight.Replace("Reference</a>", "Literal sdh:word instead of MadCap:word</a>"));
    compared = HelpSourceComparer.Compare(left.Source, right.Source);
    Check(compared.Topics.Single(topic => topic.Path == "basics/example.htm").TextChanged, "Literal prefix-like text is never rewritten as XML vocabulary");
    File.WriteAllText(r, "<!DOCTYPE html [<!ENTITY x 'not allowed'>]><html><body>&x;</body></html>");
    Refuses(() => HelpSourceComparer.Compare(left.Source, right.Source), "Comparison rejects DTDs without resolving or changing source");

    if (args.Contains("--full-corpus"))
    {
        var full = New(); var complete = DesktopHelpPublisher.Publish(HelpProject.Open(Path.Combine(root, "HelpSource")), full.Repository, Path.Combine(root, "src/StatsDirectHelpEditor.App/Web"));
        Check(complete.TopicCount == 404 && complete.SearchDocumentCount == 403 && complete.ContextCount > 100, "Full saved production corpus publishes 404 topics,403 search documents and application context IDs in an isolated repository", complete);
        Check(DesktopHelpPublisher.VerifyDestination(full.Repository).ReceiptSha256 == complete.ReceiptSha256, "Every file of the full404-page desktop delivery verifies after activation"); CleanTransaction(full);
    }
}
catch (Exception error) { Check(false, "Unexpected delivery/comparison test exception", error.ToString()); }
var report = new { checkedAtUtc = DateTimeOffset.UtcNow, passed = failed == 0, total = checks.Count, failed, runRoot, checks };
File.WriteAllText(Path.Combine(evidence, "checks.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { report.passed, report.total, report.failed, report.runRoot }));
return failed == 0 ? 0 : 1;

sealed class InlineProgress(Action<DesktopHelpPublishProgress> action) : IProgress<DesktopHelpPublishProgress>
{ public void Report(DesktopHelpPublishProgress value) => action(value); }
sealed class Fixture
{
    public string Source { get; }
    public string Assets { get; }
    public string Repository { get; }
    public string Help => Path.Combine(Repository, "StatsDirectUI/Assets/Help");
    public Fixture(string root)
    {
        Source = Path.Combine(root, "Source"); Assets = Path.Combine(root, "Runtime"); Repository = Path.Combine(root, "Application");
        void Write(string directory, string path, string text) { string file = Path.Combine(directory, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text); }
        Write(Source, HelpProject.MarkerFile, "{}");
        Write(Source, "Content/contents.htm", "<html><head><title>Contents</title></head><body><h1>Contents</h1><p>Desktop fixture.</p></body></html>");
        Write(Source, "Content/basics/example.htm", "<html><head><title>Example</title></head><body><h1>Example</h1><p>Original example.</p></body></html>");
        Write(Source, "Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title='Contents' Link='/Content/contents.htm'/><TocEntry Title='Example' Link='/Content/basics/example.htm'/></CatapultToc>");
        Write(Source, "Project/Advanced/STATSDIRECT.flali", "<CatapultAliasFile><Map Name='CONTENTS' Link='/Content/contents.htm'/><Map Name='EXAMPLE' Link='/Content/basics/example.htm'/></CatapultAliasFile>");
        Write(Source, "Project/Advanced/SD3ContextID.h", "#define CONTENTS 1000\n#define EXAMPLE 1001\n");
        Write(Assets, "mathjax/tex-mml-svg.js", "// Isolated offline renderer fixture; no network.\n");
        Write(Repository, "StatsDirectUI/StatsDirectUI.csproj", "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>StatsDirect</AssemblyName><RootNamespace>StatsDirect</RootNamespace></PropertyGroup><ItemGroup><Content Include='Assets/**'/></ItemGroup></Project>");
        Directory.CreateDirectory(Path.Combine(Repository, "StatsDirectUI/Assets/Operations"));
    }
    public void MakeFlare()
    {
        Directory.CreateDirectory(Path.Combine(Help, "Data"));
        File.WriteAllText(Path.Combine(Help, "index.html"), "<html><body>Prior complete Flare output</body></html>");
        File.WriteAllText(Path.Combine(Help, "index.mcwebhelp"), "<WebHelpSystem/>");
        File.WriteAllText(Path.Combine(Help, "Data/HelpSystem.xml"), "<HelpSystem/>");
        File.WriteAllText(Path.Combine(Help, "Data/Alias.xml"), "<CatapultAliasFile/>");
        File.WriteAllText(Path.Combine(Help, "Data/old.output"), "Known obsolete Flare build record");
    }
}
