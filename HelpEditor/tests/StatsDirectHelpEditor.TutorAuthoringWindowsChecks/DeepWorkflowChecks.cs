using System.Reflection;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static partial class Program
{
    private static void CheckNativeNavigationSoak()
    {
        string path = Path.Combine(fixture, "navigation-soak-course.json");
        File.Copy(Path.Combine(root, "TutorSource", "starter-course.json"), path);
        using var window = new TutorEditorForm(path, fixture, externalLinkLauncher: _ => {})
        { SuppressDialogs = true, ShowInTaskbar = false, WindowState = FormWindowState.Normal, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0), Size = new Size(1500, 1000) };
        window.Show();
        string before = window.Document.Content.ToJsonString(), disk = Hash(path);
        var items = new[] { "lessons", "questions" }.SelectMany(kind => window.Document.GetItems(kind).Select(row => (Kind: kind, Id: S(row, "id")))).ToArray();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long memoryBefore = GC.GetTotalMemory(true); var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int repeat = 0; repeat < 4; repeat++) foreach (var item in items) window.SelectItem(item.Kind, item.Id);
        watch.Stop(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long memoryAfter = GC.GetTotalMemory(true);
        Check(!window.HasUnsavedChanges && window.Document.Content.ToJsonString() == before && Hash(path) == disk,
            "Repeated navigation through all 47 original lessons/questions leaves content and source unchanged");
        var first = items[0]; window.SelectItem(first.Kind, first.Id); string title = window.GetField("title");
        window.SetField("title", title + " — after navigation soak"); window.CommitCurrent(); window.UndoItem();
        Check(window.GetField("title") == title && window.LastError == null,
            "Editing and Undo remain usable after 188 native item transitions without disposed-control errors");
        File.WriteAllText(Path.Combine(evidence, "tutor-native-navigation-soak.json"), System.Text.Json.JsonSerializer.Serialize(new
        { transitions = items.Length * 4, elapsedMilliseconds = watch.ElapsedMilliseconds, managedBytesBefore = memoryBefore, managedBytesAfter = memoryAfter, sourceUnchanged = Hash(path) == disk }));
        window.Close();
    }

    private static void CheckDeepNativeWorkflows()
    {
        string path = Path.Combine(fixture, "deep-native-course.json");
        var initial = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        initial["optionalMetadata"] = new JsonObject { ["note"] = "Remove this through the JSON editor." };
        File.WriteAllText(path, initial.ToJsonString());
        using var window = new TutorEditorForm(path, fixture, externalLinkLauncher: _ => {})
        { SuppressDialogs = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0), Size = new Size(1500, 1000) };
        window.Show();
        var tabs = (TabControl)typeof(TutorEditorForm).GetField("tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var raw = (TextBox)typeof(TutorEditorForm).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        JsonObject Metadata()
        {
            var metadata = (JsonObject)window.Document.Content.DeepClone();
            foreach (string key in new[] { "lessons", "questions", "documents" }) metadata.Remove(key);
            return metadata;
        }
        var originalLessons = window.Document.Content["lessons"]!.DeepClone();
        var metadata = Metadata(); metadata.Remove("optionalMetadata");
        window.SetJsonSource(metadata.ToJsonString()); window.CommitCurrent();
        Check(!window.Document.Content.ContainsKey("optionalMetadata"), "Deleting optional course metadata in JSON actually removes it from the document");
        Check(JsonNode.DeepEquals(window.Document.Content["lessons"], originalLessons), "Course metadata JSON edits preserve lesson collections");
        window.SaveCourse();
        Check(!TutorCourseDocument.Open(path).Content.ContainsKey("optionalMetadata"), "Deleted course metadata stays deleted after Save and reopen");
        window.UndoItem();
        Check(window.Document.Content["optionalMetadata"]?["note"]?.GetValue<string>()?.Contains("Remove this") == true && window.HasUnsavedChanges, "Undo after Save restores deleted metadata as an unsaved change");
        window.RedoItem();
        Check(!window.Document.Content.ContainsKey("optionalMetadata"), "Redo removes the optional metadata again");
        string validCourse = window.Document.Content.ToJsonString();
        metadata = Metadata(); metadata["provider"] = 42;
        window.SetJsonSource(metadata.ToJsonString()); bool malformedMetadata = false;
        try { window.CommitCurrent(); } catch (InvalidDataException) { malformedMetadata = true; }
        Check(malformedMetadata && window.Document.Content.ToJsonString() == validCourse && raw.Text.Contains("42"), "Malformed course structure is rejected before changing the live document and retains its raw draft");
        window.SetJsonSource(Metadata().ToJsonString()); window.CommitCurrent();
        window.SelectItem("questions", "question-1");
        tabs.SelectedIndex = 0;
        window.SetField("hint", "An edit retained for command-history regression."); window.CommitCurrent(); window.UndoItem();
        var question = window.Document.GetItems("questions").Single(row => S(row, "id") == "question-1");
        string beforeRename = window.Document.Content.ToJsonString();
        var undoStack = (Stack<string>)typeof(TutorEditorForm).GetField("undo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var redoStack = (Stack<string>)typeof(TutorEditorForm).GetField("redo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        int undoBefore = undoStack.Count, redoBefore = redoStack.Count;
        var renamed = (JsonObject)question.DeepClone(); renamed["id"] = "renamed-existing-id";
        window.SetJsonSource(renamed.ToJsonString());
        bool renameRejected = false;
        try { window.CommitCurrent(); } catch (InvalidDataException) { renameRejected = true; }
        Check(renameRejected && window.Document.Content.ToJsonString() == beforeRename && raw.Text.Contains("renamed-existing-id"), "Raw editing cannot silently rename a stable question ID and retains the rejected draft");
        Check(redoBefore > 0 && undoStack.Count == undoBefore && redoStack.Count == redoBefore, "A rejected raw stable-ID change preserves Undo and Redo history");
        window.SetJsonSource(question.ToJsonString()); window.CommitCurrent(); tabs.SelectedIndex = 0;
        window.RedoItem();
        Check(window.GetField("hint") == "An edit retained for command-history regression.", "Redo still restores the valid preceding edit after a rejected raw change");
        window.UndoItem(); undoBefore = undoStack.Count; redoBefore = redoStack.Count;
        bool blankReviewer = false; try { window.RecordReview("", "An incomplete author review."); } catch (ArgumentException) { blankReviewer = true; }
        Check(blankReviewer && undoStack.Count == undoBefore && redoStack.Count == redoBefore, "A rejected author review cannot consume valid command history");
        window.SetField("stem", "A clinical question with Unicode: μ, ±, ≥ and café.");
        tabs.SelectedIndex = 1;
        Check(raw.Text.Contains("clinical question") && S(window.Document.GetItems("questions").Single(row => S(row, "id") == "question-1"), "stem").Contains("café"), "Switching from fields to JSON commits full Unicode question text");
        raw.Text = "{ unfinished JSON"; tabs.SelectedIndex = 0;
        Check(tabs.SelectedIndex == 1 && raw.Text == "{ unfinished JSON" && window.HasUnsavedChanges, "A real tab change refuses malformed JSON while retaining its exact draft");
        question = window.Document.GetItems("questions").Single(row => S(row, "id") == "question-1");
        window.SetJsonSource(question.ToJsonString()); window.CommitCurrent(); tabs.SelectedIndex = 0;
        window.SaveCourse();
        Check(S(TutorCourseDocument.Open(path).Content["questions"]![0], "stem").Contains("café"), "Correcting a rejected raw draft lets Save retain the preceding Unicode field edit");
        window.SelectItem("lessons", "lesson-1");
        string originalColumns = window.GetField("columns"); string beforeBadColumns = window.Document.Content.ToJsonString();
        window.SetField("columns", "{\"not\":\"an array\"}"); bool badColumns = false;
        try { window.SelectItem("questions", "question-1"); } catch (InvalidOperationException) { badColumns = true; }
        Check(badColumns && window.SelectedKind == "lessons" && window.Document.Content.ToJsonString() == beforeBadColumns, "Invalid example-data JSON blocks navigation without modifying the lesson");
        window.SetField("columns", originalColumns); window.CommitCurrent();
        var menu = window.MainMenuStrip!;
        ToolStripMenuItem Item(string label) => menu.Items.OfType<ToolStripMenuItem>().SelectMany(parent => parent.DropDownItems.OfType<ToolStripMenuItem>()).Single(item => item.Text == label);
        string lessonBeforeDelete = window.Document.GetItems("lessons").Single(row => S(row, "id") == "lesson-1").ToJsonString();
        Item("Delete selected item…").PerformClick();
        Check(window.SelectedKind == "course" && window.Document.GetItems("lessons").Count == 0 && window.Document.DeletedItems().Count > 0, "Native Delete moves a lesson into course recovery and selects course metadata");
        window.UndoItem();
        Check(window.Document.GetItems("lessons").Single().ToJsonString() == lessonBeforeDelete, "Native Undo after Delete restores the complete lesson");
        window.RedoItem();
        metadata = Metadata(); metadata.Remove("authoring");
        window.SetJsonSource(metadata.ToJsonString()); window.CommitCurrent();
        Check(window.Document.DeletedItems().Any(item => item.Kind == "lessons"), "Deleting course authoring metadata through JSON preserves the lesson recovery archive");
        string beforeBadMetadata = window.Document.Content.ToJsonString(); metadata = Metadata(); metadata["authoring"] = "A note typed into the wrong structure";
        window.SetJsonSource(metadata.ToJsonString()); bool badAuthoring = false;
        try { window.CommitCurrent(); } catch (InvalidDataException) { badAuthoring = true; }
        Check(badAuthoring && window.Document.Content.ToJsonString() == beforeBadMetadata && raw.Text.Contains("wrong structure"), "Malformed course authoring metadata retains the raw note and does not erase saved recovery data");
        window.SetJsonSource(Metadata().ToJsonString()); window.CommitCurrent();
        window.SaveCourse();
        var reopened = TutorCourseDocument.Open(path); var deletion = reopened.DeletedItems().Single(item => item.Kind == "lessons"); reopened.Restore(deletion.Id); reopened.Save();
        Check(reopened.GetItems("lessons").Single().ToJsonString() == lessonBeforeDelete, "Deletion recovery survives native Save/reopen with exact lesson content");
        Item("New &multiple-choice question").PerformClick();
        string newId = window.SelectedId;
        Check(window.SelectedKind == "questions" && newId != "question-1" && window.HasUnsavedChanges, "Native New question creates a distinct editable draft");
        window.SetField("stem", "A newly authored clinical exercise."); window.CommitCurrent(); Item("Duplicate selected item").PerformClick();
        Check(window.SelectedId != newId && S(window.Document.GetItems("questions").Single(row => S(row, "id") == window.SelectedId), "stem") == "A newly authored clinical exercise.", "Native Duplicate keeps the exercise under a new stable ID");
        window.UndoItem();
        Check(!window.Document.GetItems("questions").Any(row => S(row, "id") == window.SelectedId) || window.SelectedKind == "course", "Undoing a newly created selected duplicate returns to a valid selection");
        window.Close();
        var malformed = (JsonObject)initial.DeepClone(); malformed["questions"]![0]!["authoring"] = "Imported author note that must remain recoverable.";
        string malformedPath = Path.Combine(fixture, "malformed-item-metadata.json"); File.WriteAllText(malformedPath, malformed.ToJsonString());
        using var recoverable = new TutorEditorForm(malformedPath, fixture, externalLinkLauncher: _ => {}) { SuppressDialogs = true, ShowInTaskbar = false, WindowState = FormWindowState.Normal, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0) };
        recoverable.Show();
        recoverable.SelectItem("questions", "question-1");
        Check(recoverable.SelectedId == "question-1" && S(recoverable.Document.Content["questions"]![0], "authoring").Contains("Imported author note"), "Malformed imported item metadata remains visible and recoverable without crashing the editor");
        var corrected = recoverable.Document.GetItems("questions")[0]; corrected["authoring"] = new JsonObject { ["notes"] = "Imported author note that must remain recoverable." };
        recoverable.SetJsonSource(corrected.ToJsonString()); recoverable.CommitCurrent(); recoverable.SaveCourse();
        Check(S(TutorCourseDocument.Open(malformedPath).Content["questions"]![0]!["authoring"], "notes").Contains("Imported author note"), "Correcting imported metadata through JSON retains the original note after Save/reopen");
    }
}
