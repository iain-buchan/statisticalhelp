using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

/// <summary>A read-only recovery chooser; the host performs restoration after it closes.</summary>
public sealed class DeletedPagesDialog : Form
{
    private readonly ListView deletedPages = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false, ShowItemToolTips = true,
        AccessibleName = "Deleted pages"
    };
    private readonly Button restore = new() { Text = "Restore", AutoSize = true, MinimumSize = new Size(120, 40), Padding = new Padding(14, 6, 14, 6), Enabled = false };
    private readonly Label detail = new() { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 8), Text = "Select a deleted page to restore." };

    public string? SelectedTopicId { get; private set; }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // These runtime-created dialog bounds do not pass through a designer's
        // scaling step. Match their logical size to the actual display font.
        int Scale(int value) => (int)Math.Ceiling(value * DeviceDpi / 96d);
        var area = Screen.FromControl(this).WorkingArea;
        int availableWidth = Math.Max(Scale(480), area.Width - Scale(24));
        int availableHeight = Math.Max(Scale(320), area.Height - Scale(24));
        MinimumSize = new Size(Math.Min(Scale(760), availableWidth), Math.Min(Scale(440), availableHeight));
        Size = new Size(Math.Min(Scale(980), availableWidth), Math.Min(Scale(560), availableHeight));
        CenterToParent();
        Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        restore.MinimumSize = new Size(Scale(120), Scale(40));
        if (CancelButton is Button close) close.MinimumSize = restore.MinimumSize;
        SizeColumns();
    }

    public DeletedPagesDialog(IReadOnlyList<DeletedTopicInfo> records)
    {
        Text = "Deleted pages";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = new Font("Arial", 11);
        Size = new Size(980, 560);
        MinimumSize = new Size(760, 440);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var explanation = new Label
        {
            AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 10),
            Text = records.Count == 0 ? "There are no deleted pages in this working copy." :
                "Choose a page to restore to its original path. Its Contents entries will also be recovered."
        };
        deletedPages.Columns.Add("Page title");
        deletedPages.Columns.Add("Original page path");
        deletedPages.Columns.Add("Deleted");
        foreach (var record in records.OrderByDescending(r => r.DeletedAtUtc))
        {
            var item = new ListViewItem(record.Title) { Tag = record, ToolTipText = record.Title + "\n" + record.OriginalPath + "\n" + (record.Problem ?? "Ready to restore") };
            item.SubItems.Add(record.OriginalPath);
            item.SubItems.Add(record.DeletedAtUtc.ToLocalTime().ToString("g"));
            deletedPages.Items.Add(item);
        }
        deletedPages.SelectedIndexChanged += (_, _) => UpdateSelection();
        deletedPages.DoubleClick += (_, _) => ChooseRestore();
        deletedPages.SizeChanged += (_, _) => SizeColumns();
        restore.Click += (_, _) => ChooseRestore();
        var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(120, 40), Padding = new Padding(14, 6, 14, 6) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        buttons.Controls.Add(close);
        buttons.Controls.Add(restore);
        layout.Controls.Add(explanation, 0, 0);
        layout.Controls.Add(deletedPages, 0, 1);
        layout.Controls.Add(detail, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        AcceptButton = restore;
        CancelButton = close;
        Shown += (_, _) =>
        {
            SizeColumns();
            if (deletedPages.Items.Count != 0) deletedPages.Items[0].Selected = true;
            UpdateSelection();
            deletedPages.Focus();
        };
    }

    private void SizeColumns()
    {
        int available = Math.Max(0, deletedPages.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        deletedPages.Columns[0].Width = (int)(available * .29);
        deletedPages.Columns[1].Width = (int)(available * .49);
        deletedPages.Columns[2].Width = available - deletedPages.Columns[0].Width - deletedPages.Columns[1].Width;
    }

    private void UpdateSelection()
    {
        var record = deletedPages.SelectedItems.Count == 1 ? (DeletedTopicInfo)deletedPages.SelectedItems[0].Tag! : null;
        restore.Enabled = record?.CanRestore == true;
        detail.Text = record == null ? deletedPages.Items.Count == 0 ? "Pages deleted in this editor will appear here." : "Select a deleted page to restore." : record.Problem ?? record.OriginalPath;
    }

    private void ChooseRestore()
    {
        if (deletedPages.SelectedItems.Count != 1 || deletedPages.SelectedItems[0].Tag is not DeletedTopicInfo record || !record.CanRestore) return;
        SelectedTopicId = record.Id;
        DialogResult = DialogResult.OK;
        Close();
    }
}
