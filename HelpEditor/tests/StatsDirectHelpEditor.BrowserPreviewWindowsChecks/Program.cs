using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static class Program
{
    private static MainForm form = null!;
    private static readonly List<object> results = [];
    private static readonly List<Uri> launched = [];
    private static int passed, failed, uiExceptions;
    private static string fixture = "", testRoot = "", evidence = "";
    private static Form? probeWindow;
    private static WebView2? probe;

    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        testRoot = Path.Combine(Path.GetTempPath(), "sdhe-browser-" + Guid.NewGuid().ToString("N")[..8]);
        fixture = Path.Combine(testRoot, "HelpSource");
        evidence = Path.Combine(root, "evidence");
        Directory.CreateDirectory(evidence);
        CreateFixture();
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => { uiExceptions++; Check(false, "Unhandled native UI exception: " + e.Exception); };
        form = new MainForm(fixture, browserPreviewLauncher: uri => launched.Add(uri), workspaceOverride: new HelpEditorWorkspace(testRoot, fixture, null)) { SuppressDialogs = true, WindowState = FormWindowState.Normal, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0), ShowInTaskbar = false, Size = new Size(1600, 1000) };
        form.Shown += async (_, _) =>
        {
            try { await RunChecks(); }
            catch (Exception ex) { Check(false, "Unexpected browser-preview check failure: " + ex); }
            finally
            {
                File.WriteAllText(Path.Combine(evidence, "browser-preview-windows-checks.json"), JsonSerializer.Serialize(new { checkedAtUtc = DateTimeOffset.UtcNow, fixture, passed, failed, results }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{passed} native browser-preview checks passed; {failed} failed");
                probeWindow?.Dispose();
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
        Check(form.ArtifactRoot == testRoot && form.CurrentTopicPath == "contents.htm", "Preview checks open an isolated source/profile root");
        await form.OpenTopicAsync("basics/alpha.htm");
        string original = form.CurrentProject.ReadTopic("basics/alpha.htm");
        string updated = original.Replace("BASELINE_TOKEN", "PreviewRebuildSentinel");
        await SetSource(updated);
        await form.SaveCurrentTopicAsync();
        await form.SwitchWorkspaceAsync("desktop");
        await form.PreviewSiteInBrowserAsync();
        Check(launched.Count == 1, "Browser preview invokes its launcher after a successful build");
        Uri desktop = launched.Single();
        string desktopIndex = Path.Combine(testRoot, "Build", "Desktop", "index.html");
        Check(desktop.IsFile && desktop.LocalPath == desktopIndex && File.Exists(desktopIndex), "Desktop preview launches the built whole-site index, not the current topic");
        Check(form.LastBuildResult?.TopicCount == 3 && File.ReadAllText(Path.Combine(testRoot, "Build", "Desktop", "basics", "alpha.htm")).Contains("PreviewRebuildSentinel"), "Preview rebuilds saved source before launching the selected Desktop target");
        await OpenProbe(desktop);
        Check(await ProbeBool("document.querySelector('#contents') && document.querySelector('#search-form') && document.querySelector('#topic') && document.querySelectorAll('#contents a').length===3"), "Whole-site preview loads navigation, search and a topic frame in file mode");
        Check(await ProbeBool("window.StatsDirectHelp.search('PreviewRebuildSentinel').some(x=>x.url==='basics/alpha.htm')"), "The rebuilt search index includes the saved page edit");
        Check(await ProbeBool("window.StatsDirectHelp.search('desktoponlysentinel').length===1 && window.StatsDirectHelp.search('publiconlysentinel').length===0"), "Desktop preview search uses Desktop conditional content");
        await ProbeJs("document.querySelector('#contents a[data-link=\"basics/alpha.htm\"]').click()");
        // location.replace deliberately keeps iframe navigation out of the
        // browser's joint history. Its original src attribute need not change.
        bool navigated = false;
        DateTime navigationLimit = DateTime.UtcNow.AddSeconds(8);
        while (!navigated && DateTime.UtcNow < navigationLimit)
        {
            using var frameTree = JsonDocument.Parse(await probe!.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.getFrameTree", "{}"));
            var rootFrame = frameTree.RootElement.GetProperty("frameTree");
            navigated = rootFrame.GetProperty("frame").GetProperty("url").GetString()!.EndsWith("/basics/alpha.htm", StringComparison.Ordinal) ||
                rootFrame.TryGetProperty("childFrames", out var children) && children.EnumerateArray()
                .Any(frame => frame.GetProperty("frame").GetProperty("url").GetString()!.EndsWith("/basics/alpha.htm", StringComparison.Ordinal));
            if (!navigated) await Task.Delay(30);
        }
        Check(navigated, "File-mode contents navigation changes the actual topic document");

        await form.SwitchWorkspaceAsync("public");
        await form.PreviewSiteInBrowserAsync();
        Uri publicIndex = launched.Last();
        Check(launched.Count == 2 && publicIndex.IsFile && publicIndex.LocalPath == Path.Combine(testRoot, "Build", "Public", "index.html"), "Public selection launches its own rebuilt whole-site index");
        await OpenProbe(publicIndex);
        Check(await ProbeBool("window.StatsDirectHelp.search('publiconlysentinel').length===1 && window.StatsDirectHelp.search('desktoponlysentinel').length===0"), "Public file-mode search excludes Desktop-only content");
        Check(await ProbeBool("window.StatsDirectHelp.search('PreviewRebuildSentinel').some(x=>x.url==='basics/alpha.htm')"), "Public preview also has the freshly rebuilt search index");

        int before = launched.Count;
        string saved = form.CurrentProject.ReadTopic("basics/alpha.htm");
        await Js("window.HelpEditor.setTab('visual');document.querySelector('.equation-widget').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));document.getElementById('equation-field').value='x^{99}';");
        bool equationBlocked = false;
        try { await form.PreviewSiteInBrowserAsync(); } catch (InvalidOperationException) { equationBlocked = true; }
        Check(equationBlocked && launched.Count == before && await Bool("window.HelpEditor.getState().equationDraftDirty"), "Browser preview is blocked without discarding an unapplied equation draft");
        await Js("document.getElementById('equation-cancel').click();window.HelpEditor.openLink(document.getElementById('beta-link'));document.getElementById('link-text').value='Unapplied link';document.getElementById('link-text').dispatchEvent(new Event('input',{bubbles:true}));");
        bool linkBlocked = false;
        try { await form.PreviewSiteInBrowserAsync(); } catch (InvalidOperationException) { linkBlocked = true; }
        Check(linkBlocked && launched.Count == before && await Bool("window.HelpEditor.getState().dialogDraftDirty"), "Browser preview is blocked without discarding an unapplied link draft");
        await Js("document.getElementById('link-cancel').click()");

        string pending = saved.Replace("</body>", "<p>Unsaved cancelled preview edit</p></body>");
        await SetSource(pending);
        var previousBuild = form.LastBuildResult;
        using (var cancel = new OwnedPromptResponse(form, 2))
        {
            await form.PreviewSiteInBrowserAsync();
            Check(cancel.Responses == 1, "The cancelled-save case handled only its own Unsaved page prompt");
        }
        Check(launched.Count == before && ReferenceEquals(previousBuild, form.LastBuildResult), "Cancelling the save prompt neither rebuilds nor launches a stale preview");
        Check(await Source() == pending && form.IsDirty && form.CurrentProject.ReadTopic("basics/alpha.htm") == saved, "Cancelling preview preserves the dirty page and its saved file");

        await SetSource("<html><body>broken");
        using (var yes = new OwnedPromptResponse(form, 6))
        {
            await form.PreviewSiteInBrowserAsync();
            Check(yes.Responses == 1, "The failed-save case answered only its own Unsaved page prompt");
        }
        Check(launched.Count == before && form.CurrentProject.ReadTopic("basics/alpha.htm") == saved && form.IsDirty, "A failed XHTML save does not build or launch a preview and preserves the editing buffer");
        await SetSource(saved);
        await form.SaveCurrentTopicAsync();

        string betaFile = Path.Combine(fixture, "Content", "basics", "beta.htm");
        string betaSource = File.ReadAllText(betaFile);
        File.WriteAllText(betaFile, "<html><body>broken");
        bool buildFailed = false;
        try { await form.PreviewSiteInBrowserAsync(); } catch { buildFailed = true; }
        finally { File.WriteAllText(betaFile, betaSource); }
        Check(buildFailed && File.Exists(publicIndex.LocalPath) && launched.Count == before, "A failed build cannot launch an older index that is already on disk");
        Check(form.CurrentProject.ReadTopic("basics/alpha.htm") == saved && uiExceptions == 0, "Preview failures leave source files intact and cause no unhandled UI exception");
    }

    private static async Task OpenProbe(Uri uri)
    {
        if (probe == null)
        {
            probe = new WebView2 { Dock = DockStyle.Fill };
            probeWindow = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-15000, 0), Size = new Size(1500, 1000), ShowInTaskbar = false };
            probeWindow.Controls.Add(probe);
            probeWindow.Show();
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(testRoot, ".file-preview-profile"));
            await probe.EnsureCoreWebView2Async(env);
            probe.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            probe.CoreWebView2.Settings.IsWebMessageEnabled = false;
        }
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult(e.IsSuccess);
        probe.CoreWebView2.NavigationCompleted += Completed;
        try { probe.CoreWebView2.Navigate(uri.AbsoluteUri); if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15))) throw new IOException("The built file-mode viewer did not load."); }
        finally { probe.CoreWebView2.NavigationCompleted -= Completed; }
        DateTime limit = DateTime.UtcNow.AddSeconds(8);
        while (!await ProbeBool("Boolean(window.StatsDirectHelp && window.StatsDirectSearch)")) { if (DateTime.UtcNow > limit) throw new TimeoutException("File-mode viewer scripts did not initialise."); await Task.Delay(30); }
    }

    private static Task Js(string script) => form.EditorWebView.ExecuteScriptAsync(script);
    private static async Task<bool> Bool(string script) => JsonSerializer.Deserialize<bool>(await form.EditorWebView.ExecuteScriptAsync(script));
    private static async Task<string> Source() => JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync("window.HelpEditor.getSource()"))!;
    private static async Task SetSource(string value) => await Js("window.HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(value) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
    private static Task ProbeJs(string script) => probe!.ExecuteScriptAsync(script);
    private static async Task<bool> ProbeBool(string script) => JsonSerializer.Deserialize<bool>(await probe!.ExecuteScriptAsync(script));
    private static void Check(bool value, string name) { if (value) passed++; else failed++; results.Add(new { name, passed = value }); Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}"); }
    private static void CreateFixture()
    {
        Directory.CreateDirectory(Path.Combine(fixture, "Content", "basics"));
        Directory.CreateDirectory(Path.Combine(fixture, "Project", "TOCs"));
        File.WriteAllText(Path.Combine(fixture, HelpProject.MarkerFile), "{\"purpose\":\"Owned whole-site browser-preview checks\"}");
        File.WriteAllText(Path.Combine(fixture, "Content", "contents.htm"), "<html><head><title>Fixture contents</title></head><body><h1>Fixture contents</h1><p>Whole-site preview fixture.</p></body></html>");
        File.WriteAllText(Path.Combine(fixture, "Content", "basics", "alpha.htm"), "<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>Alpha</title></head><body><h1>Alpha</h1><p>BASELINE_TOKEN</p><p MadCap:conditions=\"Primary.SWHELP\">DesktopOnlySentinel</p><p MadCap:conditions=\"Primary.WEBHELP\">PublicOnlySentinel</p><p><a id=\"beta-link\" href=\"beta.htm\">Beta</a></p><MadCap:equation>$x^2$</MadCap:equation></body></html>");
        File.WriteAllText(Path.Combine(fixture, "Content", "basics", "beta.htm"), "<html><head><title>Beta</title></head><body><h1>Beta</h1><p>Another page.</p></body></html>");
        File.WriteAllText(Path.Combine(fixture, "Project", "TOCs", "STATSDIRECT.fltoc"), "<CatapultToc Version=\"1\"><TocEntry Title=\"Contents\" Link=\"/Content/contents.htm\"/><TocEntry Title=\"Alpha\" Link=\"/Content/basics/alpha.htm\"/><TocEntry Title=\"Beta\" Link=\"/Content/basics/beta.htm\"/></CatapultToc>");
    }

    /// <summary>Test-only response to this form's ordinary save prompt; never any authentication window.</summary>
    private sealed class OwnedPromptResponse : IDisposable
    {
        private readonly IntPtr owner;
        private readonly int response;
        private readonly Hook callback;
        private readonly IntPtr hook;
        public int Responses { get; private set; }
        public OwnedPromptResponse(Form form, int response)
        {
            owner = form.Handle;
            this.response = response;
            callback = OnHook;
            hook = SetWindowsHookEx(5, callback, IntPtr.Zero, GetCurrentThreadId());
            if (hook == IntPtr.Zero) throw new InvalidOperationException("Could not attach the owned test prompt handler.");
        }
        private IntPtr OnHook(int code, IntPtr window, IntPtr data)
        {
            if (code == 5 && GetWindow(window, 4) == owner)
            {
                var title = new StringBuilder(128);
                var kind = new StringBuilder(64);
                GetWindowText(window, title, title.Capacity);
                GetClassName(window, kind, kind.Capacity);
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
