using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args.Length > 0 ? args[0] : "../../../../../");
string evidence = Path.Combine(root, "evidence");
string fixtures = Path.Combine(evidence, "SearchReplaceFixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtures);
var checks = new List<object>();
int passed = 0, failed = 0;
void Check(bool value, string name, object? detail = null)
{
    if (value) passed++; else failed++;
    checks.Add(new { name, passed = value, detail });
    Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}");
}
void Case(string name, Action action)
{
    try { action(); }
    catch (Exception ex) { Check(false, name + " completed without an unexpected exception", ex.ToString()); }
}
void Reject(Action action, string name)
{
    try { action(); Check(false, name); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or System.Xml.XmlException or UnauthorizedAccessException)
    { Check(true, name); }
}
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
Dictionary<string, string> Digests(string path) => Directory.GetFiles(path, "*", SearchOption.AllDirectories).ToDictionary(file => Path.GetRelativePath(path, file), Hash);
bool Same(Dictionary<string, string> before, string path)
{
    var after = Digests(path);
    return before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string? hash) && hash == pair.Value);
}
string Page(string body, string title = "Fixture") => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>" + title + "</title></head>\r\n<body>" + body + "</body></html>\r\n";
void Write(string directory, string relative, string text, Encoding? encoding = null)
{
    string path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text, encoding ?? new UTF8Encoding(false));
}
(HelpProject Project, string Path) Fixture(string name, params (string Path, string Text)[] topics)
{
    string directory = Path.Combine(fixtures, name); Directory.CreateDirectory(directory);
    Write(directory, HelpProject.MarkerFile, "{\"prototypeWorkingCopy\":true}");
    Write(directory, "Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title=\"One\" Link=\"/Content/a.htm\"/></CatapultToc>");
    foreach (var topic in topics) Write(directory, "Content/" + topic.Path, topic.Text);
    return (HelpProject.Open(directory), directory);
}
string[] Ids(HelpSearchPreview preview) => preview.Matches.Select(match => match.Id).ToArray();

Case("Corpus search and selected replacement", () =>
{
    var f = Fixture("selection", ("a.htm", Page("<h1>Needle</h1><p>Needle needle Needles needlework.</p><a href=\"https://example.invalid/Needle\">Needle link</a>", "Needle title")),
        ("nested/b.html", Page("<p>needle beta</p>")), ("unlinked.htm", Page("<p>Needle outside Contents</p>")));
    var options = new HelpSearchOptions("Needle", "Changed", WholeWords: true);
    var preview = f.Project.SearchReplacePreview(options);
    Check(preview.Matches.Count == 7 && preview.Matches.Select(match => match.TopicPath).Distinct().Count() == 3, "Find all includes titles, body, HTML extensions, nested pages and pages absent from Contents");
    Check(preview.Matches.All(match => match.SourceOffset >= 0 && match.SourceLength > 0 && match.Line > 0) && preview.Matches.Select(match => match.Id).Distinct().Count() == preview.Matches.Count, "Matches carry unique selection IDs and usable source positions");
    var before = Digests(f.Path);
    var none = f.Project.ApplySearchReplace(preview, []);
    Check(none.ChangedPages == 0 && none.Replacements == 0 && Same(before, f.Path), "An empty selection changes no files and creates no undo batch");
    string chosen = preview.Matches.Single(match => match.TopicPath == "nested/b.html").Id;
    var selected = f.Project.ApplySearchReplace(preview, [chosen]);
    Check(selected.ChangedPages == 1 && selected.Replacements == 1 && selected.ChangedTopicPaths.SequenceEqual(["nested/b.html"]), "Replace selected changes only the selected occurrence's page");
    Check(f.Project.ReadTopic("nested/b.html").Contains("Changed beta") && f.Project.ReadTopic("a.htm").Contains("Needle needle Needles needlework"), "Selected replacement leaves every unselected page and occurrence intact");
    Check(f.Project.CanUndoSearchReplace, "A successful replacement exposes one session-local Undo batch");
    var repeat = f.Project.SearchReplacePreview(new("Changed", "Changed", MatchCase: true));
    var repeated = f.Project.ApplySearchReplace(repeat, Ids(repeat));
    Check(repeated.Replacements == 0 && f.Project.CanUndoSearchReplace, "A later no-op does not discard the previous meaningful Undo batch");
    var undone = f.Project.UndoLastSearchReplace();
    Check(undone.ChangedPages == 1 && Same(before, f.Path) && !f.Project.CanUndoSearchReplace, "Undo restores exact original bytes and consumes the replacement batch");
    var all = f.Project.SearchReplacePreview(options);
    var result = f.Project.ApplySearchReplace(all, Ids(all));
    Check(result.ChangedPages == 3 && result.Replacements == 7 && f.Project.ListTopics().Single(topic => topic.Path == "a.htm").Title == "Changed title", "Replace all changes every reviewed match and updates page titles");
    Check(f.Project.ReadTopic("a.htm").Contains("Needles needlework") && f.Project.ReadTopic("a.htm").Contains("href=\"https://example.invalid/Needle\""), "Whole-word text replacement preserves longer words and link destinations");
});

