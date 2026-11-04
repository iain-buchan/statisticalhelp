using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace StatsDirect.HelpEditor.Core;

public sealed record HelpSourceTopicDifference(string Path, bool ExistsOnLeft, bool ExistsOnRight,
    bool RawBytesEqual, bool NamespaceEquivalent, bool TextChanged, bool UrlsChanged,
    bool EquationsChanged, bool CodeChanged, bool OtherMarkupChanged);

public sealed record HelpSourceComparisonReport(string LeftRoot, string RightRoot, string LeftSha256,
    string RightSha256, int LeftTopicCount, int RightTopicCount, int CommonTopicCount,
    int EquivalentTopicCount, IReadOnlyList<HelpSourceTopicDifference> Topics,
    IReadOnlyList<HelpSourceTopicDifference> Contents)
{
    public IReadOnlyList<HelpSourceTopicDifference> Differences => Topics.Where(topic => !topic.NamespaceEquivalent).ToArray();
    public bool? ContentsEquivalent => Contents.Count == 0 ? null : Contents.All(item => item.NamespaceEquivalent);
}

/// <summary>Compares frozen saved XML without opening, marking, migrating or saving either source tree.</summary>
public static class HelpSourceComparer
{
    public static HelpSourceComparisonReport Compare(string leftProjectOrContent, string rightProjectOrContent,
        CancellationToken cancellationToken = default)
    {
        var left = Read(leftProjectOrContent, cancellationToken);
        var right = Read(rightProjectOrContent, cancellationToken);
        var topics = CompareFiles(left.Topics, right.Topics, cancellationToken);
        var contents = CompareFiles(left.Contents, right.Contents, cancellationToken);
        AssertUnchanged(left, cancellationToken); AssertUnchanged(right, cancellationToken);
        return new(left.Root, right.Root, left.Hash, right.Hash, left.Topics.Count, right.Topics.Count,
            topics.Count(item => item.ExistsOnLeft && item.ExistsOnRight),
            topics.Count(item => item.NamespaceEquivalent), topics, contents);
    }

    private sealed record Snapshot(string Root, string Content, Dictionary<string, byte[]> Topics,
        Dictionary<string, byte[]> Contents, string Hash);

    private static Snapshot Read(string path, CancellationToken token)
    {
        string requested = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        SafePaths.CheckNoLinks(requested);
        if (!Directory.Exists(requested)) throw new DirectoryNotFoundException(requested);
        string content = Path.GetFileName(requested).Equals("Content", StringComparison.OrdinalIgnoreCase)
            ? requested : Path.Combine(requested, "Content");
        if (!Directory.Exists(content)) throw new InvalidDataException("Choose a help project or its Content directory.");
        string root = Path.GetDirectoryName(content)!;
        var topics = ReadFiles(content, [".htm", ".html"], token);
        if (topics.Count == 0) throw new InvalidDataException("The selected Content directory contains no help topics.");
        string toc = Path.Combine(root, "Project", "TOCs");
        var contents = Directory.Exists(toc) ? ReadFiles(toc, [".fltoc"], token) : new(StringComparer.OrdinalIgnoreCase);
        return new(root, content, topics, contents, Hash(topics, contents));
    }

