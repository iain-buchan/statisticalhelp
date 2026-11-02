using System.Security.Cryptography;
using System.Text;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public sealed partial class MainForm
{
    private ToolStripMenuItem? undoSearchReplaceMenu;

    private void UpdateSearchReplaceCommands()
    {
        if (undoSearchReplaceMenu != null) undoSearchReplaceMenu.Enabled = project?.CanUndoSearchReplace == true && !busy && !editorDialogOpen;
    }

    private async Task ShowSearchReplaceAsync()
    {
        if (project == null) return;
        await RequireEquationFinishedAsync();
        // A batch writes saved pages. Resolve the current draft through the
        // ordinary Save/Discard/Cancel prompt before taking its search snapshot.
        if (!await ConfirmLeaveAsync()) return;
        using var dialog = new SearchReplaceDialog(CurrentProject, RefreshAfterSearchReplaceAsync) { SuppressDialogs = SuppressDialogs };
        dialog.ShowDialog(this);
        UpdateSearchReplaceCommands();
        if (dialog.RequestedMatch is { } match) await OpenSearchMatchAsync(match);
    }

    private async Task UndoSearchReplaceAsync()
    {
        if (project == null || !await ConfirmLeaveAsync()) return;
        var restored = await Task.Run(CurrentProject.UndoLastSearchReplace);
        await RefreshAfterSearchReplaceAsync(restored);
        status.Text = $"Restored {restored.ChangedPages} pages from the last replacement batch";
        UpdateSearchReplaceCommands();
    }

    private async Task RefreshAfterSearchReplaceAsync(HelpReplaceResult result)
    {
        if (result.ChangedPages == 0) return;
        MarkSavedSourceChanged();
        try
        {
            await ReloadProjectLists();
            if (currentPath is { } current && result.ChangedTopicPaths.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                string reference = current + (string.IsNullOrEmpty(currentTopicFragment) ? "" : "#" + currentTopicFragment);
                await OpenTopicAsync(reference);
            }
            else await RefreshVisibleOutputPreviewsAsync();
            UpdateSearchReplaceCommands();
            status.Text = $"Updated {result.ChangedPages} help pages · rebuild saved output before publishing";
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("The source files were updated, but the editor could not refresh them. Reopen the current page from All pages. " + ex.Message, ex);
        }
    }

    private async Task OpenSearchMatchAsync(HelpSearchMatch match)
    {
        await OpenTopicAsync(match.TopicPath);
        string source = loadedDiskSource ?? throw new InvalidOperationException("The page did not finish loading.");
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        if (digest != match.SourceDigest)
            throw new InvalidOperationException("The page changed after this search. Find all again to select the current match.");
        int rawStart = Math.Clamp(match.SourceOffset, 0, source.Length);
        int rawEnd = Math.Clamp(rawStart + match.SourceLength, rawStart, source.Length);
        static int TextareaLength(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Length;
        int start = TextareaLength(source[..rawStart]);
        int end = start + TextareaLength(source[rawStart..rawEnd]);
        await editor.ExecuteScriptAsync("window.HelpEditor.setOutputView('source');(()=>{const t=document.getElementById('source-editor');t.focus();t.setSelectionRange(" + start + "," + end +
            ");t.scrollTop=Math.max(0,(t.value.slice(0," + start + ").split('\\n').length-3)*parseFloat(getComputedStyle(t).lineHeight));})()");
        await RefreshEditorStateAsync();
        status.Text = "Search match · " + match.TopicPath + " · line " + match.Line;
    }
}
