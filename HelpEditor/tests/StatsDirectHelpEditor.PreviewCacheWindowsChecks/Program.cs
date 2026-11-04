using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static class Program
{
    private static MainForm form = null!;
    private static string root = "", testRoot = "", fixture = "";
    private static readonly List<object> checks = [];
    private static readonly List<CoreWebView2Frame> frames = [];
    private static int passed, failed;
    [STAThread]
    private static int Main(string[] args)
    {
        root = Path.GetFullPath(args[0]); testRoot = Path.Combine(Path.GetTempPath(), "sdhe-cache-" + Guid.NewGuid().ToString("N")[..8]);
        fixture = Path.Combine(testRoot, "HelpSource"); CreateFixture();
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Check(false, "Unhandled native exception", e.Exception.ToString());
        form = new MainForm(fixture, _ => throw new InvalidOperationException("Unexpected external URL"), initialTopic: "basics/alpha.htm", workspaceOverride: new HelpEditorWorkspace(testRoot, fixture, null))
        { SuppressDialogs = true, WindowState = FormWindowState.Normal, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new(-16000, 0), Size = new(1500, 900) };
        form.Shown += async (_, _) =>
        {
            try { await Run(); }
            catch (Exception ex) { Check(false, "Native preview checks completed", ex.ToString()); }
            finally
            {
                Directory.CreateDirectory(Path.Combine(root, "evidence"));
                File.WriteAllText(Path.Combine(root, "evidence", "preview-cache-windows-checks.json"), JsonSerializer.Serialize(new { checkedAtUtc = DateTimeOffset.UtcNow, fixture, deviceDpi = form.DeviceDpi, passed = failed == 0, total = passed + failed, passedCount = passed, failedCount = failed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{passed} preview cache checks passed; {failed} failed");
                typeof(MainForm).GetField("closingApproved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, true); form.Close();
            }
        };
        Application.Run(form); return failed == 0 ? 0 : 1;
    }
    private static async Task Run()
    {
        await form.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
        form.EditorWebView.CoreWebView2.FrameCreated += (_, e) => Track(e.Frame);
        var firstWatch = Stopwatch.StartNew(); await form.SwitchWorkspaceAsync("public"); firstWatch.Stop();
        Check(form.OutputPreviewBuildCount == 1 && !form.LastOutputPreviewWasReused, "The first Browser view builds one complete isolated target");
        var first = form.LastOutputPreviewResult!; string info = File.ReadAllText(Path.Combine(first.OutputDirectory, "build-info.json"));
        var sourceBaseline = Directory.GetFiles(fixture, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        await form.SwitchWorkspaceAsync("public");
        Check(form.OutputPreviewBuildCount == 1 && form.LastOutputPreviewWasReused && ReferenceEquals(first, form.LastOutputPreviewResult), "Selecting Browser view again reuses the prepared target");
        var navWatch = Stopwatch.StartNew();
        await ObserveNavigationAsync("All pages selection", async () =>
        {
            var pages = Field<ListBox>("pages");
            pages.SelectedItem = pages.Items.Cast<TopicInfo>().Single(topic => topic.Path == "basics/beta.htm");
            await Until(() => form.CurrentTopicPath == "basics/beta.htm" && !Field<bool>("busy"));
        });
        navWatch.Stop();
        Check(form.OutputPreviewBuildCount == 1 && form.LastOutputPreviewWasReused && ReferenceEquals(first, form.LastOutputPreviewResult), "Navigating to another saved page does not regenerate help or search");
        Check(File.ReadAllText(Path.Combine(first.OutputDirectory, "build-info.json")) == info, "Saved-page navigation leaves the build receipt unchanged", new { initialBuildMs = firstWatch.ElapsedMilliseconds, savedNavigationMs = navWatch.ElapsedMilliseconds });
        Check(await BrowserHas("location.pathname.endsWith('/basics/beta.htm')"), "Cached All pages navigation opens the selected generated topic");
        await ObserveNavigationAsync("Generated Browser Contents", async () =>
        {
            await ClickContents("basics/alpha.htm#AlphaTarget");
            await Until(() => form.CurrentTopicPath == "basics/alpha.htm" && !Field<bool>("busy"));
        });
        Check(form.CurrentWorkspace == "public" && form.OutputPreviewBuildCount == 1 && form.LastOutputPreviewWasReused, "Clicking the generated Browser Contents follows the saved page without a build");
        Check(await BrowserHas("location.pathname.endsWith('/basics/alpha.htm') && location.hash==='#AlphaTarget'"), "Contents-link navigation retains its fragment while reusing output");
        await ObserveNavigationAsync("Editor Contents tree", async () =>
        {
            var contents = Field<TreeView>("contents");
            contents.SelectedNode = contents.Nodes.Cast<TreeNode>().Single(n => ((TocNode)n.Tag!).Link == "basics/beta.htm#BetaTarget");
            await Until(() => form.CurrentTopicPath == "basics/beta.htm" && !Field<bool>("busy"));
        });
        Check(form.OutputPreviewBuildCount == 1 && form.LastOutputPreviewWasReused, "Selecting the editor Contents tree also reuses the same generated site");
        Check(await BrowserHas("location.pathname.endsWith('/basics/beta.htm') && location.hash==='#BetaTarget'"), "Native Contents selection preserves its requested page fragment");
        Check(sourceBaseline.All(p => p.Value.AsSpan().SequenceEqual(File.ReadAllBytes(p.Key))), "All saved navigation and fragments leave the source files unchanged");

        string betaSaved = form.CurrentProject.ReadTopic("basics/beta.htm");
        await form.SwitchWorkspaceAsync("edit"); await SetSource(betaSaved.Replace("BETA_BASELINE", "BETA_DRAFT_ONE")); await form.SwitchWorkspaceAsync("public");
        int draftCount = form.OutputPreviewBuildCount;
        Check(draftCount == 2 && !form.LastOutputPreviewWasReused && Output("basics/beta.htm").Contains("BETA_DRAFT_ONE"), "The current unsaved draft invalidates output and is rendered without being saved");
        await form.SwitchWorkspaceAsync("public");
        Check(form.OutputPreviewBuildCount == draftCount && form.LastOutputPreviewWasReused && Output("basics/beta.htm").Contains("BETA_DRAFT_ONE"), "An unchanged draft reuses its generated preview");
        await form.SwitchWorkspaceAsync("edit"); await SetSource(betaSaved.Replace("BETA_BASELINE", "BETA_DRAFT_TWO")); await form.SwitchWorkspaceAsync("public");
        Check(form.OutputPreviewBuildCount == draftCount + 1 && Output("basics/beta.htm").Contains("BETA_DRAFT_TWO") && !Output("basics/beta.htm").Contains("BETA_DRAFT_ONE"), "Changing the draft never serves the older cached draft");
        await form.OpenTopicAsync("basics/alpha.htm"); // Test-only explicit discard of this owned fixture's draft.
        Check(form.OutputPreviewBuildCount == draftCount + 2 && Output("basics/beta.htm").Contains("BETA_BASELINE") && !Output("basics/beta.htm").Contains("BETA_DRAFT_"), "Leaving a discarded draft removes its overlay from the complete saved preview");
        int savedCount = form.OutputPreviewBuildCount;
        await form.OpenTopicAsync("basics/beta.htm");
        Check(form.OutputPreviewBuildCount == savedCount && form.LastOutputPreviewWasReused, "Subsequent saved navigation reuses output after the old draft is removed");

        string alphaPath = Path.Combine(fixture, "Content", "basics", "alpha.htm"); var alphaTime = File.GetLastWriteTimeUtc(alphaPath); string alpha = File.ReadAllText(alphaPath);
        File.WriteAllText(alphaPath, alpha.Replace("ALPHA_BASELINE", "ALPHA_EXTERNAL"), new UTF8Encoding(false)); File.SetLastWriteTimeUtc(alphaPath, alphaTime);
        await form.SwitchWorkspaceAsync("public");
        Check(form.OutputPreviewBuildCount == savedCount + 1 && !form.LastOutputPreviewWasReused && Output("basics/alpha.htm").Contains("ALPHA_EXTERNAL"), "External saved-page edits invalidate the cache even with identical length and timestamp");
        string css = Path.Combine(fixture, "Content", "resources", "stylesheets", "statsdirect.css"); File.AppendAllText(css, "\np{color:#123456}");
        int beforeCss = form.OutputPreviewBuildCount; await form.SwitchWorkspaceAsync("public");
        Check(form.OutputPreviewBuildCount == beforeCss + 1 && File.ReadAllText(Path.Combine(form.LastOutputPreviewResult!.OutputDirectory, "resources", "stylesheets", "statsdirect.css")).Contains("#123456"), "Stylesheet and artwork assets participate in preview invalidation");
        string toc = Path.Combine(fixture, "Project", "TOCs", "STATSDIRECT.fltoc"); File.WriteAllText(toc, File.ReadAllText(toc).Replace("Alpha title", "Updated Alpha title"));
        int beforeToc = form.OutputPreviewBuildCount; await form.SwitchWorkspaceAsync("public");
        Check(form.OutputPreviewBuildCount == beforeToc + 1 && File.ReadAllText(Path.Combine(form.LastOutputPreviewResult!.OutputDirectory, "contents-data.js")).Contains("Updated Alpha title"), "Contents edits rebuild navigation instead of reusing stale links");
        int beforeDesign = form.OutputPreviewBuildCount; await form.SetDesignAsync("clean-blue");
        Check(form.OutputPreviewBuildCount == beforeDesign + 1 && Output("basics/beta.htm").Contains("data-help-skin=\"clean-blue\""), "A changed target design invalidates its generated preview");
        int afterDesign = form.OutputPreviewBuildCount; await form.OpenTopicAsync("basics/alpha.htm");
        Check(form.OutputPreviewBuildCount == afterDesign && form.LastOutputPreviewWasReused, "Navigation within the changed design reuses the rebuilt target");
        string newPage = Path.Combine(fixture, "Content", "unlisted.htm"); File.WriteAllText(newPage, Page("Unlisted", "<p>NEW_PAGE_CONTEXT</p>"));
        int beforeAdd = form.OutputPreviewBuildCount; await form.SwitchWorkspaceAsync("public");
        Check(form.OutputPreviewBuildCount == beforeAdd + 1 && form.LastOutputPreviewResult!.TopicCount == 4 && Output("unlisted.htm").Contains("NEW_PAGE_CONTEXT"), "Adding an unlisted page updates the whole generated site and search");
        File.Move(newPage, Path.Combine(testRoot, "unlisted-removed-page.probe")); await form.SwitchWorkspaceAsync("public");
        Check(form.LastOutputPreviewResult!.TopicCount == 3 && !File.Exists(Path.Combine(form.LastOutputPreviewResult.OutputDirectory, "unlisted.htm")), "Deleting a page invalidates the preview and removes obsolete generated output");
        int beforeMissing = form.OutputPreviewBuildCount; File.Move(Path.Combine(form.LastOutputPreviewResult.OutputDirectory, "search-index.js"), Path.Combine(testRoot, "search-index-missing.probe")); await form.SwitchWorkspaceAsync("public");
        Check(form.OutputPreviewBuildCount == beforeMissing + 1 && File.Exists(Path.Combine(form.LastOutputPreviewResult!.OutputDirectory, "search-index.js")), "A missing generated search index triggers a rebuild instead of a broken cached site");
        string betaPath = Path.Combine(fixture, "Content", "basics", "beta.htm"); string betaBeforeFailure = File.ReadAllText(betaPath);
        File.WriteAllText(betaPath, "<html><body>broken external page"); bool buildFailed = false;
        try { await form.SwitchWorkspaceAsync("public"); } catch (System.Xml.XmlException) { buildFailed = true; }
        Check(buildFailed, "A malformed external page surfaces the rebuild failure instead of silently reusing old help");
        File.WriteAllText(betaPath, betaBeforeFailure); int beforeRevert = form.OutputPreviewBuildCount; await form.SwitchWorkspaceAsync("public");
        Check(form.OutputPreviewBuildCount == beforeRevert + 1 && !form.LastOutputPreviewWasReused && Output("basics/beta.htm").Contains("BETA_BASELINE"), "After a failed build, reverting source forces a complete valid rebuild instead of trusting potentially partial cached output");
        int beforeDesktop = form.OutputPreviewBuildCount; await form.SwitchWorkspaceAsync("desktop");
        Check(form.OutputPreviewBuildCount == beforeDesktop + 1 && !form.LastOutputPreviewWasReused, "App preview builds its independent target only when first needed");
        int desktop = form.OutputPreviewBuildCount; await form.OpenTopicAsync("basics/beta.htm");
        Check(form.OutputPreviewBuildCount == desktop && form.LastOutputPreviewWasReused, "Saved-page App-pane navigation reuses the prepared Desktop target too");
        await form.SwitchWorkspaceAsync("public");
        Check(form.OutputPreviewBuildCount == desktop && form.LastOutputPreviewWasReused, "Switching back to Browser view reuses its independently cached target");
        await form.SwitchWorkspaceAsync("edit"); await SetSource("<html><body>unfinished"); bool refused = false;
        try { await form.SwitchWorkspaceAsync("public"); } catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException) { refused = true; }
        Check(refused && form.CurrentWorkspace == "edit" && await Source() == null && await JsString("document.getElementById('source-editor').value") == "<html><body>unfinished", "A malformed draft is refused and preserved instead of showing a stale cached preview");
        await SetSource(form.CurrentProject.ReadTopic("basics/beta.htm"));
        Check(form.CurrentProject.RootPath == fixture && !fixture.Equals(Path.Combine(root, "HelpSource"), StringComparison.OrdinalIgnoreCase), "All cache tests use an isolated help source without modifying maintained pages");
    }
    private static string Output(string path) => File.ReadAllText(Path.Combine(form.LastOutputPreviewResult!.OutputDirectory, path.Replace('/', Path.DirectorySeparatorChar)));
    private static async Task ObserveNavigationAsync(string name, Func<Task> navigate)
    {
        using var probe = new NavigationProbe();
        await Js("window.__navigationPaintProbe={records:[]};window.__navigationPaintObserver=new MutationObserver(records=>{for(const r of records)window.__navigationPaintProbe.records.push({target:r.target.id||'body',attribute:r.attributeName,before:r.oldValue,after:r.target.getAttribute(r.attributeName)});});for(const id of ['preview-panel','visual-panel','source-panel','editor-toolbar'])window.__navigationPaintObserver.observe(document.getElementById(id),{attributes:true,attributeOldValue:true,attributeFilter:['hidden']});window.__navigationPaintObserver.observe(document.body,{attributes:true,attributeOldValue:true,attributeFilter:['data-workspace-view']});");
        await navigate();
        var dom = JsonSerializer.Deserialize<JsonElement>(await form.EditorWebView.ExecuteScriptAsync("(()=>{window.__navigationPaintObserver.disconnect();return {view:HelpEditor.getState().workspaceView,previewHidden:document.getElementById('preview-panel').hidden,records:window.__navigationPaintProbe.records};})()"));
        Check(probe.Transitions.All(pair => pair.Key == "editor.Enabled") && probe.InputWasBlocked && !probe.InputReleasedWhileBusy,
            name + " does not disable, hide or resize the native shell, or flash its progress indicator",
            new { probe.Transitions, probe.Events, probe.InputWasBlocked, probe.InputReleasedWhileBusy });
        bool stable = dom.GetProperty("view").GetString() == "public" && !dom.GetProperty("previewHidden").GetBoolean() &&
            probe.Views.All(view => view == "public") && dom.GetProperty("records").EnumerateArray().All(record =>
                record.GetProperty("before").GetRawText() == record.GetProperty("after").GetRawText());
        Check(stable, name + " keeps Browser view visible without a transient Formatted or source view",
            new { ViewNotifications = probe.Views, Dom = dom });
    }
    private sealed class NavigationProbe : IDisposable
    {
        internal Dictionary<string, int> Transitions { get; } = [];
        internal List<object> Events { get; } = [];
        internal List<string> Views { get; } = [];
        internal bool InputWasBlocked { get; private set; }
        internal bool InputReleasedWhileBusy { get; private set; }
        private readonly List<Action> unsubscribe = [];
        internal NavigationProbe()
        {
            foreach (var entry in new (string Name, Control Control)[]
            {
                ("menu", form.MainMenuStrip!), ("contents", Field<TreeView>("contents")), ("pages", Field<ListBox>("pages")),
                ("contentsBar", Field<ToolStrip>("contentsBar")), ("pageFilter", Field<TextBox>("pageFilter")),
                ("designBar", Field<ToolStrip>("designBar")), ("editor", form.EditorWebView)
            })
            {
                string name = entry.Name; Control control = entry.Control;
                EventHandler enabled = (_, _) => Record(name + ".Enabled", new { control.Enabled });
                EventHandler visible = (_, _) => Record(name + ".Visible", new { control.Visible });
                EventHandler size = (_, _) => Record(name + ".Size", new { control.Size });
                control.EnabledChanged += enabled; control.VisibleChanged += visible; control.SizeChanged += size;
                unsubscribe.Add(() => { control.EnabledChanged -= enabled; control.VisibleChanged -= visible; control.SizeChanged -= size; });
            }
            var progress = Field<ToolStripProgressBar>("progress");
            EventHandler available = (_, _) => Record("progress.Available", new { progress.Available, progress.Visible });
            progress.AvailableChanged += available; unsubscribe.Add(() => progress.AvailableChanged -= available);
            EventHandler<CoreWebView2WebMessageReceivedEventArgs> message = (_, args) =>
            {
                if (!args.Source.StartsWith("https://help-editor.invalid/", StringComparison.Ordinal)) return;
                using var data = JsonDocument.Parse(args.WebMessageAsJson);
                if (data.RootElement.TryGetProperty("action", out var action) && action.GetString() == "view" &&
                    data.RootElement.TryGetProperty("workspaceView", out var view) && view.ValueKind == JsonValueKind.String)
                    Views.Add(view.GetString()!);
            };
            form.EditorWebView.CoreWebView2.WebMessageReceived += message;
            unsubscribe.Add(() => form.EditorWebView.CoreWebView2.WebMessageReceived -= message);
        }
        private void Record(string name, object value)
        {
            if (name == "editor.Enabled")
            {
                if (!form.EditorWebView.Enabled && Field<bool>("busy")) InputWasBlocked = true;
                if (form.EditorWebView.Enabled && Field<bool>("busy")) InputReleasedWhileBusy = true;
            }
            Transitions[name] = Transitions.GetValueOrDefault(name) + 1; Events.Add(new { name, value });
        }
        public void Dispose() { foreach (var action in unsubscribe) action(); }
    }
    private static T Field<T>(string name) => (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
    private static Task Js(string script) => form.EditorWebView.ExecuteScriptAsync(script);
    private static async Task<string?> Source() => JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync("HelpEditor.getSource()"));
    private static async Task<string> JsString(string script) => JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync(script))!;
    private static Task SetSource(string source) => Js("HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(source) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}))");
    private static void Track(CoreWebView2Frame frame) { frames.Add(frame); frame.FrameCreated += (_, e) => Track(e.Frame); }
    private static async Task<bool> BrowserHas(string expression)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < until)
        {
            foreach (var frame in frames.ToArray().Reverse()) try { if (JsonSerializer.Deserialize<bool>(await frame.ExecuteScriptAsync("Boolean(" + expression + ")"))) return true; } catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            await Task.Delay(40);
        }
        return false;
    }
    private static async Task ClickContents(string path)
    {
        string script = "(()=>{const a=Array.from(document.querySelectorAll('#contents a')).find(a=>a.dataset.link===" + JsonSerializer.Serialize(path) + ");if(!a)return false;a.click();return true;})()";
        foreach (var frame in frames.ToArray().Reverse()) try { if (JsonSerializer.Deserialize<bool>(await frame.ExecuteScriptAsync(script))) return; } catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        throw new InvalidOperationException("Generated Contents link was not available.");
    }
    private static async Task Until(Func<bool> condition) { var until = DateTime.UtcNow.AddSeconds(20); while (!condition()) { if (DateTime.UtcNow >= until) throw new TimeoutException("Native navigation did not complete."); await Task.Delay(40); } }
    private static void Check(bool value, string name, object? detail = null) { if (value) passed++; else failed++; checks.Add(new { name, passed = value, detail }); Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}"); }
    private static string Page(string title, string body) => "<html><head><title>" + title + "</title></head><body>" + body + "</body></html>";
    private static void CreateFixture()
    {
        string expected = Path.GetFullPath(Path.Combine(testRoot, "HelpSource"));
        if (!fixture.Equals(expected, StringComparison.OrdinalIgnoreCase) || !fixture.StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture path.");
        if (Directory.Exists(fixture)) throw new InvalidOperationException("The isolated fixture must be new; existing evidence is retained.");
        Directory.CreateDirectory(Path.Combine(fixture, "Content", "basics")); Directory.CreateDirectory(Path.Combine(fixture, "Content", "resources", "stylesheets")); Directory.CreateDirectory(Path.Combine(fixture, "Project", "TOCs"));
        File.WriteAllText(Path.Combine(fixture, HelpProject.MarkerFile), "{\"purpose\":\"isolated preview-cache checks\"}");
        File.WriteAllText(Path.Combine(fixture, "Content", "contents.htm"), Page("Contents", "<h1>Fixture contents</h1>"));
        File.WriteAllText(Path.Combine(fixture, "Content", "basics", "alpha.htm"), Page("Alpha", "<h1 id='AlphaTarget'>Alpha</h1><p>ALPHA_BASELINE</p><a href='beta.htm#BetaTarget'>Beta</a>"));
        File.WriteAllText(Path.Combine(fixture, "Content", "basics", "beta.htm"), Page("Beta", "<h1 id='BetaTarget'>Beta</h1><p>BETA_BASELINE</p><a href='alpha.htm#AlphaTarget'>Alpha</a>"));
        File.WriteAllText(Path.Combine(fixture, "Content", "resources", "stylesheets", "statsdirect.css"), "body{font:10pt Arial}");
        File.WriteAllText(Path.Combine(fixture, "Project", "TOCs", "STATSDIRECT.fltoc"), "<CatapultToc><TocEntry Title='Contents' Link='/Content/contents.htm'/><TocEntry Title='Alpha title' Link='/Content/basics/alpha.htm#AlphaTarget'/><TocEntry Title='Beta title' Link='/Content/basics/beta.htm#BetaTarget'/></CatapultToc>");
    }
}
