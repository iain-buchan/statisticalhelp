using System.Globalization;
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
}

/// <summary>Edits only explicitly marked working copies, never the original Flare project.</summary>
public sealed partial class HelpProject
{
    public const string MarkerFile = ".statsdirect-help-editor.json";
    public string RootPath { get; }
    internal string ContentPath { get; }
    internal string TocPath => SafePaths.Resolve(RootPath, "Project/TOCs/STATSDIRECT.fltoc");
    private readonly Dictionary<string, Dictionary<XName, string>> tocAttributes = [];

    private HelpProject(string root)
    {
        RootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        SafePaths.CheckNoLinks(RootPath);
        if (SafePaths.IsWithin(RootPath, @"C:\Develop\SD3Help\StatsDirect"))
            throw new InvalidOperationException("The editor opens a working copy, not the original Flare project.");
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

    public void SaveTopic(string relativePath, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var document = Parse(source);
        if (document.Root?.Name.LocalName != "html" || !document.Descendants().Any(e => e.Name.LocalName == "body"))
            throw new InvalidDataException("A topic must contain an HTML root and body.");
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
        var doc = XDocument.Load(TocPath, LoadOptions.PreserveWhitespace);
        TocNode Read(XElement e)
        {
            var node = new TocNode { Title = (string?)e.Attribute("Title") ?? "Untitled section", Link = NormalizeTocLink((string?)e.Attribute("Link")) };
            node.OriginalAttributes = e.Attributes().ToDictionary(a => a.Name, a => a.Value);
            tocAttributes[node.Id] = node.OriginalAttributes;
            node.Children = e.Elements("TocEntry").Select(Read).ToList();
            return node;
        }
        return doc.Root!.Elements("TocEntry").Select(Read).ToList();
    }

    public void SaveContents(IReadOnlyList<TocNode> nodes)
        => SafePaths.AtomicWrite(TocPath, HelpSourceNamespaces.NormalizeSource(CreateContentsSource(nodes)));

    private string CreateContentsSource(IReadOnlyList<TocNode> nodes)
    {
        var doc = XDocument.Load(TocPath, LoadOptions.PreserveWhitespace);
        var seen = new HashSet<string>();
        XElement Write(TocNode n, int depth)
        {
            if (depth > 40 || !seen.Add(n.Id)) throw new InvalidDataException("The contents tree contains a repeated or excessively nested node.");
            if (string.IsNullOrWhiteSpace(n.Title)) throw new InvalidDataException("Every contents entry needs a title.");
            var attrs = tocAttributes.GetValueOrDefault(n.Id) ?? n.OriginalAttributes;
            var element = new XElement("TocEntry", attrs.Where(a => a.Key.LocalName is not ("Title" or "Link")).Select(a => new XAttribute(a.Key, a.Value)), new XAttribute("Title", n.Title.Trim()));
            if (!string.IsNullOrWhiteSpace(n.Link))
            {
                var link = NormalizeTocLink(n.Link)!;
                var part = link.Split('#', '?')[0];
                TopicPath(part); // reject traversal and references outside Content
                if (!File.Exists(TopicPath(part))) throw new FileNotFoundException("Contents entry points to a missing topic: " + link);
                element.Add(new XAttribute("Link", "/Content/" + link));
            }
            element.Add(n.Children.Select(c => Write(c, depth + 1)));
            return element;
        }
        var root = new XElement(doc.Root!.Name, doc.Root.Attributes().Select(a => new XAttribute(a)), nodes.Select(n => Write(n, 0)));
        doc.Root.ReplaceWith(root);
        // AtomicWrite emits UTF-8. A restored UTF-16 original must not leave a
        // stale UTF-16 declaration on its next ordinary Contents edit.
        if (doc.Declaration != null) doc.Declaration.Encoding = "utf-8";
        return doc.Declaration + Environment.NewLine + doc.ToString();
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

internal static class SafePaths
{
    internal static bool Same(string a, string b) => Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    internal static bool IsWithin(string path, string parent)
    {
        path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        parent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        return path.Equals(parent, StringComparison.OrdinalIgnoreCase) || path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    internal static string Resolve(string root, string relative, bool allowDot = false)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.IndexOfAny(['\0', '#', '?']) >= 0)
            throw new ArgumentException("Use a path within the working copy.");
        var clean = relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (!allowDot && clean.Split(Path.DirectorySeparatorChar).Any(p => p is ".." or ".")) throw new ArgumentException("Parent-directory paths are not allowed here.");
        var path = Path.GetFullPath(Path.Combine(root, clean));
        if (!IsWithin(path, root)) throw new ArgumentException("The path is outside the working copy.");
        CheckNoLinks(path);
        return path;
    }

    internal static void CheckNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current)!)
        {
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked files or folders are not supported in this prototype: " + current);
        }
    }

    internal static void AtomicWrite(string path, string text)
    {
        CheckNoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".helpeditor-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text, new UTF8Encoding(false)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