Case("Case and Unicode boundaries", () =>
{
    var f = Fixture("words", ("a.htm", Page("<p>Needle needle NEEDLE Needles needlework Needle_2 Needleβ (Needle) café CAFÉ</p>")));
    Check(f.Project.SearchReplacePreview(new("Needle", "X", MatchCase: true, WholeWords: true)).Matches.Count == 2, "Match case respects exact spelling and punctuation boundaries");
    Check(f.Project.SearchReplacePreview(new("needle", "X", WholeWords: true)).Matches.Count == 4, "Whole words exclude suffixes, digits/underscores and Unicode letters");
    Check(f.Project.SearchReplacePreview(new("café", "X", WholeWords: true)).Matches.Count == 2, "Case-insensitive text search handles Unicode accented letters");
    var before = Digests(f.Path);
    var noop = f.Project.SearchReplacePreview(new("Needle", "Needle", MatchCase: true));
    var result = f.Project.ApplySearchReplace(noop, Ids(noop));
    Check(result.ChangedPages == 0 && result.Replacements == 0 && !f.Project.CanUndoSearchReplace && Same(before, f.Path), "Replacing text with the same text does not rewrite pages or create misleading Undo");
    Reject(() => f.Project.SearchReplacePreview(new("", "X")), "An empty find string is rejected instead of matching every source position");
});

Case("Protected source and visible runs", () =>
{
    string protectedParts = "<!-- Needle comment --><pre xml:space=\"preserve\">Needle &lt;- c(1, 2)\n  mean(Needle)</pre><code>Needle</code><textarea>Needle</textarea><script>var Needle=1;</script><style>.Needle{color:red}</style><math xmlns=\"http://www.w3.org/1998/Math/MathML\"><mi>Needle</mi></math><MadCap:equation>\\frac{Needle}{2}</MadCap:equation><svg xmlns=\"http://www.w3.org/2000/svg\"><text>Needle</text></svg><MadCap:dropDown class=\"rcode\"><MadCap:dropDownHead>R code</MadCap:dropDownHead><MadCap:dropDownBody><p>Needle note</p><pre>Needle &lt;- 1</pre></MadCap:dropDownBody></MadCap:dropDown>";
    string source = Page("<p data-label=\"Needle\">Needle normal text.</p>" + protectedParts);
    var f = Fixture("protection", ("a.htm", source));
    var preview = f.Project.SearchReplacePreview(new("Needle", "Changed"));
    Check(preview.Matches.Count == 1, "Default text mode skips attributes, comments, R/code, MathML, equations, SVG and R dropdowns");
    f.Project.ApplySearchReplace(preview, Ids(preview));
    Check(f.Project.ReadTopic("a.htm") == source.Replace("Needle normal text", "Changed normal text", StringComparison.Ordinal), "Text replacement preserves all protected markup and code bytes exactly");
    var inline = Fixture("inline", ("a.htm", Page("<p>A <b class=\"keep\">bold</b> word after.</p><p>start <code>blocked</code>end</p><p>blockone</p><p>blocktwo</p>")));
    var phrase = inline.Project.SearchReplacePreview(new("A bold word", "New phrase"));
    Check(phrase.Matches.Count == 1, "A visible phrase can span inline formatting without requiring HTML knowledge");
    inline.Project.ApplySearchReplace(phrase, Ids(phrase));
    var document = XDocument.Parse(inline.Project.ReadTopic("a.htm"));
    Check(document.Descendants("p").First().Value == "New phrase after." && (string?)document.Descendants("b").Single().Attribute("class") == "keep", "Inline replacement preserves formatting elements and unmatched text");
    Check(inline.Project.SearchReplacePreview(new("start end", "X")).Matches.Count == 0 && inline.Project.SearchReplacePreview(new("blockoneblocktwo", "X")).Matches.Count == 0, "Text search never joins across protected code or paragraph boundaries");
});

