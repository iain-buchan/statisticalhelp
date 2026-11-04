using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public sealed partial class MainForm
{
    public DesktopHelpPublishResult? LastDesktopHelpPublication { get; private set; }
    public HelpSourceComparisonReport? LastSourceComparison { get; private set; }

    private async Task ShowSourceComparisonAsync()
    {
        if (project == null) throw new InvalidOperationException("Open a help working copy first.");
        using var picker = new FolderBrowserDialog
        {
            Description = "Choose another help project or Content folder. Comparison reads saved files only; neither source is changed.",
            UseDescriptionForTitle = true,
            InitialDirectory = project.RootPath
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var report = await CompareSavedSourcesCoreAsync(picker.SelectedPath, CancellationToken.None);
        using var view = new Form
        {
            Text = "Saved help source comparison",
            Font = Font,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96, 96),
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(1100, 650),
            MinimizeBox = false
        };
        var summary = new Label
        {
            Dock = DockStyle.Top, Height = 95, Padding = new Padding(10),
            Text = $"Other source: {report.LeftRoot}\nCurrent source: {report.RightRoot}\n" +
                $"{report.EquivalentTopicCount} equivalent topics · {report.Differences.Count} different/missing topics · " +
                $"Contents: {(report.ContentsEquivalent is null ? "not compared" : report.ContentsEquivalent.Value ? "equivalent" : "different")}\n" +
                "Authoring namespaces are treated as equivalent. Unsaved drafts are excluded. No files are synchronised."
        };
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            AccessibleName = "Saved source differences"
        };
        grid.Columns.Add("page", "Topic / Contents");
        grid.Columns.Add("changes", "Differences");
        foreach (var item in report.Topics.Concat(report.Contents).Where(item => !item.NamespaceEquivalent))
        {
            var changes = new List<string>();
            if (!item.ExistsOnLeft) changes.Add("Only in current source");
            if (!item.ExistsOnRight) changes.Add("Only in other source");
            if (item.TextChanged) changes.Add("Text");
            if (item.UrlsChanged) changes.Add("Links");
            if (item.EquationsChanged) changes.Add("Equations");
            if (item.CodeChanged) changes.Add("R / code");
            if (item.OtherMarkupChanged) changes.Add("Other markup or metadata");
            grid.Rows.Add(item.Path, string.Join(", ", changes));
        }
        var close = new Button { Text = "Close", Dock = DockStyle.Bottom, Height = 38, DialogResult = DialogResult.OK };
        view.Controls.Add(grid);
        view.Controls.Add(summary);
        view.Controls.Add(close);
        view.AcceptButton = close;
        view.ShowDialog(this);
    }

    public async Task<HelpSourceComparisonReport> CompareSavedSourcesAsync(string otherProjectOrContent,
        CancellationToken cancellationToken = default)
    {
        if (busy) throw new InvalidOperationException("Please wait for the current action to finish.");
        SetBusy(true);
        try { return await CompareSavedSourcesCoreAsync(otherProjectOrContent, cancellationToken); }
        finally { SetBusy(false); }
    }

    private async Task<HelpSourceComparisonReport> CompareSavedSourcesCoreAsync(string other,
        CancellationToken cancellationToken)
    {
        var source = CurrentProject;
        var report = await Task.Run(() => HelpSourceComparer.Compare(other, source.RootPath, cancellationToken), cancellationToken);
        LastSourceComparison = report;
        status.Text = $"Compared saved source · {report.EquivalentTopicCount} equivalent topics · {report.Differences.Count} different/missing · no files changed";
        return report;
    }

    private async Task ShowDesktopHelpPublishAsync()
    {
        await RequireSavedHelpForDeliveryAsync();
        using var picker = new FolderBrowserDialog
        {
            Description = "Choose the StatsDirect source repository that should receive desktop help for its next build.",
            UseDescriptionForTitle = true,
            InitialDirectory = AuthoringPreferences.ReadStatsDirectSourceRoot(artifactRoot)
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var inspected = await Task.Run(() => DesktopHelpPublisher.InspectDestination(picker.SelectedPath));
        if (inspected.Ownership == DesktopHelpOwnership.Unrecognised)
            throw new InvalidDataException(string.Join(Environment.NewLine, inspected.Problems));
        bool adopt = inspected.Ownership == DesktopHelpOwnership.FlareNeedsAdoption;
        string review = $"Publish saved desktop help to:\n{inspected.DestinationDirectory}\n\n" +
            $"Current output: {inspected.Ownership} · {inspected.FileCount} files\n" +
            $"Inspected snapshot: {inspected.Sha256}\n\n" +
            (adopt ? "This explicitly adopts and replaces the inspected Flare output.\n" : "The verified generated help will replace the inspected output.\n") +
            "The StatsDirect application and installers are not rebuilt by this command. Continue?";
        if (MessageBox.Show(this, review, "Publish desktop help", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        await PublishDesktopHelpCoreAsync(picker.SelectedPath, new DesktopHelpPublishOptions
        {
            ExpectedDestinationSha256 = inspected.Sha256,
            AdoptExistingFlareOutput = adopt,
            AdoptionReason = adopt ? "Author confirmed the inspected Flare output in Help Editor." : null
        }, CancellationToken.None);
    }

    public async Task<DesktopHelpPublishResult> PublishDesktopHelpAsync(string statsDirectSource,
        DesktopHelpPublishOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (busy) throw new InvalidOperationException("Please wait for the current action to finish.");
        SetBusy(true);
        try { return await PublishDesktopHelpCoreAsync(statsDirectSource, options, cancellationToken); }
        finally { SetBusy(false); }
    }

    private async Task<DesktopHelpPublishResult> PublishDesktopHelpCoreAsync(string destination,
        DesktopHelpPublishOptions? options, CancellationToken cancellationToken)
    {
        await RequireSavedHelpForDeliveryAsync();
        var source = CurrentProject;
        status.Text = "Building and verifying saved desktop help…";
        var result = await Task.Run(() => DesktopHelpPublisher.Publish(source, destination,
            Path.Combine(AppContext.BaseDirectory, "Web"), options, cancellationToken), cancellationToken);
        LastDesktopHelpPublication = result;
        string remembered = "";
        try { AuthoringPreferences.SaveStatsDirectSourceRoot(artifactRoot, destination); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { remembered = " · destination could not be remembered"; }
        status.Text = $"Published verified desktop help · {result.TopicCount} pages · {result.SearchDocumentCount} search entries · {result.ContextCount} contexts" +
            (result.ConsumerGuardInstalled ? " · build guard installed" : " · see desktop publishing instructions for the optional build guard") + remembered;
        return result;
    }

    private async Task RequireSavedHelpForDeliveryAsync()
    {
        var source = CurrentProject;
        foreach (var form in Application.OpenForms.OfType<MainForm>().ToArray())
        {
            if (form.project == null || !Path.GetFullPath(form.project.RootPath).Equals(
                Path.GetFullPath(source.RootPath), StringComparison.OrdinalIgnoreCase)) continue;
            await form.RefreshEditorStateAsync();
            if (form.editorDialogOpen || form.dirty)
                throw new InvalidOperationException("Finish and save the open page edits before publishing desktop help. Your drafts remain open.");
        }
    }
}
