using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.Core;

internal sealed class BundleFixture
{
    internal string Root { get; }
    internal string ManifestPath => Path.Combine(Root, "Published/content-manifest.json");
    internal JsonObject Manifest { get; private set; }
    internal const string HelpHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string ProducerCommit = "1111111111111111111111111111111111111111";
    internal const string ReviewBinding = "authoring-source-before-generated-sync-v1";

    private BundleFixture(string root, JsonObject manifest) { Root = root; Manifest = manifest; }

    internal static BundleFixture Create(string root, bool reviewedMetadata = false)
    {
        string source = Path.Combine(root, "FixtureInputs/HelpSource"), assets = Path.Combine(root, "FixtureInputs/Web");
        Write(source, HelpProject.MarkerFile, "{}");
        Write(source, "Content/contents.htm", "<html><head><title>Contents</title></head><body><h1>Contents</h1><a href='basics/example.htm#Example'>Example</a></body></html>");
        Write(source, "Content/basics/example.htm", "<html><head><title>Example</title></head><body><h1 id='Example'>Example</h1><p>Fictional test content only.</p></body></html>");
        Write(source, "Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title='Contents' Link='/Content/contents.htm'/><TocEntry Title='Example' Link='/Content/basics/example.htm#Example'/></CatapultToc>");
        Write(source, "Project/Advanced/STATSDIRECT.flali", "<CatapultAliasFile><Map Name='CONTENTS' Link='/Content/contents.htm'/><Map Name='EXAMPLE' Link='/Content/basics/example.htm#Example'/></CatapultAliasFile>");
        Write(source, "Project/Advanced/SD3ContextID.h", "#define CONTENTS 1000\n#define EXAMPLE 1001\n");
        Write(assets, "mathjax/tex-mml-svg.js", "// Offline isolated renderer fixture; not a real renderer.\n");
        var project = HelpProject.Open(source);
        string bundleRoot = Path.Combine(root, "Repository"); Directory.CreateDirectory(bundleRoot);
        var website = HelpBuilder.Build(project, Path.Combine(bundleRoot, "Docs"), assets, HelpBuildTarget.Public);
        var desktop = HelpBuilder.Build(project, Path.Combine(bundleRoot, "Published/DesktopHelp"), assets, HelpBuildTarget.Desktop);
        foreach (var (directory, target) in new[] { (website.OutputDirectory, "Public"), (desktop.OutputDirectory, "Desktop") })
        {
            Write(directory, "resources/help-editor-runtime/publication.json", new JsonObject { ["schemaVersion"] = 1, ["sourceSha256"] = HelpHash, ["target"] = target }.ToJsonString());
            string ownership = Path.Combine(directory, ".statsdirect-help-output.json"); var own = Read(ownership);
            own["files"]!.AsArray().Add("resources/help-editor-runtime/publication.json"); File.WriteAllText(ownership, own.ToJsonString());
        }
        DesktopHelpPublisher.CreatePortableReceipt(project, desktop, assets, HelpHash);
        var course = TutorCourseDocument.Create(Course());
        course.Content["authoring"]!["builtinBaseline"] = TutorBuiltinPublisher.CreateBaseline(course.Content,
            ["FixtureOperation"], ["contents.htm", "basics/example.htm"]);
        if (reviewedMetadata)
        {
            var lesson = course.GetItems("lessons").Single(); lesson["summary"] = Text(lesson, "summary") + " Synthetic α <>& revision.";
            course.Update("lessons", "fixture-lesson", lesson); course.MarkReviewed("lessons", "fixture-lesson", "Software fixture reviewer", "Synthetic structural test only; never ship as curriculum.");
            var q = course.GetItems("questions").Single(); q["difficulty"] = "advanced"; q["learnerGoals"] = new JsonArray("mastersStatistics", "usMedicalBoards");
            q["learnerGoalNotes"] = "Synthetic α <>& target notes"; q["futureMetadata"] = new JsonObject { ["text"] = "Preserve unknown metadata" };
            q["options"]![0]!["futureOptionMetadata"] = "Retain this too";
            course.Update("questions", "FIXTURE-Q1", q); course.MarkReviewed("questions", "FIXTURE-Q1", "Software fixture reviewer", "Synthetic structural test only; never ship as curriculum.");
        }
        string coursePath = Path.Combine(root, "FixtureInputs/saved-course.json"); course.Save(coursePath);
        var tutor = TutorBuiltinPublisher.Publish(course, Path.Combine(bundleRoot, "Published/Tutor"));
        var manifest = new JsonObject
        {
            ["schemaVersion"] = 1, ["format"] = "statsdirect-published-content-v1", ["publisher"] = "StatsDirectHelpEditor",
            ["publicationId"] = Guid.NewGuid().ToString("D"), ["publishedAtUtc"] = DateTimeOffset.UtcNow.ToString("o"),
            ["producer"] = new JsonObject { ["version"] = "1.0.0", ["sourceCommit"] = ProducerCommit },
            ["helpSourceSha256"] = HelpHash, ["tutorSourceSha256"] = Hash(coursePath),
            ["artifacts"] = new JsonObject
            {
                ["website"] = new JsonObject { ["path"] = "Docs", ["topicCount"] = website.TopicCount, ["searchDocumentCount"] = website.SearchDocumentCount },
                ["desktopHelp"] = new JsonObject { ["path"] = "Published/DesktopHelp", ["topicCount"] = desktop.TopicCount, ["searchDocumentCount"] = desktop.SearchDocumentCount, ["contextCount"] = 2 },
                ["tutor"] = new JsonObject { ["path"] = "Published/Tutor", ["lessonCount"] = tutor.LessonCount, ["questionCount"] = tutor.QuestionCount,
                    ["trackCount"] = 5, ["bankVersion"] = tutor.BankVersion, ["reviewProofBinding"] = ReviewBinding }
            }
        };
        var result = new BundleFixture(bundleRoot, manifest);
        foreach (string artifact in new[] { "website", "desktopHelp", "tutor" }) result.Refresh(artifact);
        result.Save(); return result;
    }

