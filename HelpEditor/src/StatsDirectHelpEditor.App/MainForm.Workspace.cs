using System.Text.Json;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public sealed partial class MainForm
{
    private readonly Panel workspaceBody = new() { Dock = DockStyle.Fill, BackColor = SystemColors.Window };
    private readonly Label workspaceCaption = new WorkspaceCaptionLabel { Dock = DockStyle.Top, Height = 53, Padding = new Padding(10, 5, 10, 3), BackColor = Color.FromArgb(245, 248, 252), ForeColor = Color.FromArgb(42, 58, 75), AutoEllipsis = true };
    private readonly Dictionary<HelpBuildTarget, int> builtRevisions = [];
    private string workspaceView = "edit";
    private bool synchronizingTarget;
    private bool changingWorkspace;
    private bool sizingMetadataLabels;
    private int sourceRevision;
    private readonly ToolTip metadataToolTip = new() { AutoPopDelay = 15000 };

    /// <summary>The active editing or current-page preview workspace.</summary>
    public string CurrentWorkspace => workspaceView;

    private void InitializeWorkspace(Control parent)
    {
        // One row of page-view tabs lives in the editor. Its HWND and parent
        // remain stable while the independent app preview docks or floats.
        HandleCreated += (_, _) => SizeMinimumWindow();
        DpiChanged += (_, _) => SizeMinimumWindow();
        SizeMinimumWindow();
        workspaceBody.Controls.Add(editor);
        InitializeDesignControls();
        parent.Controls.Add(workspaceBody);
        parent.Controls.Add(workspaceCaption);
        parent.Controls.Add(designBar);
        foreach (var label in new[] { workspaceCaption, copyLabel })
        {
            label.AutoSize = false;
            label.TextChanged += (_, _) => SizeMetadataLabels();
            label.FontChanged += (_, _) => SizeMetadataLabels();
            label.SizeChanged += (_, _) => SizeMetadataLabels();
            label.DpiChangedAfterParent += (_, _) => SizeMetadataLabels();
        }
        copyLabel.AutoEllipsis = true;
        Disposed += (_, _) => { metadataToolTip.Dispose(); appHelpPreview?.Dispose(); polishClient?.Dispose(); };
        UpdateWorkspaceCaption();
    }

    private void SizeMinimumWindow()
    {
        double scale = DeviceDpi / 96d;
        MinimumSize = new Size((int)Math.Ceiling(1000 * scale), (int)Math.Ceiling(620 * scale));
    }

    private void SizeMetadataLabels()
    {
        if (sizingMetadataLabels) return;
        sizingMetadataLabels = true;
        try
        {
            // These controls are constructed outside a designer's scaling pass.
            // Measure against the actual inherited DPI-scaled font and width so
            // the second caption line and working-copy path remain readable.
            workspaceCaption.Height = workspaceCaption.Font.Height * 2 + workspaceCaption.Padding.Vertical + 4;
            // Keep long absolute paths on one readable line; the full path is
            // also available as a tooltip and in GitHub > Status / review.
            copyLabel.Height = copyLabel.Font.Height + copyLabel.Padding.Vertical + 4;
            metadataToolTip.SetToolTip(copyLabel, copyLabel.Text);
            metadataToolTip.SetToolTip(workspaceCaption, currentPath ?? workspaceCaption.Text);
        }
        finally { sizingMetadataLabels = false; }
    }

    public async Task SwitchWorkspaceAsync(string view)
    {
        if (view is not ("edit" or "desktop" or "public")) throw new ArgumentException("Unknown help workspace.", nameof(view));
        if (busy) throw new InvalidOperationException("Please wait for the current action to finish.");
        if (changingWorkspace) return;
        changingWorkspace = true;
        SetBusy(true);
        try
        {
            await browserReady.Task;
            await RequireEquationFinishedAsync();
            if (view is "desktop" or "public")
                await RenderOutputPreviewAsync(view == "public" ? HelpBuildTarget.Public : HelpBuildTarget.Desktop, activate: true);
            else
            {
                string accepted = await editor.ExecuteScriptAsync("window.HelpEditor.setWorkspaceView('edit')");
                if (accepted != "true") throw new InvalidDataException("Apply or Cancel the open editor dialog before changing view.");
            }
            workspaceView = view;
            editor.Visible = true;
            editor.BringToFront();
            if (view is "desktop" or "public") SetTargetSilently(view == "public" ? 1 : 0);
            await RefreshEditorStateAsync();
            UpdateWorkspaceCaption();
        }
        finally { changingWorkspace = false; SetBusy(false); }
    }

    private void ApplyWorkspaceState(JsonElement snapshot)
    {
        if (snapshot.TryGetProperty("workspaceView", out var view) && view.GetString() is string current && current is "edit" or "desktop" or "public")
        {
            workspaceView = current;
        }
        if ((snapshot.TryGetProperty("target", out var buildTarget) || snapshot.TryGetProperty("buildTarget", out buildTarget)) && buildTarget.GetString() is string selected)
            SetTargetSilently(selected == "Public" ? 1 : 0);
        UpdateWorkspaceCaption();
    }

    private void SetTargetSilently(int selected)
    {
        synchronizingTarget = true;
        try { target.SelectedIndex = selected; }
        finally { synchronizingTarget = false; }
        SynchronizeDesignSelection();
    }

    private void ResetWorkspaceProject()
    {
        sourceRevision = 0;
        builtRevisions.Clear();
        staleDesignTargets.Clear();
        LastBuildResult = null;
        ResetOutputPreviews();
        ResetGitStatus();
        SynchronizeDesignSelection();
    }

    private void MarkSavedSourceChanged()
    {
        sourceRevision++;
        MarkGitStatusStale();
        UpdateWorkspaceCaption();
    }

    private void UpdateWorkspaceCaption()
    {
        string buildName = target.SelectedIndex == 1 ? "Website" : "StatsDirect app";
        string page = currentPath == null ? "Choose a page" : Path.GetFileName(currentPath);
        string buffer = dirty ? "Live unsaved draft" : "Saved page";
        if (workspaceView == "edit")
        {
            workspaceCaption.Text = $"{buffer} · {page}\nBuild / F5: {buildName}" + (IsAppPreviewVisible ? " · App pane open; select App view to refresh" : "");
            return;
        }
        HelpBuildTarget selected = workspaceView == "public" ? HelpBuildTarget.Public : HelpBuildTarget.Desktop;
        string build = !builtRevisions.TryGetValue(selected, out int revision) ? "Saved output: not rebuilt in this session"
            : dirty || revision != sourceRevision || staleDesignTargets.Contains(selected) ? "Saved output is out of date" : "Saved output includes your saved edits";
        workspaceCaption.Text = $"{buffer} · {page}\n{(selected == HelpBuildTarget.Desktop ? "App pane" : "Browser view")} · {build}";
    }

    private async Task UpdateSharedFootersAsync()
    {
        object? Footer(HelpBuildTarget selected)
        {
            var shared = CurrentProject.ReadSharedFooter(selected);
            return shared == null ? null : new { html = shared.Html, baseUrl = ProjectOrigin + "/" + string.Join("/", shared.SourcePath.Split('/').Select(Uri.EscapeDataString)) };
        }
        await editor.ExecuteScriptAsync("window.HelpEditor.setSharedFooters(" + JsonSerializer.Serialize(new { Desktop = Footer(HelpBuildTarget.Desktop), Public = Footer(HelpBuildTarget.Public) }) + ")");
    }

    private static string GitComparisonText(GitStatus result) => !result.HasComparison
        ? "Unavailable — no usable cached comparison branch"
        : $"{result.Ahead} ahead / {result.Behind} behind {result.ComparisonBranch} (cached)" + (result.HasTrackingBranch ? " · tracking branch" : " · matching remote branch, no upstream");

    private sealed class WorkspaceCaptionLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            using var background = new SolidBrush(BackColor);
            e.Graphics.FillRectangle(background, ClientRectangle);
            var lines = Text.Replace("\r", "").Split('\n');
            int height = Font.Height;
            for (int i = 0; i < Math.Min(2, lines.Length); i++)
            {
                var line = new Rectangle(Padding.Left, Padding.Top + i * height, Math.Max(0, ClientSize.Width - Padding.Horizontal), height);
                TextRenderer.DrawText(e.Graphics, lines[i], Font, line, ForeColor, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
