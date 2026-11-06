using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StatsDirect.HelpEditor.Core;

public sealed record TutorBuiltinPublishResult(string OutputDirectory, int LessonCount, int QuestionCount,
    string BankVersion, string ManifestSha256, IReadOnlyList<TutorValidationIssue> ReviewIssues, IReadOnlyList<string> Warnings);
public sealed record TutorBuiltinInspection(bool CanPublish, int LessonCount, int QuestionCount,
    string? BankVersion, IReadOnlyList<TutorValidationIssue> ReviewIssues);

/// <summary>Publish reviewed source inputs for the normal StatsDirect built-in tutor build.</summary>
public static class TutorBuiltinPublisher
{
    private const string Format = "statsdirect-builtin-tutor";
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private static readonly string[] OutputFiles = ["lessons.json", "questions.json", "tracks.json", "bank.mjs", "manifest.json", "README.md"];

    /// <summary>Capture only an original, independently identified baseline, never an edited working draft.</summary>
    public static JsonObject CreateBaseline(JsonObject originalCourse, IEnumerable<string>? operationIds = null, IEnumerable<string>? helpPaths = null)
    {
        var lessons = Array(originalCourse, "lessons"); var questions = Array(originalCourse, "questions");
        var tracks = Tracks(originalCourse); var lessonHashes = new JsonObject(); var questionHashes = new JsonObject();
        foreach (var lesson in lessons.OfType<JsonObject>()) lessonHashes[S(lesson, "id")] = RecordHash(lesson);
        foreach (var question in questions.OfType<JsonObject>()) questionHashes[S(question, "id")] = RecordHash(question);
        var all = lessons.Concat(questions).OfType<JsonObject>().ToArray();
        return new JsonObject
        {
            ["schemaVersion"] = 1, ["recordHashAlgorithm"] = TutorContentFingerprint.Algorithm, ["bankVersion"] = S(originalCourse["authoring"], "sourceBankVersion", S(originalCourse, "version")),
            ["bodyHash"] = BodyHash(lessons, questions, tracks), ["lessonHashes"] = lessonHashes, ["questionHashes"] = questionHashes,
            ["trackIds"] = Strings(tracks.Select(pair => pair.Key)),
            ["operationIds"] = Strings(operationIds ?? all.Select(row => S(row, "operation")).Where(value => value.Length > 0)),
            ["helpPaths"] = Strings(helpPaths ?? all.Select(row => S(row, "help")).Where(value => value.Length > 0))
        };
    }