Case("Entity and line mapping", () =>
{
    var f = Fixture("entities", ("a.htm", "<?xml version=\"1.0\"?>\r\n<html><head><title>Entity</title></head>\r\n<body>\r\n<p>Fish &amp; chips &#x3b1; &lt; 2</p>\r\n</body></html>"));
    var preview = f.Project.SearchReplacePreview(new("Fish & chips α < 2", "α & < >"));
    Check(preview.Matches.Count == 1 && preview.Matches[0].Line == 4, "Visible search decodes XML entities and reports the original source line");
    f.Project.ApplySearchReplace(preview, Ids(preview));
    string changed = f.Project.ReadTopic("a.htm");
    Check(XDocument.Parse(changed).Descendants("p").Single().Value == "α & < >" && changed.Contains("\r\n<p>") && changed.EndsWith("\r\n</body></html>", StringComparison.Ordinal), "Text replacement escapes XML characters while preserving surrounding CRLF bytes");
    f.Project.UndoLastSearchReplace();
    var before = Digests(f.Path);
    var noop = f.Project.SearchReplacePreview(new("&", "&")); f.Project.ApplySearchReplace(noop, Ids(noop));
    Check(Same(before, f.Path), "An entity-aware no-op preserves the original entity spelling");
});

Case("HTML source mode and batch validation", () =>
{
    var f = Fixture("html", ("a.htm", Page("<p><a href=\"https://example.invalid/old\">Visible</a></p><MadCap:equation>\\frac{x_1}{2}</MadCap:equation><pre>x_1 &lt;- 3\n  mean(x_1)</pre>")));
    var links = f.Project.SearchReplacePreview(new("https://example.invalid/old", "https://example.invalid/new", Scope: HelpSearchScope.HtmlSource));
    f.Project.ApplySearchReplace(links, Ids(links));
    Check((string?)XDocument.Parse(f.Project.ReadTopic("a.htm")).Descendants("a").Single().Attribute("href") == "https://example.invalid/new", "Explicit HTML mode can update link destinations");
    var code = f.Project.SearchReplacePreview(new("x_1", "y_1", Scope: HelpSearchScope.HtmlSource));
    Check(code.Matches.Count == 3, "Explicit HTML mode can review equation and R-code source occurrences");
    f.Project.ApplySearchReplace(code, Ids(code));
    Check(f.Project.ReadTopic("a.htm").Contains("\\frac{y_1}{2}") && f.Project.ReadTopic("a.htm").Contains("y_1 &lt;- 3\n  mean(y_1)"), "HTML replacements retain equation syntax, entity spelling and R indentation");
    var invalid = Fixture("invalid-batch", ("a.htm", Page("<p>FLAG</p>")), ("b.htm", Page("<p title=\"FLAG\">Later page</p>")));
    var before = Digests(invalid.Path);
    var unsafePreview = invalid.Project.SearchReplacePreview(new("FLAG", "\"", Scope: HelpSearchScope.HtmlSource));
    Reject(() => invalid.Project.ApplySearchReplace(unsafePreview, Ids(unsafePreview)), "Invalid XML in a later page rejects the entire reviewed batch");
    Check(Same(before, invalid.Path) && !invalid.Project.CanUndoSearchReplace, "All affected pages are validated before any batch file is written");
});

