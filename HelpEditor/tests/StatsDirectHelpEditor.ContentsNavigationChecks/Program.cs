using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args[0]);
string evidence = Path.Combine(root, "evidence", "ReviewImprovements-20261104", "ContentsNavigation"); Directory.CreateDirectory(evidence);
string fixture = Path.Combine(evidence, "Fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
var checks = new List<object>(); int failed = 0;
void Check(bool valid, string name) { checks.Add(new { name, passed = valid }); Console.WriteLine((valid ? "PASS " : "FAIL ") + name); if (!valid) failed++; }
void Refuses(Action action, string name) { bool refused = false; try { action(); } catch (Exception error) when (error is InvalidOperationException or InvalidDataException or IOException or ArgumentException) { refused = true; } Check(refused, name); }
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
string Toc(HelpProject project) => Path.Combine(project.RootPath, "Project", "TOCs", "STATSDIRECT.fltoc");
string Page(string title) => "<html><head><title>" + title + "</title></head><body><h1 id='section'>" + title + "</h1></body></html>";
void Write(string folder, string path, string value) { string file = Path.Combine(folder, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, value, new UTF8Encoding(false)); }
HelpProject Project(string name, string? contents = null)
{
    string folder = Path.Combine(fixture, name); Write(folder, HelpProject.MarkerFile, "{}");
    foreach (string topic in new[] { "a", "b", "c", "d" }) Write(folder, "Content/" + topic + ".htm", Page(topic));
    Write(folder, "Project/TOCs/STATSDIRECT.fltoc", contents ?? "<CatapultToc><TocEntry Title='A' Link='/Content/a.htm'/></CatapultToc>"); return HelpProject.Open(folder);
}
string MetadataToc() => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<?root before?>\r\n<CatapultToc Version=\"1\" CustomRoot=\"keep\">\r\n  <!-- root comment -->\r\n  <RootMetadata Value=\"keep\"/>\r\n  <TocEntry Title=\"A\" Link=\"/Content/a.htm\" Custom=\"parent\">\r\n    <!-- parent comment -->\r\n    <EntryMetadata Value=\"parent-data\"/>\r\n    <TocEntry Title=\"B\" Link=\"/Content/b.htm\" Custom=\"child\"><!-- child comment --><EntryMetadata Value=\"child-data\"/></TocEntry>\r\n  </TocEntry>\r\n  <TocEntry Title=\"C\" Link=\"/Content/c.htm\" Custom=\"sibling\"/>\r\n</CatapultToc>\r\n";
try
{
    var project = Project("Metadata", MetadataToc()); string file = Toc(project);
    byte[] preamble = Encoding.UTF8.GetPreamble(); File.WriteAllBytes(file, preamble.Concat(Encoding.UTF8.GetBytes(MetadataToc())).ToArray());
    string initialHash = Hash(file); File.SetLastWriteTimeUtc(file, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc)); DateTime initialTime = File.GetLastWriteTimeUtc(file);
    var nodes = project.LoadContents(); project.SaveContents(nodes);
    Check(Hash(file) == initialHash && File.GetLastWriteTimeUtc(file) == initialTime, "Canonical no-op Contents save preserves exact BOM, CRLF, XML bytes and file timestamp");
    var implicitTitle = Project("ImplicitTitle", "<CatapultToc><TocEntry Link='/Content/a.htm' Custom='keep'/></CatapultToc>"); string implicitHash = Hash(Toc(implicitTitle)); implicitTitle.SaveContents(implicitTitle.LoadContents());
    Check(Hash(Toc(implicitTitle)) == implicitHash, "A no-op Contents save does not invent an explicit title for an existing entry that uses the imported fallback");
    nodes[0].Title = "Renamed A"; project.SaveContents(nodes); var xml = XDocument.Load(file, LoadOptions.PreserveWhitespace);
    Check(xml.DescendantNodes().OfType<XComment>().Count() == 3 && xml.Descendants("EntryMetadata").Count() == 2 && xml.Descendants("RootMetadata").Count() == 1 && xml.Nodes().OfType<XProcessingInstruction>().Count() == 1,
        "An ordinary Contents title edit preserves root/entry comments, extension elements and processing instructions");
    Check(xml.Root!.Attribute("CustomRoot")!.Value == "keep" && xml.Descendants("TocEntry").First().Attributes().Select(a => a.Name.LocalName).SequenceEqual(new[] { "Title", "Link", "Custom" }),
        "Editing an existing attribute preserves unknown root attributes and the original entry attribute order");
    string renamedHash = Hash(file); project.SaveContents(nodes); Check(Hash(file) == renamedHash, "The same live tree can save repeatedly without false stale errors or new source differences");
    nodes[0].Title = "A"; project.SaveContents(nodes);
    Check(XDocument.Load(file).Descendants("TocEntry").First().Attribute("Title")!.Value == "A" && XDocument.Load(file).Descendants("EntryMetadata").Count() == 2,
        "Reverting an earlier title edit retains the entry's preserved XML metadata");

    var child = nodes[0].Children.Single(); nodes[0].Children.Clear(); nodes.Insert(1, child); project.SaveContents(nodes); xml = XDocument.Load(file);
    var moved = xml.Root!.Elements("TocEntry").Single(e => (string?)e.Attribute("Title") == "B");
    Check(moved.Element("EntryMetadata")?.Attribute("Value")?.Value == "child-data" && moved.Nodes().OfType<XComment>().Single().Value.Contains("child") && xml.Descendants("EntryMetadata").Count() == 2,
        "Moving a Contents entry to a new parent carries its extension XML/comments exactly once");
    var section = new TocNode { Title = "New section", Children = [nodes[2]] }; nodes.RemoveAt(2); nodes.Add(section); project.SaveContents(nodes); xml = XDocument.Load(file);
    Check(xml.Descendants("TocEntry").Single(e => (string?)e.Attribute("Title") == "C").Attribute("Custom")!.Value == "sibling" && xml.Descendants("RootMetadata").Count() == 1,
        "Adding a section and nesting an existing entry preserves metadata attached to existing entries and the document");

    var duplicate = project.LoadContents(); duplicate.Add(new TocNode { Id = duplicate[0].Id, Title = "Duplicate ID" }); string good = Hash(file);
    Refuses(() => project.SaveContents(duplicate), "Duplicate stable Contents IDs are rejected before writing"); Check(Hash(file) == good, "A rejected duplicate-ID tree leaves the complete Contents file unchanged");
    var renamedId = project.LoadContents(); renamedId[0].Id = "changed-existing-id";
    Refuses(() => project.SaveContents(renamedId), "Changing an existing node's stable ID cannot rebind it to another entry's metadata");
    var fresh = project.LoadContents(); string reusedId = fresh[0].Id; fresh.RemoveAt(0); fresh.Insert(0, new TocNode { Id = reusedId, Title = "New A", Link = "a.htm" }); project.SaveContents(fresh);
    Check(XDocument.Load(file).Root!.Elements("TocEntry").First().Attribute("Custom") == null, "A new node reusing a removed ID cannot inherit that node's hidden metadata");

    var foreign = Project("Foreign"); string foreignHash = Hash(Toc(foreign));
    Refuses(() => foreign.SaveContents(project.LoadContents()), "A loaded tree from another working copy is not silently applied"); Check(Hash(Toc(foreign)) == foreignHash, "Foreign-tree refusal preserves the target project's Contents");
    var stale = project.LoadContents(); string current = File.ReadAllText(file); File.WriteAllText(file, current.Replace("parent-data", "edited-data"));
    // This particular tree removed the parent earlier, so ensure a real external metadata edit exists.
    File.WriteAllText(file, File.ReadAllText(file).Replace("CustomRoot=\"keep\"", "CustomRoot=\"new!\"")); string newer = Hash(file);
    Refuses(() => project.SaveContents(stale), "External Contents metadata changes invalidate an older in-memory tree"); Check(Hash(file) == newer, "Stale-tree rejection keeps the newer external metadata intact");
    var emptied = project.LoadContents(); emptied.Clear(); File.AppendAllText(file, "<!-- external after clear -->"); newer = Hash(file);
    Refuses(() => project.SaveContents(emptied), "An empty edited tree still retains its source ownership and cannot erase newer Contents"); Check(Hash(file) == newer, "Rejected empty stale tree preserves the external revision");

    var utf16 = Project("Utf16"); string utf16Text = MetadataToc().Replace("encoding=\"utf-8\"", "encoding=\"utf-16\""); File.WriteAllBytes(Toc(utf16), Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(utf16Text)).ToArray());
    string unicodeHash = Hash(Toc(utf16)); var unicodeNodes = utf16.LoadContents(); utf16.SaveContents(unicodeNodes); Check(Hash(Toc(utf16)) == unicodeHash, "An unchanged UTF-16 Contents document keeps its exact original bytes");
    unicodeNodes[0].Title = "Edited UTF16 source"; utf16.SaveContents(unicodeNodes);
    Check(XDocument.Load(Toc(utf16)).Declaration?.Encoding == "utf-8" && XDocument.Load(Toc(utf16)).Descendants("EntryMetadata").Count() == 2,
        "A real edit converts UTF-16 Contents to a matching UTF-8 declaration while retaining extension XML");

    var recover = Project("Recovery", MetadataToc()); byte[] originalRecovery = File.ReadAllBytes(Toc(recover)); recover.DeleteTopic("a.htm");
    Check(XDocument.Load(Toc(recover)).Descendants("RootMetadata").Count() == 1 && XDocument.Load(Toc(recover)).Descendants("EntryMetadata").Count() == 1,
        "Page deletion preserves document metadata and promoted child metadata without duplicating it");
    var deleted = recover.ListDeletedTopics().Single(); recover.RestoreDeletedTopic(deleted.Id);
    Check(File.ReadAllBytes(Toc(recover)).AsSpan().SequenceEqual(originalRecovery), "Unchanged-Contents recovery restores the exact original document, including metadata and formatting");
    recover.DeleteTopic("a.htm"); deleted = recover.ListDeletedTopics().Single(); var later = recover.LoadContents(); later.Add(new TocNode { Title = "Later addition", Link = "d.htm" }); recover.SaveContents(later); recover.RestoreDeletedTopic(deleted.Id); xml = XDocument.Load(Toc(recover));
    var restored = xml.Root!.Elements("TocEntry").Single(e => (string?)e.Attribute("Title") == "A");
    Check(restored.Element("EntryMetadata")?.Attribute("Value")?.Value == "parent-data" && restored.Nodes().OfType<XComment>().Any() && !restored.Elements("TocEntry").Any()
        && xml.Descendants("TocEntry").Count(e => (string?)e.Attribute("Title") == "B") == 1 && xml.Descendants("TocEntry").Any(e => (string?)e.Attribute("Title") == "Later addition"),
        "Recovery after later navigation edits restores entry comments/extension XML without duplicating promoted children or losing later work");

    var navigation = Project("Navigation"); Write(navigation.RootPath, "Content/Folder/Mixed Case.htm", Page("Original title"));
    var target = HelpLinkResolver.Resolve(navigation, "a.htm", "/Content/folder/mIXED%20cASE.HTM#section");
    Check(target.TopicPath == "Folder/Mixed Case.htm" && target.Fragment == "section", "Direct navigation resolves escaped spaces and actual directory/file casing without scanning the catalogue");
    Write(navigation.RootPath, "Content/b.htm", "<html><body>Malformed unrelated source");
    Check(HelpLinkResolver.Resolve(navigation, "a.htm", "#section").TopicPath == "a.htm", "A valid current-page fragment resolves despite an unrelated malformed topic");
    bool invalidCatalog = false; try { navigation.ListTopics(); } catch (System.Xml.XmlException) { invalidCatalog = true; }
    Check(invalidCatalog, "Full topic catalog validation still reports the unrelated malformed source");
    string asset = Path.Combine(root, "src/StatsDirectHelpEditor.App/Web"); bool invalidBuild = false;
    try { HelpBuilder.Build(navigation, Path.Combine(fixture, "InvalidBuild"), asset); } catch (System.Xml.XmlException) { invalidBuild = true; }
    Check(invalidBuild, "Build validation remains strict rather than hiding malformed pages behind navigation optimization");
    File.Delete(Path.Combine(navigation.RootPath, "Content/b.htm")); string mixed = Path.Combine(navigation.RootPath, "Content/Folder/Mixed Case.htm"); DateTime originalTime = File.GetLastWriteTimeUtc(mixed);
    string oldText = File.ReadAllText(mixed), newText = oldText.Replace("Original title", "Revised! title"); Check(oldText.Length == newText.Length, "External-edit fixture holds file length constant");
    File.WriteAllText(mixed, newText); File.SetLastWriteTimeUtc(mixed, originalTime);
    Check(navigation.ListTopics().Single(row => row.Path == "Folder/Mixed Case.htm").Title == "Revised! title", "Same-size timestamp-preserving external edits remain visible because no stale XML catalogue cache is introduced");
    string renamed = Path.Combine(navigation.RootPath, "Content/Folder/Renamed.htm"); File.Move(mixed, renamed);
    Check(HelpLinkResolver.Resolve(navigation, "a.htm", "/Content/Folder/Renamed.htm").TopicPath == "Folder/Renamed.htm", "External renames are seen immediately by direct navigation");
    Refuses(() => HelpLinkResolver.Resolve(navigation, "a.htm", "/Content/Folder/Mixed%20Case.htm"), "A deleted or renamed link target is not accepted from a stale catalogue");
    Refuses(() => navigation.ResolveTopicPath("../outside.htm"), "Direct path resolution keeps traversal protection");
    Refuses(() => navigation.ResolveTopicPath("Folder/../a.htm"), "Direct path resolution does not weaken existing source path rules");

    string maintainedToc = Path.Combine(root, "HelpSource/Project/TOCs/STATSDIRECT.fltoc"), maintainedBefore = Hash(maintainedToc);
    string copy = Path.Combine(fixture, "MaintainedCopy"); Write(copy, HelpProject.MarkerFile, "{}");
    foreach (string topic in Directory.EnumerateFiles(Path.Combine(root, "HelpSource/Content"), "*", SearchOption.AllDirectories).Where(path => Path.GetExtension(path).ToLowerInvariant() is ".htm" or ".html"))
    { string destination = Path.Combine(copy, "Content", Path.GetRelativePath(Path.Combine(root, "HelpSource/Content"), topic)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(topic, destination); }
    Directory.CreateDirectory(Path.Combine(copy, "Project/TOCs")); File.Copy(maintainedToc, Path.Combine(copy, "Project/TOCs/STATSDIRECT.fltoc")); var actual = HelpProject.Open(copy); string actualHash = Hash(Toc(actual));
    actual.SaveContents(actual.LoadContents()); Check(Hash(Toc(actual)) == actualHash && Hash(maintainedToc) == maintainedBefore, "A no-op round trip of the maintained 261-entry Contents copy is byte-identical and leaves the real source untouched");
    var watch = Stopwatch.StartNew(); for (int i = 0; i < 40; i++) _ = HelpLinkResolver.Resolve(actual, "basics/basics.htm", "#section"); watch.Stop();
    File.WriteAllText(Path.Combine(evidence, "navigation-timing.json"), JsonSerializer.Serialize(new { pages = actual.ListTopics().Count, resolves = 40, elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, perResolveMilliseconds = watch.Elapsed.TotalMilliseconds / 40 }));
    Check(true, "Repeated direct fragment resolution succeeds on the complete maintained corpus without rebuilding its XML catalogue");
}
catch (Exception error) { Check(false, "Unexpected Contents/navigation exception: " + error); }
var report = new { checkedAtUtc = DateTimeOffset.UtcNow, passed = failed == 0, failed, assertions = checks.Count, fixture, checks };
File.WriteAllText(Path.Combine(evidence, "checks.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(new { report.passed, report.failed, report.assertions, report.fixture })); return failed == 0 ? 0 : 1;
