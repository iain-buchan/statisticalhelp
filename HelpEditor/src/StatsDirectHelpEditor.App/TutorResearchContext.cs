using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

/// <summary>Frozen, bounded author-supplied tutor/help context. It cannot read arbitrary files.</summary>
public sealed class TutorResearchContext
{
    private readonly Dictionary<string, string> items;
    private readonly HelpPolishContext? help;
    private readonly string metadataJson;
    public string CatalogJson { get; }
    public int TutorItemCount => items.Count;
    public int HelpPageCount => help?.PageCount ?? 0;
    private TutorResearchContext(JsonObject course, HelpPolishContext? help)
    {
        this.help = help;
        items = new(StringComparer.Ordinal);
        var catalog = new JsonArray();
        foreach (string collection in new[] { "lessons", "questions", "documents" })
        {
            if (course[collection] is not JsonArray records) continue;
            for (int index = 0; index < records.Count; index++)
            {
                if (records[index] is not JsonObject value) throw new ArgumentException("Tutor context must contain object records.");
                string key = collection + "/" + index, json = value.ToJsonString();
                items.Add(key, json);
                catalog.Add(new JsonObject { ["path"] = key, ["id"] = Text(value, "id"), ["title"] = Text(value, "title") is { Length: > 0 } title ? title : Text(value, "topic"), ["characters"] = json.Length });
            }
        }
        var metadata = new JsonObject();
        foreach (string key in new[] { "id", "title", "version", "description", "audience", "learningObjectives", "prerequisites" })
            if (course.TryGetPropertyValue(key, out var value)) metadata[key] = value?.DeepClone();
        if (course["authoring"] is JsonObject authoring)
            foreach (string key in new[] { "audience", "learningObjectives", "prerequisites", "teachingGoals" })
                if (authoring.TryGetPropertyValue(key, out var value)) metadata["authoring_" + key] = value?.DeepClone();
        if (course["provider"] is JsonObject provider) metadata["providerName"] = Text(provider, "name");
        metadataJson = metadata.ToJsonString();
        if (metadataJson.Length > 100000) throw new ArgumentException("The course objectives are too large for one research context.");
        CatalogJson = new JsonObject { ["courseTitle"] = Text(course, "title"), ["courseMetadataAvailable"] = true, ["tutorItems"] = catalog,
            ["helpCatalog"] = help == null ? null : JsonNode.Parse(help.CatalogText) }.ToJsonString();
        if (CatalogJson.Length > 1_000_000) throw new ArgumentException("The tutor catalog is too large for one research session.");
    }
    public static TutorResearchContext Create(JsonObject course, HelpPolishContext? help = null)
    { ArgumentNullException.ThrowIfNull(course); return new((JsonObject)course.DeepClone(), help); }

