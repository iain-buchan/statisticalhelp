using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static class Program
{
    private static MainForm form = null!;
    private static readonly List<object> results = [];
    private static int passed, failed, uiExceptions;
    private static string fixture = "", testRoot = "", evidence = "";
    private static readonly List<CoreWebView2Frame> previewFrames = [];
    private static readonly List<CoreWebView2Frame> appFrames = [];

    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        testRoot = Path.Combine(root, "tests", "StatsDirectHelpEditor.WorkspaceWindowsChecks");
        fixture = Path.Combine(testRoot, "HelpSource");
        evidence = Path.Combine(root, "evidence");
        Directory.CreateDirectory(evidence);
        CreateFixture();
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => { uiExceptions++; Check(false, "Unhandled native workspace exception: " + e.Exception); };
        form = new MainForm(fixture, _ => throw new InvalidOperationException("Unexpected external link launch"), browserPreviewLauncher: _ => throw new InvalidOperationException("Unexpected browser launch"))
        { SuppressDialogs = true, WindowState = FormWindowState.Normal, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0), ShowInTaskbar = false, Size = new Size(1600, 1000) };
        form.Shown += async (_, _) =>
        {
            try { await RunChecks(); }
            catch (Exception ex) { Check(false, "Unexpected workspace check failure: " + ex); }
            finally
            {
                File.WriteAllText(Path.Combine(evidence, "workspace-windows-checks.json"), JsonSerializer.Serialize(new { passed, failed, results }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{passed} native workspace checks passed; {failed} failed");
                typeof(MainForm).GetField("closingApproved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
                form.Close();
            }
        };
        Application.Run(form);
        return failed == 0 ? 0 : 1;
    }

    private static async Task RunChecks()
    {
        await form.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
        form.EditorWebView.CoreWebView2.FrameCreated += (_, e) => TrackPreviewFrame(e.Frame);
        await form.OpenTopicAsync("basics/alpha.htm");
        Check(!form.Controls.OfType<ToolStrip>().Any(bar => bar is not MenuStrip && bar is not StatusStrip),
            "The native shell has no duplicate top command toolbar between its menu and working-copy label");
        var fileMenu = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item => (item.Text ?? "").Replace("&", "") == "File");
        ToolStripMenuItem FileCommand(string text) => fileMenu.DropDownItems.OfType<ToolStripMenuItem>()
            .Single(item => (item.Text ?? "").Replace("&", "") == text);
        var fileShortcuts = new[]
        {
            (Text: "Open working copy…", Shortcut: Keys.Control | Keys.O),
            (Text: "New page…", Shortcut: Keys.Control | Keys.N),
            (Text: "Save page", Shortcut: Keys.Control | Keys.S),
            (Text: "Build help and search", Shortcut: Keys.Control | Keys.B),
            (Text: "Preview site in browser", Shortcut: Keys.F5)
        };
        Check(fileShortcuts.All(command => FileCommand(command.Text).Enabled && FileCommand(command.Text).ShortcutKeys == command.Shortcut) &&
            FileCommand("Delete page…").Enabled && FileCommand("Deleted pages…").Enabled && FileCommand("Preview site in editor").Enabled,
            "File retains Open, New, Save, recovery, Build and Preview commands with their original keyboard shortcuts");
        var editMenu = form.MainMenuStrip.Items.OfType<ToolStripMenuItem>().Single(item => (item.Text ?? "").Replace("&", "") == "Edit");
        Check(editMenu.DropDownItems.OfType<ToolStripMenuItem>().Any(item => (item.Text ?? "").Replace("&", "") == "Polish current page with ChatGPT…" && item.Enabled) &&
            editMenu.DropDownItems.OfType<ToolStripMenuItem>().Any(item => item.ShortcutKeys == (Keys.Control | Keys.Shift | Keys.H)),
            "Edit retains ChatGPT polishing and the all-pages Find and Replace shortcut after toolbar removal");
        Check(Field<ToolStrip>("contentsBar").Visible && Field<ToolStrip>("contentsBar").Items.OfType<ToolStripButton>()
            .Any(button => button.Text == "Page +"), "The separate Contents editing tools remain available");
        string beforeMenuSave = form.CurrentProject.ReadTopic("basics/alpha.htm");
        string menuSaveDraft = beforeMenuSave.Replace("BASELINE_TOKEN", "MENU_SAVE_SENTINEL");
        await SetSource(menuSaveDraft);
        FileCommand("Save page").PerformClick();
        await WaitUntil(() => !Field<bool>("busy") && !form.IsDirty);
        Check(form.CurrentProject.ReadTopic("basics/alpha.htm") == menuSaveDraft.Replace(HelpSourceNamespaces.LegacyUri, HelpSourceNamespaces.CurrentUri),
            "File > Save page saves the edited buffer with the canonical authoring namespace");
        await SetSource(beforeMenuSave);
        FileCommand("Save page").PerformClick();
        await WaitUntil(() => !Field<bool>("busy") && !form.IsDirty);
        Check(form.CurrentProject.ReadTopic("basics/alpha.htm") == beforeMenuSave.Replace(HelpSourceNamespaces.LegacyUri, HelpSourceNamespaces.CurrentUri),
            "A second File > Save restores the fixture source with the canonical authoring namespace");
        var captionMetrics = Field<Label>("workspaceCaption");
        Check(captionMetrics.Height >= captionMetrics.PreferredHeight && captionMetrics.Height >= captionMetrics.Font.Height * 2 + captionMetrics.Padding.Vertical && form.EditorWebView.Parent!.Top >= captionMetrics.Bottom,
            "Both caption lines fit the actual DPI-scaled font above the editor", new { captionMetrics.DeviceDpi, captionMetrics.Height, captionMetrics.PreferredHeight, FontHeight = captionMetrics.Font.Height, captionMetrics.Bounds });
        var copyMetrics = Field<Label>("copyLabel");
        Check(copyMetrics.Height >= copyMetrics.PreferredHeight && copyMetrics.Height >= copyMetrics.Font.Height + copyMetrics.Padding.Vertical,
            "The working-copy path fits its actual DPI-scaled font", new { copyMetrics.DeviceDpi, copyMetrics.Height, copyMetrics.PreferredHeight, FontHeight = copyMetrics.Font.Height });
        Size beforeResize = form.Size;
        Check(form.MinimumSize.Width >= (int)Math.Ceiling(1000 * form.DeviceDpi / 96d) && form.MinimumSize.Height >= (int)Math.Ceiling(620 * form.DeviceDpi / 96d),
            "The minimum window size uses logical dimensions at the actual display DPI", new { form.DeviceDpi, form.MinimumSize });
        form.Size = new Size(Math.Max(form.MinimumSize.Width, 1000), Math.Max(form.MinimumSize.Height, 620));
        form.PerformLayout();
        var navigationTabMetrics = Descendants(form).OfType<TabControl>().Single(t => t.AccessibleName == "Help navigation");
        CheckNativeTabGeometry(navigationTabMetrics, "Contents / All pages");
        Check(Field<TreeView>("contents").Font.Equals(form.Font) && Field<ListBox>("pages").Font.Equals(form.Font),
            "Larger tab headers leave the Contents tree and All pages body fonts unchanged");
        CheckNativeTabPainting(navigationTabMetrics);
        Check(captionMetrics.Height >= captionMetrics.PreferredHeight && form.EditorWebView.Parent!.Top >= captionMetrics.Bottom,
            "Resizing to the minimum window keeps caption text above the page viewport", new { captionMetrics.Height, captionMetrics.PreferredHeight, captionMetrics.Width, BrowserBounds = form.EditorWebView.Parent!.Bounds });
        Check(captionMetrics.ClientSize.Height >= captionMetrics.Font.Height * 2 + captionMetrics.Padding.Vertical,
            "Both explicit caption lines remain readable at the minimum window width", new { captionMetrics.Font.Height, AvailableHeight = captionMetrics.ClientSize.Height, captionMetrics.Width });
        Check(form.EditorWebView.Parent!.Height >= captionMetrics.Font.Height * 2,
            "The page viewport remains usable below the metadata at the minimum window width", new { ViewportHeight = form.EditorWebView.Parent!.Height, FontHeight = captionMetrics.Font.Height });
        await Js("HelpEditor.setTab('source')");
        Check(await Bool("document.getElementById('source-editor').getBoundingClientRect().height > 100"),
            "The HTML source editor retains more than 100 CSS pixels of height at the DPI-scaled minimum size");
        await Js("HelpEditor.setTab('visual')");
        int shortCaptionHeight = captionMetrics.Height;
        string shortCaption = captionMetrics.Text;
        captionMetrics.Text = new string('W', 200) + "\nBuild / F5: Website";
        Check(captionMetrics.Height == shortCaptionHeight && form.EditorWebView.Parent!.Height >= captionMetrics.Font.Height * 2,
            "A very long topic name is ellipsized without consuming the editor viewport");
        captionMetrics.Text = shortCaption;
        form.Size = beforeResize;
        Check(form.ArtifactRoot == testRoot && form.CurrentWorkspace == "edit", "Views open in an isolated source/profile root with Formatted selected");
        Check(await Bool("HelpEditor.getState().unifiedViews && Array.from(document.querySelectorAll('.tabs button')).filter(b=>getComputedStyle(b).display!=='none').map(b=>b.textContent.trim()).join('|')==='Formatted|HTML source|Browser view|App view'"), "One HTML row offers Formatted, HTML source, Browser view and App view");
        Check(!Descendants(form).OfType<TabControl>().Any(t => t.AccessibleName == "Help workspace") && !Field<ToolStripComboBox>("target").Visible, "The duplicate native workspace tabs and build-target selector are absent");
        var design = Field<ToolStripComboBox>("design");
        string designSettings = Path.Combine(fixture, "Project", "HelpEditorDesign.json");
        Check(design.Visible && design.Items.Cast<string>().SequenceEqual(new[] { "Flare original", "Clean blue", "Warm paper", "Readable Contents" }) && design.SelectedIndex == 0 && !File.Exists(designSettings),
            "A visible authoring Design control defaults to Flare style without creating project settings");
        var designDisplay = new ComboBoxInfo { Size = Marshal.SizeOf<ComboBoxInfo>() };
        bool hasDesignDisplay = GetComboBoxInfo(design.ComboBox.Handle, ref designDisplay);
        int designTextWidth = designDisplay.Item.Right - designDisplay.Item.Left;
        var designNameWidths = design.Items.Cast<string>().Select(name => new { Name = name, Width = TextRenderer.MeasureText(name, design.ComboBox.Font,
            Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width }).ToArray();
        Check(hasDesignDisplay && designNameWidths.All(name => name.Width <= designTextWidth) && design.DropDownWidth >= design.Width,
            "The selected design and all design names fit the actual DPI-scaled ComboBox text rectangle", new { design.ComboBox.DeviceDpi, design.Width, design.DropDownWidth, TextWidth = designTextWidth, FontHeight = design.ComboBox.Font.Height, Names = designNameWidths });
        var browserParent = form.EditorWebView.Parent;
        var core = form.EditorWebView.CoreWebView2;
        string saved = form.CurrentProject.ReadTopic("basics/alpha.htm");
        string pending = saved.Replace("BASELINE_TOKEN", "LiveDraftSentinel");
        await SetSource(pending);
        await Js("document.getElementById('source-editor').setSelectionRange(24,41)");
        await Js("document.getElementById('app-tab').click()");
        await WaitUntil(() => form.CurrentWorkspace == "desktop");
        await WaitUntil(() => !Field<bool>("busy") && form.IsAppPreviewVisible && form.AppPreviewWebView?.CoreWebView2 != null);
        Check(form.IsDirty && await Source() == pending && form.CurrentProject.ReadTopic("basics/alpha.htm") == saved, "Clicking App view previews the live draft without saving or losing its source");
        string desktop = await AppHtml("LiveDraftSentinel");
        Check(desktop.Contains("LiveDraftSentinel") && desktop.Contains("DesktopOnlySentinel") && !desktop.Contains("PublicOnlySentinel"), "StatsDirect app preview filters the current draft to Desktop content");
        Check(desktop.Contains("DesktopFooterSentinel") && desktop.Contains("footer.svg") && !desktop.Contains("PublicFooterSentinel"), "StatsDirect app preview receives the generated Desktop footer and local artwork");
        Check(desktop.Contains("help-viewer") && await AppBool("Boolean(document.querySelector('#contents') && document.querySelector('#search-form'))"), "App view loads the complete generated navigation shell rather than the editable body");
        Check(form.IsAppPreviewDocked && form.IsAppPreviewVisible && !ReferenceEquals(form.AppPreviewWebView, form.EditorWebView), "App view uses a separate browser in a visible right-hand dock");
        var appBrowser = form.AppPreviewWebView!;
        var appCore = appBrowser.CoreWebView2;
        appCore.FrameCreated += (_, e) => TrackFrame(e.Frame, appFrames);
        var appBounds = appBrowser.RectangleToScreen(appBrowser.ClientRectangle);
        var editBounds = form.EditorWebView.RectangleToScreen(form.EditorWebView.ClientRectangle);
        Check(appBounds.Left >= editBounds.Right && editBounds.Width > 100 && appBounds.Width > 100, "Docked App help reserves space to the right without covering the editing surface", new { App = appBounds, Editor = editBounds });
        Size appTestSize = form.Size;
        form.Size = form.MinimumSize;
        form.PerformLayout();
        await Task.Delay(100);
        appBounds = appBrowser.RectangleToScreen(appBrowser.ClientRectangle);
        editBounds = form.EditorWebView.RectangleToScreen(form.EditorWebView.ClientRectangle);
        Check(appBounds.Left >= editBounds.Right && await Bool("innerWidth >= 240 && document.getElementById('source-editor').getBoundingClientRect().height > 100") && await AppBool("innerWidth >= 240"),
            "At the DPI-scaled minimum window, the App pane and editable page remain usable without overlap", new { App = appBounds, Editor = editBounds, form.DeviceDpi, form.Size });
        form.Size = appTestSize;
        form.PerformLayout();
        Check(Descendants(appBrowser.Parent!).OfType<ToolStrip>().SelectMany(bar => bar.Items.Cast<ToolStripItem>()).Select(item => item.Text).Contains("Contents") && Descendants(appBrowser.Parent!).OfType<ToolStrip>().SelectMany(bar => bar.Items.Cast<ToolStripItem>()).Select(item => item.Text).Contains("Print"), "App view reuses StatsDirect's Contents, Print and navigation toolbar");
        await AppJs("window.__workspaceProbe='retained';document.getElementById('app-details').open=true");
        await form.ToggleAppPreviewModeAsync();
        Check(!form.IsAppPreviewDocked && form.IsAppPreviewVisible && ReferenceEquals(appBrowser, form.AppPreviewWebView) && ReferenceEquals(appCore, form.AppPreviewWebView!.CoreWebView2), "Pop out retains the same App browser session");
        Check(await AppBool("window.__workspaceProbe==='retained' && document.getElementById('app-details').open"), "Popping out preserves App reading state and expanded R content");
        await form.ToggleAppPreviewModeAsync();
        Check(form.IsAppPreviewDocked && await AppBool("window.__workspaceProbe==='retained' && document.getElementById('app-details').open"), "Dock restores the same expanded App content without reloading");
        form.HideAppPreview();
        Check(!form.IsAppPreviewVisible && await Source() == pending && form.IsDirty, "Closing App help leaves the unsaved editor intact");
        await form.SwitchWorkspaceAsync("desktop");
        Check(form.IsAppPreviewVisible && ReferenceEquals(appBrowser, form.AppPreviewWebView) && ReferenceEquals(appCore, form.AppPreviewWebView!.CoreWebView2), "Reopening App view reuses its native browser");
        await form.SwitchWorkspaceAsync("edit");
        Check(form.IsAppPreviewVisible && await Bool("!document.getElementById('source-panel').hidden"), "Editing can remain visible alongside the docked App preview");
        await form.SwitchWorkspaceAsync("desktop");
        Check(Field<Label>("workspaceCaption").Text.Contains("Live unsaved draft") && Field<Label>("workspaceCaption").Text.Contains("not rebuilt"), "Preview caption distinguishes the live current page from an unbuilt whole site");
        string historyBeforeDesign = await String("JSON.stringify({index:HelpEditor.getState().historyIndex,source:document.getElementById('source-editor').value})");
        design.SelectedIndex = 3;
        await WaitUntil(() => form.CurrentProject.GetAppearance(HelpBuildTarget.Desktop) == "readable" && !Field<bool>("busy"));
        Check(await Source() == pending && form.IsDirty && await Bool("HelpEditor.getState().appearance==='readable'") &&
            await String("JSON.stringify({index:HelpEditor.getState().historyIndex,source:document.getElementById('source-editor').value})") == historyBeforeDesign,
            "The optional Readable Contents design updates the draft preview without changing its source, selection or Undo history");
        design.SelectedIndex = 1;
        await WaitUntil(() => form.CurrentProject.GetAppearance(HelpBuildTarget.Desktop) == "clean-blue" && !Field<bool>("busy"));
        Check(await Source() == pending && form.IsDirty && form.CurrentProject.ReadTopic("basics/alpha.htm") == saved && await String("JSON.stringify({index:HelpEditor.getState().historyIndex,source:document.getElementById('source-editor').value})") == historyBeforeDesign,
            "Choosing a design preserves the unsaved HTML, saved page and Undo history");
        Check(HelpProject.Open(fixture).GetAppearance(HelpBuildTarget.Desktop) == "clean-blue" && HelpProject.Open(fixture).GetAppearance(HelpBuildTarget.Public) == "flare" &&
            OutputHtml().Contains("data-help-skin=\"clean-blue\""),
            "An explicit Desktop design choice persists for that target and updates its current-page preview immediately");
        await form.SwitchWorkspaceAsync("public");
        string website = await BrowserHtml("LiveDraftSentinel");
        Check(website.Contains("PublicOnlySentinel") && !website.Contains("DesktopOnlySentinel") && website.Contains("PublicFooterSentinel") && !website.Contains("DesktopFooterSentinel"), "Website preview applies Public conditions and its own shared footer");
        Check(Field<ToolStripComboBox>("target").SelectedIndex == 1 && await Source() == pending && form.IsDirty, "Website selection updates build target while retaining the unsaved page");
        Check(design.SelectedIndex == 0 && await Bool("HelpEditor.getState().appearance==='flare'"), "Switching targets selects Website's independent Flare design");
        design.SelectedIndex = 2;
        await WaitUntil(() => form.CurrentProject.GetAppearance(HelpBuildTarget.Public) == "warm-paper" && !Field<bool>("busy"));
        Check(design.SelectedIndex == 2 && OutputHtml().Contains("data-help-skin=\"warm-paper\"") && await Source() == pending,
            "Website can use Warm paper while the live draft stays unchanged");
        Check(await BrowserHas("document.documentElement.dataset.helpSkin==='warm-paper' && Boolean(document.querySelector('#contents') && document.querySelector('#search-form') && document.querySelector('iframe#topic')) && typeof window.StatsDirectHelp?.search==='function'"),
            "Warm paper Browser view contains the complete Contents/search shell and nested topic frame");
        await form.SwitchWorkspaceAsync("edit");
        Check(await Bool("HelpEditor.getState().tab==='source' && document.getElementById('source-editor').selectionStart===24 && document.getElementById('source-editor').selectionEnd===41"), "Returning to Edit restores the HTML-source view and selection");
        await Js("HelpEditor.undo()");
        bool undoAfterDesign = await Source() == saved;
        await Js("HelpEditor.redo();document.getElementById('source-editor').setSelectionRange(24,41)");
        Check(undoAfterDesign && await Source() == pending && design.SelectedIndex == 2, "Design choices create no phantom Undo entries and Undo/Redo cannot change the project design");
        Check(ReferenceEquals(browserParent, form.EditorWebView.Parent) && ReferenceEquals(core, form.EditorWebView.CoreWebView2), "All workspace switches retain the same browser and parent handle");

        var githubMenu = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item => (item.Text ?? "").Replace("&", "") == "GitHub");
        Check(githubMenu.DropDownItems.OfType<ToolStripMenuItem>().Any(item => (item.Text ?? "").Contains("Status / review")) &&
            githubMenu.DropDownItems.OfType<ToolStripMenuItem>().Any(item => (item.Text ?? "").Contains("Rebuild website and commit")) &&
            githubMenu.DropDownItems.OfType<ToolStripMenuItem>().Any(item => (item.Text ?? "").Contains("Push branch")),
            "GitHub menu retains review, commit and explicit push commands without a workspace tab");
        string gitText = await CaptureGitReviewAsync();
        Check(form.IsDirty && await Source() == pending && form.EditorWebView.Visible && form.CurrentWorkspace == "edit",
            "Opening GitHub review from the menu preserves the current editing workspace and unsaved buffer");
        Check(gitText.Contains("Not connected to GitHub") && gitText.Contains("https://github.com/iain-buchan/statisticalhelp") && !gitText.Contains("0 ahead / 0 behind"),
            "Standalone source review shows no invented synchronization or remote connection");
        Check(gitText.Contains("unsaved draft") && gitText.Contains("not included in Git"), "GitHub review distinguishes an unsaved page from saved Git changes");
        string commitReview = CaptureCommitReview();
        Check(commitReview.Contains("3 pages") && commitReview.Contains("1 warnings") && commitReview.Contains("Publication warning sentinel"),
            "Commit review shows rebuilt website counts and every build warning before a commit");
        Check(await Source() == pending && form.IsDirty && form.CurrentWorkspace == "edit", "Cancelling commit review preserves the open draft and editing workspace");
        Check(Field<ToolStripStatusLabel>("gitBadge").Text == "Git: standalone" && Field<ToolStripStatusLabel>("gitBadge").Owner is StatusStrip,
            "A compact persistent status-bar badge exposes Git status alongside every editing workspace");
        var visibleBadge = Field<ToolStripStatusLabel>("gitBadge");
        Check(visibleBadge.Visible && visibleBadge.Placement == ToolStripItemPlacement.Main && visibleBadge.Bounds.Width > 30 && visibleBadge.Bounds.Right <= visibleBadge.Owner!.ClientSize.Width,
            "Git status badge is visibly laid out in the status bar", new { visibleBadge.Visible, visibleBadge.Placement, visibleBadge.Bounds, OwnerSize = visibleBadge.Owner!.ClientSize });
        var messageLabel = Field<ToolStripStatusLabel>("status");
        Check(messageLabel.Bounds.Height >= messageLabel.Font.Height, "The status message has enough height for its DPI-scaled font", new { messageLabel.Bounds, messageLabel.Font.Height });
        Check(await Source() == pending && await Bool("HelpEditor.getState().tab==='source' && document.getElementById('source-editor').selectionStart===24 && document.getElementById('source-editor').selectionEnd===41"),
            "Closing GitHub review retains source and its caret without switching workspaces");

        await SetSource("<html><body>unfinished");
        await form.SetDesignAsync("clean-blue");
        Check(await String("document.getElementById('source-editor').value") == "<html><body>unfinished" && form.IsDirty && form.CurrentProject.GetAppearance(HelpBuildTarget.Public) == "clean-blue",
            "Changing design does not parse, save or discard incomplete HTML-source work");
        bool malformedBlocked = false;
        try { await form.SwitchWorkspaceAsync("desktop"); } catch (InvalidDataException) { malformedBlocked = true; }
        Check(malformedBlocked && form.CurrentWorkspace == "edit" && await String("document.getElementById('source-editor').value") == "<html><body>unfinished", "Malformed HTML blocks preview and leaves its draft in the Edit workspace");
        await SetSource(saved);
        await form.SaveCurrentTopicAsync();
        await form.OpenTopicAsync("basics/alpha.htm#AppTarget");
        await form.SwitchWorkspaceAsync("desktop");
        Check(await AppHasFragment("basics/alpha.htm", "AppTarget"), "App view retains the selected topic's target fragment in the displayed page");
        await form.SwitchWorkspaceAsync("edit");
        await Js("HelpEditor.setTab('visual');document.querySelector('.equation-widget').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));document.getElementById('equation-field').value='x^{99}'");
        bool equationBlocked = false;
        try { await form.SwitchWorkspaceAsync("public"); } catch (InvalidOperationException) { equationBlocked = true; }
        Check(equationBlocked && await Bool("HelpEditor.getState().equationDialogOpen && HelpEditor.getState().equationDraftDirty") && form.CurrentWorkspace == "edit", "Workspace switching cannot discard an unapplied equation draft");
        bool equationDesignBlocked = false;
        try { await form.SetDesignAsync("warm-paper"); } catch (InvalidOperationException) { equationDesignBlocked = true; }
        Check(equationDesignBlocked && !Field<ToolStrip>("designBar").Enabled && form.CurrentProject.GetAppearance(HelpBuildTarget.Public) == "clean-blue" && await Bool("HelpEditor.setAppearance('warm-paper')===false && HelpEditor.getState().equationDraftDirty"),
            "Native and renderer design changes cannot hide or discard an unapplied equation");
        await Js("document.getElementById('equation-cancel').click();HelpEditor.openLink(document.getElementById('beta-link'));document.getElementById('link-text').value='Draft link';document.getElementById('link-text').dispatchEvent(new Event('input',{bubbles:true}))");
        Check(!githubMenu.Enabled && !Field<ToolStripStatusLabel>("gitBadge").Enabled && await Bool("HelpEditor.getState().dialogDraftDirty") && form.CurrentWorkspace == "edit",
            "GitHub menu and status badge cannot hide or discard an unapplied link draft");
        bool linkDesignBlocked = false;
        try { await form.SetDesignAsync("warm-paper"); } catch (InvalidOperationException) { linkDesignBlocked = true; }
        Check(linkDesignBlocked && form.CurrentProject.GetAppearance(HelpBuildTarget.Public) == "clean-blue" && await Bool("HelpEditor.setAppearance('warm-paper')===false && HelpEditor.getState().dialogDraftDirty"),
            "Native and renderer design changes preserve an unapplied link draft");
        await Js("document.getElementById('link-cancel').click()");

        await form.SwitchWorkspaceAsync("public");
        await ClickPreviewLink("beta-link");
        await WaitUntil(() => form.CurrentTopicPath == "basics/beta.htm" && !Field<bool>("busy"));
        Check(form.CurrentWorkspace == "public" && (await BrowserHtml("BETA_PAGE")).Contains("BETA_PAGE"), "Clicking a saved page's preview link follows its topic within the Website workspace");
        Check(await BrowserHas("location.pathname.endsWith('/basics/beta.htm') && location.hash==='#BetaTarget' && Boolean(document.getElementById('BetaTarget'))"),
            "A cross-page Browser link preserves its target fragment in the nested topic document");
        await form.OpenTopicAsync("basics/alpha.htm");
        await form.SwitchWorkspaceAsync("edit");
        await SetSource(pending);
        await form.SwitchWorkspaceAsync("public");
        using (var cancel = new OwnedPromptResponse(form, 2))
        {
            await ClickPreviewLink("beta-link");
            await WaitUntil(() => cancel.Responses > 0 && !Field<bool>("busy"));
            Check(cancel.Responses == 1, "A live-draft preview link asks the ordinary owned save prompt before leaving");
        }
        Check(form.CurrentTopicPath == "basics/alpha.htm" && form.CurrentWorkspace == "public" && form.IsDirty && await Source() == pending && form.CurrentProject.ReadTopic("basics/alpha.htm") == saved,
            "Cancelling preview-link navigation preserves the live draft, saved file and Website workspace");
        Check((await BrowserHtml("LiveDraftSentinel")).Contains("LiveDraftSentinel"), "Cancelling a preview link keeps the live page preview instead of navigating its iframe to raw XHTML");
        await form.SaveCurrentTopicAsync();
        await form.OpenTopicAsync("basics/beta.htm");
        Check(form.CurrentWorkspace == "public" && (await BrowserHtml("BETA_PAGE")).Contains("BETA_PAGE"), "Selecting a new topic keeps the Website workspace and previews the new page");
        await form.BuildHelpAsync();
        Check(form.LastBuildResult?.OutputDirectory == Path.Combine(testRoot, "Build", "Public") && Field<Label>("workspaceCaption").Text.Contains("includes your saved edits"), "Whole-site build uses the active preview target and records that saved edits were included");
        string beforeBuiltDesignChange = await Source();
        await form.SetDesignAsync("warm-paper");
        Check(Field<Label>("workspaceCaption").Text.Contains("out of date") && await Source() == beforeBuiltDesignChange && !form.IsDirty &&
            OutputHtml().Contains("data-help-skin=\"warm-paper\""),
            "Changing a built target's design marks its whole site stale and updates only the current-page preview");
        await form.BuildHelpAsync();
        Check(Field<Label>("workspaceCaption").Text.Contains("includes your saved edits"), "Rebuilding clears the target's stale design status");
        await form.SwitchWorkspaceAsync("edit");
        await SetSource(form.CurrentProject.ReadTopic("basics/beta.htm").Replace("BETA_PAGE", "NEW_BETA_DRAFT"));
        await form.SwitchWorkspaceAsync("public");
        Check(Field<Label>("workspaceCaption").Text.Contains("out of date") && Field<Label>("workspaceCaption").Text.Contains("Live unsaved draft"), "Editing after a build clearly marks built output out of date while showing a live preview");
        await form.SaveCurrentTopicAsync();
        Check(Field<Label>("workspaceCaption").Text.Contains("out of date") && !(await Source()).Contains("help-shared-footer"), "Saving a page keeps build status stale and never writes preview-only footer furniture into source");

        await InitializeGit();
        string previewBeforeGitReview = await String("document.getElementById('preview-frame').src");
        int targetBeforeGitReview = Field<ToolStripComboBox>("target").SelectedIndex;
        gitText = await CaptureGitReviewAsync(clickBadge: true);
        Check(form.CurrentWorkspace == "public" && form.EditorWebView.Visible && Field<ToolStripComboBox>("target").SelectedIndex == targetBeforeGitReview &&
            await String("document.getElementById('preview-frame').src") == previewBeforeGitReview,
            "Status-bar GitHub review leaves the checked Website preview and build target unchanged");
        Check(gitText.Contains("help-editor/native-workspace") && gitText.Contains("None configured") && gitText.Contains("Unavailable") && !gitText.Contains("0 ahead / 0 behind"), "A Git branch without upstream shows unavailable comparison rather than synchronized zero counts");
        Check(gitText.Contains("Last successful fetch: Not recorded") && gitText.Contains("No network request was made"), "Local status and remote fetch evidence are distinct");
        File.AppendAllText(Path.Combine(fixture, "Content", "basics", "alpha.htm"), "\n");
        File.WriteAllText(Path.Combine(fixture, "unrelated.txt"), "Not source");
        await InvokeTask("UpdateGitBadgeAsync");
        Check(GitText().Contains("1 source / 1 other"), "GitHub review separates saved source changes from other files");
        var renamed = new GitStatus(true, "help-editor/native-workspace", null, 0, 0,
            [new GitFileChange("R ", "Content/basics/new α.htm") { OriginalPath = "Content/basics/old α.htm" }]);
        typeof(MainForm).GetMethod("ApplyGitBadge", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [renamed]);
        Check(GitText().Contains("Content/basics/old α.htm → Content/basics/new α.htm"), "GitHub review retains both paths and Unicode for a renamed source file");
        var status = new GitStatus(true, "help-editor/native-workspace", "https://user:secret@example.test/help.git?token=secret", 2, 3, [])
        { ComparisonBranch = "origin/help-editor/native-workspace", HasComparison = true, LastSuccessfulFetchUtc = DateTimeOffset.UtcNow.AddMinutes(-12) };
        typeof(MainForm).GetMethod("ApplyGitBadge", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [status]);
        gitText = GitText();
        Check(gitText.Contains("2 ahead / 3 behind") && gitText.Contains("(cached)") && gitText.Contains("None configured") && !gitText.Contains("secret"), "Cached branch comparison stays distinct from configured tracking and redacts remote credentials");
        typeof(MainForm).GetMethod("MarkGitStatusStale", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        Check(Field<ToolStripStatusLabel>("gitBadge").Text == "Git: refresh status" && Field<ToolStripStatusLabel>("gitBadge").ToolTipText!.Contains("changed since the last local status check"),
            "Saved source changes mark the persistent Git status stale");
        typeof(MainForm).GetMethod("ShowGitStatusFailure", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, ["Owned fixture failure"]);
        Check(Field<ToolStripStatusLabel>("gitBadge").Text == "Git: status unavailable" && Field<ToolStripStatusLabel>("gitBadge").ToolTipText!.Contains("earlier comparison is cached"),
            "A failed local status check cannot leave a fresh-looking synchronization badge");
        var missing = status with { TrackingBranch = "origin/deleted", ComparisonBranch = "origin/deleted", HasComparison = false };
        typeof(MainForm).GetMethod("ApplyGitBadge", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [missing]);
        Check(GitText().Contains("origin/deleted") && GitText().Contains("Unavailable") && !GitText().Contains("2 ahead"), "A configured missing upstream never displays invalid ahead/behind counts");
        await form.OpenProjectAsync(fixture);
        await WaitUntil(() => !Field<bool>("busy") && form.LastOutputPreviewResult != null);
        Check(design.SelectedIndex == 2 && await Bool("HelpEditor.getState().appearance==='warm-paper'"), "Reopening the project restores its saved Website design");
        await form.SwitchWorkspaceAsync("desktop");
        Check(design.SelectedIndex == 1 && await Bool("HelpEditor.getState().appearance==='clean-blue'"), "Returning to StatsDirect app restores that target's saved Clean blue design");
        Check(uiExceptions == 0 && form.CurrentProject.RootPath == fixture, "Workspace checks leave the maintained source untouched and have no unhandled UI errors");
    }

    private static Task Js(string script) => form.EditorWebView.ExecuteScriptAsync(script);
    private static async Task<bool> Bool(string script) => JsonSerializer.Deserialize<bool>(await form.EditorWebView.ExecuteScriptAsync(script));
    private static async Task<string> String(string script) => JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync(script))!;
    private static Task<string> Source() => String("HelpEditor.getSource()");
    private static Task AppJs(string script) => form.AppPreviewWebView!.ExecuteScriptAsync(script);
    private static async Task<bool> AppBool(string script) => JsonSerializer.Deserialize<bool>(await form.AppPreviewWebView!.ExecuteScriptAsync(script));
    private static async Task<string> AppHtml(string token)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < until)
        {
            if (form.AppPreviewWebView?.CoreWebView2 != null)
            {
                string html = JsonSerializer.Deserialize<string>(await form.AppPreviewWebView.ExecuteScriptAsync("document.documentElement.outerHTML")) ?? "";
                if (html.Contains(token, StringComparison.Ordinal)) return html;
            }
            await Task.Delay(35);
        }
        string diagnostics = form.AppPreviewWebView?.CoreWebView2 == null ? "No browser" :
            "Source=" + form.AppPreviewWebView.CoreWebView2.Source + " HTML=" + await form.AppPreviewWebView.ExecuteScriptAsync("document.documentElement.outerHTML.slice(0,2000)");
        throw new TimeoutException("The App preview did not display " + token + "; " + diagnostics);
    }
    private static string OutputHtml() => File.ReadAllText(Path.Combine(form.LastOutputPreviewResult!.OutputDirectory, form.CurrentTopicPath!.Replace('/', Path.DirectorySeparatorChar)));
    private static void TrackPreviewFrame(CoreWebView2Frame frame) => TrackFrame(frame, previewFrames);
    private static void TrackFrame(CoreWebView2Frame frame, List<CoreWebView2Frame> frames)
    {
        frames.Add(frame);
        frame.FrameCreated += (_, e) => TrackFrame(e.Frame, frames);
    }
    private static async Task<bool> BrowserHas(string script)
    {
        var until = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < until)
        {
            foreach (var frame in previewFrames.ToArray().Reverse())
            {
                // A frame being navigated can return JSON null instead of a
                // Boolean. Only true establishes readiness; otherwise poll on.
                try { if (await frame.ExecuteScriptAsync("Boolean(" + script + ")") == "true") return true; }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            }
            await Task.Delay(35);
        }
        return false;
    }
    private static async Task<bool> AppHasFragment(string path, string fragment)
    {
        string script = "location.pathname.endsWith(" + JsonSerializer.Serialize("/" + path) + ") && location.hash===" + JsonSerializer.Serialize("#" + fragment) + " && Boolean(document.getElementById(" + JsonSerializer.Serialize(fragment) + "))";
        var until = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < until)
        {
            if (form.CurrentProject.GetAppearance(HelpBuildTarget.Desktop) == "flare" &&
                await form.AppPreviewWebView!.ExecuteScriptAsync("Boolean(" + script + ")") == "true") return true;
            foreach (var frame in appFrames.ToArray().Reverse())
            {
                try { if (await frame.ExecuteScriptAsync("Boolean(" + script + ")") == "true") return true; }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            }
            await Task.Delay(35);
        }
        return false;
    }
    private static async Task<string> BrowserHtml(string token)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < until)
        {
            foreach (var frame in previewFrames.ToArray().Reverse())
            {
                try
                {
                    string html = JsonSerializer.Deserialize<string>(await frame.ExecuteScriptAsync("document.documentElement.outerHTML")) ?? "";
                    if (html.Contains(token, StringComparison.Ordinal)) return html;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            }
            await Task.Delay(35);
        }
        throw new TimeoutException("The generated Browser preview did not display " + token);
    }
    private static async Task SetSource(string value) => await Js("HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(value) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
    private static T Field<T>(string name) => (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    private static Task InvokeTask(string name) => (Task)typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null)!;
    private static IEnumerable<Control> Descendants(Control control) => new[] { control }.Concat(control.Controls.Cast<Control>().SelectMany(Descendants));
    private static string GitText() => (string)typeof(MainForm).GetMethod("GitStatusText", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(form, [Field<GitStatus>("lastGitStatus")])!;
    private static string CaptureCommitReview()
    {
        string captured = "";
        using var timer = new System.Windows.Forms.Timer { Interval = 25 };
        timer.Tick += (_, _) =>
        {
            var dialog = form.OwnedForms.SingleOrDefault(f => f.Text == "Commit help source and rebuilt website");
            if (dialog == null) return;
            timer.Stop();
            var review = Descendants(dialog).OfType<TextBox>().Single(text => text.ReadOnly);
            captured = review.Text;
            Check(Descendants(dialog).OfType<TextBox>().Any(text => text.Text == "Fixture Author") &&
                Descendants(dialog).OfType<TextBox>().Any(text => text.Text == "author@example.test"),
                "Commit review uses the current Git identity rather than a bundled author's name");
            dialog.DialogResult = DialogResult.Cancel;
            dialog.Close();
        };
        timer.Start();
        var publication = new WebsitePublication("Owned fixture only", 3, 3, ["Publication warning sentinel"]);
        var result = typeof(MainForm).GetMethod("PromptCommit", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [Field<GitStatus>("lastGitStatus"), publication, new GitAuthorIdentity("Fixture Author", "author@example.test")]);
        Check(result == null, "Commit review cancellation creates no commit");
        return captured;
    }
    private static async Task<string> CaptureGitReviewAsync(bool clickBadge = false)
    {
        var captured = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timer = new System.Windows.Forms.Timer { Interval = 25 };
        timer.Tick += (_, _) =>
        {
            var dialog = form.OwnedForms.SingleOrDefault(f => f.Text == "GitHub status / review changes");
            if (dialog == null) return;
            timer.Stop();
            try
            {
                var text = Descendants(dialog).OfType<TextBox>().Single();
                Check(text.ReadOnly && text.Multiline && text.ScrollBars == ScrollBars.Both, "GitHub review opens a readable, scrollable, read-only dialog");
                captured.TrySetResult(text.Text);
            }
            catch (Exception ex) { captured.TrySetException(ex); }
            finally { dialog.Close(); }
        };
        timer.Start();
        if (clickBadge) Field<ToolStripStatusLabel>("gitBadge").PerformClick();
        else form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item => (item.Text ?? "").Replace("&", "") == "GitHub")
            .DropDownItems.OfType<ToolStripMenuItem>().Single(item => (item.Text ?? "").Contains("Status / review")).PerformClick();
        string result = await captured.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitUntil(() => !Field<bool>("busy"));
        return result;
    }
    private static async Task WaitUntil(Func<bool> test)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < until)
        {
            if (test()) { await Task.Delay(100); if (test()) return; }
            await Task.Delay(25);
        }
        throw new TimeoutException("View selection did not finish; workspace=" + form.CurrentWorkspace +
            ", topic=" + form.CurrentTopicPath + ", busy=" + Field<bool>("busy") +
            ", output=" + form.LastOutputPreviewResult?.OutputDirectory + ", renderer=" + await form.EditorWebView.ExecuteScriptAsync("HelpEditor.getState()"));
    }
    private static async Task ClickPreviewLink(string id)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < until)
        {
            foreach (var frame in previewFrames.ToArray().Reverse())
            {
                try
                {
                    string found = await frame.ExecuteScriptAsync("Boolean(document.getElementById(" + JsonSerializer.Serialize(id) + "))");
                    if (found != "true") continue;
                    await frame.ExecuteScriptAsync("setTimeout(()=>document.getElementById(" + JsonSerializer.Serialize(id) + ").click(),0);true");
                    return;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* An older preview frame has been destroyed. */ }
            }
            await Task.Delay(25);
        }
        throw new TimeoutException("The actual preview link was not available in its sandboxed iframe.");
    }
    private static void Check(bool value, string name, object? detail = null) { if (value) passed++; else failed++; results.Add(new { name, passed = value, detail }); Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}"); }

    private static void CheckNativeTabGeometry(TabControl tabs, string name)
    {
        int textInset = (int)Math.Ceiling(24 * tabs.DeviceDpi / 96d);
        var labels = tabs.TabPages.Cast<TabPage>().Select((page, index) => new
        {
            page.Text,
            Bounds = tabs.GetTabRect(index),
            TextWidth = TextRenderer.MeasureText(page.Text, tabs.Font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width
        }).ToArray();
        Check(tabs.DrawMode == TabDrawMode.OwnerDrawFixed && tabs.Font.Bold && tabs.Font.SizeInPoints >= 12 &&
            labels.All(label => label.Bounds.Width >= label.TextWidth + textInset && label.Bounds.Height >= (int)Math.Ceiling(40 * tabs.DeviceDpi / 96d)) &&
            labels.Max(label => label.Bounds.Right) <= tabs.ClientSize.Width,
            "All " + name + " tab labels fit generous separate headers at the DPI-scaled minimum window width",
            new { tabs.DeviceDpi, tabs.Font.SizeInPoints, FontHeight = tabs.Font.Height, tabs.ClientSize, Labels = labels });
    }

    private static void CheckNativeTabPainting(TabControl tabs)
    {
        using var image = new Bitmap(tabs.Width, tabs.Height);
        tabs.DrawToBitmap(image, tabs.ClientRectangle);
        int inset = (int)Math.Ceiling(8 * tabs.DeviceDpi / 96d);
        Rectangle selected = tabs.GetTabRect(tabs.SelectedIndex), other = tabs.GetTabRect((tabs.SelectedIndex + 1) % tabs.TabCount);
        Color active = image.GetPixel(selected.Left + inset, selected.Top + inset), inactive = image.GetPixel(other.Left + inset, other.Top + inset);
        double Luminance(Color color)
        {
            double Channel(byte channel) { double v = channel / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        double light = Luminance(inactive), dark = Luminance(active);
        double contrast = (Math.Max(light, dark) + .05) / (Math.Min(light, dark) + .05);
        Check(contrast >= 4.5 && active.ToArgb() != inactive.ToArgb(),
            "The actually painted active navigation tab is distinct from inactive tabs with at least 4.5:1 contrast",
            new { Active = active.ToArgb(), Inactive = inactive.ToArgb(), Contrast = contrast });
        image.Save(Path.Combine(evidence, "readable-native-navigation-tabs.png"));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo
    {
        public int Size;
        public NativeRectangle Item, Button;
        public int ButtonState;
        public IntPtr ComboBox, Edit, List;
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetComboBoxInfo(IntPtr comboBox, ref ComboBoxInfo info);

    private static async Task InitializeGit()
    {
        foreach (string[] arguments in new[] { new[] { "init", "-b", "help-editor/native-workspace" }, new[] { "config", "user.name", "Help Editor Test" }, new[] { "config", "user.email", "help-editor-test@example.invalid" }, new[] { "add", "Content", "Project" }, new[] { "commit", "-m", "Owned native workspace fixture" } })
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = fixture, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException(await error + await output);
        }
        string exclude = Path.Combine(fixture, ".git", "info", "exclude");
        File.AppendAllText(exclude, "\n/" + HelpProject.MarkerFile + "\n");
    }

    private static void CreateFixture()
    {
        // Only this owned fixture may be removed; no maintained source is used.
        string expected = Path.GetFullPath(Path.Combine(testRoot, "HelpSource"));
        if (Path.GetFullPath(fixture) != expected || !expected.StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture path");
        if (Directory.Exists(fixture))
        {
            foreach (string file in Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(fixture, true);
        }
        Directory.CreateDirectory(Path.Combine(fixture, "Content", "basics"));
        Directory.CreateDirectory(Path.Combine(fixture, "Content", "resources", "masterpages"));
        Directory.CreateDirectory(Path.Combine(fixture, "Content", "resources", "images"));
        File.WriteAllText(Path.Combine(fixture, "Content", "resources", "images", "footer.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"12\" height=\"12\"><circle cx=\"6\" cy=\"6\" r=\"5\"/></svg>");
        Directory.CreateDirectory(Path.Combine(fixture, "Project", "Targets"));
        Directory.CreateDirectory(Path.Combine(fixture, "Project", "TOCs"));
        File.WriteAllText(Path.Combine(fixture, HelpProject.MarkerFile), "{\"purpose\":\"Owned native workspace checks\"}");
        File.WriteAllText(Path.Combine(fixture, "Content", "contents.htm"), "<html><head><title>Fixture contents</title></head><body><h1>Fixture contents</h1></body></html>");
        File.WriteAllText(Path.Combine(fixture, "Content", "basics", "alpha.htm"), "<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>Alpha</title></head><body><h1 id=\"AppTarget\">Alpha</h1><p>BASELINE_TOKEN</p><p MadCap:conditions=\"Primary.SWHELP\">DesktopOnlySentinel</p><p MadCap:conditions=\"Primary.WEBHELP\">PublicOnlySentinel</p><p><a id=\"beta-link\" href=\"beta.htm#BetaTarget\">Beta</a></p><MadCap:equation>$x^2$</MadCap:equation></body></html>");
        File.WriteAllText(Path.Combine(fixture, "Content", "basics", "beta.htm"), "<html><head><title>Beta</title></head><body><h1 id=\"BetaTarget\">Beta</h1><p>BETA_PAGE</p></body></html>");
        string alphaFile = Path.Combine(fixture, "Content", "basics", "alpha.htm");
        string alpha = File.ReadAllText(alphaFile).Replace("</body>", "<details id=\"app-details\"><summary>R equivalent</summary><pre>mean(c(1, 2))</pre></details>" + string.Concat(Enumerable.Range(1, 40).Select(i => $"<p>Reading position paragraph {i}</p>")) + "</body>");
        File.WriteAllText(alphaFile, alpha);
        File.WriteAllText(Path.Combine(fixture, "Project", "TOCs", "STATSDIRECT.fltoc"), "<CatapultToc Version=\"1\"><TocEntry Title=\"Contents\" Link=\"/Content/contents.htm\"/><TocEntry Title=\"Alpha\" Link=\"/Content/basics/alpha.htm\"/><TocEntry Title=\"Beta\" Link=\"/Content/basics/beta.htm\"/></CatapultToc>");
        foreach (var (target, footer, sentinel) in new[] { ("DesktopHelp", "desktop", "DesktopFooterSentinel"), ("Website", "public", "PublicFooterSentinel") })
        {
            File.WriteAllText(Path.Combine(fixture, "Project", "Targets", target + ".fltar"), $"<CatapultTarget MasterPage=\"/Content/resources/masterpages/{footer}.flmsp\" />");
            File.WriteAllText(Path.Combine(fixture, "Content", "resources", "masterpages", footer + ".flmsp"), $"<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><body><MadCap:bodyProxy/><MadCap:pageFooter><p>{sentinel}</p><img src=\"../images/footer.svg\" /></MadCap:pageFooter></body></html>");
        }
    }

    /// <summary>Test-only response to this form's ordinary save prompt; never authentication windows.</summary>
    private sealed class OwnedPromptResponse : IDisposable
    {
        private readonly IntPtr owner;
        private readonly int response;
        private readonly Hook callback;
        private readonly IntPtr hook;
        public int Responses { get; private set; }
        public OwnedPromptResponse(Form form, int response)
        {
            owner = form.Handle; this.response = response; callback = OnHook;
            hook = SetWindowsHookEx(5, callback, IntPtr.Zero, GetCurrentThreadId());
            if (hook == IntPtr.Zero) throw new InvalidOperationException("Could not attach the owned test prompt handler.");
        }
        private IntPtr OnHook(int code, IntPtr window, IntPtr data)
        {
            if (code == 5 && GetWindow(window, 4) == owner)
            {
                var title = new StringBuilder(128); var kind = new StringBuilder(64);
                GetWindowText(window, title, title.Capacity); GetClassName(window, kind, kind.Capacity);
                if (title.ToString() == "Unsaved page" && kind.ToString() == "#32770") { Responses++; PostMessage(window, 0x0111, (IntPtr)response, IntPtr.Zero); }
            }
            return CallNextHookEx(hook, code, window, data);
        }
        public void Dispose() { UnhookWindowsHookEx(hook); GC.KeepAlive(callback); }
        private delegate IntPtr Hook(int code, IntPtr window, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, Hook procedure, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr window, IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    }
}
