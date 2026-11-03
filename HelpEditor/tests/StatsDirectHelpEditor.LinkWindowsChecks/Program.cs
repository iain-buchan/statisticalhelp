using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static class Program
{
    private static MainForm parent = null!;
    private static readonly List<object> results = [];
    private static readonly List<Uri> external = [];
    private static int passed, failed;
    private static string fixture = "", evidence = "";

    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        string testRoot = Path.Combine(Path.GetTempPath(), "sdhe-link-" + Guid.NewGuid().ToString("N")[..8]);
        fixture = Path.Combine(testRoot, "HelpSource");
        evidence = Path.Combine(root, "evidence");
        Directory.CreateDirectory(evidence);
        CreateFixture();
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Check(false, "Unhandled UI exception: " + e.Exception);
        parent = new MainForm(fixture, uri => external.Add(uri), workspaceOverride: new HelpEditorWorkspace(testRoot, fixture, null)) { SuppressDialogs = true, WindowState = FormWindowState.Normal, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false, Location = new Point(-16000, 0), Size = new Size(1600, 1000) };
        parent.Shown += async (_, _) =>
        {
            try { await RunChecks(); }
            catch (Exception ex) { Check(false, "Unexpected native link failure: " + ex); }
            finally
            {
                File.WriteAllText(Path.Combine(evidence, "link-windows-checks.json"), JsonSerializer.Serialize(new { checkedAtUtc = DateTimeOffset.UtcNow, fixture, passed, failed, results }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{passed} native link checks passed; {failed} failed");
                foreach (MainForm window in Windows().ToArray()) ForceClose(window);
            }
        };
        using var context = new EditorApplicationContext(parent);
        Application.Run(context);
        return failed == 0 ? 0 : 1;
    }

    private static async Task RunChecks()
    {
        await parent.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
        Check(parent.CurrentTopicPath == "basics/alpha.htm" && parent.LastError == null, "Native link fixture initializes");
        Check(parent.ArtifactRoot == Path.GetDirectoryName(fixture), "Link checks use their own renderer profile and source fixture");
        var project = parent.CurrentProject;
        var unicode = HelpLinkResolver.Resolve(project, parent.CurrentTopicPath, "../references/%CE%BB%20page.htm#%CE%BB-section");
        Check(unicode.TopicPath == "references/λ page.htm" && unicode.Fragment == "λ-section", "Unicode filenames and anchors resolve after one URL decoding");
        var rootPath = HelpLinkResolver.Resolve(project, parent.CurrentTopicPath, "/Content/references/beta.htm#AnchorCase");
        Check(rootPath.TopicPath == "references/beta.htm" && rootPath.Fragment == "AnchorCase", "Content-root paths preserve anchor case");
        Check(HelpLinkResolver.Resolve(project, parent.CurrentTopicPath, "#ParentAnchor").TopicPath == "basics/alpha.htm", "Fragment-only links resolve to the current topic");
        Check(HelpLinkResolver.Resolve(project, parent.CurrentTopicPath, "https://www.statsdirect.com/help/references/beta.htm#AnchorCase").TopicPath == "references/beta.htm", "Published StatsDirect help URLs resolve to the current source copy");
        Check(HelpLinkResolver.Resolve(project, parent.CurrentTopicPath, "https://iain-buchan.github.io/statisticalhelp/references/beta.htm").TopicPath == "references/beta.htm", "GitHub Pages help URLs resolve to source topics");
        foreach (string bad in new[] { "../../../../outside.htm", "../%2e%2e/%2e%2e/outside.htm", "https://help-project.invalid/%252e%252e/outside.htm", "file:///C:/Windows/notepad.exe", "javascript:alert(1)", "data:text/html,hello", @"C:\Windows\notepad.exe", "../references/a.pdf" })
        {
            bool blocked = false;
            try { HelpLinkResolver.Resolve(project, parent.CurrentTopicPath, bad); } catch { blocked = true; }
            Check(blocked, "Unsafe/non-topic link is blocked: " + bad);
        }

        string source = project.ReadTopic("basics/alpha.htm");
        await Js(parent, "window.HelpEditor.openLink(document.getElementById('internal-link'))");
        await Wait(() => Bool(parent, "window.HelpEditor.getState().dialogOpen && window.HelpEditor.getState().dialogKind === 'link'"));
        Check(await Bool(parent, "Array.from(document.getElementById('link-page').options).some(x=>x.value==='references/λ page.htm')"), "The native host supplies the page catalog to the link dialog");
        await Js(parent, "document.getElementById('link-page').value='references/gamma.htm';document.getElementById('link-page').dispatchEvent(new Event('change',{bubbles:true}));document.getElementById('link-anchor').value='DraftAnchor';document.getElementById('link-anchor').dispatchEvent(new Event('input',{bubbles:true}));document.getElementById('link-text').value='Unapplied label';document.getElementById('link-text').dispatchEvent(new Event('input',{bubbles:true}));");
        await Wait(() => Bool(parent, "window.HelpEditor.getState().dialogDraftDirty"));
        Check(!Field<TreeView>(parent, "contents").Enabled && parent.EditorWebView.Enabled, "A pending link draft blocks native navigation while its dialog stays usable");
        bool draftSaveBlocked = false;
        try { await parent.SaveCurrentTopicAsync(); } catch (InvalidOperationException) { draftSaveBlocked = true; }
        Check(draftSaveBlocked && project.ReadTopic("basics/alpha.htm") == source, "Native Save cannot omit an unapplied link draft");
        var prior = Windows().ToHashSet();
        await Js(parent, "document.getElementById('link-open').click()");
        var gamma = await WaitForNewWindow(prior, "references/gamma.htm");
        Check(gamma.Owner == null && gamma.CurrentProject.RootPath == fixture, "Link dialog Open creates an independent editor of the same marked copy");
        Check(gamma.CurrentTopicPath == "references/gamma.htm", "Link dialog Open uses the current draft destination rather than the original link");
        Check(await Bool(gamma, "window.HelpEditor.revealFragment('DraftAnchor') && !window.HelpEditor.revealFragment('draftanchor')"), "The opened page retains case-sensitive anchors");
        Check(await Bool(parent, "window.HelpEditor.getState().dialogOpen && window.HelpEditor.getState().dialogDraftDirty") && await Source(parent) == source, "Opening the draft target keeps the parent's unapplied link and source intact");
        parent.Close();
        await Task.Delay(80);
        Check(!parent.IsDisposed && await Bool(parent, "window.HelpEditor.getState().dialogDraftDirty"), "Closing the native parent cannot discard a pending link draft");
        await Js(parent, "document.getElementById('link-cancel').click()");
        await Wait(() => Bool(parent, "!window.HelpEditor.getState().dialogOpen"));
        Check(await Source(parent) == source && !parent.IsDirty, "Cancel retains the original applied link and source");
        ForceClose(gamma);

        string dirty = source.Replace("</body>", "<p>Parent unsaved edit</p></body>");
        await Js(parent, "window.HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(dirty) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));window.HelpEditor.setTab('visual');");
        string dirtySnapshot = await Source(parent);
        prior = Windows().ToHashSet();
        await CustomOpen("internal-link");
        var beta = await WaitForNewWindow(prior, "references/beta.htm");
        Check(beta.ArtifactRoot == parent.ArtifactRoot && beta.EditorWebView.CoreWebView2.Environment.UserDataFolder == parent.EditorWebView.CoreWebView2.Environment.UserDataFolder,
            "A new link window retains the originating workspace and its isolated renderer profile");
        Check(parent.CurrentTopicPath == "basics/alpha.htm" && await Source(parent) == dirtySnapshot && parent.IsDirty, "Custom right-click Open preserves the parent's unsaved page");
        Check(beta.Owner == null, "Custom context-menu windows have no disposing Owner relationship");
        int count = Windows().Count();
        await CustomOpen("missing-link");
        await Wait(async () => { await Task.CompletedTask; return parent.LastError?.Contains("missing.htm") == true; });
        Check(Windows().Count() == count && await Source(parent) == dirtySnapshot, "A missing context-menu target reports an error without losing edits or leaving a blank window");

        await Js(parent, "window.HelpEditor.setTab('source')");
        string[] editingMenu = await NativeContext(parent, "#source-editor", false);
        Check(!editingMenu.Any(IsBrowserNavigation) && editingMenu.Any(n => n.Equals("paste", StringComparison.OrdinalIgnoreCase)), "Native editing context menu keeps Paste and removes browser reload/navigation");
        await Js(parent, "window.HelpEditor.setTab('visual');var p=document.createElement('a');p.id='native-link-probe';p.href='https://help-project.invalid/references/beta.htm#AnchorCase';p.textContent='Native link context probe';p.style.cssText='position:fixed;left:24px;top:170px;z-index:10000;background:white;padding:10px';document.body.appendChild(p);");
        prior = Windows().ToHashSet();
        string[] linkMenu = await NativeContext(parent, "#native-link-probe", true);
        var nativeBeta = await WaitForNewWindow(prior, "references/beta.htm");
        Check(linkMenu.Contains("openLinkInNewWindow"), "The actual WebView native link command is available");
        Check(nativeBeta.Owner == null && await Source(parent) == dirtySnapshot && parent.IsDirty, "Native Open link in new window reaches the shared handler and preserves the dirty parent");
        await Js(nativeBeta, "window.lastRevealedAnchor=null;var originalScroll=HTMLElement.prototype.scrollIntoView;HTMLElement.prototype.scrollIntoView=function(...args){window.lastRevealedAnchor=this.id;return originalScroll.apply(this,args)};");
        await nativeBeta.OpenTopicAsync("references/beta.htm#Percent%2520Anchor");
        await Wait(() => Bool(nativeBeta, "window.lastRevealedAnchor === 'Percent%20Anchor'"));
        Check(await Bool(nativeBeta, "window.lastRevealedAnchor === 'Percent%20Anchor'"), "Native fragment navigation decodes percent literals exactly once");

        count = Windows().Count();
        await parent.OpenLinkAsync("https://example.com/advice?a=1#part");
        Check(external.Count == 1 && external[0].AbsoluteUri == "https://example.com/advice?a=1#part" && Windows().Count() == count, "Explicit external https Open uses the injected browser launcher without opening files/programs");
        Check(await Source(parent) == dirtySnapshot && parent.IsDirty, "Opening an external link does not alter the parent's editing buffer");

        await parent.SaveCurrentTopicAsync();
        string saved = project.ReadTopic("basics/alpha.htm");
        string retitled = saved.Replace("<title>Alpha</title>", "<title>Updated Alpha title</title>");
        await Js(parent, "window.HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(retitled) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
        await parent.SaveCurrentTopicAsync();
        await Js(parent, "window.HelpEditor.openLink()");
        Check(await Bool(parent, "Array.from(document.getElementById('link-page').options).some(x=>x.value==='basics/alpha.htm' && x.textContent.includes('Updated Alpha title'))"), "Saving a page title refreshes the link catalog immediately");
        await Js(parent, "document.getElementById('link-cancel').click()");
        string created = project.CreateTopic("New catalog page", "basics");
        Invoke(parent, "ReloadProjectLists");
        await Wait(async () => { await Js(parent, "window.HelpEditor.openLink()"); return await Bool(parent, "Array.from(document.getElementById('link-page').options).some(x=>x.value===" + JsonSerializer.Serialize(created) + ")"); });
        Check(await Bool(parent, "Array.from(document.getElementById('link-page').options).some(x=>x.value===" + JsonSerializer.Serialize(created) + ")"), "List reload adds a newly created page to the link catalog");
        await Js(parent, "document.getElementById('link-cancel').click()");
        project.DeleteTopic(created);
        Invoke(parent, "ReloadProjectLists");
        await Js(parent, "window.HelpEditor.openLink()");
        Check(await Bool(parent, "!Array.from(document.getElementById('link-page').options).some(x=>x.value===" + JsonSerializer.Serialize(created) + ")"), "Deleting a page removes it from the link catalog");
        await Js(parent, "document.getElementById('link-cancel').click()");
        ForceClose(beta);
        string childSource = nativeBeta.CurrentProject.ReadTopic("references/beta.htm");
        string childDirty = childSource.Replace("</body>", "<p>Independent child unsaved edit</p></body>");
        await Js(nativeBeta, "window.HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(childDirty) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
        await Wait(async () => { await Task.CompletedTask; return nativeBeta.IsDirty; });
        parent.Close();
        await Wait(async () => { await Task.CompletedTask; return parent.IsDisposed; });
        Check(!nativeBeta.IsDisposed && Windows().Contains(nativeBeta) && nativeBeta.IsDirty, "Closing the first window keeps an independent dirty child and the message loop alive");
        Check(await Source(nativeBeta) == childDirty, "The independent child's unsaved source survives the first window closing");
        await nativeBeta.SaveCurrentTopicAsync();
        Check(nativeBeta.CurrentProject.ReadTopic("references/beta.htm") == childDirty, "The surviving child can still save normally");
    }

    private static async Task CustomOpen(string id)
    {
        await Js(parent, "var a=document.getElementById(" + JsonSerializer.Serialize(id) + ");a.dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true,button:2,clientX:60,clientY:190}));document.getElementById('context-open-link').click();");
    }
    private static async Task<string[]> NativeContext(MainForm form, string selector, bool openWindow)
    {
        var completion = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            e.Handled = true;
            string[] names = e.MenuItems.Select(i => i.Name).ToArray();
            var item = e.MenuItems.FirstOrDefault(i => i.Name == "openLinkInNewWindow");
            if (openWindow && item != null) e.SelectedCommandId = item.CommandId;
            completion.TrySetResult(names);
        }
        form.EditorWebView.CoreWebView2.ContextMenuRequested += Handler;
        try
        {
            string position = await form.EditorWebView.ExecuteScriptAsync("(()=>{var e=document.querySelector(" + JsonSerializer.Serialize(selector) + ");e.scrollIntoView({block:'center'});var r=e.getBoundingClientRect();return{x:r.left+Math.min(24,r.width/2),y:r.top+Math.min(14,r.height/2)}})()");
            using var point = JsonDocument.Parse(position);
            double x = point.RootElement.GetProperty("x").GetDouble(), y = point.RootElement.GetProperty("y").GetDouble();
            foreach (string type in new[] { "mousePressed", "mouseReleased" })
                await form.EditorWebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type, x, y, button = "right", buttons = type == "mousePressed" ? 2 : 0, clickCount = 1 }));
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
        }
        finally { form.EditorWebView.CoreWebView2.ContextMenuRequested -= Handler; }
    }
    private static bool IsBrowserNavigation(string name) => new[] { "reload", "refresh", "back", "forward", "goBack", "goForward" }.Contains(name, StringComparer.OrdinalIgnoreCase);
    private static IEnumerable<MainForm> Windows() => Application.OpenForms.OfType<MainForm>();
    private static async Task<MainForm> WaitForNewWindow(HashSet<MainForm> before, string path)
    {
        MainForm? found = null;
        await Wait(async () => { await Task.CompletedTask; found = Windows().FirstOrDefault(w => !before.Contains(w) && w.CurrentTopicPath == path && w.Initialization.IsCompletedSuccessfully); return found != null; });
        return found!;
    }
    private static T Field<T>(MainForm form, string name) => (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    private static object? Invoke(MainForm form, string name, params object?[] args) => typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
    private static Task Js(MainForm form, string script) => form.EditorWebView.ExecuteScriptAsync(script);
    private static async Task<bool> Bool(MainForm form, string script) => JsonSerializer.Deserialize<bool>(await form.EditorWebView.ExecuteScriptAsync(script));
    private static async Task<string> Source(MainForm form) => JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync("window.HelpEditor.getSource()"))!;
    private static async Task Wait(Func<Task<bool>> test)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(20);
        while (!await test()) { if (DateTime.UtcNow > end) throw new TimeoutException("The native link editor did not reach the expected state."); await Task.Delay(40); }
    }
    private static void ForceClose(MainForm form) { if (form.IsDisposed) return; typeof(MainForm).GetField("closingApproved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true); form.Close(); }
    private static void Check(bool value, string name) { if (value) passed++; else failed++; results.Add(new { name, passed = value }); Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}"); }
    private static void CreateFixture()
    {
        Directory.CreateDirectory(Path.Combine(fixture, "Content/basics"));
        Directory.CreateDirectory(Path.Combine(fixture, "Content/references"));
        Directory.CreateDirectory(Path.Combine(fixture, "Project/TOCs"));
        File.WriteAllText(Path.Combine(fixture, HelpProject.MarkerFile), "{\"purpose\":\"Isolated native link-editor checks\"}");
        File.WriteAllText(Path.Combine(fixture, "Content/basics/alpha.htm"), "<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>Alpha</title></head><body><h1>Alpha</h1><p id=\"ParentAnchor\">Parent content: <a id=\"internal-link\" href=\"../references/beta.htm#AnchorCase\">Original Beta link</a>, <a id=\"missing-link\" href=\"../references/missing.htm\">Missing target</a>.</p><p><MadCap:equation>$x^2$</MadCap:equation></p></body></html>");
        File.WriteAllText(Path.Combine(fixture, "Content/references/beta.htm"), "<html><head><title>Beta</title></head><body><h1>Beta</h1><p id=\"AnchorCase\">Case-sensitive anchor</p><p id=\"λ-section\">Unicode anchor</p><p id=\"Percent%20Anchor\">Literal percent anchor</p></body></html>");
        File.WriteAllText(Path.Combine(fixture, "Content/references/gamma.htm"), "<html><head><title>Gamma</title></head><body><h1>Gamma</h1><p id=\"DraftAnchor\">Draft destination</p></body></html>");
        File.WriteAllText(Path.Combine(fixture, "Content/references/λ page.htm"), "<html><head><title>Unicode page</title></head><body><h1 id=\"λ-section\">Unicode page</h1></body></html>");
        File.WriteAllText(Path.Combine(fixture, "Project/TOCs/STATSDIRECT.fltoc"), "<CatapultToc Version=\"1\"><TocEntry Title=\"Alpha\" Link=\"/Content/basics/alpha.htm\"/><TocEntry Title=\"Beta\" Link=\"/Content/references/beta.htm\"/><TocEntry Title=\"Gamma\" Link=\"/Content/references/gamma.htm\"/></CatapultToc>");
    }
}
