using System.Globalization;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace StatsDirect.HelpEditor.Core;

public record TopicInfo(string Path, string Title);
public record SharedFooterInfo(string SourcePath, string Html);

public sealed class TocNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string? Link { get; set; }
    public List<TocNode> Children { get; set; } = [];
    internal Dictionary<XName, string> OriginalAttributes { get; set; } = [];
    internal TocSourceSnapshot? SourceSnapshot { get; set; }
}

internal sealed record TocSourceSnapshot(string ProjectRoot, string SourceStamp, string NodeId, XElement Element);

/// <summary>Edits only explicitly marked working copies, never the original Flare project.</summary>
public sealed partial class HelpProject
{
    private sealed record ContentsDocumentSnapshot(string ProjectRoot, string SourceStamp);
    private static readonly ConditionalWeakTable<IReadOnlyList<TocNode>, ContentsDocumentSnapshot> ContentsSources = new();
    public const string MarkerFile = ".statsdirect-help-editor.json";
    public string RootPath { get; }
    internal string ContentPath { get; }
    internal string TocPath => SafePaths.Resolve(RootPath, "Project/TOCs/STATSDIRECT.fltoc");

    private HelpProject(string root)
    {
        RootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        SafePaths.CheckNoLinks(RootPath);
        WorkspaceProtectionSettings.Load().RequireUnprotected(RootPath, "Open help source");
        if (!File.Exists(SafePaths.Resolve(RootPath, MarkerFile)))
            throw new InvalidOperationException("This folder is not a marked StatsDirectHelpEditor working copy.");
        ContentPath = SafePaths.Resolve(RootPath, "Content");
        if (!Directory.Exists(ContentPath) || !File.Exists(TocPath))
            throw new InvalidDataException("The working copy must contain Content and Project/TOCs/STATSDIRECT.fltoc.");
    }

    public static HelpProject Open(string root) => new(root);

    /// <summary>Read the design baked into the selected target's generated help.</summary>
    public string GetAppearance(HelpBuildTarget target) => HelpDesignSettings.GetAppearance(RootPath, target);

    /// <summary>Save an authoring design choice without changing topic or Flare source files.</summary>
    public void SetAppearance(HelpBuildTarget target, string key) => HelpDesignSettings.SetAppearance(RootPath, target, key);

    /// <summary>Read the selected target's shared footer without changing its master page.</summary>
    public SharedFooterInfo? ReadSharedFooter(HelpBuildTarget target)
    {
        string targetPath = SafePaths.Resolve(RootPath, "Project/Targets/" + (target == HelpBuildTarget.Desktop ? "DesktopHelp.fltar" : "Website.fltar"));
        if (!File.Exists(targetPath)) return null;
        string? master = (string?)Parse(File.ReadAllText(targetPath)).Root?.Attribute("MasterPage");
        if (string.IsNullOrWhiteSpace(master)) return null;
        master = master.Replace('\\', '/').TrimStart('/');
        if (!master.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The shared footer master page must be inside the working copy's Content folder.");
        string sourcePath = master[8..];
        string fullPath = SafePaths.Resolve(ContentPath, sourcePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The target's shared footer master page is missing.", fullPath);
        var footer = Parse(File.ReadAllText(fullPath)).Descendants().FirstOrDefault(element => HelpSourceNamespaces.IsElement(element, "pageFooter"));
        return footer == null ? null : new SharedFooterInfo(sourcePath, string.Concat(footer.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting))));
    }

