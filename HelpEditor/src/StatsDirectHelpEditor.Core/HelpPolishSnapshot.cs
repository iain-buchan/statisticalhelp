using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace StatsDirect.HelpEditor.Core;

public sealed record HelpPolishBlock(string Id, string Text, string Location);
public sealed record HelpPolishProposal(string BlockId, string Before, string After, string Reason);
public sealed record HelpPolishChange(string Id, string Before, string After, string Reason, string Location)
{
    public string BlockId { get; init; } = "";
}

/// <summary>A frozen draft. AI suggestions can replace prose only; it never writes a file.</summary>
public sealed class HelpPolishSnapshot
{
    private static readonly HashSet<string> Excluded = new("head math equation math-field svg pre code script style textarea kbd samp var sup sub".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly Regex Numbers = new(@"(?<![\p{L}\p{Nd}])[-+−]?(?:\d+(?:[.,]\d+)*|\.\d+)(?:[eE][-+]?\d+)?%?", RegexOptions.CultureInvariant);
    private static readonly Regex Addresses = new(@"(?:https?://|mailto:)\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly IReadOnlyDictionary<string, ProseSpan> spans;
    public string TopicPath { get; }
    public string Source { get; }
    public string SourceDigest { get; }
    public IReadOnlyList<HelpPolishBlock> Blocks { get; }

    private HelpPolishSnapshot(string topicPath, string source, List<HelpPolishBlock> blocks, Dictionary<string, ProseSpan> spans)
    {
        TopicPath = topicPath; Source = source; Blocks = blocks.AsReadOnly(); this.spans = spans;
        SourceDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    public static HelpPolishSnapshot Create(string topicPath, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string path = NormalizePath(topicPath);
        var doc = HelpProject.Parse(source);
        if (doc.Root?.Name.LocalName != "html" || !doc.Descendants().Any(e => e.Name.LocalName == "body"))
            throw new InvalidDataException("Polishing requires an HTML topic with a body.");
        var blocks = new List<HelpPolishBlock>(); var spans = new Dictionary<string, ProseSpan>(StringComparer.Ordinal);
        var stack = new Stack<(string Local, bool Editable, bool Excluded)>();
        int line = 1, cursor = 0;
        void Advance(int end) { for (int at = cursor; at < end; at++) if (source[at] == '\n') line++; cursor = end; }
        void AddText(int start, int end, bool cdata)
        {
            if (stack.Count == 0 || !stack.Peek().Editable || start == end) return;
            var text = new StringBuilder(); var positions = new List<(int Offset, int Length)>();
            for (int at = start; at < end;)
            {
                int count = 1; string value;
                if (!cdata && source[at] == '&')
                {
                    int close = source.IndexOf(';', at);
                    count = close - at + 1; value = WebUtility.HtmlDecode(source.Substring(at, count));
                }
                else if (source[at] == '\r') { count = at + 1 < end && source[at + 1] == '\n' ? 2 : 1; value = "\n"; }
                else value = source[at].ToString();
                text.Append(value); foreach (char unused in value) positions.Add((at, count)); at += count;
            }
            string decoded = text.ToString(); int left = 0, right = decoded.Length;
            while (left < right && char.IsWhiteSpace(decoded[left])) left++;
            while (right > left && char.IsWhiteSpace(decoded[right - 1])) right--;
            if (left == right || !Regex.IsMatch(decoded[left..right], @"\p{L}{2}")) return;
            int rawStart = positions[left].Offset, rawEnd = positions[right - 1].Offset + positions[right - 1].Length;
            string id = "text-" + (blocks.Count + 1).ToString("D4");
            int actualLine = line + source.AsSpan(start, rawStart - start).Count('\n');
            string element = stack.Peek().Local;
            var block = new HelpPolishBlock(id, decoded[left..right], $"{element} · line {actualLine}");
            blocks.Add(block); spans.Add(id, new(rawStart, rawEnd - rawStart, cdata, block));
        }
        while (cursor < source.Length)
        {
            int tag = source.IndexOf('<', cursor);
            if (tag < 0) { AddText(cursor, source.Length, false); Advance(source.Length); break; }
            AddText(cursor, tag, false); Advance(tag);
            if (source.AsSpan(tag).StartsWith("<!--")) { Advance(source.IndexOf("-->", tag, StringComparison.Ordinal) + 3); continue; }
            if (source.AsSpan(tag).StartsWith("<![CDATA["))
            {
                int end = source.IndexOf("]]>", tag, StringComparison.Ordinal); AddText(tag + 9, end, true); Advance(end + 3); continue;
            }
            int close = tag + 1; char quote = '\0';
            for (; close < source.Length; close++)
            {
                char c = source[close];
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c is '\'' or '"') quote = c;
                else if (c == '>') break;
            }
            string token = source[(tag + 1)..close]; Advance(close + 1);
            if (token.StartsWith('?') || token.StartsWith('!')) continue;
            bool ending = token.StartsWith('/');
            string local = Regex.Match(token, @"^/?([^\s/>]+)").Groups[1].Value.Split(':')[^1].ToLowerInvariant();
            if (ending) { stack.Pop(); continue; }
            bool inherited = stack.Count > 0 && stack.Peek().Editable;
            bool excluded = Excluded.Contains(local) || stack.Count > 0 && stack.Peek().Excluded
                || AttributeWords(token, "class").Any(c => c.Equals("rcode", StringComparison.OrdinalIgnoreCase) || c.Equals("rcodenote", StringComparison.OrdinalIgnoreCase))
                || HasHiddenAttribute(token);
            bool editable = !excluded && (local == "body" || inherited);
            if (!token.TrimEnd().EndsWith('/')) stack.Push((local, editable, excluded));
        }
        return new(path, source, blocks, spans);
    }

    public HelpPolishReview Review(IEnumerable<HelpPolishProposal> proposals)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        var changes = new List<HelpPolishChange>(); var replacements = new Dictionary<string, ProseEdit>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var proposal in proposals)
        {
            if (proposal == null || proposal.BlockId == null || !spans.TryGetValue(proposal.BlockId, out var span))
                throw new InvalidDataException("A suggestion refers to text outside this page's editable prose.");
            if (!seen.Add(proposal.BlockId)) throw new InvalidDataException("The AI returned more than one suggestion for the same text. Request a fresh review.");
            if (proposal.Before != span.Block.Text) throw new InvalidDataException("A suggestion's original text does not match this draft. Request a fresh review.");
            if (proposal.After == null || proposal.Reason == null) throw new InvalidDataException("A suggestion is missing its replacement or explanation.");
            if (proposal.After == proposal.Before) throw new InvalidDataException("An unchanged passage is not a proposed change.");
            if (proposal.After.Length > Math.Max(4_000, proposal.Before.Length * 4L) || proposal.Reason.Length > 4_000)
                throw new InvalidDataException("An AI suggestion is unexpectedly long. Request a more focused review.");
            XmlConvert.VerifyXmlChars(proposal.After); XmlConvert.VerifyXmlChars(proposal.Reason);
            if (!Numbers.Matches(proposal.Before).Select(m => m.Value).SequenceEqual(Numbers.Matches(proposal.After).Select(m => m.Value)))
                throw new InvalidDataException("A suggestion changes numeric values. Text polishing must preserve the page's numbers.");
            if (!Addresses.Matches(proposal.Before).Select(m => m.Value).SequenceEqual(Addresses.Matches(proposal.After).Select(m => m.Value)))
                throw new InvalidDataException("A suggestion changes a visible URL. Edit references and links explicitly instead.");
            string id = Guid.NewGuid().ToString("N");
            var change = new HelpPolishChange(id, proposal.Before, proposal.After, proposal.Reason, span.Block.Location) { BlockId = proposal.BlockId };
            changes.Add(change);
            string replacement = span.Cdata ? proposal.After.Replace("]]>", "]]]]><![CDATA[>") : SecurityElement.Escape(proposal.After) ?? "";
            replacements.Add(id, new(span.Offset, span.Length, replacement));
        }
        if (changes.Count > 1_000) throw new InvalidDataException("Too many changes were returned for one page.");
        var review = new HelpPolishReview(this, changes, replacements);
        _ = review.Apply(changes.Select(c => c.Id)); // Validate the complete proposed draft before showing any changes.
        return review;
    }

