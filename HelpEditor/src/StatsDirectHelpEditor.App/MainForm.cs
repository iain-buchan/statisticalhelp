using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

/// <summary>A separate authoring shell. Only marked working copies can be opened.</summary>
public sealed partial class MainForm : Form
{
    private const string EditorOrigin = "https://help-editor.invalid";
    private const string ProjectOrigin = "https://help-project.invalid";
    private readonly string artifactRoot;
    private readonly HelpEditorWorkspace workspace;
    private readonly string initialProject;
    private readonly string? initialTopic;
    private readonly Icon applicationIcon;
    private readonly Action<Uri> externalLinkLauncher;
    private readonly Action<Uri> browserPreviewLauncher;
    private readonly WebView2 editor = new() { Dock = DockStyle.Fill };
    private readonly SplitContainer authoringSplit = new() { Dock = DockStyle.Fill, Size = new Size(1400, 800), SplitterDistance = 320, Panel1MinSize = 250, Panel2MinSize = 650, BackColor = Color.FromArgb(218, 223, 230) };
    private readonly TreeView contents = new() { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true, ShowNodeToolTips = true };
    private readonly ListBox pages = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly TextBox pageFilter = new() { Dock = DockStyle.Top, PlaceholderText = "Find a page by title or filename…" };
    private readonly ToolStrip contentsBar = new() { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(2, 3, 2, 3) };
    private readonly ToolStripComboBox target = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 100, AccessibleName = "Help build target" };
    private readonly ToolStripStatusLabel status = new("Opening editor…") { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Overflow = ToolStripItemOverflow.Never };
    private readonly ToolStripStatusLabel unsaved = new("");
    private readonly ToolStripProgressBar progress = new() { Visible = false, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Width = 95 };
    private readonly Label copyLabel = new() { AutoSize = false, Height = 34, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 10, 0), BackColor = Color.FromArgb(235, 242, 250), ForeColor = Color.FromArgb(30, 65, 100) };
    private readonly TaskCompletionSource<bool> browserReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private HelpProject? project;
    private List<TocNode> toc = [];
    private IReadOnlyList<TopicInfo> allTopics = [];
    private string? currentPath;
    private string? currentTopicFragment;
    private string? loadedDiskSource;
    private string? loadedContentsSource;
    private bool dirty;
    private bool changingSelection;
    private bool busy;
    private bool closingApproved;
    private bool editorDialogOpen;
    private string? editorDialogKind;
    private string DialogNotice => editorDialogKind == "link" ? "Apply or Cancel the open link before continuing." : "Apply or Cancel the open equation before continuing.";

    public WebView2 EditorWebView => editor;
    public HelpProject CurrentProject => project ?? throw new InvalidOperationException("No working copy is open.");
    public string? CurrentTopicPath => currentPath;
    public bool IsEditorReady => browserReady.Task.IsCompletedSuccessfully;
    public bool IsDirty => dirty;
    public Task Initialization => initialized.Task;
    public string ArtifactRoot => artifactRoot;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool SuppressDialogs { get; set; }
    public string? LastError { get; private set; }
    public BuildResult? LastBuildResult { get; private set; }

    public MainForm(string? projectPath = null, Action<Uri>? externalLinkLauncher = null, string? initialTopic = null, Action<Uri>? browserPreviewLauncher = null, HelpEditorWorkspace? workspaceOverride = null)
    {
        workspace = workspaceOverride ?? HelpEditorWorkspace.Resolve(AppContext.BaseDirectory);
        artifactRoot = workspace.RootPath;
        initialProject = projectPath ?? workspace.DefaultProjectPath;
        this.initialTopic = initialTopic;
        this.externalLinkLauncher = externalLinkLauncher ?? (uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        this.browserPreviewLauncher = browserPreviewLauncher ?? (uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        // Keep every ICO frame for Windows' title-bar/taskbar DPI choices. Each
        // editor window owns its clone independently of the resource stream.
        using var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("StatsDirect.HelpEditor.AppIcon")
            ?? throw new InvalidOperationException("The Help Editor application icon is missing.");
        using var resourceIcon = new Icon(iconStream);
        applicationIcon = (Icon)resourceIcon.Clone();
        Icon = applicationIcon;
        Disposed += (_, _) => applicationIcon.Dispose();
        Text = "StatsDirect Help Editor — prototype";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = new Font("Arial", 11);
        Size = new Size(1420, 940);
        MinimumSize = new Size(1000, 620);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;

        var menu = MakeMenu();
        MainMenuStrip = menu;
        contentsBar.Font = Font;
        contentsBar.Padding = new Padding(4, 4, 4, 4);
        target.Font = target.ComboBox.Font = Font;
        target.Width = 120;
        InitializeTargetAndContentsToolbar();
        Disposed += (_, _) => target.Dispose();
        var split = authoringSplit;
        split.SizeChanged += (_, _) => FitAuthoringNavigation();
        var tabs = new ReadableTabControl { Dock = DockStyle.Fill, AccessibleName = "Help navigation" };
        var contentsTab = new TabPage("Contents") { Padding = new Padding(4), Font = Font };
        var pagesTab = new TabPage("All pages") { Padding = new Padding(4), Font = Font };
        contentsTab.Controls.Add(contents);
        contentsTab.Controls.Add(contentsBar);
        pagesTab.Controls.Add(pages);
        pagesTab.Controls.Add(pageFilter);
        tabs.TabPages.AddRange([contentsTab, pagesTab]);
        split.Panel1.Controls.Add(tabs);
        split.Panel2.Padding = new Padding(4, 0, 0, 0);
        InitializeWorkspace(split.Panel2);
        var statusBar = new StatusStrip { Font = Font, ShowItemToolTips = true, LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow };
        // Reserve the right-hand indicators before sizing the long status message.
        // The table/spring layout can move them to an invisible second row at high DPI.
        foreach (ToolStripItem item in new ToolStripItem[] { unsaved, gitBadge, progress })
        { item.Alignment = ToolStripItemAlignment.Right; item.Overflow = ToolStripItemOverflow.Never; }
        bool sizingStatus = false;
        statusBar.Layout += (_, _) =>
        {
            if (sizingStatus) return;
            sizingStatus = true;
            try
            {
                int indicators = statusBar.Items.Cast<ToolStripItem>().Where(item => item != status && item.Available)
                    .Sum(item => item.GetPreferredSize(Size.Empty).Width + item.Margin.Horizontal);
                status.Width = Math.Max(1, statusBar.DisplayRectangle.Width - indicators - status.Margin.Horizontal);
                status.Height = Math.Max(1, statusBar.DisplayRectangle.Height - status.Margin.Vertical);
            }
            finally { sizingStatus = false; }
        };
        status.TextChanged += (_, _) => status.ToolTipText = status.Text;
        gitBadge.Click += async (_, _) => await RunSafeAsync(ReviewGitStatusAsync);
        statusBar.Items.AddRange([status, unsaved, gitBadge, progress]);
        Controls.Add(split);
        Controls.Add(copyLabel);
        Controls.Add(menu);
        Controls.Add(statusBar);

        pages.Format += (_, e) => { if (e.ListItem is TopicInfo t) e.Value = $"{t.Title}   ({t.Path})"; };
        pages.FormattingEnabled = true;
        pageFilter.TextChanged += (_, _) => RefreshPageList();
        pages.SelectedIndexChanged += async (_, _) =>
        {
            if (changingSelection || busy || pages.SelectedItem is not TopicInfo topic || topic.Path == currentPath) return;
            await RunSafeAsync(async () => { if (await ConfirmLeaveAsync()) await OpenTopicAsync(topic.Path); else SelectCurrentPage(); });
        };
        contents.AfterSelect += async (_, e) =>
        {
            if (changingSelection || busy || e.Node?.Tag is not TocNode node || string.IsNullOrWhiteSpace(node.Link)) return;
            string path = TopicPath(node.Link);
            string? fragment = node.Link.Contains('#') ? node.Link[(node.Link.IndexOf('#') + 1)..] : null;
            if (path == currentPath && fragment == currentTopicFragment) return;
            await RunSafeAsync(async () =>
            {
                if (path == currentPath)
                {
                    // A section link on this page is navigation, not a reload
                    // from disk. Keep any unsaved text and its Undo history.
                    currentTopicFragment = fragment;
                    if (!string.IsNullOrEmpty(fragment))
                        await editor.ExecuteScriptAsync("window.HelpEditor.revealFragment(" + JsonSerializer.Serialize(fragment) + ")");
                    await RefreshVisibleOutputPreviewsAsync();
                    status.Text = node.Link;
                }
                else if (await ConfirmLeaveAsync()) await OpenTopicAsync(node.Link);
                else SelectCurrentPage();
            });
        };
        contents.NodeMouseDoubleClick += (_, e) => e.Node?.Toggle();
        Shown += async (_, _) =>
        {
            try
            {
                SetBusy(true);
                Exception? firstRunError = null;
                if (projectPath == null)
                {
                    try { await Task.Run(workspace.PrepareDefaultProject); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                    { firstRunError = ex; }
                }
                await InitializeBrowserAsync();
                if (firstRunError != null)
                    throw new IOException("The installed help working copy could not be prepared. Use File > Open working copy to select another copy, or repair the installation.", firstRunError);
                await OpenProjectAsync(initialProject);
                if (!string.IsNullOrWhiteSpace(this.initialTopic)) await OpenTopicAsync(this.initialTopic);
                initialized.TrySetResult(true);
            }
            catch (Exception ex) { initialized.TrySetException(ex); ShowError(ex); }
            finally { SetBusy(false); }
        };
        FormClosing += async (_, e) =>
        {
            if (closingApproved) return;
            if (busy) { e.Cancel = true; status.Text = "Please wait for the current action to finish."; return; }
            // Cancel synchronously before awaiting the renderer: its latest input
            // or an unapplied MathLive draft may precede the native web message.
            e.Cancel = true;
            try
            {
                if (await ConfirmLeaveAsync()) { closingApproved = true; BeginInvoke(Close); }
            }
            catch (Exception ex) { ShowError(ex); }
        };
    }

    private MenuStrip MakeMenu()
    {
        var menu = new MenuStrip { Font = new Font("Arial", 12), Padding = new Padding(6, 2, 6, 2), BackColor = SystemColors.Window, ForeColor = SystemColors.ControlText };
        var file = new ToolStripMenuItem("&File");
        AddMenu(file, "&Open working copy…", async () => await PickProjectAsync(), Keys.Control | Keys.O);
        AddMenu(file, "&New page…", NewPageAsync, Keys.Control | Keys.N);
        AddMenu(file, "&Save page", SaveCurrentTopicAsync, Keys.Control | Keys.S);
        AddMenu(file, "Reload page from disk…", ReloadPageAsync);
        AddMenu(file, "Delete page…", DeletePageAsync);
        AddMenu(file, "Deleted pages…", ShowDeletedPagesAsync);
        file.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(file, "&Build help and search", BuildHelpAsync, Keys.Control | Keys.B);
        AddMenu(file, "&Preview site in browser", PreviewSiteInBrowserAsync, Keys.F5);
        AddMenu(file, "Preview site in editor", BuildAndPreviewAsync);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Close", null, (_, _) => Close());
        var editMenu = new ToolStripMenuItem("&Edit");
        AddMenu(editMenu, "Find and &replace in all pages…", ShowSearchReplaceAsync, Keys.Control | Keys.Shift | Keys.H);
        undoSearchReplaceMenu = new ToolStripMenuItem("Undo last replace in all pages") { Enabled = false };
        undoSearchReplaceMenu.Click += async (_, _) => await RunSafeAsync(UndoSearchReplaceAsync);
        editMenu.DropDownItems.Add(undoSearchReplaceMenu);
        editMenu.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(editMenu, "&Polish current page with ChatGPT…", ShowPolishAsync);
        editMenu.DropDownOpening += (_, _) => UpdateSearchReplaceCommands();
        var tocMenu = new ToolStripMenuItem("&Contents");
        AddMenu(tocMenu, "Add existing &page…", AddExistingPageAsync);
        AddMenu(tocMenu, "Add &section…", AddSectionAsync);
        AddMenu(tocMenu, "&Rename entry…", RenameContentsAsync);
        AddMenu(tocMenu, "Change &destination…", ChangeContentsLinkAsync);
        tocMenu.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(tocMenu, "Move up", () => MoveContentsAsync(-1));
        AddMenu(tocMenu, "Move down", () => MoveContentsAsync(1));
        AddMenu(tocMenu, "Indent", IndentContentsAsync);
        AddMenu(tocMenu, "Outdent", OutdentContentsAsync);
        tocMenu.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(tocMenu, "Remove entry…", RemoveContentsAsync);
        var github = new ToolStripMenuItem("&GitHub");
        AddMenu(github, "&Clone project…", CloneGitProjectAsync);
        AddMenu(github, "&Status / review changes…", ReviewGitStatusAsync);
        github.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(github, "&Fetch", FetchGitAsync);
        AddMenu(github, "Pull (&fast-forward only)", PullGitAsync);
        AddMenu(github, "New working &branch…", CreateGitBranchAsync);
        github.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(github, "&Rebuild website and commit…", CommitGitAsync);
        AddMenu(github, "&Push branch", PushGitAsync);
        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add("About this prototype", null, (_, _) => MessageBox.Show(this,
            "Edits are saved only to the marked working copy.\n\nContents changes save automatically. Removing a contents entry keeps the page file. Deleted pages can be recovered through File > Deleted pages. Deletion is blocked if other pages or application context IDs still refer to the page.\n\nBuild help regenerates the offline pages, navigation and search index under this prototype’s Build folder. It does not publish help or rebuild StatsDirect installers.",
            "StatsDirect Help Editor", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.AddRange([file, editMenu, tocMenu, github, help]);
        foreach (ToolStripMenuItem item in menu.Items)
        {
            item.Font = menu.Font;
            item.Padding = new Padding(10, 4, 10, 4);
            StyleDropDown(item, menu.Font);
        }
        return menu;
    }

    private static void StyleDropDown(ToolStripDropDownItem parent, Font font)
    {
        if (!parent.HasDropDownItems) return;
        parent.DropDown.Font = font;
        parent.DropDown.BackColor = SystemColors.Window;
        parent.DropDown.ForeColor = SystemColors.ControlText;
        foreach (ToolStripItem child in parent.DropDownItems)
        {
            child.Font = font;
            if (child is ToolStripMenuItem item)
            {
                item.Padding = new Padding(8, 5, 8, 5);
                StyleDropDown(item, font);
            }
        }
    }

    private void AddMenu(ToolStripMenuItem parent, string text, Func<Task> action, Keys shortcut = Keys.None)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
        item.Click += async (_, _) => await RunSafeAsync(action);
        parent.DropDownItems.Add(item);
    }

    private void InitializeTargetAndContentsToolbar()
    {
        target.Items.AddRange(["Desktop", "Public"]);
        target.SelectedIndex = 0;
        target.SelectedIndexChanged += async (_, _) =>
        {
            if (!IsEditorReady || synchronizingTarget) return;
            // Refreshing the modal state also reflects the renderer's current
            // target into this control. Preserve the requested command before
            // awaiting it so that refresh cannot turn Public back into Desktop.
            int requestedTarget = target.SelectedIndex;
            try
            {
                await RequireEquationFinishedAsync();
                if (project != null) await ApplyPreviewDesignAsync(requestedTarget == 1 ? HelpBuildTarget.Public : HelpBuildTarget.Desktop);
                await editor.ExecuteScriptAsync("window.HelpEditor.setBuildTarget(" + JsonSerializer.Serialize(requestedTarget == 1 ? "Public" : "Desktop") + ")");
                if (workspaceView is "desktop" or "public")
                    await SwitchWorkspaceAsync(requestedTarget == 1 ? "public" : "desktop");
                SetTargetSilently(requestedTarget);
                UpdateWorkspaceCaption();
            }
            catch (Exception ex) { ShowError(ex); }
        };
        // The page-view tabs choose the preview/build target. Retain this private
        // selector as command state only; File and Edit contain the commands.
        target.Visible = false;

        AddButton(contentsBar, "Page +", AddExistingPageAsync, "Add an existing page to Contents");
        AddButton(contentsBar, "Section +", AddSectionAsync, "Add a contents section");
        AddButton(contentsBar, "Rename", RenameContentsAsync, "Rename the selected contents entry");
        AddButton(contentsBar, "Link…", ChangeContentsLinkAsync, "Change the destination of this contents entry");
        AddButton(contentsBar, "↑", () => MoveContentsAsync(-1), "Move entry up");
        AddButton(contentsBar, "↓", () => MoveContentsAsync(1), "Move entry down");
        AddButton(contentsBar, "→", IndentContentsAsync, "Nest entry under the previous entry");
        AddButton(contentsBar, "←", OutdentContentsAsync, "Move entry out of its section");
        AddButton(contentsBar, "Remove", RemoveContentsAsync, "Remove this contents entry; keep its page file");
    }

    private ToolStripButton AddButton(ToolStrip bar, string text, Func<Task> action, string tooltip)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = tooltip, AccessibleName = tooltip, Padding = new Padding(8, 4, 8, 4) };
        button.Click += async (_, _) => await RunSafeAsync(action);
        bar.Items.Add(button);
        return button;
    }

    private async Task InitializeBrowserAsync()
    {
        string assets = Path.Combine(AppContext.BaseDirectory, "Web");
        if (!File.Exists(Path.Combine(assets, "index.html"))) throw new FileNotFoundException("The editor’s Web assets are missing. Build the prototype project first.");
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(artifactRoot, ".editor-profile"));
        await editor.EnsureCoreWebView2Async(environment);
        var core = editor.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("help-editor.invalid", assets, CoreWebView2HostResourceAccessKind.DenyCors);
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.NavigationStarting += (_, e) =>
        {
            if (!IsOrigin(e.Uri, EditorOrigin)) { e.Cancel = true; status.Text = "Use Preview to follow help links."; }
        };
        core.NewWindowRequested += async (_, e) =>
        {
            using var deferral = e.GetDeferral();
            e.Handled = true;
            if (e.IsUserInitiated && !string.IsNullOrWhiteSpace(e.Uri) && e.Uri != "about:blank")
                await RunLinkOpenAsync(e.Uri, true);
        };
        core.ContextMenuRequested += (_, e) => RemoveBrowserNavigation(e.MenuItems);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            string uri = e.Request.Uri;
            if (IsOrigin(uri, EditorOrigin) || IsOrigin(uri, ProjectOrigin) || IsOrigin(uri, GeneratedPreviewOrigin) || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("blob:" + EditorOrigin, StringComparison.OrdinalIgnoreCase) || uri.StartsWith("blob:" + GeneratedPreviewOrigin, StringComparison.OrdinalIgnoreCase)) return;
            e.Response = environment.CreateWebResourceResponse(new MemoryStream([]), 403, "Offline editor", "Content-Type: text/plain");
        };
        core.WebMessageReceived += async (_, e) =>
        {
            try
            {
                if (!IsOrigin(e.Source, EditorOrigin)) return;
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("action", out var action)) return;
                switch (action.GetString())
                {
                    case "ready": browserReady.TrySetResult(true); projectResourceReady?.TrySetResult(true); break;
                    case "dirty":
                        if (currentPath != null && (!doc.RootElement.TryGetProperty("path", out var dirtyPath) || dirtyPath.GetString() == currentPath))
                            SetDirty(!doc.RootElement.TryGetProperty("dirty", out var changed) || changed.GetBoolean());
                        break;
                    case "view":
                        ApplyWorkspaceState(doc.RootElement);
                        break;
                    case "preview-request":
                        if (!busy && !changingWorkspace && !(doc.RootElement.TryGetProperty("reason", out var reason) && reason.GetString() == "load") && doc.RootElement.TryGetProperty("view", out var requestedView))
                            await SwitchWorkspaceAsync(requestedView.GetString() == "browser" ? "public" : "desktop");
                        break;
                    case "search-replace":
                        await RunSafeAsync(ShowSearchReplaceAsync);
                        break;
                    case "equation-dialog":
                    case "editor-dialog":
                        await RefreshEditorStateAsync();
                        status.Text = editorDialogOpen ? DialogNotice : currentPath ?? "Ready";
                        break;
                    case "open-link":
                        if (doc.RootElement.TryGetProperty("href", out var href) && href.GetString() is string destination)
                            await RunLinkOpenAsync(destination, !doc.RootElement.TryGetProperty("newWindow", out var newWindow) || newWindow.GetBoolean());
                        break;
                    case "save":
                        if (doc.RootElement.TryGetProperty("path", out var page) && page.GetString() is string path && path != currentPath) return;
                        await RunSafeAsync(SaveCurrentTopicAsync);
                        break;
                }
            }
            catch (Exception ex) { ShowError(ex); }
        };
        await core.AddScriptToExecuteOnDocumentCreatedAsync(GeneratedPreviewLinkBridge);
        MapInitialProjectResources();
        core.Navigate(EditorOrigin + "/index.html");
        await browserReady.Task.WaitAsync(TimeSpan.FromSeconds(45));
        await editor.ExecuteScriptAsync("window.HelpEditor.setUnifiedViews(true)");
    }

    public async Task OpenProjectAsync(string root)
    {
        await RequireEquationFinishedAsync();
        var opened = HelpProject.Open(root);
        if (!File.Exists(Path.Combine(opened.RootPath, ".statsdirect-help-editor.json"))) throw new InvalidOperationException("Open a marked working copy, not the original Flare project.");
        // Validate the new copy before rebinding the editor or clearing any dirty
        // state. A broken TOC/topic in another copy must leave this page intact.
        var (newContents, newContentsSource) = ReadContentsSnapshot(opened);
        var newTopics = opened.ListTopics();
        // Validate optional design settings before replacing the active copy.
        _ = opened.GetAppearance(HelpBuildTarget.Desktop);
        _ = opened.GetAppearance(HelpBuildTarget.Public);
        string? first = Flatten(newContents).Where(n => !string.IsNullOrWhiteSpace(n.Link))
            .Select(n => TopicPath(n.Link!)).FirstOrDefault(p => newTopics.Any(t => t.Path.Equals(p, StringComparison.OrdinalIgnoreCase)));
        first ??= newTopics.FirstOrDefault()?.Path;
        await browserReady.Task;
        await EnsureProjectResourcesAsync(opened);
        project = opened;
        ResetWorkspaceProject();
        await UpdateSharedFootersAsync();
        currentPath = null;
        loadedDiskSource = null;
        loadedContentsSource = newContentsSource;
        SetDirty(false);
        copyLabel.Text = "WORKING COPY   " + opened.RootPath;
        toc = newContents;
        allTopics = newTopics;
        PopulateContents(null);
        RefreshPageList();
        if (first != null) await OpenTopicAsync(first);
        else await ShowEmptyAsync();
        await UpdateGitBadgeAsync();
        status.Text = $"Working copy open · {allTopics.Count} pages · Contents changes save automatically";
        UpdateWorkspaceCaption();
    }

    private async Task CloneGitProjectAsync()
    {
        if (!await ConfirmLeaveAsync()) return;
        var choice = PromptClone();
        if (choice == null) return;
        status.Text = "Cloning the GitHub project into a new working copy…";
        var cloned = await GitSyncService.CloneAsync(choice.Url, choice.Destination);
        string branch = DefaultWorkingBranch();
        await new GitSyncService(cloned).CreateBranchAsync(branch);
        await OpenProjectAsync(cloned.RootPath);
        status.Text = $"Cloned project · working branch {branch} · the previous working source was not imported or changed";
    }

    private async Task ReviewGitStatusAsync()
    {
        if (project == null) return;
        GitStatus result;
        try { result = await new GitSyncService(project).StatusAsync(); }
        catch (Exception ex) { ShowGitStatusFailure(ex.Message); throw; }
        ApplyGitBadge(result);
        ShowGitStatus(result);
    }

    private async Task<GitSyncService> RequireGitAsync()
    {
        var service = new GitSyncService(CurrentProject);
        var result = await service.StatusAsync();
        if (!result.IsRepository)
            throw new InvalidOperationException("This is a standalone working copy. Use GitHub > Clone project to create a separate GitHub working copy. The current source is not attached or uploaded automatically.");
        return service;
    }

    private async Task FetchGitAsync()
    {
        if (project == null) return;
        var service = await RequireGitAsync();
        status.Text = "Fetching GitHub updates…";
        await service.FetchAsync();
        var result = await service.StatusAsync();
        ApplyGitBadge(result);
        status.Text = $"Fetched updates · {result.Branch} · " + GitComparisonText(result);
    }

    private async Task PullGitAsync()
    {
        if (project == null || !await ConfirmLeaveAsync()) return;
        var service = await RequireGitAsync();
        string root = project.RootPath;
        string? page = currentPath;
        status.Text = "Pulling fast-forward updates…";
        await service.PullAsync();
        await OpenProjectAsync(root);
        if (page != null && allTopics.Any(t => t.Path.Equals(page, StringComparison.OrdinalIgnoreCase))) await OpenTopicAsync(page);
        status.Text = "Fast-forward pull complete · working copy reloaded";
    }

    private async Task CreateGitBranchAsync()
    {
        if (project == null || !await ConfirmLeaveAsync()) return;
        var service = await RequireGitAsync();
        string? branch = Prompt("New working branch", "Branch name", DefaultWorkingBranch());
        if (branch == null) return;
        await service.CreateBranchAsync(branch);
        await UpdateGitBadgeAsync();
        status.Text = "Created working branch " + branch;
    }

    private async Task CommitGitAsync()
    {
        if (project == null || !await ConfirmLeaveAsync()) return;
        var service = await RequireGitAsync();
        status.Text = "Rebuilding the GitHub website from saved help source…";
        var website = await service.PrepareWebsiteAsync(Path.Combine(AppContext.BaseDirectory, "Web"));
        var gitStatus = await service.StatusAsync();
        ApplyGitBadge(gitStatus);
        var choice = PromptCommit(gitStatus, website, await service.ReadAuthorIdentityAsync());
        if (choice == null) { status.Text = "Website rebuilt locally · nothing committed or pushed"; return; }
        status.Text = "Committing saved help source and regenerated website…";
        string committed = await service.CommitWebsiteAsync(choice.Message, choice.AuthorName, choice.AuthorEmail);
        await UpdateGitBadgeAsync();
        status.Text = committed + " · use GitHub > Push branch when ready";
    }

    private async Task PushGitAsync()
    {
        if (project == null || !await ConfirmLeaveAsync()) return;
        var service = await RequireGitAsync();
        status.Text = "Pushing the working branch…";
        await service.PushWebsiteAsync();
        await UpdateGitBadgeAsync();
        status.Text = "Source and rebuilt website pushed for review · merging to master triggers GitHub Pages deployment";
    }

    private async Task UpdateGitBadgeAsync()
    {
        if (project == null) return;
        try { ApplyGitBadge(await new GitSyncService(project).StatusAsync()); }
        catch (Exception ex) { copyLabel.Text = "WORKING COPY   " + project.RootPath; ShowGitStatusFailure(ex.Message); }
    }

    private void ApplyGitBadge(GitStatus gitStatus)
    {
        copyLabel.Text = "WORKING COPY   " + CurrentProject.RootPath + (gitStatus.IsRepository ? "   •   branch: " + gitStatus.Branch : "   •   standalone source copy");
        lastGitStatus = gitStatus;
        gitStatusStale = false;
        gitStatusError = null;
        UpdateGitStatusBadge();
    }

    private void ShowGitStatus(GitStatus gitStatus)
    {
        string text = GitStatusText(gitStatus);
        using var dialog = new Form { Text = "GitHub status / review changes", AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96), Size = new Size(900, 640), StartPosition = FormStartPosition.CenterParent, Font = Font, MinimizeBox = false, MaximizeBox = true };
        var view = new TextBox { Text = text, Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new Font("Consolas", 10), BackColor = SystemColors.Window };
        var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 38 };
        dialog.Controls.Add(view);
        dialog.Controls.Add(close);
        dialog.AcceptButton = close;
        dialog.CancelButton = close;
        dialog.ShowDialog(this);
    }

    private static string DefaultWorkingBranch() => "help-editor/" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
    private static string DisplayRemote(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote)) return "(none configured)";
        if (!Uri.TryCreate(remote, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return remote;
        return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.ToString();
    }

    private sealed record CloneChoice(string Url, string Destination);
    private CloneChoice? PromptClone()
    {
        using var dialog = new Form { Text = "Clone GitHub help project", AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96), Size = new Size(790, 310), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, Font = Font };
        var urlLabel = new Label { Text = "GitHub HTTPS repository URL", Left = 16, Top = 16, Width = 740, Height = 24 };
        var url = new TextBox { Left = 16, Top = 43, Width = 740, Text = "https://github.com/iain-buchan/statisticalhelp.git" };
        var destinationLabel = new Label { Text = "New working-copy folder", Left = 16, Top = 81, Width = 740, Height = 24 };
        var destination = new TextBox { Left = 16, Top = 108, Width = 740, Text = Path.Combine(artifactRoot, "WorkingCopies", "statisticalhelp-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")) };
        var note = new Label { Left = 16, Top = 148, Width = 740, Height = 48, Text = "This opens the GitHub version on a new help-editor working branch. Your current working source is kept separately; its edits are not imported or overwritten." };
        var clone = new Button { Text = "Clone project", Left = 534, Top = 211, Width = 120, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 666, Top = 211, Width = 90, DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange([urlLabel, url, destinationLabel, destination, note, clone, cancel]);
        dialog.AcceptButton = clone;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        if (string.IsNullOrWhiteSpace(url.Text) || string.IsNullOrWhiteSpace(destination.Text)) throw new ArgumentException("Enter a repository URL and a new destination folder.");
        return new CloneChoice(url.Text.Trim(), Path.GetFullPath(destination.Text.Trim()));
    }

    private sealed record CommitChoice(string Message, string AuthorName, string AuthorEmail);
    private CommitChoice? PromptCommit(GitStatus gitStatus, WebsitePublication website, GitAuthorIdentity identity)
    {
        using var dialog = new Form { Text = "Commit help source and rebuilt website", AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96), Size = new Size(720, 520), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, Font = Font };
        var branch = new Label { Left = 16, Top = 15, Width = 670, Height = 44, Text = $"Branch: {gitStatus.Branch}\r\nSaved source and regenerated Docs are committed together. Push is separate." };
        var messageLabel = new Label { Left = 16, Top = 68, Width = 670, Height = 24, Text = "Commit message" };
        var message = new TextBox { Left = 16, Top = 95, Width = 670, Height = 90, Multiline = true, ScrollBars = ScrollBars.Vertical };
        var nameLabel = new Label { Left = 16, Top = 201, Width = 310, Height = 24, Text = "Author / committer name" };
        var name = new TextBox { Left = 16, Top = 228, Width = 310, Text = identity.Name, PlaceholderText = "Your Git commit name" };
        var emailLabel = new Label { Left = 346, Top = 201, Width = 340, Height = 24, Text = "Email" };
        var email = new TextBox { Left = 346, Top = 228, Width = 340, Text = identity.Email, PlaceholderText = "Your Git commit email" };
        var buildReview = new TextBox { Left = 16, Top = 276, Width = 670, Height = 105, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window,
            Text = $"Website rebuilt: {website.TopicCount} pages; {website.SearchDocumentCount} searchable pages; {website.Warnings.Count} warnings.\r\n" +
                (website.Warnings.Count == 0 ? "Contents, search, artwork and equations are included. Merging the reviewed branch to master triggers GitHub Pages deployment." : string.Join("\r\n", website.Warnings)) };
        var commit = new Button { Left = 470, Top = 402, Width = 120, Text = "Commit locally", DialogResult = DialogResult.OK };
        var cancel = new Button { Left = 602, Top = 402, Width = 84, Text = "Cancel", DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange([branch, messageLabel, message, nameLabel, name, emailLabel, email, buildReview, commit, cancel]);
        dialog.AcceptButton = commit;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        if (string.IsNullOrWhiteSpace(message.Text) || string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(email.Text)) throw new ArgumentException("Enter a commit message, name and email.");
        return new CommitChoice(message.Text.Trim(), name.Text.Trim(), email.Text.Trim());
    }

    public async Task OpenTopicAsync(string path)
    {
        try
        {
            string? fragment = null;
            int hash = path.IndexOf('#');
            if (hash >= 0) { fragment = path[(hash + 1)..]; path = path[..hash]; }
            path = path.Split('?')[0];
            await browserReady.Task;
            await RequireEquationFinishedAsync();
            string source = CurrentProject.ReadTopic(path);
            string title = allTopics.FirstOrDefault(t => t.Path.Equals(path, StringComparison.OrdinalIgnoreCase))?.Title ?? Path.GetFileNameWithoutExtension(path);
            string payload = JsonSerializer.Serialize(new { path, title, source, fragment, topics = TopicCatalog(), target = target.SelectedIndex == 1 ? "Public" : "Desktop", appearance = CurrentProject.GetAppearance(SelectedBuildTarget), baseUrl = ProjectOrigin + "/" + string.Join("/", path.Split('/').Select(Uri.EscapeDataString)) });
            await editor.ExecuteScriptAsync("window.HelpEditor.load(" + payload + ")");
            currentPath = path;
            currentTopicFragment = fragment;
            loadedDiskSource = source;
            SetDirty(false);
            // Read the accepted view directly: load emits intermediate view
            // notifications before restoring Browser/App for the new page.
            await RefreshEditorStateAsync();
            SelectCurrentPage();
            Text = $"{title} — StatsDirect Help Editor (prototype)";
            status.Text = path;
            UpdateWorkspaceCaption();
            await RefreshVisibleOutputPreviewsAsync();
        }
        catch { SelectCurrentPage(); throw; }
    }

    public async Task SaveCurrentTopicAsync()
    {
        if (currentPath == null || project == null) return;
        var sourceProject = project;
        string path = currentPath;
        await browserReady.Task;
        await RequireEquationFinishedAsync();
        string encoded = await editor.ExecuteScriptAsync("window.HelpEditor.getSource()");
        string source = JsonSerializer.Deserialize<string>(encoded) ?? throw new InvalidDataException("The page editor did not return source HTML.");
        if (sourceProject != project || path != currentPath) throw new InvalidOperationException("The selected page or working copy changed while saving; try Save again.");
        // The renderer is asynchronous. Read the disk snapshot after it returns
        // so an edit made in another window during that wait is not overwritten.
        string onDisk = sourceProject.ReadTopic(path);
        if (source == onDisk)
        {
            loadedDiskSource = source;
            await editor.ExecuteScriptAsync("window.HelpEditor.markSaved()");
            SetDirty(false);
            status.Text = "Page already saved · no files or previews changed";
            return;
        }
        if (loadedDiskSource != null && onDisk != loadedDiskSource)
            throw new InvalidOperationException("This page was changed on disk by another editor. Your unsaved changes are still open. Copy them from HTML source, then use File > Reload page from disk to review and merge with the newer file before saving.");
        sourceProject.SaveTopic(path, source);
        MarkSavedSourceChanged();
        loadedDiskSource = source;
        await editor.ExecuteScriptAsync("window.HelpEditor.markSaved()");
        SetDirty(false);
        allTopics = project.ListTopics();
        await UpdateTopicCatalogAsync();
        RefreshPageList();
        SelectCurrentPage();
        string title = allTopics.FirstOrDefault(topic => topic.Path.Equals(path, StringComparison.OrdinalIgnoreCase))?.Title ?? Path.GetFileNameWithoutExtension(path);
        Text = $"{title} — StatsDirect Help Editor (prototype)";
        status.Text = "Saved " + path + " · current-page previews use your latest edits";
        await RefreshVisibleOutputPreviewsAsync();
    }

    private async Task ReloadPageAsync()
    {
        if (currentPath == null || !await ConfirmLeaveAsync()) return;
        await OpenTopicAsync(currentPath);
        status.Text = "Reloaded " + currentPath + " from disk";
    }

    public async Task<MainForm?> OpenLinkAsync(string href, bool newWindow = true)
    {
        href = SourceLinkFromGeneratedPreview(href);
        await RefreshEditorStateAsync();
        // Looking at a target in an independent window does not discard a pending
        // link draft. Other dialogs, or navigation in this window, remain modal.
        if (editorDialogOpen && (!newWindow || editorDialogKind != "link")) throw new InvalidOperationException(DialogNotice);
        var link = HelpLinkResolver.Resolve(CurrentProject, currentPath, href);
        if (link.ExternalUri != null)
        {
            externalLinkLauncher(link.ExternalUri);
            status.Text = "Opened web link in the system browser";
            return null;
        }
        string reference = link.TopicPath! + (link.Fragment == null ? "" : "#" + Uri.EscapeDataString(link.Fragment));
        if (!newWindow)
        {
            if (!await ConfirmLeaveAsync()) return null;
            await OpenTopicAsync(reference);
            return this;
        }
        var opened = new MainForm(CurrentProject.RootPath, externalLinkLauncher, reference, browserPreviewLauncher)
        {
            SuppressDialogs = SuppressDialogs,
            StartPosition = FormStartPosition.Manual,
            Size = Size,
            Location = Location,
            WindowState = WindowState,
            ShowInTaskbar = ShowInTaskbar
        };
        EditorApplicationContext.ShowEditor(opened); // Deliberately no Owner.
        if (WindowState == FormWindowState.Normal) opened.Bounds = Bounds;
        try { await opened.Initialization; }
        catch
        {
            // Initialization is completed before authoring is enabled.
            opened.closingApproved = true;
            opened.Close();
            throw;
        }
        status.Text = "Opened " + link.TopicPath + " in a separate editor window";
        return opened;
    }

    private async Task RunLinkOpenAsync(string href, bool newWindow)
    {
        if (busy) return;
        SetBusy(true);
        try { await OpenLinkAsync(href, newWindow); }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private static void RemoveBrowserNavigation(IList<CoreWebView2ContextMenuItem> items)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            string name = items[i].Name.ToLowerInvariant();
            if (name is "reload" or "refresh" or "back" or "forward" or "goback" or "goforward") items.RemoveAt(i);
            else if (items[i].Kind == CoreWebView2ContextMenuItemKind.Submenu) RemoveBrowserNavigation(items[i].Children);
        }
    }

    public async Task BuildHelpAsync() => await BuildHelpInternalAsync();

    private async Task<bool> BuildHelpInternalAsync()
    {
        if (project == null) return false;
        await RequireEquationFinishedAsync();
        if (dirty && !await ConfirmLeaveAsync()) return false;
        bool ownBusy = !busy;
        if (ownBusy) SetBusy(true);
        try
        {
            HelpBuildTarget buildTarget = target.SelectedIndex == 1 ? HelpBuildTarget.Public : HelpBuildTarget.Desktop;
            string output = Path.Combine(artifactRoot, "Build", buildTarget.ToString());
            status.Text = "Rebuilding help, navigation and search…";
            var result = await Task.Run(() => HelpBuilder.Build(project, output, Path.Combine(AppContext.BaseDirectory, "Web"), buildTarget));
            LastBuildResult = result;
            builtRevisions[buildTarget] = sourceRevision;
            staleDesignTargets.Remove(buildTarget);
            UpdateWorkspaceCaption();
            status.Text = $"Built {result.TopicCount} pages · indexed {result.SearchDocumentCount} pages · {result.Warnings.Count} warnings · {result.OutputDirectory}";
            if (result.Warnings.Count != 0 && !SuppressDialogs)
                MessageBox.Show(this, string.Join(Environment.NewLine, result.Warnings.Take(12)) + (result.Warnings.Count > 12 ? $"\n\n…and {result.Warnings.Count - 12} more. See the build’s validation report." : ""), "Help built with warnings", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        finally { if (ownBusy) SetBusy(false); }
    }

    private async Task BuildAndPreviewAsync()
    {
        if (!await BuildHelpInternalAsync()) return;
        string output = Path.Combine(artifactRoot, "Build", target.SelectedIndex == 1 ? "Public" : "Desktop");
        if (!File.Exists(Path.Combine(output, "index.html"))) return;
        new PreviewForm(output, artifactRoot).Show(this);
    }

    public async Task PreviewSiteInBrowserAsync()
    {
        if (!await BuildHelpInternalAsync()) return;
        string home = Path.Combine(LastBuildResult!.OutputDirectory, "index.html");
        if (!File.Exists(home)) throw new FileNotFoundException("The rebuilt help home page is missing.", home);
        browserPreviewLauncher(new Uri(Path.GetFullPath(home)));
        status.Text = "Opened the rebuilt help site in your default browser · Contents and search are available";
    }

    private async Task PickProjectAsync()
    {
        if (!await ConfirmLeaveAsync()) return;
        using var picker = new FolderBrowserDialog { Description = "Select a marked help working copy", UseDescriptionForTitle = true, SelectedPath = project?.RootPath ?? initialProject };
        if (picker.ShowDialog(this) == DialogResult.OK) await OpenProjectAsync(picker.SelectedPath);
    }

    private async Task NewPageAsync()
    {
        if (project == null || !await ConfirmLeaveAsync()) return;
        string? title = Prompt("New page", "Page title", "New help topic");
        if (title == null) return;
        string defaultFolder = currentPath?.Contains('/') == true ? currentPath[..currentPath.LastIndexOf('/')] : "basics";
        string? folder = Prompt("New page", "Folder inside Content (for example basics)", defaultFolder);
        if (folder == null) return;
        await CreateTopicInContentsAsync(title, folder);
    }

    private async Task CreateTopicInContentsAsync(string title, string folder)
    {
        string path = CurrentProject.CreateTopic(title, folder);
        try
        {
            var node = new TocNode { Title = title, Link = path };
            InsertAtSelection(node);
            SaveTocAndRefresh(node.Id);
        }
        catch (Exception ex)
        {
            allTopics = CurrentProject.ListTopics();
            await UpdateTopicCatalogAsync();
            RefreshPageList();
            throw new InvalidOperationException($"Created {path}, but could not add it to Contents. The new file is available in All pages. {ex.Message}", ex);
        }
        allTopics = CurrentProject.ListTopics();
        await UpdateTopicCatalogAsync();
        RefreshPageList();
        await OpenTopicAsync(path);
    }

    private async Task DeletePageAsync()
    {
        if (project == null || currentPath == null || !await ConfirmLeaveAsync()) return;
        string path = currentPath;
        var references = project.FindReferences(path);
        string details = references.Count == 0 ? "" : "\n\nReferences found:\n" + string.Join("\n", references.Take(6)) + (references.Count > 6 ? "\n…" : "");
        if (MessageBox.Show(this, $"Move {path} to Deleted pages?\n\nIts Contents entries will be removed. You can restore the page through File > Deleted pages. Deletion is blocked if another page or an application context ID still needs it.{details}", "Delete page", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        project.DeleteTopic(path);
        MarkSavedSourceChanged();
        currentPath = null;
        loadedDiskSource = null;
        SetDirty(false);
        await ReloadProjectLists();
        string? next = Flatten(toc).Select(n => n.Link).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (next != null) await OpenTopicAsync(TopicPath(next));
        else await ShowEmptyAsync();
        status.Text = "Moved " + path + " to Deleted pages · File > Deleted pages to restore · rebuild help to update search";
    }

    private Task AddSectionAsync()
    {
        if (project == null) return Task.CompletedTask;
        string? title = Prompt("New contents section", "Section title", "New section");
        if (title == null) return Task.CompletedTask;
        var node = new TocNode { Title = title };
        InsertAtSelection(node);
        SaveTocAndRefresh(node.Id);
        return Task.CompletedTask;
    }

    private async Task AddExistingPageAsync()
    {
        if (project == null || !await ConfirmLeaveAsync()) return;
        string? path = PickTopic("Add page to Contents", currentPath, false);
        if (path == null) return;
        var node = new TocNode { Title = allTopics.First(t => t.Path == path).Title, Link = path };
        InsertAtSelection(node);
        SaveTocAndRefresh(node.Id);
        await OpenTopicAsync(path);
    }

    private async Task ChangeContentsLinkAsync()
    {
        if (project == null || contents.SelectedNode?.Tag is not TocNode node || !await ConfirmLeaveAsync()) return;
        string? path = PickTopic("Contents destination", node.Link == null ? null : TopicPath(node.Link), true);
        if (path == null) return;
        node.Link = path.Length == 0 ? null : path;
        SaveTocAndRefresh(node.Id);
        if (path.Length != 0) await OpenTopicAsync(path);
    }

    private Task RenameContentsAsync()
    {
        if (contents.SelectedNode?.Tag is not TocNode node) return Task.CompletedTask;
        string? title = Prompt("Rename contents entry", "Contents title (the page filename is unchanged)", node.Title);
        if (title == null) return Task.CompletedTask;
        node.Title = title;
        SaveTocAndRefresh(node.Id);
        return Task.CompletedTask;
    }

    private Task MoveContentsAsync(int direction)
    {
        if (contents.SelectedNode?.Tag is not TocNode node || FindLocation(toc, node.Id) is not { } location) return Task.CompletedTask;
        int next = location.Index + direction;
        if (next < 0 || next >= location.Siblings.Count) return Task.CompletedTask;
        location.Siblings.RemoveAt(location.Index);
        location.Siblings.Insert(next, node);
        SaveTocAndRefresh(node.Id);
        return Task.CompletedTask;
    }

    private Task IndentContentsAsync()
    {
        if (contents.SelectedNode?.Tag is not TocNode node || FindLocation(toc, node.Id) is not { Index: > 0 } location) return Task.CompletedTask;
        TocNode previous = location.Siblings[location.Index - 1];
        location.Siblings.RemoveAt(location.Index);
        previous.Children.Add(node);
        SaveTocAndRefresh(node.Id);
        return Task.CompletedTask;
    }

    private Task OutdentContentsAsync()
    {
        if (contents.SelectedNode?.Tag is not TocNode node || FindLocation(toc, node.Id) is not { Parent: { } parent } location || FindLocation(toc, parent.Id) is not { } parentLocation) return Task.CompletedTask;
        location.Siblings.RemoveAt(location.Index);
        parentLocation.Siblings.Insert(parentLocation.Index + 1, node);
        SaveTocAndRefresh(node.Id);
        return Task.CompletedTask;
    }

    private Task RemoveContentsAsync()
    {
        if (contents.SelectedNode?.Tag is not TocNode node || FindLocation(toc, node.Id) is not { } location) return Task.CompletedTask;
        if (MessageBox.Show(this, $"Remove “{node.Title}”{(node.Children.Count > 0 ? " and its child entries" : "")} from Contents?\n\nThe page files will remain in the working copy.", "Remove contents entry", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return Task.CompletedTask;
        location.Siblings.RemoveAt(location.Index);
        SaveTocAndRefresh(null);
        return Task.CompletedTask;
    }

    private void InsertAtSelection(TocNode node)
    {
        if (contents.SelectedNode?.Tag is TocNode selected && FindLocation(toc, selected.Id) is { } location)
        {
            if (string.IsNullOrWhiteSpace(selected.Link)) selected.Children.Add(node);
            else location.Siblings.Insert(location.Index + 1, node);
        }
        else toc.Add(node);
    }

    private void SaveTocAndRefresh(string? selectedId)
    {
        try
        {
            string onDisk = File.ReadAllText(ContentsPath(CurrentProject));
            if (loadedContentsSource != null && onDisk != loadedContentsSource)
                throw new InvalidOperationException("Contents were changed on disk by another editor. The newer contents have been reloaded; review them and try your change again.");
            CurrentProject.SaveContents(toc);
            loadedContentsSource = File.ReadAllText(ContentsPath(CurrentProject));
            MarkSavedSourceChanged();
        }
        catch
        {
            try { (toc, loadedContentsSource) = ReadContentsSnapshot(CurrentProject); PopulateContents(null); }
            catch { /* Keep the saved disk file untouched if it cannot be reloaded. */ }
            throw;
        }
        PopulateContents(selectedId);
        status.Text = "Contents saved · rebuild help to refresh navigation";
    }

    private async Task ReloadProjectLists()
    {
        (toc, loadedContentsSource) = ReadContentsSnapshot(CurrentProject);
        allTopics = CurrentProject.ListTopics();
        PopulateContents(null);
        RefreshPageList();
        await UpdateTopicCatalogAsync();
    }

    private static string ContentsPath(HelpProject opened) => Path.Combine(opened.RootPath, "Project", "TOCs", "STATSDIRECT.fltoc");
    private static (List<TocNode> Nodes, string Source) ReadContentsSnapshot(HelpProject opened)
    {
        string source = File.ReadAllText(ContentsPath(opened));
        var nodes = opened.LoadContents();
        if (File.ReadAllText(ContentsPath(opened)) != source)
            throw new InvalidOperationException("Contents changed while they were being read. Please open the working copy again.");
        return (nodes, source);
    }

    private void PopulateContents(string? selectedId)
    {
        changingSelection = true;
        contents.BeginUpdate();
        try
        {
            var expanded = GetTreeNodes(contents.Nodes).Where(n => n.IsExpanded).Select(n => ((TocNode)n.Tag!).Id).ToHashSet();
            contents.Nodes.Clear();
            void Add(TreeNodeCollection nodes, IEnumerable<TocNode> models)
            {
                foreach (var model in models)
                {
                    var node = new TreeNode(model.Title) { Tag = model, ToolTipText = model.Link ?? "Contents section" };
                    nodes.Add(node);
                    Add(node.Nodes, model.Children);
                    if (expanded.Contains(model.Id)) node.Expand();
                    if (model.Id == selectedId) { contents.SelectedNode = node; node.EnsureVisible(); }
                }
            }
            Add(contents.Nodes, toc);
            if (selectedId == null) SelectCurrentTreeNode();
        }
        finally { contents.EndUpdate(); changingSelection = false; }
    }

    private void RefreshPageList()
    {
        bool wasChanging = changingSelection;
        changingSelection = true;
        pages.BeginUpdate();
        try
        {
            string filter = pageFilter.Text.Trim();
            pages.Items.Clear();
            pages.Items.AddRange(allTopics.Where(t => filter.Length == 0 || t.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase) || t.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)).Cast<object>().ToArray());
            for (int i = 0; i < pages.Items.Count; i++)
                if (((TopicInfo)pages.Items[i]).Path == currentPath) { pages.SelectedIndex = i; break; }
        }
        finally { pages.EndUpdate(); changingSelection = wasChanging; }
    }

    private void SelectCurrentPage()
    {
        bool wasChanging = changingSelection;
        changingSelection = true;
        try { SelectCurrentTreeNode(); RefreshPageList(); }
        finally { changingSelection = wasChanging; }
    }

    private void SelectCurrentTreeNode()
    {
        var candidates = GetTreeNodes(contents.Nodes).Where(n => n.Tag is TocNode t && t.Link != null && TopicPath(t.Link).Equals(currentPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        TreeNode? node = candidates.FirstOrDefault(n => n.Tag is TocNode t &&
            (t.Link!.Contains('#') ? t.Link[(t.Link.IndexOf('#') + 1)..] : null) == currentTopicFragment) ?? candidates.FirstOrDefault();
        if (node == null) { contents.SelectedNode = null; return; }
        contents.SelectedNode = node;
        node.EnsureVisible();
    }

    private async Task ShowEmptyAsync()
    {
        await editor.ExecuteScriptAsync("window.HelpEditor.load(" + JsonSerializer.Serialize(new { path = "", title = "No page selected", source = "<html><head><title>No page selected</title></head><body><h1>No page selected</h1><p>Choose a page or create a new one.</p></body></html>", topics = TopicCatalog(), target = target.SelectedIndex == 1 ? "Public" : "Desktop", appearance = CurrentProject.GetAppearance(SelectedBuildTarget), baseUrl = ProjectOrigin + "/" }) + ")");
    }

    private object[] TopicCatalog() => allTopics.Select(t => (object)new { path = t.Path, title = t.Title }).ToArray();
    private async Task UpdateTopicCatalogAsync()
    {
        if (!IsEditorReady || editor.IsDisposed) return;
        try { await editor.ExecuteScriptAsync("window.HelpEditor.setTopics(" + JsonSerializer.Serialize(TopicCatalog()) + ")"); }
        catch (Exception ex) { if (!IsDisposed) { LastError = ex.ToString(); status.Text = "Could not refresh the page-link list: " + ex.Message; } }
    }

    private async Task<bool> ConfirmLeaveAsync()
    {
        if (!await AllowNativeActionAsync()) return false;
        if (!dirty || currentPath == null) return true;
        var response = MessageBox.Show(this, $"Save changes to {currentPath}?", "Unsaved page", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (response == DialogResult.Cancel) return false;
        if (response == DialogResult.No) return true;
        try { await SaveCurrentTopicAsync(); return true; }
        catch (Exception ex) { ShowError(ex); return false; }
    }

    private async Task RunSafeAsync(Func<Task> action)
    {
        if (busy) return;
        // Claim the command before the first renderer await. Two fast clicks
        // must not both pass the idle check while its state query is pending.
        SetBusy(true);
        try { if (await AllowNativeActionAsync()) await action(); }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        ApplyInteractionState();
        progress.Visible = value;
        UseWaitCursor = value;
    }

    private void ApplyInteractionState()
    {
        bool enabled = !busy && !editorDialogOpen;
        target.Enabled = enabled;
        contentsBar.Enabled = enabled;
        contents.Enabled = enabled;
        pages.Enabled = enabled;
        pageFilter.Enabled = enabled;
        if (MainMenuStrip != null) MainMenuStrip.Enabled = enabled;
        // The native shell acts modally while an editor dialog is open, but its
        // Apply and Cancel buttons must remain usable inside the WebView.
        editor.Enabled = !busy;
        designBar.Enabled = enabled;
        gitBadge.Enabled = enabled;
    }

    private async Task RefreshEditorStateAsync()
    {
        if (!IsEditorReady || editor.CoreWebView2 == null) return;
        string snapshot = await editor.ExecuteScriptAsync("window.HelpEditor.getState()");
        using var state = JsonDocument.Parse(snapshot);
        if (state.RootElement.ValueKind != JsonValueKind.Object) return;
        if (state.RootElement.TryGetProperty("dialogOpen", out var modal))
        {
            editorDialogOpen = modal.GetBoolean();
            editorDialogKind = state.RootElement.TryGetProperty("dialogKind", out var kind) ? kind.GetString() : null;
        }
        else if (state.RootElement.TryGetProperty("equationDialogOpen", out var equation))
        {
            editorDialogOpen = equation.GetBoolean();
            editorDialogKind = editorDialogOpen ? "equation" : null;
        }
        if (currentPath != null && state.RootElement.TryGetProperty("path", out var path) && path.GetString() == currentPath && state.RootElement.TryGetProperty("dirty", out var changed))
            SetDirty(changed.GetBoolean());
        ApplyWorkspaceState(state.RootElement);
        ApplyInteractionState();
    }

    private async Task<bool> AllowNativeActionAsync()
    {
        await RefreshEditorStateAsync();
        if (!editorDialogOpen) return true;
        status.Text = DialogNotice;
        SelectCurrentPage();
        editor.Focus();
        return false;
    }

    private async Task RequireEquationFinishedAsync()
    {
        if (!await AllowNativeActionAsync()) throw new InvalidOperationException(DialogNotice);
    }

    private void SetDirty(bool value) { dirty = value; unsaved.Text = value ? "Unsaved changes" : ""; UpdateWorkspaceCaption(); UpdateGitStatusBadge(); }
    private void ShowError(Exception ex) { LastError = ex.ToString(); status.Text = ex.Message; if (!SuppressDialogs) MessageBox.Show(this, ex.Message, "StatsDirect Help Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    private static string TopicPath(string link)
    {
        string path = link.Split('#', '?')[0].Replace('\\', '/').TrimStart('/');
        return path.StartsWith("Content/", StringComparison.OrdinalIgnoreCase) ? path[8..] : path;
    }
    private static bool IsOrigin(string uri, string origin) => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.GetLeftPart(UriPartial.Authority).Equals(origin, StringComparison.OrdinalIgnoreCase);
    private static IEnumerable<TocNode> Flatten(IEnumerable<TocNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));
    private static IEnumerable<TreeNode> GetTreeNodes(TreeNodeCollection nodes) => nodes.Cast<TreeNode>().SelectMany(n => new[] { n }.Concat(GetTreeNodes(n.Nodes)));
    private sealed record TocLocation(List<TocNode> Siblings, int Index, TocNode? Parent);
    private static TocLocation? FindLocation(List<TocNode> nodes, string id, TocNode? parent = null)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Id == id) return new TocLocation(nodes, i, parent);
            var found = FindLocation(nodes[i].Children, id, nodes[i]);
            if (found != null) return found;
        }
        return null;
    }

    private string? Prompt(string title, string label, string initial)
    {
        using var dialog = new Form { Text = title, AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96), Width = 540, Height = 195, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, Font = Font };
        var caption = new Label { Text = label, Left = 15, Top = 14, Width = 490, Height = 24 };
        var input = new TextBox { Text = initial, Left = 15, Top = 43, Width = 490 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 330, Top = 91, Width = 80, Height = 32 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 425, Top = 91, Width = 80, Height = 32 };
        dialog.Controls.AddRange([caption, input, ok, cancel]);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(input.Text) ? input.Text.Trim() : null;
    }

    private string? PickTopic(string title, string? initialPath, bool allowSection)
    {
        using var dialog = new Form { Text = title, AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96), Width = 760, Height = 520, MinimumSize = new Size(570, 350), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, Font = Font };
        var filter = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Find a page by title or filename…", Margin = new Padding(10) };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, FormattingEnabled = true };
        list.Format += (_, e) => { if (e.ListItem is TopicInfo t) e.Value = $"{t.Title}   ({t.Path})"; };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 85, Height = 32 };
        var ok = new Button { Text = "Use page", DialogResult = DialogResult.OK, Width = 95, Height = 32 };
        var section = new Button { Text = "Section only", DialogResult = DialogResult.Ignore, Width = 115, Height = 32, Visible = allowSection };
        buttons.Controls.AddRange([cancel, ok, section]);
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        panel.Controls.Add(list);
        panel.Controls.Add(filter);
        dialog.Controls.Add(panel);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        void Fill()
        {
            list.BeginUpdate();
            list.Items.Clear();
            string query = filter.Text.Trim();
            list.Items.AddRange(allTopics.Where(t => query.Length == 0 || t.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) || t.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).Cast<object>().ToArray());
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            list.EndUpdate();
            ok.Enabled = list.SelectedItem != null;
        }
        filter.TextChanged += (_, _) => Fill();
        list.SelectedIndexChanged += (_, _) => ok.Enabled = list.SelectedItem != null;
        list.DoubleClick += (_, _) => { if (list.SelectedItem != null) { dialog.DialogResult = DialogResult.OK; dialog.Close(); } };
        Fill();
        for (int i = 0; i < list.Items.Count; i++) if (((TopicInfo)list.Items[i]).Path == initialPath) { list.SelectedIndex = i; break; }
        dialog.Shown += (_, _) => filter.Focus();
        var result = dialog.ShowDialog(this);
        return result == DialogResult.Ignore ? "" : result == DialogResult.OK && list.SelectedItem is TopicInfo topic ? topic.Path : null;
    }

}

