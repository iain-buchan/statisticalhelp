using System.ComponentModel;
using System.Text.RegularExpressions;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

/// <summary>A proposed edit never enters the document until explicitly accepted and applied.</summary>
public sealed class PolishReviewDialog : Form
{
    private readonly HelpPolishSnapshot snapshot;
    private readonly HelpPolishContext context;
    private readonly IPolishChatGptClient client;
    private readonly Action<Uri> launch;
    private readonly Label connection = new() { AutoSize = true, Anchor = AnchorStyles.Left, Text = "ChatGPT is not connected" };
    private readonly Label scope = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly TextBox instructions = new() { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
        AccessibleName = "Polishing instructions", Text = "Improve clarity, grammar and consistency in British English. Keep the statistical meaning, advanced results and terminology. Avoid unnecessary rewriting." };
    private readonly Button signIn = Button("Sign in with ChatGPT");
    private readonly Button signOut = Button("Sign out");
    private readonly Button suggest = Button("Suggest changes");
    private readonly Button stop = Button("Stop");
    private readonly Button accept = Button("Accept change");
    private readonly Button reject = Button("Reject change");
    private readonly Button acceptAll = Button("Accept all");
    private readonly Button rejectAll = Button("Reject all");
    private readonly Button apply = Button("Apply accepted changes");
    private readonly Button close = Button("Close");
    private readonly Label summary = new() { AutoSize = true, Dock = DockStyle.Fill, Text = "Choose Suggest changes to request a review. Your page stays unchanged until you apply accepted changes." };
    private readonly Label reason = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(45, 65, 82) };
    private readonly RichTextBox diff = new() { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, BackColor = SystemColors.Window,
        BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Tracked changes", HideSelection = false };
    private readonly DataGridView changes = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoGenerateColumns = false,
        BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Suggested changes" };
    private readonly SplitContainer reviewSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
        Panel1MinSize = 100, Panel2MinSize = 100, SplitterDistance = 350, Size = new Size(1000, 400) };
    private readonly Dictionary<string, string> decisions = new(StringComparer.Ordinal);
    private CancellationTokenSource? active;
    private bool closeRequested;
    private bool refreshing;

    public HelpPolishReview? Review { get; private set; }
    public IReadOnlyList<string> AcceptedChangeIds => Review?.Changes.Where(change => decisions.GetValueOrDefault(change.Id) == "Accepted")
        .Select(change => change.Id).ToArray() ?? [];
    public string? LastError { get; private set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool SuppressDialogs { get; set; }

    public PolishReviewDialog(HelpPolishSnapshot snapshot, HelpPolishContext context, IPolishChatGptClient client, Action<Uri>? externalLinkLauncher = null)
    {
        this.snapshot = snapshot;
        this.context = context;
        this.client = client;
        launch = externalLinkLauncher ?? (uri => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        Text = "Polish current page with ChatGPT";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = new Font("Arial", 11);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 8 };
        for (int row = 0; row < 8; row++) layout.RowStyles.Add(new RowStyle(row == 5 ? SizeType.Percent : SizeType.AutoSize, row == 5 ? 100 : 0));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold), Text = snapshot.TopicPath }, 0, 0);
        var account = Flow(); account.Controls.AddRange([connection, signIn, signOut, suggest, stop]);
        layout.Controls.Add(account, 0, 1);
        scope.Text = $"All {context.PageCount} help pages are available for context, including this draft and any unsaved edits. Suggest changes sends the current page and requested help context to OpenAI. Only this page can be edited.";
        scope.Margin = new Padding(3, 4, 3, 8);
        layout.Controls.Add(scope, 0, 2);
        var instructionPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        instructionPanel.Controls.Add(new Label { AutoSize = true, Text = "Instructions for this review:" }, 0, 0);
        instructions.Height = Font.Height * 3 + 12;
        instructionPanel.Controls.Add(instructions, 0, 1);
        layout.Controls.Add(instructionPanel, 0, 3);
        layout.Controls.Add(summary, 0, 4);
        changes.Columns.Add(new DataGridViewTextBoxColumn { Name = "Decision", HeaderText = "Decision", Width = 88, SortMode = DataGridViewColumnSortMode.NotSortable });
        changes.Columns.Add(new DataGridViewTextBoxColumn { Name = "Wording", HeaderText = "Original wording", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        changes.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        changes.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        reviewSplit.Panel1.Controls.Add(changes);
        var detail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(7, 0, 0, 0) };
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize)); detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill, Text = "Tracked changes: red strike-through = deleted; green underline = added" }, 0, 0);
        detail.Controls.Add(diff, 0, 1); detail.Controls.Add(reason, 0, 2);
        reviewSplit.Panel2.Controls.Add(detail);
        layout.Controls.Add(reviewSplit, 0, 5);
        var decisionsBar = Flow(); decisionsBar.Controls.AddRange([accept, reject, acceptAll, rejectAll]); layout.Controls.Add(decisionsBar, 0, 6);
        var finalBar = Flow(); finalBar.FlowDirection = FlowDirection.RightToLeft; finalBar.Controls.AddRange([close, apply]); layout.Controls.Add(finalBar, 0, 7);
        Controls.Add(layout);
        foreach (var label in new[] { scope, summary, reason })
            label.SizeChanged += (_, _) => { if (label.Parent is { } parent) label.MaximumSize = new Size(Math.Max(100, parent.ClientSize.Width - 20), 0); };
        changes.SelectionChanged += (_, _) => ShowChange();
        signIn.Click += async (_, _) => await RunAsync(async token =>
        {
            string? url = await client.SignInAsync(token);
            if (url != null)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("ChatGPT returned an invalid sign-in address.");
                launch(uri);
                summary.Text = "Complete sign-in in your browser, then choose Suggest changes.";
            }
        });
        signOut.Click += async (_, _) => await RunAsync(client.SignOutAsync);
        suggest.Click += async (_, _) =>
        {
            // A second request must not discard decisions already under review.
            if (Review is { Changes.Count: > 0 }) return;
            string request = instructions.Text;
            await RunAsync(async token =>
            {
                summary.Text = "ChatGPT is reviewing this page and can read related help pages…";
                var review = await client.PolishAsync(context, snapshot, request, token);
                token.ThrowIfCancellationRequested();
                SetReview(review);
            });
        };
        stop.Click += async (_, _) =>
        {
            if (active != null) { active.Cancel(); summary.Text = "Stopping…"; }
            else if (client.LoginUrl != null) await RunAsync(client.CancelSignInAsync);
        };
        accept.Click += (_, _) => Decide("Accepted"); reject.Click += (_, _) => Decide("Rejected");
        acceptAll.Click += (_, _) => DecideAll("Accepted"); rejectAll.Click += (_, _) => DecideAll("Rejected");
        apply.Click += (_, _) => { if (AcceptedChangeIds.Count > 0) { DialogResult = DialogResult.OK; Close(); } };
        close.Click += (_, _) => Close();
        client.Changed += ClientChanged;
        Shown += async (_, _) => await RunAsync(client.InitializeAsync);
        FormClosing += (_, e) =>
        {
            if (active != null) { e.Cancel = true; closeRequested = true; active.Cancel(); summary.Text = "Stopping…"; }
        };
        FormClosed += (_, _) => client.Changed -= ClientChanged;
        UpdateActions();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        int Scale(int value) => (int)Math.Ceiling(value * DeviceDpi / 96d);
        var area = Screen.FromControl(Owner ?? this).WorkingArea;
        MinimumSize = new Size(Math.Min(Scale(950), area.Width - Scale(24)), Math.Min(Scale(650), area.Height - Scale(24)));
        Size = new Size(Math.Min(Scale(1180), area.Width - Scale(24)), Math.Min(Scale(800), area.Height - Scale(24)));
        reviewSplit.SplitterDistance = Math.Max(100, (int)(ClientSize.Width * .34));
        changes.RowTemplate.Height = Math.Max(Font.Height + Scale(8), Scale(27));
        changes.Columns["Decision"]!.Width = TextRenderer.MeasureText("Accepted", Font).Width + Scale(28);
        instructions.Height = Font.Height * 3 + Scale(10);
        CenterToParent();
        Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)), Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    public void SetReview(HelpPolishReview review)
    {
        if (!ReferenceEquals(review.Snapshot, snapshot)) throw new InvalidOperationException("The review belongs to another draft.");
        Review = review;
        decisions.Clear();
        refreshing = true;
        try
        {
            changes.Rows.Clear();
            foreach (var change in review.Changes)
            {
                decisions.Add(change.Id, "Pending");
                int row = changes.Rows.Add("Pending", Regex.Replace(change.Before, @"\s+", " "));
                changes.Rows[row].Tag = change;
            }
        }
        finally { refreshing = false; }
        LastError = null;
        summary.ForeColor = SystemColors.ControlText;
        UpdateSummary(); ShowChange(); UpdateActions();
    }

    private HelpPolishChange? Selected => changes.CurrentRow?.Tag as HelpPolishChange;
    private void Decide(string value)
    {
        if (active != null || Selected is not { } change) return;
        decisions[change.Id] = value;
        changes.CurrentRow!.Cells[0].Value = value;
        var next = changes.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => row.Tag is HelpPolishChange item && decisions[item.Id] == "Pending");
        if (next != null) changes.CurrentCell = next.Cells[0];
        UpdateSummary(); ShowChange(); UpdateActions();
    }
    private void DecideAll(string value)
    {
        if (active != null || Review == null) return;
        foreach (DataGridViewRow row in changes.Rows)
            if (row.Tag is HelpPolishChange change) { decisions[change.Id] = value; row.Cells[0].Value = value; }
        UpdateSummary(); ShowChange(); UpdateActions();
    }
    private void UpdateSummary()
    {
        if (Review == null) return;
        int accepted = decisions.Count(pair => pair.Value == "Accepted"), rejected = decisions.Count(pair => pair.Value == "Rejected");
        summary.Text = Review.Changes.Count == 0 ? "ChatGPT suggested no wording changes. Your page is unchanged." :
            $"{Review.Changes.Count} proposed changes · {accepted} accepted · {rejected} rejected · {Review.Changes.Count - accepted - rejected} pending. Apply writes accepted changes into the unsaved draft only.";
    }
    private void ShowChange()
    {
        if (refreshing) return;
        diff.Clear();
        if (Selected is not { } change) { reason.Text = ""; UpdateActions(); return; }
        reason.Text = change.Location + Environment.NewLine + change.Reason;
        AppendDiff(diff, change.Before, change.After);
        diff.Select(0, 0);
        UpdateActions();
    }

    // A bounded word diff makes small edits readable without disguising a full rewrite.
    private static void AppendDiff(RichTextBox box, string before, string after)
    {
        string[] oldWords = Regex.Matches(before, @"\s+|[\p{L}\p{N}_]+|[^\s]").Select(match => match.Value).ToArray();
        string[] newWords = Regex.Matches(after, @"\s+|[\p{L}\p{N}_]+|[^\s]").Select(match => match.Value).ToArray();
        using var normal = new Font(box.Font, FontStyle.Regular);
        using var deleted = new Font(box.Font, FontStyle.Strikeout);
        using var added = new Font(box.Font, FontStyle.Underline);
        void Add(string text, int kind)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = kind < 0 ? Color.FromArgb(165, 30, 30) : kind > 0 ? Color.FromArgb(0, 105, 50) : SystemColors.WindowText;
            box.SelectionFont = kind < 0 ? deleted : kind > 0 ? added : normal;
            box.AppendText(text);
        }
        if ((long)oldWords.Length * newWords.Length > 300_000)
        { Add(before, -1); Add(Environment.NewLine, 0); Add(after, 1); return; }
        int[,] lengths = new int[oldWords.Length + 1, newWords.Length + 1];
        for (int i = oldWords.Length - 1; i >= 0; i--)
            for (int j = newWords.Length - 1; j >= 0; j--)
                lengths[i, j] = oldWords[i] == newWords[j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        int a = 0, b = 0;
        while (a < oldWords.Length || b < newWords.Length)
        {
            if (a < oldWords.Length && b < newWords.Length && oldWords[a] == newWords[b]) { Add(oldWords[a++], 0); b++; }
            else if (a < oldWords.Length && (b >= newWords.Length || lengths[a + 1, b] >= lengths[a, b + 1])) Add(oldWords[a++], -1);
            else Add(newWords[b++], 1);
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (active != null) return;
        using var cancellation = new CancellationTokenSource(); active = cancellation;
        LastError = null; summary.ForeColor = SystemColors.ControlText; UpdateActions();
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { if (!IsDisposed) summary.Text = "Stopped. No page changes were applied."; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            if (!IsDisposed) { summary.ForeColor = Color.FromArgb(165, 30, 30); summary.Text = ex.Message; }
        }
        finally
        {
            active = null;
            if (!IsDisposed) { UpdateActions(); if (closeRequested) Close(); }
        }
    }
    private void ClientChanged()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(ClientChanged); return; }
        UpdateActions();
    }
    private void UpdateActions()
    {
        bool idle = active == null;
        connection.Text = client.Status;
        signIn.Enabled = idle && !client.IsSignedIn;
        signOut.Enabled = idle && client.IsSignedIn;
        suggest.Enabled = idle && client.IsSignedIn && snapshot.Blocks.Count > 0 && Review is not { Changes.Count: > 0 };
        stop.Enabled = !idle || client.LoginUrl != null;
        instructions.Enabled = idle && Review is not { Changes.Count: > 0 };
        accept.Enabled = reject.Enabled = idle && Selected != null;
        acceptAll.Enabled = rejectAll.Enabled = idle && Review?.Changes.Count > 0;
        apply.Enabled = idle && AcceptedChangeIds.Count > 0;
        changes.Enabled = idle;
        close.Text = idle ? "Close" : "Stop and close";
    }
    private static Button Button(string text) => new() { Text = text, AccessibleName = text, AutoSize = true, Margin = new Padding(3, 4, 7, 4) };
    private static FlowLayoutPanel Flow() => new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
}
