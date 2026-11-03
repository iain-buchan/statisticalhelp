using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args.Length == 0 ? Path.Combine(AppContext.BaseDirectory, "../../../../../") : args[0]);
string evidence = Path.Combine(root, "evidence");
string fixture = Path.Combine(evidence, "NamespaceFixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var checks = new List<object>();
int failed = 0;
void Check(bool passed, string name)
{
    checks.Add(new { name, passed });
    if (!passed) failed++;
    Console.WriteLine((passed ? "PASS " : "FAIL ") + name);
}
void Write(string folder, string relative, string source)
{
    string path = Path.Combine(folder, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, source, new UTF8Encoding(false));
}
string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
Dictionary<string, string> Digests(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(path => path, Digest);
string Page(string prefix, string uri, string body) => $"<html xmlns:{prefix}='{uri}'><head><title>Namespace equivalence α</title></head><body>{body}</body></html>";
string CreateFixture(string name, string prefix, string uri)
{
    string folder = Path.Combine(fixture, name);
    Write(folder, HelpProject.MarkerFile, "{}");
    Write(folder, "Project/TOCs/STATSDIRECT.fltoc", $"<CatapultToc xmlns:{prefix}='{uri}'><TocEntry Title='Both' Link='/Content/basics/example.htm'/><TocEntry Title='DesktopOnlyEntry' Link='/Content/basics/example.htm' {prefix}:conditions='Primary.SWHELP'/><TocEntry Title='PublicOnlyEntry' Link='/Content/basics/example.htm' {prefix}:conditions='Primary.WEBHELP'/></CatapultToc>");
    foreach (string target in new[] { "DesktopHelp", "Website" })
        Write(folder, $"Project/Targets/{target}.fltar", "<CatapultTarget MasterPage='Content/resources/masterpages/footer.flmsp'/>");
    Write(folder, "Content/resources/masterpages/footer.flmsp", Page(prefix, uri,
        $"<{prefix}:bodyProxy/><{prefix}:pageFooter><p>Sharedfooterword</p><p {prefix}:conditions='Primary.SWHELP'>Desktopfooterword</p><p {prefix}:conditions='Primary.WEBHELP'>Publicfooterword</p><{prefix}:equation>$z+1$</{prefix}:equation></{prefix}:pageFooter>"));
    Write(folder, "Content/resources/stylesheets/statsdirect.css", "p{color:black}");
    Write(folder, "Content/basics/example.htm", Page(prefix, uri,
        $"<h1><{prefix}:keyword term='KeywordOnlyNeedle'/>Namespace equivalence α</h1>"
        + $"<p {prefix}:conditions='Primary.SWHELP'>Desktoponlyword</p><p {prefix}:conditions='Primary.WEBHELP'>Publiconlyword</p>"
        + $"<p {prefix}:searchable='False' {prefix}:ignoredWords='metadata' {prefix}:lastBlocdatackDepth='2' onclick='bad()'>Sharedword</p>"
        + $"<{prefix}:equation style='font-size:10pt'>$x^2$</{prefix}:equation><{prefix}:equation><math xmlns='http://www.w3.org/1998/Math/MathML'><mfrac><mi>α</mi><mn>2</mn></mfrac></math></{prefix}:equation>"
        + $"<{prefix}:dropDown class='rcode'><{prefix}:dropDownHead><{prefix}:dropDownHotspot>R code</{prefix}:dropDownHotspot></{prefix}:dropDownHead><{prefix}:dropDownBody><pre xml:space='preserve'><![CDATA[x <- c(1, 2)\n  mean(x)]]></pre></{prefix}:dropDownBody></{prefix}:dropDown>"
        + "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><path d='M0 0L1 1'/></svg><script>bad()</script>"));
    return folder;
}

try
{
    string legacyFolder = CreateFixture("Legacy", "Old", HelpSourceNamespaces.LegacyUri);
    string currentFolder = CreateFixture("Current", "sdh", HelpSourceNamespaces.CurrentUri);
    string runtime = Path.Combine(fixture, "Runtime");
    Write(runtime, "mathjax/tex-mml-svg.js", "// Isolated builder fixture; browser rendering is tested separately.");
    var legacy = HelpProject.Open(legacyFolder);
    var current = HelpProject.Open(currentFolder);
    var before = Digests(legacyFolder).Concat(Digests(currentFolder)).ToDictionary(pair => pair.Key, pair => pair.Value);
    foreach (var target in new[] { HelpBuildTarget.Desktop, HelpBuildTarget.Public })
    {
        string oldOutput = Path.Combine(fixture, "OutputLegacy" + target), newOutput = Path.Combine(fixture, "OutputCurrent" + target);
        var oldBuild = HelpBuilder.Build(legacy, oldOutput, runtime, target);
        var newBuild = HelpBuilder.Build(current, newOutput, runtime, target);
        Check(oldBuild.TopicCount == 1 && oldBuild.SearchDocumentCount == 1 && newBuild.TopicCount == 1 && newBuild.SearchDocumentCount == 1 && oldBuild.Warnings.Count == 0 && newBuild.Warnings.Count == 0,
            target + " builds both source vocabularies with matching counts and no warnings");
        foreach (string relative in new[] { "basics/example.htm", "contents-data.js", "search-index.js", "Data/Alias.xml" })
            Check(File.ReadAllText(Path.Combine(oldOutput, relative)) == File.ReadAllText(Path.Combine(newOutput, relative)), target + " produces identical " + relative);
        string html = File.ReadAllText(Path.Combine(newOutput, "basics/example.htm"));
        bool desktop = target == HelpBuildTarget.Desktop;
        Check(html.Contains(desktop ? "Desktoponlyword" : "Publiconlyword") && !html.Contains(desktop ? "Publiconlyword" : "Desktoponlyword") &&
            html.Contains(desktop ? "Desktopfooterword" : "Publicfooterword") && !html.Contains(desktop ? "Publicfooterword" : "Desktopfooterword"), target + " filters topic and footer conditions");
        Check(html.Contains("<footer class=\"help-shared-footer\">") && !html.Contains(HelpSourceNamespaces.LegacyUri) && !html.Contains(HelpSourceNamespaces.CurrentUri) &&
            !html.Contains("conditions=") && !html.Contains("searchable=") && !html.Contains("onclick=") && !html.Contains("bad()"), target + " removes authoring declarations and metadata, including the footer root");
        Check(html.Contains("<details") && html.Contains("<summary>R code</summary>") && html.Replace("\r\n", "\n").Contains("x &lt;- c(1, 2)\n  mean(x)") &&
            html.Contains("help-equation-live") && html.Contains("http://www.w3.org/1998/Math/MathML") && html.Contains("http://www.w3.org/2000/svg"), target + " preserves R spacing, equations, MathML and SVG namespaces");
        string search = File.ReadAllText(Path.Combine(newOutput, "search-index.js"));
        string contents = File.ReadAllText(Path.Combine(newOutput, "contents-data.js"));
        Check(search.Contains("keywordonlyneedle") && contents.Contains(desktop ? "DesktopOnlyEntry" : "PublicOnlyEntry") && !contents.Contains(desktop ? "PublicOnlyEntry" : "DesktopOnlyEntry"), target + " retains keyword indexing and conditional contents");
    }
    Check(before.All(pair => File.Exists(pair.Key) && Digest(pair.Key) == pair.Value), "Building legacy and current projects leaves every source byte unchanged");
    legacy.SaveContents(legacy.LoadContents());
    var savedContents = XDocument.Load(Path.Combine(legacyFolder, "Project/TOCs/STATSDIRECT.fltoc"));
    Check(savedContents.Descendants("TocEntry").Count(element => element.Attribute(HelpSourceNamespaces.Current + "conditions") != null) == 2 &&
        !File.ReadAllText(Path.Combine(legacyFolder, "Project/TOCs/STATSDIRECT.fltoc")).Contains(HelpSourceNamespaces.LegacyUri), "Explicit Contents saves migrate authoring conditions while retaining the original project structure");

    string source = "<?xml version='1.0' encoding='utf-8'?>\r\n<html\r\n xmlns:Old = '" + HelpSourceNamespaces.LegacyUri + "' xmlns:feature='urn:feature'><head><title>Exact source</title></head><body>"
        + "<!-- xmlns:Old='" + HelpSourceNamespaces.LegacyUri + "' -->"
        + "<p data-example='" + HelpSourceNamespaces.LegacyUri + "'>The namespace URL in prose: " + HelpSourceNamespaces.LegacyUri + " &amp; &#945;.</p>"
        + "<Old:equation Old:lastBlocdatackDepth='2'>$\\frac{x}{2}$</Old:equation><feature:value>Unrelated vocabulary</feature:value>"
        + "<pre><![CDATA[x <- 1\r\n  # xmlns:Old='" + HelpSourceNamespaces.LegacyUri + "']]></pre>"
        + "<div xmlns:Nested=\"" + HelpSourceNamespaces.LegacyUri + "\"><Nested:keyword term='Nested'/></div></body></html>\r\n";
    string expected = source.Replace("xmlns:Old = '" + HelpSourceNamespaces.LegacyUri + "'", "xmlns:Old = '" + HelpSourceNamespaces.CurrentUri + "'")
        .Replace("xmlns:Nested=\"" + HelpSourceNamespaces.LegacyUri + "\"", "xmlns:Nested=\"" + HelpSourceNamespaces.CurrentUri + "\"");
    current.SaveTopic("basics/example.htm", source);
    Check(current.ReadTopic("basics/example.htm") == expected, "An explicit save changes actual namespace declarations alone, preserving protected source and unrelated URI text exactly");
    var saved = XDocument.Parse(current.ReadTopic("basics/example.htm"));
    Check(saved.Descendants(HelpSourceNamespaces.Current + "equation").Single().Attribute(HelpSourceNamespaces.Current + "lastBlocdatackDepth")?.Value == "2" &&
        saved.Descendants(XName.Get("value", "urn:feature")).Single().Value == "Unrelated vocabulary", "Imported extensions and legacy typo metadata now use the own vocabulary; unrelated namespaces survive");
    current.SaveTopic("basics/example.htm", expected);
    Check(current.ReadTopic("basics/example.htm") == expected, "Saving canonical source is byte-exact and idempotent");
    string created = current.CreateTopic("New own namespace page");
    string newPage = current.ReadTopic(created);
    Check(newPage.Contains("xmlns:sdh=\"" + HelpSourceNamespaces.CurrentUri + "\"") && !newPage.Contains(HelpSourceNamespaces.LegacyUri), "New pages declare the own namespace using the preferred sdh prefix");
    string conflict = Page("old", HelpSourceNamespaces.LegacyUri, $"<p xmlns:sdh='{HelpSourceNamespaces.CurrentUri}' old:conditions='Primary.SWHELP' sdh:conditions='Primary.WEBHELP'>Conflict</p>");
    bool rejected = false;
    try { current.SaveTopic("basics/example.htm", conflict); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected && current.ReadTopic("basics/example.htm") == expected, "Conflicting mixed-vocabulary attributes fail before replacing the valid saved page");

    var oldEquation = XElement.Parse($"<old:equation xmlns:old='{HelpSourceNamespaces.LegacyUri}' xmlns:m='http://www.w3.org/1998/Math/MathML' old:lastHeight='20' style='font-size:10pt'><m:math><m:mi>α</m:mi></m:math></old:equation>");
    var ownEquation = XElement.Parse(oldEquation.ToString(SaveOptions.DisableFormatting).Replace(HelpSourceNamespaces.LegacyUri, HelpSourceNamespaces.CurrentUri));
    Check(EquationArtworkCatalog.CanonicalSource(oldEquation) == EquationArtworkCatalog.CanonicalSource(ownEquation) && EquationArtworkCatalog.SourceKey(oldEquation) == EquationArtworkCatalog.SourceKey(ownEquation), "Namespace migration keeps canonical equation artwork identity, including authoring attributes and MathML children");
    ownEquation.Name = XName.Get("equation", "urn:unrelated");
    Check(EquationArtworkCatalog.SourceKey(oldEquation) != EquationArtworkCatalog.SourceKey(ownEquation), "Unrelated namespaces cannot claim the identity of verified equation artwork");
    foreach (var result in SearchIndexChecks.Run(fixture)) Check(result.Passed, result.Name);
}
catch (Exception exception)
{
    Check(false, exception.ToString());
}

var report = new { passed = failed == 0, checkedAtUtc = DateTimeOffset.UtcNow, fixture, checks = checks.Count, failed, results = checks };
File.WriteAllText(Path.Combine(evidence, "namespace-core-checks.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { report.passed, report.checks, report.failed, report.fixture }));
return failed == 0 ? 0 : 1;
