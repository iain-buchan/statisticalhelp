using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StatsDirect.HelpEditor.Core;

public sealed record TutorValidationIssue(string Severity, string Path, string Message);
public sealed record TutorDeletedItem(string Id, string Kind, string Title, DateTimeOffset DeletedAtUtc);

/// <summary>Editable schema-2 provider course; exports import directly into StatsDirect Learning.</summary>
public sealed class TutorCourseDocument
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private string? diskHash;
    private string savedRuntimeHash;
    private string savedVersion;
    public JsonObject Content { get; }
    public string? FilePath { get; private set; }
    private TutorCourseDocument(JsonObject content, string? path, string? hash)
    { Content = content; FilePath = path; diskHash = hash; Shape(content); savedRuntimeHash = RuntimeHash(content); savedVersion = S(content, "version"); }

    public static TutorCourseDocument Open(string path)
    {
        path = Path.GetFullPath(path);
        if (new FileInfo(path).Length > 16_000_000) throw new InvalidDataException("Tutor authoring source files must be 16 MB or smaller; exported course packs remain limited to 2 MB.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 16_000_000) throw new InvalidDataException("Tutor authoring source files must be 16 MB or smaller; exported course packs remain limited to 2 MB.");
        int start = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        var content = JsonNode.Parse(new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start), documentOptions: new() { MaxDepth = 64 }) as JsonObject
            ?? throw new InvalidDataException("Open a JSON tutor course object.");
        return new(content, path, Digest(bytes));
    }

    public static TutorCourseDocument Create(JsonObject course) => new((JsonObject)course.DeepClone(), null, null);
    internal void AssertSavedSourceUnchanged()
    { if (FilePath != null && (!File.Exists(FilePath) || Digest(File.ReadAllBytes(FilePath)) != diskHash)) throw new IOException("The tutor source changed on disk after it was opened. Reload it before publishing; the open draft is retained."); }

    public IReadOnlyList<JsonObject> GetItems(string kind) => Items(kind).Cast<JsonObject>().Select(Clone).ToArray();
    public JsonObject NewLesson() => Add("lessons", new JsonObject
    {
        ["id"] = NewId("lessons", "lesson"), ["title"] = "New lesson", ["objective"] = "Write the learning objective.",
        ["summary"] = "Write the lesson explanation.", ["steps"] = "", ["challenge"] = "Write an original exercise for your students.",
        ["r"] = "", ["help"] = "", ["columns"] = new JsonArray(), ["readingURLs"] = new JsonArray(), ["review"] = DraftReview()
    });
    public JsonObject NewQuestion() => Add("questions", new JsonObject
    {
        ["id"] = NewId("questions", "question"), ["version"] = 1, ["track"] = "foundation", ["topic"] = "New question", ["objective"] = "Write the learning objective.",
        ["stem"] = "Write an original single-best-answer question.", ["options"] = new JsonArray(
            new JsonObject { ["id"] = "A", ["text"] = "First answer", ["feedback"] = "Explain this answer." },
            new JsonObject { ["id"] = "B", ["text"] = "Second answer", ["feedback"] = "Explain this answer." }),
        ["correct"] = "A", ["explanation"] = "Explain the correct answer and the reasoning.", ["hint"] = "", ["lesson"] = "", ["help"] = "", ["review"] = DraftReview()
    });
    public JsonObject NewDocument() => Add("documents", new JsonObject { ["id"] = NewId("documents", "source"), ["title"] = "New source extract", ["text"] = "Write an attributed source summary or course note." });
    public JsonObject AddRecord(string kind, JsonObject record)
    {
        var copy = Clone(record); string id = S(copy, "id");
        if (string.IsNullOrWhiteSpace(id) || Items(kind).Any(row => S(row, "id") == id)) throw new InvalidDataException("Choose a new, unique tutor item ID.");
        var draft = copy["review"] is JsonObject suppliedReview ? Clone(suppliedReview) : new JsonObject();
        draft.Remove("reviewer"); draft.Remove("contentSha256"); draft["status"] = "Draft — author review required"; draft["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd");
        copy["review"] = draft; InvalidateVerification(copy); InvalidateSourceDates(null, copy);
        return Add(kind, copy);
    }
    public JsonObject Duplicate(string kind, string id)
    {
        var copy = Clone(Find(kind, id)); copy["id"] = NewId(kind, id + "-copy");
        if (kind == "lessons") copy["title"] = S(copy, "title") + " (copy)";
        copy["review"] = DraftReview();
        return Add(kind, copy);
    }
    private JsonObject Add(string kind, JsonObject value) { Items(kind).Add(value); MarkCourseDraft(); return Clone(value); }

    public void Update(string kind, string id, JsonObject value)
    {
        var items = Items(kind); var original = Find(kind, id); var copy = Clone(value);
        if (S(copy, "id") != id) throw new InvalidDataException("An existing item ID cannot be changed. Duplicate it to create a different item.");
        if (JsonNode.DeepEquals(original, copy)) return;
        bool learnerContentChanged = !JsonNode.DeepEquals(WithoutReview(original), WithoutReview(copy));
        if (learnerContentChanged && original["review"] is JsonObject oldReview)
        {
            var local = RequireAuthoring(copy);
            local["previousReview"] = oldReview.DeepClone();
        }
        if (learnerContentChanged)
        {
            if (kind == "lessons" && S(original, "challenge") is { Length: > 0 } oldChallenge && oldChallenge != S(copy, "challenge"))
            {
                var previous = copy["previousChallenges"] as JsonArray; if (previous == null) copy["previousChallenges"] = previous = new();
                if (!previous.Any(value => value is JsonValue text && text.TryGetValue<string>(out var challenge) && challenge == oldChallenge)) previous.Add(oldChallenge);
            }
            var draft = copy["review"] is JsonObject suppliedReview ? Clone(suppliedReview) : new JsonObject();
            draft.Remove("reviewer"); draft.Remove("contentSha256"); draft["status"] = "Draft — author review required"; draft["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd");
            copy["review"] = draft;
            InvalidateVerification(copy);
            InvalidateSourceDates(original, copy);
            if (kind == "questions")
            {
                if (I(original, "version") == int.MaxValue) throw new InvalidDataException("This question's version has reached the supported limit. Duplicate it with a new ID before making another revision.");
                copy["version"] = Math.Max(1, I(original, "version")) + 1;
            }
            if (copy.ContainsKey("contentVersion")) copy["contentVersion"] = DateTime.UtcNow.ToString("yyyy-MM-dd");
        }
        items[items.IndexOf(original)] = copy;
        if (learnerContentChanged) MarkCourseDraft();
    }

    /// <summary>Record an author's review separately from an edit to the teaching material.</summary>
    public void MarkReviewed(string kind, string id, string reviewer, string notes)
    {
        if (string.IsNullOrWhiteSpace(reviewer) || reviewer.Length > 200 || notes.Length > 10000) throw new ArgumentException("Provide a reviewer name and review notes within the allowed lengths.");
        var value = Clone(Find(kind, id));
        if (value["review"] != null)
        {
            var local = RequireAuthoring(value);
            local["previousReview"] = value["review"]!.DeepClone();
        }
        var review = value["review"] as JsonObject; if (review == null) value["review"] = review = new();
        review["status"] = "Reviewed by course author"; review["reviewer"] = reviewer.Trim(); review["notes"] = notes; review["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd");
        review["contentSha256"] = TutorContentFingerprint.Record(value);
        Update(kind, id, value);
    }

    public void Move(string kind, string id, int delta)
    {
        var items = Items(kind); var row = Find(kind, id); int from = items.IndexOf(row), to = Math.Clamp(from + delta, 0, items.Count - 1);
        if (from == to) return;
        items.RemoveAt(from); items.Insert(to, row); MarkCourseDraft();
    }
    public string Delete(string kind, string id)
    {
        var items = Items(kind); var row = Find(kind, id); string deletionId = Guid.NewGuid().ToString("N");
        var local = Authoring(); var deleted = RequireHistory(local, "deletedItems");
        deleted.Add(new JsonObject { ["id"] = deletionId, ["kind"] = kind, ["index"] = items.IndexOf(row), ["deletedAtUtc"] = DateTimeOffset.UtcNow.ToString("o"), ["record"] = row.DeepClone() });
        items.Remove(row); MarkCourseDraft(); return deletionId;
    }
    public IReadOnlyList<TutorDeletedItem> DeletedItems() => (ReadAuthoring(Content)?["deletedItems"] as JsonArray ?? [])
        .OfType<JsonObject>().Select(row => new TutorDeletedItem(S(row, "id"), S(row, "kind"), S(row["record"], "title", S(row["record"], "topic", S(row["record"], "id"))), DateTimeOffset.TryParse(S(row, "deletedAtUtc"), out var date) ? date : DateTimeOffset.MinValue)).ToArray();
    public void Restore(string deletionId)
    {
        var deleted = ReadAuthoring(Content)?["deletedItems"] as JsonArray ?? throw new InvalidOperationException("There are no deleted tutor items.");
        var row = deleted.OfType<JsonObject>().SingleOrDefault(value => S(value, "id") == deletionId) ?? throw new InvalidOperationException("The deleted item is unavailable.");
        string kind = S(row, "kind"); var value = row["record"] as JsonObject ?? throw new InvalidDataException("The recovery item is damaged.");
        if (Items(kind).Any(item => S(item, "id") == S(value, "id"))) throw new InvalidOperationException("A current item already uses that ID. Keep it or duplicate it before restoring.");
        Items(kind).Insert(Math.Clamp(I(row, "index"), 0, Items(kind).Count), value.DeepClone()); deleted.Remove(row); MarkCourseDraft();
    }

    public void Save(string? path = null)
    {
        Shape(Content);
        string destination = Path.GetFullPath(path ?? FilePath ?? throw new InvalidOperationException("Choose a filename for the tutor course."));
        string lockPath = OperatingSystem.IsWindows() ? destination.ToUpperInvariant() : destination;
        using var saveMutex = new Mutex(false, "StatsDirectHelpTutorSave-" + Digest(Encoding.UTF8.GetBytes(lockPath)));
        bool acquired = false;
        try
        {
            try { acquired = saveMutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Another editor is saving this tutor source. Try again; the open draft is retained.");
            SaveLocked(destination);
        }
        finally { if (acquired) saveMutex.ReleaseMutex(); }
    }
    private void SaveLocked(string destination)
    {
        bool same = FilePath != null && destination.Equals(FilePath, StringComparison.OrdinalIgnoreCase);
        if (same && (!File.Exists(destination) || Digest(File.ReadAllBytes(destination)) != diskHash)) throw new IOException("The tutor course changed on disk. Reload it before saving; the open draft has been retained.");
        if (!same && File.Exists(destination)) throw new IOException("That course file already exists. Choose a new filename or open it before editing.");
        var candidate = Clone(Content);
        if (RuntimeHash(candidate) != savedRuntimeHash && S(candidate, "version") == savedVersion) candidate["version"] = NextVersion(savedVersion);
        byte[] bytes = Encode(candidate, 16_000_000);
        if (same && Digest(bytes) == diskHash) return;
        AtomicWrite(destination, bytes, same ? diskHash : null, requireAbsent: !same);
        Content.Clear(); foreach (var pair in candidate) Content[pair.Key] = pair.Value?.DeepClone();
        FilePath = destination; diskHash = Digest(bytes); savedRuntimeHash = RuntimeHash(Content); savedVersion = S(Content, "version");
    }

    public void Export(string path)
    {
        var errors = Validate().Where(issue => issue.Severity == "Error").ToArray();
        if (errors.Length > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(issue => issue.Path + ": " + issue.Message)));
        string destination = Path.GetFullPath(path);
        if (FilePath != null && destination.Equals(FilePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Export to a separate course-pack filename so your authoring and deleted-item recovery data are retained.");
        var runtime = RuntimeContent(Content);
        if (RuntimeHash(Content) != savedRuntimeHash)
        {
            // An author may export several different unsaved drafts. A content
            // suffix keeps those learner question snapshots distinct without
            // writing the authoring source or inventing the same next revision.
            var identity = Clone(runtime); identity.Remove("version");
            string suffix = "-d" + Digest(Encoding.UTF8.GetBytes(identity.ToJsonString())).ToLowerInvariant()[..12];
            string version = Regex.Replace(S(runtime, "version"), "-d[0-9a-f]{12}$", "", RegexOptions.IgnoreCase);
            runtime["version"] = version[..Math.Min(80 - suffix.Length, version.Length)] + suffix;
        }
        AtomicWrite(destination, Encode(runtime));
    }

    public IReadOnlyList<TutorValidationIssue> Validate()
    {
        var issues = new List<TutorValidationIssue>();
        void Error(string path, bool valid, string message) { if (!valid) issues.Add(new("Error", path, message)); }
        void Warning(string path, bool valid, string message) { if (!valid) issues.Add(new("Warning", path, message)); }
        Error("course", I(Content, "schemaVersion") == 2, "Use provider course schema version 2.");
        Error("id", Identifier(S(Content, "id")), "Use 1–100 letters, numbers, underscores or hyphens.");
        Error("title", Text(Content, "title", 200, true), "A title of at most 200 characters is required.");
        Error("version", Text(Content, "version", 80, true), "A course version of at most 80 characters is required.");
        var provider = Content["provider"] as JsonObject;
        Error("provider", provider != null && Text(provider, "name", 200, true) && Email(S(provider, "reviewEmail")), "Provide a course provider name and valid review email.");
        if (provider != null)
        {
            Error("provider.assessmentRequirements", Text(provider, "assessmentRequirements", 5000), "Assessment requirements must be at most 5,000 characters.");
            Error("provider.cpdStatement", Text(provider, "cpdStatement", 2000), "The CPD statement must be at most 2,000 characters.");
            Error("provider.assessmentType", provider["assessmentType"] == null || new[] { "none", "cpd", "self" }.Contains(S(provider, "assessmentType")), "Choose none, cpd or self.");
        }
        var lessons = Content["lessons"] as JsonArray ?? []; var questions = Content["questions"] as JsonArray ?? []; var documents = Content["documents"] as JsonArray ?? [];
        Error("lessons", Content["lessons"] is JsonArray && lessons.Count <= 100 && Unique(lessons), "Use up to 100 uniquely identified lessons.");
        Error("questions", Content["questions"] is JsonArray && questions.Count <= 300 && Unique(questions), "Use up to 300 uniquely identified questions.");
        Error("documents", Content["documents"] is JsonArray && documents.Count <= 500 && Unique(documents) && (documents.Count > 0 || lessons.Count > 0), "Provide at least one lesson or readable document; document IDs must be unique.");
        int totalText = 0;
        foreach (var doc in documents)
        {
            Error("documents." + S(doc, "id"), Text(doc, "id", 120, true) && Text(doc, "title", 300, true) && Text(doc, "text", 1_000_000, true) && (doc?["url"] == null || Web(S(doc, "url"))), "A source document needs an ID, title, text and optional HTTPS URL.");
            totalText += Encoding.UTF8.GetByteCount(S(doc, "text"));
        }
        Error("documents", totalText <= 1_000_000, "Readable source text must total at most 1 MB.");
        CheckLinks(Content, "resourceURLs", 30, "course", Error);
        foreach (var lesson in lessons)
        {
            string path = "lessons." + S(lesson, "id");
            Error(path + ".authoring", lesson?["authoring"] == null || lesson["authoring"] is JsonObject, "Local authoring metadata must be an object; correct it in JSON source before editing this record.");
            Error(path, Identifier(S(lesson, "id")) && Text(lesson, "title", 200, true) && Text(lesson, "objective", 3000, true) && Text(lesson, "summary", 30000, true), "A lesson needs an ID, title, objective and summary within runtime limits.");
            Error(path, Text(lesson, "steps", 20000) && Text(lesson, "challenge", 5000) && Text(lesson, "r", 50000) && HelpPath(S(lesson, "help")), "Check the steps, exercise, R code lengths and relative help link.");
            CheckLinks(lesson, "readingURLs", 20, path, Error);
            var columns = lesson?["columns"] as JsonArray ?? []; int cells = 0;
            Error(path + ".columns", lesson?["columns"] == null || lesson["columns"] is JsonArray, "Example columns must be a JSON array.");
            Error(path + ".columns", columns.Count <= 32, "Examples support up to 32 columns.");
            foreach (var column in columns)
            {
                if (column is not JsonObject) { Error(path + ".columns", false, "Every example column must be an object with title and values."); continue; }
                var values = column?["values"] as JsonArray; cells += values?.Count ?? 0;
                Error(path + ".columns", Text(column, "title", 200, true) && values != null && values.All(value => value is JsonValue number && number.TryGetValue<double>(out double n) && double.IsFinite(n)), "Example columns require a title and finite numeric values.");
            }
            Error(path + ".columns", cells <= 20000, "Examples support at most 20,000 values.");
            Warning(path + ".review", Reviewed(lesson), "Draft or edited lesson: review its statistical reasoning and sources before student use.");
        }
        foreach (var question in questions)
        {
            string path = "questions." + S(question, "id"); var options = question?["options"] as JsonArray ?? [];
            Error(path + ".authoring", question?["authoring"] == null || question["authoring"] is JsonObject, "Local authoring metadata must be an object; correct it in JSON source before editing this record.");
            Error(path, Identifier(S(question, "id")) && Text(question, "stem", 6000, true) && Text(question, "explanation", 10000, true) && HelpPath(S(question, "help")), "A question needs an ID, stem, explanation and valid optional help link.");
            Error(path + ".options", options.Count is >= 2 and <= 8 && Unique(options) && options.Any(option => S(option, "id") == S(question, "correct")), "Use 2–8 unique choices and select exactly one existing correct-answer ID.");
            Error(path + ".options", options.All(option => S(option, "id").Length == 1 && "ABCDEFGH".Contains(S(option, "id")) && Text(option, "text", 3000, true) && Text(option, "feedback", 6000)), "Choices use IDs A–H, nonempty text and optional feedback within runtime limits.");
            Warning(path + ".options", options.All(option => !string.IsNullOrWhiteSpace(S(option, "feedback"))), "Add feedback explaining each distractor as well as the correct choice.");
            Warning(path + ".review", Reviewed(question), "Draft or edited question: independently check the key, distractors, numerical results and sources before student use.");
        }
        Warning("authoring", !string.IsNullOrWhiteSpace(S(Content["authoring"], "audience")), "Describe your learners and intended level.");
        try { Error("course", Encode(RuntimeContent(Content)).Length <= 2_000_000, "The exported course exceeds the 2 MB runtime limit."); } catch (InvalidDataException error) { Error("course", false, error.Message); }
        return issues;
    }

    private static void CheckLinks(JsonNode? value, string key, int maximum, string path, Action<string, bool, string> error)
    { var links = value?[key] as JsonArray ?? []; error(path + "." + key, links.Count <= maximum && links.All(link => link is JsonValue v && v.TryGetValue<string>(out var url) && Web(url)), "Use HTTPS reading URLs within the runtime limit."); }
    private static bool Reviewed(JsonNode? value) => S(value?["review"], "status").Trim().StartsWith("Reviewed", StringComparison.OrdinalIgnoreCase) && !S(value?["review"], "status").Contains("draft", StringComparison.OrdinalIgnoreCase);
    private static JsonObject DraftReview() => new() { ["status"] = "Draft — author review required", ["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd") };
    private void MarkCourseDraft() { InvalidateVerification(Content); var authoring = Authoring(); authoring["reviewStatus"] = "Draft — content changed; recheck answers and evidence"; authoring["editedAtUtc"] = DateTimeOffset.UtcNow.ToString("o"); }
    private JsonObject Authoring() => RequireAuthoring(Content);
    private static JsonObject? ReadAuthoring(JsonObject record)
    { if (record["authoring"] == null) return null; return record["authoring"] as JsonObject ?? throw new InvalidDataException("Local authoring metadata must be an object. Its original value was retained; correct it in JSON source before continuing."); }
    private static JsonObject RequireAuthoring(JsonObject record)
    { var value = ReadAuthoring(record); if (value == null) record["authoring"] = value = new(); return value; }
    private static JsonArray RequireHistory(JsonObject metadata, string key)
    { if (metadata[key] == null) { var created = new JsonArray(); metadata[key] = created; return created; } return metadata[key] as JsonArray ?? throw new InvalidDataException("The local " + key + " recovery history is damaged. It was retained; correct it before adding further recovery records."); }
    private JsonArray Items(string kind) => kind is "lessons" or "questions" or "documents" ? Content[kind] as JsonArray ?? throw new InvalidDataException("The course is missing its " + kind + " list.") : throw new ArgumentException("Use lessons, questions or documents.", nameof(kind));
    private JsonObject Find(string kind, string id) => Items(kind).OfType<JsonObject>().SingleOrDefault(row => S(row, "id") == id) ?? throw new InvalidOperationException("The tutor item is unavailable: " + id);
    private string NewId(string kind, string prefix) { prefix = Regex.Replace(prefix, "[^A-Za-z0-9_-]", "-"); prefix = prefix[..Math.Min(85, prefix.Length)]; if (prefix.Length == 0) prefix = "item"; var ids = Items(kind).Select(row => S(row, "id")).ToHashSet(); for (int i = 1; ; i++) { string id = prefix + "-" + i; if (!ids.Contains(id)) return id; } }
    private static void Shape(JsonObject content) { if (I(content, "schemaVersion") != 2 || content["lessons"] is not JsonArray lessons || content["questions"] is not JsonArray questions || content["documents"] is not JsonArray documents || content["provider"] is not JsonObject || !Unique(lessons) || !Unique(questions) || !Unique(documents)) throw new InvalidDataException("Use a provider course (schemaVersion 2) with provider, documents, lessons and questions. Item IDs must be unique."); _ = ReadAuthoring(content); }
    private static bool Unique(JsonArray rows) => rows.All(row => row is JsonObject) && rows.Select(row => S(row, "id")).Distinct(StringComparer.Ordinal).Count() == rows.Count;
    private static bool Identifier(string value) => Regex.IsMatch(value, "\\A[A-Za-z0-9_-]{1,100}\\z");
    private static bool HelpPath(string value) => value.Length == 0 || !value.Contains("..") && Regex.IsMatch(value, "\\A[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*\\.htm\\z");
    private static bool Web(string? value) => value != null && value.Length <= 2000 && Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" && url.UserInfo.Length == 0 && url.IsDefaultPort && url.Host.Contains('.') && !url.Host.EndsWith(".local") && !url.Host.EndsWith(".localhost");
    private static bool Email(string value) => value.Length <= 254 && Regex.IsMatch(value, "\\A[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?\\.[A-Za-z]{2,63}\\z");
    private static bool Text(JsonNode? node, string key, int maximum, bool required = false) => node is JsonObject row && (row[key] == null ? !required : row[key] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length <= maximum && (!required || !string.IsNullOrWhiteSpace(text)));
    private static string S(JsonNode? value, string key, string fallback = "") => value is JsonObject row && row[key] is JsonValue item && item.TryGetValue<string>(out string? text) ? text ?? fallback : fallback;
    private static int I(JsonNode? value, string key) => value is JsonObject row && row[key] is JsonValue item && item.TryGetValue<int>(out int number) ? number : 0;
    private static JsonObject Clone(JsonObject value) => (JsonObject)value.DeepClone();
    private static JsonObject WithoutReview(JsonObject value) { var copy = Clone(value); copy.Remove("review"); copy.Remove("verification"); copy.Remove("authoring"); return copy; }
    private static void InvalidateVerification(JsonObject value)
    {
        if (value["verification"] is not JsonObject verification || S(verification, "status") == "not-rechecked-after-edit") return;
        var local = RequireAuthoring(value);
        local["previousVerification"] = verification.DeepClone();
        verification["status"] = "not-rechecked-after-edit";
        verification["note"] = "The teaching content changed after the recorded checks. Rerun and review the reproducible checks; none have been run by this editor.";
        foreach (string field in new[] { "checkedAt", "checkedAtUtc", "verifiedAt", "verifiedAtUtc" }) verification.Remove(field);
        if (verification.ContainsKey("passed")) verification["passed"] = false;
    }
    private static void InvalidateSourceDates(JsonObject? original, JsonObject edited)
    {
        var previous = (original?["sources"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
        var sources = (edited["sources"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
        for (int index = 0; index < sources.Length; index++)
        {
            var source = sources[index]; if (source["checkedAt"] == null) continue;
            string id = S(source, "id");
            var before = id.Length > 0 ? previous.FirstOrDefault(row => S(row, "id") == id)
                : previous.FirstOrDefault(row => S(row, "url") == S(source, "url") && S(row, "title") == S(source, "title")) ?? previous.ElementAtOrDefault(index);
            if (before != null && new[] { "url", "title", "scope" }.All(key => S(before, key) == S(source, key))) continue;
            var local = RequireAuthoring(edited);
            var history = RequireHistory(local, "previousSourceChecks");
            history.Add(new JsonObject { ["sourceId"] = id, ["previousSource"] = (before ?? source).DeepClone(), ["reason"] = "Source URL, title or scope changed; the previous check date does not verify the revised reference." });
            source.Remove("checkedAt"); source["verificationStatus"] = "Author source check required after reference edit";
        }
    }
    private static string NextVersion(string version)
    {
        var match = Regex.Match(version, "\\A(.*)-r([0-9]+)\\z");
        string prefix = match.Success ? match.Groups[1].Value : version;
        var number = match.Success ? BigInteger.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) + BigInteger.One : new BigInteger(2);
        string suffix = "-r" + number.ToString(CultureInfo.InvariantCulture);
        if (suffix.Length > 80) throw new InvalidDataException("The course revision counter has reached its length limit. Choose a new course version before saving; your draft is retained.");
        return prefix[..Math.Min(80 - suffix.Length, prefix.Length)] + suffix;
    }
    private static JsonObject RuntimeContent(JsonObject source) { var copy = Clone(source); void Strip(JsonNode? node) { if (node is JsonObject row) { row.Remove("authoring"); foreach (var pair in row.ToArray()) Strip(pair.Value); } else if (node is JsonArray array) foreach (var item in array) Strip(item); } Strip(copy); return copy; }
    private static string RuntimeHash(JsonObject source) => Digest(Encoding.UTF8.GetBytes(RuntimeContent(source).ToJsonString()));
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] Encode(JsonObject source, int maximum = 2_000_000) { var bytes = Encoding.UTF8.GetBytes(source.ToJsonString(Pretty) + Environment.NewLine); if (bytes.Length > maximum) throw new InvalidDataException(maximum == 2_000_000 ? "The exported tutor course exceeds the 2 MB runtime limit." : "The tutor authoring source exceeds the 16 MB limit; no data was truncated."); return bytes; }
    private static void AtomicWrite(string path, byte[] bytes, string? expectedHash = null, bool requireAbsent = false)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current)) { FileSystemInfo item = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current); if (item.LinkTarget != null) throw new IOException("Choose a tutor file without linked folders or files."); }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); string temporary = Path.Combine(Path.GetDirectoryName(path)!, ".tutor-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            if (expectedHash != null && (!File.Exists(path) || Digest(File.ReadAllBytes(path)) != expectedHash)) throw new IOException("The tutor source changed while saving. The newer file and your open draft are retained.");
            if (requireAbsent && File.Exists(path)) throw new IOException("Another editor created that tutor source. Choose a different filename; your open draft is retained.");
            File.Move(temporary, path, overwrite: !requireAbsent);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
