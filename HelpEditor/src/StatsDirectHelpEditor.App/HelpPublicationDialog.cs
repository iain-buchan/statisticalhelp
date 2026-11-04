using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

internal sealed class HelpPublicationDialog : Form
{
    private readonly TextBox zip = new() { Dock = DockStyle.Fill };
    private readonly TextBox repository = new() { Dock = DockStyle.Fill };
    private readonly TextBox branch = new() { Dock = DockStyle.Fill };
    private readonly TextBox desktop = new() { Dock = DockStyle.Fill };
    private readonly TextBox authorName = new() { Dock = DockStyle.Fill };
    private readonly TextBox authorEmail = new() { Dock = DockStyle.Fill };
    private readonly CheckBox allowDefault = new() { AutoSize = true, Text = "Allow publishing to the repository's default branch (may deploy its website)" };
    private readonly CheckBox adoptFlare = new() { AutoSize = true, Text = "Replace the recognised Flare desktop output shown below" };
    private readonly Label inspection = new() { Dock = DockStyle.Fill, AutoSize = true, AutoEllipsis = false };
    private readonly Label error = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.DarkRed };
    private readonly Button publish = new() { Text = "Publish help", AutoSize = true, Enabled = false };
    private readonly HelpPublicationOptions defaults;
    private readonly TableLayoutPanel layout;
    private readonly FlowLayoutPanel buttons;
    private DesktopHelpDestination? inspected;
    private int inspectionGeneration;
    public HelpPublicationOptions? Options { get; private set; }

    internal HelpPublicationDialog(HelpPublicationOptions defaults)
    {
        this.defaults = defaults;
        Text = "Publish help";
        Font = new Font("Arial", 11);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(900, 760);
        MinimumSize = new Size(850, 760);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(14), ColumnCount = 3, RowCount = 11 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        var introduction = new Label
        {
            Dock = DockStyle.Fill, AutoSize = true,
            Text = "One saved-source snapshot will create the website ZIP, commit and push the GitHub website, and deliver desktop help for the next StatsDirect build.\n\nIt does not upload to statsdirect.com or build the StatsDirect application or installers."
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(introduction, 0, 0); layout.SetColumnSpan(introduction, 3);
        AddField(layout, 1, "Website ZIP", zip, "Choose…", ChooseZip);
        AddField(layout, 2, "GitHub repository", repository);
        AddField(layout, 3, "GitHub branch", branch);
        AddField(layout, 4, "Commit author", authorName);
        AddField(layout, 5, "Author email", authorEmail);
        AddField(layout, 6, "StatsDirect source", desktop, "Choose…", ChooseDesktop);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(allowDefault, 0, 7); layout.SetColumnSpan(allowDefault, 3);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(adoptFlare, 0, 8); layout.SetColumnSpan(adoptFlare, 3);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(inspection, 0, 9); layout.SetColumnSpan(inspection, 3);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(error, 0, 10); layout.SetColumnSpan(error, 3);
        buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(cancel); buttons.Controls.Add(publish);
        var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        content.Controls.Add(layout);
        Controls.Add(content);
        Controls.Add(buttons);
        CancelButton = cancel;
        AcceptButton = publish;
        zip.Text = defaults.WebsiteZipPath;
        repository.Text = defaults.GitHubRepositoryUrl;
        branch.Text = defaults.GitHubBranch;
        desktop.Text = defaults.StatsDirectSource;
        authorName.Text = defaults.AuthorName;
        authorEmail.Text = defaults.AuthorEmail;
        allowDefault.Checked = defaults.AllowDefaultBranchPublication;
        adoptFlare.Visible = false;
        desktop.TextChanged += async (_, _) => await InspectDesktopAsync();
        foreach (var field in new[] { zip, repository, branch, authorName, authorEmail }) field.TextChanged += (_, _) => ValidateChoices();
        allowDefault.CheckedChanged += (_, _) => ValidateChoices();
        adoptFlare.CheckedChanged += (_, _) => ValidateChoices();
        Shown += async (_, _) => await InspectDesktopAsync();
        publish.Click += (_, _) =>
        {
            if (!ValidateChoices() || inspected == null) return;
            Options = new HelpPublicationOptions
            {
                WebsiteZipPath = Path.GetFullPath(zip.Text.Trim()),
                GitHubRepositoryUrl = repository.Text.Trim(), GitHubBranch = branch.Text.Trim(),
                AuthorName = authorName.Text.Trim(), AuthorEmail = authorEmail.Text.Trim(),
                StatsDirectSource = Path.GetFullPath(desktop.Text.Trim()),
                ExpectedDesktopSha256 = inspected.Sha256,
                AdoptExistingFlareOutput = adoptFlare.Visible && adoptFlare.Checked,
                AdoptionReason = adoptFlare.Visible && adoptFlare.Checked ? "Author adopted the inspected Flare output in Publish help." : null,
                AllowDefaultBranchPublication = allowDefault.Checked,
                PushToGitHub = true, JournalDirectory = defaults.JournalDirectory
            };
            DialogResult = DialogResult.OK;
        };
    }

    private static void AddField(TableLayoutPanel layout, int row, string label, TextBox field,
        string? buttonText = null, EventHandler? action = null)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        field.Margin = new Padding(3, 6, 3, 6);
        layout.Controls.Add(field, 1, row);
        if (buttonText == null) layout.SetColumnSpan(field, 2);
        else
        {
            var button = new Button { Text = buttonText, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(4) };
            button.Click += action;
            layout.Controls.Add(button, 2, row);
        }
    }

    protected override void OnLoad(EventArgs args)
    {
        base.OnLoad(args);
        int Scale(int value) => (int)Math.Ceiling(value * DeviceDpi / 96d);
        var area = Screen.FromControl(Owner ?? this).WorkingArea;
        int width = Math.Max(Scale(480), area.Width - Scale(24));
        int height = Math.Max(Scale(360), area.Height - Scale(24));
        MinimumSize = new Size(Math.Min(Scale(800), width), Math.Min(Scale(580), height));
        Size = new Size(Math.Min(Scale(940), width), Math.Min(Scale(720), height));
        layout.Padding = new Padding(Scale(14));
        layout.ColumnStyles[0].Width = Scale(185);
        layout.ColumnStyles[2].Width = Scale(110);
        foreach (Control control in layout.Controls)
        {
            control.Margin = new Padding(Scale(4), Scale(7), Scale(4), Scale(7));
            if (control is TextBox) control.Margin = new Padding(Scale(4), Scale(8), Scale(4), Scale(8));
        }
        buttons.Padding = new Padding(Scale(14), Scale(8), Scale(14), Scale(12));
        foreach (Button button in buttons.Controls)
        {
            button.MinimumSize = new Size(Scale(110), Scale(36));
            button.Padding = new Padding(Scale(10), Scale(4), Scale(10), Scale(4));
            button.Margin = new Padding(Scale(5));
        }
        CenterToParent();
        Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    private void ChooseZip(object? sender, EventArgs args)
    {
        using var picker = new SaveFileDialog { Filter = "Website ZIP (*.zip)|*.zip", DefaultExt = "zip", AddExtension = true, OverwritePrompt = true };
        try { picker.InitialDirectory = Path.GetDirectoryName(zip.Text); picker.FileName = Path.GetFileName(zip.Text); }
        catch (ArgumentException) { }
        if (picker.ShowDialog(this) == DialogResult.OK) zip.Text = picker.FileName;
    }

    private void ChooseDesktop(object? sender, EventArgs args)
    {
        using var picker = new FolderBrowserDialog { Description = "Choose the StatsDirect source repository for the next build", UseDescriptionForTitle = true };
        if (Directory.Exists(desktop.Text)) picker.InitialDirectory = desktop.Text;
        if (picker.ShowDialog(this) == DialogResult.OK) desktop.Text = picker.SelectedPath;
    }

    private async Task InspectDesktopAsync()
    {
        int generation = ++inspectionGeneration;
        string selected = desktop.Text.Trim();
        inspected = null; publish.Enabled = false;
        adoptFlare.Visible = false; adoptFlare.Checked = false;
        inspection.Text = "Inspecting desktop-help destination…";
        try
        {
            if (!Path.IsPathFullyQualified(selected)) throw new ArgumentException("Choose the complete StatsDirect source path.");
            var result = await Task.Run(() => DesktopHelpPublisher.InspectDestination(selected));
            if (IsDisposed || generation != inspectionGeneration) return;
            if (result.Ownership == DesktopHelpOwnership.Unrecognised) throw new InvalidDataException(string.Join(Environment.NewLine, result.Problems));
            inspected = result;
            adoptFlare.Visible = result.Ownership == DesktopHelpOwnership.FlareNeedsAdoption;
            inspection.Text = $"Desktop destination: {result.DestinationDirectory}\n{result.Ownership} · {result.FileCount} existing files\nInspected SHA256: {result.Sha256}\n\nPublishing uses saved pages only. GitHub Pages deployment, if configured for this branch, continues after the push.";
        }
        catch (Exception failure) when (failure is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            if (IsDisposed || generation != inspectionGeneration) return;
            inspection.Text = failure.Message;
        }
        ValidateChoices();
    }

    private bool ValidateChoices()
    {
        string message = "";
        if (!Uri.TryCreate(repository.Text.Trim(), UriKind.Absolute, out var remote) || remote.Scheme != "https" ||
            remote.Host != "github.com" || remote.UserInfo.Length != 0 || !remote.IsDefaultPort || remote.Query.Length != 0 || remote.Fragment.Length != 0 ||
            remote.AbsolutePath.Trim('/').Split('/').Length != 2)
            message = "Enter a GitHub HTTPS repository URL without credentials.";
        else if (!PlausibleBranch(branch.Text.Trim())) message = "Enter a valid Git branch name, for example master or help-updates.";
        else if (string.IsNullOrWhiteSpace(authorName.Text) || authorName.Text.IndexOfAny(['\r', '\n', '<', '>']) >= 0 ||
            !System.Text.RegularExpressions.Regex.IsMatch(authorEmail.Text.Trim(), @"\A[^\s<>@]+@[^\s<>@]+\z"))
            message = "Enter a valid commit author's name and email address.";
        else if (!Path.IsPathFullyQualified(zip.Text) || !Path.GetExtension(zip.Text).Equals(".zip", StringComparison.OrdinalIgnoreCase)) message = "Choose a complete website ZIP filename.";
        else if (inspected == null) message = "Choose and inspect a valid desktop-help destination.";
        else if (adoptFlare.Visible && !adoptFlare.Checked) message = "Confirm replacement of the recognised Flare desktop output.";
        else if ((branch.Text.Trim() is "master" or "main") && !allowDefault.Checked) message = "Confirm default-branch publication to update the GitHub website.";
        error.Text = message;
        publish.Enabled = message.Length == 0;
        return publish.Enabled;
    }

    private static bool PlausibleBranch(string value) => value.Length > 0 && value != "@" &&
        !value.StartsWith('-') && !value.StartsWith('/') && !value.EndsWith('/') && !value.EndsWith('.') &&
        !value.Contains("..") && !value.Contains("//") && !value.Contains("@{") &&
        !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || "~^:?*[\\".Contains(character)) &&
        value.Split('/').All(part => !part.StartsWith('.') && !part.EndsWith(".lock", StringComparison.OrdinalIgnoreCase));
}
