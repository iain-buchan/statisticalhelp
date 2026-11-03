using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public sealed partial class MainForm
{
    private readonly Func<SaveFileDialog, DialogResult>? websiteZipPicker;
    public WebsiteZipExportResult? LastWebsiteZipExportResult { get; private set; }
    public string SuggestedWebsiteZipPath => AuthoringPreferences.ReadWebsiteZipPath(artifactRoot, out _);

    private async Task ShowWebsiteZipExportAsync()
    {
        // Validate the live draft before asking for a filename. Export includes
        // that draft; it never invokes Save/Discard or edits the working copy.
        await CaptureWebsiteExportAsync();
        string suggested = AuthoringPreferences.ReadWebsiteZipPath(artifactRoot, out string? preferenceWarning);
        if (preferenceWarning != null) status.Text = preferenceWarning;
        string? directory = Path.GetDirectoryName(suggested);
        using var dialog = new SaveFileDialog
        {
            Title = "Export Website ZIP for www.statsdirect.com/help",
            Filter = "ZIP archives (*.zip)|*.zip", DefaultExt = "zip", AddExtension = true,
            FileName = Path.GetFileName(suggested),
            InitialDirectory = directory != null && Directory.Exists(directory) ? directory : AuthoringPreferences.DefaultDirectory,
            OverwritePrompt = true, CheckPathExists = true, RestoreDirectory = true
        };
        if ((websiteZipPicker?.Invoke(dialog) ?? dialog.ShowDialog(this)) != DialogResult.OK)
        {
            status.Text = "Website ZIP export cancelled · the page draft is unchanged";
            return;
        }
        // Re-read after the picker: another window may have changed saved
        // source while it was open, just as the live preview checks its input.
        await ExportWebsiteZipCoreAsync(dialog.FileName, CancellationToken.None);
    }

    /// <summary>Export the current valid draft and other saved pages as a fresh Website ZIP; never saves the draft.</summary>
    public async Task<WebsiteZipExportResult> ExportWebsiteZipAsync(string zipPath, CancellationToken cancellationToken = default)
    {
        if (busy) throw new InvalidOperationException("Please wait for the current action to finish.");
        SetBusy(true);
        try { return await ExportWebsiteZipCoreAsync(zipPath, cancellationToken); }
        finally { SetBusy(false); }
    }

    private async Task<WebsiteZipExportResult> ExportWebsiteZipCoreAsync(string zipPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await CaptureWebsiteExportAsync();
        cancellationToken.ThrowIfCancellationRequested();
        status.Text = "Exporting a fresh Website ZIP" + (snapshot.IncludesDraft ? " including the unsaved current-page draft…" : " from saved pages…");
        var result = await Task.Run(() => WebsiteZipExporter.Export(snapshot.Project, zipPath, Path.Combine(AppContext.BaseDirectory, "Web"), snapshot.Overrides, cancellationToken), cancellationToken);
        LastWebsiteZipExportResult = result;
        string? rememberWarning = null;
        try { AuthoringPreferences.SaveWebsiteZipPath(artifactRoot, result.ZipPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            rememberWarning = "The ZIP was created, but its location could not be remembered.";
        }
        status.Text = "Exported Website ZIP · " + result.ZipPath + " · " + result.TopicCount + " pages · " + result.SearchDocumentCount + " search entries · " + result.Warnings.Count + " warnings · " +
            (snapshot.IncludesDraft ? "includes the unsaved current-page draft" : "saved pages") + " · upload the extracted contents to www.statsdirect.com/help" + (rememberWarning == null ? "" : " · " + rememberWarning);
        return result;
    }

    private async Task<WebsiteExportSnapshot> CaptureWebsiteExportAsync()
    {
        if (project == null) throw new InvalidOperationException("Open a working copy before exporting a Website ZIP.");
        await browserReady.Task;
        await RequireEquationFinishedAsync();
        var sourceProject = project;
        string? path = currentPath;
        if (path == null) return new(sourceProject, null, false);
        string draft = JsonSerializer.Deserialize<string>(await editor.ExecuteScriptAsync("window.HelpEditor.getSource()"))
            ?? throw new InvalidDataException("Correct the HTML source before exporting; the unsaved draft remains open.");
        using (var reader = XmlReader.Create(new StringReader(draft), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 }))
        {
            var document = XDocument.Load(reader);
            if (document.Root?.Name.LocalName != "html" || !document.Descendants().Any(element => element.Name.LocalName == "body"))
                throw new InvalidDataException("The current page must contain an HTML root and body before export.");
        }
        var input = await Task.Run(() => PreviewInput(sourceProject, path, draft));
        if (sourceProject != project || path != currentPath || draft != JsonSerializer.Deserialize<string>(await editor.ExecuteScriptAsync("window.HelpEditor.getSource()")))
            throw new InvalidOperationException("The open draft changed while preparing the Website ZIP. Export again; your changes are preserved.");
        return new(sourceProject, input.Overrides, input.Overrides != null);
    }

    private sealed record WebsiteExportSnapshot(HelpProject Project, IReadOnlyDictionary<string, string>? Overrides, bool IncludesDraft);
}