    internal BundleFixture Copy(string destination)
    {
        CopyTree(Root, destination);
        return new(destination, Read(Path.Combine(destination, "Published/content-manifest.json")));
    }
    internal JsonObject Artifact(string name) => Manifest["artifacts"]![name]!.AsObject();
    internal string ArtifactRoot(string name) => Path.Combine(Root, Text(Artifact(name), "path").Replace('/', Path.DirectorySeparatorChar));
    internal JsonArray Files(string name) => Artifact(name)["files"]!.AsArray();
    internal void Refresh(string name)
    {
        var rows = new JsonArray();
        foreach (var file in Directory.GetFiles(ArtifactRoot(name), "*", SearchOption.AllDirectories).OrderBy(file => Path.GetRelativePath(ArtifactRoot(name), file).Replace('\\', '/'), StringComparer.Ordinal))
            rows.Add(new JsonObject { ["path"] = Path.GetRelativePath(ArtifactRoot(name), file).Replace('\\', '/'), ["bytes"] = new FileInfo(file).Length, ["sha256"] = Hash(file) });
        Artifact(name)["fileCount"] = rows.Count; Artifact(name)["files"] = rows;
    }
    internal void Save() => Write(Root, "Published/content-manifest.json", Manifest.ToJsonString() + "\n");
    internal Dictionary<string, string> AllHashes() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
        .ToDictionary(file => Path.GetRelativePath(Root, file).Replace('\\', '/'), Hash, StringComparer.Ordinal);
    internal void RewriteTutor(Action<JsonArray, JsonArray, JsonObject, JsonObject> edit)
    {
        string directory = ArtifactRoot("tutor");
        var lessons = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "lessons.json")))!.AsArray();
        var questions = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "questions.json")))!.AsArray();
        var tracks = Read(Path.Combine(directory, "tracks.json")); var child = Read(Path.Combine(directory, "manifest.json"));
        edit(lessons, questions, tracks, child);
        Write(directory, "lessons.json", lessons.ToJsonString() + "\n"); Write(directory, "questions.json", questions.ToJsonString() + "\n");
        Write(directory, "tracks.json", tracks.ToJsonString() + "\n");
        Write(directory, "bank.mjs", "// Isolated static test bank\nexport const bankVersion = " + System.Text.Json.JsonSerializer.Serialize(Text(child, "bankVersion"))
            + ";\nexport const tracks = " + tracks.ToJsonString() + ";\nexport const questions = " + questions.ToJsonString() + ";\n");
        foreach (var row in child["files"]!.AsArray().Select(node => node!.AsObject())) { string path = Text(row, "path"); row["bytes"] = new FileInfo(Path.Combine(directory, path)).Length; row["sha256"] = Hash(Path.Combine(directory, path)); }
        Write(directory, "manifest.json", child.ToJsonString() + "\n"); Refresh("tutor"); Save();
    }
    internal void RewriteDesktopReceipt(Action<JsonObject> edit)
    {
        string directory = ArtifactRoot("desktopHelp"), path = Path.Combine(directory, DesktopHelpPublisher.ReceiptFileName);
        var receipt = Read(path); edit(receipt); File.WriteAllText(path, receipt.ToJsonString() + "\n"); Refresh("desktopHelp"); Save();
    }
    internal static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    internal static string Text(JsonNode? obj, string key) => obj?[key]?.GetValue<string>() ?? "";
    internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    internal static void Write(string root, string path, string text)
    { string file = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text, new UTF8Encoding(false)); }
    internal static void CopyTree(string source, string destination)
    { foreach (string path in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) { string target = Path.Combine(destination, Path.GetRelativePath(source, path)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target); } }
    private static JsonObject Course()
    {
        var question = new JsonObject
        {
            ["id"] = "FIXTURE-Q1", ["version"] = 1, ["track"] = "foundation", ["topic"] = "Fictional arithmetic", ["objective"] = "Identify a simple sum.",
            ["stem"] = "In this synthetic software test, what is 2 + 2?", ["correct"] = "A", ["explanation"] = "Two plus two equals four.",
            ["hint"] = "Count the four objects.", ["lesson"] = "Fictional arithmetic", ["lessonId"] = "fixture-lesson", ["help"] = "basics/example.htm",
            ["options"] = new JsonArray(new JsonObject { ["id"] = "A", ["text"] = "4", ["feedback"] = "Correct for this fictional test." }, new JsonObject { ["id"] = "B", ["text"] = "5", ["feedback"] = "This is not the stated sum." }),
            ["review"] = new JsonObject { ["status"] = "Draft — synthetic software test only", ["date"] = "2026-11-05" }
        };
        var lesson = new JsonObject
        {
            ["id"] = "fixture-lesson", ["title"] = "Synthetic test lesson", ["objective"] = "Exercise content plumbing without teaching claims.",
            ["summary"] = "This is a fictional software-validation fixture, not publishable curriculum.", ["steps"] = "", ["challenge"] = "", ["r"] = "", ["help"] = "basics/example.htm", ["columns"] = new JsonArray(),
            ["contentVersion"] = "fixture-v1", ["practiceQuestions"] = new JsonArray(question.DeepClone()), ["assessmentIds"] = new JsonArray("FIXTURE-Q1"),
            ["review"] = new JsonObject { ["status"] = "Draft — synthetic software test only", ["date"] = "2026-11-05" }
        };
        var tracks = new JsonObject(); foreach (string id in new[] { "foundation", "medicine", "publicHealth", "analyst", "researcher" }) tracks[id] = new JsonObject { ["title"] = "Fixture " + id };
        return new JsonObject { ["schemaVersion"] = 2, ["id"] = "synthetic-shared-bundle", ["version"] = "fixture-v1", ["title"] = "Synthetic bundle fixture",
            ["provider"] = new JsonObject { ["name"] = "Software test fixture", ["reviewEmail"] = "fixture@example.invalid" },
            ["documents"] = new JsonArray(), ["lessons"] = new JsonArray(lesson), ["questions"] = new JsonArray(question),
            ["authoring"] = new JsonObject { ["audience"] = "Software tests only", ["sourceTracks"] = tracks, ["sourceBankVersion"] = "fixture-v1" } };
    }
}
