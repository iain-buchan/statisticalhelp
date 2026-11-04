using System.Reflection;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static partial class Program
{
    private static void CheckQuestionClassification()
    {
        string path = Path.Combine(fixture, "native-classification-course.json");
        var seed = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "TutorSource", "starter-course.json")))!.AsObject();
        var original = seed["questions"]![0]!.AsObject(); string id = S(original, "id");
        original["educatorMetadata"] = new JsonObject { ["customTag"] = "Keep this tutor's annotation", ["nested"] = new JsonArray(true, null, "μ") };
        original["options"]![0]!["unknownTargetOption"] = "Retain this answer metadata";
        File.WriteAllText(path, seed.ToJsonString());
        using var window = new TutorEditorForm(path, fixture, externalLinkLauncher: _ => {})
        { SuppressDialogs = true, ShowInTaskbar = false, WindowState = FormWindowState.Normal, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0), Size = new Size(1500, 1000) };
        window.Show();
        JsonObject Question() => window.Document.GetItems("questions").Single(row => S(row, "id") == id);
        string initial = window.Document.Content.ToJsonString(), sourceHash = Hash(path);
        window.SelectItem("questions", id);
        Check(window.GetField("difficulty") == "" && window.GetField("learnerGoals") == "" && !window.HasUnsavedChanges
            && window.Document.Content.ToJsonString() == initial && Hash(path) == sourceHash,
            "Native question classification opens legacy material unassigned without changing the document or source");
        string track = S(Question(), "track"), correct = S(Question(), "correct"); var answerOptions = Question()["options"]!.DeepClone(); int version = Question()["version"]!.GetValue<int>();
        window.SetField("difficulty", "intermediate"); window.SetField("learnerGoals", "undergraduateMedicine\npublicHealth");
        window.SetField("learnerGoalNotes", "MRCP preparation <qualification> & local syllabus");
        Check(window.HasUnsavedChanges, "Native difficulty, learner checklist and notes edits mark the course as unsaved before commit");
        window.CommitCurrent(); var classified = Question();
        Check(S(classified, "difficulty") == "intermediate" && classified["learnerGoals"]!.AsArray().Select(value => value!.GetValue<string>()).ToHashSet().SetEquals(new[] { "undergraduateMedicine", "publicHealth" })
            && S(classified, "learnerGoalNotes").Contains("MRCP"),
            "Native classification controls commit the chosen difficulty, multiple learner goals and qualification notes together");
        Check(classified["version"]!.GetValue<int>() == version + 1 && S(classified["review"], "status").Contains("Draft")
            && S(classified, "track") == track && S(classified, "correct") == correct && JsonNode.DeepEquals(classified["options"], answerOptions)
            && JsonNode.DeepEquals(classified["educatorMetadata"], original["educatorMetadata"]),
            "Native classification invalidates earlier review while preserving the pathway, key, choices and unknown educator metadata");
        window.UndoItem();
        Check(JsonNode.DeepEquals(Question(), original) && window.GetField("difficulty") == "" && window.GetField("learnerGoals") == "",
            "Native Undo restores an unassigned question and its exact original metadata after classification");
        window.RedoItem();
        Check(JsonNode.DeepEquals(Question(), classified) && window.GetField("difficulty") == "intermediate" && window.GetField("learnerGoals").Contains("publicHealth"),
            "Native Redo restores classification and refreshes the selected controls");
        window.SaveCourse(); var reopened = TutorCourseDocument.Open(path).GetItems("questions").Single(row => S(row, "id") == id);
        Check(JsonNode.DeepEquals(Question(), reopened) && !window.HasUnsavedChanges
            && TutorCourseDocument.Open(path).GetItems("questions").Skip(1).All(row => row["difficulty"] == null && row["learnerGoals"] == null),
            "Native Save and reopen retain classification exactly and leave all other legacy questions unassigned");

        window.RecordReview("Native course author", "Reviewed question difficulty and audience against the stated learning objective."); window.SaveCourse();
        var reviewed = Question(); string reviewedDocument = window.Document.Content.ToJsonString();
        var goals = (CheckedListBox)typeof(TutorEditorForm).GetField("questionGoals", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        int checkedIndex = goals.CheckedIndices.Cast<int>().First(); goals.SetItemChecked(checkedIndex, false); goals.SetItemChecked(checkedIndex, true);
        window.CommitCurrent();
        Check(JsonNode.DeepEquals(Question(), reviewed) && window.Document.Content.ToJsonString() == reviewedDocument && !window.HasUnsavedChanges,
            "Unchecking and rechecking the same learner goal does not change the version, review or saved state");

        string html = window.BuildPreviewHtml();
        Check(html.Contains("Intermediate") && html.Contains("Undergraduate medicine") && html.Contains("Public health")
            && html.Contains("&lt;qualification&gt; &amp; local syllabus") && !html.Contains("<qualification>"),
            "Native learner preview shows readable classification labels and safely escapes qualification notes");
        var filterBox = (TextBox)typeof(TutorEditorForm).GetField("filter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var tree = (TreeView)typeof(TutorEditorForm).GetField("navigation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        filterBox.Text = "Undergraduate medicine";
        var branch = tree.Nodes.Cast<TreeNode>().Single(node => node.Text.StartsWith("Multiple-choice questions", StringComparison.Ordinal));
        string NodeId(TreeNode node) => node.Tag?.GetType().GetProperty("Id")?.GetValue(node.Tag)?.ToString() ?? "";
        Check(branch.Nodes.Count == 1 && NodeId(branch.Nodes[0]) == id && !window.HasUnsavedChanges,
            "Native course search finds the classified question by its learner-goal label without altering content");
        filterBox.Text = "";

        window.SetField("difficulty", ""); window.SetField("learnerGoals", ""); window.SetField("learnerGoalNotes", ""); window.CommitCurrent();
        var cleared = Question();
        Check(!cleared.ContainsKey("difficulty") && cleared["learnerGoals"] is JsonArray { Count: 0 } && S(cleared, "learnerGoalNotes") == ""
            && S(cleared, "track") == track && JsonNode.DeepEquals(cleared["educatorMetadata"], original["educatorMetadata"]),
            "Native controls can clear all optional classifications without changing the pathway or losing unknown metadata");
        window.UndoItem(); Check(JsonNode.DeepEquals(Question(), reviewed), "Undo restores reviewed classification after it was cleared");
        window.RedoItem(); Check(JsonNode.DeepEquals(Question(), cleared), "Redo restores the cleared unassigned classification"); window.SaveCourse();
        reopened = TutorCourseDocument.Open(path).GetItems("questions").Single(row => S(row, "id") == id);
        Check(!reopened.ContainsKey("difficulty") && reopened["learnerGoals"] is JsonArray { Count: 0 }, "Cleared classifications remain unassigned after native Save and reopen");

        string beforeDiscussion = window.Document.Content.ToJsonString(), savedHash = Hash(path);
        window.SetField("learnerGoalNotes", "An unsaved local teaching note.");
        var discussion = new TutorResearchProposal("questions", "Discuss", Question().ToJsonString(), new JsonObject(), "Discussion only", "Clarify the intended course level.", "", [], [], false,
            HasDraft: false, ConversationReply: "Which health course and learner level do you want to target?");
        bool discussionRejected = false; try { window.ApplyResearchProposal(discussion); } catch (InvalidOperationException) { discussionRejected = true; }
        Check(discussionRejected && window.Document.Content.ToJsonString() == beforeDiscussion && Hash(path) == savedHash
            && window.GetField("learnerGoalNotes") == "An unsaved local teaching note." && window.HasUnsavedChanges,
            "A discussion-only ChatGPT reply cannot apply content or commit and discard an unrelated pending author edit");
        window.SetField("learnerGoalNotes", ""); window.CommitCurrent();
        Check(window.LastError == null, "Native classification workflow completes without reported UI errors"); window.Close();

        string futurePath = Path.Combine(fixture, "native-future-classification.json");
        original["difficulty"] = "futureLevel"; original["learnerGoals"] = new JsonArray("futureLearningGoal"); File.WriteAllText(futurePath, seed.ToJsonString());
        using var future = new TutorEditorForm(futurePath, fixture, externalLinkLauncher: _ => {})
        { SuppressDialogs = true, ShowInTaskbar = false, WindowState = FormWindowState.Normal, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0), Size = new Size(1500, 1000) };
        future.Show(); future.SelectItem("questions", id); future.SetField("hint", future.GetField("hint") + " An unrelated wording revision."); future.CommitCurrent();
        var retained = future.Document.GetItems("questions").Single(row => S(row, "id") == id);
        Check(S(retained, "difficulty") == "futureLevel" && retained["learnerGoals"]![0]!.GetValue<string>() == "futureLearningGoal"
            && JsonNode.DeepEquals(retained["educatorMetadata"], original["educatorMetadata"]),
            "Unrelated native edits retain unrecognised imported classifications for explicit author correction instead of silently dropping metadata");
        future.Close();
    }
}
