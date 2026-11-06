using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Web.WebView2.WinForms;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public sealed partial class MainForm
{
    private const string GeneratedPreviewOrigin = "https://help-generated.invalid";
    private readonly string outputPreviewSession = Guid.NewGuid().ToString("N");
    private AppHelp.HelpService? appHelpPreview;
    private bool refreshingOutputPreviews;
    private bool fittingAuthoringNavigation;
    private readonly Dictionary<HelpBuildTarget, OutputPreviewCache> outputPreviewCache = [];
    private readonly Dictionary<HelpBuildTarget, OutputPreviewDisplay> outputPreviewDisplays = [];
    private string? outputPreviewRuntimeFingerprint;

    public WebView2? AppPreviewWebView => appHelpPreview?.View?.Browser;
    public bool IsAppPreviewVisible => appHelpPreview?.IsVisible == true;
    public bool IsAppPreviewDocked => appHelpPreview?.IsDocked == true;
    public BuildResult? LastOutputPreviewResult { get; private set; }
    public int OutputPreviewBuildCount { get; private set; }
    public bool LastOutputPreviewWasReused { get; private set; }
    public Task ToggleAppPreviewModeAsync() => appHelpPreview?.ToggleModeAsync() ?? Task.CompletedTask;
    public void HideAppPreview() => appHelpPreview?.Hide();

    private void ResetOutputPreviews()
    {
        appHelpPreview?.Dispose();
        appHelpPreview = null;
        LastOutputPreviewResult = null;
        outputPreviewCache.Clear();
        outputPreviewDisplays.Clear();
        OutputPreviewBuildCount = 0;
        LastOutputPreviewWasReused = false;
        currentTopicFragment = null;
    }

    private async Task RenderOutputPreviewAsync(HelpBuildTarget selected, bool activate)
    {
        if (currentPath == null || project == null) return;
        string path = currentPath;
        string fragment = string.IsNullOrEmpty(currentTopicFragment) ? "" : "#" + Uri.EscapeDataString(Uri.UnescapeDataString(currentTopicFragment));
        var sourceProject = project;
        string draft = JsonSerializer.Deserialize<string>(await editor.ExecuteScriptAsync("window.HelpEditor.getSource()"))
            ?? throw new InvalidDataException("Correct the HTML source before previewing; the draft remains open.");
        string output = Path.Combine(artifactRoot, "Build", "EditorPreview", outputPreviewSession, selected.ToString());
        // Moving to another saved topic changes navigation, not the site's
        // input. Keep the generated site, navigation and search until source,
        // design or the current unsaved draft actually changes. Hash source
        // bytes instead of timestamps so external same-size edits are noticed.
        var input = await Task.Run(() => PreviewInput(sourceProject, path, draft));
        bool reused = outputPreviewCache.TryGetValue(selected, out var cached) && cached.Fingerprint == input.Fingerprint &&
            PreviewOutputExists(cached.Result.OutputDirectory, path, sourceProject.GetAppearance(selected));
        BuildResult result;
        if (reused) result = cached!.Result;
        else
        {
            // Build reuses this target's output folder. A failed build may have
            // already replaced files, so its previous cache entry is no longer
            // trustworthy even if source is later reverted to that old input.
            outputPreviewCache.Remove(selected);
            status.Text = "Preparing the complete " + (selected == HelpBuildTarget.Public ? "browser" : "app") + " preview…";
            result = await Task.Run(() => HelpBuilder.Build(sourceProject, output, Path.Combine(AppContext.BaseDirectory, "Web"), selected, input.Overrides));
            OutputPreviewBuildCount++;
            var after = await Task.Run(() => PreviewInput(sourceProject, path, draft));
            if (after.Fingerprint != input.Fingerprint)
                throw new InvalidOperationException("The help source changed while preparing its preview. Select the view again to refresh; your draft is preserved.");
            outputPreviewCache[selected] = new(input.Fingerprint, result);
        }
        if (sourceProject != project || path != currentPath || draft != JsonSerializer.Deserialize<string>(await editor.ExecuteScriptAsync("window.HelpEditor.getSource()")))
            throw new InvalidOperationException("The draft changed while preparing its preview. Select the view again to refresh; your edits are preserved.");
        string requestedTopic = path.ToLowerInvariant() + fragment;
        bool retainDocument = reused && outputPreviewDisplays.TryGetValue(selected, out var displayed) &&
            displayed.Fingerprint == input.Fingerprint && displayed.Topic == requestedTopic;
        if (activate)
        {
            await ApplyPreviewDesignAsync(selected);
            string view = selected == HelpBuildTarget.Public ? "browser" : "app";
            if (await editor.ExecuteScriptAsync("window.HelpEditor.setOutputView(" + JsonSerializer.Serialize(view) + ")") != "true")
                throw new InvalidOperationException(DialogNotice);
        }
        if (selected == HelpBuildTarget.Public)
        {
            HelpWebViewSettings.MapLocalFolder(editor.CoreWebView2, "help-generated.invalid", output);
            string uri = sourceProject.GetAppearance(HelpBuildTarget.Public) == "flare"
                ? GeneratedPreviewOrigin + "/" + string.Join("/", path.ToLowerInvariant().Split('/').Select(Uri.EscapeDataString)) + fragment
                : GeneratedPreviewOrigin + "/index.html#topic=" + Uri.EscapeDataString(path.ToLowerInvariant() + fragment);
            await editor.ExecuteScriptAsync("window.HelpEditor.setGeneratedPreview(" + JsonSerializer.Serialize(uri) + "," + (retainDocument ? "true" : "false") + ")");
        }
        else
        {
            bool firstPresentation = appHelpPreview == null;
            if (firstPresentation)
            {
                appHelpPreview = new AppHelp.HelpService(Path.Combine(artifactRoot, ".app-help-preview-profile", outputPreviewSession));
                appHelpPreview.Closed += async () =>
                {
                    if (IsDisposed || Disposing) return;
                    if (workspaceView == "desktop" && IsEditorReady)
                    {
                        await editor.ExecuteScriptAsync("window.HelpEditor.setWorkspaceView('edit')");
                        await RefreshEditorStateAsync();
                    }
                    FitAuthoringNavigation();
                    UpdateWorkspaceCaption();
                };
                appHelpPreview.Show(this, requestedTopic, output);
                appHelpPreview.View.ExternalLinkRequested += uri => externalLinkLauncher(new Uri(uri));
            }
            else appHelpPreview!.ShowExisting();
            await appHelpPreview.View.Ready.WaitAsync(TimeSpan.FromSeconds(45));
            // First Show queues its requested topic before Ready. Afterwards,
            // revealing an unchanged pane must retain its reading position,
            // expanded R sections and browser history, not navigate twice.
            if (!firstPresentation && !retainDocument)
                appHelpPreview.View.Browser.CoreWebView2.Navigate(AppHelp.HelpCatalog.Origin + "/" +
                    string.Join("/", path.ToLowerInvariant().Split('/').Select(Uri.EscapeDataString)) + "?editorPreview=" + Guid.NewGuid().ToString("N") + fragment);
            FitAuthoringNavigation();
        }
        LastOutputPreviewResult = result;
        outputPreviewDisplays[selected] = new(input.Fingerprint, requestedTopic);
        LastOutputPreviewWasReused = reused;
        status.Text = $"{(selected == HelpBuildTarget.Public ? "Browser" : "App")} preview · {result.TopicCount} pages · {(dirty ? "includes the unsaved page draft" : "saved source")} · {result.Warnings.Count} warnings";
    }

    private PreviewBuildInput PreviewInput(HelpProject sourceProject, string path, string draft)
    {
        string saved = sourceProject.ReadTopic(path);
        bool hasDraft = draft != saved;
        // Runtime assets are part of the running application and remain fixed
        // for its lifetime. A newly deployed editor starts a new cache session.
        outputPreviewRuntimeFingerprint ??= FingerprintFolders(Path.Combine(AppContext.BaseDirectory, "Web"));
        string source = FingerprintFolders(Path.Combine(sourceProject.RootPath, "Content"), Path.Combine(sourceProject.RootPath, "Project"));
        string overlay = hasDraft ? path.ToLowerInvariant() + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(draft))) : "saved";
        return new(source + ":" + outputPreviewRuntimeFingerprint + ":" + overlay,
            hasDraft ? new Dictionary<string, string> { [path] = draft } : null);
    }

    private static string FingerprintFolders(params string[] folders)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string folder in folders)
        {
            var files = new List<string>(); Collect(folder);
            foreach (string file in files.Order(StringComparer.Ordinal))
            {
                byte[] name = Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, file));
                digest.AppendData(BitConverter.GetBytes(name.Length)); digest.AppendData(name);
                using var stream = File.OpenRead(file);
                digest.AppendData(SHA256.HashData(stream));
            }
            // Delimit independently rooted input trees, including empty ones.
            digest.AppendData([0, 255, 0, 255]);
            void Collect(string directory)
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Preview inputs must not contain linked folders.");
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Preview inputs must not contain linked files or folders.");
                    if ((attributes & FileAttributes.Directory) != 0) Collect(entry); else files.Add(entry);
                }
            }
        }
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    private static bool PreviewOutputExists(string output, string path, string appearance) =>
        File.Exists(Path.Combine(output, ".statsdirect-help-output.json")) && File.Exists(Path.Combine(output, "build-info.json")) &&
        File.Exists(Path.Combine(output, "index.html")) && File.Exists(Path.Combine(output, "search-index.js")) &&
        File.Exists(Path.Combine(output, "contents-data.js")) && File.Exists(Path.Combine(output, "help-viewer.css")) &&
        File.Exists(Path.Combine(output, "help-viewer.js")) && File.Exists(Path.Combine(output, "Data", "Alias.xml")) &&
        File.Exists(Path.Combine(output, "resources", "help-editor-runtime", "mathjax", "tex-mml-svg.js")) &&
        (appearance != "flare" || File.Exists(Path.Combine(output, "resources", "help-editor-runtime", "topic.css"))) &&
        File.Exists(Path.Combine(output, path.ToLowerInvariant().Replace('/', Path.DirectorySeparatorChar)));

    private sealed record PreviewBuildInput(string Fingerprint, IReadOnlyDictionary<string, string>? Overrides);
    private sealed record OutputPreviewCache(string Fingerprint, BuildResult Result);
    private sealed record OutputPreviewDisplay(string Fingerprint, string Topic);

    private async Task RefreshVisibleOutputPreviewsAsync()
    {
        if (refreshingOutputPreviews || project == null || currentPath == null) return;
        refreshingOutputPreviews = true;
        try
        {
            if (IsAppPreviewVisible || workspaceView == "desktop") await RenderOutputPreviewAsync(HelpBuildTarget.Desktop, activate: false);
            if (workspaceView == "public") await RenderOutputPreviewAsync(HelpBuildTarget.Public, activate: false);
        }
        finally { refreshingOutputPreviews = false; }
    }

    private void FitAuthoringNavigation()
    {
        if (fittingAuthoringNavigation) return;
        fittingAuthoringNavigation = true;
        try
        {
            // At a narrow window, give the page editor room beside the real app
            // pane. The source Contents returns as soon as space permits.
            bool collapse = IsAppPreviewVisible && IsAppPreviewDocked && authoringSplit.ClientSize.Width < 900 * DeviceDpi / 96d;
            const int preferredMinimum = 650;
            int minimum = !collapse && authoringSplit.ClientSize.Width >= authoringSplit.Panel1MinSize + authoringSplit.SplitterWidth + preferredMinimum
                ? preferredMinimum : 0;
            // SplitContainer still enforces Panel2MinSize when Panel1 is collapsed.
            // Clear it before narrowing the editor, otherwise an already-collapsed
            // panel retains its old bounds and overlaps the docked App preview.
            if (authoringSplit.Panel2MinSize > minimum) authoringSplit.Panel2MinSize = minimum;
            if (authoringSplit.Panel1Collapsed != collapse) authoringSplit.Panel1Collapsed = collapse;
            if (authoringSplit.Panel2MinSize != minimum) authoringSplit.Panel2MinSize = minimum;
        }
        finally { fittingAuthoringNavigation = false; }
    }

    private string SourceLinkFromGeneratedPreview(string href)
    {
        if (!IsOrigin(href, GeneratedPreviewOrigin)) return href;
        var uri = new Uri(href);
        string path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (path == "index.html") path = "contents.htm";
        var topic = allTopics.SingleOrDefault(topic => topic.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The preview link does not identify an editable help page.");
        return ProjectOrigin + "/" + string.Join("/", topic.Path.Split('/').Select(Uri.EscapeDataString)) + uri.Fragment;
    }

    private const string GeneratedPreviewLinkBridge = "if(location.origin==='https://help-generated.invalid'){function send(href){parent.postMessage({type:'help-editor-preview-link',generation:Number(new URLSearchParams(location.search).get('__help_preview')),href:href},'*');}addEventListener('message',function(e){var f=document.getElementById('topic');if(f instanceof HTMLIFrameElement&&e.source===f.contentWindow&&e.data?.type==='help-editor-preview-link'&&typeof e.data.href==='string')send(e.data.href);});document.addEventListener('click',function(e){var a=e.target instanceof Element?e.target.closest('a[href]'):null;if(!a||e.button!==0||e.ctrlKey||e.shiftKey||e.altKey||e.metaKey)return;var u=new URL(a.href);if(u.pathname===location.pathname&&u.origin===location.origin&&u.hash)return;e.preventDefault();e.stopImmediatePropagation();send(a.href);},true);}";
}
