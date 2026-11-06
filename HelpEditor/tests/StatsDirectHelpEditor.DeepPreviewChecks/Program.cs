using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
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
    private sealed record LiveFrame(CoreWebView2Frame Frame, CancellationToken Lifetime);
    private static readonly List<LiveFrame> frames = [];
    private static readonly List<string> external = [];
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FrameProbeTimeout = TimeSpan.FromMilliseconds(750);
    private static string phase = "initialization";
    private static bool cleanupAttempted, stallBrowserProbe;
    private static int passed, failed, uiErrors, destroyedFrames, frameProbeTimeouts;
    [STAThread]
    private static int Main(string[] args)
    {
        root = Path.GetFullPath(args[0]); testRoot = Path.Combine(Path.GetTempPath(), "sdhe-preview-" + Guid.NewGuid().ToString("N")[..8]);
        fixture = Path.Combine(testRoot, "HelpSource");
        stallBrowserProbe = args.Contains("--stall-browser-probe");
        report = stallBrowserProbe ? "deep-preview-stalled-probe.json" : args.Contains("--baseline") ? "deep-preview-before.json" : "deep-preview-checks.json";
        CreateFixture();
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => { uiErrors++; Check(false, "Unhandled native preview exception", e.Exception.ToString()); CloseFixture(); };
        form = new MainForm(fixture, uri => external.Add(uri.AbsoluteUri), initialTopic: "basics/alpha.htm", workspaceOverride: new HelpEditorWorkspace(testRoot, fixture, null))
        { SuppressDialogs = true, WindowState = FormWindowState.Normal, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new(-16000, 0), Size = new(1600, 1000) };
        form.Shown += async (_, _) =>
        {
            bool corpus = args.Contains("--corpus");
            try { await Run(corpus).WaitAsync(TimeSpan.FromMinutes(corpus ? 6 : 3)); }
            catch (Exception ex) { Check(false, $"Deep preview workflows completed ({phase})", ex.ToString()); Console.WriteLine(ex.Message); }
            finally { CloseFixture(); }
        };
        try { Application.Run(form); }
        catch (Exception ex) { Check(false, "Native preview message loop completed", ex.ToString()); }
        finally
        {
            CloseFixture();
            Check(form.IsDisposed && Application.OpenForms.Count == 0, "Deep preview cleanup disposes the fixture window");
            Directory.CreateDirectory(Path.Combine(root, "evidence"));
            File.WriteAllText(Path.Combine(root, "evidence", report), JsonSerializer.Serialize(new { checkedAtUtc = DateTimeOffset.UtcNow, fixture, passed = failed == 0, total = passed + failed, passedCount = passed, failedCount = failed, uiErrors, phase, deviceDpi = form.DeviceDpi, destroyedFrames, frameProbeTimeouts, liveFrames = frames.Count, formDisposed = form.IsDisposed, openNativeFormCount = Application.OpenForms.Count, expectedFault = stallBrowserProbe, checks }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"{passed} deep preview checks passed; {failed} failed");
        }
        return failed == 0 ? 0 : 1;
    }

    private static void CloseFixture()
    {
        if (cleanupAttempted) return;
        cleanupAttempted = true;
        TryCleanup("approve close", () => FieldInfo("closingApproved").SetValue(form, true));
        TryCleanup("close", () => form.Close());
        TryCleanup("dispose", () => { if (!form.IsDisposed) form.Dispose(); });
        TryCleanup("exit message loop", Application.ExitThread);
    }

    private static void TryCleanup(string action, Action run)
    {
        try { run(); }
        catch (Exception ex) { Check(false, "Deep preview cleanup could not " + action, ex.ToString()); }
    }

    private static async Task NativeStep(string name, Func<Task> action)
    {
        SetPhase(name);
        try { await action().WaitAsync(TimeSpan.FromSeconds(60)); }
        catch (TimeoutException ex) { throw new TimeoutException("Native preview transition did not complete: " + name, ex); }
    }

    private static void SetPhase(string name)
    {
        phase = name;
        Console.WriteLine("RUN " + name);
    }

    private static async Task Run(bool corpus)
    {
        await form.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
        form.EditorWebView.CoreWebView2.FrameCreated += (_, e) => Track(e.Frame);
        var saved = Digests(fixture);
        await NativeStep("Open initial Browser preview", () => form.SwitchWorkspaceAsync("public"));
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

        await NativeStep("Open initial App preview", () => form.SwitchWorkspaceAsync("desktop"));
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

        await NativeStep("Open Browser preview for cache repair", () => form.SwitchWorkspaceAsync("public"));
        await UntilBrowser("Boolean(document.getElementById('BetaTarget'))");
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
            await NativeStep(skin + ": activate Browser view", () => form.SwitchWorkspaceAsync("public"));
            await NativeStep(skin + ": apply design", () => form.SetDesignAsync(skin));
            await NativeStep(skin + ": open Alpha topic", () => form.OpenTopicAsync("basics/alpha.htm"));
            SetPhase(skin + ": find generated Contents link");
            await UntilBrowser("Boolean(document.querySelector('#contents a[data-link=\"basics/beta.htm#BetaTarget\"]'))");
            int builds = form.OutputPreviewBuildCount;
            await BrowserScript("document.querySelector('#contents a[data-link=\"basics/beta.htm#BetaTarget\"]').click()");
            await Until(() => form.CurrentTopicPath == "basics/beta.htm" && !Field<bool>("busy"));
            SetPhase(skin + ": verify generated Contents target");
            await UntilBrowser("Boolean(document.getElementById('BetaTarget')) && location.hash==='#BetaTarget'");
            Check(form.OutputPreviewBuildCount == builds && form.CurrentWorkspace == "public", skin + " generated Contents keeps the authoring page and fragment synchronized without a rebuild");
            await BrowserScript("window.StatsDirectHelp.search('uniqueforalpha')");
            await UntilBrowser("Boolean(document.querySelector('#results a.result'))");
            await BrowserScript("document.querySelector('#results a.result').click()");
            await Until(() => form.CurrentTopicPath == "basics/alpha.htm" && !Field<bool>("busy"));
            SetPhase(skin + ": verify generated search target");
            await UntilBrowser("Boolean(document.getElementById('AlphaTarget'))");
            Check(form.OutputPreviewBuildCount == builds, skin + " search-result navigation reuses output and opens the requested authoring page");
            int beforeExternal = external.Count;
            await BrowserDocumentScript("Boolean(document.getElementById('external'))", "document.getElementById('external').click()");
            await Until(() => external.Count > beforeExternal);
            Check(external[^1] == "https://example.org/statistical-context" && form.CurrentTopicPath == "basics/alpha.htm", skin + " external links open separately and preserve the editing page");
        }
        await NativeStep("Restore Flare design for native Contents navigation", () => form.SetDesignAsync("flare"));
        var tree = Field<TreeView>("contents");
        tree.SelectedNode = tree.Nodes.Cast<TreeNode>().Single(node => ((TocNode)node.Tag!).Link == "basics/beta.htm#BetaTarget");
        await Until(() => form.CurrentTopicPath == "basics/beta.htm" && Field<string>("currentTopicFragment") == "BetaTarget" && !Field<bool>("busy"));
        await UntilBrowser("Boolean(document.getElementById('BetaTarget')) && location.hash==='#BetaTarget'");
        Check(true, "Editor Contents keeps a cross-page section fragment in the actual Browser preview");
        tree.SelectedNode = tree.Nodes.Cast<TreeNode>().Single(node => ((TocNode)node.Tag!).Link == "basics/alpha.htm#AlphaTarget");
        await Until(() => form.CurrentTopicPath == "basics/alpha.htm" && !Field<bool>("busy"));
        await form.SwitchWorkspaceAsync("edit");
        string alphaSaved = form.CurrentProject.ReadTopic("basics/alpha.htm"), draft = alphaSaved.Replace("uniqueforalpha", "uniqueforalpha unsaved author draft");
        await EditorScript("HelpEditor.setTab('source');const t=document.getElementById('source-editor');t.value=" + JsonSerializer.Serialize(draft) + ";t.dispatchEvent(new Event('input',{bubbles:true}));");
        await Task.Delay(300);
        int history = JsonSerializer.Deserialize<JsonElement>(await EditorScript("HelpEditor.getState()")).GetProperty("historyIndex").GetInt32();
        tree.SelectedNode = tree.Nodes.Cast<TreeNode>().Single(node => ((TocNode)node.Tag!).Link == "basics/alpha.htm#AlphaSecond");
        await Until(() => Field<string>("currentTopicFragment") == "AlphaSecond" && !Field<bool>("busy"));
        string afterAnchor = JsonSerializer.Deserialize<string>(await EditorScript("HelpEditor.getSource()"))!;
        int afterHistory = JsonSerializer.Deserialize<JsonElement>(await EditorScript("HelpEditor.getState()")).GetProperty("historyIndex").GetInt32();
        Check(afterAnchor == draft && form.IsDirty && history == afterHistory && form.CurrentProject.ReadTopic("basics/alpha.htm") == alphaSaved,
            "A same-page Contents section changes the anchor while preserving the exact unsaved draft and Undo history");
        await EditorScript("HelpEditor.undo()");
        Check(JsonSerializer.Deserialize<string>(await EditorScript("HelpEditor.getSource()")) == alphaSaved,
            "Normal Undo still restores the earlier edit after a same-page Contents jump");
        Check(saved.Where(p => !p.Key.EndsWith("HelpEditorDesign.json", StringComparison.OrdinalIgnoreCase)).All(p => File.Exists(p.Key) && p.Value == Hash(p.Key)), "Preview workflows do not rewrite any topic, artwork, stylesheet or Contents source");
        Check(uiErrors == 0 && form.LastError == null, "Deep preview navigation produces no unhandled or hidden host errors", form.LastError);
        if (corpus) await CorpusNavigation();
    }

    private static async Task CorpusNavigation()
    {
        SetPhase("Full-corpus Browser navigation");
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
    private static Task<string> EditorScript(string script) => ScriptReply(form.EditorWebView.ExecuteScriptAsync(script), "Authoring editor script");
    private static Task<string> AppScript(string script) => ScriptReply(form.AppPreviewWebView!.CoreWebView2.ExecuteScriptAsync(script), "App preview script");
    private static async Task<bool> AppBoolean(string expression)
    {
        try { return IsTrue(await AppScript("Boolean(" + expression + ")")); }
        catch (Exception ex) when (ex is InvalidOperationException or COMException) { return false; }
    }
    private static Task UntilApp(string expression) => WaitForState(() => AppBoolean(expression), "App: " + expression);

    private static void Track(CoreWebView2Frame frame)
    {
        var lifetime = new CancellationTokenSource();
        var entry = new LiveFrame(frame, lifetime.Token);
        frames.Add(entry);
        frame.FrameCreated += (_, e) => Track(e.Frame);
        frame.Destroyed += (_, _) =>
        {
            frames.Remove(entry);
            destroyedFrames++;
            lifetime.Cancel();
            lifetime.Dispose();
        };
    }

    private static bool InjectStalledProbe => stallBrowserProbe && phase == "warm-paper: find generated Contents link";

    private static async Task<bool> FrameMatches(LiveFrame entry, string expression)
    {
        if (entry.Lifetime.IsCancellationRequested) return false;
        try
        {
            bool injectStall = InjectStalledProbe;
            Task<string> reply = injectStall ? new TaskCompletionSource<string>().Task : entry.Frame.ExecuteScriptAsync("Boolean(" + expression + ")");
            string value = await ScriptReply(reply, "Browser frame probe: " + expression, entry.Lifetime, injectStall ? TimeSpan.FromSeconds(1) : FrameProbeTimeout);
            return !entry.Lifetime.IsCancellationRequested && IsTrue(value);
        }
        catch (OperationCanceledException) when (entry.Lifetime.IsCancellationRequested) { return false; }
        catch (Exception ex) when (ex is InvalidOperationException or COMException) { return false; }
    }
    private static async Task BrowserScript(string script)
        => await BrowserDocumentScript("Boolean(window.StatsDirectHelp)", script);
    private static async Task BrowserDocumentScript(string predicate, string script)
    {
        LiveFrame? target = null;
        // A topic's DOM can be visible before the viewer script is ready.
        await WaitForState(async () =>
        {
            target = await FindBrowserFrame(predicate);
            return target != null;
        }, "Generated viewer action target: " + script);

        // The click may already have taken effect when its reply fails. Do
        // not retry this mutating operation in another frame.
        await ScriptReply(target!.Frame.ExecuteScriptAsync(script), "Generated viewer action: " + script);
    }
    private static async Task<LiveFrame?> FindBrowserFrame(string expression)
    {
        foreach (var entry in frames.ToArray().Reverse())
        {
            // A read started during a child-frame navigation can stop answering.
            // Keep checking live frames within the overall state deadline.
            try { if (await FrameMatches(entry, expression)) return entry; }
            catch (TimeoutException) when (!InjectStalledProbe) { frameProbeTimeouts++; }
        }
        return null;
    }
    private static async Task<bool> BrowserBoolean(string expression) => await FindBrowserFrame(expression) != null;
    private static Task UntilBrowser(string expression) => WaitForState(() => BrowserBoolean(expression), "Browser: " + expression);

    private static bool IsTrue(string json) => JsonSerializer.Deserialize<JsonElement>(json).ValueKind == JsonValueKind.True;

    private static async Task<string> ScriptReply(Task<string> reply, string description, CancellationToken cancellation = default, TimeSpan? timeout = null)
    {
        try { return await reply.WaitAsync(timeout ?? ReplyTimeout, cancellation); }
        catch (TimeoutException ex) { throw new TimeoutException("No WebView2 reply for " + description, ex); }
    }

    private static async Task WaitForState(Func<Task<bool>> condition, string description)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < ReplyTimeout)
        {
            TimeSpan remaining = ReplyTimeout - timer.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            try { if (await condition().WaitAsync(remaining)) return; }
            catch (TimeoutException ex) { throw new TimeoutException("Preview state was not reached: " + description, ex); }
            await Task.Delay(40);
        }
        throw new TimeoutException("Preview state was not reached: " + description);
    }
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