    public static TutorBuiltinPublishResult Publish(TutorCourseDocument course, string outputDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(course); cancellationToken.ThrowIfCancellationRequested(); course.AssertSavedSourceUnchanged();
        var prepared = Prepare(course);
        var (liveStamp, snapshot, lessons, questions, tracks, issues, reviewedChanges, bankVersion) = prepared;
        string output = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar);
        string parent = Path.GetDirectoryName(output) ?? throw new InvalidOperationException("Choose a dedicated tutor source-output folder.");
        if (SafePaths.Same(output, Path.GetPathRoot(output)!) || course.FilePath != null && SafePaths.IsWithin(course.FilePath, output)) throw new InvalidOperationException("Tutor build output must not contain or replace its authoring source.");
        SafePaths.CheckNoLinks(output); Directory.CreateDirectory(parent);
        string name = Path.GetFileName(output), token = Guid.NewGuid().ToString("N");
        string stage = Path.Combine(parent, "." + name + "-stage-" + token), old = Path.Combine(parent, "." + name + "-old-" + token);
        string marker = Path.Combine(parent, "." + name + ".publishing");
        using (var own = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None)) own.Write(Encoding.UTF8.GetBytes(token));
        bool promoted = false, movedOld = false; var warnings = new List<string>();
        try
        {
            if (Directory.Exists(output)) VerifyOwnedOutput(output);
            else if (File.Exists(output)) throw new IOException("The output folder name is occupied by a file.");
            Directory.CreateDirectory(stage);
            Write(stage, "lessons.json", lessons.ToJsonString(Pretty) + "\n");
            Write(stage, "questions.json", questions.ToJsonString(Pretty) + "\n");
            Write(stage, "tracks.json", tracks.ToJsonString(Pretty) + "\n");
            string bank = "// Generated StatsDirect built-in tutor source. Edit the authoring course, then publish again.\n"
                + "export const bankVersion = " + JsonSerializer.Serialize(bankVersion) + ";\n"
                + "export const tracks = " + tracks.ToJsonString(Pretty) + ";\n"
                + "export const questions = " + questions.ToJsonString(Pretty) + ";\n";
            Write(stage, "bank.mjs", bank);
            string readme = "# StatsDirect built-in tutor build inputs\n\nGenerated by the StatsDirect Help Editor from reviewed authoring source.\nThe normal Windows Learning build reads this manifest, verifies every hash,\nloads lessons and bank together, then rebuilds the shipped app.js and lessons.json.\nThese are built-in lessons/questions, with their original IDs, pathways and operations.\nThey are not a provider course pack and do not use course: ID prefixes.\n\nDo not edit generated files. Publish a new reviewed authoring revision instead.\nA sibling ." + name + ".publishing marker means publishing is incomplete: builds must stop.\nNo R code is executed by publishing. Published content does not itself award CPD credit.\n";
            Write(stage, "README.md", readme);
            var files = new JsonArray();
            foreach (string file in OutputFiles.Take(4)) files.Add(new JsonObject { ["path"] = file, ["sha256"] = FileHash(Path.Combine(stage, file)), ["bytes"] = new FileInfo(Path.Combine(stage, file)).Length });
            var manifest = new JsonObject
            {
                ["schemaVersion"] = 1, ["format"] = Format, ["publisher"] = "StatsDirectHelpEditor", ["publishedAtUtc"] = DateTimeOffset.UtcNow.ToString("o"),
                ["sourceCourseId"] = S(snapshot, "id"), ["sourceCourseVersion"] = S(snapshot, "version"), ["sourceCourseSha256"] = liveStamp,
                ["sourceFileName"] = course.FilePath == null ? null : Path.GetFileName(course.FilePath), ["sourceFileSha256"] = course.FilePath == null ? null : FileHash(course.FilePath),
                ["bankVersion"] = bankVersion, ["lessonCount"] = lessons.Count, ["questionCount"] = questions.Count, ["trackCount"] = tracks.Count,
                ["files"] = files, ["readmeSha256"] = FileHash(Path.Combine(stage, "README.md")),
                ["reviewIssues"] = JsonSerializer.SerializeToNode(issues.Select(issue => new { severity = issue.Severity, path = issue.Path, message = issue.Message })),
                ["reviewWarningCount"] = issues.Count(issue => issue.Severity == "Warning"), ["reviewGatePassed"] = true,
                ["reviewedChangedItems"] = reviewedChanges, ["unreviewedChangedItems"] = new JsonArray()
            };
            Write(stage, "manifest.json", manifest.ToJsonString(Pretty) + "\n");
            cancellationToken.ThrowIfCancellationRequested(); course.AssertSavedSourceUnchanged();
            if (liveStamp != Hash(Encoding.UTF8.GetBytes(course.Content.ToJsonString()))) throw new InvalidOperationException("The authoring draft changed during publishing. Publish again; prior output is preserved.");
            VerifyOwnedOutput(stage); SafePaths.CheckNoLinks(output);
            if (Directory.Exists(output)) { VerifyOwnedOutput(output); Directory.Move(output, old); movedOld = true; }
            try { Directory.Move(stage, output); promoted = true; }
            catch { if (movedOld && !Directory.Exists(output)) { Directory.Move(old, output); movedOld = false; } throw; }
            return new(output, lessons.Count, questions.Count, bankVersion, FileHash(Path.Combine(output, "manifest.json")), issues.AsReadOnly(), warnings.AsReadOnly());
        }
        finally
        {
            try { if (Directory.Exists(stage)) DeleteOwned(stage, parent, "." + name + "-stage-" + token); if (promoted && movedOld && Directory.Exists(old)) DeleteOwned(old, parent, "." + name + "-old-" + token); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { if (promoted) warnings.Add("Build inputs were published, but temporary owned output could not be removed: " + error.Message); }
            try { if ((promoted || !movedOld) && File.Exists(marker) && File.ReadAllText(marker) == token) File.Delete(marker); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { if (promoted) warnings.Add("The inputs were published, but the publishing marker could not be removed; builds must wait until it is resolved: " + error.Message); }
        }
    }

    public static TutorBuiltinInspection Inspect(TutorCourseDocument course)
    {
        try { var p = Prepare(course); return new(true, p.Lessons.Count, p.Questions.Count, p.BankVersion, p.Issues.AsReadOnly()); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or IOException or ArgumentException)
        { return new(false, course.GetItems("lessons").Count, course.GetItems("questions").Count, null, [new("Error", "course", error.Message)]); }
    }

    private sealed record PreparedTutor(string LiveStamp, JsonObject Snapshot, JsonArray Lessons, JsonArray Questions, JsonObject Tracks,
        List<TutorValidationIssue> Issues, JsonArray ReviewedChanges, string BankVersion);
    private static PreparedTutor Prepare(TutorCourseDocument course)
    {
        course.AssertSavedSourceUnchanged();
        string liveStamp = Hash(Encoding.UTF8.GetBytes(course.Content.ToJsonString()));
        var snapshot = (JsonObject)course.Content.DeepClone();
        var baseline = snapshot["authoring"]?["builtinBaseline"] as JsonObject
            ?? throw new InvalidDataException("This course has no identified built-in baseline. Initialise it from the original packaged tutor source before publishing.");
        if (I(baseline, "schemaVersion") != 1) throw new InvalidDataException("The built-in tutor baseline format is unsupported.");
        if (S(baseline, "recordHashAlgorithm") != TutorContentFingerprint.Algorithm) throw new InvalidDataException("Initialise the built-in baseline from the original packaged source using the current content fingerprint format.");
        var issues = course.Validate().ToList();
        var lessons = (JsonArray)Array(snapshot, "lessons").DeepClone(); var questions = (JsonArray)Array(snapshot, "questions").DeepClone(); var tracks = (JsonObject)Tracks(snapshot).DeepClone();
        StripAuthoring(lessons); StripAuthoring(questions);
        ValidateBindings(lessons, questions, tracks, baseline, issues);
        ReviewGate("lessons", lessons, baseline["lessonHashes"] as JsonObject ?? new(), issues);
        ReviewGate("questions", questions, baseline["questionHashes"] as JsonObject ?? new(), issues);
        if (S(snapshot["verification"], "status") == "not-rechecked-after-edit") issues.Add(new("Error", "course.verification", "The course's previous numerical verification is stale. Review/rerun the recorded checks and update its verification record before publishing."));
        var reviewedChanges = ReviewedChanges(lessons, questions, baseline);
        SynchroniseEmbeddedQuestions(lessons, questions, baseline, issues);
        CheckHostLessonContext(lessons, issues);
        var errors = issues.Where(issue => issue.Severity == "Error").ToArray();
        if (errors.Length > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(issue => issue.Path + ": " + issue.Message)));
        string bodyHash = BodyHash(lessons, questions, tracks);
        string bankVersion = bodyHash == S(baseline, "bodyHash") ? S(baseline, "bankVersion") : S(snapshot, "version") + "-" + bodyHash[..12];
        if (string.IsNullOrWhiteSpace(bankVersion)) throw new InvalidDataException("The tutor bank needs a version.");
        return new(liveStamp, snapshot, lessons, questions, tracks, issues, reviewedChanges, bankVersion);
    }

    private static void ReviewGate(string kind, JsonArray records, JsonObject hashes, List<TutorValidationIssue> issues)
    {
        foreach (var record in records.OfType<JsonObject>())
        {
            string id = S(record, "id"); bool changed = RecordHash(record) != S(hashes, id);
            if (!changed) continue;
            var review = record["review"] as JsonObject;
            bool reviewed = S(review, "status").StartsWith("Reviewed", StringComparison.OrdinalIgnoreCase) && !S(review, "status").Contains("draft", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(S(review, "reviewer")) && S(review, "reviewer").Length <= 200
                && !string.IsNullOrWhiteSpace(S(review, "notes")) && S(review, "notes").Length <= 10000
                && DateOnly.TryParseExact(S(review, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                && S(review, "contentSha256") == TutorContentFingerprint.Record(record);
            if (!reviewed) issues.Add(new("Error", kind + "." + id + ".review", "Changed or new built-in material needs an explicit author review tied to its current content, with reviewer and notes, before it can ship."));
            if (S(record["verification"], "status") == "not-rechecked-after-edit") issues.Add(new("Error", kind + "." + id + ".verification", "Rerun/review the recorded numerical checks and update their verification record before publishing this changed material."));
        }
    }
    private static void ValidateBindings(JsonArray lessons, JsonArray questions, JsonObject tracks, JsonObject baseline, List<TutorValidationIssue> issues)
    {
        var lessonIds = lessons.OfType<JsonObject>().Select(row => S(row, "id")).ToHashSet(StringComparer.Ordinal);
        var questionIds = questions.OfType<JsonObject>().Select(row => S(row, "id")).ToHashSet(StringComparer.Ordinal);
        var operations = Array(baseline, "operationIds").Select(node => node!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var help = Array(baseline, "helpPaths").Select(node => node!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        void Error(string path, bool valid, string text) { if (!valid) issues.Add(new("Error", path, text)); }
        Error("lessons", lessons.Count > 0, "The built-in tutor needs at least one lesson; a document-only provider course cannot replace the main tutor library.");
        Error("tracks", tracks.Count > 0 && tracks.All(pair => Regex.IsMatch(pair.Key, "\\A[A-Za-z0-9_-]{1,100}\\z") && pair.Value is JsonObject row && !string.IsNullOrWhiteSpace(S(row, "title")) && S(row, "title").Length <= 300), "Provide named built-in learning pathways with valid IDs and titles of at most 300 characters.");
        var requiredTracks = new[] { "foundation", "medicine", "publicHealth", "analyst", "researcher" }.Concat(Array(baseline, "trackIds").Select(node => node!.GetValue<string>())).Distinct(StringComparer.Ordinal);
        Error("tracks", requiredTracks.All(tracks.ContainsKey), "Keep the existing learning pathway IDs so saved portfolios and the default foundation pathway can still open. Labels may change and new pathways may be added.");
        foreach (var record in lessons.Concat(questions).OfType<JsonObject>())
        {
            string id = S(record, "id"), operation = S(record, "operation"), helpPath = S(record, "help");
            Error(id + ".operation", operation.Length == 0 || operations.Contains(operation), "Choose a known StatsDirect operation ID.");
            Error(id + ".help", helpPath.Length == 0 || help.Contains(helpPath), "Choose a help topic present in the target application.");
        }
        foreach (var lesson in lessons.OfType<JsonObject>())
        {
            string id = S(lesson, "id");
            Error(id + ".prerequisites", Array(lesson, "prerequisites").All(value => value is JsonValue node && node.TryGetValue<string>(out var reference) && reference != id && lessonIds.Contains(reference)), "A prerequisite must identify a different current lesson.");
            Error(id + ".assessmentIds", Array(lesson, "assessmentIds").All(value => value is JsonValue node && node.TryGetValue<string>(out var reference) && questionIds.Contains(reference)), "An assessment link identifies a missing current question.");
            foreach (string key in new[] { "prerequisites", "assessmentIds", "practiceQuestions", "sources" }) Error(id + "." + key, lesson[key] == null || lesson[key] is JsonArray, "This built-in metadata field must be an array.");
        }
        foreach (var question in questions.OfType<JsonObject>())
        {
            string id = S(question, "id"), track = S(question, "track"), lessonId = S(question, "lessonId");
            foreach (string field in new[] { "topic", "hint", "lesson", "help" })
                Error(id + "." + field, question[field] is JsonValue text && text.TryGetValue<string>(out _), "Built-in questions need a string for this field (an empty string is allowed). The application uses it when rendering feedback and restoring saved attempts.");
            Error(id + ".version", I(question, "version") > 0, "A built-in question needs a positive integer version.");
            foreach (var option in Array(question, "options").OfType<JsonObject>())
                Error(id + ".options." + S(option, "id") + ".feedback", option["feedback"] is JsonValue feedback && feedback.TryGetValue<string>(out _), "Every built-in option needs a feedback string; an empty string is allowed.");
            Error(id + ".track", track == "core" || tracks.ContainsKey(track), "Choose one of the named learning pathways or core.");
            Error(id + ".lessonId", lessonId.Length == 0 || lessonIds.Contains(lessonId), "The question identifies a missing lesson.");
            var sourceIds = Array(question, "sourceIds");
            Error(id + ".sourceIds", question["sourceIds"] == null || question["sourceIds"] is JsonArray, "Source IDs must be an array.");
            var lesson = lessons.OfType<JsonObject>().SingleOrDefault(row => S(row, "id") == lessonId);
            var knownSources = Array(lesson, "sources").OfType<JsonObject>().Select(row => S(row, "id")).ToHashSet(StringComparer.Ordinal);
            Error(id + ".sourceIds", sourceIds.Count == 0 || lesson != null && sourceIds.All(value => value is JsonValue node && node.TryGetValue<string>(out var sourceId) && knownSources.Contains(sourceId)), "The question's source IDs must belong to its linked lesson.");
        }
        var byLessonId = lessons.OfType<JsonObject>().ToDictionary(row => S(row, "id"), StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal); var visited = new HashSet<string>(StringComparer.Ordinal);
        bool HasCycle(string id)
        {
            if (visiting.Contains(id)) return true;
            if (!visited.Add(id) || !byLessonId.TryGetValue(id, out var lesson)) return false;
            visiting.Add(id);
            foreach (var prerequisite in Array(lesson, "prerequisites"))
                if (prerequisite is JsonValue value && value.TryGetValue<string>(out var target) && HasCycle(target)) return true;
            visiting.Remove(id); return false;
        }
        Error("lessons.prerequisites", !byLessonId.Keys.Any(HasCycle), "Lesson prerequisites must not form a cycle.");
    }
    private static JsonArray ReviewedChanges(JsonArray lessons, JsonArray questions, JsonObject baseline)
    {
        var result = new JsonArray();
        foreach (var (kind, records, hashKey) in new[] { ("lessons", lessons, "lessonHashes"), ("questions", questions, "questionHashes") })
            foreach (var row in records.OfType<JsonObject>())
                if (RecordHash(row) != S(baseline[hashKey], S(row, "id"))) result.Add(new JsonObject { ["kind"] = kind, ["id"] = S(row, "id"), ["reviewer"] = S(row["review"], "reviewer"), ["date"] = S(row["review"], "date"), ["notes"] = S(row["review"], "notes"), ["contentSha256"] = S(row["review"], "contentSha256") });
        return result;
    }
    private static void CheckHostLessonContext(JsonArray lessons, List<TutorValidationIssue> issues)
    {
        string[] fields = ["id", "title", "objective", "summary", "steps", "challenge", "r", "contentVersion", "review", "prerequisites", "learningObjectives", "keyConcepts", "misconceptions", "workedExample", "teachingPrompts", "rProgression", "sources", "assessmentIds", "practiceQuestions"];
        foreach (var lesson in lessons.OfType<JsonObject>())
        {
            var curated = new JsonObject(); foreach (string field in fields) if (lesson[field] != null) curated[field] = lesson[field]!.DeepClone();
            if (curated.ToJsonString().Length > 10000) curated.Remove("r");
            if (curated.ToJsonString().Length > 10000) issues.Add(new("Error", "lessons." + S(lesson, "id"), "This lesson exceeds the application's 10,000-character tutor-context limit even without R code. Split it into smaller lessons or shorten learner-facing notes; content was not truncated."));
        }
    }
    private static void SynchroniseEmbeddedQuestions(JsonArray lessons, JsonArray questions, JsonObject baseline, List<TutorValidationIssue> issues)
    {
        var byId = questions.OfType<JsonObject>().ToDictionary(row => S(row, "id"), StringComparer.Ordinal);
        foreach (var lesson in lessons.OfType<JsonObject>())
        {
            if (lesson["practiceQuestions"] is not JsonArray embedded) continue;
            var current = new JsonArray();
            foreach (var question in embedded.OfType<JsonObject>())
            {
                string id = S(question, "id");
                if (byId.TryGetValue(id, out var authoritative)) current.Add(authoritative.DeepClone());
                // A deleted baseline question is removed from the mirror. New
                // embedded-only questions must first enter the flat collection.
                else if (baseline["questionHashes"] is not JsonObject known || !known.ContainsKey(id)) issues.Add(new("Error", S(lesson, "id") + ".practiceQuestions", "Add a new embedded question to the course's main question collection before publishing."));
            }
            if (!JsonNode.DeepEquals(embedded, current)) lesson["contentVersion"] = S(lesson, "contentVersion") + "-q" + Hash(Encoding.UTF8.GetBytes(current.ToJsonString()))[..12];
            lesson["practiceQuestions"] = current;
        }
    }
    private static JsonObject Tracks(JsonObject course) => course["authoring"]?["tracks"] as JsonObject ?? course["authoring"]?["sourceTracks"] as JsonObject ?? throw new InvalidDataException("The built-in learning pathways are missing from this authoring course.");
    private static string RecordHash(JsonObject record) => TutorContentFingerprint.Record(record);
    private static string BodyHash(JsonArray lessons, JsonArray questions, JsonObject tracks) => Hash(Encoding.UTF8.GetBytes(new JsonObject { ["lessons"] = lessons.DeepClone(), ["questions"] = questions.DeepClone(), ["tracks"] = tracks.DeepClone() }.ToJsonString()));
    private static void StripAuthoring(JsonNode? node) { if (node is JsonObject row) { row.Remove("authoring"); foreach (var pair in row.ToArray()) StripAuthoring(pair.Value); } else if (node is JsonArray values) foreach (var value in values) StripAuthoring(value); }
    private static JsonArray Array(JsonNode? node, string key) => node is JsonObject row && row[key] is JsonArray values ? values : [];
    private static string S(JsonNode? node, string key, string fallback = "") => node is JsonObject row && row[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : fallback;
    private static int I(JsonNode node, string key) => node[key] is JsonValue value && value.TryGetValue<int>(out int number) ? number : 0;
    private static JsonArray Strings(IEnumerable<string> values) => new(values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(value => (JsonNode)JsonValue.Create(value)!).ToArray());
    private static void Write(string directory, string name, string text) => File.WriteAllText(Path.Combine(directory, name), SafePaths.NormalizeNewlines(text), new UTF8Encoding(false));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string FileHash(string file) => Hash(File.ReadAllBytes(file));
    private static void VerifyOwnedOutput(string directory)
    {
        SafePaths.CheckNoLinks(directory);
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) return;
        if (Directory.EnumerateDirectories(directory).Any() || Directory.EnumerateFiles(directory).Select(Path.GetFileName).Order().SequenceEqual(OutputFiles.Order()) == false) throw new IOException("Use an empty dedicated folder or untouched prior tutor build output; other files will not be replaced.");
        foreach (string file in OutputFiles) SafePaths.CheckNoLinks(Path.Combine(directory, file));
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json"))) as JsonObject ?? throw new InvalidDataException("The previous tutor build manifest is unreadable.");
        if (I(manifest, "schemaVersion") != 1 || S(manifest, "format") != Format) throw new InvalidDataException("This folder is not owned StatsDirect tutor build output.");
        var files = Array(manifest, "files");
        if (files.Count != 4 || !files.OfType<JsonObject>().Select(row => S(row, "path")).Order().SequenceEqual(OutputFiles.Take(4).Order())) throw new InvalidDataException("The tutor build manifest file list is invalid.");
        foreach (var row in files.OfType<JsonObject>()) if (FileHash(Path.Combine(directory, S(row, "path"))) != S(row, "sha256")) throw new IOException("A generated tutor input was edited outside the authoring source. Preserve it before publishing again.");
        if (FileHash(Path.Combine(directory, "README.md")) != S(manifest, "readmeSha256")) throw new IOException("The generated README was edited; preserve it before replacing this output.");
    }
    internal static void RequireOwnedOutput(string directory) => VerifyOwnedOutput(directory);
    private static void DeleteOwned(string directory, string parent, string expectedName)
    {
        if (!SafePaths.Same(Path.GetDirectoryName(directory)!, parent) || Path.GetFileName(directory) != expectedName) throw new IOException("Temporary tutor output ownership could not be verified.");
        SafePaths.CheckNoLinks(directory);
        foreach (string file in Directory.EnumerateFiles(directory)) { SafePaths.CheckNoLinks(file); File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly); File.Delete(file); }
        if (Directory.EnumerateDirectories(directory).Any()) throw new IOException("Unexpected directory in temporary tutor output; retained for inspection.");
        Directory.Delete(directory);
    }
}
