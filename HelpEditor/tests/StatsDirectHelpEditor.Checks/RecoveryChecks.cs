using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using StatsDirect.HelpEditor.Core;

internal static class RecoveryChecks
{
    private static readonly List<object> Results = [];
    private static int passed, failed;
    private static string runRoot = "";

    internal static int Run(string root)
    {
        Results.Clear(); passed = failed = 0;
        runRoot = Path.Combine(root, "evidence", "RecoveryFixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runRoot);
        try
        {
            var project = Fixture("Exact");
            string page = Page(project, "parent"), toc = Toc(project);
            byte[] originalPage = File.ReadAllBytes(page), originalToc = File.ReadAllBytes(toc);
            Check(project.ListDeletedTopics().Count == 0 && !Directory.Exists(Path.Combine(project.RootPath, ".help-editor-recovery")), "Reading an empty recovery list creates no files");
            project.DeleteTopic("basics/parent.htm");
            var deleted = HelpProject.Open(project.RootPath).ListDeletedTopics().Single();
            Check(!File.Exists(page) && deleted.CanRestore && deleted.OriginalPath == "basics/parent.htm" && deleted.Title == "Parent α", "Deletion persists a discoverable recovery record across project reopen");
            Check(deleted.DeletedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-2) && Guid.TryParseExact(deleted.Id, "N", out _), "Recovery metadata identifies the deletion and its date");
            string archive = Archive(project, deleted.Id);
            Check(File.ReadAllBytes(Path.Combine(archive, "page.bin")).AsSpan().SequenceEqual(originalPage) && File.ReadAllBytes(Path.Combine(archive, "contents-before.xml")).AsSpan().SequenceEqual(originalToc), "The archive preserves exact Unicode/BOM/R/MathML bytes and original Contents bytes");
            var afterDelete = XDocument.Load(toc);
            Check(afterDelete.Descendants("TocEntry").Count(e => (string?)e.Attribute("Link") == "/Content/basics/child.htm") == 1 && !afterDelete.Descendants("TocEntry").Any(e => ((string?)e.Attribute("Link"))?.Contains("parent.htm") == true), "Deleting a parent removes duplicate anchored entries while promoting its child once");
            var restored = HelpProject.Open(project.RootPath).RestoreDeletedTopic(deleted.Id);
            Check(restored.OriginalContentsRestored && restored.Notice == null && File.ReadAllBytes(page).AsSpan().SequenceEqual(originalPage) && File.ReadAllBytes(toc).AsSpan().SequenceEqual(originalToc), "Unchanged navigation restores the exact page and complete original UTF-16 Contents hierarchy");
            Check(project.ListDeletedTopics().Count == 0 && Directory.Exists(archive), "Restored records leave the deleted list while retaining their archive");
            project.SaveContents(project.LoadContents());
            Check(XDocument.Load(toc).Declaration?.Encoding == "utf-8" && HelpProject.Open(project.RootPath).LoadContents().Count == 2,
                "An ordinary Contents edit after UTF-16 recovery writes a valid matching UTF-8 declaration");
            project.DeleteTopic("basics/parent.htm");
            var second = project.ListDeletedTopics().Single();
            Check(second.Id != deleted.Id && Directory.Exists(archive) && Directory.Exists(Archive(project, second.Id)), "Repeated deletion creates distinct recovery history without overwriting earlier records");
            project.RestoreDeletedTopic(second.Id);

            var changed = Fixture("ChangedContents");
            byte[] changedPage = File.ReadAllBytes(Page(changed, "parent"));
            changed.DeleteTopic("basics/parent.htm");
            var changedRecord = changed.ListDeletedTopics().Single();
            var current = XDocument.Load(Toc(changed), LoadOptions.PreserveWhitespace);
            current.Descendants("TocEntry").Single(e => (string?)e.Attribute("Link") == "/Content/basics/child.htm").SetAttributeValue("Title", "Child edited after deletion");
            current.Root!.Add(new XElement("TocEntry", new XAttribute("Title", "Later addition"), new XAttribute("Link", "/Content/basics/other.htm#later"), new XAttribute("Custom", "keep")));
            current.Save(Toc(changed));
            var merge = changed.RestoreDeletedTopic(changedRecord.Id);
            var merged = XDocument.Load(Toc(changed));
            Check(!merge.OriginalContentsRestored && merge.Notice?.Contains("changed", StringComparison.OrdinalIgnoreCase) == true && File.ReadAllBytes(Page(changed, "parent")).AsSpan().SequenceEqual(changedPage), "Changed Contents receive a clear safe-position notice while restoring exact page bytes");
            Check(merged.Descendants("TocEntry").Any(e => (string?)e.Attribute("Title") == "Later addition" && (string?)e.Attribute("Custom") == "keep") && merged.Descendants("TocEntry").Any(e => (string?)e.Attribute("Title") == "Child edited after deletion"), "Recovery preserves later navigation additions and title edits");
            var recoveredEntries = merged.Root!.Elements("TocEntry").Where(e => ((string?)e.Attribute("Link"))?.Contains("parent.htm") == true).ToList();
            Check(recoveredEntries.Count == 2 && recoveredEntries.All(e => !e.Elements("TocEntry").Any()) && recoveredEntries.Any(e => (string?)e.Attribute("Custom") == "parent-metadata") && merged.Descendants("TocEntry").Count(e => (string?)e.Attribute("Link") == "/Content/basics/child.htm") == 1, "Fallback restores every deleted entry's metadata without duplicating promoted children");

            var unlisted = Fixture("Unlisted");
            string orphan = unlisted.CreateTopic("Unlisted β", "basics");
            byte[] unlistedToc = File.ReadAllBytes(Toc(unlisted));
            unlisted.DeleteTopic(orphan);
            Check(File.ReadAllBytes(Toc(unlisted)).AsSpan().SequenceEqual(unlistedToc), "Deleting an unlisted page leaves Contents byte-for-byte unchanged");
            var unlistedRecord = unlisted.ListDeletedTopics().Single();
            var unlistedNav = unlisted.LoadContents(); unlistedNav.Add(new TocNode { Title = "New section after deletion" }); unlisted.SaveContents(unlistedNav);
            byte[] editedUnlistedToc = File.ReadAllBytes(Toc(unlisted));
            unlisted.RestoreDeletedTopic(unlistedRecord.Id);
            Check(File.Exists(Path.Combine(unlisted.RootPath, "Content", orphan)) && File.ReadAllBytes(Toc(unlisted)).AsSpan().SequenceEqual(editedUnlistedToc), "Restoring a previously unlisted page preserves later Contents without inventing an entry");

            var collision = Fixture("Collision");
            collision.DeleteTopic("basics/parent.htm");
            var collisionRecord = collision.ListDeletedTopics().Single();
            byte[] collisionToc = File.ReadAllBytes(Toc(collision));
            File.WriteAllText(Page(collision, "parent"), Html("A newer page", "Keep this new page"));
            byte[] newer = File.ReadAllBytes(Page(collision, "parent"));
            Reject(() => collision.RestoreDeletedTopic(collisionRecord.Id), "Recovery refuses to overwrite a newer page at the original path");
            Check(File.ReadAllBytes(Page(collision, "parent")).AsSpan().SequenceEqual(newer) && File.ReadAllBytes(Toc(collision)).AsSpan().SequenceEqual(collisionToc) && !collision.ListDeletedTopics().Single().CanRestore, "Collision failure preserves the current page, navigation and recoverable archive");
            File.Delete(Page(collision, "parent")); Directory.CreateDirectory(Page(collision, "parent"));
            Check(!collision.ListDeletedTopics().Single().CanRestore, "A directory occupying the original filename is also reported as a collision");
            Directory.Delete(Page(collision, "parent"));
            collision.RestoreDeletedTopic(collisionRecord.Id);
            Check(collision.ListDeletedTopics().Count == 0, "Removing a collision permits normal recovery later");

            var damaged = Fixture("Damaged");
            damaged.DeleteTopic("basics/parent.htm");
            var damagedRecord = damaged.ListDeletedTopics().Single();
            string damagedBlob = Path.Combine(Archive(damaged, damagedRecord.Id), "page.bin");
            File.AppendAllText(damagedBlob, "corruption");
            byte[] damagedToc = File.ReadAllBytes(Toc(damaged));
            Check(damaged.ListDeletedTopics().Single() is { CanRestore: false, OriginalPath: "basics/parent.htm" }, "A damaged archive stays visible with its page identity and a problem notice");
            Reject(() => damaged.RestoreDeletedTopic(damagedRecord.Id), "Hash verification refuses a corrupted recovery payload");
            Check(!File.Exists(Page(damaged, "parent")) && File.ReadAllBytes(Toc(damaged)).AsSpan().SequenceEqual(damagedToc), "Corrupt recovery cannot modify active source or navigation");
            File.Delete(damagedBlob);
            Reject(() => damaged.RestoreDeletedTopic(damagedRecord.Id), "A missing recovery payload is rejected without source writes");
            string good = damaged.CreateTopic("Another deleted page", "basics"); damaged.DeleteTopic(good);
            Check(damaged.ListDeletedTopics().Count == 2 && damaged.ListDeletedTopics().Count(r => r.CanRestore) == 1, "One damaged archive does not hide another recoverable page");
            Reject(() => damaged.RestoreDeletedTopic("../outside"), "Recovery rejects a traversal identifier");
            var goodRecord = damaged.ListDeletedTopics().Single(r => r.CanRestore);
            string goodManifest = Path.Combine(Archive(damaged, goodRecord.Id), "manifest.json");
            var metadata = JsonNode.Parse(File.ReadAllText(goodManifest))!; metadata["originalPath"] = "../outside.htm"; File.WriteAllText(goodManifest, metadata.ToJsonString());
            Reject(() => damaged.RestoreDeletedTopic(goodRecord.Id), "Recovery rejects an archive destination outside Content");
            Check(!File.Exists(Path.Combine(damaged.RootPath, "outside.htm")), "A forged archive destination creates no outside file");

            var linked = Fixture("LinkedArchive"); linked.DeleteTopic("basics/parent.htm");
            var linkedRecord = linked.ListDeletedTopics().Single();
            string linkedBlob = Path.Combine(Archive(linked, linkedRecord.Id), "page.bin"), outsideBlob = Path.Combine(runRoot, "outside-payload.bin");
            byte[] linkedBytes = File.ReadAllBytes(linkedBlob); File.WriteAllBytes(outsideBlob, linkedBytes); File.Delete(linkedBlob);
            bool linkedCreated = false;
            try
            {
                try { File.CreateSymbolicLink(linkedBlob, outsideBlob); linkedCreated = true; }
                catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException || ex is IOException io && (io.HResult & 0xffff) == 1314)
                { Results.Add(new { name = "Linked-archive platform check", skipped = true, detail = ex.Message }); }
                if (linkedCreated)
                {
                    Reject(() => linked.RestoreDeletedTopic(linkedRecord.Id), "A symlinked archive payload is refused even when its target has the correct hash");
                    Check(!File.Exists(Page(linked, "parent")) && !linked.ListDeletedTopics().Single().CanRestore,
                        "Linked recovery cannot write active source and stays visibly unavailable");
                }
            }
            finally { if (File.Exists(linkedBlob)) File.Delete(linkedBlob); File.WriteAllBytes(linkedBlob, linkedBytes); }

            var prepared = Fixture("PreparedCrash"); prepared.DeleteTopic("basics/parent.htm");
            var preparedRecord = prepared.ListDeletedTopics().Single();
            string preparedManifest = Path.Combine(Archive(prepared, preparedRecord.Id), "manifest.json");
            var pendingMetadata = JsonNode.Parse(File.ReadAllText(preparedManifest))!; pendingMetadata["state"] = "prepared"; File.WriteAllText(preparedManifest, pendingMetadata.ToJsonString());
            var reopened = HelpProject.Open(prepared.RootPath);
            Check(reopened.ListDeletedTopics().Single().CanRestore, "A crash before the final deleted-state update leaves the prepared archive recoverable");
            reopened.RestoreDeletedTopic(preparedRecord.Id);
            Check(File.Exists(Page(prepared, "parent")), "Prepared crash recovery restores the page normally");
            File.Delete(Path.Combine(Archive(prepared, preparedRecord.Id), "page.bin"));
            Check(reopened.ListDeletedTopics().Count == 0, "A missing payload in old restored history does not reappear as a deleted page");

            var readOnly = Fixture("ReadOnly");
            byte[] readOnlyPage = File.ReadAllBytes(Page(readOnly, "parent")), readOnlyToc = File.ReadAllBytes(Toc(readOnly));
            try { File.SetAttributes(Page(readOnly, "parent"), FileAttributes.ReadOnly); Reject(() => readOnly.DeleteTopic("basics/parent.htm"), "Read-only pages are not deleted or archived as successful deletions"); }
            finally { File.SetAttributes(Page(readOnly, "parent"), FileAttributes.Normal); }
            using (File.Open(Page(readOnly, "parent"), FileMode.Open, FileAccess.Read, FileShare.None))
                Reject(() => readOnly.DeleteTopic("basics/parent.htm"), "Locked pages are refused before navigation changes");
            try { File.SetAttributes(Toc(readOnly), FileAttributes.ReadOnly); Reject(() => readOnly.DeleteTopic("basics/parent.htm"), "Read-only Contents prevents page deletion"); }
            finally { File.SetAttributes(Toc(readOnly), FileAttributes.Normal); }
            Check(File.ReadAllBytes(Page(readOnly, "parent")).AsSpan().SequenceEqual(readOnlyPage) && File.ReadAllBytes(Toc(readOnly)).AsSpan().SequenceEqual(readOnlyToc) && readOnly.ListDeletedTopics().Count == 0, "Refused deletion preserves exact source/navigation and an empty deleted-pages list");
            readOnly.DeleteTopic("basics/parent.htm"); var lockedRecord = readOnly.ListDeletedTopics().Single();
            byte[] lockedToc = File.ReadAllBytes(Toc(readOnly));
            try { File.SetAttributes(Toc(readOnly), FileAttributes.ReadOnly); Reject(() => readOnly.RestoreDeletedTopic(lockedRecord.Id), "Read-only Contents blocks restoration before an active page is created"); }
            finally { File.SetAttributes(Toc(readOnly), FileAttributes.Normal); }
            using (File.Open(Toc(readOnly), FileMode.Open, FileAccess.Read, FileShare.None))
                Reject(() => readOnly.RestoreDeletedTopic(lockedRecord.Id), "Locked Contents blocks restoration without consuming the recovery record");
            Check(!File.Exists(Page(readOnly, "parent")) && File.ReadAllBytes(Toc(readOnly)).AsSpan().SequenceEqual(lockedToc) && readOnly.ListDeletedTopics().Single().CanRestore, "Failed restoration leaves the archive available and current navigation intact");
            readOnly.RestoreDeletedTopic(lockedRecord.Id);

            var blockedArchive = Fixture("ArchiveWriteFailure");
            byte[] blockedPage = File.ReadAllBytes(Page(blockedArchive, "parent")), blockedToc = File.ReadAllBytes(Toc(blockedArchive));
            File.WriteAllText(Path.Combine(blockedArchive.RootPath, ".help-editor-recovery"), "A file prevents archive directory creation.");
            Reject(() => blockedArchive.DeleteTopic("basics/parent.htm"), "Failure to create a durable archive aborts deletion");
            Check(File.ReadAllBytes(Page(blockedArchive, "parent")).AsSpan().SequenceEqual(blockedPage) && File.ReadAllBytes(Toc(blockedArchive)).AsSpan().SequenceEqual(blockedToc), "Archive write failure leaves the exact page and Contents untouched");

            var blank = Fixture("BlankTitle");
            File.WriteAllText(Page(blank, "parent"), Html("  ", "<h1>Visible heading β</h1>"));
            blank.DeleteTopic("basics/parent.htm");
            var blankRecord = blank.ListDeletedTopics().Single();
            Check(blankRecord.CanRestore && blankRecord.Title == "Visible heading β", "A blank title falls back to the visible heading for recovery metadata");
            blank.RestoreDeletedTopic(blankRecord.Id);

            var links = Fixture("Linked");
            File.WriteAllText(Page(links, "other"), Html("Other", "<a href=\"parent.htm\">Parent</a>"));
            Reject(() => links.DeleteTopic("basics/parent.htm"), "Incoming page links continue to prevent deletion");
            File.WriteAllText(Page(links, "other"), Html("Other", "Other"));
            Directory.CreateDirectory(Path.Combine(links.RootPath, "Project", "Advanced"));
            File.WriteAllText(Path.Combine(links.RootPath, "Project", "Advanced", "STATSDIRECT.flali"), "<CatapultAliasFile><Map Name=\"PARENT\" Link=\"/Content/basics/parent.htm\" /></CatapultAliasFile>");
            Reject(() => links.DeleteTopic("basics/parent.htm"), "Application context IDs continue to prevent deletion");
            Check(links.ListDeletedTopics().Count == 0, "Blocked references do not create deleted-page records");

            var lifecycle = Fixture("BuildLifecycle");
            string output = Path.Combine(runRoot, "Build", "Lifecycle"), runtime = Path.Combine(root, "src", "StatsDirectHelpEditor.App", "Web");
            lifecycle.DeleteTopic("basics/parent.htm");
            var lifecycleRecord = lifecycle.ListDeletedTopics().Single();
            var deletedBuild = HelpBuilder.Build(lifecycle, output, runtime);
            Check(deletedBuild.TopicCount == 2 && !File.Exists(Path.Combine(output, "basics", "parent.htm")) && !File.ReadAllText(Path.Combine(output, "search-index.js")).Contains("quartzrecoverytoken", StringComparison.OrdinalIgnoreCase), "Deleting and rebuilding removes the page and its unique search terms");
            Check(!Directory.GetFiles(output, "*", SearchOption.AllDirectories).Any(file => file.Contains(".help-editor-recovery", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file) is "page.bin" or "manifest.json"), "Recovery archives are excluded from output artwork and search publication");
            string archiveRoot = Path.Combine(lifecycle.RootPath, ".help-editor-recovery");
            var archiveDigests = Directory.GetFiles(archiveRoot, "*", SearchOption.AllDirectories).ToDictionary(file => file, file => SHA256.HashData(File.ReadAllBytes(file)));
            string forbiddenOutput = Path.Combine(Archive(lifecycle, lifecycleRecord.Id), "generated");
            Reject(() => HelpBuilder.Build(lifecycle, forbiddenOutput, runtime), "The builder refuses an empty output folder inside recovery storage");
            Check(!Directory.Exists(forbiddenOutput) && archiveDigests.All(item => File.Exists(item.Key) && SHA256.HashData(File.ReadAllBytes(item.Key)).AsSpan().SequenceEqual(item.Value)), "Rejected recovery-folder builds preserve every archived byte");
            lifecycle.RestoreDeletedTopic(lifecycleRecord.Id);
            var restoredBuild = HelpBuilder.Build(lifecycle, output, runtime);
            Check(restoredBuild.TopicCount == 3 && File.Exists(Path.Combine(output, "basics", "parent.htm")) && File.ReadAllText(Path.Combine(output, "search-index.js")).Contains("quartzrecoverytoken", StringComparison.OrdinalIgnoreCase), "Restoring and rebuilding returns the page, navigation and search content");
        }
        catch (Exception ex) { Check(false, "Unexpected recovery exception", ex.ToString()); }
        var report = new { passed, failed, fixture = runRoot, results = Results };
        File.WriteAllText(Path.Combine(root, "evidence", "recovery-core-checks.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{passed} recovery core checks passed; {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static HelpProject Fixture(string name)
    {
        string folder = Path.Combine(runRoot, name);
        Directory.CreateDirectory(Path.Combine(folder, "Content", "basics"));
        Directory.CreateDirectory(Path.Combine(folder, "Content", "resources", "stylesheets"));
        Directory.CreateDirectory(Path.Combine(folder, "Project", "TOCs"));
        File.WriteAllText(Path.Combine(folder, HelpProject.MarkerFile), "{\"purpose\":\"Owned recovery test fixture\"}");
        File.WriteAllText(Path.Combine(folder, "Content", "resources", "stylesheets", "statsdirect.css"), "body{font:10pt Arial}");
        string page = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>Parent α</title></head><body><h1>Parent</h1><p>Quartzrecoverytoken</p><math xmlns=\"http://www.w3.org/1998/Math/MathML\"><mfrac><mi>α</mi><mn>2</mn></mfrac></math><MadCap:dropDown class=\"rcode\"><MadCap:dropDownHead>R</MadCap:dropDownHead><MadCap:dropDownBody><pre><![CDATA[x <- c(1, 2)\r\n  # λ exact spacing]]></pre></MadCap:dropDownBody></MadCap:dropDown></body></html>\r\n";
        File.WriteAllBytes(Path.Combine(folder, "Content", "basics", "parent.htm"), Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(page)).ToArray());
        File.WriteAllText(Path.Combine(folder, "Content", "basics", "child.htm"), Html("Child", "<p id=\"section\">Child</p>"));
        File.WriteAllText(Path.Combine(folder, "Content", "basics", "other.htm"), Html("Other", "Other"));
        string toc = "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n<CatapultToc Version=\"1\" Custom=\"root-metadata\">\r\n<!-- preserved on exact restore -->\r\n<TocEntry Title=\"Section\" Custom=\"section-metadata\"><TocEntry Title=\"Parent entry\" Link=\"/Content/basics/parent.htm\" Custom=\"parent-metadata\"><TocEntry Title=\"Child entry\" Link=\"/Content/basics/child.htm\" /></TocEntry><TocEntry Title=\"Second parent entry\" Link=\"/Content/basics/parent.htm#anchor\" BreakType=\"chapter\" /></TocEntry><TocEntry Title=\"Other entry\" Link=\"/Content/basics/other.htm\" /></CatapultToc>\r\n";
        File.WriteAllBytes(Path.Combine(folder, "Project", "TOCs", "STATSDIRECT.fltoc"), Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(toc)).ToArray());
        return HelpProject.Open(folder);
    }
    private static string Page(HelpProject project, string name) => Path.Combine(project.RootPath, "Content", "basics", name + ".htm");
    private static string Toc(HelpProject project) => Path.Combine(project.RootPath, "Project", "TOCs", "STATSDIRECT.fltoc");
    private static string Archive(HelpProject project, string id) => Path.Combine(project.RootPath, ".help-editor-recovery", "deleted-pages", id);
    private static string Html(string title, string body) => "<html><head><title>" + title + "</title></head><body>" + body + "</body></html>";
    private static void Check(bool value, string name, object? detail = null)
    {
        if (value) passed++; else failed++;
        Results.Add(new { name, passed = value, detail });
        Console.WriteLine((value ? "PASS " : "FAIL ") + name);
    }
    private static void Reject(Action action, string name)
    {
        try { action(); Check(false, name); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Xml.XmlException or JsonException) { Check(true, name); }
    }
}
