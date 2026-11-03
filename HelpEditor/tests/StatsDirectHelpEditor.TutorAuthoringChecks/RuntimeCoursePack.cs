using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace StatsDirect.UI.Learning;

/// <summary>Mac schema v1/v2, bounded text-only import. R examples are opened for review, never executed on import.</summary>
internal static class CoursePack
{
    internal static bool Identifier(string s) => Regex.IsMatch(s, @"^[A-Za-z0-9_-]{1,100}$");
    internal static bool HelpPath(string s) => s.Length == 0 || !s.Contains("..") && Regex.IsMatch(s, @"^[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*\.htm$");
    internal static bool Web(string s) => s.Length <= 2000 && Uri.TryCreate(s, UriKind.Absolute, out var url) && url.Scheme == "https" && url.UserInfo.Length == 0 && url.IsDefaultPort && url.Host.Contains('.') && !url.Host.EndsWith(".local") && !url.Host.EndsWith(".localhost");
    internal static bool Email(string s) => s.Length <= 254 && Regex.IsMatch(s, @"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?\.[A-Za-z]{2,63}$");
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static bool Text(JsonNode n, string key, int maximum, bool required = false) => n is JsonObject obj && (obj[key] == null ? !required : obj[key] is JsonValue v && v.TryGetValue<string>(out var text) && text.Length <= maximum && (!required || !string.IsNullOrWhiteSpace(text)));
    private static bool Unique(JsonArray a) => a.All(n => n is JsonObject) && a.Select(n => n.S("id")).Distinct().Count() == a.Count;
    internal static JsonObject Validate(JsonNode value)
    {
        Require(value is JsonObject, "Use a JSON course pack object.");
        var pack = (JsonObject)value; int schema = pack.I("schemaVersion");
        var documents = pack.A("documents"); var lessons = pack.A("lessons"); var questions = pack.A("questions");
        Require(schema is 1 or 2 && Text(pack, "title", 200, true) && pack["documents"] is JsonArray && (documents.Count > 0 || schema == 2 && lessons.Count > 0) && documents.Count <= 500 && Unique(documents), "A course pack needs a title, unique documents and at most 500 readable sections.");
        int total = 0;
        foreach (var doc in documents)
        {
            Require(Text(doc, "id", 120, true) && Text(doc, "title", 300, true) && Text(doc, "text", 1_000_000, true) && (doc["url"] == null || Web(doc.S("url"))), "A course document has invalid text, title or URL.");
            total += Encoding.UTF8.GetByteCount(doc.S("text"));
        }
        Require(total <= 1_000_000, "Course packs may contain at most 1 MB of readable text.");
        if (schema == 1)
        { Require(new[] { "provider", "lessons", "questions", "resourceURLs" }.All(k => pack[k] == null), "Use schemaVersion 2 for provider courses."); return pack; }
        var provider = pack["provider"];
        Require(Identifier(pack.S("id")) && Text(pack, "version", 80, true) && provider is JsonObject && Text(provider, "name", 200, true) && Email(provider.S("reviewEmail")) && Text(provider, "assessmentRequirements", 5000) && Text(provider, "cpdStatement", 2000) && (provider["assessmentType"] == null || new[] { "none", "cpd", "self" }.Contains(provider.S("assessmentType"))), "The provider course identity, contact or assessment settings are invalid.");
        Require(lessons.Count <= 100 && Unique(lessons) && questions.Count <= 300 && Unique(questions), "A provider course supports up to 100 uniquely named lessons and 300 questions.");
        ValidateLinks(pack, "resourceURLs", 30);
        foreach (var lesson in lessons)
        {
            Require(Identifier(lesson.S("id")) && Text(lesson, "title", 200, true) && Text(lesson, "objective", 3000, true) && Text(lesson, "summary", 30000, true) && Text(lesson, "steps", 20000) && Text(lesson, "challenge", 5000) && Text(lesson, "r", 50000) && HelpPath(lesson.S("help")), "A course lesson has invalid text or help links.");
            ValidateLinks(lesson, "readingURLs", 20);
            var columns = lesson.A("columns"); int cells = 0;
            Require(columns.Count <= 32, "An example supports at most 32 columns.");
            foreach (var column in columns)
            {
                Require(Text(column, "title", 200, true) && column is JsonObject obj && obj["values"] is JsonArray, "An example needs named numeric columns.");
                var values = column.A("values"); cells += values.Count;
                Require(values.All(v => v is JsonValue j && j.TryGetValue<double>(out double n) && double.IsFinite(n)), "An example has an invalid numeric value.");
            }
            Require(cells <= 20000, "An example supports at most 20,000 values.");
        }
        foreach (var q in questions)
        {
            var options = q.A("options");
            Require(Identifier(q.S("id")) && Text(q, "stem", 6000, true) && Text(q, "explanation", 10000, true) && HelpPath(q.S("help")) && options.Count is >= 2 and <= 8 && Unique(options) && options.Any(o => o.S("id") == q.S("correct")), "A question needs 2–8 choices, an answer key and an explanation.");
            Require(options.All(o => o.S("id").Length == 1 && "ABCDEFGH".Contains(o.S("id")) && Text(o, "text", 3000, true) && Text(o, "feedback", 6000)), "A question option is invalid.");
        }
        return pack;
    }
    private static void ValidateLinks(JsonNode row, string key, int count)
    { var urls = row.A(key); Require(urls.Count <= count && urls.All(v => v is JsonValue j && j.TryGetValue<string>(out var s) && Web(s)), "Use valid HTTPS reading links."); }
    internal static JsonObject Import(string[] paths)
    {
        Require(paths.Length is > 0 and <= 100, "Choose up to 100 course files.");
        var documents = new JsonArray(); string title = Path.GetFileNameWithoutExtension(paths[0]);
        foreach (var path in paths)
        {
            Require(new FileInfo(path).Length <= 16_000_000, "Each source file must be 16 MB or smaller.");
            string extension = Path.GetExtension(path).ToLowerInvariant(), prefix = "file" + Array.IndexOf(paths, path);
            if (extension == ".json")
            {
                var pack = Validate(LearningJson.Read(path, 2_000_000));
                if (pack.I("schemaVersion") == 2) { Require(paths.Length == 1, "Import a provider course on its own."); return pack; }
                if (paths.Length == 1) return pack;
                foreach (var doc in pack.A("documents")) { var copy = (JsonObject)doc.DeepClone(); copy["id"] = prefix + "-" + copy.S("id"); documents.Add(copy); }
            }
            else if (extension == ".pdf")
            {
                using var pdf = PdfDocument.Open(path);
                Require(pdf.NumberOfPages <= 500, "The PDF has more than 500 pages.");
                foreach (var page in pdf.GetPages())
                {
                    string text = ContentOrderTextExtractor.GetText(page);
                    if (!string.IsNullOrWhiteSpace(text)) documents.Add(new JsonObject { ["id"] = prefix + "-page" + page.Number, ["title"] = Path.GetFileName(path) + " · page " + page.Number, ["text"] = text });
                }
            }
            else
            {
                Require(extension is ".txt" or ".md", "Choose JSON, searchable PDF, UTF-8 text or Markdown.");
                documents.Add(new JsonObject { ["id"] = prefix, ["title"] = Path.GetFileName(path), ["text"] = File.ReadAllText(path, new UTF8Encoding(false, true)) });
            }
        }
        return Validate(new JsonObject { ["schemaVersion"] = 1, ["title"] = title, ["documents"] = documents });
    }
    internal static JsonArray Excerpts(JsonNode pack, string query)
    {
        var terms = Regex.Split(query.ToLowerInvariant(), @"\W+").Where(w => w.Length > 3).Distinct().ToArray();
        var parts = new List<(int score, JsonNode row)>();
        foreach (var doc in pack.A("documents"))
        {
            string text = doc.S("text");
            for (int offset = 0; offset < text.Length; offset += 1400)
            {
                string excerpt = text[offset..].Limit(1700), search = excerpt.ToLowerInvariant(), title = doc.S("title").ToLowerInvariant();
                int score = terms.Sum(t => (title.Contains(t) ? 4 : 0) + (search.Contains(t) ? 1 : 0));
                if (score > 0) parts.Add((score, new JsonObject { ["id"] = doc.S("id"), ["title"] = doc.S("title"), ["text"] = excerpt, ["url"] = doc.S("url") }));
            }
        }
        return new JsonArray(parts.OrderByDescending(p => p.score).Take(5).Select(p => p.row).ToArray());
    }
}