Case("Supplementary Unicode and semantic no-ops", () =>
{
    var f = Fixture("supplementary-entity", ("a.htm", Page("<p>A &#x1F600; B</p>")));
    var preview = f.Project.SearchReplacePreview(new("A 😀", "Changed"));
    Check(preview.Matches.Count == 1, "Entity-aware search maps a supplementary Unicode character as one source entity");
    f.Project.ApplySearchReplace(preview, Ids(preview));
    Check(XDocument.Parse(f.Project.ReadTopic("a.htm")).Descendants("p").Single().Value == "Changed B", "Replacing a phrase containing a supplementary entity creates no overlapping or duplicated source edits");
    var words = Fixture("supplementary-word", ("a.htm", Page("<p>X𐐀Y X Y</p>")));
    Check(words.Project.SearchReplacePreview(new("X", "Z", MatchCase: true, WholeWords: true)).Matches.Count == 1, "Supplementary-plane Unicode letters remain part of a word boundary");
    var numeric = Fixture("numeric-noop", ("a.htm", Page("<p>Fish &#38; chips</p>")));
    var before = Digests(numeric.Path);
    var same = numeric.Project.SearchReplacePreview(new("&", "&"));
    var unchanged = numeric.Project.ApplySearchReplace(same, Ids(same));
    Check(unchanged.Replacements == 0 && Same(before, numeric.Path), "A semantic entity no-op preserves numeric entity spelling byte for byte");
    var casing = Fixture("normalize-case", ("a.htm", Page("<p>StatsDirect statsdirect STATSDIRECT</p>")));
    var normalized = casing.Project.SearchReplacePreview(new("StatsDirect", "StatsDirect"));
    var result = casing.Project.ApplySearchReplace(normalized, Ids(normalized));
    Check(result.Replacements == 2 && XDocument.Parse(casing.Project.ReadTopic("a.htm")).Descendants("p").Single().Value == "StatsDirect StatsDirect StatsDirect", "Case-insensitive replacement can normalize differing case while preserving exact-case no-ops");
});

Case("Encoding preservation", () =>
{
    foreach (var item in new[] { (Name: "utf8-bom", Encoding: (Encoding)new UTF8Encoding(true), Declaration: "utf-8"), (Name: "utf16-le", Encoding: (Encoding)new UnicodeEncoding(false, true), Declaration: "utf-16"), (Name: "utf16-be", Encoding: (Encoding)new UnicodeEncoding(true, true), Declaration: "utf-16") })
    {
        string original = "<?xml version=\"1.0\" encoding=\"" + item.Declaration + "\"?>\r\n<html><head><title>Encoding α</title></head><body><p>Needle α</p></body></html>\r\n";
        var f = Fixture(item.Name, ("a.htm", Page("<p>Temporary</p>")));
        string path = Path.Combine(f.Path, "Content", "a.htm"); File.WriteAllText(path, original, item.Encoding);
        byte[] before = File.ReadAllBytes(path);
        var preview = f.Project.SearchReplacePreview(new("Needle", "Changed β")); f.Project.ApplySearchReplace(preview, Ids(preview));
        byte[] expected = item.Encoding.GetPreamble().Concat(item.Encoding.GetBytes(original.Replace("Needle", "Changed β", StringComparison.Ordinal))).ToArray();
        Check(File.ReadAllBytes(path).SequenceEqual(expected), item.Name + " preserves BOM, encoding and every unmatched newline/source byte");
        f.Project.UndoLastSearchReplace();
        Check(File.ReadAllBytes(path).SequenceEqual(before), item.Name + " Undo restores original bytes rather than reserializing XML");
    }
});

