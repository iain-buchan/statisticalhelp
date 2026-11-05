using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StatsDirect.HelpEditor.Core;

internal static class SourceContentChecks
{
internal static int Run(string root)
{
root = Path.GetFullPath(root);
string fixture = Path.Combine(Path.GetTempPath(), "sdhe-content-" + Guid.NewGuid().ToString("N")[..8]);
var checks = new List<object>(); int failed = 0;
void Check(bool value, string name, object? detail = null)
{ checks.Add(new { name, passed = value, detail }); if (!value) failed++; Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}"); }
XDocument Parse(string text) => XDocument.Parse(text, LoadOptions.PreserveWhitespace);
string Page(string body, string head = "") => "<html xmlns:xlink='http://www.w3.org/1999/xlink'><head><title>Example</title>" + head + "</head><body>" + body + "</body></html>";
bool Reject(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
void Write(string path, string text) { path = Path.Combine(fixture, path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
try
{
    Write(HelpProject.MarkerFile, "{}");
    Write("Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title='One' Link='/Content/one.htm'/><TocEntry Title='Two' Link='/Content/two.htm'/></CatapultToc>");
    const string safe = "<p>Replace me.</p><pre xml:space='preserve'>x &lt;- c(1, 2)\n  cat('&lt;script onclick=\"x\"&gt;javascript:alert(1)&lt;/script&gt;')\n# α &amp; β</pre><math xmlns='http://www.w3.org/1998/Math/MathML'><mfrac><mi>α</mi><mi>β</mi></mfrac></math><a href='https://example.invalid/paper?x=1&amp;y=2'>Reference</a>";
    string original = Page(safe, "<script type='text/javascript'>/*<![CDATA[*//*]]>*/</script>");
    Write("Content/one.htm", original); Write("Content/two.htm", original);
    var project = HelpProject.Open(fixture);
    string path = Path.Combine(fixture, "Content/one.htm");
    var cases = new Dictionary<string, string>
    {
        ["mixed-case event"] = Page("<p OnClIcK='alert(1)'>Text</p>"),
        ["root event"] = Page("<p>Text</p>").Replace("<html ", "<html onload='x' "),
        ["namespaced event"] = Page("<p xmlns:q='urn:q' q:onload='x'>Text</p>"),
        ["javascript URL"] = Page("<a href=' JaVaScRiPt:alert(1)'>Link</a>"),
        ["entity and URL whitespace"] = Page("<a href='&#x20;&#x09;java&#x0a;scri&#x0d;pt:alert(1)'>Link</a>"),
        ["SVG xlink URL"] = Page("<svg xmlns='http://www.w3.org/2000/svg'><a xlink:href='java&#x09;script:x'><text>SVG link</text></a></svg>"),
        ["formaction URL"] = Page("<button formaction='javascript:x'>Go</button>"),
        ["head script"] = Page("<p>Text</p>", "<script>window.bad=1</script>"),
        ["external script"] = Page("<p>Text</p>", "<script src='local.js'/>"),
        ["JSON data external source"] = Page("<script type='application/json' src='local.js'>{}</script>"),
        ["JSON data event"] = Page("<script type='application/json' onclick='bad()'>{}</script>"),
        ["ambiguous JSON type"] = Page("<script type='application/json' TYPE='text/javascript'>{}</script>"),
        ["SVG script with JSON type"] = Page("<svg xmlns='http://www.w3.org/2000/svg'><script type='application/json'>{}</script></svg>"),
        ["Unicode line separator script"] = Page("<p>Text</p>", "<script>// comment\u2028alert(1)</script>"),
        ["Unicode paragraph separator script"] = Page("<p>Text</p>", "<script>// comment\u2029alert(1)</script>"),
        ["SVG script"] = Page("<svg xmlns='http://www.w3.org/2000/svg'><script>bad()</script></svg>"),
        ["iframe srcdoc"] = Page("<iframe srcdoc='&lt;script&gt;bad()&lt;/script&gt;'/>"),
        ["object"] = Page("<object data='local.html'/>"),
        ["embed"] = Page("<embed src='local.html'/>")
    };
    foreach (var item in cases)
    {
        byte[] before = File.ReadAllBytes(path);
        Check(Reject(() => project.SaveTopic("one.htm", item.Value)) && before.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "Save refuses " + item.Key + " before writing");
        var imported = Parse(item.Value); int stripped = HelpSourceContent.StripActiveContent(imported.Root!);
        Check(stripped > 0 && !Reject(() => HelpSourceContent.ValidateForSave(imported)), "Parsed import removes " + item.Key);
    }
    var clean = Parse(original); string beforeClean = clean.ToString(SaveOptions.DisableFormatting);
    Check(HelpSourceContent.StripActiveContent(clean.Root!) == 0 && clean.ToString(SaveOptions.DisableFormatting) == beforeClean, "Clean import leaves R text, escaped active-looking examples, MathML and inert legacy head script unchanged");
    project.SaveTopic("one.htm", original);
    Check(File.ReadAllText(path) == original, "Safe save retains the exact source spelling and R indentation");
    foreach (string type in new[] { "application/json", "application/ld+json", " &#x09;ApPlIcAtIoN/JsOn&#x0a; " })
    {
        string data = Page("<script type='" + type + "'><![CDATA[{\"markup\":\"<tag onclick='example'>\",\"url\":\"javascript:example\",\"n\":2}]]></script>");
        var document = Parse(data);
        Check(HelpSourceContent.StripActiveContent(document.Root!) == 0 && !Reject(() => HelpSourceContent.ValidateForSave(document)), "Known inert JSON data survives import with MIME " + type);
        project.SaveTopic("one.htm", data);
        Check(File.ReadAllText(path) == data, "Known inert JSON data and its CDATA save byte-exact with MIME " + type);
    }
    project.SaveTopic("one.htm", original);
    HelpSourceContent.ValidateForSave(Parse(Page("<p data-note='javascript:example' xmlns:onclick='urn:example'>Safe metadata</p><a href='../page.htm#part'>Local</a><a href='mailto:stats@example.invalid'>Mail</a>")));
    Check(true, "Safe metadata and local/mail/reference links are accepted");

    byte[] oneBefore = File.ReadAllBytes(path), twoBefore = File.ReadAllBytes(Path.Combine(fixture, "Content/two.htm"));
    var preview = project.SearchReplacePreview(new("<p>Replace me.</p>", "<p onclick='x'>Replaced.</p>", Scope: HelpSearchScope.HtmlSource));
    Check(preview.Matches.Count == 2 && Reject(() => project.ApplySearchReplace(preview, preview.Matches.Select(x => x.Id))) &&
        oneBefore.AsSpan().SequenceEqual(File.ReadAllBytes(path)) && twoBefore.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(fixture, "Content/two.htm"))), "Batch source replacement rejects unsafe content before changing either page");

    var corpus = Directory.EnumerateFiles(Path.Combine(root, "HelpSource", "Content"), "*", SearchOption.AllDirectories).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".htm" or ".html").ToArray();
    var hashes = corpus.ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
    foreach (string file in corpus) HelpSourceContent.ValidateForSave(Parse(File.ReadAllText(file)));
    Check(corpus.Length == 404 && hashes.All(x => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x.Key))) == x.Value), "All 404 maintained topics pass the policy without changing their source");

    // Raw source builds use the same cleaning policy, independently of the GUI.
    Write("Content/one.htm", Page(safe + "<p id='bad' onclick='x'><a href='java&#x09;script:x'>Bad</a></p><iframe src='local.htm'/><object/><embed/><svg xmlns='http://www.w3.org/2000/svg'><script>window.bad=1</script></svg>"));
    var build = HelpBuilder.Build(project, Path.Combine(fixture, "Output"), Path.Combine(root, "src", "StatsDirectHelpEditor.App", "Web"));
    string html = File.ReadAllText(Path.Combine(build.OutputDirectory, "one.htm"));
    Check(!Regex.IsMatch(html, @"<[^>]+\sonclick\s*=", RegexOptions.IgnoreCase) && !html.Contains("href=\"javascript:", StringComparison.OrdinalIgnoreCase) && !html.Contains("<iframe", StringComparison.OrdinalIgnoreCase) && !html.Contains("<object", StringComparison.OrdinalIgnoreCase) && !html.Contains("<embed", StringComparison.OrdinalIgnoreCase) && !html.Contains("window.bad=1"), "Headless output removes active elements and obfuscated URL attributes");
    Check(html.Contains("cat('&lt;script onclick=\"x\"&gt;javascript:alert(1)&lt;/script&gt;')") && html.Contains("# α &amp; β") && html.Contains("<mfrac>"), "Headless output preserves escaped code examples and MathML");
}
catch (Exception ex) { Check(false, "Checks completed", ex.ToString()); }
finally
{
    string evidence = Path.Combine(root, "evidence", "SourceContent-20261104"); Directory.CreateDirectory(evidence);
    File.WriteAllText(Path.Combine(evidence, "core-checks.json"), JsonSerializer.Serialize(new { completedAtUtc = DateTimeOffset.UtcNow, fixture, passed = failed == 0, count = checks.Count, failed, checks }, new JsonSerializerOptions { WriteIndented = true }));
}
Console.WriteLine($"{checks.Count - failed} passed; {failed} failed");
return failed == 0 ? 0 : 1;
}
}
