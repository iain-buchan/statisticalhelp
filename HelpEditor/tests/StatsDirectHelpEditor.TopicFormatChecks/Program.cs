using System.Security.Cryptography;
using System.Text;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args.Length == 0 ? "HelpEditor" : args[0]);
string fixtures = Path.Combine(root, "evidence", "TopicFormat-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtures);
int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
    passed++;
}
void Reject<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, message); return; }
    throw new Exception(message);
}
string Source(string ending) => string.Join(ending, new[]
{
    "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
    "<html><head><title>Format fixture</title></head><body>",
    "<p>Before café α and emoji 🧪.</p>",
    "<pre>first\u2028second</pre>",
    "</body></html>",
    ""
});
string Normalize(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n');
byte[] Encode(string source, bool bom) => (bom ? Encoding.UTF8.GetPreamble() : []).Concat(Encoding.UTF8.GetBytes(source)).ToArray();

try
{
    foreach (string ending in new[] { "\r\n", "\n", "\r" })
    foreach (bool bom in new[] { false, true })
    {
        string name = (ending == "\r\n" ? "CRLF" : ending == "\n" ? "LF" : "CR") + (bom ? " BOM" : " no BOM");
        string folder = Path.Combine(fixtures, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "Content"));
        Directory.CreateDirectory(Path.Combine(folder, "Project", "TOCs"));
        File.WriteAllText(Path.Combine(folder, HelpProject.MarkerFile), "{}");
        File.WriteAllText(Path.Combine(folder, "Project", "TOCs", "STATSDIRECT.fltoc"), "<CatapultToc/>");
        string path = Path.Combine(folder, "Content", "fixture.htm");
        string original = Source(ending);
        byte[] originalBytes = Encode(original, bom);
        File.WriteAllBytes(path, originalBytes);
        var project = HelpProject.Open(folder);
        var before = project.ReadTopicSnapshot("fixture.htm");
        Check(before.Source == original && before.Revision == Convert.ToHexString(SHA256.HashData(originalBytes)), name + " read captures source and exact byte revision");
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        var unchanged = project.SaveTopicPreservingFormat("fixture.htm", Normalize(before.Source), before.Revision);
        Check(File.ReadAllBytes(path).SequenceEqual(originalBytes) && File.GetLastWriteTimeUtc(path) == timestamp && unchanged == before,
            name + " normalized no-op retains bytes, timestamp and revision");
        string edited = Normalize(before.Source).Replace("Before café", "After café");
        var saved = project.SaveTopicPreservingFormat("fixture.htm", edited, before.Revision);
        byte[] expected = Encode(original.Replace("Before café", "After café"), bom);
        Check(File.ReadAllBytes(path).SequenceEqual(expected) && saved.Source == original.Replace("Before café", "After café"), name + " edit preserves every other byte and Unicode separator");
        var restored = project.SaveTopicPreservingFormat("fixture.htm", Normalize(original), saved.Revision);
        Check(File.ReadAllBytes(path).SequenceEqual(originalBytes) && restored == before, name + " revert restores original bytes");
        File.WriteAllBytes(path, Encode(original, !bom));
        Reject<HelpTopicConflictException>(() => project.SaveTopicPreservingFormat("fixture.htm", edited, restored.Revision), name + " BOM-only external edit is a revision conflict");
        var current = project.ReadTopicSnapshot("fixture.htm");
        byte[] safe = File.ReadAllBytes(path);
        Reject<InvalidDataException>(() => project.SaveTopicPreservingFormat("fixture.htm", original.Replace("</body>", "<script>alert(1)</script></body>"), current.Revision), name + " executable source is refused");
        Check(File.ReadAllBytes(path).SequenceEqual(safe), name + " rejection leaves source bytes intact");
    }

    string mixedFolder = Directory.EnumerateDirectories(fixtures).First();
    string mixedPath = Path.Combine(mixedFolder, "Content", "fixture.htm");
    string mixed = "<html>\r\n<head><title>Mixed</title></head>\n<body>\r<p>Before</p>\r\n<p>Stable</p>\n</body>\r</html>\r\n";
    File.WriteAllBytes(mixedPath, Encode(mixed, true));
    var mixedProject = HelpProject.Open(mixedFolder);
    var mixedBefore = mixedProject.ReadTopicSnapshot("fixture.htm");
    mixedProject.SaveTopicPreservingFormat("fixture.htm", Normalize(mixed).Replace("Before", "After"), mixedBefore.Revision);
    Check(File.ReadAllBytes(mixedPath).SequenceEqual(Encode(mixed.Replace("Before", "After"), true)), "Mixed endings remain attached to unchanged source around a text edit");
    var mixedEdited = mixedProject.ReadTopicSnapshot("fixture.htm");
    string inserted = Normalize(mixedEdited.Source).Replace("<body>\n", "<body>\n<p>Added</p>\n");
    var mixedSaved = mixedProject.SaveTopicPreservingFormat("fixture.htm", inserted, mixedEdited.Revision);
    Check(mixedSaved.Source.EndsWith("<p>After</p>\r\n<p>Stable</p>\n</body>\r</html>\r\n", StringComparison.Ordinal),
        "Mixed-ending insertion preserves the unchanged suffix separators");
    Check(File.ReadAllBytes(mixedPath).AsSpan().StartsWith(Encoding.UTF8.GetPreamble()), "Mixed-ending insertion retains the BOM");

    string multiEdit = "<html>\r\n<head><title>X</title></head>\n<body>\r<p>old</p>\r\n<p>keep</p>\r<p>end</p>\n</body>\r\n</html>";
    File.WriteAllBytes(mixedPath, Encode(multiEdit, true));
    var multiBefore = mixedProject.ReadTopicSnapshot("fixture.htm");
    string multiDraft = Normalize(multiEdit).Replace("old", "new").Replace("<p>end</p>", "<p>added</p>\n<p>end</p>");
    var multiSaved = mixedProject.SaveTopicPreservingFormat("fixture.htm", multiDraft, multiBefore.Revision);
    string multiExpected = multiEdit.Replace("old", "new").Replace("<p>end</p>", "<p>added</p>\r\n<p>end</p>");
    Check(File.ReadAllBytes(mixedPath).SequenceEqual(Encode(multiExpected, true)),
        "A mixed text edit plus insertion preserves the untouched middle line's CR separator");
    Check(multiSaved.Source == multiExpected, "The mixed multi-edit response contains exactly the saved separator bytes");
    string deleted = Normalize(multiSaved.Source).Replace("<p>new</p>\n", "").Replace("<title>X</title>", "<title>Changed</title>");
    var deletion = mixedProject.SaveTopicPreservingFormat("fixture.htm", deleted, multiSaved.Revision);
    Check(deletion.Source.Contains("<p>keep</p>\r<p>added</p>\r\n<p>end</p>\n", StringComparison.Ordinal),
        "A mixed deletion and second edit preserve unchanged middle line separators");

    string repeated = "<html>\r\n<body>\n<p>x</p>\r<p>x</p>\r\n</body>\n</html>";
    File.WriteAllBytes(mixedPath, Encode(repeated, true));
    var repeatedBefore = mixedProject.ReadTopicSnapshot("fixture.htm");
    string repeatedDraft = Normalize(repeated).Replace("<p>x</p>\n</body>", "<p>new</p>\n<p>x</p>\n</body>");
    var repeatedSaved = mixedProject.SaveTopicPreservingFormat("fixture.htm", repeatedDraft, repeatedBefore.Revision);
    string repeatedExpected = repeated.Replace("<p>x</p>\r\n</body>", "<p>new</p>\r\n<p>x</p>\r\n</body>");
    Check(File.ReadAllBytes(mixedPath).SequenceEqual(Encode(repeatedExpected, true)),
        "An insertion between repeated lines retains their distinct CR and CRLF separators");
    var repeatedRestored = mixedProject.SaveTopicPreservingFormat("fixture.htm", Normalize(repeated), repeatedSaved.Revision);
    Check(File.ReadAllBytes(mixedPath).SequenceEqual(Encode(repeated, true)) && repeatedRestored == repeatedBefore,
        "Reverting an insertion between repeated lines restores the exact original bytes");
    Console.WriteLine($"{passed} topic-format checks passed");
    return 0;
}
finally
{
    Directory.Delete(fixtures, true);
}
