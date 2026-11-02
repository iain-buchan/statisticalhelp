#nullable disable
// Snapshot of StatsDirect Windows help host (MIT).
// Original: StatsDirectUI/UI/ToolWindows/ToolDockHost.cs
// See provenance.json for source hashes and the deliberately limited editor integration changes.
using System;
using System.Linq;
using System.Windows.Forms;

namespace StatsDirect.HelpEditor.App.AppHelp;

/// <summary>Reserves workspace space without making a tool an MDI document.</summary>
internal sealed class ToolDockHost : IDisposable
{
    private readonly Form host;
    private readonly DockStyle edge;
    private readonly int minimumLength;
    internal Panel Panel { get; }
    internal Splitter Divider { get; }
    private int preferredLength;
    private bool sizing;

    internal ToolDockHost(Form host, string name, DockStyle edge, int length, int minimumLength = 240)
    {
        this.host = host;
        this.edge = edge;
        this.minimumLength = minimumLength;
        float scale = host.DeviceDpi / 96f;
        preferredLength = (int)(length * scale);
        Panel = new Panel { Dock = edge, Visible = false, AccessibleName = name + " pane", Tag = this };
        Divider = new Splitter { Dock = edge, Visible = false, AccessibleName = "Resize " + name.ToLowerInvariant() + " pane", TabStop = false };
        Divider.Width = Divider.Height = Math.Max(5, (int)(5 * scale));
        host.SuspendLayout();
        host.Controls.Add(Panel);
        host.Controls.Add(Divider);
        Arrange();
        host.ResumeLayout(true);
        host.ClientSizeChanged += HostResized;
        Divider.SplitterMoved += SplitterMoved;
    }

    // Docking is in reverse z-order: ribbon, side tools, bottom tools, workspace.
    // Reassert the order after handles are created or floating views return.
    private void Arrange()
    {
        Control workspace = host.Controls.Cast<Control>().FirstOrDefault(c => c is MdiClient)
            ?? host.Controls.Cast<Control>().FirstOrDefault(c => c.Dock == DockStyle.Fill);
        var docks = host.Controls.Cast<Control>().Where(c => c.Tag is ToolDockHost)
            .Select(c => (ToolDockHost)c.Tag).OrderBy(d => d.edge == DockStyle.Bottom ? 0 : 1).ToArray();
        workspace?.BringToFront();
        int index = workspace == null ? 0 : 1;
        foreach (var dock in docks)
        {
            host.Controls.SetChildIndex(dock.Divider, index++);
            host.Controls.SetChildIndex(dock.Panel, index++);
        }
    }
    internal void SetVisible(bool visible)
    {
        host.SuspendLayout();
        if (visible) FitLength();
        Panel.Visible = Divider.Visible = visible;
        if (visible) Arrange();
        host.ResumeLayout(true);
    }
    private void HostResized(object sender, EventArgs e) { if (Panel.Visible) FitLength(); }
    private void SplitterMoved(object sender, SplitterEventArgs e)
    {
        if (!sizing) preferredLength = edge == DockStyle.Bottom ? Panel.Height : Panel.Width;
    }
    private void FitLength()
    {
        sizing = true;
        try
        {
            int available = edge == DockStyle.Bottom
                ? host.ClientSize.Height - host.Controls.Cast<Control>().Where(c => c.Visible && c.Dock == DockStyle.Top).Sum(c => c.Height) - Divider.Height
                : host.ClientSize.Width - Divider.Width;
            available = Math.Max(0, available);
            // The drag limits must agree with the fitted size, including in a
            // small window at high DPI. Keep room for both tool and workspace.
            int minimum = Math.Min((int)(minimumLength * host.DeviceDpi / 96f), available / 2);
            Divider.MinSize = minimum;
            Divider.MinExtra = Math.Min((int)(240 * host.DeviceDpi / 96f), available / 2);
            int maximum = available - Divider.MinExtra;
            int length = Math.Clamp(preferredLength, minimum, maximum);
            if (edge == DockStyle.Bottom) Panel.Height = length;
            else Panel.Width = length;
        }
        finally { sizing = false; }
    }
    public void Dispose()
    {
        host.ClientSizeChanged -= HostResized;
        Divider.SplitterMoved -= SplitterMoved;
        Divider.Dispose();
        Panel.Dispose();
    }
}
