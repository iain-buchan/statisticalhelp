#nullable disable
// Snapshot of StatsDirect Windows help host (MIT).
// Original: StatsDirectUI/UI/HtmlHelp/HelpIndexMenu.cs
// See provenance.json for source hashes and the deliberately limited editor integration changes.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace StatsDirect.HelpEditor.App.AppHelp;

/// <summary>A reusable keyword picker. Moving through the list never opens a topic.</summary>
internal sealed class HelpIndexMenu : ToolStripDropDown
{
    private readonly TextBox filter = new() { Dock = DockStyle.Top, PlaceholderText = "Type a keyword", AccessibleName = "Filter index keywords", TabIndex = 0 };
    private readonly ListBox list = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, AccessibleName = "Help keyword index", TabIndex = 1 };
    private readonly Label count = new() { Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label target = new() { Dock = DockStyle.Bottom, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, AccessibleName = "Selected index topic" };
    private readonly Panel panel = new() { TabStop = false };
    private readonly ToolStripControlHost host;
    private IReadOnlyList<HelpIndexEntry> source;
    private IndexRow[] rows = [];
    internal event Action<HelpIndexEntry> TopicChosen;

    internal HelpIndexMenu()
    {
        AutoSize = false;
        AutoClose = true;
        AccessibleName = "Help index";
        panel.Controls.Add(list);
        panel.Controls.Add(target);
        panel.Controls.Add(count);
        panel.Controls.Add(filter);
        host = new ToolStripControlHost(panel) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
        Items.Add(host);
        filter.TextChanged += (_, _) => ApplyFilter();
        filter.KeyDown += (_, e) =>
        {
            if (e.KeyData is Keys.Down or Keys.Up) { HandleKey(e.KeyData); e.SuppressKeyPress = true; }
        };
        list.SelectedIndexChanged += (_, _) => target.Text = list.SelectedItem is IndexRow row ? row.Entry.Title + (row.ShowPath ? Environment.NewLine + row.Entry.Path : "") : "";
        list.MouseUp += (_, e) =>
        {
            int row = list.IndexFromPoint(e.Location);
            if (e.Button == MouseButtons.Left && row >= 0 && list.GetItemRectangle(row).Contains(e.Location)) Choose();
        };
    }

    internal void Prepare(IReadOnlyList<HelpIndexEntry> entries, Control owner, ToolStripItem button)
    {
        if (!ReferenceEquals(source, entries))
        {
            source = entries;
            var multiple = entries.GroupBy(e => e.Term, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var duplicateTitles = entries.GroupBy(e => e.Term + "\0" + e.Title, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            rows = entries.Select(e => new IndexRow(e, !multiple.Contains(e.Term) ? e.Term :
                e.Term + " — " + e.Title + (duplicateTitles.Contains(e.Term + "\0" + e.Title) ? " (" + e.Path + ")" : ""), duplicateTitles.Contains(e.Term + "\0" + e.Title))).ToArray();
            ApplyFilter();
        }
        Font = owner.Font;
        panel.Font = owner.Font;
        float scale = owner.DeviceDpi / 96f;
        var parent = button.GetCurrentParent() ?? owner;
        var anchor = parent.PointToScreen(button.Bounds.Location);
        var screen = Screen.FromPoint(anchor).WorkingArea;
        Padding = new Padding((int)Math.Ceiling(8 * scale));
        Size = new Size(Math.Min((int)Math.Ceiling(440 * scale), screen.Width - 16), Math.Min((int)Math.Ceiling(480 * scale), screen.Height - 16));
        host.Size = panel.Size = new Size(Width - Padding.Horizontal, Height - Padding.Vertical);
        filter.Height = Math.Max(filter.PreferredHeight, (int)Math.Ceiling(26 * scale));
        count.Height = (int)Math.Ceiling(26 * scale);
        target.Height = (int)Math.Ceiling(42 * scale);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        BeginInvoke(() => { if (!IsDisposed && Visible) { filter.Focus(); filter.SelectAll(); } });
    }

    private void ApplyFilter()
    {
        var selected = (list.SelectedItem as IndexRow)?.Entry;
        string query = filter.Text.Trim();
        var compare = CultureInfo.GetCultureInfo("en-GB").CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        var matches = rows.Where(row => query.Length == 0 || compare.IndexOf(row.Entry.Term, query, options) >= 0 || compare.IndexOf(row.Entry.Title, query, options) >= 0).ToArray();
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            list.Items.AddRange(matches);
            if (matches.Length > 0)
            {
                int previous = Array.FindIndex(matches, row => row.Entry == selected);
                list.SelectedIndex = Math.Max(0, previous);
            }
        }
        finally { list.EndUpdate(); }
        count.Text = matches.Length == 0 ? "No matching index entries" : query.Length == 0 ? $"{matches.Length} index entries" : $"{matches.Length} of {rows.Length} entries";
        if (matches.Length == 0) target.Text = "";
    }

    private void Choose()
    {
        if (list.SelectedItem is not IndexRow row) return;
        Close(ToolStripDropDownCloseReason.ItemClicked);
        TopicChosen?.Invoke(row.Entry);
    }

    private bool HandleKey(Keys key)
    {
        if (!Visible) return false;
        if (key == Keys.Escape) { Close(ToolStripDropDownCloseReason.Keyboard); return true; }
        if (key == Keys.Enter) { Choose(); return true; }
        if (key == Keys.Tab || key == (Keys.Shift | Keys.Tab))
        {
            if (filter.ContainsFocus && list.Items.Count > 0) list.Focus(); else filter.Focus();
            return true;
        }
        if (filter.ContainsFocus && key is Keys.Down or Keys.Up)
        {
            if (list.Items.Count > 0) { list.Focus(); if (list.SelectedIndex < 0) list.SelectedIndex = 0; }
            return true;
        }
        return false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);
    protected override bool ProcessDialogKey(Keys keyData) => HandleKey(keyData) || base.ProcessDialogKey(keyData);
    private sealed record IndexRow(HelpIndexEntry Entry, string Text, bool ShowPath) { public override string ToString() => Text; }
}