Case("Concurrent edits and selection ownership", () =>
{
    var f = Fixture("stale", ("a.htm", Page("<p>Needle</p>")), ("b.htm", Page("<p>Needle</p>")));
    var preview = f.Project.SearchReplacePreview(new("Needle", "Changed"));
    Write(f.Path, "Content/b.htm", Page("<p>Needle edited elsewhere</p>")); var before = Digests(f.Path);
    Reject(() => f.Project.ApplySearchReplace(preview, Ids(preview)), "A selected page changed after preview is not silently overwritten");
    Check(Same(before, f.Path), "Stale-preview refusal preserves both changed and unchanged pages");
    preview = f.Project.SearchReplacePreview(new("Needle", "Changed"));
    File.Delete(Path.Combine(f.Path, "Content", "b.htm")); before = Digests(f.Path);
    Reject(() => f.Project.ApplySearchReplace(preview, Ids(preview)), "Deleting a selected page invalidates its preview before any write");
    Check(Same(before, f.Path), "A deleted preview page is neither recreated nor partially replaced");
    preview = f.Project.SearchReplacePreview(new("Needle", "Changed")); before = Digests(f.Path);
    Reject(() => f.Project.ApplySearchReplace(preview, ["not-a-preview-match"]), "Unknown match IDs cannot redirect a replacement");
    Reject(() => f.Project.ApplySearchReplace(preview, [preview.Matches[0].Id, preview.Matches[0].Id]), "Duplicate selections are rejected instead of applying a source span twice");
    Check(Same(before, f.Path), "Invalid selection IDs leave source unchanged");
    var other = Fixture("other-project", ("a.htm", Page("<p>Needle</p>")));
    Reject(() => other.Project.ApplySearchReplace(preview, Ids(preview)), "A preview from another project cannot be applied to a different working copy");
    preview = f.Project.SearchReplacePreview(new("Needle", "Changed"));
    Write(f.Path, "Content/new-page.htm", Page("<p>Needle added since preview</p>")); before = Digests(f.Path);
    Reject(() => f.Project.ApplySearchReplace(preview, Ids(preview)), "A page added after Find all requires a fresh all-pages review");
    Check(Same(before, f.Path), "Page-inventory changes cannot cause partial or incomplete all-pages replacement");
});

Case("Whole-batch file guards and Undo conflicts", () =>
{
    var f = Fixture("guards", ("a.htm", Page("<p>Needle</p>")), ("z.htm", Page("<p>Needle</p>")));
    var preview = f.Project.SearchReplacePreview(new("Needle", "Changed")); var before = Digests(f.Path);
    string last = Path.Combine(f.Path, "Content", "z.htm");
    try
    {
        File.SetAttributes(last, File.GetAttributes(last) | FileAttributes.ReadOnly);
        Reject(() => f.Project.ApplySearchReplace(preview, Ids(preview)), "A later read-only page prevents all batch writes");
        Check(Same(before, f.Path), "Read-only refusal preserves the earlier writable page too");
    }
    finally { File.SetAttributes(last, File.GetAttributes(last) & ~FileAttributes.ReadOnly); }
    using (File.Open(last, FileMode.Open, FileAccess.Read, FileShare.Read))
        Reject(() => f.Project.ApplySearchReplace(preview, Ids(preview)), "A later locked page prevents all batch writes");
    Check(Same(before, f.Path), "File-lock refusal leaves the complete batch unchanged");
    f.Project.ApplySearchReplace(preview, Ids(preview));
    Write(f.Path, "Content/z.htm", Page("<p>Changed plus a new manual edit</p>")); var beforeUndo = Digests(f.Path);
    Reject(() => f.Project.UndoLastSearchReplace(), "Undo refuses to overwrite a page edited after the replacement");
    Check(Same(beforeUndo, f.Path), "Rejected Undo preserves manual edits and every other replaced page");
    Check(!Directory.EnumerateFiles(f.Path, "*", SearchOption.AllDirectories).Any(path => path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)), "Search and replace creates no obsolete backup builds or .bak files");
});

