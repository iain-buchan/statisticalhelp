using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public sealed partial class MainForm
{
    private string? mappedProjectContent;
    private TaskCompletionSource<bool>? projectResourceReady;

    private bool MapProjectResources(HelpProject opened)
    {
        string content = Path.Combine(opened.RootPath, "Content");
        if (string.Equals(mappedProjectContent, content, StringComparison.OrdinalIgnoreCase)) return false;
        editor.CoreWebView2.SetVirtualHostNameToFolderMapping("help-project.invalid", content, CoreWebView2HostResourceAccessKind.Allow);
        mappedProjectContent = content;
        return true;
    }

    private void MapInitialProjectResources()
    {
        // WebView2 captures virtual-host mappings in each document's resource
        // loader. Source artwork must be mapped before the editor first loads.
        try { MapProjectResources(HelpProject.Open(initialProject)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            // OpenProjectAsync reports the invalid copy after the editor loads,
            // leaving File > Open working copy available for recovery.
        }
    }

    private async Task EnsureProjectResourcesAsync(HelpProject opened)
    {
        if (!MapProjectResources(opened)) return;
        int selectedTarget = target.SelectedIndex;
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        projectResourceReady = ready;
        try
        {
            // A different, already validated working copy needs a fresh loader.
            // This never runs for ordinary page/view changes within that copy.
            editor.CoreWebView2.Reload();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(45));
            await editor.ExecuteScriptAsync("window.HelpEditor.setUnifiedViews(true)");
            await editor.ExecuteScriptAsync("window.HelpEditor.setBuildTarget(" + JsonSerializer.Serialize(selectedTarget == 1 ? "Public" : "Desktop") + ")");
            SetTargetSilently(selectedTarget);
        }
        catch
        {
            // Do not treat a failed loader refresh as a ready mapping on retry.
            mappedProjectContent = null;
            throw;
        }
        finally { if (projectResourceReady == ready) projectResourceReady = null; }
    }
}
