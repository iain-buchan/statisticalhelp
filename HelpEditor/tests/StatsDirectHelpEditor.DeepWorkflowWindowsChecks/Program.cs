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
    private static int passed, failed;
    private const string Topic = "basics/page.htm";
    [STAThread]
    private static int Main(string[] args)
    {
        root = Path.GetFullPath(args[0]); testRoot = Path.Combine(Path.GetTempPath(), "sdhe-workflow-" + Guid.NewGuid().ToString("N")[..8]);
        fixture = Path.Combine(testRoot, "HelpSource"); CreateFixture();
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Check(false, "Unhandled workflow UI exception", e.Exception.ToString());
        form = new MainForm(fixture, _ => throw new InvalidOperationException("Unexpected external URL"), initialTopic: Topic, workspaceOverride: new HelpEditorWorkspace(testRoot, fixture, null))
        { SuppressDialogs = true, WindowState = FormWindowState.Normal, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new(-16000, 0), Size = new(1400, 900) };
        form.Shown += async (_, _) =>
        {
            try { await Run(); }
            catch (Exception ex) { Check(false, "Workflow checks completed", ex.ToString()); }
            finally
            {
                Directory.CreateDirectory(Path.Combine(root, "evidence"));
                File.WriteAllText(Path.Combine(root, "evidence", "deep-workflow-windows-checks.json"), JsonSerializer.Serialize(new { checkedAtUtc = DateTimeOffset.UtcNow, fixture, passed = failed == 0, count = passed + failed, passedCount = passed, failedCount = failed, checks }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{passed} workflow checks passed; {failed} failed");
                typeof(MainForm).GetField("closingApproved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, true); form.Close();
            }
        };
        Application.Run(form); return failed == 0 ? 0 : 1;
    }
    private static async Task Run()
    {
        await form.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
        Check(form.ArtifactRoot == testRoot && !form.IsDirty, "Workflow checks use an isolated saved page and browser profile");
        string file = Path.Combine(fixture, "Content", Topic.Replace('/', Path.DirectorySeparatorChar));
        string original = File.ReadAllText(file);
        var sentinelTime = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc); File.SetLastWriteTimeUtc(file, sentinelTime);
        int revision = Field<int>("sourceRevision");
        await form.SaveCurrentTopicAsync();
        Check(File.GetLastWriteTimeUtc(file) == sentinelTime && File.ReadAllText(file) == original,
            "Save on an unchanged page does not rewrite its file or change its timestamp");
        Check(Field<int>("sourceRevision") == revision, "No-op Save does not mark builds and Git source stale");
        await Js("chrome.webview.postMessage({action:'dirty',path:'basics/elsewhere.htm',dirty:true})");
        await Task.Delay(80);
        Check(!form.IsDirty, "A delayed dirty notification from another page cannot mark the current saved page unsaved");
        await SetSource(original);
        await form.SaveCurrentTopicAsync();
        Check(!form.IsDirty && File.GetLastWriteTimeUtc(file) == sentinelTime && Field<int>("sourceRevision") == revision,
            "A draft identical to disk is marked saved without rewriting or invalidating output");

        string renamed = original.Replace("Original title", "Revised title").Replace("Original wording", "Revised wording");
        await SetSource(renamed); await form.SaveCurrentTopicAsync();
        Check(File.ReadAllText(file) == renamed && !form.IsDirty && Field<int>("sourceRevision") == revision + 1, "An actual edit saves once and advances source state once");
        Check(form.Text.StartsWith("Revised title", StringComparison.Ordinal), "Saving a changed page title immediately updates the native window title");
        Check(Field<IReadOnlyList<TopicInfo>>("allTopics").Single(topic => topic.Path == Topic).Title == "Revised title", "The saved page title is reflected in the page catalog");

        int commands = 0, concurrent = 0, maximumConcurrent = 0;
        Func<Task> action = async () => { commands++; concurrent++; maximumConcurrent = Math.Max(maximumConcurrent, concurrent); await Task.Delay(150); concurrent--; };
        Task first = RunSafe(action), second = RunSafe(action);
        await Task.WhenAll(first, second);
        Check(commands == 1 && maximumConcurrent == 1, "Two native commands arriving before the renderer replies cannot run concurrently", new { commands, maximumConcurrent });
        Check(!Field<bool>("busy") && form.EditorWebView.Enabled, "The command guard releases controls after normal completion");
        await RunSafe(() => throw new InvalidOperationException("Owned workflow failure"));
        Check(form.LastError?.Contains("Owned workflow failure") == true && !Field<bool>("busy"), "A failed command releases the busy guard and reports its error");
        await Js("HelpEditor.openLink()");
        int beforeBlocked = commands;
        await RunSafe(action);
        Check(commands == beforeBlocked && !Field<bool>("busy") && await Bool("document.getElementById('link-dialog').open"), "An open link draft blocks native commands without leaving the shell busy");
        await Js("document.getElementById('link-cancel').click()");

        string latest = File.ReadAllText(file);
        string userDraft = latest.Replace("Revised wording", "My unsaved wording");
        string external = latest.Replace("Revised wording", "Other editor wording");
        await SetSource(userDraft);
        bool intercepted = false;
        EventHandler<CoreWebView2WebMessageReceivedEventArgs> saveRace = (_, args) =>
        {
            using var message = JsonDocument.Parse(args.WebMessageAsJson);
            if (!message.RootElement.TryGetProperty("deepSaveIntercept", out var intercept)) return;
            intercepted = true; File.WriteAllText(file, external, new UTF8Encoding(false));
        };
        form.EditorWebView.CoreWebView2.WebMessageReceived += saveRace;
        await Js("window.deepOriginalGetSource=HelpEditor.getSource;HelpEditor.getSource=()=>{const value=deepOriginalGetSource();chrome.webview.postMessage({deepSaveIntercept:true});const until=performance.now()+200;while(performance.now()<until){}return value;}");
        bool refused = false;
        try { await form.SaveCurrentTopicAsync(); } catch (InvalidOperationException) { refused = true; }
        finally { form.EditorWebView.CoreWebView2.WebMessageReceived -= saveRace; await Js("HelpEditor.getSource=deepOriginalGetSource;delete window.deepOriginalGetSource;"); }
        Check(intercepted, "The save-race fixture changes the page after disk capture and before the renderer returns");
        Check(refused && File.ReadAllText(file) == external && await Source() == userDraft && form.IsDirty,
            "An external edit made while Save reads the draft is preserved and the unsaved draft remains recoverable");
        // The author has reconciled both copies so an equal explicit Save should be harmless.
        await SetSource(external); var externalTime = File.GetLastWriteTimeUtc(file);
        bool equalSave = true;
        try { await form.SaveCurrentTopicAsync(); } catch { equalSave = false; }
        Check(equalSave && !form.IsDirty && File.GetLastWriteTimeUtc(file) == externalTime,
            "When the current draft already equals the newer disk file, Save acknowledges it without a conflict or rewrite");
        await form.OpenTopicAsync(Topic);
        Check(await Source() == external && !form.IsDirty, "Reopening the saved page uses the latest externally edited text");
        Check(Directory.GetFiles(Path.Combine(fixture, "Content"), "*.htm", SearchOption.AllDirectories).Length == 2, "Workflow checks create no unintended help topics");
        string recoveryPath = form.CurrentProject.CreateTopic("Owned recovery fixture");
        form.CurrentProject.DeleteTopic(recoveryPath);
        string id = form.CurrentProject.ListDeletedTopics().Single(item => item.OriginalPath == recoveryPath).Id;
        await Js("window.deepCatalogUpdates=0;window.deepOriginalSetTopics=HelpEditor.setTopics;HelpEditor.setTopics=(...args)=>{deepCatalogUpdates++;return deepOriginalSetTopics(...args)}");
        await form.RestoreDeletedPageAsync(id);
        int catalogUpdates = JsonSerializer.Deserialize<int>(await form.EditorWebView.ExecuteScriptAsync("deepCatalogUpdates"));
        await Js("HelpEditor.setTopics=deepOriginalSetTopics;delete window.deepOriginalSetTopics");
        Check(catalogUpdates == 1 && form.CurrentTopicPath == recoveryPath, "Restoring a deleted page updates the catalog once and awaits that update", new { catalogUpdates });
    }
    private static Task RunSafe(Func<Task> action) => (Task)typeof(MainForm).GetMethod("RunSafeAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [action])!;
    private static T Field<T>(string name) => (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
    private static Task Js(string script) => form.EditorWebView.ExecuteScriptAsync(script);
    private static async Task<bool> Bool(string script) => JsonSerializer.Deserialize<bool>(await form.EditorWebView.ExecuteScriptAsync(script));
    private static async Task<string> Source() => JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync("HelpEditor.getSource()")) ?? "";
    private static Task SetSource(string source) => Js("HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(source) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
    private static void Check(bool value, string name, object? detail = null) { if (value) passed++; else failed++; checks.Add(new { name, passed = value, detail }); Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}"); }
    private static void CreateFixture()
    {
        string expected = Path.GetFullPath(Path.Combine(testRoot, "HelpSource"));
        if (!fixture.Equals(expected, StringComparison.OrdinalIgnoreCase) || !fixture.StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture path.");
        if (Directory.Exists(fixture)) throw new InvalidOperationException("The isolated fixture must be new; existing evidence is retained.");
        Directory.CreateDirectory(Path.Combine(fixture, "Content", "basics")); Directory.CreateDirectory(Path.Combine(fixture, "Project", "TOCs"));
        File.WriteAllText(Path.Combine(fixture, HelpProject.MarkerFile), "{}");
        File.WriteAllText(Path.Combine(fixture, "Content", "contents.htm"), "<html><head><title>Contents</title></head><body><h1>Contents</h1></body></html>");
        File.WriteAllText(Path.Combine(fixture, "Content", Topic.Replace('/', Path.DirectorySeparatorChar)), "<html><head><title>Original title</title></head><body><h1>Original title</h1><p>Original wording</p></body></html>");
        File.WriteAllText(Path.Combine(fixture, "Project", "TOCs", "STATSDIRECT.fltoc"), "<CatapultToc><TocEntry Title='Contents' Link='/Content/contents.htm'/><TocEntry Title='Original title' Link='/Content/basics/page.htm'/></CatapultToc>");
    }
}
