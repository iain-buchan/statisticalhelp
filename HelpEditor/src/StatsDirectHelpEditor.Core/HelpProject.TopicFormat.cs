using System.Security.Cryptography;
using System.Text;

namespace StatsDirect.HelpEditor.Core;

public sealed record HelpTopicSnapshot(string Source, string Revision);

public sealed class HelpTopicConflictException(string path)
    : InvalidOperationException("The saved topic changed. Reload it before saving: " + path);

public sealed partial class HelpProject
{
    /// <summary>Read editable source with a revision covering the original encoded bytes.</summary>
    public HelpTopicSnapshot ReadTopicSnapshot(string relativePath)
    {
        var file = ReadSearchReplaceFile(File.ReadAllBytes(TopicPath(relativePath)));
        return TopicSnapshot(file.Source, file.Bytes);
    }

    /// <summary>Save a browser draft while retaining the saved topic's encoding, BOM and line endings.</summary>
    public HelpTopicSnapshot SaveTopicPreservingFormat(string relativePath, string source, string expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(source);
        string path = TopicPath(relativePath);
        var file = ReadSearchReplaceFile(File.ReadAllBytes(path));
        var before = TopicSnapshot(file.Source, file.Bytes);
        if (before.Revision != expectedRevision) throw new HelpTopicConflictException(relativePath);

        var document = Parse(source);
        if (document.Root?.Name.LocalName != "html" || !document.Descendants().Any(element => element.Name.LocalName == "body"))
            throw new InvalidDataException("A topic must contain an HTML root and body.");
        HelpSourceContent.ValidateForSave(document);

        // A textarea normalizes CRLF/CR to LF even if the author changes nothing.
        if (SafePaths.NormalizeNewlines(source) == SafePaths.NormalizeNewlines(file.Source)) return before;
        string canonical = HelpSourceNamespaces.NormalizeSource(source);
        string restored = RestoreTopicLineEndings(file.Source, canonical);
        byte[] after = file.Preamble.Concat(file.Encoding.GetBytes(restored)).ToArray();
        if (after.AsSpan().SequenceEqual(file.Bytes)) return before;
        RequireWritable(path, "The topic is read-only: " + relativePath);

        string temporary = path + ".helpeditor-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            SafePaths.CheckNoLinks(temporary);
            using (var stream = File.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(after);
                stream.Flush(flushToDisk: true);
            }
            SafePaths.CheckNoLinks(path);
            SafePaths.CheckNoLinks(temporary);
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(file.Bytes)) throw new HelpTopicConflictException(relativePath);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return TopicSnapshot(restored, after);
    }

    private static HelpTopicSnapshot TopicSnapshot(string source, byte[] bytes) =>
        new(source, Convert.ToHexString(SHA256.HashData(bytes)));

    private static string RestoreTopicLineEndings(string original, string edited)
    {
        string[] styles = ["\n", "\r\n", "\r"];
        var endings = new List<byte>();
        int[] counts = new int[styles.Length];
        for (int index = 0; index < original.Length; index++)
        {
            byte style;
            if (original[index] == '\r')
            {
                style = 2;
                if (index + 1 < original.Length && original[index + 1] == '\n') { style = 1; index++; }
            }
            else if (original[index] == '\n') style = 0;
            else continue;
            endings.Add(style);
            counts[style]++;
        }
        if (endings.Count == 0) return SafePaths.NormalizeNewlines(edited);
        string normalized = SafePaths.NormalizeNewlines(edited);
        if (counts[endings[0]] == endings.Count) return normalized.Replace("\n", styles[endings[0]]);

        string before = SafePaths.NormalizeNewlines(original);
        int editedLines = normalized.AsSpan().Count('\n');
        var runs = new List<TopicEndingRun>();
        int oldStart = 0, newStart = 0, oldOffset = 0, newOffset = 0;
        foreach (var anchor in UnchangedTopicLines(before, normalized))
        {
            if (anchor.Original >= endings.Count || anchor.Edited >= editedLines) continue;
            int oldGap = anchor.Original - oldStart, newGap = anchor.Edited - newStart;
            int oldEnd = AdvanceTopicLines(before, oldOffset, oldGap);
            int newEnd = AdvanceTopicLines(normalized, newOffset, newGap);
            AddTopicEndingGap(runs, before, oldOffset, oldEnd, oldStart, oldGap,
                normalized, newOffset, newEnd, newGap);
            runs.Add(new(1, anchor.Original));
            oldStart = anchor.Original + 1;
            newStart = anchor.Edited + 1;
            oldOffset = AdvanceTopicLines(before, oldEnd, 1);
            newOffset = AdvanceTopicLines(normalized, newEnd, 1);
        }
        int oldRemainder = endings.Count - oldStart, newRemainder = editedLines - newStart;
        AddTopicEndingGap(runs, before, oldOffset, AdvanceTopicLines(before, oldOffset, oldRemainder), oldStart, oldRemainder,
            normalized, newOffset, AdvanceTopicLines(normalized, newOffset, newRemainder), newRemainder);
        byte preferred = endings[0];
        for (byte style = 0; style < styles.Length; style++)
            if (counts[style] > counts[preferred]) preferred = style;
        int line = 0, runIndex = 0, runStart = 0;
        var result = new StringBuilder(normalized.Length + counts[1]);
        foreach (char character in normalized)
        {
            if (character != '\n')
            {
                result.Append(character);
                continue;
            }
            while (line >= runStart + runs[runIndex].Count) runStart += runs[runIndex++].Count;
            var run = runs[runIndex];
            int originalLine = run.Original < 0 ? -1 : run.Original + line - runStart;
            byte style = originalLine >= 0 && originalLine < endings.Count ? endings[originalLine] : preferred;
            result.Append(styles[style]);
            line++;
        }
        return result.ToString();
    }

    private readonly record struct TopicEndingRun(int Count, int Original);
    private readonly record struct TopicLineAnchor(int Original, int Edited);

    private static int AdvanceTopicLines(string source, int offset, int count)
    {
        while (count-- > 0)
        {
            int newline = source.IndexOf('\n', offset);
            if (newline < 0) return source.Length;
            offset = newline + 1;
        }
        return offset;
    }

    private static void AddTopicEndingGap(List<TopicEndingRun> runs,
        string original, int oldOffset, int oldEnd, int oldLine, int oldCount,
        string edited, int newOffset, int newEnd, int newCount)
    {
        int prefix = 0;
        while (prefix < oldCount && prefix < newCount)
        {
            int oldLineEnd = original.IndexOf('\n', oldOffset);
            int newLineEnd = edited.IndexOf('\n', newOffset);
            if (!original.AsSpan(oldOffset, oldLineEnd - oldOffset).SequenceEqual(edited.AsSpan(newOffset, newLineEnd - newOffset))) break;
            prefix++;
            oldOffset = oldLineEnd + 1;
            newOffset = newLineEnd + 1;
        }
        int suffix = 0;
        while (suffix < oldCount - prefix && suffix < newCount - prefix)
        {
            int oldContentEnd = oldEnd - 1, newContentEnd = newEnd - 1;
            int oldLineStart = oldOffset + original.AsSpan(oldOffset, oldContentEnd - oldOffset).LastIndexOf('\n') + 1;
            int newLineStart = newOffset + edited.AsSpan(newOffset, newContentEnd - newOffset).LastIndexOf('\n') + 1;
            if (!original.AsSpan(oldLineStart, oldContentEnd - oldLineStart).SequenceEqual(edited.AsSpan(newLineStart, newContentEnd - newLineStart))) break;
            suffix++;
            oldEnd = oldLineStart;
            newEnd = newLineStart;
        }
        if (prefix > 0) runs.Add(new(prefix, oldLine));
        int oldMiddle = oldCount - prefix - suffix, newMiddle = newCount - prefix - suffix;
        if (newMiddle > 0) runs.Add(new(newMiddle, oldMiddle == newMiddle ? oldLine + prefix : -1));
        if (suffix > 0) runs.Add(new(suffix, oldLine + oldCount - suffix));
    }

    // Unique unchanged whole lines anchor mixed separators across several edits
    // and inserted/deleted lines. The increasing sequence keeps those anchors in
    // document order without a quadratic line-diff table.
    private static IReadOnlyList<TopicLineAnchor> UnchangedTopicLines(string original, string edited)
    {
        var originalLines = new Dictionary<string, int>(StringComparer.Ordinal);
        var originalLookup = originalLines.GetAlternateLookup<ReadOnlySpan<char>>();
        int start = 0, line = 0;
        for (int index = 0; index <= original.Length; index++)
        {
            if (index < original.Length && original[index] != '\n') continue;
            var text = original.AsSpan(start, index - start);
            if (!text.IsEmpty)
                originalLookup[text] = originalLookup.ContainsKey(text) ? -1 : line;
            start = index + 1;
            line++;
        }

        var editedPositions = new Dictionary<int, int>();
        start = 0;
        line = 0;
        for (int index = 0; index <= edited.Length; index++)
        {
            if (index < edited.Length && edited[index] != '\n') continue;
            var text = edited.AsSpan(start, index - start);
            if (originalLookup.TryGetValue(text, out int originalLine) && originalLine >= 0)
                editedPositions[originalLine] = editedPositions.ContainsKey(originalLine) ? -1 : line;
            start = index + 1;
            line++;
        }
        var candidates = editedPositions.Where(pair => pair.Value >= 0)
            .Select(pair => new TopicLineAnchor(pair.Key, pair.Value)).OrderBy(anchor => anchor.Edited).ToArray();
        if (candidates.Length == 0) return [];

        var previous = new int[candidates.Length];
        Array.Fill(previous, -1);
        var tails = new List<int>();
        for (int index = 0; index < candidates.Length; index++)
        {
            int low = 0, high = tails.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (candidates[tails[middle]].Original < candidates[index].Original) low = middle + 1;
                else high = middle;
            }
            if (low > 0) previous[index] = tails[low - 1];
            if (low == tails.Count) tails.Add(index);
            else tails[low] = index;
        }
        var anchors = new List<TopicLineAnchor>(tails.Count);
        for (int index = tails[^1]; index >= 0; index = previous[index]) anchors.Add(candidates[index]);
        anchors.Reverse();
        return anchors;
    }
}
