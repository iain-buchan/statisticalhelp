using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static class Program
{
    private static MainForm form = null!;
    private static readonly List<object> checks = [];
    private static readonly List<object> cliRuns = [];
    private static string root = "", runRoot = "", source = "", other = "", repo = "", prefs = "", cli = "";
    private static int failed;
    private const string Topic = "basics/alpha.htm";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--read-preference")
        {
            var type = typeof(MainForm).Assembly.GetType("StatsDirect.HelpEditor.App.AuthoringPreferences")!;
            Console.WriteLine(JsonSerializer.Serialize(type.GetMethod("ReadStatsDirectSourceRoot", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [args[1]])));
            return 0;
        }
        root = Path.GetFullPath(args[0]);
        string evidence = Path.Combine(root, "evidence/ReviewImprovements-20261104/DesktopComparison/NativeIntegration");
        Directory.CreateDirectory(evidence);
        // Chromium cache descendants can exceed MAX_PATH under a CI checkout.
        // Keep reports in evidence, but isolate browser profiles in short temp.
        runRoot = Path.Combine(Path.GetTempPath(), "sdhe-delivery-" + Guid.NewGuid().ToString("N")[..8]);
        source = Path.Combine(runRoot, "HelpSource"); other = Path.Combine(runRoot, "OriginalSource");
        repo = Path.Combine(runRoot, "Application"); prefs = Path.Combine(runRoot, "Settings/authoring-preferences.json");
        cli = Path.Combine(root, "src/StatsDirectHelpEditor.Cli/bin/Release/net10.0/StatsDirectHelpBuilder.exe");
        CreateFixture(source, true); CreateFixture(other, false); CreateRepository(repo);
        Write(prefs, "{\"futureSetting\":{\"preserve\":\"unchanged\"}}");
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, error) => Check(false, "No unhandled native delivery exception", error.Exception.ToString());
        form = NewForm();
        form.Shown += async (_, _) =>
        {
            try { await RunNative(); await RunCli(); }
            catch (Exception error) { Check(false, "Delivery integration completes", error.ToString()); }
            finally
            {
                File.WriteAllText(Path.Combine(evidence, "checks.json"), JsonSerializer.Serialize(new
                {
                    checkedAtUtc = DateTimeOffset.UtcNow, passed = failed == 0, count = checks.Count, failed, runRoot,
                    appSha256 = Hash(typeof(MainForm).Assembly.Location), cliSha256 = File.Exists(cli) ? Hash(cli) : null,
                    results = checks, cliRuns
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{checks.Count - failed}/{checks.Count} delivery integration checks passed; {failed} failed");
                Close(form);
            }
        };
        Application.Run(form);
        return failed == 0 ? 0 : 1;
    }

    private static MainForm NewForm() => new(source, initialTopic: Topic, workspaceOverride: new HelpEditorWorkspace(runRoot, source, null))
    {
        SuppressDialogs = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
        Location = new Point(-16000, 0), Size = new Size(1500, 950)
    };

    private static async Task RunNative()
    {
        await form.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
        await form.OpenTopicAsync(Topic);
        var file = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item => Clean(item.Text) == "File");
        var commands = file.DropDownItems.OfType<ToolStripMenuItem>().ToArray();
        Check(commands.Single(item => Clean(item.Text) == "Publish desktop help for StatsDirect builds…").Enabled &&
            commands.Single(item => Clean(item.Text) == "Compare saved help sources…").Enabled,
            "File exposes explicit desktop delivery and saved-source comparison");

        var savedSources = Snapshot(source); var originalSources = Snapshot(other);
        string saved = form.CurrentProject.ReadTopic(Topic), draft = saved.Replace("Savedalpha", "Unsaveddraftdeliveryneedle");
        await SetSource(draft); await Until(() => form.IsDirty);
        await Js("HelpEditor.undo();HelpEditor.redo();document.getElementById('source-editor').setSelectionRange(30,50,'backward')");
        string beforeCompare = await EditorSnapshot();
        var compared = await form.CompareSavedSourcesAsync(other);
        Check(compared.EquivalentTopicCount == 3 && compared.Differences.Count == 0 && compared.ContentsEquivalent == true && form.LastSourceComparison == compared,
            "Native comparison compares saved namespace-equivalent source and records the fresh result");
        Check(await EditorSnapshot() == beforeCompare && form.IsDirty && Same(savedSources, source) && Same(originalSources, other) && !File.Exists(Path.Combine(other, HelpProject.MarkerFile)),
            "Comparison preserves the exact draft, selection, Undo position and both source trees without marking the original");
        string preferencesBefore = File.ReadAllText(prefs);
        Check(await Reject(() => form.PublishDesktopHelpAsync(repo)) && await EditorSnapshot() == beforeCompare && !Directory.Exists(Help(repo)) && File.ReadAllText(prefs) == preferencesBefore,
            "Dirty-page delivery refuses before creating output or changing preferences and retains the draft");
        await form.OpenTopicAsync(Topic); // Explicit test discard, never a user draft.
        await Js("HelpEditor.setTab('visual');HelpEditor.openLink(document.getElementById('beta-link'));document.getElementById('link-text').value='Unapplied link';document.getElementById('link-text').dispatchEvent(new Event('input',{bubbles:true}));");
        Check(await Reject(() => form.PublishDesktopHelpAsync(repo)) && await Bool("HelpEditor.getState().dialogDraftDirty") && !Directory.Exists(Help(repo)),
            "An unapplied link blocks desktop publication and remains open");
        await Js("document.getElementById('link-cancel').click();document.querySelector('.equation-widget').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));document.getElementById('equation-field').value='x+99';");
        Check(await Reject(() => form.PublishDesktopHelpAsync(repo)) && await Bool("HelpEditor.getState().equationDraftDirty") && !Directory.Exists(Help(repo)),
            "An unapplied MathLive equation blocks desktop publication and remains open");
        await Js("document.getElementById('equation-cancel').click()");

        using (var second = NewForm())
        {
            second.Show(); await second.Initialization.WaitAsync(TimeSpan.FromSeconds(45)); await second.OpenTopicAsync(Topic);
            await second.EditorWebView.ExecuteScriptAsync("HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(draft) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
            await Until(() => second.IsDirty);
            Check(await Reject(() => form.PublishDesktopHelpAsync(repo)) && second.IsDirty && !Directory.Exists(Help(repo)),
                "A draft in another open window of the same source also blocks desktop delivery");
            Close(second);
        }

        await SetSource(draft); await Until(() => form.IsDirty); await form.SaveCurrentTopicAsync();
        Check(!form.IsDirty && form.CurrentProject.ReadTopic(Topic).Contains("Unsaveddraftdeliveryneedle"), "Explicit Save provides the source that desktop delivery will consume");
        savedSources = Snapshot(source); originalSources = Snapshot(other);
        int ticks = 0; DesktopHelpPublishResult published;
        using (var timer = new System.Windows.Forms.Timer { Interval = 20 })
        {
            timer.Tick += (_, _) => ticks++; timer.Start(); published = await form.PublishDesktopHelpAsync(repo);
        }
        Check(published.TopicCount == 3 && published.SearchDocumentCount == 3 && published.ContextCount == 2 &&
            form.LastDesktopHelpPublication == published && File.Exists(published.ReceiptPath),
            "Saved native publication delivers a verified full artifact and records its receipt");
        string delivered = File.ReadAllText(Path.Combine(Help(repo), Topic));
        Check(delivered.Contains("Unsaveddraftdeliveryneedle") && delivered.Contains("DESKTOP_ONLY") && !delivered.Contains("PUBLIC_ONLY") &&
            File.ReadAllText(Path.Combine(Help(repo), "Data/Alias.xml")).Contains("ResolvedId=\"1001\""),
            "Native delivery includes the saved page, Desktop conditions and numeric application context map");
        Check(DesktopHelpPublisher.VerifyDestination(repo).ReceiptSha256 == published.ReceiptSha256 && !published.ConsumerGuardInstalled && Status.Contains("optional build guard"),
            "The fresh receipt verifies all delivered bytes and the UI honestly reports the optional consumer guard");
        Check(ticks > 0 && !Field<bool>("busy") && form.EditorWebView.Enabled && form.MainMenuStrip.Enabled,
            "Publication keeps the UI responsive and restores idle controls");
        using (var preferences = JsonDocument.Parse(File.ReadAllText(prefs)))
            Check(preferences.RootElement.GetProperty("statsDirectSourceRoot").GetString() == repo && preferences.RootElement.GetProperty("futureSetting").GetProperty("preserve").GetString() == "unchanged",
                "Only successful delivery remembers the selected repository while preserving unknown user preferences");
        var preferenceRead = await Process(Environment.ProcessPath!, ["--read-preference", runRoot]);
        Check(preferenceRead.Code == 0 && JsonSerializer.Deserialize<string>(preferenceRead.Output.Trim()) == repo, "The desktop destination is retained across a fresh process");
        string hash = DesktopHelpPublisher.InspectDestination(repo).Sha256, remembered = File.ReadAllText(prefs);
        Check(await Reject(() => form.PublishDesktopHelpAsync(repo, new() { ExpectedDestinationSha256 = new string('0', 64) })) &&
            DesktopHelpPublisher.InspectDestination(repo).Sha256 == hash && File.ReadAllText(prefs) == remembered && form.LastDesktopHelpPublication == published,
            "A stale destination hash refuses replacement without changing output, preferences or the last successful result");
        Check(Same(savedSources, source) && Same(originalSources, other) && !Directory.GetFiles(Help(repo), "*", SearchOption.AllDirectories).Any(path => path.Contains("Settings") || path.Contains(".help-editor-recovery")),
            "Native publication leaves both saved sources intact and excludes private authoring state");
    }

    private static async Task RunCli()
    {
        Check(File.Exists(cli), "The integration harness uses the freshly built CLI executable");
        var sourceBefore = Snapshot(source); var originalBefore = Snapshot(other); var uiBefore = await EditorSnapshot();
        var result = await Cli("compare-source", "--left", other, "--right", source);
        using (var json = JsonDocument.Parse(result.Output))
            Check(result.Code == 0 && result.Error.Length == 0 && json.RootElement.GetProperty("CommonTopicCount").GetInt32() == 3 && json.RootElement.GetProperty("Differences").GetArrayLength() == 1,
                "CLI comparison emits parseable current saved-source differences with success exit");
        result = await Cli("compare-flare-source", "--flare", other, "--working", source);
        Check(result.Code == 0 && JsonDocument.Parse(result.Output).RootElement.GetProperty("Differences").GetArrayLength() == 1,
            "The explicit legacy-named comparison alias is also read-only");
        result = await Cli("inspect-desktop-help", "--statsdirect-source", repo);
        using (var json = JsonDocument.Parse(result.Output))
            Check(result.Code == 0 && json.RootElement.GetProperty("FileCount").GetInt32() > 3 && json.RootElement.GetProperty("Sha256").GetString() == DesktopHelpPublisher.InspectDestination(repo).Sha256,
                "CLI inspection reports the exact delivered destination snapshot");
        result = await Cli("verify-desktop-help", "--statsdirect-source", repo);
        Check(result.Code == 0 && JsonDocument.Parse(result.Output).RootElement.GetProperty("ReceiptSha256").GetString() == form.LastDesktopHelpPublication!.ReceiptSha256,
            "CLI verification certifies the same actual artifact as native delivery");
        var before = Snapshot(repo); result = await Cli("recover-desktop-help", "--statsdirect-source", repo);
        Check(result.Code == 0 && JsonDocument.Parse(result.Output).RootElement.GetProperty("Outcome").GetString() == "Nothing pending" &&
            !File.Exists(DesktopHelpPublisher.PendingMarkerPath(repo)) && Same(before, repo),
            "CLI recovery with no pending publication is an honest no-op");

        string cliRepo = Path.Combine(runRoot, "CliApplication"); CreateRepository(cliRepo);
        result = await Cli("publish-desktop-help", "--project", source, "--statsdirect-source", cliRepo, "--assets", Path.Combine(AppContext.BaseDirectory, "Web"));
        Check(result.Code == 0 && result.Error.Length == 0 && File.ReadAllText(Path.Combine(Help(cliRepo), Topic)).Contains("Unsaveddraftdeliveryneedle") &&
            JsonDocument.Parse(result.Output).RootElement.GetProperty("TopicCount").GetInt32() == 3,
            "CLI publish writes a fresh verified Desktop artifact from saved source into an isolated consumer");
        string cliHash = DesktopHelpPublisher.InspectDestination(cliRepo).Sha256;
        result = await Cli("publish-desktop-help", "--project", source, "--statsdirect-source", cliRepo, "--assets", Path.Combine(AppContext.BaseDirectory, "Web"));
        Check(Error(result, "exact ExpectedDestinationSha256") && DesktopHelpPublisher.InspectDestination(cliRepo).Sha256 == cliHash,
            "CLI replacement refuses an omitted inspection digest and preserves the actual complete output");
        result = await Cli("publish-desktop-help", "--project", source, "--statsdirect-source", cliRepo, "--expected-destination-sha256", cliHash, "--assets", Path.Combine(AppContext.BaseDirectory, "Web"));
        Check(result.Code == 0 && DesktopHelpPublisher.VerifyDestination(cliRepo).TopicCount == 3,
            "CLI replacement accepts the exact reviewed destination digest");

        foreach (string command in new[] { "compare-source", "compare-flare-source", "inspect-desktop-help", "publish-desktop-help", "verify-desktop-help", "recover-desktop-help" })
        {
            string key = command == "compare-source" ? "--left" : command == "compare-flare-source" ? "--flare" : "--statsdirect-source";
            result = await Cli(command); Check(Error(result, "Missing"), command + " rejects missing required options with exit 1 and stderr only");
            result = await Cli(command, "--unknown", "x"); Check(Error(result, "Unknown option"), command + " rejects unknown options");
            result = await Cli(command, key); Check(Error(result, "Missing value"), command + " rejects a missing option value");
            result = await Cli(command, key, "x", key, "y"); Check(Error(result, "Duplicate option"), command + " rejects duplicate options");
        }
        result = await Cli("publish-desktop-help", "--allow-warnings", "--allow-warnings");
        Check(Error(result, "Duplicate option"), "Boolean delivery flags cannot be duplicated");
        result = await Cli("publish-desktop-help", "--adopt-flare-output", "false");
        Check(Error(result, "Unknown option"), "Boolean delivery flags do not consume accidental positional values");
        File.WriteAllText(DesktopHelpPublisher.PendingMarkerPath(cliRepo), "{}"); before = Snapshot(cliRepo);
        result = await Cli("verify-desktop-help", "--statsdirect-source", cliRepo);
        Check(Error(result, "incomplete") && Same(before, cliRepo), "CLI verify refuses a pending generation without modifying its journal or output");
        result = await Cli("recover-desktop-help", "--statsdirect-source", cliRepo);
        Check(result.Code == 1 && result.Output.Length == 0 && result.Error.Length > 0 && Same(before, cliRepo),
            "CLI recover refuses a malformed journal without guessing or deleting the last complete output");
        File.Delete(DesktopHelpPublisher.PendingMarkerPath(cliRepo));
        string page = Path.Combine(Help(cliRepo), Topic); File.AppendAllText(page, "EXTERNAL_EDIT"); before = Snapshot(cliRepo);
        result = await Cli("verify-desktop-help", "--statsdirect-source", cliRepo);
        Check(result.Code == 1 && result.Output.Length == 0 && Same(before, cliRepo), "CLI verification detects corruption of actual delivered files without rewriting them");
        Check(Same(sourceBefore, source) && Same(originalBefore, other) && await EditorSnapshot() == uiBefore,
            "Every CLI success and error path preserves saved sources and the open native editor state");
    }

    private sealed record ProcessResult(int Code, string Output, string Error);
    private static async Task<ProcessResult> Process(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in arguments) start.ArgumentList.Add(arg);
        using var child = System.Diagnostics.Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
        return new(child.ExitCode, await output, await error);
    }
    private static async Task<ProcessResult> Cli(params string[] arguments)
    {
        var result = await Process(cli, arguments); cliRuns.Add(new { arguments, result.Code, result.Output, result.Error }); return result;
    }
    private static bool Error(ProcessResult result, string fragment) => result.Code == 1 && result.Output.Length == 0 && result.Error.Contains(fragment, StringComparison.OrdinalIgnoreCase);
    private static async Task<bool> Reject(Func<Task> action)
    {
        try { await action(); return false; }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or IOException or System.Xml.XmlException or OperationCanceledException or ArgumentException) { return true; }
    }
    private static string Clean(string? text) => (text ?? "").Replace("&", "");
    private static T Field<T>(string name) => (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    private static string Status => Field<ToolStripStatusLabel>("status").Text ?? "";
    private static Task Js(string script) => form.EditorWebView.ExecuteScriptAsync(script);
    private static async Task<bool> Bool(string script) => JsonSerializer.Deserialize<bool>(await form.EditorWebView.ExecuteScriptAsync(script));
    private static async Task<string> EditorSnapshot() => JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync("(()=>{const t=document.getElementById('source-editor');return JSON.stringify({source:HelpEditor.getSource(),tab:HelpEditor.getState().tab,history:HelpEditor.getState().historyIndex,start:t.selectionStart,end:t.selectionEnd,direction:t.selectionDirection,dirty:HelpEditor.getState().dirty})})()"))!;
    private static Task SetSource(string text) => Js("HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(text) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
    private static async Task Until(Func<bool> condition) { var clock = Stopwatch.StartNew(); while (!condition()) { if (clock.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Native delivery state did not settle: " + form.LastError); await Task.Delay(25); } }
    private static void Close(MainForm window) { typeof(MainForm).GetField("closingApproved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true); window.Close(); }
    private static void Check(bool passed, string name, object? detail = null) { checks.Add(new { name, passed, detail }); if (!passed) failed++; Console.WriteLine((passed ? "PASS " : "FAIL ") + name); }
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    private static Dictionary<string, string> Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(directory, path), Hash);
    private static bool Same(Dictionary<string, string> before, string directory) { var after = Snapshot(directory); return before.Count == after.Count && before.All(item => after.TryGetValue(item.Key, out string? value) && value == item.Value); }
    private static string Help(string repository) => Path.Combine(repository, "StatsDirectUI/Assets/Help");
    private static void Write(string file, string value) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, value, new UTF8Encoding(false)); }
    private static void CreateFixture(string directory, bool marked)
    {
        void FileAt(string name, string text) => Write(Path.Combine(directory, name), text);
        string Page(string title, string body) => $"<html xmlns:sdh='{HelpSourceNamespaces.CurrentUri}'><head><title>{title}</title></head><body>{body}</body></html>";
        if (marked) FileAt(HelpProject.MarkerFile, "{}");
        FileAt("Content/contents.htm", Page("Contents", "<h1>Contents</h1><p>Desktop delivery integration.</p>"));
        FileAt("Content/" + Topic, Page("Alpha", "<h1>Alpha</h1><p>Savedalpha</p><p sdh:conditions='Primary.WEBHELP'>PUBLIC_ONLY</p><p sdh:conditions='Primary.SWHELP'>DESKTOP_ONLY</p><p><a id='beta-link' href='beta.htm'>Beta</a></p><sdh:equation>$x^2$</sdh:equation>"));
        FileAt("Content/basics/beta.htm", Page("Beta", "<h1>Beta</h1><p>Saved beta.</p>"));
        FileAt("Content/resources/stylesheets/statsdirect.css", "body{font:10pt Arial}");
        FileAt("Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title='Contents' Link='/Content/contents.htm'/><TocEntry Title='Alpha' Link='/Content/basics/alpha.htm'/><TocEntry Title='Beta' Link='/Content/basics/beta.htm'/></CatapultToc>");
        FileAt("Project/Advanced/STATSDIRECT.flali", "<CatapultAliasFile><Map Name='CONTENTS' Link='/Content/contents.htm'/><Map Name='ALPHA' Link='/Content/basics/alpha.htm'/></CatapultAliasFile>");
        FileAt("Project/Advanced/SD3ContextID.h", "#define CONTENTS 1000\n#define ALPHA 1001\n");
        FileAt(".help-editor-recovery/retained-user-data.txt", "This recovery data must remain private.");
    }
    private static void CreateRepository(string directory)
    {
        Write(Path.Combine(directory, "StatsDirectUI/StatsDirectUI.csproj"), "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>StatsDirect</AssemblyName><RootNamespace>StatsDirect</RootNamespace></PropertyGroup><ItemGroup><Content Include='Assets/**'/></ItemGroup></Project>");
        Directory.CreateDirectory(Path.Combine(directory, "StatsDirectUI/Assets/Operations"));
    }
}
