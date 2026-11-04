using System.Text.Json;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public sealed partial class MainForm
{
    private static readonly string[] DesignKeys = ["flare", "clean-blue", "warm-paper", "readable"];
    private readonly ToolStrip designBar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(8, 4, 8, 4), BackColor = Color.FromArgb(245, 248, 252) };
    private readonly ToolStripComboBox design = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, AccessibleName = "Help design for the selected target", ToolTipText = "Save this target’s design and update the current page preview. Rebuild to update the whole site." };
    private readonly ToolStripLabel designTarget = new();
    private readonly HashSet<HelpBuildTarget> staleDesignTargets = [];
    private bool synchronizingDesign;

    private HelpBuildTarget SelectedBuildTarget => target.SelectedIndex == 1 ? HelpBuildTarget.Public : HelpBuildTarget.Desktop;

    private void InitializeDesignControls()
    {
        designBar.Font = design.Font = design.ComboBox.Font = Font;
        design.Items.AddRange(["Flare original", "Clean blue", "Warm paper", "Readable Contents"]);
        designBar.Items.AddRange([new ToolStripLabel("Design:"), design, designTarget]);
        design.ComboBox.HandleCreated += (_, _) => SizeDesignControl();
        design.ComboBox.FontChanged += (_, _) => SizeDesignControl();
        design.ComboBox.DpiChangedAfterParent += (_, _) => SizeDesignControl();
        designBar.DpiChangedAfterParent += (_, _) => SizeDesignControl();
        SizeDesignControl();
        design.SelectedIndexChanged += async (_, _) =>
        {
            if (synchronizingDesign || project == null || !IsEditorReady || design.SelectedIndex < 0) return;
            if (busy) { SynchronizeDesignSelection(); return; }
            string key = DesignKeys[design.SelectedIndex];
            try { await SetDesignAsync(key); }
            catch (Exception ex) { ShowError(ex); }
            finally { SynchronizeDesignSelection(); }
        };
        SynchronizeDesignSelection();
    }

    private void SizeDesignControl()
    {
        // ToolStrip controls constructed outside the designer do not receive
        // its width scaling pass. Measure the actual display font and reserve
        // DPI-scaled space for the dropdown arrow, borders and text insets.
        int textWidth = design.Items.Cast<string>().Select(name => TextRenderer.MeasureText(name, design.ComboBox.Font,
            Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width).DefaultIfEmpty(0).Max();
        int width = textWidth + (int)Math.Ceiling(36 * design.ComboBox.DeviceDpi / 96d);
        design.Width = width;
        design.DropDownWidth = width;
    }

    private void SynchronizeDesignSelection()
    {
        synchronizingDesign = true;
        try
        {
            string key = project?.GetAppearance(SelectedBuildTarget) ?? "flare";
            design.SelectedIndex = Array.IndexOf(DesignKeys, key);
            designTarget.Text = target.SelectedIndex == 1 ? "for Website" : "for StatsDirect app";
        }
        finally { synchronizingDesign = false; }
    }

    private async Task ApplyPreviewDesignAsync(HelpBuildTarget selected)
    {
        string key = CurrentProject.GetAppearance(selected);
        string accepted = await editor.ExecuteScriptAsync("window.HelpEditor.setAppearance(" + JsonSerializer.Serialize(key) + ")");
        if (accepted != "true") throw new InvalidOperationException(DialogNotice);
    }

    /// <summary>Save a design choice for the current target without saving or reloading the page.</summary>
    public async Task SetDesignAsync(string key)
    {
        if (project == null) throw new InvalidOperationException("No working copy is open.");
        if (!DesignKeys.Contains(key)) throw new ArgumentException("Choose Flare original, Readable Contents, Clean blue or Warm paper.", nameof(key));
        if (busy) throw new InvalidOperationException("Please wait for the current action to finish.");
        SetBusy(true);
        try
        {
            await RequireEquationFinishedAsync();
            HelpBuildTarget selected = SelectedBuildTarget;
            string previous = CurrentProject.GetAppearance(selected);
            if (key == previous) { SynchronizeDesignSelection(); return; }
            // The renderer changes only preview furniture. Its source, selection
            // and history are deliberately never serialized or reloaded here.
            string accepted = await editor.ExecuteScriptAsync("window.HelpEditor.setAppearance(" + JsonSerializer.Serialize(key) + ")");
            if (accepted != "true") throw new InvalidOperationException(DialogNotice);
            try { CurrentProject.SetAppearance(selected, key); }
            catch
            {
                await editor.ExecuteScriptAsync("window.HelpEditor.setAppearance(" + JsonSerializer.Serialize(previous) + ")");
                throw;
            }
            staleDesignTargets.Add(selected);
            MarkGitStatusStale();
            SynchronizeDesignSelection();
            UpdateWorkspaceCaption();
            if (workspaceView == "public" && selected == HelpBuildTarget.Public || IsAppPreviewVisible && selected == HelpBuildTarget.Desktop)
                await RenderOutputPreviewAsync(selected, activate: false);
            status.Text = "Design saved for " + (selected == HelpBuildTarget.Public ? "Website" : "StatsDirect app") + " · current page updated · rebuild the whole site to apply";
        }
        finally { SetBusy(false); }
    }
}
