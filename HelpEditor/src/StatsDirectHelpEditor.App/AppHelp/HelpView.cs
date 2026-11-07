#nullable disable
// Snapshot of StatsDirect Windows help host (MIT).
// Original: StatsDirectUI/UI/HtmlHelp/HelpView.cs
// See provenance.json for source hashes and the deliberately limited editor integration changes.
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StatsDirect.HelpEditor.App.AppHelp;

/// <summary>One browser session, reparented between the dock and floating window without navigation or reload.</summary>
internal sealed class HelpView : UserControl
{
    internal readonly WebView2 Browser = new() { Dock = DockStyle.Fill };
    internal Task Ready => ready.Task;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HelpCatalog catalog;
    private readonly string userData;
    private readonly ToolStripButton back = new("Back") { Enabled = false }, forward = new("Forward") { Enabled = false };
    private readonly ToolStripButton browse = new("Contents") { Enabled = false };
    private readonly ToolStripDropDownButton index = new("Index") { Enabled = false, ToolTipText = "Find a topic by an index keyword" };
    private readonly HelpIndexMenu indexMenu = new();
    private readonly ToolStripTextBox search = new() { AutoSize = false, Width = 190, AccessibleName = "Search all help topics" };
    private readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private const string BrowseFailure = "Could not open the help topics";
    private readonly ToolStripButton mode = new("Pop out") { Alignment = ToolStripItemAlignment.Right, Overflow = ToolStripItemOverflow.Never };
    internal string PageTitle { get; private set; } = "StatsDirect Help";
    internal event Action TitleChanged;
    internal event Action ModeRequested;
    internal event Action CloseRequested;
    private string requested;
    private bool starting;
    internal event Action<string> ExternalLinkRequested;

