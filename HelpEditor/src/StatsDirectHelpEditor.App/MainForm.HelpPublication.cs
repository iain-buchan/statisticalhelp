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
        string defaultTutor = Path.Combine(artifactRoot, "TutorSource", "starter-course.json");
        string selectedTutor = defaults.TutorCoursePath ?? "";
        if (string.IsNullOrWhiteSpace(selectedTutor)) selectedTutor = defaultTutor;
        defaults = defaults with { TutorCoursePath = selectedTutor };
        bool usesDefaultTutor = false;
        try { usesDefaultTutor = Path.GetFullPath(selectedTutor).Equals(Path.GetFullPath(defaultTutor), StringComparison.OrdinalIgnoreCase); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException) { }
        if (usesDefaultTutor && !File.Exists(defaultTutor)) PrepareTutorSeed();
        defaults = defaults with { PublishSharedContent = true };
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
            status.Text = "Content publication cancelled · no published outputs changed";
            return;
        }
        await PublishHelpCoreAsync(selected with { PublishSharedContent = true,
            TutorCoursePath = selected.TutorCoursePath ?? defaults.TutorCoursePath }, CancellationToken.None);
    }

    public async Task<HelpPublicationResult> PublishHelpAsync(HelpPublicationOptions options,
        CancellationToken cancellationToken = default)
    {
        if (busy) throw new InvalidOperationException("Please wait for the current action to finish.");
        SetBusy(true);
        try { return await PublishHelpCoreAsync(options, cancellationToken); }
        finally { SetBusy(false); }
    }

    public Task<HelpPublicationResult> PublishContentAsync(HelpPublicationOptions options,
        CancellationToken cancellationToken = default) =>
        PublishHelpAsync(options with { PublishSharedContent = true }, cancellationToken);

    private static void RequireSavedTutorForPublication(HelpPublicationOptions options)
    {
        if (!options.PublishSharedContent) return;
        string? path = options.TutorCoursePath;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            !Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new InvalidDataException("Choose an existing saved tutor course JSON file before publishing content.");
        string selected = Path.GetFullPath(path);
        foreach (var tutor in Application.OpenForms.OfType<TutorEditorForm>())
            if (tutor.Document.FilePath is string open &&
                Path.GetFullPath(open).Equals(selected, StringComparison.OrdinalIgnoreCase) && tutor.HasUnsavedChanges)
                throw new InvalidOperationException("Save the selected tutor course's edits before publishing content. The open tutor draft is preserved.");
    }

    private async Task<HelpPublicationResult> PublishHelpCoreAsync(HelpPublicationOptions options,
        CancellationToken cancellationToken)
    {
        await RequireSavedHelpForDeliveryAsync();
        RequireSavedTutorForPublication(options);
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
            string sharedSummary = result.Tutor == null ? "" : $"tutor {result.Tutor.LessonCount} lessons / {result.Tutor.QuestionCount} questions · ";
            if (result.SharedContentManifestSha256 is string contentHash)
                sharedSummary += "shared version " + contentHash[..Math.Min(12, contentHash.Length)] + " · ";
            status.Text = (result.Completed
                ? (options.PublishSharedContent ? "Content published · " + sharedSummary : "Help published · ") + "website ZIP saved · desktop help delivered · GitHub: " + result.PagesDeploymentState
                : "Content publication is incomplete · review the per-target receipt before using these outputs") + warning;
            return result;
        }
        catch (HelpPublicationException error)
        {
            LastHelpPublication = error.Result;
            status.Text = "Content publication did not finish · completed targets and retry evidence: " + error.Result.JournalPath;
            throw;
        }
    }
}
