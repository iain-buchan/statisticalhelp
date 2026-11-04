using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.Core;

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
    private readonly Label targetSummary = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly ComboBox audience = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, AccessibleName = "Tutor audience" };
    private readonly ComboBox mode = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Research mode" };
    private readonly ComboBox difficulty = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Question difficulty" };
    private readonly Dictionary<string, CheckBox> goals = new(StringComparer.Ordinal);
    private readonly TextBox goalNotes = new() { Dock = DockStyle.Fill, MaxLength = 3000, AccessibleName = "Qualification and learner-goal notes" };
    private readonly TextBox instructions = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Tutor research instructions" };
    private readonly CheckBox web = new() { AutoSize = true, Checked = true, Text = "Research current Internet sources", AccessibleName = "Research current Internet sources" };
    private readonly Button signIn = MakeButton("Sign in with ChatGPT"), signOut = MakeButton("Sign out"), research = MakeButton("Send"), stop = MakeButton("Stop"), unqueue = MakeButton("Remove queued message"), accept = MakeButton("Accept this draft"), close = MakeButton("Close");
    private readonly Button editTargets = MakeButton("Learning targets…");
    private readonly RichTextBox before = ReadBox("Current tutor record"), after = ReadBox("Proposed tutor record"), notes = ReadBox("Statistical review notes"), rCode = ReadBox("Proposed R verification code"), discussion = ReadBox("Research discussion"), activity = ReadBox("Observed web research");
    private readonly DataGridView sources = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = SystemColors.Window, AccessibleName = "Research sources" };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox live = ReadBox("Live web activity");
    private CancellationTokenSource? active;
    private bool closingRequested, researching, loadingTargets, updatingAudience;
    private bool audienceAutomatic = true;
    private string previousDiscussion = "";
    private QueuedMessage? queued;
    private readonly List<TutorResearchActivity> currentActivity = [];
    private sealed record TargetChoice(string? Difficulty, string[] Goals, string Notes);
    private sealed record QueuedMessage(string Instructions, string Audience, string Mode, bool Web, TargetChoice Targets, TaskCompletionSource<TutorResearchProposal> Completion);
    public TutorResearchProposal? Proposal { get; private set; }
    public TutorResearchProposal? LastReply { get; private set; }
    public TutorResearchProposal? AcceptedProposal { get; private set; }
    public string ConversationText => discussion.Text;
    public string LiveActivityText => live.Text;
    public bool HasQueuedFollowUp => queued != null;
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
        Text = "Tutor conversation and research with ChatGPT";
        Font = new Font("Arial", 10); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(900, 700); Size = new Size(1250, 950); ShowInTaskbar = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int index = 0; index < 8; index++) layout.RowStyles.Add(new RowStyle(index == 5 ? SizeType.Percent : SizeType.AutoSize, index == 5 ? 100 : 0));
        var account = Flow(); account.Controls.AddRange([connection, signIn, signOut]); layout.Controls.Add(account, 0, 0);
        var scope = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = $"Send shares your instructions and context from {context.TutorItemCount} tutor items and {context.HelpPageCount} help pages with OpenAI. Drafts need your acceptance." };
        layout.Controls.Add(scope, 0, 1);
        var choices = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); choices.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        choices.Controls.Add(new Label { AutoSize = true, Text = "Task", Anchor = AnchorStyles.Left }, 0, 0); choices.Controls.Add(mode, 1, 0);
        choices.Controls.Add(new Label { AutoSize = true, Text = "Audience", Anchor = AnchorStyles.Left }, 2, 0); choices.Controls.Add(audience, 3, 0); layout.Controls.Add(choices, 0, 2);
        mode.Items.AddRange(["Discuss / research ideas", "Refine selected material", "Create a new " + (kind == "questions" ? "multiple-choice question" : "lesson / practical exercise"), "Review statistical robustness"]);
        mode.SelectedIndex = 0;
        audience.Items.AddRange(["Use the course context and selected learner goals", "Health and biomedicine undergraduates", "Masters students taking statistical modules", "UK postgraduate medical / Royal College membership candidates", "US medical Board candidates", "Healthcare commissioning / payer analysts moving from Excel to R"]); audience.SelectedIndex = 0;
        audience.TextChanged += (_, _) => { if (!updatingAudience) audienceAutomatic = false; };
        var targets = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 5 };
        for (int index = 0; index < 4; index++) targets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        targets.Controls.Add(new Label { AutoSize = true, Text = "Difficulty" }, 0, 0); targets.Controls.Add(difficulty, 1, 0); targets.SetColumnSpan(difficulty, 3);
        difficulty.Items.Add("Unassigned"); foreach (var pair in TutorLearningTargets.Difficulties) difficulty.Items.Add(pair.Value); difficulty.SelectedIndex = 0;
        var targetLabel = new Label { AutoSize = true, Text = "Learner goals — choose any" }; targets.Controls.Add(targetLabel, 0, 1); targets.SetColumnSpan(targetLabel, 4);
        int goalIndex = 0;
        foreach (var pair in TutorLearningTargets.Goals)
        {
            var box = new CheckBox { Text = pair.Value, AutoSize = true, Dock = DockStyle.Fill, AccessibleName = pair.Value };
            goals.Add(pair.Key, box); targets.Controls.Add(box, goalIndex % 4, 2 + goalIndex / 4); goalIndex++;
            box.CheckedChanged += (_, _) => TargetsChanged();
        }
        targets.Controls.Add(new Label { AutoSize = true, Text = "Qualification / learning goals" }, 0, 4); targets.Controls.Add(goalNotes, 1, 4); targets.SetColumnSpan(goalNotes, 3);
        choices.Controls.Add(targetSummary, 0, 1); choices.SetColumnSpan(targetSummary, 3); choices.Controls.Add(editTargets, 3, 1);
        editTargets.Click += (_, _) =>
        {
            if (active != null) return;
            using var editor = new Form { Text = "Difficulty and learner goals", Font = Font, AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96), StartPosition = FormStartPosition.CenterParent, Size = new Size(950, 420), MinimizeBox = false, MaximizeBox = false };
            targets.Dock = DockStyle.Top; editor.Controls.Add(targets);
            var done = MakeButton("Done"); done.Dock = DockStyle.Bottom; done.DialogResult = DialogResult.OK; editor.Controls.Add(done); editor.AcceptButton = done;
            editor.ShowDialog(this); editor.Controls.Remove(targets);
        };
        LoadTargets(selected); difficulty.SelectedIndexChanged += (_, _) => TargetsChanged(); goalNotes.TextChanged += (_, _) => TargetsChanged();
        var instructionPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 3 };
        instructionPanel.Controls.Add(new Label { AutoSize = true, Text = "Message / follow-up (Ctrl+Enter to send; Enter starts a new line):" }, 0, 0);
        instructions.Height = Font.Height * 3 + 10; instructions.MaxLength = 12000;
        var compose = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true }; compose.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); compose.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); compose.Controls.Add(instructions, 0, 0); compose.Controls.Add(research, 1, 0);
        var requestControls = Flow(); requestControls.Controls.AddRange([web, stop, unqueue]);
        instructionPanel.Controls.Add(compose, 0, 1); instructionPanel.Controls.Add(requestControls, 0, 2); layout.Controls.Add(instructionPanel, 0, 6);
        summary.Text = "Ask a question or discuss an exercise, then switch Task when you want a complete draft."; layout.Controls.Add(summary, 0, 3);
        evidence.Text = "AI suggestions need author review; R checks are not run."; layout.Controls.Add(evidence, 0, 4);
        var conversation = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 }; conversation.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); conversation.RowStyles.Add(new RowStyle(SizeType.AutoSize)); conversation.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        discussion.Font = new Font("Arial", 11); conversation.Controls.Add(discussion, 0, 0); conversation.Controls.Add(new Label { AutoSize = true, Text = "Live web activity — observed actions, not verification" }, 0, 1); conversation.Controls.Add(live, 0, 2); AddTab("Conversation", conversation);
        discussion.DetectUrls = true; discussion.LinkClicked += (_, args) => { if (PolishChatGptClient.SourceUrl(args.LinkText ?? "") is { } url) launch(new Uri(url)); };
        var proposalTab = new TabPage("Proposed content");
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); split.RowStyles.Add(new RowStyle(SizeType.AutoSize)); split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Controls.Add(new Label { Text = "Current material", AutoSize = true }, 0, 0); split.Controls.Add(new Label { Text = "Proposed draft", AutoSize = true }, 1, 0); split.Controls.Add(before, 0, 1); split.Controls.Add(after, 1, 1); proposalTab.Controls.Add(split); tabs.TabPages.Add(proposalTab);
        AddTab("Statistical review", notes); AddTab("R check (not run)", rCode); AddTab("Sources", sources); AddTab("Observed searches", activity);
        layout.Controls.Add(tabs, 0, 5);
        var final = Flow(); final.FlowDirection = FlowDirection.RightToLeft; final.Controls.AddRange([close, accept]); layout.Controls.Add(final, 0, 7); Controls.Add(layout);
        sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "Source", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 32 });
        sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "URL", HeaderText = "URL (double-click to open)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 40 });
        sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "Evidence", HeaderText = "Browsing evidence", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 28 });
        sources.CellDoubleClick += (_, eventArgs) => { if (eventArgs.RowIndex >= 0 && LastReply != null && eventArgs.RowIndex < LastReply.Sources.Count && PolishChatGptClient.SourceUrl(LastReply.Sources[eventArgs.RowIndex].Url) is { } url) launch(new Uri(url)); };
        before.Text = Readable(selected); accept.Enabled = false; stop.Enabled = false;
        research.Click += async (_, _) => await SendComposerAsync();
        instructions.KeyDown += async (_, args) => { if (args.Control && args.KeyCode == Keys.Enter) { args.SuppressKeyPress = true; await SendComposerAsync(); } };
        unqueue.Click += (_, _) => RemoveQueuedMessage();
        stop.Click += (_, _) => StopResearch(); close.Click += (_, _) => Close(); accept.Click += (_, _) => AcceptProposal();
        signIn.Click += async (_, _) => await TryAsync(async () => await AccountAsync(async token => { string? url = await this.client.SignInAsync(token); if (url != null && PolishChatGptClient.IsLoginUrl(url)) launch(new Uri(url)); }));
        signOut.Click += async (_, _) => await TryAsync(async () => await AccountAsync(this.client.SignOutAsync));
        this.client.Changed += ConnectionChanged;
        if (this.client is ITutorResearchProgressSource progress) progress.ResearchActivity += ObserveActivity;
        Shown += async (_, _) => { FitToScreen(); await TryAsync(async () => await AccountAsync(this.client.InitializeAsync)); };
        FormClosing += (_, eventArgs) => { if (active != null) { closingRequested = true; eventArgs.Cancel = true; StopResearch(); } };
        FormClosed += (_, _) => { this.client.Changed -= ConnectionChanged; if (ownsClient) this.client.Dispose(); };
        foreach (var label in new[] { scope, summary, evidence, targetSummary })
        {
            label.MaximumSize = new Size(0, Font.Height * 2 + 4); label.AutoEllipsis = true;
            label.SizeChanged += (_, _) => label.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - 10), Font.Height * 2 + 4);
        }
        UpdateButtons();
    }

    private void FitToScreen()
    {
        var work = Screen.FromControl(this).WorkingArea; int margin = Math.Max(12, DeviceDpi / 8);
        int width = Math.Min(work.Width - margin * 2, Math.Max(Width, (int)(940 * DeviceDpi / 96f)));
        int height = Math.Min(work.Height - margin * 2, Math.Max(Height, (int)(740 * DeviceDpi / 96f)));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, width), Math.Min(MinimumSize.Height, height)); Size = new Size(width, height);
        if (!SuppressDialogs) Location = new Point(Math.Clamp(Left, work.Left + margin, work.Right - width - margin), Math.Clamp(Top, work.Top + margin, work.Bottom - height - margin));
    }

    private string ModeValue => mode.SelectedIndex switch { 1 => "refine", 2 => "create", 3 => "review", _ => "discuss" };
    private TargetChoice Targets => new(difficulty.SelectedIndex > 0 ? TutorLearningTargets.Difficulties.Keys.ElementAt(difficulty.SelectedIndex - 1) : null,
        goals.Where(pair => pair.Value.Checked).Select(pair => pair.Key).ToArray(), goalNotes.Text);
    public void SetLearningTargets(string? value, IReadOnlyList<string> learnerGoals, string learnerGoalNotes = "")
    {
        var target = new TargetChoice(value, learnerGoals.ToArray(), learnerGoalNotes); var row = new JsonObject(); ApplyTargets(row, target);
        if (TutorLearningTargets.Validate(row, "targets").Count != 0) throw new ArgumentException("Choose supported difficulty and learner goals.");
        if (active != null) throw new InvalidOperationException("Wait for the current reply before changing its classification.");
        LoadTargets(row); TargetsChanged();
    }
    private void LoadTargets(JsonObject row)
    {
        loadingTargets = true;
        try
        {
            difficulty.SelectedIndex = Array.IndexOf(TutorLearningTargets.Difficulties.Keys.ToArray(), TutorResearchContext.Text(row, "difficulty")) + 1;
            foreach (var pair in goals) pair.Value.Checked = row["learnerGoals"] is JsonArray chosen && chosen.Any(value => value is JsonValue scalar && scalar.TryGetValue<string>(out var id) && id == pair.Key);
            goalNotes.Text = TutorResearchContext.Text(row, "learnerGoalNotes");
        }
        finally { loadingTargets = false; }
        UpdateTargetSummary(); RefreshDerivedAudience();
    }
    private void TargetsChanged()
    {
        if (loadingTargets || active != null) return;
        UpdateTargetSummary(); RefreshDerivedAudience();
        if (Proposal == null) return;
        var row = (JsonObject)Proposal.ProposedRecord.DeepClone(); ApplyTargets(row, Targets); Proposal = Proposal with { ProposedRecord = row }; after.Text = Readable(row);
        summary.Text = "Your classification is shown in Proposed content and will be included when you accept it.";
    }
    private void UpdateTargetSummary() => targetSummary.Text = "Difficulty: " + difficulty.Text + " • Goals: " + (goals.Any(pair => pair.Value.Checked) ? string.Join(", ", goals.Where(pair => pair.Value.Checked).Select(pair => pair.Value.Text)) : "Unassigned");
    private void RefreshDerivedAudience()
    {
        if (!audienceAutomatic) return;
        updatingAudience = true;
        try { audience.Text = goals.Any(pair => pair.Value.Checked) ? string.Join("; ", goals.Where(pair => pair.Value.Checked).Select(pair => pair.Value.Text)) : "Use the course context and selected learner goals"; }
        finally { updatingAudience = false; }
    }
    private static void ApplyTargets(JsonObject row, TargetChoice target) => PolishChatGptClient.ApplyRequestedTargets(row,
        new("questions", "discuss", new(), "", "", false, Difficulty: target.Difficulty, LearnerGoals: target.Goals, LearnerGoalNotes: target.Notes));
    private async Task SendComposerAsync()
    {
        string message = instructions.Text;
        if (string.IsNullOrWhiteSpace(message)) { summary.Text = "Type a question or instructions for ChatGPT."; instructions.Focus(); return; }
        await TryAsync(async () => { var result = SendMessageAsync(message, audience.Text, ModeValue, web.Checked); instructions.Clear(); await result; });
    }
    public Task<TutorResearchProposal> SendMessageAsync(string message, string targetAudience, string researchMode, bool useWebResearch)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (string.IsNullOrWhiteSpace(message) || message.Length > 12000) throw new ArgumentException("Type a message of at most 12,000 characters.");
        if (active == null) return ResearchCoreAsync(message, targetAudience, researchMode, useWebResearch, Targets, default);
        if (!researching) throw new InvalidOperationException("Wait for the ChatGPT connection before sending a message.");
        if (queued != null) throw new InvalidOperationException("One follow-up is already queued. Remove it before sending a replacement.");
        var completion = new TaskCompletionSource<TutorResearchProposal>(TaskCreationOptions.RunContinuationsAsynchronously);
        queued = new(message, targetAudience, researchMode, useWebResearch, Targets, completion);
        summary.Text = "One follow-up is queued for after this reply. Stop cancels the current request and queue."; UpdateButtons(); return completion.Task;
    }
    public Task<TutorResearchProposal> ResearchAsync(string authorInstructions, string targetAudience, string researchMode, bool useWebResearch, CancellationToken cancellationToken = default) =>
        ResearchCoreAsync(authorInstructions, targetAudience, researchMode, useWebResearch, Targets, cancellationToken);
    private async Task<TutorResearchProposal> ResearchCoreAsync(string authorInstructions, string targetAudience, string researchMode, bool useWebResearch, TargetChoice target, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (active != null) throw new InvalidOperationException("Stop the current request before starting another.");
        if (string.IsNullOrWhiteSpace(authorInstructions)) throw new ArgumentException("Type a question or instructions for ChatGPT.");
        LastError = null; researching = true; active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); UpdateButtons(); currentActivity.Clear();
        live.Text = useWebResearch ? "Waiting for any observed web activity…" : "Web research disabled for this message."; AppendConversation("You", authorInstructions);
        try
        {
            var request = new TutorResearchRequest(kind, researchMode, (JsonObject)selected.DeepClone(), authorInstructions, targetAudience, useWebResearch, previousDiscussion, target.Difficulty, target.Goals, target.Notes);
            var proposed = await client.ResearchTutorAsync(context, request, active.Token);
            active.Token.ThrowIfCancellationRequested(); LastReply = proposed;
            if (proposed.HasDraft) { Proposal = proposed; after.Text = Readable(proposed.ProposedRecord); }
            RenderProposal(proposed); AppendConversation("ChatGPT", ConversationBody(proposed));
            string next = "AUTHOR: " + authorInstructions + "\nCHATGPT: " + proposed.ConversationReply + "\n" + proposed.Summary +
                (proposed.HasDraft ? "\nLATEST DRAFT: " + proposed.ProposedRecord.ToJsonString() : "\nDISCUSSION ONLY: no draft supplied.") + "\nREVIEW NOTES: " + proposed.StatisticalNotes +
                "\nPROPOSED R CHECK (not run): " + proposed.RValidationCode + "\nSOURCE CITATIONS (browsing evidence, not verification): " + JsonSerializer.Serialize(proposed.Sources) + "\n\n";
            previousDiscussion += next;
            if (previousDiscussion.Length > 180000) previousDiscussion = "[Earlier discussion omitted for length.]\n" + previousDiscussion[^180000..];
            return proposed;
        }
        catch (OperationCanceledException) { RemoveQueuedMessage(); if (!IsDisposed) { summary.Text = "Stopped. Any earlier draft remains available."; AppendConversation("Status", "Request stopped; no partial reply was applied."); } throw; }
        catch (Exception error) { RemoveQueuedMessage(); LastError = error.Message; if (!IsDisposed) { summary.Text = error.Message; AppendConversation("Status", error.Message); } throw; }
        finally
        {
            FinishOperation();
            if (queued is { } next && !closingRequested && !IsDisposed && !Disposing) { queued = null; _ = RunQueuedAsync(next); }
        }
    }
    private async Task RunQueuedAsync(QueuedMessage next)
    {
        try { next.Completion.TrySetResult(await ResearchCoreAsync(next.Instructions, next.Audience, next.Mode, next.Web, next.Targets, default)); }
        catch (OperationCanceledException) { next.Completion.TrySetCanceled(); }
        catch (Exception error) { next.Completion.TrySetException(error); }
    }
    public void RemoveQueuedMessage()
    {
        if (queued is not { } waiting) return;
        queued = null; waiting.Completion.TrySetCanceled();
        if (!IsDisposed && !Disposing) { if (instructions.Text.Length == 0) instructions.Text = waiting.Instructions; UpdateButtons(); }
    }
    public void StopResearch() { RemoveQueuedMessage(); active?.Cancel(); if (!IsDisposed) summary.Text = "Stopping… any earlier draft remains available."; }
    public void AcceptProposal()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (active != null || Proposal is not { HasDraft: true }) throw new InvalidOperationException("Finish a complete draft before accepting it.");
        TargetsChanged();
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
        active?.Dispose(); active = null; researching = false;
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
        research.Text = researching ? "Queue follow-up" : "Send"; research.Enabled = client.IsSignedIn && (!busy || researching && queued == null); stop.Enabled = busy; accept.Enabled = !busy && Proposal is { HasDraft: true }; unqueue.Visible = queued != null;
        mode.Enabled = audience.Enabled = web.Enabled = difficulty.Enabled = goalNotes.Enabled = editTargets.Enabled = !busy;
        foreach (var box in goals.Values) box.Enabled = !busy;
        instructions.Enabled = true;
    }
    private void ObserveActivity(TutorResearchActivity observation)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        var observedRequest = active;
        void Display()
        {
            if (IsDisposed || !researching || active == null || !ReferenceEquals(active, observedRequest) || active.IsCancellationRequested || currentActivity.Count >= 200) return;
            currentActivity.Add(observation); live.Text = string.Join("\n", currentActivity.Select(ActivityText)); live.SelectionStart = live.TextLength; live.ScrollToCaret();
        }
        if (InvokeRequired) { try { BeginInvoke(Display); } catch (InvalidOperationException) { } } else Display();
    }
    private void RenderProposal(TutorResearchProposal value)
    {
        summary.Text = value.Summary + (!value.HasDraft && Proposal != null ? " Earlier complete draft remains on Proposed content." : ""); evidence.Text = value.WebResearchRequested && !value.WebResearchObserved ? "No web activity observed for this reply; check suggested sources. R checks are not run." : "Opened sources show browsing, not independent verification. R checks are not run.";
        notes.Text = value.StatisticalNotes; rCode.Text = value.RValidationCode.Length == 0 ? "No numerical R check proposed. Review the conceptual argument and sources." : value.RValidationCode;
        sources.Rows.Clear();
        foreach (var source in value.Sources) { int row = sources.Rows.Add(source.Title, source.Url, source.ObservedOpened ? "Opened during this research" : "Suggested — open and check"); sources.Rows[row].Cells[0].ToolTipText = source.Relevance + (source.AccessedAtUtc is { } date ? "\nAccessed: " + date.ToString("u") : ""); }
        activity.Text = value.SearchActivity.Count == 0 ? "No web-search activity was observed for this reply." : string.Join("\n\n", value.SearchActivity.Select(ActivityText)); live.Text = activity.Text;
    }
    private static string ActivityText(TutorResearchActivity item) => (item.Action is "openPage" or "open_page" or "findInPage" or "find_in_page" ? "Opened: " : "Search: ") + (item.Url.Length > 0 ? item.Url : item.Query);
    private static string ConversationBody(TutorResearchProposal value)
    {
        var text = new StringBuilder(value.ConversationReply.Length > 0 ? value.ConversationReply : value.Summary);
        if (value.HasDraft)
        {
            var row = value.ProposedRecord; text.AppendLine().AppendLine().AppendLine("Draft for review:");
            foreach (string key in new[] { "title", "objective", "stem", "summary", "steps", "challenge" })
                if (TutorResearchContext.Text(row, key) is { Length: > 0 } content) text.AppendLine(content).AppendLine();
            if (row["options"] is JsonArray options)
                foreach (var option in options) text.Append(TutorResearchContext.Text(option, "id")).Append(". ").AppendLine(TutorResearchContext.Text(option, "text")).Append("   ").AppendLine(TutorResearchContext.Text(option, "feedback"));
            if (TutorResearchContext.Text(row, "correct") is { Length: > 0 } correct) text.Append("Best answer: ").AppendLine(correct);
            if (TutorResearchContext.Text(row, "explanation") is { Length: > 0 } explanation) text.AppendLine(explanation);
        }
        if (value.StatisticalNotes.Length > 0) text.AppendLine().AppendLine("Statistical review:").AppendLine(value.StatisticalNotes);
        if (value.Sources.Count > 0) { text.AppendLine().AppendLine("Sources:"); foreach (var source in value.Sources) text.Append(source.Title).Append(" — ").AppendLine(source.Url).AppendLine(source.Relevance).AppendLine(source.ObservedOpened ? "Opened during this research; still requires review." : "Suggested; open and check."); }
        if (value.RValidationCode.Length > 0) text.AppendLine().AppendLine("Proposed R check (not run):").AppendLine(value.RValidationCode);
        return text.ToString();
    }
    private void AppendConversation(string speaker, string body)
    {
        discussion.SelectionStart = discussion.TextLength; discussion.SelectionFont = new Font(discussion.Font, FontStyle.Bold); discussion.AppendText(speaker + "\n"); discussion.SelectionFont = discussion.Font; discussion.AppendText(body + "\n\n"); discussion.ScrollToCaret();
    }
    private static string Readable(JsonObject value)
    {
        if (value.Count == 0) return "A new record will be proposed. Nothing has been added to the course.";
        var text = new StringBuilder();
        foreach (string key in new[] { "id", "title", "topic", "objective", "stem", "summary", "steps", "challenge", "options", "correct", "explanation", "hint", "lesson", "difficulty", "learnerGoals", "learnerGoalNotes", "sources", "r" })
        {
            if (!value.TryGetPropertyValue(key, out var content) || content == null) continue;
            if (key == "options" && content is JsonArray options)
                foreach (var option in options) text.Append(TutorResearchContext.Text(option, "id")).Append(". ").AppendLine(TutorResearchContext.Text(option, "text")).Append("   Feedback: ").AppendLine(TutorResearchContext.Text(option, "feedback"));
            else if (key == "learnerGoals" && content is JsonArray selectedGoals) text.AppendLine("Learner goals:").AppendLine(string.Join(", ", selectedGoals.Select(goal => goal is JsonValue scalar && scalar.TryGetValue<string>(out var id) ? TutorLearningTargets.Goals.GetValueOrDefault(id, id) : ""))).AppendLine();
            else if (key == "sources" && content is JsonArray sourceList) { text.AppendLine("Sources:"); foreach (var source in sourceList) text.AppendLine(TutorResearchContext.Text(source, "title")).AppendLine(TutorResearchContext.Text(source, "url")); }
            else if (content is JsonValue scalar && scalar.TryGetValue<string>(out var words)) text.Append(key == "difficulty" ? "Difficulty" : key == "learnerGoalNotes" ? "Qualification / learning goals" : key).Append(":\n").AppendLine(key == "difficulty" ? TutorLearningTargets.Difficulties.GetValueOrDefault(words, words) : words).AppendLine();
        }
        return text.ToString();
    }
    private void AddTab(string title, Control control) { var page = new TabPage(title); page.Controls.Add(control); tabs.TabPages.Add(page); }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RemoveQueuedMessage(); active?.Cancel();
            client.Changed -= ConnectionChanged;
            if (client is ITutorResearchProgressSource progress) progress.ResearchActivity -= ObserveActivity;
            if (ownsClient) client.Dispose();
        }
        base.Dispose(disposing);
    }
    private static Button MakeButton(string text) => new() { Text = text, AutoSize = true, AccessibleName = text };
    private static RichTextBox ReadBox(string name) => new() { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, Font = new Font("Arial", 10), BackColor = SystemColors.Window, AccessibleName = name, HideSelection = false };
    private static FlowLayoutPanel Flow() => new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 3, 0, 3) };
}
