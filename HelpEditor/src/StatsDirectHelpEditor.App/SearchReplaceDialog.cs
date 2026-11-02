using System.ComponentModel;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public sealed class SearchReplaceDialog : Form
{
    private readonly HelpProject project;
    private readonly Func<HelpReplaceResult, Task> changed;
    private readonly TextBox find = new() { Dock = DockStyle.Fill, AccessibleName = "Find text" };
    private readonly TextBox replacement = new() { Dock = DockStyle.Fill, AccessibleName = "Replace with" };
    private readonly CheckBox matchCase = new() { Text = "Match case", AutoSize = true, AccessibleName = "Match case" };
    private readonly CheckBox wholeWords = new() { Text = "Whole words", AutoSize = true, AccessibleName = "Whole words" };
    private readonly ComboBox scope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, AccessibleName = "Search scope" };
    private readonly Button search = new() { Text = "Find all", AutoSize = true, AccessibleName = "Find all" };
    private readonly Button selectAll = new() { Text = "Select all", AutoSize = true, AccessibleName = "Select all" };
    private readonly Button selectNone = new() { Text = "Select none", AutoSize = true, AccessibleName = "Select none" };
    private readonly Button applySelected = new() { Text = "Replace selected", AutoSize = true, AccessibleName = "Replace selected" };
    private readonly Button applyAll = new() { Text = "Replace all", AutoSize = true, AccessibleName = "Replace all" };
    private readonly Button undo = new() { Text = "Undo last replace", AutoSize = true, AccessibleName = "Undo last replace" };
    private readonly Button open = new() { Text = "Open page", AutoSize = true, AccessibleName = "Open page" };
    private readonly Button close = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel, AccessibleName = "Close" };
    private readonly Label summary = new() { AutoSize = true, Dock = DockStyle.Fill, Text = "Enter the text to find, then choose Find all." };
    private readonly Label notice = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(45, 65, 82) };
    private readonly DataGridView results = new()
    {
        Dock = DockStyle.Fill, AccessibleName = "Search results", AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells, BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle,
        AutoGenerateColumns = false, VirtualMode = true, EditMode = DataGridViewEditMode.EditOnEnter
    };
    private HelpSearchPreview? preview;
    private bool[] selectedMatches = [];
    private int selectedCount;
    private bool busy;

    public HelpSearchMatch? RequestedMatch { get; private set; }
    public string? LastError { get; private set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool SuppressDialogs { get; set; }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        int Scale(int value) => (int)Math.Ceiling(value * DeviceDpi / 96d);
        var area = Screen.FromControl(Owner ?? this).WorkingArea;
        int availableWidth = Math.Max(Scale(480), area.Width - Scale(24));
        int availableHeight = Math.Max(Scale(360), area.Height - Scale(24));
        MinimumSize = new Size(Math.Min(Scale(860), availableWidth), Math.Min(Scale(600), availableHeight));
        Size = new Size(Math.Min(Scale(1140), availableWidth), Math.Min(Scale(760), availableHeight));
        SizeSearchControls();
        CenterToParent();
        Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)), Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    private void SizeSearchControls()
    {
        int Scale(int value) => (int)Math.Ceiling(value * DeviceDpi / 96d);
        scope.Width = scope.Items.Cast<string>().Select(text => TextRenderer.MeasureText(text, scope.Font).Width).Max() + Scale(36);
        scope.DropDownWidth = scope.Width;
        foreach (string name in new[] { "Replace", "Line" })
        {
            var column = results.Columns[name]!;
            column.Width = Math.Max(Scale(name == "Replace" ? 85 : 65), TextRenderer.MeasureText(column.HeaderText, results.Font).Width + Scale(16));
        }
    }

    public SearchReplaceDialog(HelpProject project, Func<HelpReplaceResult, Task> changedCallback)
    {
        this.project = project;
        changed = changedCallback;
        Text = "Find and replace in all pages";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = new Font("Arial", 11);
        Size = new Size(1140, 760);
        MinimumSize = new Size(860, 600);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        scope.Items.AddRange(["Page text", "HTML source"]);
        scope.SelectedIndex = 0;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var inputs = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 3, Margin = Padding.Empty };
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputs.Controls.Add(new Label { Text = "Find:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        inputs.Controls.Add(find, 1, 0);
        inputs.Controls.Add(search, 2, 0);
        inputs.Controls.Add(new Label { Text = "Replace with:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        inputs.Controls.Add(replacement, 1, 1);
        inputs.SetColumnSpan(replacement, 2);
        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 7, 0, 5) };
        options.Controls.AddRange([matchCase, wholeWords, new Label { Text = "Search in:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(12, 4, 0, 0) }, scope]);
        inputs.Controls.Add(options, 0, 2);
        inputs.SetColumnSpan(options, 3);
        layout.Controls.Add(inputs, 0, 0);
        var explanation = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 10),
            Text = "Searches all saved help pages, including pages outside Contents. Page text skips equations and R/code blocks; HTML source also searches markup and link URLs. Review the matches before replacing." };
        layout.Controls.Add(explanation, 0, 1);
        summary.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(summary, 0, 2);
        results.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Replace", HeaderText = "Replace", Width = 85, SortMode = DataGridViewColumnSortMode.NotSortable });
        results.Columns.Add(new DataGridViewTextBoxColumn { Name = "Page", HeaderText = "Page", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 24 });
        results.Columns.Add(new DataGridViewTextBoxColumn { Name = "Line", HeaderText = "Line", ReadOnly = true, Width = 65 });
        results.Columns.Add(new DataGridViewTextBoxColumn { Name = "Before", HeaderText = "Before", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 38 });
        results.Columns.Add(new DataGridViewTextBoxColumn { Name = "After", HeaderText = "After", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 38 });
        results.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        foreach (DataGridViewColumn column in results.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        layout.Controls.Add(results, 0, 3);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 8, 0, 6) };
        actions.Controls.AddRange([selectAll, selectNone, open, applySelected, applyAll, undo, close]);
        layout.Controls.Add(actions, 0, 4);
        notice.Margin = new Padding(0, 0, 0, 2);
        layout.Controls.Add(notice, 0, 5);
        Controls.Add(layout);
        AcceptButton = search;
        CancelButton = close;
        search.Click += async (_, _) => await RunAsync(FindAsync);
        applySelected.Click += async (_, _) => await RunAsync(() => ReplaceAsync(all: false));
        applyAll.Click += async (_, _) => await RunAsync(() => ReplaceAsync(all: true));
        undo.Click += async (_, _) => await RunAsync(UndoAsync);
        selectAll.Click += (_, _) => SelectMatches(true);
        selectNone.Click += (_, _) => SelectMatches(false);
        open.Click += (_, _) => OpenMatch();
        results.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex != 0) OpenMatch(); };
        results.CurrentCellDirtyStateChanged += (_, _) => { if (results.IsCurrentCellDirty) results.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        results.CellValueChanged += (_, _) => UpdateActions();
        results.CellValueNeeded += (_, e) =>
        {
            if (preview == null || e.RowIndex < 0 || e.RowIndex >= preview.Matches.Count || e.RowIndex >= selectedMatches.Length) return;
            var match = preview.Matches[e.RowIndex];
            e.Value = e.ColumnIndex switch
            {
                0 => selectedMatches[e.RowIndex], 1 => match.Title + "\n" + match.TopicPath,
                2 => match.Line, 3 => match.Before, 4 => match.After, _ => null
            };
        };
        results.CellValuePushed += (_, e) =>
        {
            if (e.ColumnIndex != 0 || e.RowIndex < 0 || e.RowIndex >= selectedMatches.Length) return;
            bool value = Equals(e.Value, true);
            if (value != selectedMatches[e.RowIndex]) { selectedCount += value ? 1 : -1; selectedMatches[e.RowIndex] = value; }
            UpdateActions();
        };
        results.SelectionChanged += (_, _) => UpdateActions();
        results.CurrentCellChanged += (_, _) => UpdateActions();
        find.TextChanged += (_, _) => InvalidatePreview();
        replacement.TextChanged += (_, _) => InvalidatePreview();
        matchCase.CheckedChanged += (_, _) => InvalidatePreview();
        wholeWords.CheckedChanged += (_, _) => InvalidatePreview();
        scope.SelectedIndexChanged += (_, _) => InvalidatePreview();
        Shown += (_, _) => find.Focus();
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        DpiChanged += (_, _) => SizeSearchControls();
        UpdateActions();
    }

    private HelpSearchOptions Options() => new(find.Text, replacement.Text, matchCase.Checked, wholeWords.Checked,
        scope.SelectedIndex == 1 ? HelpSearchScope.HtmlSource : HelpSearchScope.PageText);

    private void InvalidatePreview()
    {
        if (!busy)
        {
            preview = null;
            selectedMatches = [];
            selectedCount = 0;
            results.Rows.Clear();
            summary.Text = "Search options changed. Choose Find all to review the current matches.";
            notice.Text = "";
        }
        UpdateActions();
    }

    private async Task FindAsync()
    {
        notice.Text = "Searching saved help pages…";
        var options = Options();
        var found = await Task.Run(() => project.SearchReplacePreview(options));
        results.RowCount = 0;
        preview = found;
        selectedMatches = Enumerable.Repeat(true, preview.Matches.Count).ToArray();
        selectedCount = selectedMatches.Length;
        results.RowCount = preview.Matches.Count;
        results.Invalidate();
        summary.Text = $"{preview.Matches.Count} matches in {preview.Matches.Select(match => match.TopicPath).Distinct().Count()} pages · {preview.PageCount} pages searched";
        notice.Text = preview.Matches.Count == 0 ? "No matches. No files were changed." : "Tick the matches to replace, or choose Replace all. Open page shows the exact match in HTML source.";
    }

    private async Task ReplaceAsync(bool all)
    {
        if (preview == null || preview.Options != Options()) throw new InvalidOperationException("Choose Find all again before replacing.");
        results.EndEdit();
        var selected = preview.Matches.Where((_, index) => all || selectedMatches[index]).Select(match => match.Id).ToArray();
        notice.Text = "Replacing the reviewed matches…";
        var result = await Task.Run(() => project.ApplySearchReplace(preview, selected));
        if (result.ChangedPages > 0) await changed(result);
        await FindAsync();
        notice.Text = result.ChangedPages > 0 ? $"Replaced {result.Replacements} matches in {result.ChangedPages} pages. Undo last replace restores that batch during this session."
            : "No files needed changing.";
    }

    private async Task UndoAsync()
    {
        notice.Text = "Restoring the last replacement batch…";
        var result = await Task.Run(project.UndoLastSearchReplace);
        if (result.ChangedPages > 0) await changed(result);
        if (find.Text.Length > 0) await FindAsync();
        else
        {
            preview = null;
            selectedMatches = [];
            selectedCount = 0;
            results.RowCount = 0;
            summary.Text = "Enter the text to find, then choose Find all.";
        }
        notice.Text = $"Restored {result.ChangedPages} pages from the last replacement batch.";
    }

    private void SelectMatches(bool selected)
    {
        Array.Fill(selectedMatches, selected);
        selectedCount = selected ? selectedMatches.Length : 0;
        results.Invalidate();
        UpdateActions();
    }

    private void OpenMatch()
    {
        if (busy || preview == null || results.CurrentRow is not { } row || row.Index >= preview.Matches.Count) return;
        try { project.ValidateSearchReplacePreview(preview); }
        catch (Exception ex)
        {
            LastError = ex.Message;
            notice.ForeColor = Color.FromArgb(155, 35, 25);
            notice.Text = ex.Message;
            preview = null;
            selectedMatches = [];
            selectedCount = 0;
            results.RowCount = 0;
            summary.Text = "The page changed after this search. Choose Find all again before opening a match.";
            UpdateActions();
            return;
        }
        RequestedMatch = preview.Matches[row.Index];
        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        LastError = null;
        notice.ForeColor = Color.FromArgb(45, 65, 82);
        UseWaitCursor = true;
        UpdateActions();
        try { await action(); }
        catch (Exception ex)
        {
            LastError = ex.Message;
            notice.ForeColor = Color.FromArgb(155, 35, 25);
            notice.Text = ex.Message;
            preview = null;
            selectedMatches = [];
            selectedCount = 0;
            results.Rows.Clear();
            summary.Text = "The operation did not complete. Correct the issue and run Find all again.";
        }
        finally { busy = false; UseWaitCursor = false; UpdateActions(); }
    }

    private void UpdateActions()
    {
        bool reviewed = !busy && preview != null && preview.Options == Options() && results.Rows.Count > 0;
        search.Enabled = !busy && find.Text.Length > 0;
        find.Enabled = replacement.Enabled = matchCase.Enabled = wholeWords.Enabled = scope.Enabled = !busy;
        results.Enabled = !busy;
        selectAll.Enabled = selectNone.Enabled = applyAll.Enabled = reviewed;
        applySelected.Enabled = reviewed && selectedCount > 0;
        applySelected.Text = "Replace selected (" + selectedCount + ")";
        open.Enabled = reviewed && results.CurrentRow != null;
        undo.Enabled = !busy && project.CanUndoSearchReplace;
        close.Enabled = !busy;
    }
}
