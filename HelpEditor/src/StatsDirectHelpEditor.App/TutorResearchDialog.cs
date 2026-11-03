using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StatsDirect.HelpEditor.App;

/// <summary>Research proposals remain drafts until the author accepts one; this window never writes course files.</summary>
public sealed class TutorResearchDialog : Form
{
    private readonly TutorResearchContext context;
    private readonly JsonObject selected;
    private readonly string kind;
    private readonly ITutorResearchChatGptClient client;
    private readonly bool ownsClient;
    private readonly Action<Uri> launch;
    private readonly Label connection = new() { AutoSize = true, Text = "Checking ChatGPT connection…" };
    private readonly Label summary = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly Label evidence = new() { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.FromArgb(80, 65, 30) };
    private readonly ComboBox audience = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, AccessibleName = "Tutor audience" };
    private readonly ComboBox mode = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Research mode" };
    private readonly TextBox instructions = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Tutor research instructions" };
    private readonly CheckBox web = new() { AutoSize = true, Checked = true, Text = "Research current Internet sources", AccessibleName = "Research current Internet sources" };
    private readonly Button signIn = MakeButton("Sign in with ChatGPT"), signOut = MakeButton("Sign out"), research = MakeButton("Research / suggest"), stop = MakeButton("Stop"), accept = MakeButton("Accept this draft"), close = MakeButton("Close");
    private readonly RichTextBox before = ReadBox("Current tutor record"), after = ReadBox("Proposed tutor record"), notes = ReadBox("Statistical review notes"), rCode = ReadBox("Proposed R verification code"), discussion = ReadBox("Research discussion"), activity = ReadBox("Observed web research");
    private readonly DataGridView sources = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = SystemColors.Window, AccessibleName = "Research sources" };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private CancellationTokenSource? active;
    private bool closingRequested;
    private string previousDiscussion = "";
    public TutorResearchProposal? Proposal { get; private set; }
    public TutorResearchProposal? AcceptedProposal { get; private set; }
    public string HumanReviewSummary => Proposal == null ? "" : Proposal.Summary + "\n\n" + Proposal.StatisticalNotes + "\n\n" + Proposal.EvidenceNote;
    public string? LastError { get; private set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool SuppressDialogs { get; set; }

    public TutorResearchDialog(TutorResearchContext context, JsonObject selectedRecord, string kind, ITutorResearchChatGptClient? client = null, Action<Uri>? externalLinkLauncher = null)
    {
        if (kind is not ("lessons" or "questions")) throw new ArgumentException("Choose lessons or questions.", nameof(kind));
        this.context = context; selected = (JsonObject)selectedRecord.DeepClone(); this.kind = kind;
        this.client = client ?? new PolishChatGptClient(); ownsClient = client == null;
        launch = externalLinkLauncher ?? (uri => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        Text = "Research and review tutor content with ChatGPT";
        Font = new Font("Arial", 10); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(900, 700); Size = new Size(1250, 950); ShowInTaskbar = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int index = 0; index < 8; index++) layout.RowStyles.Add(new RowStyle(index == 6 ? SizeType.Percent : SizeType.AutoSize, index == 6 ? 100 : 0));
        var account = Flow(); account.Controls.AddRange([connection, signIn, signOut, research, stop]); layout.Controls.Add(account, 0, 0);
        var scope = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = $"ChatGPT can consult all {context.TutorItemCount} frozen tutor items and {context.HelpPageCount} help pages. Research / suggest sends this material on request to OpenAI, with your instructions. Proposals stay here until you accept a draft; your course is not saved or published by this window." };
        layout.Controls.Add(scope, 0, 1);
        var choices = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); choices.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        choices.Controls.Add(new Label { AutoSize = true, Text = "Task", Anchor = AnchorStyles.Left }, 0, 0); choices.Controls.Add(mode, 1, 0);
        choices.Controls.Add(new Label { AutoSize = true, Text = "Audience", Anchor = AnchorStyles.Left }, 2, 0); choices.Controls.Add(audience, 3, 0); layout.Controls.Add(choices, 0, 2);
        mode.Items.AddRange(["Refine selected material", "Create a new " + (kind == "questions" ? "multiple-choice question" : "lesson / practical exercise"), "Review statistical robustness"]);
        mode.SelectedIndex = selected.Count == 0 ? 1 : 0;
        audience.Items.AddRange(["Health and biomedicine undergraduates", "Masters students taking statistical modules", "UK postgraduate medical / Royal College membership candidates", "US medical Board candidates", "Healthcare commissioning / payer analysts moving from Excel to R"]); audience.SelectedIndex = 0;
        var instructionPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 3 };
        instructionPanel.Controls.Add(new Label { AutoSize = true, Text = "Ask for material, changes or a follow-up to the current proposal:" }, 0, 0);
        instructions.Height = Font.Height * 4 + 10;
        instructions.Text = "Use a realistic fictional healthcare example. Explain the assumptions, provide clear feedback, and suggest a short reproducible base-R check where numerical answers are involved. Identify sources and any unresolved concerns.";
        instructionPanel.Controls.Add(instructions, 0, 1); instructionPanel.Controls.Add(web, 0, 2); layout.Controls.Add(instructionPanel, 0, 3);
        summary.Text = "Choose the audience and task, then ask ChatGPT for a proposal. You can refine it through follow-up requests before accepting."; layout.Controls.Add(summary, 0, 4);
        evidence.Text = "AI review does not independently certify statistical correctness, exam endorsement or CPD credit."; layout.Controls.Add(evidence, 0, 5);
        var proposalTab = new TabPage("Proposed content");
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); split.RowStyles.Add(new RowStyle(SizeType.AutoSize)); split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Controls.Add(new Label { Text = "Current material", AutoSize = true }, 0, 0); split.Controls.Add(new Label { Text = "Proposed draft", AutoSize = true }, 1, 0); split.Controls.Add(before, 0, 1); split.Controls.Add(after, 1, 1); proposalTab.Controls.Add(split); tabs.TabPages.Add(proposalTab);
        AddTab("Statistical review", notes); AddTab("R check (not run)", rCode); AddTab("Sources", sources); AddTab("Observed searches", activity); AddTab("Discussion", discussion);
        layout.Controls.Add(tabs, 0, 6);
        var final = Flow(); final.FlowDirection = FlowDirection.RightToLeft; final.Controls.AddRange([close, accept]); layout.Controls.Add(final, 0, 7); Controls.Add(layout);
        sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "Source", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 32 });
        sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "URL", HeaderText = "URL (double-click to open)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 40 });
        sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "Evidence", HeaderText = "Browsing evidence", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 28 });
        sources.CellDoubleClick += (_, eventArgs) => { if (eventArgs.RowIndex >= 0 && Proposal != null && eventArgs.RowIndex < Proposal.Sources.Count && PolishChatGptClient.SourceUrl(Proposal.Sources[eventArgs.RowIndex].Url) is { } url) launch(new Uri(url)); };
        before.Text = Readable(selected); accept.Enabled = false; stop.Enabled = false;
        research.Click += async (_, _) => await TryAsync(() => ResearchAsync(instructions.Text, audience.Text, ModeValue, web.Checked));
        stop.Click += (_, _) => StopResearch(); close.Click += (_, _) => Close(); accept.Click += (_, _) => AcceptProposal();
        signIn.Click += async (_, _) => await TryAsync(async () => await AccountAsync(async token => { string? url = await this.client.SignInAsync(token); if (url != null && PolishChatGptClient.IsLoginUrl(url)) launch(new Uri(url)); }));
        signOut.Click += async (_, _) => await TryAsync(async () => await AccountAsync(this.client.SignOutAsync));
        this.client.Changed += ConnectionChanged;
        Shown += async (_, _) => await TryAsync(async () => await AccountAsync(this.client.InitializeAsync));
        FormClosing += (_, eventArgs) => { if (active != null) { closingRequested = true; eventArgs.Cancel = true; StopResearch(); } };
        FormClosed += (_, _) => { this.client.Changed -= ConnectionChanged; if (ownsClient) this.client.Dispose(); };
        foreach (var label in new[] { scope, summary, evidence }) label.SizeChanged += (_, _) => label.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - 10), 0);
        UpdateButtons();
    }

    private string ModeValue => mode.SelectedIndex switch { 1 => "create", 2 => "review", _ => "refine" };
    public async Task<TutorResearchProposal> ResearchAsync(string authorInstructions, string targetAudience, string researchMode, bool useWebResearch, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (active != null) throw new InvalidOperationException("Stop the current request before starting another.");
        LastError = null; active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); UpdateButtons();
        try
        {
            var request = new TutorResearchRequest(kind, researchMode, (JsonObject)selected.DeepClone(), authorInstructions, targetAudience, useWebResearch, previousDiscussion);
            var proposed = await client.ResearchTutorAsync(context, request, active.Token);
            active.Token.ThrowIfCancellationRequested(); Proposal = proposed; RenderProposal();
            string next = "AUTHOR: " + authorInstructions + "\nAI PROPOSAL: " + proposed.ProposedRecord.ToJsonString() + "\nREVIEW NOTES: " + proposed.StatisticalNotes +
                "\nPROPOSED R CHECK (not run): " + proposed.RValidationCode + "\nSOURCE CITATIONS (browsing evidence, not verification): " + JsonSerializer.Serialize(proposed.Sources) + "\n\n";
            previousDiscussion += next;
            if (previousDiscussion.Length > 180000) previousDiscussion = "[Earlier discussion omitted for length.]\n" + previousDiscussion[^180000..];
            discussion.Text = previousDiscussion;
            return proposed;
        }
        catch (OperationCanceledException) { if (!IsDisposed) summary.Text = "Stopped. The course is unchanged; any earlier proposal remains available."; throw; }
        catch (Exception error) { LastError = error.Message; if (!IsDisposed) summary.Text = error.Message; throw; }
        finally { FinishOperation(); }
    }
    public void StopResearch() { active?.Cancel(); if (!IsDisposed) summary.Text = "Stopping… the course remains unchanged."; }
    public void AcceptProposal()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (active != null || Proposal == null) throw new InvalidOperationException("Finish a proposal before accepting it.");
        AcceptedProposal = Proposal with { ProposedRecord = (JsonObject)Proposal.ProposedRecord.DeepClone() };
        DialogResult = DialogResult.OK; Close();
    }
    private async Task AccountAsync(Func<CancellationToken, Task> action)
    {
        if (active != null) return;
        active = new(); UpdateButtons();
        try { await action(active.Token); }
        finally { FinishOperation(); }
    }
    private void FinishOperation()
    {
        active?.Dispose(); active = null;
        if (!IsDisposed) { UpdateButtons(); if (closingRequested) BeginInvoke(Close); }
    }
    private async Task TryAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { if (!IsDisposed && !Disposing) summary.Text = "Stopped. The course is unchanged; any earlier proposal remains available."; }
        catch (Exception error)
        {
            LastError = error.Message;
            if (IsDisposed || Disposing) return;
            summary.Text = error.Message;
            if (!SuppressDialogs && !closingRequested) MessageBox.Show(this, error.Message, "Tutor research", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
    private void ConnectionChanged()
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(UpdateButtons); } catch (InvalidOperationException) { }
            return;
        }
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        bool busy = active != null; connection.Text = client.Status;
        signIn.Enabled = !busy && !client.IsSignedIn; signOut.Enabled = !busy && client.IsSignedIn;
        research.Enabled = !busy && client.IsSignedIn; stop.Enabled = busy; accept.Enabled = !busy && Proposal != null;
        mode.Enabled = audience.Enabled = instructions.Enabled = web.Enabled = !busy;
    }
    private void RenderProposal()
    {
        var value = Proposal!; summary.Text = value.Summary; evidence.Text = value.EvidenceNote;
        after.Text = Readable(value.ProposedRecord); notes.Text = value.StatisticalNotes; rCode.Text = value.RValidationCode.Length == 0 ? "No numerical R check proposed. Review the conceptual argument and sources." : value.RValidationCode;
        sources.Rows.Clear();
        foreach (var source in value.Sources) { int row = sources.Rows.Add(source.Title, source.Url, source.ObservedOpened ? "Opened during this research" : "Suggested — open and check"); sources.Rows[row].Cells[0].ToolTipText = source.Relevance + (source.AccessedAtUtc is { } date ? "\nAccessed: " + date.ToString("u") : ""); }
        activity.Text = value.SearchActivity.Count == 0 ? "No web-search activity was observed for this reply." : string.Join("\n\n", value.SearchActivity.Select(item => item.Action + "\n" + item.Query + (item.Url.Length == 0 ? "" : "\n" + item.Url)));
    }
    private static string Readable(JsonObject value)
    {
        if (value.Count == 0) return "A new record will be proposed. Nothing has been added to the course.";
        var text = new StringBuilder();
        foreach (var pair in value)
        {
            if (pair.Key == "options" && pair.Value is JsonArray options)
                foreach (var option in options) text.Append(TutorResearchContext.Text(option, "id")).Append(". ").AppendLine(TutorResearchContext.Text(option, "text")).Append("   Feedback: ").AppendLine(TutorResearchContext.Text(option, "feedback"));
            else if (pair.Value is JsonValue scalar && scalar.TryGetValue<string>(out var content)) text.Append(pair.Key).Append(":\n").AppendLine(content).AppendLine();
            else text.Append(pair.Key).Append(":\n").AppendLine(pair.Value?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null").AppendLine();
        }
        return text.ToString();
    }
    private void AddTab(string title, Control control) { var page = new TabPage(title); page.Controls.Add(control); tabs.TabPages.Add(page); }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            active?.Cancel();
            client.Changed -= ConnectionChanged;
            if (ownsClient) client.Dispose();
        }
        base.Dispose(disposing);
    }
    private static Button MakeButton(string text) => new() { Text = text, AutoSize = true, AccessibleName = text };
    private static RichTextBox ReadBox(string name) => new() { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, Font = new Font("Arial", 10), BackColor = SystemColors.Window, AccessibleName = name, HideSelection = false };
    private static FlowLayoutPanel Flow() => new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 3, 0, 3) };
}
