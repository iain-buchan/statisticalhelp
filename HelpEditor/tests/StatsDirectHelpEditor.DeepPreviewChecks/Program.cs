using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static class Program
{
    private static MainForm form = null!;
    private static string root = "", testRoot = "", fixture = "", report = "";
    private static readonly List<object> checks = [];
    private static readonly List<CoreWebView2Frame> frames = [];
    private static readonly List<string> external = [];
    private static int passed, failed, uiErrors;
    [STAThread]
    private static int Main(string[] args)
    {
        root = Path.GetFullPath(args[0]); testRoot = Path.Combine(Path.GetTempPath(), "sdhe-preview-" + Guid.NewGuid().ToString("N")[..8]);
        fixture = Path.Combine(testRoot, "HelpSource");
        report = args.Contains("--baseline") ? "deep-preview-before.json" : "deep-preview-checks.json";
        CreateFixture();
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => { uiErrors++; Check(false, "Unhandled native preview exception", e.Exception.ToString()); };
        form = new MainForm(fixture, uri => external.Add(uri.AbsoluteUri), initialTopic: "basics/alpha.htm", workspaceOverride: new HelpEditorWorkspace(testRoot, fixture, null))
        { SuppressDialogs = true, WindowState = FormWindowState.Normal, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new(-16000, 0), Size = new(1600, 1000) };
        form.Shown += async (_, _) =>
        {
            try { await Run(args.Contains("--corpus")); }
            catch (Exception ex) { Check(false, "Deep preview workflows completed", ex.ToString()); }
            finally
            {
                Directory.CreateDirectory(Path.Combine(root, "evidence"));
                File.WriteAllText(Path.Combine(root, "evidence", report), JsonSerializer.Serialize(new { checkedAtUtc = DateTimeOffset.UtcNow, fixture, passed = failed == 0, total = passed + failed, passedCount = passed, failedCount = failed, uiErrors, checks }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{passed} deep preview checks passed; {failed} failed");
                FieldInfo("closingApproved").SetValue(form, true); form.Close();
            }
        };
        Application.Run(form); return failed == 0 ? 0 : 1;
    }
    private static async Task Run(bool corpus)
    {
        await form.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
        form.EditorWebView.CoreWebView2.FrameCreated += (_, e) => Track(e.Frame);
        var saved = Digests(fixture);
        await form.SwitchWorkspaceAsync("public");
        await UntilBrowser("Boolean(document.getElementById('AlphaTarget'))");
        await BrowserScript("window.deepMarker='browser-first';document.querySelector('details.rcode').open=true;document.getElementById('topic').scrollTop=500");
        int firstBuilds = form.OutputPreviewBuildCount;
        await form.SwitchWorkspaceAsync("edit"); await form.SwitchWorkspaceAsync("public");
        await UntilBrowser("Boolean(document.getElementById('AlphaTarget'))");
        Check(await BrowserBoolean("window.deepMarker==='browser-first' && document.querySelector('details.rcode').open"), "Returning to an unchanged Browser view retains its document and expanded R code");
        Check(form.OutputPreviewBuildCount == firstBuilds, "Returning to an unchanged Browser view does not build the site");
        await BrowserScript("window.StatsDirectHelp.search('needleforbeta')");
        Check(await BrowserBoolean("!document.getElementById('results').hidden && document.querySelector('#results a.result')?.href.includes('beta.htm')"), "Browser search produces the matching saved page");
        await form.SwitchWorkspaceAsync("edit"); await form.SwitchWorkspaceAsync("public");
        Check(await BrowserBoolean("!document.getElementById('results').hidden && document.querySelector('#results a.result')?.href.includes('beta.htm')"), "Returning to Browser view preserves its active search instead of resetting it");

        await form.SwitchWorkspaceAsync("desktop");
        await UntilApp("Boolean(document.getElementById('AlphaTarget'))");
        int appNavigations = 0;
        form.AppPreviewWebView!.CoreWebView2.NavigationStarting += (_, _) => appNavigations++;
        await AppScript("window.deepMarker='app-first';document.querySelector('details.rcode').open=true;document.getElementById('topic').scrollTop=500");
        await form.SwitchWorkspaceAsync("edit");
        await form.SwitchWorkspaceAsync("desktop");
        await UntilApp("Boolean(document.getElementById('AlphaTarget'))");
        Check(await AppBoolean("window.deepMarker==='app-first' && document.querySelector('details.rcode').open"), "Returning to an unchanged App pane retains its document and expanded R code", new { appNavigations });
        Check(appNavigations == 0, "Returning to an unchanged App pane creates no redundant browser navigation", new { appNavigations });
        await AppScript("window.deepMarker='hide-reveal';document.querySelector('details.rcode').open=true");
        form.HideAppPreview(); await Until(() => !form.IsAppPreviewVisible && !Field<bool>("busy"));
        int beforeReveal = appNavigations; await form.SwitchWorkspaceAsync("desktop");
        await UntilApp("Boolean(document.getElementById('AlphaTarget'))");
        Check(await AppBoolean("window.deepMarker==='hide-reveal' && document.querySelector('details.rcode').open") && appNavigations == beforeReveal, "Closing and reopening an unchanged App pane reveals the same reading session");
        int beforePageChange = appNavigations;
        await form.OpenTopicAsync("basics/beta.htm#BetaTarget");
        await UntilApp("Boolean(document.getElementById('BetaTarget')) && location.hash==='#BetaTarget'");
        Check(appNavigations - beforePageChange == 1, "Selecting another authoring page navigates the App pane exactly once", new { navigations = appNavigations - beforePageChange });
        await AppScript("window.deepMarker='before-dock'");
        int beforeDock = appNavigations; await form.ToggleAppPreviewModeAsync(); await form.ToggleAppPreviewModeAsync();
        Check(await AppBoolean("window.deepMarker==='before-dock'") && appNavigations == beforeDock, "Pop out and Dock preserve the browser document without navigation");

        await form.SwitchWorkspaceAsync("public"); await UntilBrowser("Boolean(document.getElementById('BetaTarget'))");
        string cssPath = Path.Combine(form.LastOutputPreviewResult!.OutputDirectory, "help-viewer.css");
        File.Move(cssPath, Path.Combine(testRoot, "missing-viewer-style.probe")); int beforeMissingCss = form.OutputPreviewBuildCount;
        await form.SwitchWorkspaceAsync("public");
        Check(File.Exists(cssPath) && form.OutputPreviewBuildCount == beforeMissingCss + 1, "A missing generated navigation stylesheet is repaired before reusing the site");
        string scriptPath = Path.Combine(form.LastOutputPreviewResult.OutputDirectory, "help-viewer.js");
        File.Move(scriptPath, Path.Combine(testRoot, "missing-viewer-script.probe")); int beforeMissingScript = form.OutputPreviewBuildCount;
        await form.SwitchWorkspaceAsync("public");
        Check(File.Exists(scriptPath) && form.OutputPreviewBuildCount == beforeMissingScript + 1, "A missing generated navigation script is repaired before reusing the site");
        string topicCssPath = Path.Combine(form.LastOutputPreviewResult.OutputDirectory, "resources", "help-editor-runtime", "topic.css");
        File.Move(topicCssPath, Path.Combine(testRoot, "missing-topic-style.probe")); int beforeMissingTopicCss = form.OutputPreviewBuildCount;
        await form.SwitchWorkspaceAsync("public");
        Check(File.Exists(topicCssPath) && form.OutputPreviewBuildCount == beforeMissingTopicCss + 1, "A missing Flare-scoped topic stylesheet is repaired without accepting a broken cache");
        string mathPath = Path.Combine(form.LastOutputPreviewResult.OutputDirectory, "resources", "help-editor-runtime", "mathjax", "tex-mml-svg.js");
        File.Move(mathPath, Path.Combine(testRoot, "missing-math-runtime.probe")); int beforeMissingMath = form.OutputPreviewBuildCount;
        await form.SwitchWorkspaceAsync("public");
        Check(File.Exists(mathPath) && form.OutputPreviewBuildCount == beforeMissingMath + 1, "Missing equation-rendering runtime is repaired before using cached pages");

        foreach (string skin in new[] { "flare", "readable", "clean-blue", "warm-paper" })
        {
            await form.SwitchWorkspaceAsync("public"); await form.SetDesignAsync(skin); await form.OpenTopicAsync("basics/alpha.htm");
            await UntilBrowser("Boolean(document.querySelector('#contents a[data-link=\"basics/beta.htm#BetaTarget\"]'))");
            int builds = form.OutputPreviewBuildCount;
            await BrowserScript("document.querySelector('#contents a[data-link=\"basics/beta.htm#BetaTarget\"]').click()");
            await Until(() => form.CurrentTopicPath == "basics/beta.htm" && !Field<bool>("busy"));
            await UntilBrowser("Boolean(document.getElementById('BetaTarget')) && location.hash==='#BetaTarget'");
            Check(form.OutputPreviewBuildCount == builds && form.CurrentWorkspace == "public", skin + " generated Contents keeps the authoring page and fragment synchronized without a rebuild");
            await BrowserScript("window.StatsDirectHelp.search('uniqueforalpha')");
            await UntilBrowser("Boolean(document.querySelector('#results a.result'))");
            await BrowserScript("document.querySelector('#results a.result').click()");
            await Until(() => form.CurrentTopicPath == "basics/alpha.htm" && !Field<bool>("busy"));
            await UntilBrowser("Boolean(document.getElementById('AlphaTarget'))");
            Check(form.OutputPreviewBuildCount == builds, skin + " search-result navigation reuses output and opens the requested authoring page");
            int beforeExternal = external.Count;
            await BrowserDocumentScript("Boolean(document.getElementById('external'))", "document.getElementById('external').click()");
            await Until(() => external.Count > beforeExternal);
            Check(external[^1] == "https://example.org/statistical-context" && form.CurrentTopicPath == "basics/alpha.htm", skin + " external links open separately and preserve the editing page");
        }
        await form.SetDesignAsync("flare");
        var tree = Field<TreeView>("contents");
        tree.SelectedNode = tree.Nodes.Cast<TreeNode>().Single(node => ((TocNode)node.Tag!).Link == "basics/beta.htm#BetaTarget");
        await Until(() => form.CurrentTopicPath == "basics/beta.htm" && Field<string>("currentTopicFragment") == "BetaTarget" && !Field<bool>("busy"));
        await UntilBrowser("Boolean(document.getElementById('BetaTarget')) && location.hash==='#BetaTarget'");
        Check(true, "Editor Contents keeps a cross-page section fragment in the actual Browser preview");
        tree.SelectedNode = tree.Nodes.Cast<TreeNode>().Single(node => ((TocNode)node.Tag!).Link == "basics/alpha.htm#AlphaTarget");
        await Until(() => form.CurrentTopicPath == "basics/alpha.htm" && !Field<bool>("busy"));
        await form.SwitchWorkspaceAsync("edit");
        string alphaSaved = form.CurrentProject.ReadTopic("basics/alpha.htm"), draft = alphaSaved.Replace("uniqueforalpha", "uniqueforalpha unsaved author draft");
        await form.EditorWebView.ExecuteScriptAsync("HelpEditor.setTab('source');const t=document.getElementById('source-editor');t.value=" + JsonSerializer.Serialize(draft) + ";t.dispatchEvent(new Event('input',{bubbles:true}));");
        await Task.Delay(300);
        int history = JsonSerializer.Deserialize<JsonElement>(await form.EditorWebView.ExecuteScriptAsync("HelpEditor.getState()")).GetProperty("historyIndex").GetInt32();
        tree.SelectedNode = tree.Nodes.Cast<TreeNode>().Single(node => ((TocNode)node.Tag!).Link == "basics/alpha.htm#AlphaSecond");
        await Until(() => Field<string>("currentTopicFragment") == "AlphaSecond" && !Field<bool>("busy"));
        string afterAnchor = JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync("HelpEditor.getSource()"))!;
        int afterHistory = JsonSerializer.Deserialize<JsonElement>(await form.EditorWebView.ExecuteScriptAsync("HelpEditor.getState()")).GetProperty("historyIndex").GetInt32();
        Check(afterAnchor == draft && form.IsDirty && history == afterHistory && form.CurrentProject.ReadTopic("basics/alpha.htm") == alphaSaved,
            "A same-page Contents section changes the anchor while preserving the exact unsaved draft and Undo history");
        await form.EditorWebView.ExecuteScriptAsync("HelpEditor.undo()");
        Check(JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync("HelpEditor.getSource()")) == alphaSaved,
            "Normal Undo still restores the earlier edit after a same-page Contents jump");
        Check(saved.Where(p => !p.Key.EndsWith("HelpEditorDesign.json", StringComparison.OrdinalIgnoreCase)).All(p => File.Exists(p.Key) && p.Value == Hash(p.Key)), "Preview workflows do not rewrite any topic, artwork, stylesheet or Contents source");
        Check(uiErrors == 0 && form.LastError == null, "Deep preview navigation produces no unhandled or hidden host errors", form.LastError);
        if (corpus) await CorpusNavigation();
    }

    private static async Task CorpusNavigation()
    {
        string copy = Path.Combine(Path.GetDirectoryName(fixture)!, "Corpus");
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "HelpSource"), "*", SearchOption.AllDirectories))
        {
            string dest = Path.Combine(copy, Path.GetRelativePath(Path.Combine(root, "HelpSource"), file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest);
        }
        var baseline = Digests(copy); await form.SwitchWorkspaceAsync("edit"); await form.OpenProjectAsync(copy);
        await form.OpenTopicAsync("basics/basics.htm"); var timer = Stopwatch.StartNew(); await form.SwitchWorkspaceAsync("public"); timer.Stop();
        long firstBuildMs = timer.ElapsedMilliseconds; int builds = form.OutputPreviewBuildCount;
        var times = new List<long>();
        foreach (var topic in form.CurrentProject.ListTopics().Where(topic => topic.Path.StartsWith("basics/")).Take(20))
        {
            timer.Restart(); await form.OpenTopicAsync(topic.Path); timer.Stop(); times.Add(timer.ElapsedMilliseconds);
        }
        Check(form.LastOutputPreviewResult!.TopicCount == 404 && form.OutputPreviewBuildCount == builds, "Twenty actual-corpus saved page transitions reuse one 404-page preview", new { firstBuildMs, navigationCount = times.Count, totalNavigationMs = times.Sum(), maximumNavigationMs = times.Max(), averageNavigationMs = times.Average() });
        Check(baseline.All(p => File.Exists(p.Key) && p.Value == Hash(p.Key)), "Full-corpus performance checks preserve every byte of the isolated source copy");
    }
    private static Task AppScript(string script) => form.AppPreviewWebView!.CoreWebView2.ExecuteScriptAsync(script);
    private static async Task<bool> AppBoolean(string expression) { try { return JsonSerializer.Deserialize<bool>(await form.AppPreviewWebView!.CoreWebView2.ExecuteScriptAsync("Boolean(" + expression + ")")); } catch { return false; } }
    private static async Task UntilApp(string expression) { var end = DateTime.UtcNow.AddSeconds(20); while (!await AppBoolean(expression)) { if (DateTime.UtcNow > end) throw new TimeoutException("App: " + expression); await Task.Delay(40); } }
    private static void Track(CoreWebView2Frame frame) { frames.Add(frame); frame.FrameCreated += (_, e) => Track(e.Frame); }
    private static async Task BrowserScript(string script)
        => await BrowserDocumentScript("Boolean(window.StatsDirectHelp)", script);
    private static async Task BrowserDocumentScript(string predicate, string script)
    {
        foreach (var frame in frames.ToArray().Reverse()) try
        {
            if (JsonSerializer.Deserialize<JsonElement>(await frame.ExecuteScriptAsync("Boolean(" + predicate + ")")).ValueKind == JsonValueKind.True) { await frame.ExecuteScriptAsync(script); return; }
        } catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        throw new InvalidOperationException("A generated viewer document was not available for: " + script);
    }
    private static async Task<bool> BrowserBoolean(string expression)
    {
        foreach (var frame in frames.ToArray().Reverse()) try { if (JsonSerializer.Deserialize<JsonElement>(await frame.ExecuteScriptAsync("Boolean(" + expression + ")")).ValueKind == JsonValueKind.True) return true; } catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        return false;
    }
    private static async Task UntilBrowser(string expression) { var end = DateTime.UtcNow.AddSeconds(20); while (!await BrowserBoolean(expression)) { if (DateTime.UtcNow > end) throw new TimeoutException("Browser: " + expression); await Task.Delay(40); } }
    private static FieldInfo FieldInfo(string name) => typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static T Field<T>(string name) => (T)FieldInfo(name).GetValue(form)!;
    private static async Task Until(Func<bool> predicate) { var end = DateTime.UtcNow.AddSeconds(20); while (!predicate()) { if (DateTime.UtcNow > end) throw new TimeoutException("Native transition"); await Task.Delay(40); } }
    private static void Check(bool value, string name, object? detail = null) { if (value) passed++; else failed++; checks.Add(new { name, passed = value, detail }); Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}"); }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static Dictionary<string, string> Digests(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => path, Hash);
    private static string Page(string title, string body) => "<html xmlns:MadCap='http://www.madcapsoftware.com/Schemas/MadCap.xsd'><head><title>" + title + "</title></head><body>" + body + "</body></html>";
    private static void CreateFixture()
    {
        void Write(string relative, string text) { string path = Path.Combine(fixture, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        Write(HelpProject.MarkerFile, "{\"purpose\":\"isolated deep preview checks\"}");
        Write("Content/contents.htm", Page("Contents", "<h1>Fixture contents</h1>"));
        string r = "<MadCap:dropDown class='rcode'><MadCap:dropDownHead><MadCap:dropDownHotspot>R code</MadCap:dropDownHotspot></MadCap:dropDownHead><MadCap:dropDownBody><pre>x &lt;- 1\nmean(x)</pre></MadCap:dropDownBody></MadCap:dropDown>";
        Write("Content/basics/alpha.htm", Page("Alpha", "<h1 id='AlphaTarget'>Alpha</h1><p>uniqueforalpha</p>" + r + "<a id='external' href='https://example.org/statistical-context'>External context</a><a id='beta-link' href='beta.htm#BetaTarget'>Beta</a>" + string.Concat(Enumerable.Repeat("<p>Long content for reading position.</p>", 90)) + "<h2 id='AlphaSecond'>Second section</h2>"));
        Write("Content/basics/beta.htm", Page("Beta", "<h1 id='BetaTarget'>Beta</h1><p>needleforbeta</p><a href='alpha.htm#AlphaTarget'>Alpha</a>"));
        Write("Content/resources/stylesheets/statsdirect.css", "body{font:10pt Arial}");
        Write("Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title='Contents' Link='/Content/contents.htm'/><TocEntry Title='Alpha title' Link='/Content/basics/alpha.htm#AlphaTarget'/><TocEntry Title='Beta title' Link='/Content/basics/beta.htm#BetaTarget'/><TocEntry Title='Alpha second section' Link='/Content/basics/alpha.htm#AlphaSecond'/></CatapultToc>");
    }
}
