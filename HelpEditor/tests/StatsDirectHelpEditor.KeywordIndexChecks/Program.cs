using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args[0]);
string evidence = Path.Combine(root, "evidence", "KeywordIndexChecks");
string fixture = Path.Combine(evidence, "Fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var checks = new List<object>();
int failed = 0;
void Check(bool passed, string name) { checks.Add(new { name, passed }); Console.WriteLine((passed ? "PASS " : "FAIL ") + name); if (!passed) failed++; }
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
Dictionary<string, string> Digests(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(file => Path.GetRelativePath(folder, file), Hash);
bool Same(string folder, Dictionary<string, string> expected) => Digests(folder).OrderBy(pair => pair.Key).SequenceEqual(expected.OrderBy(pair => pair.Key));
void Write(string folder, string relative, string value) { string file = Path.Combine(folder, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, value, new UTF8Encoding(false)); }
void Reject(Action action, string name) { bool rejected = false; try { action(); } catch (Exception error) when (error is InvalidOperationException or InvalidDataException) { rejected = true; } Check(rejected, name); }
Entry[] Read(string output)
{
    using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "keyword-index.json")));
    if (data.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unexpected keyword-index schema.");
    return data.RootElement.GetProperty("entries").EnumerateArray().Select(entry => new Entry(entry.GetProperty("term").GetString()!, entry.GetProperty("title").GetString()!, entry.GetProperty("path").GetString()!)).ToArray();
}
string Page(string title, string body, string rootAttributes = "") => "<html xmlns:sdh='" + HelpSourceNamespaces.CurrentUri + "' xmlns:MadCap='" + HelpSourceNamespaces.LegacyUri + "' xmlns:other='urn:unrelated' " + rootAttributes + "><head><title>" + title + "</title></head><body><h1>" + title + "</h1>" + body + "</body></html>";
HelpProject Project(string name, params (string Path, string Source)[] topics)
{
    string folder = Path.Combine(fixture, name);
    Write(folder, HelpProject.MarkerFile, "{}");
    foreach (var topic in topics) Write(folder, "Content/" + topic.Path, topic.Source);
    Write(folder, "Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc>" + string.Concat(topics.Select(topic => "<TocEntry Title='Page' Link='/Content/" + topic.Path + "'/>")) + "</CatapultToc>");
    return HelpProject.Open(folder);
}
try
{
    string runtime = Path.Combine(fixture, "Runtime");
    Write(runtime, "mathjax/tex-mml-svg.js", "// Keyword metadata fixture; rendering is tested separately.");
    string alpha = Page("Alpha &amp; treatment", """
        <p>alwaysprose</p>
        <sdh:keyword term=" shared ; Common; COMMON;  Regression:   Logistic ; Fish &amp; Chips ;   "/>
        <MadCap:keyword term="Common; Fisher&#39;s exact test"/>
        <other:keyword term="not-authored"/>
        <div sdh:conditions="Primary.WEBHELP"><sdh:keyword term="Website only"/></div>
        <div MadCap:conditions="Primary.SWHELP"><MadCap:keyword term="Desktop only"/></div>
        <sdh:keyword sdh:conditions="Primary.WEBHELP" term="Website direct"/>
        <MadCap:keyword MadCap:conditions="Primary.SWHELP" term="Desktop direct"/>
        """);
    var project = Project("Source", ("Folder/Alpha.htm", alpha),
        ("other/Zeta.htm", Page("Zeta", "<MadCap:keyword term='Common; shared; Other term'/><p>secondprose</p>")),
        ("private.htm", Page("Private", "<sdh:keyword term='Private keyword'/><p>privateprose</p>", "sdh:searchable='false'")));
    var original = Digests(project.RootPath);
    string desktop = Path.Combine(fixture, "Desktop"), website = Path.Combine(fixture, "Public");
    var desktopBuild = HelpBuilder.Build(project, desktop, runtime);
    var desktopEntries = Read(desktop);
    Check(desktopBuild.TopicCount == 3 && desktopBuild.SearchDocumentCount == 2 && desktopBuild.Warnings.Count == 0, "Index generation preserves topic/search counts and introduces no warnings");
    Check(desktopEntries.Length == 11, "Semicolon terms expand into exactly eleven distinct keyword-to-page entries");
    Check(desktopEntries.Count(entry => entry.Term.Equals("Common", StringComparison.OrdinalIgnoreCase) && entry.Path == "folder/alpha.htm") == 1, "Repeated and case-varied terms deduplicate on one page across both authoring namespaces");
    Check(desktopEntries.Where(entry => entry.Term.Equals("Common", StringComparison.OrdinalIgnoreCase)).Select(entry => entry.Path).SequenceEqual(new[] { "folder/alpha.htm", "other/zeta.htm" }), "One keyword retains both topic targets in title order");
    Check(desktopEntries.Contains(new Entry("Regression: Logistic", "Alpha & treatment", "folder/alpha.htm")), "Whitespace is normalized while colon hierarchy, decoded title and lowercase page path are retained");
    Check(desktopEntries.Contains(new Entry("Fish & Chips", "Alpha & treatment", "folder/alpha.htm")) && desktopEntries.Any(entry => entry.Term == "Fisher's exact test"), "XML entities decode before splitting terms rather than being split at entity semicolons");
    Check(desktopEntries.Any(entry => entry.Term == "Desktop direct") && desktopEntries.Any(entry => entry.Term == "Desktop only") && !desktopEntries.Any(entry => entry.Term.StartsWith("Website")), "Desktop index respects direct keyword and enclosing-container target conditions");
    Check(!desktopEntries.Any(entry => entry.Term is "not-authored" or ""), "Unrelated keyword elements and empty semicolon fields do not become index entries");
    Check(desktopEntries.Any(entry => entry.Term == "Private keyword" && entry.Path == "private.htm"), "An authored keyword on a search-excluded page remains in the keyword index");
    string[] expectedDesktop = ["Common|folder/alpha.htm", "Common|other/zeta.htm", "Desktop direct|folder/alpha.htm", "Desktop only|folder/alpha.htm", "Fish & Chips|folder/alpha.htm", "Fisher's exact test|folder/alpha.htm", "Other term|other/zeta.htm", "Private keyword|private.htm", "Regression: Logistic|folder/alpha.htm", "shared|folder/alpha.htm", "shared|other/zeta.htm"];
    Check(desktopEntries.Select(entry => entry.Term + "|" + entry.Path).SequenceEqual(expectedDesktop), "Keyword entries have deterministic case-insensitive term, title and path ordering");
    string searchScript = File.ReadAllText(Path.Combine(desktop, "search-index.js"));
    using (var search = JsonDocument.Parse(searchScript[searchScript.IndexOf('=')..].TrimStart('=').TrimEnd(';')))
        Check(search.RootElement.GetProperty("documents").GetArrayLength() == 2 && !search.RootElement.GetProperty("documents").EnumerateArray().Any(document => document.GetProperty("url").GetString() == "private.htm") && !search.RootElement.GetProperty("terms").TryGetProperty("privateprose", out _), "Keyword indexing does not make excluded prose or pages searchable");
    Check(!File.ReadAllText(Path.Combine(desktop, "folder", "alpha.htm")).Contains("sdh:keyword") && !File.ReadAllText(Path.Combine(desktop, "private.htm")).Contains("term="), "Authoring keywords are metadata and remain absent from rendered reader HTML");
    using (var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(desktop, ".statsdirect-help-output.json"))))
        Check(receipt.RootElement.GetProperty("files").EnumerateArray().Any(file => file.GetString() == "keyword-index.json"), "The normal builder receipt owns keyword-index.json");
    byte[] bytes = File.ReadAllBytes(Path.Combine(desktop, "keyword-index.json"));
    Check(!bytes.Contains((byte)'\r') && !bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()), "Generated keyword metadata is UTF-8 without BOM and contains no CR line endings");
    string keywordScript = File.ReadAllText(Path.Combine(desktop, "keyword-index.js"));
    Check(keywordScript.StartsWith("window.STATSDIRECT_HELP_KEYWORDS=" + Encoding.UTF8.GetString(bytes) + ";\n", StringComparison.Ordinal), "The browser script carries the exact native keyword JSON without a network fetch");
    string topicJson = keywordScript.Split("window.StatsDirectTopics=", StringSplitOptions.None)[1].TrimEnd(';', '\n');
    using (var browserTopics = JsonDocument.Parse(topicJson))
        Check(browserTopics.RootElement.EnumerateArray().Count() == 3 && browserTopics.RootElement.EnumerateArray().Any(topic => topic.GetProperty("url").GetString() == "private.htm"), "The browser catalogue includes search-excluded topics for safe keyword navigation");
    using (var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(desktop, ".statsdirect-help-output.json"))))
        Check(receipt.RootElement.GetProperty("files").EnumerateArray().Any(file => file.GetString() == "keyword-index.js"), "The builder receipt owns the browser keyword companion script");
    string originalIndexHash = Hash(Path.Combine(desktop, "keyword-index.json"));
    HelpBuilder.Build(project, desktop, runtime);
    Check(Hash(Path.Combine(desktop, "keyword-index.json")) == originalIndexHash && File.ReadAllText(Path.Combine(desktop, "search-index.js")) == searchScript, "Repeated builds preserve exact index bytes and the existing full-text search output");

    var publicBuild = HelpBuilder.Build(project, website, runtime, HelpBuildTarget.Public);
    var publicEntries = Read(website);
    Check(publicEntries.Length == 11 && publicBuild.SearchDocumentCount == 2 && publicEntries.Any(entry => entry.Term == "Website only") && publicEntries.Any(entry => entry.Term == "Website direct") && !publicEntries.Any(entry => entry.Term.StartsWith("Desktop")), "Website index uses Website conditions without changing search exclusions");
    Check(Same(project.RootPath, original), "Both target builds preserve every original fixture byte");

    string draft = Page("Draft &amp; review", "<sdh:keyword term='Draft term; Regression: Logistic'/><p>draftprose</p>");
    HelpBuilder.Build(project, desktop, runtime, topicOverrides: new Dictionary<string, string> { ["Folder/Alpha.htm"] = draft });
    var draftEntries = Read(desktop);
    Check(draftEntries.Where(entry => entry.Path == "folder/alpha.htm").SequenceEqual(new[] { new Entry("Draft term", "Draft & review", "folder/alpha.htm"), new Entry("Regression: Logistic", "Draft & review", "folder/alpha.htm") }), "Draft builds use unsaved keyword and title edits and remove that page's stale keyword targets");
    Check(Same(project.RootPath, original), "Preview keyword indexing never saves its unsaved draft");
    HelpBuilder.Build(project, desktop, runtime);
    Check(Hash(Path.Combine(desktop, "keyword-index.json")) == originalIndexHash, "A follow-on saved build restores the source index without stale preview terms");

    var empty = Project("EmptySource", ("topic.htm", Page("Topic", "<p>No authored keywords.</p>")));
    string emptyOutput = Path.Combine(fixture, "EmptyOutput");
    HelpBuilder.Build(empty, emptyOutput, runtime);
    Check(Read(emptyOutput).Length == 0, "A project with no authored keywords emits a valid empty index rather than inventing title entries");

    string foreignOutput = Path.Combine(fixture, "UnownedOutput");
    Write(foreignOutput, ".statsdirect-help-output.json", "{\"files\":[]}"); Write(foreignOutput, "keyword-index.json", "Unowned user data");
    string foreignHash = Hash(Path.Combine(foreignOutput, "keyword-index.json"));
    Reject(() => HelpBuilder.Build(project, foreignOutput, runtime), "An existing unowned keyword-index.json cannot be overwritten");
    Check(Hash(Path.Combine(foreignOutput, "keyword-index.json")) == foreignHash, "Refusing an unowned metadata file preserves its original bytes");
    string foreignScriptOutput = Path.Combine(fixture, "UnownedScriptOutput");
    Write(foreignScriptOutput, ".statsdirect-help-output.json", "{\"files\":[]}"); Write(foreignScriptOutput, "keyword-index.js", "Source-owned script");
    string foreignScriptHash = Hash(Path.Combine(foreignScriptOutput, "keyword-index.js"));
    Reject(() => HelpBuilder.Build(project, foreignScriptOutput, runtime), "An unowned keyword companion script cannot be overwritten");
    Check(Hash(Path.Combine(foreignScriptOutput, "keyword-index.js")) == foreignScriptHash, "Refusing an unowned companion script preserves its original bytes");
    var conflict = Project("ConflictSource", ("topic.htm", Page("Topic", "<p>Source conflict.</p>")));
    Write(conflict.RootPath, "Content/keyword-index.json", "Source-owned data");
    var conflictBefore = Digests(conflict.RootPath);
    Reject(() => HelpBuilder.Build(conflict, Path.Combine(fixture, "ConflictOutput"), runtime), "A source resource cannot shadow the generated keyword metadata name");
    Check(Same(conflict.RootPath, conflictBefore), "A reserved-name conflict leaves all source bytes untouched");
    var scriptConflict = Project("ScriptConflictSource", ("topic.htm", Page("Topic", "<p>Source conflict.</p>")));
    Write(scriptConflict.RootPath, "Content/keyword-index.js", "Source-owned script");
    var scriptConflictBefore = Digests(scriptConflict.RootPath);
    Reject(() => HelpBuilder.Build(scriptConflict, Path.Combine(fixture, "ScriptConflictOutput"), runtime), "A source resource cannot shadow the generated keyword companion script");
    Check(Same(scriptConflict.RootPath, scriptConflictBefore), "A companion-script name conflict leaves the source untouched");
}
catch (Exception error) { Check(false, "Unexpected keyword-index exception: " + error); }
var report = new { checkedAtUtc = DateTimeOffset.UtcNow, passed = failed == 0, failed, assertions = checks.Count, fixture, checks };
File.WriteAllText(Path.Combine(evidence, "checks.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { report.passed, report.failed, report.assertions, report.fixture }));
return failed == 0 ? 0 : 1;

internal sealed record Entry(string Term, string Title, string Path);
