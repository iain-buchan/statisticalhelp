namespace StatsDirect.HelpEditor.App;

public sealed partial class MainForm
{
    private async Task ShowDeletedPagesAsync()
    {
        if (project == null) return;
        await RequireEquationFinishedAsync();
        // Choosing a record is read-only. Resolve the current draft only after
        // Restore is pressed and this modal chooser has closed.
        using var dialog = new DeletedPagesDialog(CurrentProject.ListDeletedTopics());
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedTopicId is not string id) return;
        await RestoreDeletedPageCoreAsync(id);
    }

    /// <summary>Restore an archived page through the same draft and modal guards as navigation.</summary>
    public async Task<bool> RestoreDeletedPageAsync(string id)
    {
        if (busy) throw new InvalidOperationException("Please wait for the current action to finish.");
        SetBusy(true);
        try { return await RestoreDeletedPageCoreAsync(id); }
        finally { SetBusy(false); }
    }

    private async Task<bool> RestoreDeletedPageCoreAsync(string id)
    {
        if (project == null) throw new InvalidOperationException("No working copy is open.");
        if (!await ConfirmLeaveAsync())
        {
            if (!editorDialogOpen) status.Text = "Restore cancelled · the deleted page remains available in File > Deleted pages";
            return false;
        }
        var restored = CurrentProject.RestoreDeletedTopic(id);
        MarkSavedSourceChanged();
        try
        {
            await ReloadProjectLists();
            await OpenTopicAsync(restored.OriginalPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Restored " + restored.OriginalPath + ", but the editor could not open it. The restored file is on disk; reopen it from All pages. " + ex.Message, ex);
        }
        status.Text = "Restored " + restored.OriginalPath + " · rebuild help to update search" +
            (string.IsNullOrWhiteSpace(restored.Notice) ? "" : " · " + restored.Notice);
        if (!string.IsNullOrWhiteSpace(restored.Notice) && !SuppressDialogs)
            MessageBox.Show(this, "Restored " + restored.OriginalPath + ".\n\n" + restored.Notice, "Page restored", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }
}