    internal string Execute(string tool, JsonObject arguments)
    {
        if (tool is "search_help" or "read_help_page")
            return PolishChatGptClient.ExecuteContextTool(help ?? throw new ArgumentException("Help context is not available in this session."), tool, arguments);
        if (tool == "read_course_metadata")
        {
            RequireFields(arguments, "offset", "length");
            int offset = RequiredInt(arguments, "offset", 0, metadataJson.Length), length = RequiredInt(arguments, "length", 1, 16000);
            int end = (int)Math.Min(metadataJson.Length, (long)offset + length);
            if (offset > 0 && offset < metadataJson.Length && char.IsLowSurrogate(metadataJson[offset]) && char.IsHighSurrogate(metadataJson[offset - 1])) offset--;
            if (end > offset && end < metadataJson.Length && char.IsHighSurrogate(metadataJson[end - 1])) end--;
            if (end == offset && end + 1 < metadataJson.Length) end += 2;
            return JsonSerializer.Serialize(new { offset, text = metadataJson[offset..end], totalCharacters = metadataJson.Length, nextOffset = end < metadataJson.Length ? (int?)end : null });
        }
        if (tool == "read_tutor_item")
        {
            RequireFields(arguments, "path", "offset", "length");
            string path = RequiredText(arguments, "path", 1000);
            if (!items.TryGetValue(path, out string? content)) throw new ArgumentException("Choose a path from the tutor catalog.");
            int offset = RequiredInt(arguments, "offset", 0, content.Length), length = RequiredInt(arguments, "length", 1, 16000);
            int end = Math.Min(content.Length, offset + length);
            if (offset > 0 && offset < content.Length && char.IsLowSurrogate(content[offset]) && char.IsHighSurrogate(content[offset - 1])) offset--;
            if (end > offset && end < content.Length && char.IsHighSurrogate(content[end - 1])) end--;
            if (end == offset && end + 1 < content.Length) end += 2;
            return JsonSerializer.Serialize(new { path, offset, text = content[offset..end], totalCharacters = content.Length, nextOffset = end < content.Length ? (int?)end : null });
        }
        if (tool == "search_tutor")
        {
            RequireFields(arguments, "query", "maxResults");
            string query = RequiredText(arguments, "query", 1000);
            int maximum = RequiredInt(arguments, "maxResults", 1, 30);
            string[] words = Regex.Matches(query, @"[\p{L}\p{Nd}]+").Select(match => match.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
            if (words.Length == 0) throw new ArgumentException("Use words or numbers to search tutor content.");
            var hits = items.Select(pair => new { path = pair.Key, text = pair.Value, offsets = words.Select(word => pair.Value.IndexOf(word, StringComparison.OrdinalIgnoreCase)).Where(index => index >= 0).ToArray() })
                .Select(item => new { item.path, item.text, score = item.offsets.Length, offset = Math.Max(0, (item.offsets.Length == 0 ? 0 : item.offsets.Min()) - 100) })
                .Where(item => item.score > 0).OrderByDescending(item => item.score).ThenBy(item => item.path, StringComparer.Ordinal).ToArray();
            return JsonSerializer.Serialize(new { totalMatches = hits.Length, results = hits.Take(maximum).Select(item => new { item.path, item.score, snippetOffset = item.offset, snippet = item.text.Substring(item.offset, Math.Min(700, item.text.Length - item.offset)), totalCharacters = item.text.Length }) });
        }
        throw new ArgumentException("Only read-only tutor and help context tools are available.");
    }
    internal static string Text(JsonNode? value, string key) => value is JsonObject row && row[key] is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : "";
    internal static string Fingerprint(JsonObject value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToJsonString())));
    private static string RequiredText(JsonObject value, string key, int max) => value[key] is JsonValue scalar && scalar.TryGetValue<string>(out string? text) && !string.IsNullOrWhiteSpace(text) && text.Length <= max ? text : throw new ArgumentException("Invalid context arguments.");
    private static int RequiredInt(JsonObject value, string key, int min, int max) => value[key] is JsonValue scalar && scalar.TryGetValue<int>(out int number) && number >= min && number <= max ? number : throw new ArgumentException("Invalid context arguments.");
    private static void RequireFields(JsonObject value, params string[] fields)
    { if (value.Count != fields.Length || value.Any(pair => !fields.Contains(pair.Key))) throw new ArgumentException("Invalid context arguments."); }
}

public sealed record TutorResearchSource(string Title, string Url, string Relevance, bool ObservedOpened, DateTimeOffset? AccessedAtUtc = null);
public sealed record TutorResearchActivity(string Action, string Query, string Url, DateTimeOffset? AtUtc = null);
public sealed record TutorResearchRequest(string Kind, string Mode, JsonObject SelectedRecord, string Instructions, string Audience,
    bool UseWebResearch, string PreviousDiscussion = "");
public sealed record TutorResearchProposal(string Kind, string Mode, string BeforeJson, JsonObject ProposedRecord, string Summary,
    string StatisticalNotes, string RValidationCode, IReadOnlyList<TutorResearchSource> Sources,
    IReadOnlyList<TutorResearchActivity> SearchActivity, bool WebResearchRequested)
{
    public bool WebResearchObserved => SearchActivity.Count > 0;
    public string EvidenceNote => "AI-assisted draft and statistical review; numerical checks and source claims still require author review. Opened URLs show browsing activity, not independent verification." +
        (WebResearchRequested && !WebResearchObserved ? " Internet research was requested, but this reply contains no observed web-search activity. Check all suggested sources." : "");
}

public interface ITutorResearchChatGptClient : IPolishChatGptClient
{
    Task<TutorResearchProposal> ResearchTutorAsync(TutorResearchContext context, TutorResearchRequest request, CancellationToken cancellationToken = default);
}
