using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static partial class Program
{
    private static MainForm form = null!;
    private static string root = "", fixture = "", sourceRoot = "", preferences = "";
    private static readonly List<object> checks = [];
    private static int failures, runnerCalls, pickerCalls;
    private static string runnerMode = "success";
    private static HelpPublicationOptions options = null!, remembered = null!;
    private static HelpPublicationOptions? pickerResult;
    private static TaskCompletionSource entered = NewSignal(), release = NewSignal();
    private static Dictionary<string, string> sourceBefore = [];
    private static HelpPublicationResult? lastFakeResult;
    private const string Topic = "basics/alpha.htm";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        root = Path.GetFullPath(args[0]);
        fixture = Path.Combine(Path.GetTempPath(), "sdhe-pubui-" + Guid.NewGuid().ToString("N")[..8]);
        sourceRoot = Path.Combine(fixture, "HelpSource");
        preferences = Path.Combine(fixture, "Settings", "authoring-preferences.json");
        CreateFixture(); sourceBefore = Digests(sourceRoot);
        options = new HelpPublicationOptions
        {
            WebsiteZipPath = Path.Combine(fixture, "Outputs", "Chosen.zip"),
            StatsDirectSource = Path.Combine(fixture, "FakeStatsDirect"),
            GitHubRepositoryUrl = "https://github.com/statsdirect-tests/new-help",
            GitHubBranch = "authoring-check", AuthorName = "Test Author", AuthorEmail = "author@example.invalid",
            JournalDirectory = Path.Combine(fixture, "Publishing"), PushToGitHub = true
        };
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Check(false, "No unhandled publication UI exception", e.Exception.ToString());
        form = CreateForm();
        form.Shown += async (_, _) =>
        {
            try { await Run(); }
            catch (Exception e) { Check(false, "Native help publication checks completed", e.ToString()); }
            finally
            {
                Check(SameSources(), "Publication UI tests preserve every saved source and recovery byte");
                Check(!Directory.Exists(options.StatsDirectSource) && !File.Exists(options.WebsiteZipPath)
                    && !Directory.Exists(options.JournalDirectory), "The mock executor never creates its configured repository, ZIP or publication history");
                string evidence = Path.Combine(root, "evidence", "HelpPublicationWindows"); Directory.CreateDirectory(evidence);
                File.WriteAllText(Path.Combine(evidence, "checks.json"), JsonSerializer.Serialize(new
                {
                    checkedAtUtc = DateTimeOffset.UtcNow, passed = failures == 0, count = checks.Count, failed = failures,
                    fixture, runnerCalls, pickerCalls, noNetwork = true, noActualPublication = true, checks
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(JsonSerializer.Serialize(new { passed = failures == 0, count = checks.Count, failed = failures, fixture }));
                CloseWithoutSaving(form);
            }
        };
        Application.Run(form); return failures == 0 ? 0 : 1;
    }

    private static MainForm CreateForm() => new(sourceRoot,
        externalLinkLauncher: _ => throw new InvalidOperationException("Unexpected external navigation"), initialTopic: Topic,
        workspaceOverride: new HelpEditorWorkspace(fixture, sourceRoot, null),
        helpPublicationRunner: FakeRunner, helpPublicationOptionsPicker: PickOptions)
    {
        SuppressDialogs = true, ShowInTaskbar = false, WindowState = FormWindowState.Normal,
        StartPosition = FormStartPosition.Manual, Location = new(-16000, 0), Size = new(1400, 900)
    };

    private static HelpPublicationOptions? PickOptions(HelpPublicationOptions defaults)
    {
        pickerCalls++; remembered = defaults;
        Check(Field<bool>(form, "busy") && !form.EditorWebView.Enabled && !form.MainMenuStrip!.Enabled,
            "Publication options are chosen under the exclusive command guard");
        return pickerResult;
    }

    private static async Task<HelpPublicationResult> FakeRunner(HelpProject project, string assets,
        HelpPublicationOptions selected, CancellationToken ct, IProgress<HelpPublicationProgress>? progress)
    {
        Interlocked.Increment(ref runnerCalls);
        if (project.RootPath != sourceRoot || !Directory.Exists(Path.Combine(assets, "mathlive")))
            throw new InvalidOperationException("The native host did not pass the selected source/runtime assets.");
        progress?.Report(new("Testing", "Frozen-source publication in progress"));
        entered.TrySetResult();
        if (runnerMode == "blocked") await release.Task.WaitAsync(ct);
        ct.ThrowIfCancellationRequested();
        bool completed = runnerMode != "partial" && runnerMode != "failure";
        var result = new HelpPublicationResult(completed, new string('a', 64),
            new WebsiteZipExportResult(selected.WebsiteZipPath, 3, 2, 8, 99, []),
            completed ? new DesktopHelpPublishResult(Path.Combine(selected.StatsDirectSource, "StatsDirectUI", "Assets", "Help"),
                "mock-receipt.json", new string('b', 64), new string('a', 64), 3, 2, 2, [], "", false) : null,
            completed ? new string('c', 40) : null, completed, selected.GitHubBranch, completed ? "Pending" : "Not published",
            Path.Combine(selected.JournalDirectory!, "mock-receipt.json"), null,
            new Dictionary<string, string> { ["WebsiteZip"] = "Completed", ["DesktopHelp"] = completed ? "Completed" : "Failed", ["GitHub"] = completed ? "Pushed" : "Not started" });
        lastFakeResult = result;
        if (runnerMode == "failure") throw new HelpPublicationException("Owned publication failure", result, new IOException("Mock target failure"));
        return result;
    }

    private static async Task Run()
    {
        await form.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
        await form.OpenTopicAsync(Topic);
        var file = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => Clean(i.Text) == "File");
        var command = file.DropDownItems.OfType<ToolStripMenuItem>().Single(i => Clean(i.Text) == "Publish help…");
        Check(command.Enabled, "File exposes one unified Publish help command");
        string saved = form.CurrentProject.ReadTopic(Topic), originalPreferences = File.ReadAllText(preferences);
        string draft = saved.Replace("Saved alpha", "Unsaved publication draft");
        await SetSource(form, draft); await Until(() => form.IsDirty);
        await Js(form, "HelpEditor.undo();HelpEditor.redo();document.getElementById('source-editor').setSelectionRange(30,55,'backward')");
        string snapshot = await Snapshot(form), diskBaseline = Field<string>(form, "loadedDiskSource");
        Check(await Refuses(() => form.PublishHelpAsync(options)) && runnerCalls == 0,
            "Unsaved source is refused before invoking any publication executor");
        Check(await Snapshot(form) == snapshot && Field<string>(form, "loadedDiskSource") == diskBaseline
            && File.ReadAllText(preferences) == originalPreferences && SameSources(),
            "Refusal preserves exact raw draft, selection, Undo position, disk baseline, saved source and settings");
        await Js(form, "HelpEditor.undo()");
        Check(await Source(form) == saved, "Normal user Undo still restores the saved source after refusal");
        await Js(form, "HelpEditor.redo()");
        string afterRedo = await Snapshot(form);
        var expectedRedo = JsonNode.Parse(snapshot)!;
        var actualRedo = JsonNode.Parse(afterRedo)!;
        // The manually selected range was set after the history entry was
        // committed. Refusal must preserve it exactly (checked above), while
        // a deliberate Undo/Redo restores the entry's own recorded caret.
        Check(actualRedo["source"]!.GetValue<string>() == expectedRedo["source"]!.GetValue<string>()
            && actualRedo["history"]!.GetValue<int>() == expectedRedo["history"]!.GetValue<int>()
            && Field<string>(form, "loadedDiskSource") == diskBaseline,
            "Redo restores the same refused draft and history position", new { before = expectedRedo, after = actualRedo });
        await SetSource(form, saved); await form.SaveCurrentTopicAsync();

        await Js(form, "HelpEditor.setTab('visual');HelpEditor.openLink(document.getElementById('beta-link'));document.getElementById('link-text').value='Unapplied publication link';document.getElementById('link-text').dispatchEvent(new Event('input',{bubbles:true}));");
        string linkSnapshot = await Snapshot(form);
        Check(await Refuses(() => form.PublishHelpAsync(options)) && runnerCalls == 0
            && await Bool(form, "HelpEditor.getState().dialogDraftDirty") && await Snapshot(form) == linkSnapshot,
            "An unapplied link draft blocks publication and remains intact");
        await Js(form, "document.getElementById('link-cancel').click();document.querySelector('.equation-widget').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));document.getElementById('equation-field').value='x+99'");
        string equationSnapshot = await Snapshot(form);
        Check(await Refuses(() => form.PublishHelpAsync(options)) && runnerCalls == 0
            && await Bool(form, "HelpEditor.getState().equationDraftDirty") && await Snapshot(form) == equationSnapshot,
            "An unapplied MathLive equation blocks publication without closing or changing it");
        await Js(form, "document.getElementById('equation-cancel').click()");

        using (var second = CreateForm())
        {
            second.Show(); await second.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
            await SetSource(second, saved.Replace("Saved alpha", "Second window unsaved wording")); await Until(() => second.IsDirty);
            string firstState = await Snapshot(form), secondState = await Snapshot(second);
            Check(await Refuses(() => form.PublishHelpAsync(options)) && runnerCalls == 0
                && await Snapshot(form) == firstState && await Snapshot(second) == secondState,
                "Unsaved edits in another window of this source block publication without disturbing either draft");
            CloseWithoutSaving(second);
        }

        pickerResult = null; string cleanState = await Snapshot(form);
        command.PerformClick(); await Until(() => pickerCalls == 1 && !Field<bool>(form, "busy"));
        Check(runnerCalls == 0 && form.LastHelpPublication == null && Status.Contains("cancelled")
            && await Snapshot(form) == cleanState && File.ReadAllText(preferences) == originalPreferences,
            "Cancelling Publish help changes no outputs, draft, result or remembered settings");
        Check(remembered.WebsiteZipPath.EndsWith("Previous.zip", StringComparison.Ordinal)
            && remembered.GitHubBranch == "previous-branch" && remembered.AuthorName == "Previous Author",
            "The options picker receives all remembered publication defaults");

        pickerResult = options; command.PerformClick();
        await Until(() => runnerCalls == 1 && !Field<bool>(form, "busy"));
        Check(ReferenceEquals(form.LastHelpPublication, lastFakeResult) && form.LastHelpPublication!.Completed
            && form.EditorWebView.Enabled && form.MainMenuStrip!.Enabled,
            "A completed mock publication is retained and returns control to the editor");
        var prefs = JsonNode.Parse(File.ReadAllText(preferences))!.AsObject();
        Check(prefs["futureSetting"]!["preserve"]!.GetValue<string>() == "outside"
            && prefs["helpPublication"]!["futureSetting"]!.GetValue<string>() == "inside",
            "Remembering publication choices preserves unknown top-level and publication settings");
        Check(prefs["websiteZipPath"]!.GetValue<string>() == options.WebsiteZipPath
            && prefs["statsDirectSourceRoot"]!.GetValue<string>() == options.StatsDirectSource,
            "Successful unified publication remembers the complete ZIP and desktop-source paths");
        string successfulPreferences = File.ReadAllText(preferences);
        pickerResult = null; command.PerformClick(); await Until(() => pickerCalls == 3 && !Field<bool>(form, "busy"));
        Check(remembered.WebsiteZipPath == options.WebsiteZipPath && remembered.StatsDirectSource == options.StatsDirectSource
            && remembered.GitHubRepositoryUrl == options.GitHubRepositoryUrl && remembered.GitHubBranch == options.GitHubBranch
            && remembered.AuthorEmail == options.AuthorEmail && File.ReadAllText(preferences) == successfulPreferences,
            "Saved publication options round-trip through a later picker and Cancel preserves them exactly");

        runnerMode = "blocked"; entered = NewSignal(); release = NewSignal();
        using (var cancellation = new CancellationTokenSource())
        {
            Task first = form.PublishHelpAsync(options, cancellation.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Until(() => Status == "Frozen-source publication in progress");
            Check(Field<bool>(form, "busy") && !form.EditorWebView.Enabled && !form.MainMenuStrip!.Enabled,
                "Observed publication progress keeps editing and other menu commands exclusively busy");
            int runningCalls = runnerCalls;
            Check(await Refuses(() => form.PublishHelpAsync(options)) && runnerCalls == runningCalls,
                "A second direct publication cannot overlap the active executor");
            cancellation.Cancel();
            Check(await Refuses(() => first) && !Field<bool>(form, "busy") && form.EditorWebView.Enabled
                && File.ReadAllText(preferences) == successfulPreferences && await Snapshot(form) == cleanState,
                "Cancellation releases the busy state and preserves the source, selection/history and remembered choices");
        }
        runnerMode = "partial";
        var partial = await form.PublishHelpAsync(options);
        Check(!partial.Completed && ReferenceEquals(form.LastHelpPublication, partial) && Status.Contains("incomplete")
            && File.ReadAllText(preferences) == successfulPreferences,
            "An incomplete per-target result is retained visibly without saving new settings");
        runnerMode = "failure";
        Check(await Refuses(() => form.PublishHelpAsync(options)) && ReferenceEquals(form.LastHelpPublication, lastFakeResult)
            && form.LastHelpPublication!.TargetStates["DesktopHelp"] == "Failed" && !Field<bool>(form, "busy")
            && File.ReadAllText(preferences) == successfulPreferences,
            "A structured target failure retains retry evidence and restores controls without rewriting preferences");
        Check(SameSources(), "Success, cancellation and failures never save a page draft implicitly");
        await RealDialogChecks();
        await CliChecks.Run(root, fixture, sourceRoot, Check);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static string Clean(string? text) => (text ?? "").Replace("&", "");
    private static T Field<T>(MainForm target, string name) => (T)typeof(MainForm).GetField(name, Private)!.GetValue(target)!;
    private static string Status => Field<ToolStripStatusLabel>(form, "status").Text ?? "";
    private static Task Js(MainForm target, string script) => target.EditorWebView.ExecuteScriptAsync(script);
    private static async Task<string> Text(MainForm target, string script) => JsonSerializer.Deserialize<string>(await target.EditorWebView.ExecuteScriptAsync(script))!;
    private static async Task<bool> Bool(MainForm target, string script) => JsonSerializer.Deserialize<bool>(await target.EditorWebView.ExecuteScriptAsync(script));
    private static Task<string> Source(MainForm target) => Text(target, "HelpEditor.getSource()");
    private static Task<string> Snapshot(MainForm target) => Text(target,
        "(()=>{const t=document.getElementById('source-editor'),s=HelpEditor.getState();return JSON.stringify({source:HelpEditor.getSource(),tab:s.tab,history:s.historyIndex,start:t.selectionStart,end:t.selectionEnd,direction:t.selectionDirection,dirty:s.dirty,linkOpen:document.getElementById('link-dialog').open,linkText:document.getElementById('link-text').value,equationOpen:document.getElementById('equation-dialog').open,equation:document.getElementById('equation-field').value})})()");
    private static Task SetSource(MainForm target, string source) => Js(target,
        "HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(source) +
        ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}))");
    private static async Task<bool> Refuses(Func<Task> action)
    {
        try { await action(); return false; }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or OperationCanceledException or System.Xml.XmlException) { return true; }
    }
    private static async Task Until(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition()) { if (timeout.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Publication UI did not settle: " + form.LastError); await Task.Delay(25); }
    }
    private static void CloseWithoutSaving(MainForm target)
    { typeof(MainForm).GetField("closingApproved", Private)!.SetValue(target, true); target.Close(); }
    private static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static Dictionary<string, string> Digests(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => path, Digest);
    private static bool SameSources() => sourceBefore.Count == Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories).Length
        && sourceBefore.All(pair => File.Exists(pair.Key) && Digest(pair.Key) == pair.Value);
    private static void Check(bool passed, string name, object? detail = null)
    { checks.Add(new { name, passed, detail }); if (!passed) failures++; Console.WriteLine((passed ? "PASS " : "FAIL ") + name); }
    private static void CreateFixture()
    {
        void Write(string relative, string text) { string path = Path.Combine(sourceRoot, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); }
        Write(HelpProject.MarkerFile, "{}");
        Write("Content/basics/alpha.htm", "<html xmlns:sdh='" + HelpSourceNamespaces.CurrentUri + "'><head><title>Alpha</title></head><body><h1>Alpha</h1><p>Saved alpha</p><p><a id='beta-link' href='beta.htm'>Beta</a></p><sdh:equation>$x^2$</sdh:equation></body></html>");
        Write("Content/basics/beta.htm", "<html><head><title>Beta</title></head><body><h1>Beta</h1><p>Saved beta.</p></body></html>");
        Write("Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title='Alpha' Link='/Content/basics/alpha.htm'/><TocEntry Title='Beta' Link='/Content/basics/beta.htm'/></CatapultToc>");
        Write("Content/resources/stylesheets/statsdirect.css", "body{font:10pt Arial}");
        Write(".help-editor-recovery/retained.txt", "Private recovery remains untouched.");
        Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);
        File.WriteAllText(preferences, JsonSerializer.Serialize(new
        {
            websiteZipPath = Path.Combine(fixture, "Previous.zip"), statsDirectSourceRoot = Path.Combine(fixture, "PreviousStatsDirect"),
            futureSetting = new { preserve = "outside" }, helpPublication = new
            {
                repositoryUrl = "https://github.com/statsdirect-tests/previous-help", branch = "previous-branch",
                authorName = "Previous Author", authorEmail = "previous@example.invalid", futureSetting = "inside"
            }
        }));
    }
}
