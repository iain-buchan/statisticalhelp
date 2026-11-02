using System.Globalization;
using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace StatsDirect.HelpEditor.Core;

public enum HelpSearchScope { PageText, HtmlSource }
public record HelpSearchOptions(string FindText, string ReplaceText, bool MatchCase = false, bool WholeWords = false, HelpSearchScope Scope = HelpSearchScope.PageText);
public record HelpSearchMatch(string Id, string TopicPath, string Title, int Line, string Before, string After)
{
    public int SourceOffset { get; init; }
    public int SourceLength { get; init; }
    public string SourceDigest { get; init; } = "";
}
public record HelpReplaceResult(int ChangedPages, int Replacements, IReadOnlyList<string> ChangedTopicPaths);

public sealed class HelpSearchPreview
{
    public HelpSearchOptions Options { get; }
    public IReadOnlyList<HelpSearchMatch> Matches { get; }
    public int PageCount => Files.Count;
    public int MatchingPageCount => Matches.Select(match => match.TopicPath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public int SearchedPageCount => Files.Count;
    internal Guid Owner { get; }
    internal IReadOnlyDictionary<string, SearchReplaceFile> Files { get; }
    internal IReadOnlyDictionary<string, SearchReplaceEdit[]> Edits { get; }
    internal HelpSearchPreview(Guid owner, HelpSearchOptions options, List<HelpSearchMatch> matches, Dictionary<string, SearchReplaceFile> files, Dictionary<string, SearchReplaceEdit[]> edits)
    { Owner = owner; Options = options; Matches = matches.AsReadOnly(); Files = files; Edits = edits; }
}

internal sealed record SearchReplaceFile(byte[] Bytes, string Source, Encoding Encoding, byte[] Preamble);
internal sealed record SearchReplaceEdit(int Offset, int Length, string Replacement);

public sealed partial class HelpProject
{
    private readonly Guid searchReplaceOwner = Guid.NewGuid();
    private SearchReplaceUndo? lastSearchReplaceUndo;
    public bool CanUndoSearchReplace => lastSearchReplaceUndo != null;
    private static readonly HashSet<string> SearchExcluded = new("pre code script style textarea kbd samp math equation svg".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> SearchBlocks = new("address article aside blockquote body caption dd div dl dt figcaption figure footer form h1 h2 h3 h4 h5 h6 head header hr li main ol p section table tbody td th thead title tr ul br".Split(' '), StringComparer.OrdinalIgnoreCase);

    /// <summary>Find literal occurrences in every saved topic; never writes source.</summary>
    public HelpSearchPreview SearchReplacePreview(HelpSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.FindText)) throw new ArgumentException("Enter text to find.");
        if (options.ReplaceText == null || !Enum.IsDefined(options.Scope)) throw new ArgumentException("Choose a valid replacement and search scope.");
        XmlConvert.VerifyXmlChars(options.FindText); XmlConvert.VerifyXmlChars(options.ReplaceText);
        var matches = new List<HelpSearchMatch>();
        var files = new Dictionary<string, SearchReplaceFile>(StringComparer.OrdinalIgnoreCase);
        var edits = new Dictionary<string, SearchReplaceEdit[]>(StringComparer.Ordinal);
        foreach (var topic in ListTopics())
        {
            string path = TopicPath(topic.Path);
            var file = ReadSearchReplaceFile(File.ReadAllBytes(path));
            _ = Parse(file.Source);
            string sourceDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.Source)));
            files.Add(topic.Path, file);
            var lines = new List<int> { 0 };
            for (int at = 0; at < file.Source.Length; at++) if (file.Source[at] == '\n') lines.Add(at + 1);
            var groups = options.Scope == HelpSearchScope.HtmlSource
                ? new[] { SearchText.Raw(file.Source) } : SearchTextGroups(file.Source);
            foreach (var group in groups)
            {
                int start = 0;
                while (start <= group.Text.Length - options.FindText.Length)
                {
                    int at = group.Text.IndexOf(options.FindText, start, options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                    if (at < 0) break;
                    start = at + options.FindText.Length;
                    if (options.WholeWords && (at > 0 && SearchWord(group.Text, at - 1) || start < group.Text.Length && SearchWord(group.Text, start))) continue;
                    if (matches.Count >= 50_000) throw new InvalidOperationException("More than 50,000 matches were found. Narrow the search before replacing.");
                    var sourceEdits = group.Edits(at, options.FindText.Length, options.ReplaceText, options.Scope);
                    int offset = sourceEdits[0].Offset, end = sourceEdits[^1].Offset + sourceEdits[^1].Length;
                    int line = lines.BinarySearch(offset); line = line >= 0 ? line + 1 : ~line;
                    string id = Guid.NewGuid().ToString("N");
                    string before = SearchContext(group.Text, at, options.FindText.Length, group.Text.Substring(at, options.FindText.Length));
                    string after = SearchContext(group.Text, at, options.FindText.Length, options.ReplaceText);
                    matches.Add(new(id, topic.Path, topic.Title, line, before, after) { SourceOffset = offset, SourceLength = end - offset, SourceDigest = sourceDigest });
                    // A semantic no-op must retain original entity spelling.
                    // Case-insensitive matches with different case can still be normalized.
                    edits.Add(id, group.Text.Substring(at, options.FindText.Length) == options.ReplaceText ? [] : sourceEdits);
                }
            }
        }
        return new(searchReplaceOwner, options, matches, files, edits);
    }

    /// <summary>Apply only reviewed occurrence IDs; every proposed page is validated before the batch starts.</summary>
    public HelpReplaceResult ApplySearchReplace(HelpSearchPreview preview, IEnumerable<string> selectedMatchIds)
    {
        ArgumentNullException.ThrowIfNull(preview); ArgumentNullException.ThrowIfNull(selectedMatchIds);
        if (preview.Owner != searchReplaceOwner) throw new InvalidOperationException("This preview belongs to another project session. Search again.");
        var ids = selectedMatchIds.ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || ids.Any(id => id == null || !preview.Edits.ContainsKey(id)))
            throw new ArgumentException("Select only occurrences from the current preview, once each.");
        if (ids.Length == 0) return EmptySearchReplaceResult();
        VerifySearchPreview(preview);
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        var changes = new List<SearchReplaceChange>();
        int replacements = 0;
        foreach (var page in preview.Matches.Where(match => selected.Contains(match.Id)).GroupBy(match => match.TopicPath, StringComparer.OrdinalIgnoreCase))
        {
            var file = preview.Files[page.Key];
            var effective = page.Where(match => preview.Edits[match.Id].Any(edit => file.Source.Substring(edit.Offset, edit.Length) != edit.Replacement)).ToArray();
            if (effective.Length == 0) continue;
            string updated = file.Source;
            foreach (var edit in effective.SelectMany(match => preview.Edits[match.Id]).OrderByDescending(edit => edit.Offset))
                updated = updated.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.Replacement);
            var document = Parse(updated);
            if (document.Root?.Name.LocalName != "html" || !document.Descendants().Any(element => element.Name.LocalName == "body"))
                throw new InvalidDataException("The replacement would remove a page's HTML root or body: " + page.Key);
            byte[] after = file.Preamble.Concat(file.Encoding.GetBytes(updated)).ToArray();
            if (after.AsSpan().SequenceEqual(file.Bytes)) continue;
            changes.Add(new(page.Key, file.Bytes, after)); replacements += effective.Length;
        }
        if (changes.Count == 0) return EmptySearchReplaceResult();
        WriteSearchReplaceBatch(changes, reverse: false);
        lastSearchReplaceUndo = new(changes, replacements);
        return new(changes.Count, replacements, changes.Select(change => change.Path).ToArray());
    }

    /// <summary>Verify that a reviewed search still describes the saved project, without changing files.</summary>
    public void ValidateSearchReplacePreview(HelpSearchPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (preview.Owner != searchReplaceOwner) throw new InvalidOperationException("This preview belongs to another project session. Search again.");
        VerifySearchPreview(preview);
    }

    /// <summary>Undo the latest project replacement in this editor session, provided none of its pages changed afterwards.</summary>
    public HelpReplaceResult UndoLastSearchReplace()
    {
        var undo = lastSearchReplaceUndo ?? throw new InvalidOperationException("There is no project replacement to undo in this session.");
        WriteSearchReplaceBatch(undo.Changes, reverse: true);
        lastSearchReplaceUndo = null;
        return new(undo.Changes.Count, undo.Replacements, undo.Changes.Select(change => change.Path).ToArray());
    }

    private void VerifySearchPreview(HelpSearchPreview preview)
    {
        var topics = ListTopics();
        if (!topics.Select(topic => topic.Path).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(preview.Files.Keys))
            throw new InvalidOperationException("The project page list changed after this preview. Search again before replacing.");
        foreach (var file in preview.Files)
            if (!File.ReadAllBytes(TopicPath(file.Key)).AsSpan().SequenceEqual(file.Value.Bytes))
                throw new InvalidOperationException("A page changed after this preview. Search again before replacing: " + file.Key);
    }

    private void WriteSearchReplaceBatch(IReadOnlyList<SearchReplaceChange> changes, bool reverse)
    {
        var held = new List<(SearchReplaceChange Change, FileStream Stream)>();
        try
        {
            foreach (var change in changes.OrderBy(change => change.Path, StringComparer.OrdinalIgnoreCase))
            {
                string path = TopicPath(change.Path); RequireWritable(path, "A replacement page is read-only: " + change.Path);
                var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                held.Add((change, stream));
                byte[] expected = reverse ? change.After : change.Before;
                if (!ReadRecoveryBytes(stream).AsSpan().SequenceEqual(expected))
                    throw new InvalidOperationException("A replacement page changed. Nothing has been replaced; refresh the search: " + change.Path);
            }
            int attempted = 0;
            try
            {
                foreach (var item in held)
                {
                    attempted++;
                    Write(item.Stream, reverse ? item.Change.Before : item.Change.After);
                }
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                foreach (var item in held.Take(attempted))
                    try { Write(item.Stream, reverse ? item.Change.After : item.Change.Before); }
                    catch (Exception rollback) { errors.Add(rollback); }
                if (errors.Count > 1) throw new AggregateException("The replacement failed and a page could not be restored. Keep the editor open and review these errors.", errors);
                throw;
            }
        }
        finally { foreach (var item in held) item.Stream.Dispose(); }
        static void Write(FileStream stream, byte[] bytes) { stream.Position = 0; stream.Write(bytes); stream.SetLength(bytes.Length); stream.Flush(flushToDisk: true); }
    }

    private static HelpReplaceResult EmptySearchReplaceResult() => new(0, 0, Array.Empty<string>());
    private sealed record SearchReplaceChange(string Path, byte[] Before, byte[] After);
    private sealed record SearchReplaceUndo(IReadOnlyList<SearchReplaceChange> Changes, int Replacements);
    private static bool SearchWord(string text, int index)
    {
        if (char.IsLowSurrogate(text[index]) && index > 0 && char.IsHighSurrogate(text[index - 1])) index--;
        return CharUnicodeInfo.GetUnicodeCategory(text, index) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.ConnectorPunctuation;
    }
    private static string SearchContext(string text, int at, int length, string replacement)
    {
        int left = Math.Max(0, at - 42), right = Math.Min(text.Length, at + length + 42);
        return (left > 0 ? "…" : "") + Regex.Replace(text[left..at] + "[" + replacement + "]" + text[(at + length)..right], @"\s+", " ") + (right < text.Length ? "…" : "");
    }

    private static SearchReplaceFile ReadSearchReplaceFile(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding; int bom;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = new UTF32Encoding(false, false, true); bom = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, false, true); bom = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) { encoding = new UTF8Encoding(false, true); bom = 3; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); bom = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); bom = 2; }
        else
        {
            bom = 0;
            if (bytes.Length >= 4 && bytes[0] == '<' && bytes[1] == 0) encoding = new UnicodeEncoding(false, false, true);
            else if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == '<') encoding = new UnicodeEncoding(true, false, true);
            else
            {
                string prefix = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 250));
                var declaration = Regex.Match(prefix, "^\\s*<\\?xml\\b[^?]*\\bencoding\\s*=\\s*['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase);
                encoding = declaration.Success ? Encoding.GetEncoding(declaration.Groups[1].Value, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) : new UTF8Encoding(false, true);
            }
        }
        string source = encoding.GetString(bytes, bom, bytes.Length - bom);
        return new(bytes, source, encoding, bytes[..bom]);
    }

    private readonly record struct SearchCharacter(int Offset, int Length, bool Cdata);
    private sealed class SearchText
    {
        internal string Text { get; }
        private readonly IReadOnlyList<SearchCharacter>? characters;
        internal SearchText(string text, IReadOnlyList<SearchCharacter>? positions) { Text = text; characters = positions; }
        internal static SearchText Raw(string source) => new(source, null);
        internal SearchReplaceEdit[] Edits(int at, int length, string replacement, HelpSearchScope scope)
        {
            if (scope == HelpSearchScope.HtmlSource) return [new(at, length, replacement)];
            var pieces = new List<SearchCharacter>();
            int previousOffset = -1, previousLength = 0;
            foreach (var part in characters!.Skip(at).Take(length))
            {
                if (part.Offset == previousOffset && part.Length == previousLength) continue; // one supplementary entity
                previousOffset = part.Offset; previousLength = part.Length;
                if (pieces.Count > 0 && pieces[^1].Offset + pieces[^1].Length == part.Offset && pieces[^1].Cdata == part.Cdata)
                    pieces[^1] = pieces[^1] with { Length = pieces[^1].Length + part.Length };
                else pieces.Add(part);
            }
            return pieces.Select((part, index) => new SearchReplaceEdit(part.Offset, part.Length, index != 0 ? "" : scope == HelpSearchScope.HtmlSource ? replacement
                : part.Cdata ? replacement.Replace("]]>", "]]]]><![CDATA[>") : SecurityElement.Escape(replacement) ?? "")).ToArray();
        }
    }

    private static IEnumerable<SearchText> SearchTextGroups(string source)
    {
        var groups = new List<SearchText>(); var text = new StringBuilder(); var positions = new List<SearchCharacter>();
        var stack = new Stack<(string Name, bool Excluded, bool Visible)>();
        void Flush() { if (text.Length > 0 && !string.IsNullOrWhiteSpace(text.ToString())) groups.Add(new(text.ToString(), positions.ToArray())); text.Clear(); positions.Clear(); }
        void Add(int from, int to, bool cdata)
        {
            if (stack.Count == 0 || stack.Peek().Excluded || !stack.Peek().Visible) return;
            for (int at = from; at < to; at++)
            {
                int count = 1; string value;
                if (!cdata && source[at] == '&') { int end = source.IndexOf(';', at); count = end - at + 1; value = WebUtility.HtmlDecode(source.Substring(at, count)); }
                else if (source[at] == '\r') { count = at + 1 < to && source[at + 1] == '\n' ? 2 : 1; value = "\n"; }
                else value = source[at].ToString();
                text.Append(value); foreach (char unused in value) positions.Add(new(at, count, cdata)); at += count - 1;
            }
        }
        int cursor = 0;
        while (cursor < source.Length)
        {
            int tag = source.IndexOf('<', cursor);
            if (tag < 0) { Add(cursor, source.Length, false); break; }
            Add(cursor, tag, false);
            if (source.AsSpan(tag).StartsWith("<!--")) { cursor = source.IndexOf("-->", tag, StringComparison.Ordinal) + 3; continue; }
            if (source.AsSpan(tag).StartsWith("<![CDATA[")) { int end = source.IndexOf("]]>", tag, StringComparison.Ordinal); Add(tag + 9, end, true); cursor = end + 3; continue; }
            int close = tag + 1; char quote = '\0';
            for (; close < source.Length; close++) { char c = source[close]; if (quote != '\0') { if (c == quote) quote = '\0'; } else if (c is '\'' or '"') quote = c; else if (c == '>') break; }
            string token = source[(tag + 1)..close]; cursor = close + 1;
            if (token.StartsWith('?') || token.StartsWith('!')) continue;
            bool ending = token.StartsWith('/');
            string name = Regex.Match(token, @"^/?([^\s/>]+)").Groups[1].Value;
            string local = name.Split(':')[^1].ToLowerInvariant();
            bool excluded = SearchExcluded.Contains(local) || local == "dropdown" && Regex.IsMatch(token, "\\bclass\\s*=\\s*(['\"])[^'\"]*\\brcode\\b[^'\"]*\\1", RegexOptions.IgnoreCase);
            if (SearchBlocks.Contains(local) || excluded) Flush();
            if (ending) { if (stack.Count > 0) stack.Pop(); }
            else if (!token.TrimEnd().EndsWith('/')) stack.Push((name, excluded || stack.Count > 0 && stack.Peek().Excluded, local is "body" or "title" || stack.Count > 0 && stack.Peek().Visible));
        }
        Flush(); return groups;
    }
}
