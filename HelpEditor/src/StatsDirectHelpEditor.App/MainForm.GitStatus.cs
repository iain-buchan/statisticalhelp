using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public sealed partial class MainForm
{
    private readonly ToolStripStatusLabel gitBadge = new("Git: not checked")
    {
        IsLink = true,
        AccessibleName = "GitHub synchronization status",
        BorderSides = ToolStripStatusLabelBorderSides.Left,
        Padding = new Padding(8, 0, 4, 0)
    };
    private GitStatus? lastGitStatus;
    private bool gitStatusStale;
    private string? gitStatusError;

    private void ResetGitStatus()
    {
        lastGitStatus = null;
        gitStatusStale = false;
        gitStatusError = null;
        UpdateGitStatusBadge();
    }

    private void MarkGitStatusStale()
    {
        gitStatusStale = true;
        UpdateGitStatusBadge();
    }

    private void ShowGitStatusFailure(string message)
    {
        gitStatusError = message;
        UpdateGitStatusBadge();
    }

    private void UpdateGitStatusBadge()
    {
        var state = lastGitStatus;
        gitBadge.Text = gitStatusError != null ? "Git: status unavailable"
            : state == null ? "Git: not checked"
            : !state.IsRepository ? "Git: standalone"
            : gitStatusStale ? "Git: refresh status"
            : state.Changes.Count > 0 ? $"Git: {state.SourceChangeCount} source / {state.OtherChangeCount} other"
            : state.HasComparison ? $"Git: {state.Ahead} ahead / {state.Behind} behind (cached)"
            : "Git: saved files clean · no comparison";
        string tooltip = "GitHub > Status / review changes checks local files. Fetch checks the remote.";
        if (state != null)
            tooltip += $"\nBranch: {(state.IsRepository ? state.Branch : "Standalone source copy")}\nSaved changes: {state.SourceChangeCount} source / {state.OtherChangeCount} other" +
                $"\nComparison: {GitComparisonText(state)}\nLast successful fetch: {FetchTimeText(state)}";
        if (dirty) tooltip += "\nThe open page has an unsaved draft; it is not included in Git changes until saved.";
        if (gitStatusStale) tooltip += "\nSaved source changed since the last local status check.";
        if (gitStatusError != null) tooltip += "\nStatus could not be refreshed. Any earlier comparison is cached.";
        gitBadge.ToolTipText = tooltip;
    }

    private static string FetchTimeText(GitStatus state) => state.LastSuccessfulFetchUtc is { } fetched
        ? fetched.ToLocalTime().ToString("g") : "Not recorded — remote state may be newer";

    private string GitStatusText(GitStatus state)
    {
        string text = !state.IsRepository
            ? "Not connected to GitHub.\r\n\r\nThis is a standalone working copy. Use GitHub > Clone project to create a separate GitHub clone on a new working branch. The current source is not attached, imported or uploaded automatically.\r\nExpected project: https://github.com/iain-buchan/statisticalhelp"
            : $"Working copy: {CurrentProject.RootPath}\r\nBranch: {state.Branch}\r\nRemote: {DisplayRemote(state.Remote)}\r\nTracking branch: {state.TrackingBranch ?? "None configured"}\r\nComparison: {GitComparisonText(state)}\r\nSaved changes: {state.SourceChangeCount} source / {state.OtherChangeCount} other";
        text += $"\r\nLocal status checked: {state.ObservedAtUtc.ToLocalTime():g}\r\nLast successful fetch: {FetchTimeText(state)}\r\nNo network request was made.\r\n\r\n";
        text += dirty ? "The open page has an unsaved draft. It is not included in Git file changes or a commit until saved.\r\n\r\n" : "";
        if (gitStatusStale) text += "Saved source changed since the last local check. Refresh status before relying on this list.\r\n\r\n";
        text += "Saved file changes:\r\n" + (state.Changes.Count == 0 ? "No changes.\r\n"
            : string.Join("\r\n", state.Changes.Select(c => c.Status.PadRight(4) + (c.IsSource ? "Source  " : "Other   ") +
                (c.OriginalPath == null ? c.Path : c.OriginalPath + " → " + c.Path).Replace("\r", "\\r").Replace("\n", "\\n")))) +
            "\r\n\r\nRebuild website and commit regenerates the Public website, including Contents, search, artwork and equations, into Docs. It commits saved Content, Project, RCode, StatsDirect.flprj and Docs together. Recovery copies and unrelated files are excluded. Push refuses stale/uncommitted source or website changes and main/master/default branches. After review, merging to master triggers the existing GitHub Pages deployment.";
        return text;
    }
}
