using System.Text.Json;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public sealed partial class MainForm
{
    private IPolishChatGptClient? polishClient;

    private async Task ShowPolishAsync()
    {
        if (project == null || currentPath == null) return;
        await RequireEquationFinishedAsync();
        string path = currentPath;
        var sourceProject = CurrentProject;
        string draft = JsonSerializer.Deserialize<string>(await editor.ExecuteScriptAsync("window.HelpEditor.getSource()"))
            ?? throw new InvalidOperationException("The current page could not be read.");
        var snapshot = HelpPolishSnapshot.Create(path, draft);
        status.Text = "Preparing the help pages for ChatGPT’s context…";
        var context = await Task.Run(() => HelpPolishContext.Create(sourceProject, snapshot));
        if (sourceProject != project || path != currentPath)
            throw new InvalidOperationException("The open page changed while preparing its review. Please try again.");
        polishClient ??= new PolishChatGptClient();
        using var dialog = new PolishReviewDialog(snapshot, context, polishClient, externalLinkLauncher);
        // The modal owns its own progress/Stop state; the parent remains disabled.
        UseWaitCursor = false;
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (sourceProject != project) throw new InvalidOperationException("The working copy changed. Request a new review.");
            if (dialog.Review is { } review)
                await ApplyPolishReviewAsync(snapshot, review, dialog.AcceptedChangeIds);
        }
        else status.Text = "ChatGPT review closed · the page is unchanged";
    }

    /// <summary>Apply accepted suggestions to the open draft as one normal Undo step; never saves a file.</summary>
    public async Task ApplyPolishReviewAsync(HelpPolishSnapshot snapshot, HelpPolishReview review, IEnumerable<string> acceptedIds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(review);
        if (!ReferenceEquals(review.Snapshot, snapshot))
            throw new InvalidOperationException("The review belongs to another page draft. Request a new review.");
        await RequireEquationFinishedAsync();
        if (currentPath != snapshot.TopicPath)
            throw new InvalidOperationException("The open page changed. Request a new review before applying suggestions.");
        string replacement = review.Apply(acceptedIds);
        string applied = await editor.ExecuteScriptAsync("window.HelpEditor.applyReviewedSource(" +
            JsonSerializer.Serialize(snapshot.TopicPath) + "," + JsonSerializer.Serialize(snapshot.Source) + "," +
            JsonSerializer.Serialize(replacement) + ")");
        if (applied != "true")
            throw new InvalidOperationException("The draft changed after the ChatGPT review. Request a new review; your current edits have been kept.");
        await RefreshEditorStateAsync();
        await RefreshVisibleOutputPreviewsAsync();
        status.Text = replacement == snapshot.Source ? "No ChatGPT changes applied" : "Accepted ChatGPT changes · unsaved · Undo is available";
    }
}