    internal HelpView(HelpCatalog catalog, string userDataDirectory = null)
    {
        this.catalog = catalog;
        userData = userDataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatsDirectHelpEditor", "AppHelpPreview");
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Arial", 10);
        Dock = DockStyle.Fill;
        Browser.TabIndex = 3;
        var heading = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, TabStop = true, TabIndex = 0 };
        var close = new ToolStripButton("Close", null, (_, _) => CloseRequested?.Invoke()) { Alignment = ToolStripItemAlignment.Right, Overflow = ToolStripItemOverflow.Never, ToolTipText = "Close help" };
        mode.Click += (_, _) => ModeRequested?.Invoke();
        heading.Items.AddRange([new ToolStripLabel("Help"), close, mode]);
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, TabStop = true, TabIndex = 1 };
        browse.Click += async (_, _) => await BrowseTopicsAsync();
        index.DropDown = indexMenu;
        indexMenu.Opening += (_, e) =>
        {
            try
            {
                if (!catalog.HasKeywordIndex)
                {
                    e.Cancel = true;
                    status.Text = "This help bundle does not yet include a keyword index.";
                    return;
                }
                indexMenu.Prepare(catalog.IndexEntries, this, index);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                e.Cancel = true;
                status.Text = "Could not open the help index: " + ex.Message;
            }
        };
        indexMenu.TopicChosen += entry => { ShowTopic(entry.Path); Browser.Focus(); };
        indexMenu.Closed += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.Keyboard && Visible && !IsDisposed)
            {
                index.GetCurrentParent()?.Focus();
                index.Select();
            }
        };
        VisibleChanged += (_, _) => { if (!Visible && indexMenu.Visible) indexMenu.Close(); };
        ParentChanged += (_, _) => { if (indexMenu.Visible) indexMenu.Close(); };
        toolbar.Items.AddRange([back, forward, new ToolStripButton("Home", null, (_, _) => ShowTopic("1000")), browse, index,
            new ToolStripSeparator(), new ToolStripButton("Print", null, (_, _) => Browser.CoreWebView2?.ShowPrintUI())]);
        var searchbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, CanOverflow = false, TabStop = true, TabIndex = 2 };
        var searchLabel = new ToolStripLabel("Search:");
        var searchButton = new ToolStripButton("Search", null, (_, _) => Search(search.Text));
        searchbar.Items.AddRange([searchLabel, search, searchButton]);
        searchbar.SizeChanged += (_, _) => search.Width = Math.Max(60, searchbar.ClientSize.Width - searchLabel.Width - searchButton.Width - 24);
        back.Click += (_, _) => { if (Browser.CoreWebView2?.CanGoBack == true) Browser.CoreWebView2.GoBack(); };
        forward.Click += (_, _) => { if (Browser.CoreWebView2?.CanGoForward == true) Browser.CoreWebView2.GoForward(); };
        search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Search(search.Text); } };
        var statusBar = new StatusStrip { TabStop = false, TabIndex = 4 };
        statusBar.Items.Add(status);
        Controls.Add(Browser);
        Controls.Add(searchbar);
        Controls.Add(toolbar);
        Controls.Add(heading);
        Controls.Add(statusBar);
        Load += async (_, _) => await InitializeAsync();
    }

    internal void SetPresentation(bool floating, bool canDock = true)
    {
        mode.Text = floating ? "Dock" : "Pop out";
        mode.Enabled = !floating || canDock;
        mode.ToolTipText = floating && !canDock ? "Help will return when the parameter dialog closes." : floating ? "Dock help beside the workspace" : "Open help in a separate window";
    }

    internal async Task PreservePositionAsync(Action move)
    {
        var core = Browser.CoreWebView2;
        if (core == null) { move(); return; }
        // Retain the requested position if a larger viewport clamps it to the
        // page end. A deliberate browser interaction clears it; a subsequent
        // Dock can then restore the reading position at the original width.
        await core.ExecuteScriptAsync("window.__sdHelpPosition ??= [...document.querySelectorAll('*')].filter(e=>e.scrollTop || e.scrollLeft).map(e=>({e,x:e.scrollLeft,y:e.scrollTop}));").WaitAsync(TimeSpan.FromSeconds(5));
        move();
        if (IsDisposed) return;
        await core.ExecuteScriptAsync("requestAnimationFrame(()=>requestAnimationFrame(()=>{for(const p of window.__sdHelpPosition || []) if(p.e.isConnected) p.e.scrollTo(p.x,p.y);}));").WaitAsync(TimeSpan.FromSeconds(5));
    }

    internal void ShowNotice(string message) => status.Text = message;

    internal void ShowTopic(string topic)
    {
        Uri page = catalog.Resolve(topic);
        if (page == null && HelpCatalog.IsExternal(topic)) { OpenExternal(topic); return; }
        status.Text = page == null ? "This topic is unavailable; showing the help contents." : "";
        requested = (page ?? catalog.Resolve("1000") ?? new Uri(HelpCatalog.Origin + "/index.html")).AbsoluteUri;
        NavigateRequested();
    }

    internal void Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) { search.Focus(); return; }
        search.Text = query;
        requested = HelpCatalog.Origin + "/index.html#searchQuery=" + Uri.EscapeDataString(query.Trim());
        status.Text = "";
        NavigateRequested();
    }

    private async Task BrowseTopicsAsync()
    {
        var core = Browser.CoreWebView2;
        if (core == null || !HelpCatalog.IsLocal(core.Source)) return;

        string source = core.Source;
        try
        {
            Browser.Focus();
            string opened = await core.ExecuteScriptAsync(BrowseTopicsScript).WaitAsync(TimeSpan.FromSeconds(5));
            if (IsDisposed || core.Source != source) return;
            if (opened == "\"focused\"")
            {
                if (status.Text.StartsWith(BrowseFailure, StringComparison.Ordinal)) status.Text = "";
                return;
            }

            // Standalone topics in the alternative layouts have no tree.
            // Only that result permits opening the reader; a focus or script
            // failure must leave the reading page and its history intact.
            if (opened == "\"no-contents\"")
            {
                var page = new Uri(source);
                if (page.AbsolutePath != "/index.html")
                {
                    requested = HelpCatalog.Origin + "/index.html#topic=" + Uri.EscapeDataString(page.AbsolutePath.TrimStart('/') + page.Fragment);
                    NavigateRequested();
                    return;
                }
            }
            status.Text = BrowseFailure + ". Use Home or search to find a topic.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or TimeoutException)
        {
            if (!IsDisposed && core.Source == source) status.Text = BrowseFailure + ": " + ex.Message;
        }
    }

    // Keep this script portable: the Mac host mirrors the same text.
    private const string BrowseTopicsScript = """
        (() => {
            const contents = document.getElementById('contents');
            if (!contents) return 'no-contents';
            const navigation = document.getElementById('help-navigation');
            const toggle = document.getElementById('navigation-toggle');
            if (toggle && navigation && getComputedStyle(navigation).display === 'none') toggle.click();
            if (contents.hidden || document.getElementById('results')?.hidden === false)
                document.getElementById('contents-tab')?.click();
            const path = location.pathname.slice(1);
            const folder = path.slice(0, path.lastIndexOf('/') + 1);
            const overview = folder ? [...contents.querySelectorAll('details > ul > li:first-child > a')]
                .find(a => a.dataset.link?.startsWith(folder)) : null;
            const link = contents.querySelector('a[aria-current="page"]') ?? overview ?? contents.querySelector('a');
            if (!link) return 'not-focused';
            for (let p = link.parentElement; p && p !== contents; p = p.parentElement)
                if (p.tagName === 'DETAILS') p.open = true;
            if (!link.getClientRects().length) return 'not-focused';
            (link.closest('li') ?? link).scrollIntoView({block: 'nearest', inline: 'nearest'});
            link.focus({preventScroll: true});
            return document.activeElement === link ? 'focused' : 'not-focused';
        })()
        """;

    private void NavigateRequested()
    {
        if (Browser.CoreWebView2 == null || !ready.Task.IsCompletedSuccessfully || requested == null) return;
        if (Browser.CoreWebView2.Source != requested) Browser.CoreWebView2.Navigate(requested);
    }

    private async Task InitializeAsync()
    {
        if (starting) return;
        starting = true;
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            if (IsDisposed) return;
            await Browser.EnsureCoreWebView2Async(environment);
            if (IsDisposed) return;
            var core = Browser.CoreWebView2;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(NativeToolbarStyle);
            await core.AddScriptToExecuteOnDocumentCreatedAsync("for(const type of ['wheel','pointerdown','keydown']) addEventListener(type,()=>{window.__sdHelpPosition=null;},{capture:true,passive:true});");
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.SetVirtualHostNameToFolderMapping("help.statsdirect.invalid", catalog.Directory, CoreWebView2HostResourceAccessKind.DenyCors);
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (!e.IsUserInitiated) return;
                if (HelpCatalog.IsLocal(e.Uri)) core.Navigate(e.Uri);
                else OpenExternal(e.Uri);
            };
            core.NavigationStarting += (_, e) =>
            {
                if (HelpCatalog.IsLocal(e.Uri))
                {
                    browse.Enabled = false;
                    index.Enabled = false;
                    return;
                }
                e.Cancel = true;
                if (e.IsUserInitiated) OpenExternal(e.Uri);
            };
            core.FrameNavigationStarting += (_, e) => { if (!HelpCatalog.IsLocal(e.Uri)) e.Cancel = true; };
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (!HelpCatalog.IsLocal(e.Request.Uri) && !e.Request.Uri.StartsWith("data:", StringComparison.Ordinal) && !e.Request.Uri.StartsWith("blob:" + HelpCatalog.Origin + "/", StringComparison.Ordinal))
                    e.Response = environment.CreateWebResourceResponse(null, 403, "External resources are disabled", "Content-Type: text/plain");
            };
            core.HistoryChanged += (_, _) => { back.Enabled = core.CanGoBack; forward.Enabled = core.CanGoForward; };
            core.DocumentTitleChanged += (_, _) =>
            {
                PageTitle = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "StatsDirect Help" : core.DocumentTitle + " — StatsDirect Help";
                TitleChanged?.Invoke();
            };
            core.NavigationCompleted += (_, e) =>
            {
                browse.Enabled = e.IsSuccess || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled;
                index.Enabled = browse.Enabled;
                if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled) status.Text = "The help page could not load: " + e.WebErrorStatus;
            };
            core.ProcessFailed += (_, _) => status.Text = "The help browser stopped. Close and reopen Help to continue.";
            ready.TrySetResult();
            if (requested == null) ShowTopic("1000"); else NavigateRequested();
        }
        catch (Exception ex)
        {
            status.Text = ex.Message + " Run StatsDirectSetup.exe to repair WebView2 if needed.";
            ready.TrySetException(ex);
            // The visible status is the UI error path; observe the task even when no test awaits it.
            _ = ready.Task.Exception;
        }
    }

    // The web reader still supplies the Contents tree and search results.
    // Hide its redundant toolbar only inside this native help window.
    private const string NativeToolbarStyle = """
        (() => {
            if (window !== window.top || location.origin !== 'https://help.statsdirect.invalid') return;
            const style = document.createElement('style');
            style.textContent = `
                :root[data-help-skin]:has(> body > header > .help-identity){--help-height:0px !important}
                html[data-help-skin] > body > header:has(> .help-identity),
                html[data-help-skin] > body > main > aside > #search-form{display:none !important}
            `;
            if (document.head) document.head.append(style);
            else {
                const observer = new MutationObserver(() => {
                    if (!document.head) return;
                    document.head.append(style);
                    observer.disconnect();
                });
                observer.observe(document,{childList:true,subtree:true});
            }
        })();
        """;

    private void OpenExternal(string uri)
    {
        if (!HelpCatalog.IsExternal(uri)) return;
        if (ExternalLinkRequested != null) { ExternalLinkRequested(uri); return; }
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { status.Text = "Could not open the link: " + ex.Message; }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.L)) { search.Focus(); search.SelectAll(); return true; }
        if (keyData == (Keys.Alt | Keys.Left) && Browser.CoreWebView2?.CanGoBack == true) { Browser.CoreWebView2.GoBack(); return true; }
        if (keyData == (Keys.Alt | Keys.Right) && Browser.CoreWebView2?.CanGoForward == true) { Browser.CoreWebView2.GoForward(); return true; }
        if (keyData == Keys.F1) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { ready.TrySetCanceled(); indexMenu.Dispose(); }
        base.Dispose(disposing);
    }
}
