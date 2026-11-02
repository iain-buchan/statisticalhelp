using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args.Length > 0 ? args[0] : "../../../../../");
string evidence = Path.Combine(root, "evidence"); Directory.CreateDirectory(evidence);
string fixtures = Path.Combine(evidence, "PolishFixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixtures);
var checks = new List<object>(); int passed = 0, failed = 0;
void Check(bool value, string name, object? detail = null) { if (value) passed++; else failed++; checks.Add(new { name, passed = value, detail }); Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}"); }
void Case(string name, Action action) { try { action(); } catch (Exception ex) { Check(false, name + " completed", ex.ToString()); } }
void Reject(Action action, string name) { try { action(); Check(false, name); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or InvalidDataException or XmlException) { Check(true, name); } }
string Page(string body, string title = "Fixture") => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>" + title + "</title><style>.prose { color: red; }</style></head>\r\n<body>" + body + "</body></html>\r\n";
void Write(string directory, string relative, string text) { string path = Path.Combine(directory, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); }
(HelpProject Project, string Directory) Fixture(string name, params (string Path, string Source)[] topics)
{
    string directory = Path.Combine(fixtures, name); Directory.CreateDirectory(directory);
    Write(directory, HelpProject.MarkerFile, "{\"prototypeWorkingCopy\":true}");
    Write(directory, "Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title=\"One\" Link=\"/Content/a.htm\"/></CatapultToc>");
    foreach (var topic in topics) Write(directory, "Content/" + topic.Path, topic.Source);
    return (HelpProject.Open(directory), directory);
}
HelpPolishReview Change(HelpPolishSnapshot snapshot, string before, string after, string reason = "Improve readability")
{
    var block = snapshot.Blocks.Single(b => b.Text == before);
    return snapshot.Review([new(block.Id, before, after, reason)]);
}

Case("Source-preserving review", () =>
{
    string source = Page("<!-- preserve this -->\r\n<p class='lead'>  This is are poor prose.  </p>\r\n<p>Other wording.</p>");
    var snapshot = HelpPolishSnapshot.Create("basics/example.htm", source);
    Check(snapshot.TopicPath == "basics/example.htm" && snapshot.Source == source && snapshot.SourceDigest.Length == 64, "Snapshot freezes the exact raw draft and relative page identity");
    Check(snapshot.Blocks.Select(b => b.Text).SequenceEqual(["This is are poor prose.", "Other wording."]), "Only ordinary body prose is editable, excluding metadata and whitespace");
    Check(snapshot.Blocks[0].Location == "p · line 4", "Each proposed prose run carries a source location");
    var again = HelpPolishSnapshot.Create("basics/example.htm", source);
    Check(snapshot.Blocks.Select(b => b.Id).SequenceEqual(again.Blocks.Select(b => b.Id)), "Text-run identifiers are deterministic for an unchanged snapshot");
    var review = Change(snapshot, "This is are poor prose.", "This is clear prose.");
    Check(ReferenceEquals(review.Snapshot, snapshot) && review.Changes.Count == 1 && review.Changes[0].BlockId == snapshot.Blocks[0].Id, "Review is bound to its original snapshot and identifies the editable block");
    Check(review.Apply([]) == source, "Rejecting every suggestion returns the exact original draft");
    string updated = review.Apply(review.Changes.Select(c => c.Id));
    Check(updated == source.Replace("This is are poor prose.", "This is clear prose."), "Accepting one change preserves tags, quotes, CRLF, comments, whitespace and all other text exactly");
    Check(review.Changes[0].Before == "This is are poor prose." && review.Changes[0].After == "This is clear prose." && review.Changes[0].Reason == "Improve readability", "Tracked review exposes original, proposed text and explanation");
    Check(review.Apply([]) == source && snapshot.Source == source, "Review application is pure and repeatable without changing its snapshot");
});

Case("Selected changes and escaping", () =>
{
    string source = Page("<p>First wording.</p><p>Second wording.</p><p>A &amp; B and &#233; and &#x1F642;.</p><p>Before <strong>bold wording</strong> after.</p>");
    var snapshot = HelpPolishSnapshot.Create("a.htm", source);
    var first = snapshot.Blocks.Single(b => b.Text == "First wording."); var second = snapshot.Blocks.Single(b => b.Text == "Second wording.");
    var review = snapshot.Review([new(first.Id, first.Text, "First improved wording.", "Clarity"), new(second.Id, second.Text, "Second improved wording.", "Clarity")]);
    string selected = review.Apply([review.Changes[1].Id]);
    Check(selected == source.Replace("Second wording.", "Second improved wording."), "Accepting a subset changes only the chosen run");
    Check(review.Apply(review.Changes.Select(c => c.Id)) == source.Replace("First wording.", "First improved wording.").Replace("Second wording.", "Second improved wording."), "Applying multiple suggestions respects original offsets");
    var escaped = Change(snapshot, "A & B and é and 🙂.", "A & B: é and 🙂 <greater>.");
    string escapedSource = escaped.Apply([escaped.Changes[0].Id]);
    Check(escapedSource.Contains("A &amp; B: é and 🙂 &lt;greater&gt;."), "Changed prose escapes XML markup and decodes entities including supplementary Unicode");
    Check(Change(snapshot, "First wording.", "Improved wording.").Apply([]).Contains("A &amp; B and &#233; and &#x1F642;."), "Untouched entity spellings remain byte-for-character intact");
    var inline = Change(snapshot, "bold wording", "clearer wording");
    Check(inline.Apply([inline.Changes[0].Id]).Contains("Before <strong>clearer wording</strong> after."), "Changes preserve inline formatting and adjacent text");
    var cdata = HelpPolishSnapshot.Create("cdata.htm", Page("<p><![CDATA[Unclear prose.]]></p>"));
    var cdReview = Change(cdata, "Unclear prose.", "Clear prose with ]]> safely included.");
    string cdUpdated = cdReview.Apply([cdReview.Changes[0].Id]);
    Check(XDocument.Parse(cdUpdated).Descendants("p").Single().Value == "Clear prose with ]]> safely included.", "CDATA replacements split closing delimiters safely without changing visible text");
});

Case("Statistical content protection", () =>
{
    string source = Page("<p>Editable prose.</p><math><mi>Protected math</mi></math><MadCap:equation>$\\frac{protected}{equation}$</MadCap:equation><svg><text>Protected SVG</text></svg>"
        + "<pre>Protected pre</pre><code>Protected code</code><script>Protected script</script><textarea>Protected textarea</textarea><kbd>Protected keys</kbd><var>Protected variable</var><sup>Protected superscript</sup><sub>Protected subscript</sub>"
        + "<MadCap:dropDown class='rcode'><MadCap:dropDownHead>Protected R title</MadCap:dropDownHead><p>Protected R note</p><pre>x &lt;- c(1,2)</pre></MadCap:dropDown>"
        + "<p class='rcode'>Protected R class</p><p class='rcodenote'>Protected R note class</p><p hidden='hidden'>Protected hidden</p><div aria-hidden='true'><p>Protected aria</p></div><p style='color:red; display: none !important'>Protected style</p>"
        + "<p style='visibility:hidden'>Protected visibility</p><a href='https://example.invalid/protected' title='Protected title'>Link prose</a><p>t <i>P</i> = 0.05</p>");
    var snapshot = HelpPolishSnapshot.Create("a.htm", source);
    Check(snapshot.Blocks.Select(b => b.Text).SequenceEqual(["Editable prose.", "Link prose"]), "Equations, SVG, R/code, hidden text, attributes and one-letter symbolic runs are protected");
    var review = Change(snapshot, "Link prose", "Clearer link prose");
    string updated = review.Apply([review.Changes[0].Id]);
    Check(updated == source.Replace(">Link prose<", ">Clearer link prose<"), "Editing link wording preserves its destination and every protected construct");
    var numeric = HelpPolishSnapshot.Create("numbers.htm", Page("<p>The 95% interval is 1.25 to 3.5 at n = 42; P = 1e-05.</p>"));
    string text = numeric.Blocks.Single().Text;
    Check(Change(numeric, text, "The 95% interval ranges from 1.25 to 3.5 for n = 42; P = 1e-05.").Changes.Count == 1, "Number-preserving improvements to ordinary wording remain reviewable");
    Reject(() => Change(numeric, text, text.Replace("95%", "90%")), "Polishing refuses altered confidence levels or numeric results");
    Reject(() => Change(numeric, text, text.Replace("1.25 to 3.5", "3.5 to 1.25")), "Polishing refuses reordered numeric values");
    var urls = HelpPolishSnapshot.Create("url.htm", Page("<p>Read https://example.invalid/article for guidance.</p>"));
    Reject(() => Change(urls, urls.Blocks.Single().Text, "Read https://elsewhere.invalid/article for guidance."), "Visible reference URLs cannot be changed by prose polishing");
});

Case("Invalid model output and identity", () =>
{
    var snapshot = HelpPolishSnapshot.Create("a.htm", Page("<p>Original wording.</p>")); var block = snapshot.Blocks.Single();
    Reject(() => snapshot.Review([new("missing", block.Text, "Changed wording.", "Reason")]), "Unknown or protected block identifiers are rejected");
    Reject(() => snapshot.Review([new(block.Id, "Stale wording.", "Changed wording.", "Reason")]), "Stale original text cannot be applied");
    Reject(() => snapshot.Review([new(block.Id, block.Text, "Changed wording.", "Reason"), new(block.Id, block.Text, "Other wording.", "Reason")]), "Duplicate or overlapping suggestions are rejected");
    Reject(() => snapshot.Review([new(block.Id, block.Text, block.Text, "No change")]), "No-op suggestions are rejected");
    Reject(() => snapshot.Review([new(block.Id, block.Text, "Invalid\0wording.", "Reason")]), "Illegal XML characters in AI text are rejected");
    Reject(() => snapshot.Review([new(block.Id, block.Text, new string('x', 5000), "Reason")]), "Unexpectedly large replacement text is rejected");
    var review = Change(snapshot, block.Text, "Changed wording."); var foreign = Change(snapshot, block.Text, "Other wording.");
    Reject(() => review.Apply([foreign.Changes[0].Id]), "A change from another review cannot be accepted here");
    Reject(() => review.Apply([review.Changes[0].Id, review.Changes[0].Id]), "Duplicate accepted change IDs are rejected");
    Reject(() => HelpPolishSnapshot.Create("../a.htm", snapshot.Source), "Relative traversal is rejected in current page identity");
    Reject(() => HelpPolishSnapshot.Create("C:/a.htm", snapshot.Source), "Absolute paths are rejected in current page identity");
    Reject(() => HelpPolishSnapshot.Create("a.htm", "<html><body><p>Bad</body></html>"), "Malformed XHTML cannot be polished");
    Reject(() => HelpPolishSnapshot.Create("a.htm", "<!DOCTYPE html [<!ENTITY x SYSTEM 'https://example.invalid'>]><html><body>&x;</body></html>"), "DTD and external entities are prohibited");
    Check(snapshot.Review([]).Apply([]) == snapshot.Source, "A completed review with no suggested changes is valid");
});

Case("Whole-project context", () =>
{
    var fixture = Fixture("context", ("a.htm", Page("<p>Saved current wording.</p>")), ("folder/b.html", Page("<p>Nested paired data methods.</p><pre>t.test(before, after, paired = TRUE)</pre><MadCap:equation>$x+y$</MadCap:equation><math xmlns='http://www.w3.org/1998/Math/MathML'><mfrac><mi>numerator</mi><msqrt><mi>denominator</mi></msqrt></mfrac></math>", "Paired methods")),
        ("unlisted.htm", Page("<p>Unlisted help about confidence intervals.</p>", "Confidence guidance")));
    string unsaved = Page("<p>Unsaved current wording.</p><p>" + string.Join(" ", Enumerable.Repeat("Chunkable text.", 5000)) + " 🙂 end.</p>");
    var snapshot = HelpPolishSnapshot.Create("a.htm", unsaved); var context = HelpPolishContext.Create(fixture.Project, snapshot);
    Check(context.PageCount == 3 && JsonDocument.Parse(context.CatalogText).RootElement.GetProperty("pages").GetArrayLength() == 3, "Context catalog includes nested HTML pages and pages absent from Contents");
    var current = JsonDocument.Parse(context.ReadPage("a.htm"));
    Check(current.RootElement.GetProperty("text").GetString()!.Contains("Unsaved current wording.") && !current.RootElement.GetProperty("text").GetString()!.Contains("Saved current wording."), "Context uses the exact current unsaved draft instead of its older disk content");
    var nested = JsonDocument.Parse(context.ReadPage("folder/b.html")); string nestedText = nested.RootElement.GetProperty("text").GetString()!;
    Check(nestedText.Contains("t.test(before, after, paired = TRUE)") && nestedText.Contains("$x+y$"), "Read-only context includes R and mathematical material without making it editable");
    Check(nestedText.Contains("[read-only equation source]") && nestedText.Contains("<mfrac>") && nestedText.Contains("<msqrt>") && nestedText.Contains("<mi>numerator</mi>"), "Equation context retains MathML fraction/root structure instead of flattening its symbols");
    var search = JsonDocument.Parse(context.Search("confidence intervals"));
    Check(search.RootElement.GetProperty("results")[0].GetProperty("path").GetString() == "unlisted.htm", "Context search finds relevant unlisted pages");
    Check(JsonDocument.Parse(context.ReadPage("FOLDER/B.HTML")).RootElement.GetProperty("path").GetString() == "folder/b.html", "Read tools resolve catalog paths case-insensitively");
    Write(fixture.Directory, "Content/folder/b.html", Page("<p>External later edit.</p>"));
    Check(context.ReadPage("folder/b.html") == nested.RootElement.GetRawText(), "Context is frozen throughout the review even when a saved page changes later");
    int offset = 0, chunks = 0; var reconstructed = new StringBuilder();
    do
    {
        var read = JsonDocument.Parse(context.ReadPage("a.htm", offset, 1400)).RootElement;
        reconstructed.Append(read.GetProperty("text").GetString()); chunks++;
        var next = read.GetProperty("nextOffset"); if (next.ValueKind == JsonValueKind.Null) break; offset = next.GetInt32();
    } while (chunks < 1000);
    Check(chunks > 1 && reconstructed.ToString().EndsWith("🙂 end.") && reconstructed.ToString().Contains("Unsaved current wording."), "Paged read access can retrieve the complete long help page without truncation");
    Check(JsonDocument.Parse(context.ReadPage("a.htm", 0, 100_000)).RootElement.GetProperty("text").GetString()!.Length <= 24_000, "Context page tool output is bounded with explicit continuation offset");
    Reject(() => context.ReadPage("../outside.htm"), "Context tools cannot traverse outside catalog paths");
    Reject(() => context.ReadPage("missing.htm"), "Context tools cannot read arbitrary unlisted files");
    Reject(() => context.ReadPage("a.htm", -1), "Invalid page chunk offsets are rejected");
    Reject(() => context.ReadPage("a.htm", 0, 0), "Zero-length context read requests are rejected");
    Reject(() => context.Search("   "), "Blank context searches are rejected");
    Check(JsonDocument.Parse(context.Search("unmatchedword")).RootElement.GetProperty("returned").GetInt32() == 0, "An empty context search result is explicit");
    Reject(() => HelpPolishContext.Create(fixture.Project, HelpPolishSnapshot.Create("other.htm", Page("<p>Other wording.</p>"))), "A current draft outside the active project cannot become review context");
});

Case("Actual corpus read-only smoke", () =>
{
    string sourceDirectory = Path.Combine(root, "HelpSource");
    var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
    var before = files.ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
    var project = HelpProject.Open(sourceDirectory); var topics = project.ListTopics();
    int blocks = 0;
    foreach (var topic in topics)
    {
        string source = project.ReadTopic(topic.Path); var snapshot = HelpPolishSnapshot.Create(topic.Path, source);
        blocks += snapshot.Blocks.Count;
        if (snapshot.Review([]).Apply([]) != source) throw new InvalidDataException(topic.Path + " did not round-trip.");
    }
    Check(topics.Count >= 400 && blocks > 4000, "Every real help page parses into a source-preserving no-change review", new { pages = topics.Count, blocks });
    var current = HelpPolishSnapshot.Create(topics[0].Path, project.ReadTopic(topics[0].Path));
    var context = HelpPolishContext.Create(project, current);
    Check(context.PageCount == topics.Count && context.CatalogText.Contains("references"), "The complete actual help corpus is available to context tools");
    var results = JsonDocument.Parse(context.Search("paired t test"));
    Check(results.RootElement.GetProperty("returned").GetInt32() > 0, "Whole-corpus search retrieves real statistical context");
    Check(before.All(p => File.Exists(p.Key) && p.Value.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(p.Key)))) && Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories).Length == files.Length,
        "Core tests did not write, delete or add any maintained help source files");
});

string report = Path.Combine(evidence, "polish-checks.json");
File.WriteAllText(report, JsonSerializer.Serialize(new { checkedAtUtc = DateTimeOffset.UtcNow, passed = failed == 0, total = passed + failed, passedCount = passed, failedCount = failed, checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{passed} passed, {failed} failed. {report}");
return failed == 0 ? 0 : 1;
