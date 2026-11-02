#nullable disable
// Snapshot of StatsDirect Windows help host (MIT).
// Original: StatsDirectUI/UI/ToolWindows/ToolWindow.cs
// See provenance.json for source hashes and the deliberately limited editor integration changes.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace StatsDirect.HelpEditor.App.AppHelp;

/// <summary>The frame owns no session state. Its view is parked before closing.</summary>
internal sealed class ToolWindow : Form
{
    internal event Action CloseRequested;
    internal bool Retiring;
    protected override bool ShowWithoutActivation => true;
    internal ToolWindow(string title, Size? minimumSize = null)
    {
        Text = title;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = minimumSize ?? new Size(360, 300);
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!Retiring && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            CloseRequested?.Invoke();
        }
        base.OnFormClosing(e);
    }
}
