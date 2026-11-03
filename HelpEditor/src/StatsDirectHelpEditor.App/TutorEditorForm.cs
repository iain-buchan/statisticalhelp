using System.Diagnostics;
using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

/// <summary>Course-author workspace. Edits a copy; exporting never changes the learner application.</summary>
public sealed partial class TutorEditorForm : Form
{
    private readonly string workspaceRoot;
    private readonly Func<Task<HelpPolishContext?>> helpContext;
    private readonly Action<Uri> openLink;
    private readonly TreeView navigation = new() { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true };
    private readonly TextBox filter = new() { Dock = DockStyle.Top, PlaceholderText = "Find a lesson or question…" };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly Panel fieldsHost = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
    private readonly TextBox source = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsTab = true, AcceptsReturn = true, WordWrap = false, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 11) };
    private readonly WebView2 preview = new() { Dock = DockStyle.Fill };
    private readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label heading = new() { Dock = DockStyle.Top, Height = 48, Padding = new Padding(12, 6, 12, 2), Font = new Font("Arial", 13, FontStyle.Bold), BackColor = Color.FromArgb(230, 239, 249) };
    private readonly Dictionary<string, TextBox> fields = [];
    private readonly Dictionary<string, string> initialFieldValues = [];
    private readonly Dictionary<string, DataGridView> teachingGrids = [];
    private readonly HashSet<string> changedTeachingGrids = [];
    private readonly Stack<string> undo = [], redo = [];
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    private DataGridView? options, references;
    private TutorCourseDocument document;
    private string saved, selectedKind = "course", selectedId = "", fieldBaseline = "";
    private JsonObject current = new();
    private bool loading, pending, rawMode, busy, previewReady;
    private string? previewDocumentUri;
    private bool optionsEdited, referencesEdited;
    private Icon? ownedIcon;

    public TutorCourseDocument Document => document;
    public bool HasUnsavedChanges => pending || EditableGrids().Any(grid => grid.IsCurrentCellDirty) || document.Content.ToJsonString() != saved;
    public string SelectedKind => selectedKind;
    public string SelectedId => selectedId;
    public string? LastError { get; private set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool SuppressDialogs { get; set; }

    public TutorEditorForm(string coursePath, string workspaceRoot, Func<Task<HelpPolishContext?>>? helpContext = null, Action<Uri>? externalLinkLauncher = null)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
        this.helpContext = helpContext ?? (() => Task.FromResult<HelpPolishContext?>(null));
        openLink = externalLinkLauncher ?? (uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        document = TutorCourseDocument.Open(coursePath);
        saved = document.Content.ToJsonString();
        Text = "StatsDirect Tutor — course authoring";
        Font = new Font("Arial", 11); Size = new Size(1500, 980); MinimumSize = new Size(1050, 700);
        StartPosition = FormStartPosition.CenterScreen; WindowState = FormWindowState.Maximized;
        using (var icon = typeof(TutorEditorForm).Assembly.GetManifestResourceStream("StatsDirect.HelpEditor.AppIcon"))
            if (icon != null) { using var read = new Icon(icon); ownedIcon = (Icon)read.Clone(); Icon = ownedIcon; }
        var menu = BuildMenu();
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1450, 850), Panel1MinSize = 230, Panel2MinSize = 650, SplitterDistance = 325 };
        split.Panel1.Controls.Add(navigation); split.Panel1.Controls.Add(filter);
        var edit = new TabPage("Edit material"); edit.Controls.Add(fieldsHost);
        var raw = new TabPage("JSON source"); raw.Controls.Add(source);
        var student = new TabPage("Learner preview"); student.Controls.Add(preview);
        tabs.TabPages.AddRange([edit, raw, student]);
        tabs.Appearance = TabAppearance.Buttons; tabs.Font = new Font("Arial", 11, FontStyle.Bold); tabs.Padding = new Point(14, 8); fieldsHost.Font = new Font("Arial", 11);
        split.Panel2.Controls.Add(tabs); split.Panel2.Controls.Add(heading);
        var footer = new StatusStrip(); footer.Items.Add(status);
        Controls.Add(split); Controls.Add(footer); Controls.Add(menu); MainMenuStrip = menu;
        navigation.BeforeSelect += (_, e) => { if (!loading && !TryCommit()) e.Cancel = true; };
        navigation.AfterSelect += (_, e) => { if (!loading && e.Node?.Tag is ItemKey key) LoadItem(key.Kind, key.Id); };
        filter.TextChanged += (_, _) => { if (TryCommit()) RefreshTree(); };
        source.TextChanged += (_, _) => { if (!loading && rawMode) Changed(); };
        tabs.Selecting += (_, e) => { if (!loading && !TryCommit()) e.Cancel = true; };
        tabs.Selected += async (_, _) =>
        {
            if (loading) return;
            rawMode = tabs.SelectedIndex == 1;
            if (rawMode) { loading = true; source.Text = current.ToJsonString(jsonOptions); loading = false; }
            else if (tabs.SelectedIndex == 0) BuildFields();
            else await RunAsync(ShowPreviewAsync);
        };
        FormClosing += (_, e) => { if (!CanLeave()) e.Cancel = true; };
        RefreshTree(); LoadItem("course", "");
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Font = new Font("Arial", 12), BackColor = SystemColors.Window };
        var file = new ToolStripMenuItem("&File");
        Add(file, "&Open course source…", OpenCourse, Keys.Control | Keys.O);
        Add(file, "Start a &new course from the basics…", NewCourse);
        Add(file, "Open health and biomedical examples…", OpenExamples);
        file.DropDownItems.Add(new ToolStripSeparator());
        Add(file, "&Save course source", SaveCourse, Keys.Control | Keys.S);
        Add(file, "Save course source &as…", SaveCourseAs);
        Add(file, "&Publish built-in tutor for StatsDirect builds…", PublishBuiltin);
        Add(file, "Export optional training-provider course pack…", ExportCourse);
        file.DropDownItems.Add(new ToolStripSeparator()); Add(file, "Close", Close);
        var edit = new ToolStripMenuItem("&Edit");
        Add(edit, "New &lesson", () => AddItem("lessons")); Add(edit, "New &multiple-choice question", () => AddItem("questions"));
        Add(edit, "New reading / course document", () => AddItem("documents"));
        edit.DropDownItems.Add(new ToolStripSeparator());
        Add(edit, "Duplicate selected item", DuplicateItem); Add(edit, "Move up", () => MoveItem(-1)); Add(edit, "Move down", () => MoveItem(1));
        Add(edit, "Delete selected item…", DeleteItem); Add(edit, "Recover deleted material…", RestoreItem);
        edit.DropDownItems.Add(new ToolStripSeparator());
        Add(edit, "Undo last item change", UndoItem, Keys.Control | Keys.Alt | Keys.Z); Add(edit, "Redo item change", RedoItem, Keys.Control | Keys.Alt | Keys.Y);
        var review = new ToolStripMenuItem("&Review");
        Add(review, "Check course / export requirements", ValidateCourse);
        Add(review, "Record author review…", RecordAuthorReview);
        var ai = new ToolStripMenuItem("&ChatGPT");
        var research = new ToolStripMenuItem("Research, draft or review material…"); research.Click += async (_, _) => await RunAsync(ResearchAsync); ai.DropDownItems.Add(research);
        var help = new ToolStripMenuItem("&Help");
        Add(help, "How to use this authoring workspace", () => MessageBox.Show(this,
            "Start with the supplied lessons and questions, then adapt the wording, examples, answer feedback and sources.\n\nSave keeps an editable draft, including recovery and author notes. Record your review, then Publish built-in tutor to write the sources used by the next StatsDirect build. New or changed material needs a review of that exact version.\n\nChatGPT can research sources and propose changes and R checks. Review these before publishing. Optional provider-course export is also available. The learner app records practice, reflections and evidence; recognition for assessment or CPD is determined by the relevant provider.", "Tutor authoring", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.AddRange([file, edit, review, ai, help]); return menu;
    }
    private void Add(ToolStripMenuItem parent, string text, Action action, Keys shortcut = Keys.None)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
        item.Click += (_, _) => { try { if (!busy) action(); } catch (Exception error) { Error(error); } };
        parent.DropDownItems.Add(item);
    }
    private static string S(JsonObject row, string key) => row[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : row[key]?.ToJsonString() ?? "";
    private static string NodeText(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node?.ToJsonString() ?? "";
    private sealed record ItemKey(string Kind, string Id);
    private void Changed() { pending = true; Text = "StatsDirect Tutor — course authoring *"; }
    private void Remember(string snapshot) { undo.Push(snapshot); if (undo.Count > 100) { var keep = undo.Take(100).Reverse().ToArray(); undo.Clear(); foreach (var state in keep) undo.Push(state); } redo.Clear(); }
    private void ChangeDocument(Action change)
    {
        string before = document.Content.ToJsonString();
        try { change(); }
        catch { if (document.Content.ToJsonString() != before) ReplaceContent(before); throw; }
        if (document.Content.ToJsonString() != before) Remember(before);
    }
    private void ReplaceContent(string text) { var replacement = JsonNode.Parse(text)!.AsObject(); document.Content.Clear(); foreach (var pair in replacement.ToArray()) document.Content[pair.Key] = pair.Value?.DeepClone(); }

    private void RefreshTree()
    {
        loading = true; navigation.BeginUpdate(); navigation.Nodes.Clear();
        navigation.Nodes.Add(new TreeNode("Course and audience") { Tag = new ItemKey("course", "") });
        foreach (string kind in new[] { "lessons", "questions", "documents" })
        {
            var rows = (document.Content[kind] as JsonArray)?.OfType<JsonObject>().ToArray() ?? [];
            var branch = new TreeNode((kind == "lessons" ? "Lessons and exercises" : kind == "questions" ? "Multiple-choice questions" : "Readings") + " (" + rows.Length + ")");
            foreach (var row in rows)
            {
                string title = S(row, kind == "questions" ? "stem" : "title");
                if (!string.IsNullOrWhiteSpace(filter.Text) && !(title + " " + S(row, "id") + " " + S(row, "topic")).Contains(filter.Text, StringComparison.OrdinalIgnoreCase)) continue;
                title = title.ReplaceLineEndings(" "); if (title.Length > 82) title = title[..79] + "…";
                branch.Nodes.Add(new TreeNode(title.Length > 0 ? title : "Untitled draft") { Tag = new ItemKey(kind, S(row, "id")) });
            }
            navigation.Nodes.Add(branch); branch.Expand();
        }
        foreach (TreeNode node in navigation.Nodes) foreach (TreeNode candidate in new[] { node }.Concat(node.Nodes.Cast<TreeNode>()))
            if (candidate.Tag is ItemKey key && key.Kind == selectedKind && key.Id == selectedId) navigation.SelectedNode = candidate;
        navigation.EndUpdate(); loading = false; UpdateStatus();
    }
    public void SelectItem(string kind, string id) { CommitCurrent(); LoadItem(kind, id); RefreshTree(); }
    private void LoadItem(string kind, string id)
    {
        selectedKind = kind; selectedId = id;
        current = kind == "course" ? CourseMetadata() : (JsonObject)((document.Content[kind] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(row => S(row, "id") == id)?.DeepClone() ?? throw new InvalidDataException("The selected item no longer exists."));
        fieldBaseline = current.ToJsonString(); pending = false; loading = true;
        heading.Text = kind == "course" ? "Course identity, audience and assessment" : (kind == "questions" ? "Multiple-choice question" : kind == "documents" ? "Course reading" : "Lesson / practical exercise") + " · " + id;
        source.Text = current.ToJsonString(jsonOptions); rawMode = tabs.SelectedIndex == 1;
        BuildFields(); loading = false; UpdateStatus();
        if (tabs.SelectedIndex == 2) _ = RunAsync(ShowPreviewAsync);
    }
    private void BuildFields()
    {
        bool wasLoading = loading; loading = true;
        foreach (Control control in fieldsHost.Controls.Cast<Control>().ToArray()) control.Dispose();
        fieldsHost.Controls.Clear(); fields.Clear(); initialFieldValues.Clear(); teachingGrids.Clear(); changedTeachingGrids.Clear(); options = null; references = null; optionsEdited = false; referencesEdited = false;
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(14), BackColor = Color.White };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fieldsHost.Controls.Add(table);
        void Field(string key, string label, int lines = 1, string? value = null)
        {
            var input = new TextBox { Text = value ?? S(current, key), Multiline = lines > 1, AcceptsReturn = lines > 1, ScrollBars = lines > 1 ? ScrollBars.Vertical : ScrollBars.None, Dock = DockStyle.Fill, Height = Math.Max(30, lines * 23), Margin = new Padding(3, 4, 3, 9), AccessibleName = label };
            if (key is "r" or "columns") input.Font = new Font("Consolas", 10);
            input.TextChanged += (_, _) => { if (!loading) Changed(); }; fields.Add(key, input);
            initialFieldValues[key] = input.Text;
            int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 8, 5, 0) }, 0, row);
            row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(input, 0, row);
        }
        if (selectedKind == "course")
        {
            var provider = current["provider"] as JsonObject ?? new(); var authoring = current["authoring"] as JsonObject ?? new();
            Field("id", "Course ID"); Field("title", "Course title"); Field("version", "Course version");
            Field("provider.name", "Training provider", value: S(provider, "name")); Field("provider.reviewEmail", "Review email", value: S(provider, "reviewEmail"));
            Field("authoring.audience", "Audience and level", 3, S(authoring, "audience")); Field("authoring.learningObjectives", "Course objectives", 4, S(authoring, "learningObjectives"));
            Field("authoring.setting", "Teaching / pilot setting", 2, S(authoring, "setting"));
            Field("provider.assessmentType", "Assessment: none / self / cpd", value: S(provider, "assessmentType"));
            Field("provider.assessmentRequirements", "Assessment / learning evidence", 4, S(provider, "assessmentRequirements")); Field("provider.cpdStatement", "CPD statement", 3, S(provider, "cpdStatement"));
            Field("resourceURLs", "Course links (one HTTPS URL per line)", 3, Lines(current["resourceURLs"]));
        }
        else
        {
            Field("id", "Stable item ID"); fields["id"].ReadOnly = true;
            if (selectedKind == "questions")
            {
                Field("topic", "Topic"); Field("track", "Learning pathway key: foundation, medicine, publicHealth, analyst, researcher or core"); Field("objective", "Learning objective", 2); Field("stem", "Question / clinical scenario", 5);
                fields["track"].AutoCompleteCustomSource.AddRange(["foundation", "medicine", "publicHealth", "analyst", "researcher", "core"]); fields["track"].AutoCompleteSource = AutoCompleteSource.CustomSource; fields["track"].AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                options = Grid("Choice", "Answer option", "Feedback for this choice");
                foreach (var option in (current["options"] as JsonArray ?? []).OfType<JsonObject>()) { int index = options.Rows.Add(S(option, "id"), S(option, "text"), S(option, "feedback")); options.Rows[index].Tag = option.DeepClone(); }
                AddGrid(table, "Answer choices", options, 255);
                options.CellValueChanged += (_, _) => { if (!loading) optionsEdited = true; }; options.RowsRemoved += (_, _) => { if (!loading) optionsEdited = true; };
                Field("correct", "Correct choice (A–H)"); Field("hint", "Hint", 2); Field("explanation", "Correct answer and reasoning", 5); Field("lesson", "Teaching explanation", 4); Field("help", "Related help topic"); Field("operation", "StatsDirect operation ID (optional)");
            }
            else if (selectedKind == "documents") { Field("title", "Reading title"); Field("text", "Course reading", 18); Field("url", "Source URL (HTTPS)"); }
            else
            {
                Field("title", "Lesson title"); Field("topic", "Topic"); Field("objective", "Learning objective", 3); Field("summary", "Teaching content", 9); Field("challenge", "Exercise / question", 4); Field("steps", "Practical steps / worked example", 8); Field("help", "Related help topic"); Field("operation", "StatsDirect operation ID (optional)"); Field("r", "R code (reviewed, never run here)", 12);
                Field("columns", "Example data columns (JSON)", 5, (current["columns"] ?? new JsonArray()).ToJsonString(jsonOptions));
                var pasteData = new Button { Text = "Paste example data from Excel", AutoSize = true, Margin = new Padding(3, 4, 3, 12) };
                pasteData.Click += (_, _) => { try { PasteExampleData(Clipboard.GetText()); } catch (Exception error) { Error(error); } };
                int dataRow = table.RowCount++; table.Controls.Add(pasteData, 0, dataRow);
                Field("learningObjectives", "Additional objectives (one per line)", 4, Lines(current["learningObjectives"]));
                Field("prerequisites", "Prerequisite lesson IDs (one per line)", 2, Lines(current["prerequisites"]));
                void TeachingGrid(string key, string label, params (string Key, string Label)[] columns)
                {
                    var grid = Grid(columns.Select(pair => pair.Label).ToArray()); grid.Tag = columns.Select(pair => pair.Key).ToArray();
                    foreach (var row in (current[key] as JsonArray ?? []).OfType<JsonObject>()) { int at = grid.Rows.Add(columns.Select(pair => (object)S(row, pair.Key)).ToArray()); grid.Rows[at].Tag = row.DeepClone(); }
                    teachingGrids[key] = grid; AddGrid(table, label, grid, 235);
                    grid.CellValueChanged += (_, _) => { if (!loading) changedTeachingGrids.Add(key); }; grid.RowsRemoved += (_, _) => { if (!loading) changedTeachingGrids.Add(key); };
                }
                TeachingGrid("keyConcepts", "Key concepts", ("term", "Term"), ("explanation", "Explanation"));
                TeachingGrid("misconceptions", "Misconceptions", ("claim", "Claim"), ("correction", "Correction"));
                TeachingGrid("teachingPrompts", "Teaching prompts", ("level", "Level"), ("prompt", "Prompt"), ("expectedReasoning", "Expected reasoning"));
                Field("workedExample.design", "Worked example: design", 3, S(current["workedExample"] as JsonObject ?? new(), "design"));
                Field("workedExample.interpretation", "Worked example: interpretation", 4, S(current["workedExample"] as JsonObject ?? new(), "interpretation"));
                foreach (string stage in new[] { "menus", "bridge", "coding" }) Field("rProgression." + stage, "R progression: " + stage, 3, S(current["rProgression"] as JsonObject ?? new(), stage));
            }
            if (selectedKind != "documents")
            {
                references = Grid("Title", "HTTPS URL", "What this source supports");
                foreach (var reference in (current["sources"] as JsonArray ?? []).OfType<JsonObject>()) { int index = references.Rows.Add(S(reference, "title"), S(reference, "url"), S(reference, "scope")); references.Rows[index].Tag = reference.DeepClone(); }
                var present = (current["sources"] as JsonArray ?? []).OfType<JsonObject>().Select(row => S(row, "url")).ToHashSet(StringComparer.Ordinal);
                foreach (var link in current["readingURLs"] as JsonArray ?? []) if (present.Add(NodeText(link))) references.Rows.Add("", NodeText(link), "");
                AddGrid(table, "Sources and reading", references, 210);
                references.CellValueChanged += (_, _) => { if (!loading) referencesEdited = true; }; references.RowsRemoved += (_, _) => { if (!loading) referencesEdited = true; };
                var review = current["review"] as JsonObject ?? new();
                Field("review.status", "Review status", value: S(review, "status")); Field("review.notes", "Author / statistical review notes", 4, S(review, "notes"));
                if (current["authoring"] is JsonObject itemAuthoring && itemAuthoring["lastAiReview"] is JsonObject aiReview)
                {
                    Field("_displayAiSummary", "Previous ChatGPT review", 4, S(aiReview, "Summary")); fields["_displayAiSummary"].ReadOnly = true;
                    Field("_displayRCheck", "Proposed R validation (not executed here)", 8, S(aiReview, "proposedRCheck")); fields["_displayRCheck"].ReadOnly = true; fields["_displayRCheck"].Font = new Font("Consolas", 10);
                }
            }
        }
        loading = wasLoading;
    }
    private DataGridView Grid(params string[] columns)
    {
        var grid = new DataGridView { AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, RowHeadersWidth = 28, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, AllowUserToAddRows = true, AllowUserToDeleteRows = true, Dock = DockStyle.Fill, DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }, EditMode = DataGridViewEditMode.EditOnEnter };
        foreach (string label in columns) grid.Columns.Add(label, label);
        if (columns[0] == "Choice") grid.Columns[0].FillWeight = 18;
        Control? previousEditor = null; EventHandler? editorChanged = null;
        grid.EditingControlShowing += (_, e) =>
        {
            if (previousEditor != null && editorChanged != null) previousEditor.TextChanged -= editorChanged;
            previousEditor = e.Control;
            editorChanged = (_, _) => { if (!loading) { DirtyGrid(grid); Changed(); } };
            previousEditor.TextChanged += editorChanged;
        };
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (!loading && grid.IsCurrentCellDirty) { DirtyGrid(grid); Changed(); } };
        grid.CellValueChanged += (_, _) => { if (!loading) Changed(); }; grid.RowsRemoved += (_, _) => { if (!loading) Changed(); }; return grid;
    }
    private IEnumerable<DataGridView> EditableGrids() => new[] { options, references }.Where(grid => grid != null).Cast<DataGridView>().Concat(teachingGrids.Values);
    private void DirtyGrid(DataGridView grid)
    { if (ReferenceEquals(grid, options)) optionsEdited = true; else if (ReferenceEquals(grid, references)) referencesEdited = true; else foreach (var pair in teachingGrids) if (ReferenceEquals(pair.Value, grid)) changedTeachingGrids.Add(pair.Key); }
    private static void AddGrid(TableLayoutPanel table, string label, DataGridView grid, int height)
    { int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font(table.Font, FontStyle.Bold), Padding = new Padding(0, 8, 5, 0) }, 0, row); row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); table.Controls.Add(grid, 0, row); }
    private JsonObject ReadEditor()
    {
        if (rawMode) return JsonNode.Parse(source.Text)?.AsObject() ?? throw new InvalidDataException("Enter a JSON object for this item.");
        var result = (JsonObject)current.DeepClone(); options?.EndEdit(); references?.EndEdit();
        foreach (var pair in fields)
        {
            if (pair.Key.StartsWith("_display", StringComparison.Ordinal)) continue;
            if (initialFieldValues.TryGetValue(pair.Key, out string? originalValue) && pair.Value.Text == originalValue) continue;
            if (pair.Key is "columns" or "resourceURLs" or "learningObjectives" or "prerequisites")
            { result[pair.Key] = pair.Key == "columns" ? JsonNode.Parse(pair.Value.Text)?.AsArray() ?? new JsonArray() : new JsonArray(pair.Value.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()); continue; }
            if (pair.Key.Contains('.'))
            { var parts = pair.Key.Split('.', 2); if (result[parts[0]] is not JsonObject) result[parts[0]] = new JsonObject(); result[parts[0]]![parts[1]] = pair.Value.Text; }
            else result[pair.Key] = pair.Value.Text;
        }
        if (options != null && optionsEdited)
        {
            var values = new JsonArray();
            foreach (DataGridViewRow row in options.Rows) if (!row.IsNewRow && row.Cells.Cast<DataGridViewCell>().Any(cell => !string.IsNullOrWhiteSpace(cell.Value?.ToString())))
            { var value = row.Tag is JsonObject old ? (JsonObject)old.DeepClone() : new JsonObject(); value["id"] = row.Cells[0].Value?.ToString()?.Trim().ToUpperInvariant() ?? ""; value["text"] = row.Cells[1].Value?.ToString() ?? ""; value["feedback"] = row.Cells[2].Value?.ToString() ?? ""; values.Add(value); }
            result["options"] = values;
        }
        if (references != null && referencesEdited)
        {
            var values = new JsonArray();
            foreach (DataGridViewRow row in references.Rows) if (!row.IsNewRow && row.Cells.Cast<DataGridViewCell>().Any(cell => !string.IsNullOrWhiteSpace(cell.Value?.ToString())))
            { var value = row.Tag is JsonObject old ? (JsonObject)old.DeepClone() : new JsonObject(); value["title"] = row.Cells[0].Value?.ToString() ?? ""; value["url"] = row.Cells[1].Value?.ToString()?.Trim() ?? ""; value["scope"] = row.Cells[2].Value?.ToString() ?? ""; values.Add(value); }
            result["sources"] = values;
            if (selectedKind == "lessons") result["readingURLs"] = new JsonArray(values.OfType<JsonObject>().Select(value => (JsonNode?)JsonValue.Create(S(value, "url"))).Where(value => !string.IsNullOrWhiteSpace(value?.GetValue<string>())).ToArray());
        }
        foreach (var grid in teachingGrids)
        {
            grid.Value.EndEdit(); if (!changedTeachingGrids.Contains(grid.Key)) continue; var values = new JsonArray(); var keys = (string[])grid.Value.Tag!;
            foreach (DataGridViewRow row in grid.Value.Rows) if (!row.IsNewRow && row.Cells.Cast<DataGridViewCell>().Any(cell => !string.IsNullOrWhiteSpace(cell.Value?.ToString())))
            { var value = row.Tag is JsonObject old ? (JsonObject)old.DeepClone() : new JsonObject(); for (int column = 0; column < keys.Length; column++) value[keys[column]] = row.Cells[column].Value?.ToString() ?? ""; values.Add(value); }
            result[grid.Key] = values;
        }
        return result;
    }
    private static string Lines(JsonNode? value) => string.Join(Environment.NewLine, (value as JsonArray ?? []).Select(NodeText));
    public void PasteExampleData(string text)
    {
        if (selectedKind != "lessons" || !fields.ContainsKey("columns")) throw new InvalidOperationException("Select a lesson to paste its example data.");
        string[] rows = text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        if (rows.Length < 2) throw new InvalidDataException("Copy Excel cells including a header row and at least one numeric data row.");
        string[] headers = rows[0].Split('\t');
        if (headers.Length > 32 || (rows.Length - 1) * headers.Length > 20000 || headers.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Use named columns, at most 32 columns and 20,000 numeric values.");
        var columns = headers.Select(header => new JsonObject { ["title"] = header.Trim(), ["values"] = new JsonArray() }).ToArray();
        foreach (string row in rows.Skip(1))
        {
            string[] cells = row.Split('\t'); if (cells.Length != headers.Length) throw new InvalidDataException("Each example-data row must have the same number of columns.");
            for (int column = 0; column < cells.Length; column++)
            {
                if (!double.TryParse(cells[column], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number)) throw new InvalidDataException("Example data require a finite number in every cell; check blank cells and decimal points.");
                columns[column]["values"]!.AsArray().Add(number);
            }
        }
        fields["columns"].Text = new JsonArray(columns.Select(column => (JsonNode)column).ToArray()).ToJsonString(jsonOptions);
    }
    public void CommitCurrent()
    {
        foreach (var grid in EditableGrids())
        {
            bool dirtyCell = grid.IsCurrentCellDirty;
            if (!grid.EndEdit()) throw new InvalidDataException("Finish the current table cell before saving or changing pages.");
            if (dirtyCell) { DirtyGrid(grid); pending = true; }
        }
        if (!pending) return;
        var edited = ReadEditor();
        if (edited.ToJsonString() != fieldBaseline)
        {
            if (selectedKind == "course")
            {
                // Course editing may change metadata only; collections and recovery stay live.
                var revised = (JsonObject)document.Content.DeepClone();
                JsonNode? recovery = (document.Content["authoring"] as JsonObject)?["deletedItems"]?.DeepClone();
                foreach (string key in revised.Select(pair => pair.Key).Where(key => key is not ("lessons" or "questions" or "documents") && !edited.ContainsKey(key)).ToArray()) revised.Remove(key);
                foreach (var pair in edited) if (pair.Key is not ("lessons" or "questions" or "documents")) revised[pair.Key] = pair.Value?.DeepClone();
                if (revised["authoring"] != null && revised["authoring"] is not JsonObject) throw new InvalidDataException("Course authoring metadata must be a JSON object; the current document and recovery archive are unchanged.");
                if (recovery != null)
                {
                    if (revised["authoring"] is not JsonObject local) revised["authoring"] = local = new JsonObject();
                    local["deletedItems"] = recovery;
                }
                _ = TutorCourseDocument.Create(revised); // Reject a malformed root without corrupting the live draft.
                ChangeDocument(() => ReplaceContent(revised.ToJsonString()));
            }
            else ChangeDocument(() => document.Update(selectedKind, selectedId, edited));
            selectedId = selectedKind == "course" ? "" : S(edited, "id");
        }
        current = selectedKind == "course" ? CourseMetadata() : (JsonObject)document.GetItems(selectedKind).First(row => S(row, "id") == selectedId).DeepClone();
        fieldBaseline = current.ToJsonString(); pending = false;
        loading = true;
        if (rawMode) { int caret = source.SelectionStart; source.Text = current.ToJsonString(jsonOptions); source.SelectionStart = Math.Min(caret, source.TextLength); }
        else
        {
            foreach (string key in new[] { "review.status", "review.notes" }) if (fields.TryGetValue(key, out var field)) field.Text = S(current["review"] as JsonObject ?? new(), key.Split('.')[1]);
            foreach (var pair in fields) initialFieldValues[pair.Key] = pair.Value.Text;
            optionsEdited = false; referencesEdited = false; changedTeachingGrids.Clear();
        }
        loading = false; UpdateStatus();
    }
    private bool TryCommit() { try { CommitCurrent(); return true; } catch (Exception error) { Error(error); return false; } }
    private JsonObject CourseMetadata() { var metadata = (JsonObject)document.Content.DeepClone(); foreach (var key in new[] { "lessons", "questions", "documents" }) metadata.Remove(key); return metadata; }
    public void SetField(string key, string text) { fields[key].Text = text; }
    public string GetField(string key) => fields[key].Text;
    public void SetJsonSource(string json) { tabs.SelectedIndex = 1; source.Text = json; }
    public void UndoItem() { CommitCurrent(); if (undo.Count == 0) return; redo.Push(document.Content.ToJsonString()); ReplaceContent(undo.Pop()); ReloadSelection(); }
    public void RedoItem() { CommitCurrent(); if (redo.Count == 0) return; undo.Push(document.Content.ToJsonString()); ReplaceContent(redo.Pop()); ReloadSelection(); }
    private void ReloadSelection() { if (selectedKind != "course" && !document.GetItems(selectedKind).Any(row => S(row, "id") == selectedId)) { selectedKind = "course"; selectedId = ""; } LoadItem(selectedKind, selectedId); RefreshTree(); }
    private void AddItem(string kind)
    {
        CommitCurrent(); JsonObject item = null!;
        ChangeDocument(() => item = kind == "lessons" ? document.NewLesson() : kind == "questions" ? document.NewQuestion() : document.NewDocument());
        LoadItem(kind, S(item, "id")); RefreshTree(); tabs.SelectedIndex = 0;
    }
    private void DuplicateItem() { CommitCurrent(); if (selectedKind == "course") return; JsonObject clone = null!; ChangeDocument(() => clone = document.Duplicate(selectedKind, selectedId)); LoadItem(selectedKind, S(clone, "id")); RefreshTree(); }
    private void MoveItem(int direction) { CommitCurrent(); if (selectedKind == "course") return; ChangeDocument(() => document.Move(selectedKind, selectedId, direction)); RefreshTree(); }
    private void DeleteItem()
    {
        CommitCurrent(); if (selectedKind == "course") return;
        if (!SuppressDialogs && MessageBox.Show(this, "Delete this item from the course? You can recover it through Edit > Recover deleted material.", "Delete material", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        ChangeDocument(() => document.Delete(selectedKind, selectedId)); LoadItem("course", ""); RefreshTree();
    }
    private void RestoreItem()
    {
        CommitCurrent(); using var dialog = new Form { Text = "Recover deleted tutor material", Size = new Size(780, 430), StartPosition = FormStartPosition.CenterParent, Font = Font };
        var list = new ListBox { Dock = DockStyle.Fill, DisplayMember = "Title" }; foreach (var item in document.DeletedItems()) list.Items.Add(item);
        var button = new Button { Text = "Recover selected", Dock = DockStyle.Bottom, Height = 40 };
        button.Click += (_, _) => { if (list.SelectedItem is TutorDeletedItem item) { try { ChangeDocument(() => document.Restore(item.Id)); dialog.DialogResult = DialogResult.OK; } catch (Exception error) { Error(error); } } };
        dialog.Controls.Add(list); dialog.Controls.Add(button); if (dialog.ShowDialog(this) == DialogResult.OK) ReloadSelection();
    }
    public void SaveCourse() { CommitCurrent(); if (document.FilePath == null) { SaveCourseAs(); return; } document.Save(); saved = document.Content.ToJsonString(); ReloadSelection(); status.Text = "Saved course source · " + document.FilePath; }
    private void SaveCourseAs()
    {
        CommitCurrent(); using var picker = new SaveFileDialog { Title = "Save editable tutor course", Filter = "Tutor course source (*.json)|*.json", FileName = Path.GetFileName(document.FilePath ?? "my-course.json"), InitialDirectory = Path.Combine(workspaceRoot, "TutorSource"), AddExtension = true, DefaultExt = "json" };
        if (picker.ShowDialog(this) != DialogResult.OK) return; document.Save(picker.FileName); saved = document.Content.ToJsonString(); ReloadSelection(); status.Text = "Saved · " + document.FilePath;
    }
    private bool CanLeave()
    {
        if (busy) return false; if (!HasUnsavedChanges) return true; if (SuppressDialogs) return true;
        var answer = MessageBox.Show(this, "Save changes to this tutor course?", "Unsaved course", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false; if (answer == DialogResult.No) return true;
        try { SaveCourse(); return !HasUnsavedChanges; } catch (Exception error) { Error(error); return false; }
    }
    private void OpenCourse()
    {
        if (!CanLeave()) return; using var picker = new OpenFileDialog { Filter = "Tutor course source (*.json)|*.json", Title = "Open a tutor course" }; if (picker.ShowDialog(this) != DialogResult.OK) return;
        document = TutorCourseDocument.Open(picker.FileName); saved = document.Content.ToJsonString(); undo.Clear(); redo.Clear(); pending = false; LoadItem("course", ""); RefreshTree();
    }
    private void NewCourse()
    {
        if (!CanLeave()) return; string original = Path.Combine(AppContext.BaseDirectory, "TutorSeed", "starter-course.json");
        var seed = TutorCourseDocument.Open(File.Exists(original) ? original : Path.Combine(workspaceRoot, "TutorSource", "starter-course.json"));
        var copy = (JsonObject)seed.Content.DeepClone(); copy["id"] = "course-" + Guid.NewGuid().ToString("N")[..10]; copy["title"] = "My health and biomedical statistics course"; copy["version"] = "1.0-draft";
        document = TutorCourseDocument.Create(copy); saved = ""; pending = false; undo.Clear(); redo.Clear(); LoadItem("course", ""); RefreshTree();
    }
    private void OpenExamples()
    {
        if (!CanLeave()) return;
        string path = Path.Combine(workspaceRoot, "TutorSource", "health-biomed-draft-pack.json");
        if (!File.Exists(path)) throw new FileNotFoundException("The optional health and biomedical example pack is missing.", path);
        document = TutorCourseDocument.Open(path); saved = document.Content.ToJsonString(); pending = false; undo.Clear(); redo.Clear(); LoadItem("course", ""); RefreshTree();
    }
    private void ExportCourse()
    {
        CommitCurrent(); var issues = document.Validate(); if (issues.Any(issue => issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase))) { ShowIssues(issues); return; }
        using var picker = new SaveFileDialog { Title = "Export course for StatsDirect Learning", Filter = "StatsDirect course pack (*.json)|*.json", FileName = S(document.Content, "id") + "-course.json", InitialDirectory = Directory.Exists(@"C:\temp") ? @"C:\temp" : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AddExtension = true, DefaultExt = "json" };
        if (picker.ShowDialog(this) != DialogResult.OK) return; document.Export(picker.FileName); status.Text = "Exported · Import this JSON in StatsDirect: Help > Learning Options > Import course materials";
    }
    private void ValidateCourse() { CommitCurrent(); ShowIssues(document.Validate()); }
    private void RecordAuthorReview()
    {
        CommitCurrent(); if (selectedKind is not ("lessons" or "questions")) { status.Text = "Select a lesson or multiple-choice question to record its review."; return; }
        using var dialog = new Form { Text = "Record author review", Size = new Size(850, 700), StartPosition = FormStartPosition.CenterParent, Font = Font };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 7 };
        var name = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Reviewer name" };
        var notes = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, PlaceholderText = "Record the sources checked, answer-key review, calculations run and any limitations." };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(new Label { Text = "This records your review, rather than an AI claim of verification.", AutoSize = true }); layout.Controls.Add(name); layout.Controls.Add(new Label { Text = "Review evidence and notes", AutoSize = true }); layout.Controls.Add(notes);
        var numerical = new CheckBox { Text = "I have also run and checked the numerical example(s)", AutoSize = true, Margin = new Padding(3, 10, 3, 6) };
        var numericalEvidence = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, PlaceholderText = "If checked: record software/version, calculation or R script, actual results and any limits." };
        layout.Controls.Add(numerical); layout.Controls.Add(numericalEvidence);
        var accept = new Button { Text = "Record review", Dock = DockStyle.Right, Width = 160 }; layout.Controls.Add(accept); dialog.Controls.Add(layout);
        accept.Click += (_, _) => { try { RecordReview(name.Text, notes.Text, numerical.Checked, numericalEvidence.Text); dialog.DialogResult = DialogResult.OK; } catch (Exception error) { Error(error); } };
        if (dialog.ShowDialog(this) == DialogResult.OK) ReloadSelection();
    }
    public void RecordReview(string reviewer, string notes, bool numericalChecked = false, string numericalEvidence = "")
    {
        CommitCurrent();
        if (selectedKind is not ("lessons" or "questions")) throw new InvalidOperationException("Select a lesson or question to review.");
        if (string.IsNullOrWhiteSpace(notes)) throw new ArgumentException("Record the evidence and reasoning checked during your review.");
        if (numericalChecked && string.IsNullOrWhiteSpace(numericalEvidence)) throw new ArgumentException("Record the calculation, actual result and software/version for the numerical check.");
        ChangeDocument(() =>
        {
            document.MarkReviewed(selectedKind, selectedId, reviewer, notes);
            if (numericalChecked)
            {
                var record = document.GetItems(selectedKind).Single(row => S(row, "id") == selectedId);
                var verification = record["verification"] as JsonObject ?? new JsonObject();
                verification["status"] = "Author-recorded numerical check"; verification["current"] = true; verification["passed"] = true;
                verification["checkedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd"); verification["reviewer"] = reviewer.Trim(); verification["evidence"] = numericalEvidence;
                verification["contentSha256"] = TutorContentFingerprint.Record(record);
                record["verification"] = verification; document.Update(selectedKind, selectedId, record);
            }
        });
        ReloadSelection();
    }
    private void ShowIssues(IReadOnlyList<TutorValidationIssue> issues)
    {
        using var dialog = new Form { Text = "Course checks", Size = new Size(1100, 620), StartPosition = FormStartPosition.CenterParent, Font = Font };
        var heading = new Label { Text = issues.Count(issue => issue.Severity == "Error") + " export errors · " + issues.Count(issue => issue.Severity != "Error") + " review notes. Double-click an item to edit it.", Dock = DockStyle.Top, AutoSize = false, Height = 55, Padding = new Padding(12), BackColor = Color.FromArgb(230, 239, 249) };
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, BackgroundColor = Color.White, DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
        grid.Columns.Add("severity", "Status"); grid.Columns[0].FillWeight = 15; grid.Columns.Add("item", "Course item"); grid.Columns[1].FillWeight = 35; grid.Columns.Add("message", "Details"); grid.Columns[2].FillWeight = 100;
        foreach (var issue in issues) grid.Rows.Add(issue.Severity, issue.Path, issue.Message);
        grid.CellDoubleClick += (_, e) => { if (e.RowIndex < 0) return; string[] path = issues[e.RowIndex].Path.Split('.'); if (path.Length > 1 && path[0] is "lessons" or "questions" or "documents") { dialog.Close(); LoadItem(path[0], path[1]); RefreshTree(); tabs.SelectedIndex = 0; } };
        var close = new Button { Text = "Close checks", Dock = DockStyle.Bottom, Height = 40 }; close.Click += (_, _) => dialog.Close();
        dialog.Controls.Add(grid); dialog.Controls.Add(heading); dialog.Controls.Add(close); dialog.ShowDialog(this);
    }
    private async Task ResearchAsync()
    {
        CommitCurrent();
        var context = TutorResearchContext.Create(document.Content, await helpContext());
        using var dialog = new TutorResearchDialog(context, selectedKind is "lessons" or "questions" ? (JsonObject)current.DeepClone() : new JsonObject(), selectedKind == "questions" ? "questions" : "lessons", externalLinkLauncher: openLink);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.AcceptedProposal == null) return;
        ApplyResearchProposal(dialog.AcceptedProposal);
    }
    public void ApplyResearchProposal(TutorResearchProposal proposal)
    {
        CommitCurrent(); string kind = proposal.Kind;
        if (kind is not ("lessons" or "questions")) throw new InvalidDataException("ChatGPT returned an unsupported item type.");
        var edited = (JsonObject)proposal.ProposedRecord.DeepClone(); string id = S(edited, "id");
        if (edited["authoring"] is not JsonObject authoring) edited["authoring"] = authoring = new JsonObject();
        authoring["lastAiReview"] = JsonSerializer.SerializeToNode(new
        {
            acceptedAtUtc = DateTimeOffset.UtcNow, proposal.Mode, proposal.Summary, proposal.StatisticalNotes,
            proposedRCheck = proposal.RValidationCode, rCheckStatus = "Not executed by the authoring editor",
            proposal.WebResearchRequested, proposal.WebResearchObserved, proposal.EvidenceNote,
            sources = proposal.Sources, observedSearchActivity = proposal.SearchActivity
        });
        var sourceRows = edited["sources"] as JsonArray;
        if (sourceRows == null) edited["sources"] = sourceRows = new JsonArray();
        foreach (var citation in proposal.Sources)
        {
            var row = sourceRows.OfType<JsonObject>().FirstOrDefault(value => S(value, "url") == citation.Url);
            if (row == null) { row = new JsonObject { ["title"] = citation.Title, ["url"] = citation.Url, ["scope"] = citation.Relevance }; sourceRows.Add(row); }
            row["aiSuggested"] = true; row["observedOpened"] = citation.ObservedOpened;
            row["accessedAtUtc"] = citation.AccessedAtUtc?.ToString("o"); row["verificationStatus"] = "Author source review required";
        }
        edited["review"] = new JsonObject { ["status"] = "AI-assisted draft — author review required", ["notes"] = proposal.StatisticalNotes, ["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd") };
        if (string.IsNullOrWhiteSpace(proposal.BeforeJson))
        {
            if (document.GetItems(kind).Any(row => S(row, "id") == id)) throw new InvalidOperationException("That item ID already exists. Ask for a new ID or refine the existing item.");
            ChangeDocument(() => document.AddRecord(kind, edited));
        }
        else
        {
            var before = JsonNode.Parse(proposal.BeforeJson)!.AsObject(); string previousId = S(before, "id"); var live = document.GetItems(kind).FirstOrDefault(row => S(row, "id") == previousId);
            if (live == null || !JsonNode.DeepEquals(live, before)) throw new InvalidOperationException("The item changed after this review. Request a fresh review; your edits are preserved.");
            ChangeDocument(() => document.Update(kind, previousId, edited));
        }
        LoadItem(kind, id); RefreshTree(); tabs.SelectedIndex = 0; status.Text = "Accepted ChatGPT draft · unsaved · review sources and statistical reasoning before exporting";
    }
    public string BuildPreviewHtml()
    {
        CommitCurrent(); string E(string value) => WebUtility.HtmlEncode(value).Replace("\n", "<br>");
        var html = new StringBuilder("<!doctype html><html><head><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'\"><style>body{font:12pt Arial;line-height:1.55;color:#1e293b;margin:0;background:#f3f6fa}main{max-width:900px;margin:24px auto;background:white;padding:30px;border-radius:10px}h1,h2{line-height:1.25;color:#173b60}h1{font-size:22pt}h2{font-size:15pt}pre{white-space:pre-wrap;background:#eef3f8;padding:16px;font:10pt Consolas}label{display:block;padding:12px;margin:8px 0;border:1px solid #bbc9d6;border-radius:5px}button{font:12pt Arial;background:#173b60;color:white;padding:10px 18px;border:0;border-radius:4px}#feedback{margin-top:18px;padding:16px;background:#edf6f0}a{color:#075b9c}small{color:#526474}</style></head><body><main>");
        html.Append("<small>Learner content preview · ").Append(E(S(document.Content, "title"))).Append("<br>Plain-text preview; StatsDirect supplies the final Markdown and equation layout.</small>");
        void Section(string title, string key) { string value = S(current, key); if (!string.IsNullOrWhiteSpace(value)) html.Append("<h2>").Append(E(title)).Append("</h2><p>").Append(E(value)).Append("</p>"); }
        if (selectedKind == "questions")
        {
            html.Append("<h1>").Append(E(S(current, "topic"))).Append("</h1><p>").Append(E(S(current, "stem"))).Append("</p>");
            foreach (var option in (current["options"] as JsonArray ?? []).OfType<JsonObject>()) html.Append("<label><input type='radio' name='answer' value='").Append(WebUtility.HtmlEncode(S(option, "id"))).Append("'> ").Append(E(S(option, "id") + ". " + S(option, "text"))).Append("</label>");
            html.Append("<button onclick='checkAnswer()'>Check answer</button><div id='feedback' hidden></div><script>const q=").Append(JsonSerializer.Serialize(new { correct = S(current, "correct"), explanation = S(current, "explanation"), options = (current["options"] as JsonArray ?? []).OfType<JsonObject>().Select(option => new { id = S(option, "id"), feedback = S(option, "feedback") }) })).Append(";function checkAnswer(){const a=document.querySelector('input:checked');if(!a)return;const f=document.getElementById('feedback');f.hidden=false;f.textContent=(a.value===q.correct?'Correct. ':'Review this choice. ')+(q.options.find(o=>o.id===a.value)?.feedback||'')+' '+q.explanation;}</script>");
        }
        else if (selectedKind == "course") { html.Append("<h1>").Append(E(S(current, "title"))).Append("</h1>"); Section("Course version", "version"); html.Append("<p>Select a lesson, exercise or multiple-choice question to preview it.</p>"); }
        else
        {
            html.Append("<h1>").Append(E(S(current, "title"))).Append("</h1>"); Section("Learning objective", "objective");
            if (current["learningObjectives"] is JsonArray objectives && objectives.Count > 0) { html.Append("<h2>Learning objectives</h2><ul>"); foreach (var objective in objectives) html.Append("<li>").Append(E(NodeText(objective))).Append("</li>"); html.Append("</ul>"); }
            Section("Teaching content", "summary"); Section("Course reading", "text");
            void Structured(string key, string title, params string[] properties)
            { if (current[key] is not JsonArray rows || rows.Count == 0) return; html.Append("<h2>").Append(E(title)).Append("</h2>"); foreach (var row in rows.OfType<JsonObject>()) { html.Append("<p>"); foreach (string property in properties) if (S(row, property).Length > 0) html.Append(E(S(row, property))).Append("<br>"); html.Append("</p>"); } }
            Structured("keyConcepts", "Key concepts", "term", "explanation"); Structured("misconceptions", "Common misconceptions", "claim", "correction");
            if (current["workedExample"] is JsonObject worked) html.Append("<h2>Worked example</h2><p>").Append(E(S(worked, "design"))).Append("</p><p>").Append(E(S(worked, "interpretation"))).Append("</p>");
            Section("Exercise", "challenge"); Section("Practical steps", "steps"); Structured("teachingPrompts", "Teaching prompts", "level", "prompt", "expectedReasoning");
            if (S(current, "r").Length > 0) html.Append("<h2>R example</h2><pre>").Append(E(S(current, "r"))).Append("</pre>");
            if (current["rProgression"] is JsonObject progression) { html.Append("<h2>From menus to R</h2>"); foreach (string stage in new[] { "menus", "bridge", "coding" }) if (S(progression, stage).Length > 0) html.Append("<p><strong>").Append(E(stage)).Append("</strong>: ").Append(E(S(progression, stage))).Append("</p>"); }
        }
        var sources = (current["sources"] as JsonArray ?? []).OfType<JsonObject>().Where(row => Uri.TryCreate(S(row, "url"), UriKind.Absolute, out var uri) && uri.Scheme == "https").ToArray();
        if (sources.Length > 0) { html.Append("<h2>Sources and further reading</h2><ul>"); foreach (var citation in sources) html.Append("<li><a href='").Append(WebUtility.HtmlEncode(S(citation, "url"))).Append("'>").Append(E(S(citation, "title").Length == 0 ? S(citation, "url") : S(citation, "title"))).Append("</a></li>"); html.Append("</ul>"); }
        return html.Append("</main></body></html>").ToString();
    }
    private async Task ShowPreviewAsync()
    {
        if (!previewReady)
        {
            string profile = Path.Combine(workspaceRoot, ".tutor-preview-profile"); Directory.CreateDirectory(profile);
            var environment = await CoreWebView2Environment.CreateAsync(null, profile); await preview.EnsureCoreWebView2Async(environment);
            preview.CoreWebView2.Settings.AreDevToolsEnabled = false;
            preview.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https") openLink(uri); };
            preview.CoreWebView2.NavigationStarting += (_, e) => { if (e.Uri != "about:blank" && e.Uri != previewDocumentUri) { e.Cancel = true; if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https") openLink(uri); } };
            previewReady = true;
        }
        // The current WebView2 uses a data URI for in-memory HTML. Permit only
        // this exact generated document; source links still open externally.
        previewDocumentUri = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(BuildPreviewHtml()));
        preview.CoreWebView2.Navigate(previewDocumentUri); status.Text = "Learner content preview · answers shown only after Check answer · R code is never executed here";
    }
    private async Task RunAsync(Func<Task> action)
    { if (busy) return; busy = true; UseWaitCursor = true; try { await action(); } catch (Exception error) { Error(error); } finally { busy = false; UseWaitCursor = false; } }
    private void Error(Exception error) { LastError = error.Message; status.Text = error.Message; if (!SuppressDialogs) MessageBox.Show(this, error.Message, "Tutor authoring", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    private void UpdateStatus() { Text = "StatsDirect Tutor — " + S(document.Content, "title") + (HasUnsavedChanges ? " *" : ""); status.Text = (HasUnsavedChanges ? "Unsaved tutor edits" : "Tutor source open") + " · " + (document.FilePath ?? "new tutor source") + " · save drafts; publish reviewed material for StatsDirect builds"; }
    protected override void Dispose(bool disposing) { if (disposing) { preview.Dispose(); ownedIcon?.Dispose(); } base.Dispose(disposing); }
}