    private static Dictionary<string, byte[]> ReadFiles(string root, string[] extensions, CancellationToken token)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested(); string folder = pending.Pop(); SafePaths.CheckNoLinks(folder);
            foreach (string child in Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested(); SafePaths.CheckNoLinks(child);
                if (Directory.Exists(child)) { pending.Push(child); continue; }
                if (!extensions.Contains(Path.GetExtension(child), StringComparer.OrdinalIgnoreCase)) continue;
                if (new FileInfo(child).Length > 20_000_000) throw new InvalidDataException("A help XML file exceeds the comparison limit.");
                string relative = Path.GetRelativePath(root, child).Replace('\\', '/');
                if (!result.TryAdd(relative, File.ReadAllBytes(child))) throw new InvalidDataException("Case-colliding source paths cannot be compared: " + relative);
                if (result.Count > 10_000) throw new InvalidDataException("The selected help inventory exceeds 10,000 XML files.");
            }
        }
        return result;
    }

    private static void AssertUnchanged(Snapshot snapshot, CancellationToken token)
    {
        var current = Read(snapshot.Content, token);
        if (current.Hash != snapshot.Hash) throw new IOException("Help source changed while it was being compared. Rerun the comparison; no source was written.");
    }

    private static string Hash(Dictionary<string, byte[]> topics, Dictionary<string, byte[]> contents)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var group in new[] { ("Content/", topics), ("Project/TOCs/", contents) })
            foreach (var file in group.Item2.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                hash.AppendData(Encoding.UTF8.GetBytes(group.Item1 + file.Key + "\0"));
                hash.AppendData(SHA256.HashData(file.Value));
            }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static HelpSourceTopicDifference[] CompareFiles(Dictionary<string, byte[]> left,
        Dictionary<string, byte[]> right, CancellationToken token)
    {
        return left.Keys.Union(right.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Select(path =>
        {
            token.ThrowIfCancellationRequested();
            bool hasLeft = left.TryGetValue(path, out var leftBytes), hasRight = right.TryGetValue(path, out var rightBytes);
            if (!hasLeft || !hasRight) return new HelpSourceTopicDifference(path, hasLeft, hasRight, false, false, true, true, true, true, true);
            var a = Parse(leftBytes!);
            var b = Parse(rightBytes!);
            bool equivalent = Canonical(a) == Canonical(b), text = VisibleText(a) != VisibleText(b), urls = Urls(a) != Urls(b),
                equations = Equations(a) != Equations(b), code = Code(a) != Code(b);
            bool markup = Canonical(a, maskContent: true) != Canonical(b, maskContent: true)
                || !equivalent && !text && !urls && !equations && !code;
            return new HelpSourceTopicDifference(path, true, true, leftBytes!.AsSpan().SequenceEqual(rightBytes!),
                equivalent, text, urls, equations, code, markup);
        }).ToArray();
    }

    private static XDocument Parse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 20_000_000 });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static string NormalName(XName name) => "{" + (HelpSourceNamespaces.IsAuthoringNamespace(name.NamespaceName)
        ? HelpSourceNamespaces.CurrentUri : name.NamespaceName) + "}" + name.LocalName;
    private static bool IsEquation(XElement element) => HelpSourceNamespaces.IsElement(element, "equation")
        || element.Name.LocalName == "math" && element.Name.NamespaceName == "http://www.w3.org/1998/Math/MathML";
    private static bool IsCode(XElement element) => element.Name.LocalName is "pre" or "code";
    private static bool IsUrl(XAttribute attribute) => attribute.Name.LocalName is "href" or "src" or "Link";
    private static string NormalText(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string VisibleText(XDocument document) => NormalText(string.Join(' ', document.DescendantNodes().OfType<XText>()
        .Where(text => !text.Ancestors().Any(element => IsEquation(element) || IsCode(element)
            || element.Name.LocalName is "script" or "style") &&
            (!text.Ancestors().Any(element => element.Name.LocalName == "head") || text.Parent?.Name.LocalName == "title"))
        .Select(text => text.Value).Concat(document.Descendants().Attributes().Where(attribute => attribute.Name.LocalName is "Title" or "title" or "alt").Select(attribute => attribute.Value))));
    private static string Urls(XDocument document) => string.Join('\n', document.Descendants().SelectMany(element => element.Attributes()
        .Where(IsUrl).Select(attribute => NormalName(element.Name) + ":" + NormalName(attribute.Name) + "=" + attribute.Value)));
    private static string Equations(XDocument document) => string.Join('\n', document.Descendants().Where(element => IsEquation(element)
        && !element.Ancestors().Any(IsEquation)).Select(element => Canonical(new XDocument(new XElement(element)))));
    private static string Code(XDocument document) => string.Join("\0", document.Descendants().Where(element => IsCode(element)
        && !element.Ancestors().Any(IsCode)).Select(element => element.Value));

    private static string Canonical(XDocument document, bool maskContent = false)
    {
        var output = new StringBuilder();
        void Field(string value) { output.Append(value.Length).Append(':').Append(value); }
        void Node(XNode node)
        {
            switch (node)
            {
                case XElement element:
                    output.Append('E'); Field(NormalName(element.Name));
                    foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).OrderBy(attribute => NormalName(attribute.Name), StringComparer.Ordinal))
                    { output.Append('A'); Field(NormalName(attribute.Name)); Field(maskContent && IsUrl(attribute) ? "<url>" : attribute.Value); }
                    if (maskContent && (IsEquation(element) || IsCode(element))) output.Append("<protected-content>");
                    else foreach (var child in element.Nodes()) Node(child);
                    output.Append('/'); break;
                case XText text: output.Append('T'); Field(maskContent ? "<text>" : text.Value); break;
                case XComment comment: output.Append('C'); Field(comment.Value); break;
                case XProcessingInstruction instruction: output.Append('P'); Field(instruction.Target); Field(instruction.Data); break;
            }
        }
        foreach (var node in document.Nodes()) Node(node);
        return output.ToString();
    }
}