public sealed class PreviewForm : Form
{
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    public WebView2 PreviewWebView => browser;
    public PreviewForm(string root, string artifactRoot)
    {
        Text = "Built help preview — " + Path.GetFileName(root);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Size = new Size(1260, 900);
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        Controls.Add(browser);
        Shown += async (_, _) =>
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(artifactRoot, ".preview-profile"));
                await browser.EnsureCoreWebView2Async(environment);
                var core = browser.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping("help-preview.invalid", root, CoreWebView2HostResourceAccessKind.DenyCors);
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsWebMessageEnabled = false;
                core.NavigationStarting += (_, e) => { if (!Local(e.Uri)) e.Cancel = true; };
                core.FrameNavigationStarting += (_, e) => { if (!Local(e.Uri) && e.Uri != "about:blank") e.Cancel = true; };
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, e) =>
                {
                    if (!Local(e.Request.Uri) && !e.Request.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && !e.Request.Uri.StartsWith("blob:https://help-preview.invalid", StringComparison.OrdinalIgnoreCase))
                        e.Response = environment.CreateWebResourceResponse(new MemoryStream([]), 403, "Offline preview", "Content-Type: text/plain");
                };
                core.Navigate("https://help-preview.invalid/index.html");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Help preview", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
    }
    private static bool Local(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.GetLeftPart(UriPartial.Authority).Equals("https://help-preview.invalid", StringComparison.OrdinalIgnoreCase);
}