    internal static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a help page.");
        path = path.Replace('\\', '/');
        if (path.StartsWith('/') || path.Split('/').Any(p => p is "" or "." or "..") || path.IndexOfAny([':', '?', '#', '\0']) >= 0
            || !new[] { ".htm", ".html" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a relative HTML page from this help project.");
        return path;
    }

    private static string AttributeValue(string token, string attribute)
    {
        var match = Regex.Match(token, "(?:^|\\s)" + attribute + "\\s*=\\s*(['\"])(.*?)\\1", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[2].Value) : "";
    }
    private static string[] AttributeWords(string token, string attribute) => AttributeValue(token, attribute).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    private static bool HasHiddenAttribute(string token) => Regex.IsMatch(token, @"(?:^|\s)hidden\s*=", RegexOptions.IgnoreCase)
        || AttributeValue(token, "aria-hidden").Equals("true", StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(AttributeValue(token, "style"), @"(?:^|;)\s*(?:display\s*:\s*none|visibility\s*:\s*hidden)\b", RegexOptions.IgnoreCase);
    private sealed record ProseSpan(int Offset, int Length, bool Cdata, HelpPolishBlock Block);
}

internal sealed record ProseEdit(int Offset, int Length, string Replacement);

/// <summary>Reviewed alternatives over one immutable draft; only explicitly accepted changes are applied.</summary>
public sealed class HelpPolishReview
{
    private readonly HelpPolishSnapshot snapshot;
    private readonly IReadOnlyDictionary<string, ProseEdit> replacements;
    public IReadOnlyList<HelpPolishChange> Changes { get; }
    public HelpPolishSnapshot Snapshot => snapshot;
    internal HelpPolishReview(HelpPolishSnapshot snapshot, List<HelpPolishChange> changes, Dictionary<string, ProseEdit> replacements)
    { this.snapshot = snapshot; Changes = changes.AsReadOnly(); this.replacements = replacements; }

    public string Apply(IEnumerable<string> acceptedIds)
    {
        ArgumentNullException.ThrowIfNull(acceptedIds);
        var ids = acceptedIds.ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || ids.Any(id => id == null || !replacements.ContainsKey(id)))
            throw new ArgumentException("Accept only changes from this review, once each.");
        if (ids.Length == 0) return snapshot.Source;
        var edits = ids.Select(id => replacements[id]).OrderByDescending(edit => edit.Offset).ToArray();
        int previous = snapshot.Source.Length; string updated = snapshot.Source;
        foreach (var edit in edits)
        {
            if (edit.Offset < 0 || edit.Length < 0 || edit.Offset + edit.Length > previous) throw new InvalidDataException("The proposed changes overlap.");
            updated = updated.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.Replacement); previous = edit.Offset;
        }
        _ = HelpProject.Parse(updated);
        return updated;
    }
}
