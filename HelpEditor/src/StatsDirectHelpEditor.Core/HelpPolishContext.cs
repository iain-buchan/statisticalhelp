using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace StatsDirect.HelpEditor.Core;

/// <summary>A read-only snapshot of the entire help corpus. Tools can retrieve every page in bounded chunks.</summary>
public sealed class HelpPolishContext
{
    private readonly IReadOnlyDictionary<string, ContextPage> pages;
    public int PageCount => pages.Count;
    public string CatalogText { get; }
    private HelpPolishContext(Dictionary<string, ContextPage> pages)
    {
        this.pages = pages;
        CatalogText = JsonSerializer.Serialize(new { pageCount = pages.Count, pages = pages.Values.Select(p => new { path = p.Path, title = p.Title, characters = p.Text.Length }) });
    }

    public static HelpPolishContext Create(HelpProject project, HelpPolishSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(current);
        var topics = project.ListTopics();
        if (!topics.Any(t => t.Path.Equals(current.TopicPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The current draft is not a page in the open help project.");
        var pages = new Dictionary<string, ContextPage>(StringComparer.OrdinalIgnoreCase);
        foreach (var topic in topics)
        {
            bool isCurrent = topic.Path.Equals(current.TopicPath, StringComparison.OrdinalIgnoreCase);
            string source = isCurrent ? current.Source : project.ReadTopic(topic.Path);
            var document = HelpProject.Parse(source);
            string title = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "title")?.Value.Trim()
                ?? document.Descendants().FirstOrDefault(e => e.Name.LocalName == "h1")?.Value.Trim() ?? topic.Title;
            var body = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "body");
            var text = new StringBuilder();
            if (body != null) AppendContext(body, text);
            pages.Add(topic.Path, new(topic.Path, title, Regex.Replace(text.ToString(), @"[ \t\r\f\v]+", " ").Trim()));
        }
        return new(pages);
    }

    public string ReadPage(string path, int offset = 0, int length = 16_000)
    {
        path = HelpPolishSnapshot.NormalizePath(path);
        if (!pages.TryGetValue(path, out var page)) throw new ArgumentException("This page is not in the help context catalog.");
        if (offset < 0 || offset > page.Text.Length || length < 1) throw new ArgumentOutOfRangeException(nameof(offset), "Use a valid page offset and positive chunk length.");
        length = Math.Min(length, 24_000);
        int end = (int)Math.Min(page.Text.Length, (long)offset + length);
        if (offset > 0 && offset < page.Text.Length && char.IsLowSurrogate(page.Text[offset]) && char.IsHighSurrogate(page.Text[offset - 1])) offset--;
        if (end < page.Text.Length && end > offset && char.IsHighSurrogate(page.Text[end - 1]) && char.IsLowSurrogate(page.Text[end])) end--;
        // A one-character request must still make progress over a supplementary character.
        if (end == offset && end + 1 < page.Text.Length) end += 2;
        return JsonSerializer.Serialize(new { path = page.Path, title = page.Title, offset, text = page.Text[offset..end], totalCharacters = page.Text.Length,
            nextOffset = end < page.Text.Length ? (int?)end : null, complete = end == page.Text.Length });
    }

    public string Search(string query, int maxResults = 12)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 1_000) throw new ArgumentException("Enter a context search of 1 to 1,000 characters.");
        maxResults = Math.Clamp(maxResults, 1, 30);
        string[] words = Regex.Matches(query, @"[\p{L}\p{Nd}]+", RegexOptions.CultureInvariant).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
        if (words.Length == 0) throw new ArgumentException("Use words or numbers to search the help pages.");
        var hits = pages.Values.Select(page =>
        {
            int first = -1, score = 0;
            foreach (string word in words)
            {
                if (page.Title.Contains(word, StringComparison.OrdinalIgnoreCase)) score += 6;
                int at = page.Text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
                if (at >= 0) { score++; if (first < 0 || at < first) first = at; }
            }
            if (page.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 20;
            int phrase = page.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (phrase >= 0) { score += 10; first = phrase; }
            int start = Math.Max(0, first - 100), end = Math.Min(page.Text.Length, start + 650);
            return new { page, score, snippet = page.Text[start..end] };
        }).Where(hit => hit.score > 0).OrderByDescending(hit => hit.score).ThenBy(hit => hit.page.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        return JsonSerializer.Serialize(new { query, totalMatches = hits.Length, returned = Math.Min(maxResults, hits.Length),
            results = hits.Take(maxResults).Select(hit => new { path = hit.page.Path, title = hit.page.Title, score = hit.score, snippet = hit.snippet }) });
    }

    private static readonly HashSet<string> ContextBlocks = new("h1 h2 h3 h4 h5 h6 p li div br table tr td th pre code blockquote math equation dropdown".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static void AppendContext(XNode node, StringBuilder text)
    {
        if (node is XText value) { text.Append(value.Value); return; }
        if (node is not XElement element || element.Name.LocalName is "script" or "style" or "svg") return;
        string local = element.Name.LocalName;
        // Flattening MathML to text would turn a fraction, power or limit into
        // an ambiguous list of symbols. Context may inspect its full structure,
        // while the independently generated editable blocks exclude equations.
        if (local is "math" or "equation")
        {
            text.Append("\n[read-only equation source]\n").Append(element.ToString(SaveOptions.DisableFormatting)).Append("\n[/read-only equation source]\n");
            return;
        }
        bool block = ContextBlocks.Contains(local);
        if (block && text.Length > 0 && text[^1] != '\n') text.Append('\n');
        if (local == "img" && element.Attribute("alt") is { } alt) text.Append(" [image: ").Append(alt.Value).Append("] ");
        foreach (var child in element.Nodes()) AppendContext(child, text);
        if (block && text.Length > 0 && text[^1] != '\n') text.Append('\n');
    }
    private sealed record ContextPage(string Path, string Title, string Text);
}
