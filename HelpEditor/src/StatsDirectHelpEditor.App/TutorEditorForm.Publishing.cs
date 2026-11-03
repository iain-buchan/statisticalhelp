using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public sealed partial class TutorEditorForm
{
    public TutorBuiltinPublishResult? LastBuiltinPublish { get; private set; }

    private void PublishBuiltin()
    {
        CommitCurrent();
        string repo = AuthoringPreferences.ReadStatsDirectSourceRoot(workspaceRoot);
        if (!ValidStatsDirectSourceRoot(repo))
        {
            using var choose = new FolderBrowserDialog { Description = "Choose the StatsDirect source repository (contains Learning and StatsDirectUI)", UseDescriptionForTitle = true, ShowNewFolderButton = false };
            if (choose.ShowDialog(this) != DialogResult.OK) return;
            repo = choose.SelectedPath;
        }
        if (!ValidStatsDirectSourceRoot(repo)) throw new InvalidDataException("Choose the StatsDirect source repository, containing Learning/build.mjs and StatsDirectUI/StatsDirectUI.csproj.");
        string output = Path.Combine(repo, "Learning", "AuthoredTutor");
        if (!SuppressDialogs && MessageBox.Show(this,
            "Publish " + document.GetItems("lessons").Count + " lessons and " + document.GetItems("questions").Count + " questions for the next StatsDirect build?\n\n" + output +
            "\n\nNew or changed material needs a recorded author review. Saving alone keeps a draft. Publishing updates build inputs; it does not create installers or push to GitHub.",
            "Publish built-in tutor sources", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        var published = PublishBuiltinTo(repo);
        string preferenceWarning = "";
        try { AuthoringPreferences.SaveStatsDirectSourceRoot(workspaceRoot, repo); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { preferenceWarning = " · source published; location could not be remembered"; }
        status.Text = "Published " + published.LessonCount + " lessons / " + published.QuestionCount + " questions to " + published.OutputDirectory + " · next StatsDirect build incorporates these sources" + preferenceWarning;
    }

    /// <summary>Publish reviewed canonical data to the consuming application's source tree; never builds or installs it.</summary>
    public TutorBuiltinPublishResult PublishBuiltinTo(string statsDirectSourceRoot)
    {
        CommitCurrent();
        string repo = Path.GetFullPath(statsDirectSourceRoot);
        if (!ValidStatsDirectSourceRoot(repo)) throw new InvalidDataException("The selected folder is not a StatsDirect source repository.");
        string original = Path.Combine(AppContext.BaseDirectory, "TutorSeed", "starter-course.json");
        if (!File.Exists(original)) throw new FileNotFoundException("The immutable original tutor template is missing. Restore TutorSeed before publishing.", original);
        var baseline = TutorCourseDocument.Open(original);
        string assets = Path.Combine(repo, "StatsDirectUI", "Assets");
        var operations = Directory.EnumerateFiles(Path.Combine(assets, "Operations"), "*.xml", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension).OfType<string>();
        string helpRoot = Path.Combine(assets, "Help");
        var pages = Directory.EnumerateFiles(helpRoot, "*.htm", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(helpRoot, path).Replace('\\', '/'));
        JsonNode? previousAuthoring = document.Content["authoring"]?.DeepClone();
        TutorBuiltinPublishResult result;
        try
        {
            if (document.Content["authoring"] is not JsonObject authoring) document.Content["authoring"] = authoring = new JsonObject();
            authoring["builtinBaseline"] = TutorBuiltinPublisher.CreateBaseline(baseline.Content, operations, pages);
            result = TutorBuiltinPublisher.Publish(document, Path.Combine(repo, "Learning", "AuthoredTutor"));
        }
        catch { if (previousAuthoring == null) document.Content.Remove("authoring"); else document.Content["authoring"] = previousAuthoring; throw; }
        LastBuiltinPublish = result;
        ReloadSelection();
        return result;
    }

    private static bool ValidStatsDirectSourceRoot(string path) => !string.IsNullOrWhiteSpace(path)
        && File.Exists(Path.Combine(path, "Learning", "build.mjs"))
        && File.Exists(Path.Combine(path, "StatsDirectUI", "StatsDirectUI.csproj"))
        && Directory.Exists(Path.Combine(path, "StatsDirectUI", "Assets", "Operations"))
        && Directory.Exists(Path.Combine(path, "StatsDirectUI", "Assets", "Help"));
}
