using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public delegate Task<HelpPublicationResult> HelpPublicationRunner(HelpProject project, string assets,
    HelpPublicationOptions options, CancellationToken cancellationToken, IProgress<HelpPublicationProgress>? progress);

public sealed partial class MainForm
{
    private readonly HelpPublicationRunner? helpPublicationRunner;
    private readonly Func<HelpPublicationOptions, HelpPublicationOptions?>? helpPublicationOptionsPicker;
    public HelpPublicationResult? LastHelpPublication { get; private set; }

    private async Task ShowHelpPublicationAsync()
    {
        await RequireSavedHelpForDeliveryAsync();
        var defaults = AuthoringPreferences.ReadHelpPublication(artifactRoot);
        if (string.IsNullOrWhiteSpace(defaults.AuthorName) || string.IsNullOrWhiteSpace(defaults.AuthorEmail))
        {
            try
            {
                var identity = await GitSyncService.ReadDefaultAuthorIdentityAsync();
                defaults = defaults with
                {
                    AuthorName = string.IsNullOrWhiteSpace(defaults.AuthorName) ? identity.Name : defaults.AuthorName,
                    AuthorEmail = string.IsNullOrWhiteSpace(defaults.AuthorEmail) ? identity.Email : defaults.AuthorEmail
                };
            }
            catch (Exception failure) when (failure is IOException or InvalidOperationException) { }
        }
        HelpPublicationOptions? selected;
        if (helpPublicationOptionsPicker != null) selected = helpPublicationOptionsPicker(defaults);
        else
        {
            using var dialog = new HelpPublicationDialog(defaults) { Font = Font };
            selected = dialog.ShowDialog(this) == DialogResult.OK ? dialog.Options : null;
        }
        if (selected == null)
        {
            status.Text = "Help publication cancelled · no outputs changed";
            return;
        }
        await PublishHelpCoreAsync(selected, CancellationToken.None);
    }

    public async Task<HelpPublicationResult> PublishHelpAsync(HelpPublicationOptions options,
        CancellationToken cancellationToken = default)
    {
        if (busy) throw new InvalidOperationException("Please wait for the current action to finish.");
        SetBusy(true);
        try { return await PublishHelpCoreAsync(options, cancellationToken); }
        finally { SetBusy(false); }
    }

    private async Task<HelpPublicationResult> PublishHelpCoreAsync(HelpPublicationOptions options,
        CancellationToken cancellationToken)
    {
        await RequireSavedHelpForDeliveryAsync();
        var source = CurrentProject;
        string assets = Path.Combine(AppContext.BaseDirectory, "Web");
        var progress = new Progress<HelpPublicationProgress>(update => status.Text = update.Message);
        try
        {
            status.Text = "Preparing a consistent saved-source publication…";
            var result = await Task.Run(() => helpPublicationRunner != null
                ? helpPublicationRunner(source, assets, options, cancellationToken, progress)
                : HelpPublicationPublisher.PublishAsync(source, assets, options, cancellationToken, progress), cancellationToken);
            LastHelpPublication = result;
            string warning = "";
            if (result.Completed)
            {
                try { AuthoringPreferences.SaveHelpPublication(artifactRoot, options); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                { warning = " · settings could not be remembered"; }
            }
            status.Text = (result.Completed
                ? "Help published · website ZIP saved · desktop help delivered · GitHub: " + result.PagesDeploymentState
                : "Help publication is incomplete · review the per-target receipt before using these outputs") + warning;
            return result;
        }
        catch (HelpPublicationException error)
        {
            LastHelpPublication = error.Result;
            status.Text = "Help publication did not finish · completed targets and retry evidence: " + error.Result.JournalPath;
            throw;
        }
    }
}