    public IReadOnlyList<TopicInfo> ListTopics() => Directory.EnumerateFiles(ContentPath, "*", SearchOption.AllDirectories)
        .Where(p => Path.GetExtension(p).Equals(".htm", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(p).Equals(".html", StringComparison.OrdinalIgnoreCase))
        .Select(p =>
        {
            SafePaths.CheckNoLinks(p);
            var rel = Path.GetRelativePath(ContentPath, p).Replace('\\', '/');
            var doc = Parse(File.ReadAllText(p));
            var title = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "title")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(title)) title = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "h1")?.Value.Trim();
            return new TopicInfo(rel, string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(p) : Regex.Replace(title, @"\s+", " "));
        }).OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToList();

    public string ReadTopic(string relativePath) => File.ReadAllText(TopicPath(relativePath));

    /// <summary>Resolve an existing topic's stored filename case without parsing the other help pages.</summary>
    public string ResolveTopicPath(string relativePath)
    {
        _ = TopicPath(relativePath); // Apply the same containment, extension and link checks as reads.
        string current = ContentPath;
        var parts = relativePath.Replace('\\', '/').Split('/');
        for (int index = 0; index < parts.Length; index++)
        {
            SafePaths.CheckNoLinks(current);
            if (!Directory.Exists(current)) throw new DirectoryNotFoundException("The help topic's folder no longer exists.");
            var matches = Directory.EnumerateFileSystemEntries(current)
                .Where(path => Path.GetFileName(path).Equals(parts[index], StringComparison.OrdinalIgnoreCase)).ToArray();
            string? match = matches.FirstOrDefault(path => Path.GetFileName(path).Equals(parts[index], StringComparison.Ordinal));
            if (match == null && matches.Length > 1) throw new IOException("The help link has ambiguous filename casing: " + relativePath);
            match ??= matches.SingleOrDefault();
            if (match == null) throw new FileNotFoundException("The help topic no longer exists: " + relativePath);
            current = SafePaths.Resolve(current, Path.GetFileName(match));
        }
        if (!File.Exists(current)) throw new FileNotFoundException("The help topic no longer exists: " + relativePath);
        return Path.GetRelativePath(ContentPath, current).Replace('\\', '/');
    }

    public void SaveTopic(string relativePath, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var document = Parse(source);
        if (document.Root?.Name.LocalName != "html" || !document.Descendants().Any(e => e.Name.LocalName == "body"))
            throw new InvalidDataException("A topic must contain an HTML root and body.");
        HelpSourceContent.ValidateForSave(document);
        var path = TopicPath(relativePath);
        if (!File.Exists(path)) throw new FileNotFoundException("Open an existing topic, or use New page.", path);
        SafePaths.AtomicWrite(path, HelpSourceNamespaces.NormalizeSource(source));
    }

    public string CreateTopic(string title, string folder = "basics")
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Enter a page title.", nameof(title));
        var directory = SafePaths.Resolve(ContentPath, string.IsNullOrWhiteSpace(folder) ? "." : folder, allowDot: true);
        Directory.CreateDirectory(directory);
        var normalized = title.Normalize(NormalizationForm.FormD);
        var stem = Regex.Replace(new string(normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", "_").Trim('_');
        if (stem.Length == 0) stem = "new_page";
        if (stem.Length > 90) stem = stem[..90];
        var path = Path.Combine(directory, stem + ".htm");
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(directory, stem + "_" + i + ".htm");
        var css = Path.GetRelativePath(directory, Path.Combine(ContentPath, "resources/stylesheets/statsdirect.css")).Replace('\\', '/');
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("html", new XAttribute(XNamespace.Xmlns + HelpSourceNamespaces.PreferredPrefix, HelpSourceNamespaces.CurrentUri),
                new XElement("head", new XElement("title", title.Trim()), new XElement("link", new XAttribute("rel", "stylesheet"), new XAttribute("href", css))),
                new XElement("body", new XElement("h1", title.Trim()), new XElement("p", "Write the new help page here."))));
        SafePaths.AtomicWrite(path, doc.Declaration + Environment.NewLine + doc.ToString());
        return Path.GetRelativePath(ContentPath, path).Replace('\\', '/');
    }

    public IReadOnlyList<string> FindReferences(string relativePath)
    {
        var target = TopicPath(relativePath);
        var references = new List<string>();
        // Shared master pages and imported snippets can link to topics too.
        // Ignoring them would let page deletion break every shared footer.
        var authoredFiles = Directory.EnumerateFiles(ContentPath, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".htm" or ".html" or ".flmsp" or ".flsnp")
            .Order(StringComparer.OrdinalIgnoreCase);
        foreach (var sourcePath in authoredFiles)
        {
            SafePaths.CheckNoLinks(sourcePath);
            if (SafePaths.Same(sourcePath, target)) continue;
            string relative = Path.GetRelativePath(ContentPath, sourcePath).Replace('\\', '/');
            var doc = Parse(File.ReadAllText(sourcePath));
            if (doc.Descendants().Attributes().Where(a => a.Name.LocalName is "href" or "src").Any(a => RefersTo(relative, a.Value, target)))
                references.Add(relative);
        }
        foreach (var node in Flatten(LoadContents()))
            if (node.Link is not null && RefersTo("", "/Content/" + node.Link, target)) references.Add("Contents: " + node.Title);
        var alias = SafePaths.Resolve(RootPath, "Project/Advanced/STATSDIRECT.flali");
        if (File.Exists(alias))
            foreach (var map in XDocument.Load(alias).Descendants("Map"))
                if (RefersTo("", (string?)map.Attribute("Link") ?? "", target)) references.Add("Context help: " + (string?)map.Attribute("Name"));
        return references.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void DeleteTopic(string relativePath) => DeleteTopicWithRecovery(relativePath);

    public List<TocNode> LoadContents()
    {
        string source = File.ReadAllText(TocPath);
        var doc = Parse(source);
        string stamp = ContentsStamp(source);
        TocNode Read(XElement e)
        {
            var node = new TocNode { Title = (string?)e.Attribute("Title") ?? "Untitled section", Link = NormalizeTocLink((string?)e.Attribute("Link")) };
            node.OriginalAttributes = e.Attributes().ToDictionary(a => a.Name, a => a.Value);
            node.SourceSnapshot = new(RootPath, stamp, node.Id, ContentsTemplate(e));
            node.Children = e.Elements("TocEntry").Select(Read).ToList();
            return node;
        }
        var nodes = (doc.Root ?? throw new InvalidDataException("Contents have no root element.")).Elements("TocEntry").Select(Read).ToList();
        ContentsSources.Add(nodes, new(RootPath, stamp));
        return nodes;
    }

    public void SaveContents(IReadOnlyList<TocNode> nodes)
    {
        string before = File.ReadAllText(TocPath);
        string source = HelpSourceNamespaces.NormalizeSource(CreateContentsSource(nodes, before));
        if (File.ReadAllText(TocPath) != before) throw new InvalidOperationException("Contents changed while saving. Reload them before applying these edits.");
        if (source != before) SafePaths.AtomicWrite(TocPath, source);
        BindContentsSnapshots(nodes, Parse(source).Root!, ContentsStamp(source));
    }

    private string CreateContentsSource(IReadOnlyList<TocNode> nodes)
        => CreateContentsSource(nodes, File.ReadAllText(TocPath));

    private string CreateContentsSource(IReadOnlyList<TocNode> nodes, string source)
    {
        var original = Parse(source);
        var doc = new XDocument(original);
        string stamp = ContentsStamp(source);
        if (ContentsSources.TryGetValue(nodes, out var treeSource) &&
            (!SafePaths.Same(treeSource.ProjectRoot, RootPath) || treeSource.SourceStamp != stamp))
            throw new InvalidOperationException("Contents changed after this tree was loaded, or it belongs to a different working copy. Reload them before applying these edits.");
        var seen = new HashSet<string>();
        XElement Write(TocNode n, int depth)
        {
            if (depth > 40 || string.IsNullOrWhiteSpace(n.Id) || !seen.Add(n.Id)) throw new InvalidDataException("The contents tree contains a repeated or excessively nested node.");
            if (string.IsNullOrWhiteSpace(n.Title)) throw new InvalidDataException("Every contents entry needs a title.");
            var snapshot = n.SourceSnapshot;
            if (snapshot != null && (!SafePaths.Same(snapshot.ProjectRoot, RootPath) || snapshot.NodeId != n.Id))
                throw new InvalidDataException("A contents entry belongs to a different working copy or its stable ID was changed. Add a new entry instead.");
            if (snapshot != null && snapshot.SourceStamp != stamp)
                throw new InvalidOperationException("Contents changed after this tree was loaded. Reload them before applying these edits.");
            var element = snapshot == null
                ? new XElement("TocEntry", n.OriginalAttributes.Select(a => new XAttribute(a.Key, a.Value)))
                : new XElement(snapshot.Element);
            if (snapshot == null || n.Title != ((string?)snapshot.Element.Attribute("Title") ?? "Untitled section")) element.SetAttributeValue("Title", n.Title.Trim());
            if (!string.IsNullOrWhiteSpace(n.Link))
            {
                var link = NormalizeTocLink(n.Link)!;
                var part = link.Split('#', '?')[0];
                TopicPath(part); // reject traversal and references outside Content
                if (!File.Exists(TopicPath(part))) throw new FileNotFoundException("Contents entry points to a missing topic: " + link);
                if (snapshot == null || link != NormalizeTocLink((string?)snapshot.Element.Attribute("Link")))
                    element.SetAttributeValue("Link", "/Content/" + link);
            }
            else if (snapshot == null || NormalizeTocLink((string?)snapshot.Element.Attribute("Link")) != null) element.Attribute("Link")?.Remove();
            ReplaceContentsChildren(element, n.Children.Select(c => Write(c, depth + 1)).ToArray());
            return element;
        }
        ReplaceContentsChildren(doc.Root ?? throw new InvalidDataException("Contents have no root element."), nodes.Select(n => Write(n, 0)).ToArray());
        if (XNode.DeepEquals(original, doc)) return source;
        // AtomicWrite emits UTF-8. A restored UTF-16 original must not leave a
        // stale UTF-16 declaration on its next ordinary Contents edit.
        if (doc.Declaration != null) doc.Declaration.Encoding = "utf-8";
        using var bytes = new MemoryStream();
        using (var writer = XmlWriter.Create(bytes, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false), Indent = false, OmitXmlDeclaration = doc.Declaration == null,
            NewLineChars = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n", NewLineHandling = NewLineHandling.Replace
        })) doc.Save(writer);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static string ContentsStamp(string source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));

    private void BindContentsSnapshots(IReadOnlyList<TocNode> nodes, XElement parent, string stamp)
    {
        ContentsSources.Remove(nodes);
        ContentsSources.Add(nodes, new(RootPath, stamp));
        var elements = parent.Elements("TocEntry").ToArray();
        if (nodes.Count != elements.Length) throw new InvalidDataException("The saved Contents tree no longer matches the edited entries. Reload it.");
        for (int index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index]; var element = elements[index];
            node.SourceSnapshot = new(RootPath, stamp, node.Id, ContentsTemplate(element));
            node.OriginalAttributes = element.Attributes().ToDictionary(a => a.Name, a => a.Value);
            BindContentsSnapshots(node.Children, element, stamp);
        }
    }

    private static void ReplaceContentsChildren(XElement parent, IReadOnlyList<XElement> children)
    {
        var slots = parent.Elements("TocEntry").ToArray();
        int shared = Math.Min(slots.Length, children.Count);
        for (int index = 0; index < shared; index++) slots[index].ReplaceWith(children[index]);
        for (int index = shared; index < slots.Length; index++) slots[index].Remove();
        XNode? previous = shared == 0 ? null : children[shared - 1];
        for (int index = shared; index < children.Count; index++)
        {
            var element = children[index];
            if (previous != null) previous.AddAfterSelf(element);
            else if (parent.LastNode is XText trailing && string.IsNullOrWhiteSpace(trailing.Value)) trailing.AddBeforeSelf(element);
            else parent.Add(element);
            previous = element;
        }
    }

    private static XElement ContentsTemplate(XElement element)
        // Each child has its own snapshot. Keep its slot here, without copying
        // every descendant into all its ancestors. LINQ to XML clones the
        // remaining parented nodes, preserving comments and extension markup.
    {
        var template = new XElement(element.Name, element.Attributes().Select(a => new XAttribute(a)),
            element.Nodes().Select(node => node is XElement child && child.Name == "TocEntry" ? new XElement(child.Name) : node));
        // LINQ to XML distinguishes <TocEntry /> from <TocEntry></TocEntry>.
        // Retain that distinction so an untouched imported tree is a true no-op.
        if (!element.IsEmpty && template.IsEmpty) template.Add("");
        return template;
    }

    internal string TopicPath(string relativePath)
    {
        var path = SafePaths.Resolve(ContentPath, relativePath);
        if (Path.GetExtension(path).ToLowerInvariant() is not (".htm" or ".html")) throw new ArgumentException("Choose an HTML topic.", nameof(relativePath));
        return path;
    }

    private bool RefersTo(string fromTopic, string link, string target)
    {
        if (string.IsNullOrWhiteSpace(link) || link.StartsWith('#') || link.Contains(':') || link.StartsWith("//")) return false;
        try
        {
            var path = link.Split('#', '?')[0].Replace('\\', '/');
            if (path.StartsWith("/Content/", StringComparison.OrdinalIgnoreCase)) path = path[9..];
            else if (path.StartsWith('/')) return false;
            else path = Path.Combine(Path.GetDirectoryName(fromTopic) ?? "", path);
            return SafePaths.Same(SafePaths.Resolve(ContentPath, Uri.UnescapeDataString(path), allowDot: true), target);
        }
        catch (ArgumentException) { return false; }
    }

    internal static string? NormalizeTocLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;
        link = link.Replace('\\', '/');
        if (link.StartsWith("/Content/", StringComparison.OrdinalIgnoreCase)) link = link[9..];
        else if (link.StartsWith("Content/", StringComparison.OrdinalIgnoreCase)) link = link[8..];
        return link.TrimStart('/');
    }

    internal static XDocument Parse(string source)
    {
        using var reader = XmlReader.Create(new StringReader(source), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    internal static IEnumerable<TocNode> Flatten(IEnumerable<TocNode> nodes)
    {
        foreach (var n in nodes) { yield return n; foreach (var child in Flatten(n.Children)) yield return child; }
    }
}