Case("Read-only preview freshness and navigation digests", () =>
{
    var f = Fixture("validate-preview", ("a.htm", Page("<p>Temporary</p>")));
    string path = Path.Combine(f.Path, "Content", "a.htm");
    string decoded = "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n<html><head><title>Navigation α</title></head><body><p>Needle α</p></body></html>\r\n";
    File.WriteAllText(path, decoded, new UnicodeEncoding(false, true));
    var preview = f.Project.SearchReplacePreview(new("Needle", "Changed")); var before = Digests(f.Path);
    f.Project.ValidateSearchReplacePreview(preview);
    Check(Same(before, f.Path) && !f.Project.CanUndoSearchReplace, "Freshness validation succeeds without changing source or creating an Undo batch");
    string expectedDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(decoded)));
    var match = preview.Matches.Single();
    Check(match.SourceDigest.Equals(expectedDigest, StringComparison.OrdinalIgnoreCase) && !match.SourceDigest.Equals(Hash(path), StringComparison.OrdinalIgnoreCase) && decoded.Substring(match.SourceOffset, match.SourceLength) == "Needle",
        "Navigation digest and offsets refer to decoded source, independent of the page's UTF-16 encoding and BOM");
    var foreign = Fixture("validate-foreign", ("a.htm", Page("<p>Needle</p>"))); var foreignBefore = Digests(foreign.Path);
    Reject(() => foreign.Project.ValidateSearchReplacePreview(preview), "Read-only freshness validation rejects another project's preview");
    Check(Same(foreignBefore, foreign.Path), "Foreign-preview validation cannot change the other working copy");
    File.AppendAllText(path, "<!-- A later edit changes navigation offsets -->", new UnicodeEncoding(false, true)); var changed = Digests(f.Path);
    Reject(() => f.Project.ValidateSearchReplacePreview(preview), "Read-only freshness validation rejects source changed since the result list was created");
    Check(Same(changed, f.Path), "Stale-result navigation validation preserves the later edit without writing source");
});

Case("Full help read-only search", () =>
{
    string original = Path.Combine(root, "HelpSource"); var before = Digests(original);
    string copy = Path.Combine(fixtures, "full-help"); Directory.CreateDirectory(copy);
    foreach (string folder in new[] { "Content", "Project", "RCode" })
    foreach (string path in Directory.GetFiles(Path.Combine(original, folder), "*", SearchOption.AllDirectories))
    {
        string destination = Path.Combine(copy, Path.GetRelativePath(original, path)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(path, destination);
    }
    File.Copy(Path.Combine(original, HelpProject.MarkerFile), Path.Combine(copy, HelpProject.MarkerFile));
    var project = HelpProject.Open(copy); var copiedBefore = Digests(copy);
    var preview = project.SearchReplacePreview(new("StatsDirect", "StatsDirect"));
    Check(project.ListTopics().Count == 404 && preview.Matches.Count > 100 && preview.Matches.Select(match => match.TopicPath).Distinct().Count() > 20, "Find all searches the complete real 404-topic corpus without depending on Contents membership");
    Check(Same(copiedBefore, copy) && Same(before, original), "Full-corpus preview leaves both its independent copy and all maintained help-source bytes unchanged");
});

File.WriteAllText(Path.Combine(evidence, "search-replace-checks.json"), JsonSerializer.Serialize(new { passed, failed, checkedAtUtc = DateTimeOffset.UtcNow, fixtures, realSourceApplied = false, results = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{passed} search/replace checks passed; {failed} failed");
return failed == 0 ? 0 : 1;
