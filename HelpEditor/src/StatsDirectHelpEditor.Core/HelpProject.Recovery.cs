using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace StatsDirect.HelpEditor.Core;

public record DeletedTopicInfo(string Id, string OriginalPath, string Title, DateTimeOffset DeletedAtUtc, string? Problem = null)
{
    public bool CanRestore => Problem == null;
}

public record TopicRestoreResult(string OriginalPath, bool OriginalContentsRestored, string? Notice);

public sealed partial class HelpProject
{
    private const string RecoveryRelativePath = ".help-editor-recovery/deleted-pages";
    private static readonly JsonSerializerOptions RecoveryJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private const int RecoveryFileLimit = 25_000_000;

    /// <summary>List locally archived deletions, including records that need attention before restoration.</summary>
    public IReadOnlyList<DeletedTopicInfo> ListDeletedTopics()
    {
        string root = SafePaths.Resolve(RootPath, RecoveryRelativePath);
        if (!Directory.Exists(root)) return [];
        var result = new List<DeletedTopicInfo>();
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            string id = Path.GetFileName(directory);
            if (!Guid.TryParseExact(id, "N", out _)) continue; // incomplete staging folders are not deletions
            RecoveryRecord? record = null;
            try
            {
                record = ReadRecoveryRecord(id);
                if (record.State is "restored" or "cancelled") continue;
                _ = ReadRecovery(id);
                string? problem = File.Exists(TopicPath(record.OriginalPath)) || Directory.Exists(TopicPath(record.OriginalPath))
                    ? "A page already exists at the original path. It will not be overwritten."
                    : null;
                result.Add(new(id, record.OriginalPath, record.Title, record.DeletedAtUtc, problem));
            }
            catch (Exception ex) when (RecoveryError(ex))
            {
                // A damaged record must not hide other recoverable pages.
                result.Add(new(id, record?.OriginalPath ?? "(unreadable recovery record)", record?.Title ?? "Deleted page needs attention",
                    record?.DeletedAtUtc ?? new DateTimeOffset(Directory.GetLastWriteTimeUtc(directory), TimeSpan.Zero), ex.Message));
            }
        }
        return result.OrderByDescending(r => r.DeletedAtUtc).ToList();
    }

    private void DeleteTopicWithRecovery(string relativePath)
    {
        string path = TopicPath(relativePath);
        if (!File.Exists(path)) throw new FileNotFoundException("The topic no longer exists.", path);
        var references = FindReferences(relativePath).Where(r => !r.StartsWith("Contents: ", StringComparison.Ordinal)).ToList();
        if (references.Count != 0)
            throw new InvalidOperationException("Remove these links before deleting the page:" + Environment.NewLine + string.Join(Environment.NewLine, references.Take(20)));
        RequireWritable(path, "The page is read-only. Its contents entry has not been changed.");
        RequireWritable(TocPath, "Contents are read-only. The page has not been deleted.");
        byte[] page;
        using (var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) page = ReadRecoveryBytes(held);
        using (File.Open(TocPath, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        byte[] tocBefore = File.ReadAllBytes(TocPath);
        var contents = LoadContents();
        int removed = 0;
        void Remove(List<TocNode> nodes)
        {
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                Remove(nodes[i].Children);
                if (nodes[i].Link is not { } link || !RefersTo("", "/Content/" + link, path)) continue;
                nodes.InsertRange(i + 1, nodes[i].Children);
                nodes.RemoveAt(i);
                removed++;
            }
        }
        Remove(contents);
        byte[] tocAfter = removed == 0 ? tocBefore : SerializeRecoveryXml(Parse(CreateContentsSource(contents)));
        if (!File.ReadAllBytes(TocPath).AsSpan().SequenceEqual(tocBefore))
            throw new InvalidOperationException("Contents changed while the page was being prepared for deletion. Nothing has been deleted.");
        string source = DecodeRecoveryXml(page);
        var document = Parse(source);
        string? title = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "title")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(title)) title = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "h1")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(title)) title = Path.GetFileNameWithoutExtension(path);
        var record = new RecoveryRecord
        {
            Id = Guid.NewGuid().ToString("N"), OriginalPath = Path.GetRelativePath(ContentPath, path).Replace('\\', '/'), Title = title,
            DeletedAtUtc = DateTimeOffset.UtcNow, OriginalLastWriteTimeUtc = File.GetLastWriteTimeUtc(path),
            PageSha256 = RecoveryDigest(page), TocBeforeSha256 = RecoveryDigest(tocBefore), TocAfterSha256 = RecoveryDigest(tocAfter)
        };
        // Complete and verify the durable archive before changing either source file.
        WriteRecovery(record, page, tocBefore, tocAfter);
        bool tocChanged = false;
        try
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(page) || !File.ReadAllBytes(TocPath).AsSpan().SequenceEqual(tocBefore))
                throw new InvalidOperationException("The page or Contents changed before deletion. Nothing has been deleted.");
            if (!tocBefore.AsSpan().SequenceEqual(tocAfter))
            {
                WriteRecoveryBytes(TocPath, tocAfter, overwrite: true);
                tocChanged = true;
            }
            // Recheck the bytes under an exclusive handle after the TOC write,
            // immediately before deletion. A save during that write must not
            // replace the newer page with an older recovery snapshot.
            using (var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                if (!ReadRecoveryBytes(held).AsSpan().SequenceEqual(page))
                    throw new InvalidOperationException("The page changed during deletion. Its newer contents have been preserved.");
            File.Delete(path);
        }
        catch (Exception deletionError) when (RecoveryError(deletionError))
        {
            if (tocChanged)
            {
                try
                {
                    if (!File.ReadAllBytes(TocPath).AsSpan().SequenceEqual(tocAfter))
                        throw new IOException("Contents changed after the deletion failed; newer navigation has been preserved.");
                    WriteRecoveryBytes(TocPath, tocBefore, overwrite: true);
                }
                catch (Exception restoreError) when (RecoveryError(restoreError))
                {
                    throw new IOException("The page could not be deleted and its Contents could not be rolled back. The recovery copy is retained; review Contents before continuing.", new AggregateException(deletionError, restoreError));
                }
            }
            TrySetRecoveryState(record, "cancelled");
            throw;
        }
        // A crash or write error here is safe: the prepared archive is already
        // restorable and listing never depends solely on this final status.
        TrySetRecoveryState(record, "deleted");
    }

    /// <summary>Restore archived bytes without overwriting an active page or newer navigation.</summary>
    public TopicRestoreResult RestoreDeletedTopic(string id)
    {
        var archive = ReadRecovery(id);
        var record = archive.Record;
        if (record.State is "restored" or "cancelled") throw new InvalidOperationException("This recovery record is no longer a deleted page.");
        string pagePath = TopicPath(record.OriginalPath);
        if (File.Exists(pagePath) || Directory.Exists(pagePath))
            throw new InvalidOperationException("A page already exists at the original path. It has not been overwritten; the recovery copy is retained.");
        RequireWritable(TocPath, "Contents are read-only. The page has not been restored.");
        using (File.Open(TocPath, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        byte[] tocCurrent = File.ReadAllBytes(TocPath);
        bool exactContents = tocCurrent.AsSpan().SequenceEqual(archive.TocAfter);
        var deletedEntries = ParseRecoveryXml(archive.TocBefore).Descendants("TocEntry")
            .Where(e => e.Attribute("Link") is { } link && RefersTo("", link.Value, pagePath)).ToList();
        byte[] tocRestored;
        if (exactContents) tocRestored = archive.TocBefore;
        else if (deletedEntries.Count == 0) tocRestored = tocCurrent; // originally unlisted pages stay unlisted
        else
        {
            var current = ParseRecoveryXml(tocCurrent);
            if (current.Root == null) throw new InvalidDataException("Contents have no root element.");
            foreach (var entry in deletedEntries)
            {
                // Children were promoted by deletion. Keep their current positions
                // and subsequent edits; restore only the removed entry itself.
                bool alreadyPresent = current.Descendants("TocEntry").Any(e =>
                    (string?)e.Attribute("Title") == (string?)entry.Attribute("Title") &&
                    (string?)e.Attribute("Link") == (string?)entry.Attribute("Link"));
                if (!alreadyPresent)
                {
                    // Child entries were promoted at deletion. Retain this entry's
                    // comments/extension XML without duplicating those children.
                    var restoredEntry = new XElement(entry);
                    restoredEntry.Elements("TocEntry").Remove();
                    current.Root.Add(restoredEntry);
                }
            }
            tocRestored = SerializeRecoveryXml(current);
        }
        bool pageCreated = false;
        try
        {
            if (!File.ReadAllBytes(TocPath).AsSpan().SequenceEqual(tocCurrent))
                throw new InvalidOperationException("Contents changed while restoration was being prepared. Nothing has been restored.");
            WriteRecoveryBytes(pagePath, archive.Page, overwrite: false);
            pageCreated = true;
            File.SetLastWriteTimeUtc(pagePath, record.OriginalLastWriteTimeUtc);
            if (!tocRestored.AsSpan().SequenceEqual(tocCurrent))
            {
                if (!File.ReadAllBytes(TocPath).AsSpan().SequenceEqual(tocCurrent))
                    throw new InvalidOperationException("Contents changed during restoration; newer navigation has been preserved.");
                WriteRecoveryBytes(TocPath, tocRestored, overwrite: true);
            }
        }
        catch (Exception restoreError) when (RecoveryError(restoreError))
        {
            if (pageCreated)
            {
                try
                {
                    // Deny writes while checking/removing only the file this
                    // transaction created. Never remove a newer edited version.
                    using var held = File.Open(pagePath, FileMode.Open, FileAccess.Read, FileShare.Delete);
                    if (!ReadRecoveryBytes(held).AsSpan().SequenceEqual(archive.Page))
                        throw new IOException("The restored page was edited; its newer contents have been preserved.");
                    File.Delete(pagePath);
                }
                catch (Exception rollbackError) when (RecoveryError(rollbackError))
                {
                    throw new IOException("The page is present but Contents could not be restored. The page and recovery copy have been preserved; review them before continuing.", new AggregateException(restoreError, rollbackError));
                }
            }
            throw;
        }
        string? notice = exactContents || deletedEntries.Count == 0 ? null :
            "The page was restored. Contents changed since deletion, so its original entries were added at the top level without changing newer navigation. You can move them using Contents.";
        if (!TrySetRecoveryState(record, "restored"))
            notice = (notice == null ? "The page was restored." : notice) + " The recovery history could not be marked as restored; the archive is retained.";
        return new(record.OriginalPath, exactContents || deletedEntries.Count == 0, notice);
    }

    private string RecoveryFolder(string id)
    {
        if (!Guid.TryParseExact(id, "N", out var parsed) || id != parsed.ToString("N"))
            throw new ArgumentException("Choose a valid deleted-page record.", nameof(id));
        return SafePaths.Resolve(RootPath, RecoveryRelativePath + "/" + id);
    }

    private RecoveryArchive ReadRecovery(string id)
    {
        string directory = RecoveryFolder(id);
        var record = ReadRecoveryRecord(id);
        byte[] page = ReadRecoveryFile(SafePaths.Resolve(directory, "page.bin"));
        byte[] before = ReadRecoveryFile(SafePaths.Resolve(directory, "contents-before.xml"));
        byte[] after = ReadRecoveryFile(SafePaths.Resolve(directory, "contents-after.xml"));
        if (RecoveryDigest(page) != record.PageSha256 || RecoveryDigest(before) != record.TocBeforeSha256 || RecoveryDigest(after) != record.TocAfterSha256)
            throw new InvalidDataException("A recovery file is missing or damaged. The active help has not been changed.");
        var topic = Parse(DecodeRecoveryXml(page));
        if (topic.Root?.Name.LocalName != "html" || !topic.Descendants().Any(e => e.Name.LocalName == "body"))
            throw new InvalidDataException("The archived page is not a valid HTML topic.");
        if (ParseRecoveryXml(before).Root?.Name.LocalName != "CatapultToc" || ParseRecoveryXml(after).Root?.Name.LocalName != "CatapultToc")
            throw new InvalidDataException("The archived Contents are invalid.");
        return new(record, page, before, after);
    }

    private RecoveryRecord ReadRecoveryRecord(string id)
    {
        string directory = RecoveryFolder(id);
        string manifest = SafePaths.Resolve(directory, "manifest.json");
        var record = JsonSerializer.Deserialize<RecoveryRecord>(ReadRecoveryFile(manifest, 1_000_000), RecoveryJson)
            ?? throw new InvalidDataException("The deleted-page recovery record is empty.");
        if (record.SchemaVersion != 1 || record.Id != id || record.State is not ("prepared" or "deleted" or "restored" or "cancelled") || string.IsNullOrWhiteSpace(record.OriginalPath) || string.IsNullOrWhiteSpace(record.Title))
            throw new InvalidDataException("The deleted-page recovery record is invalid or unsupported.");
        string original = TopicPath(record.OriginalPath);
        if (record.OriginalPath != Path.GetRelativePath(ContentPath, original).Replace('\\', '/'))
            throw new InvalidDataException("The recovery record contains a non-canonical page path.");
        return record;
    }

    private void WriteRecovery(RecoveryRecord record, byte[] page, byte[] before, byte[] after)
    {
        string root = SafePaths.Resolve(RootPath, RecoveryRelativePath);
        Directory.CreateDirectory(root);
        string staging = SafePaths.Resolve(root, ".pending-" + record.Id);
        Directory.CreateDirectory(staging);
        try
        {
            WriteRecoveryBytes(SafePaths.Resolve(staging, "page.bin"), page, overwrite: false);
            WriteRecoveryBytes(SafePaths.Resolve(staging, "contents-before.xml"), before, overwrite: false);
            WriteRecoveryBytes(SafePaths.Resolve(staging, "contents-after.xml"), after, overwrite: false);
            WriteRecoveryBytes(SafePaths.Resolve(staging, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(record, RecoveryJson), overwrite: false);
            Directory.Move(staging, RecoveryFolder(record.Id));
            _ = ReadRecovery(record.Id); // verified before active source is touched
        }
        catch
        {
            // Only known staging files are removed; completed archives are never purged.
            foreach (string name in new[] { "page.bin", "contents-before.xml", "contents-after.xml", "manifest.json" })
            {
                try { string file = SafePaths.Resolve(staging, name); if (File.Exists(file)) File.Delete(file); }
                catch (Exception ex) when (RecoveryError(ex)) { }
            }
            try { if (Directory.Exists(staging)) Directory.Delete(staging); }
            catch (Exception ex) when (RecoveryError(ex)) { }
            throw;
        }
    }

    private bool TrySetRecoveryState(RecoveryRecord record, string state)
    {
        try
        {
            record.State = state;
            WriteRecoveryBytes(SafePaths.Resolve(RecoveryFolder(record.Id), "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(record, RecoveryJson), overwrite: true);
            return true;
        }
        catch (Exception ex) when (RecoveryError(ex)) { return false; }
    }

    private static bool RecoveryError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or XmlException or JsonException;
    private static string RecoveryDigest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void RequireWritable(string file, string message)
    {
        if ((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0) throw new UnauthorizedAccessException(message);
    }
    private static byte[] ReadRecoveryFile(string path, int limit = RecoveryFileLimit)
    {
        SafePaths.CheckNoLinks(path);
        using var stream = File.OpenRead(path);
        if (stream.Length > limit) throw new InvalidDataException("A recovery file is too large.");
        return ReadRecoveryBytes(stream);
    }
    private static byte[] ReadRecoveryBytes(Stream stream)
    {
        if (stream.Length > RecoveryFileLimit) throw new InvalidDataException("The page or Contents is too large to archive safely.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }
    private static string DecodeRecoveryXml(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
    private static XDocument ParseRecoveryXml(byte[] bytes) => Parse(DecodeRecoveryXml(bytes));
    private static byte[] SerializeRecoveryXml(XDocument document)
    {
        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, OmitXmlDeclaration = document.Declaration == null })) document.Save(writer);
        return buffer.ToArray();
    }
    private static void WriteRecoveryBytes(string path, byte[] bytes, bool overwrite)
    {
        SafePaths.CheckNoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".helpeditor-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed class RecoveryRecord
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = "";
        public string OriginalPath { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTimeOffset DeletedAtUtc { get; set; }
        public DateTime OriginalLastWriteTimeUtc { get; set; }
        public string PageSha256 { get; set; } = "";
        public string TocBeforeSha256 { get; set; } = "";
        public string TocAfterSha256 { get; set; } = "";
        public string State { get; set; } = "prepared";
    }
    private sealed record RecoveryArchive(RecoveryRecord Record, byte[] Page, byte[] TocBefore, byte[] TocAfter);
}
