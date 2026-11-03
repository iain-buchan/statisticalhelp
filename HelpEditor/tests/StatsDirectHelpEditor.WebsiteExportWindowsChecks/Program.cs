using System.Diagnostics;
using System.IO.Compression;
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
    private static string root = "", testRoot = "", sourceRoot = "", prefsPath = "", previousZip = "", chosenZip = "";
    private static int failed, pickerCalls;
    private static DialogResult pickerResult = DialogResult.Cancel;
    private static bool pickerWasBusy, pickerUsedRememberedPath;
    private const string Topic = "basics/alpha.htm";
    private static Dictionary<string, string> sourceBefore = [];

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--read-preference")
        {
            ApplicationConfiguration.Initialize();
            using var reader = new MainForm(args[2], workspaceOverride: new HelpEditorWorkspace(args[1], args[2], null));
            Console.WriteLine(JsonSerializer.Serialize(reader.SuggestedWebsiteZipPath));
            return 0;
        }
        root = Path.GetFullPath(args[0]);
        testRoot = Path.Combine(Path.GetTempPath(), "sdhe-zip-" + Guid.NewGuid().ToString("N")[..8]);
        sourceRoot = Path.Combine(testRoot, "HelpSource");
        prefsPath = Path.Combine(testRoot, "Settings", "authoring-preferences.json");
        previousZip = Path.Combine(testRoot, "Remembered folder", "Previous website.zip");
        chosenZip = Path.Combine(testRoot, "Chosen folder", "Full chosen filename.zip");
        CreateFixture();
        sourceBefore = Digests(sourceRoot);
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, exception) => Check(false, "No unhandled native export exception", exception.Exception.ToString());
        form = new MainForm(sourceRoot, initialTopic: Topic, workspaceOverride: new HelpEditorWorkspace(testRoot, sourceRoot, null), websiteZipPicker: PickZip)
        {
            SuppressDialogs = true, ShowInTaskbar = false, WindowState = FormWindowState.Normal,
            StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0), Size = new Size(1500, 950)
        };
        form.Shown += async (_, _) =>
        {
            try { await Run(); }
            catch (Exception exception) { Check(false, "Native Website ZIP checks completed", exception.ToString()); }
            finally
            {
                Check(SameSources(), "All export paths preserve every original source and recovery byte");
                Directory.CreateDirectory(Path.Combine(root, "evidence"));
                File.WriteAllText(Path.Combine(root, "evidence", "website-export-windows-checks.json"), JsonSerializer.Serialize(new
                {
                    passed = failed == 0, checkedAtUtc = DateTimeOffset.UtcNow, fixture = testRoot, count = checks.Count, failed,
                    sourceFiles = sourceBefore.Count, results = checks
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{checks.Count - failed} native Website ZIP checks passed; {failed} failed");
                typeof(MainForm).GetField("closingApproved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
                form.Close();
            }
        };
        Application.Run(form);
        return failed == 0 ? 0 : 1;
    }

    private static DialogResult PickZip(SaveFileDialog dialog)
    {
        pickerCalls++;
        pickerWasBusy = Field<bool>("busy") && !form.EditorWebView.Enabled && !form.MainMenuStrip!.Enabled;
        pickerUsedRememberedPath = dialog.InitialDirectory == Path.GetDirectoryName(previousZip) && dialog.FileName == Path.GetFileName(previousZip)
            && dialog.DefaultExt == "zip" && dialog.OverwritePrompt && dialog.AddExtension && dialog.Title.Contains("www.statsdirect.com/help");
        if (pickerResult == DialogResult.OK) dialog.FileName = chosenZip;
        return pickerResult;
    }

    private static async Task Run()
    {
        await form.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
        await form.OpenTopicAsync(Topic);
        var file = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item => Clean(item.Text) == "File");
        var command = file.DropDownItems.OfType<ToolStripMenuItem>().Single(item => Clean(item.Text) == "Export website ZIP…");
        Check(command.Enabled && file.DropDownItems.IndexOf(command) > file.DropDownItems.OfType<ToolStripMenuItem>().Where(item => Clean(item.Text) == "Build help and search").Select(file.DropDownItems.IndexOf).Single(),
            "File exposes Website ZIP export beside the explicit build and preview commands");
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (documents.Length == 0) documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
        string defaultZip = Path.Combine(documents, "StatsDirectHelp-Website.zip");
        Check(form.SuggestedWebsiteZipPath == defaultZip && !File.Exists(prefsPath), "A new workspace suggests Documents without creating or hardcoding a machine preference");

        Directory.CreateDirectory(Path.GetDirectoryName(previousZip)!);
        Directory.CreateDirectory(Path.GetDirectoryName(chosenZip)!);
        using (var archive = ZipFile.Open(previousZip, ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry("previous.txt").Open()); writer.Write("Existing ZIP must survive cancellation and failure."); }
        WritePreferences(previousZip);
        string originalZipHash = Digest(previousZip), originalPrefs = File.ReadAllText(prefsPath);
        string saved = form.CurrentProject.ReadTopic(Topic);
        string draft = saved.Replace("Savedalpha", "Unsaveddraftzipneedle");
        await SetSource(draft);
        await Until(() => form.IsDirty);
        // Settle the editor's debounced typing entry before comparing command
        // history. Ordinary Undo/Redo commits that entry without a timed sleep.
        await Js("HelpEditor.undo();HelpEditor.redo()");
        await Js("document.getElementById('source-editor').setSelectionRange(30,50,'backward')");
        string beforeCancel = await EditorSnapshot();
        command.PerformClick();
        await Until(() => pickerCalls == 1 && !Field<bool>("busy"));
        Check(pickerWasBusy && pickerUsedRememberedPath, "The ZIP picker uses the remembered full location, correct filter and exclusive busy state");
        Check(await EditorSnapshot() == beforeCancel && form.IsDirty && Digest(previousZip) == originalZipHash && File.ReadAllText(prefsPath) == originalPrefs,
            "Cancel preserves the exact draft, selection, Undo position, previous ZIP and preferences");
        Check(Status.Contains("cancelled") && form.LastWebsiteZipExportResult == null, "Cancelled export reports cancellation without inventing an export result");

        await Js("HelpEditor.setTab('visual');HelpEditor.openLink(document.getElementById('beta-link'));document.getElementById('link-text').value='Unapplied link';document.getElementById('link-text').dispatchEvent(new Event('input',{bubbles:true}));");
        bool blockedLink = await Reject(() => form.ExportWebsiteZipAsync(previousZip));
        Check(blockedLink && await Bool("HelpEditor.getState().dialogDraftDirty") && Digest(previousZip) == originalZipHash && File.ReadAllText(prefsPath) == originalPrefs,
            "An unapplied link draft blocks ZIP export without losing the dialog, archive or remembered path");
        await Js("document.getElementById('link-cancel').click();document.querySelector('.equation-widget').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));document.getElementById('equation-field').value='x+99';");
        bool blockedEquation = await Reject(() => form.ExportWebsiteZipAsync(previousZip));
        Check(blockedEquation && await Bool("HelpEditor.getState().equationDraftDirty") && Digest(previousZip) == originalZipHash,
            "An unapplied equation draft blocks export and remains open for Apply or Cancel");
        await Js("document.getElementById('equation-cancel').click()");

        await SetSource("<html><body>unfinished");
        bool malformed = await Reject(() => form.ExportWebsiteZipAsync(previousZip));
        Check(malformed && await String("document.getElementById('source-editor').value") == "<html><body>unfinished" && form.IsDirty
            && Digest(previousZip) == originalZipHash && File.ReadAllText(prefsPath) == originalPrefs,
            "Malformed HTML refuses export while preserving the raw draft, previous ZIP and remembered path");
        await SetSource(draft);
        await Js("HelpEditor.undo();HelpEditor.redo()");
        string beforeFailure = await EditorSnapshot();
        string betaPath = Path.Combine(sourceRoot, "Content", "basics", "beta.htm");
        byte[] beta = File.ReadAllBytes(betaPath);
        bool failedBuild;
        try { File.WriteAllText(betaPath, "<html><body>broken saved page"); failedBuild = await Reject(() => form.ExportWebsiteZipAsync(previousZip)); }
        finally { File.WriteAllBytes(betaPath, beta); }
        Check(failedBuild && await EditorSnapshot() == beforeFailure && Digest(previousZip) == originalZipHash && File.ReadAllText(prefsPath) == originalPrefs,
            "A different invalid saved page fails the fresh build without replacing the previous ZIP, preferences or current draft");
        bool cancelled = await Reject(() => form.ExportWebsiteZipAsync(previousZip, new CancellationToken(canceled: true)));
        Check(cancelled && !Field<bool>("busy") && await EditorSnapshot() == beforeFailure && Digest(previousZip) == originalZipHash && File.ReadAllText(prefsPath) == originalPrefs,
            "A cancelled export token leaves the editor usable and preserves the prior archive, path and draft");
        string forbidden = Path.Combine(sourceRoot, "Content", "must-not-write.zip");
        bool sourceBlocked = await Reject(() => form.ExportWebsiteZipAsync(forbidden));
        Check(sourceBlocked && !File.Exists(forbidden) && File.ReadAllText(prefsPath) == originalPrefs && SameSources(), "The native export cannot write a ZIP inside protected help source");

        int previewBuilds = form.OutputPreviewBuildCount;
        string beforeSuccess = await EditorSnapshot();
        pickerResult = DialogResult.OK;
        int uiTicks = 0;
        using (var timer = new System.Windows.Forms.Timer { Interval = 20 })
        {
            timer.Tick += (_, _) => uiTicks++;
            timer.Start();
            command.PerformClick();
            await Until(() => pickerCalls == 2 && !Field<bool>("busy"), TimeSpan.FromSeconds(90));
        }
        var result = form.LastWebsiteZipExportResult;
        Check(result != null && result.ZipPath == chosenZip && result.TopicCount == 3 && result.SearchDocumentCount == 2 && result.Warnings.Count == 0 && File.Exists(chosenZip),
            "The File command creates a fresh Website ZIP including all topics and only searchable pages", form.LastError);
        Check(uiTicks > 0 && !Field<bool>("busy") && form.EditorWebView.Enabled, "Website ZIP generation runs asynchronously and returns the native shell to idle");
        Check(await EditorSnapshot() == beforeSuccess && form.IsDirty && form.CurrentProject.ReadTopic(Topic) == saved && SameSources(), "Successful export includes the draft without saving, reloading or changing source/selection/Undo");
        Check(form.OutputPreviewBuildCount == previewBuilds, "Export does not rebuild or invalidate ordinary navigation previews");
        using (var zip = ZipFile.OpenRead(chosenZip))
        {
            string html = ZipText(zip, "basics/alpha.htm"), search = ZipText(zip, "search-index.js");
            Check(zip.GetEntry("index.html") != null && zip.GetEntry("Data/Alias.xml") != null && zip.GetEntry("resources/help-editor-runtime/mathjax/tex-mml-svg.js") != null,
                "ZIP contents begin directly at the website root with context aliases and offline mathematics");
            Check(html.Contains("Unsaveddraftzipneedle") && html.Contains("PUBLIC_ONLY") && !html.Contains("DESKTOP_ONLY") && html.Contains("WEBSITE_FOOTER") && !html.Contains("DESKTOP_FOOTER"),
                "ZIP uses Website conditions and footer while including the current unsaved draft");
            Check(search.Contains("unsaveddraftzipneedle") && !search.Contains("aboutprivateword") && ZipText(zip, "menus/about.htm").Contains("aboutprivateword"),
                "The draft enters fresh search while a non-searchable About page stays readable in the ZIP");
            Check(!zip.Entries.Any(entry => entry.FullName.Contains(".help-editor-recovery") || entry.FullName.Contains("authoring-preferences") || entry.FullName.StartsWith("Project/", StringComparison.OrdinalIgnoreCase)),
                "Authoring preferences, recovery archives and project configuration never enter the public ZIP");
        }
        using (var prefs = JsonDocument.Parse(File.ReadAllText(prefsPath)))
            Check(prefs.RootElement.GetProperty("websiteZipPath").GetString() == chosenZip && prefs.RootElement.GetProperty("futureSetting").GetProperty("preserve").GetString() == "unchanged",
                "Only a successful export remembers the full filename and preserves unknown preference keys");
        Check(!Directory.GetFiles(Path.GetDirectoryName(prefsPath)!, ".authoring-preferences-*.tmp").Any(), "Successful preference replacement leaves one valid complete settings file");
        Check(Status.Contains(chosenZip) && Status.Contains("unsaved current-page draft") && Status.Contains("www.statsdirect.com/help"),
            "Success names the actual ZIP and clearly identifies the included draft and website destination");
        Check(await ReadPreferenceInFreshProcess() == chosenZip, "The full chosen ZIP folder and filename survive a fresh editor process");

        byte[] validPreferences = File.ReadAllBytes(prefsPath);
        const string malformedPreferences = "{ unparseable preferences";
        File.WriteAllText(prefsPath, malformedPreferences);
        Check(form.SuggestedWebsiteZipPath == defaultZip, "Malformed existing preferences safely suggest Documents without blocking export or modifying the file");
        string fallbackZip = Path.Combine(testRoot, "ZIP despite malformed preferences.zip");
        var fallback = await form.ExportWebsiteZipAsync(fallbackZip);
        Check(File.Exists(fallback.ZipPath) && File.ReadAllText(prefsPath) == malformedPreferences && Status.Contains("location could not be remembered"),
            "Export still succeeds with malformed preferences, preserving them and explaining that the created ZIP location could not be remembered");
        File.WriteAllBytes(prefsPath, validPreferences);
        WritePreferences("relative-invalid.zip");
        string invalidPreferences = File.ReadAllText(prefsPath);
        Check(form.SuggestedWebsiteZipPath == defaultZip && File.ReadAllText(prefsPath) == invalidPreferences, "An invalid remembered location falls back safely without silently rewriting preferences");
        File.WriteAllBytes(prefsPath, validPreferences);
        await form.OpenTopicAsync("basics/beta.htm");
        Check(form.OutputPreviewBuildCount == previewBuilds && form.SuggestedWebsiteZipPath == chosenZip, "Ordinary page navigation retains the export choice and triggers no export or whole-site build");
    }

    private static async Task<string> ReadPreferenceInFreshProcess()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "--read-preference", testRoot, sourceRoot }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25));
        if (child.ExitCode != 0) throw new InvalidOperationException(await error);
        return JsonSerializer.Deserialize<string>((await output).Trim())!;
    }
    private static string ZipText(ZipArchive zip, string path) { using var reader = new StreamReader(zip.GetEntry(path)?.Open() ?? throw new FileNotFoundException("Missing ZIP entry: " + path)); return reader.ReadToEnd(); }
    private static void WritePreferences(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(prefsPath)!);
        File.WriteAllText(prefsPath, JsonSerializer.Serialize(new { websiteZipPath = path, futureSetting = new { preserve = "unchanged" } }));
    }
    private static async Task<bool> Reject(Func<Task> action)
    {
        try { await action(); return false; }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or System.Xml.XmlException or OperationCanceledException or ArgumentException) { return true; }
    }
    private static string Clean(string? value) => (value ?? "").Replace("&", "");
    private static T Field<T>(string name) => (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    private static string Status => Field<ToolStripStatusLabel>("status").Text ?? "";
    private static Task Js(string script) => form.EditorWebView.ExecuteScriptAsync(script);
    private static async Task<string> String(string script) => JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync(script))!;
    private static Task<string> Source() => String("HelpEditor.getSource()");
    private static async Task<bool> Bool(string script) => JsonSerializer.Deserialize<bool>(await form.EditorWebView.ExecuteScriptAsync(script));
    private static Task<string> EditorSnapshot() => String("(()=>{const t=document.getElementById('source-editor');return JSON.stringify({source:HelpEditor.getSource(),tab:HelpEditor.getState().tab,history:HelpEditor.getState().historyIndex,start:t.selectionStart,end:t.selectionEnd,direction:t.selectionDirection,dirty:HelpEditor.getState().dirty})})()");
    private static Task SetSource(string source) => Js("HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(source) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
    private static async Task Until(Func<bool> condition, TimeSpan? timeout = null) { var timer = Stopwatch.StartNew(); while (!condition()) { if (timer.Elapsed > (timeout ?? TimeSpan.FromSeconds(15))) throw new TimeoutException("Native export did not finish: " + form.LastError); await Task.Delay(25); } }
    private static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static Dictionary<string, string> Digests(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(path => path, Digest);
    private static bool SameSources() => sourceBefore.Count == Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories).Length && sourceBefore.All(pair => File.Exists(pair.Key) && Digest(pair.Key) == pair.Value);
    private static void Check(bool passed, string name, object? detail = null) { checks.Add(new { name, passed, detail }); if (!passed) failed++; Console.WriteLine((passed ? "PASS " : "FAIL ") + name); }
    private static void CreateFixture()
    {
        void Write(string relative, string text) { string path = Path.Combine(sourceRoot, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); }
        string Page(string title, string body, string flag = "") => $"<html xmlns:sdh='{HelpSourceNamespaces.CurrentUri}' {flag}><head><title>{title}</title></head><body>{body}</body></html>";
        Write(HelpProject.MarkerFile, "{}");
        Write("Content/basics/alpha.htm", Page("Alpha", "<h1>Alpha</h1><p>Savedalpha</p><p sdh:conditions='Primary.WEBHELP'>PUBLIC_ONLY</p><p sdh:conditions='Primary.SWHELP'>DESKTOP_ONLY</p><p><a id='beta-link' href='beta.htm'>Beta</a></p><sdh:equation>$x^2$</sdh:equation><sdh:dropDown class='rcode'><sdh:dropDownHead>R code</sdh:dropDownHead><sdh:dropDownBody><pre><![CDATA[x <- c(1, 2)\n  mean(x)]]></pre></sdh:dropDownBody></sdh:dropDown>"));
        Write("Content/basics/beta.htm", Page("Beta", "<h1>Beta</h1><p>Saved beta page.</p>"));
        Write("Content/menus/about.htm", Page("About", "<h1>About</h1><p>aboutprivateword</p>", "sdh:searchable='False'"));
        Write("Content/resources/stylesheets/statsdirect.css", "body{font:10pt Arial}");
        Write("Content/resources/masterpages/website.flmsp", Page("Footer", "<sdh:pageFooter><p>WEBSITE_FOOTER</p></sdh:pageFooter>"));
        Write("Content/resources/masterpages/desktop.flmsp", Page("Footer", "<sdh:pageFooter><p>DESKTOP_FOOTER</p></sdh:pageFooter>"));
        Write("Project/Targets/Website.fltar", "<CatapultTarget MasterPage='Content/resources/masterpages/website.flmsp'/>");
        Write("Project/Targets/DesktopHelp.fltar", "<CatapultTarget MasterPage='Content/resources/masterpages/desktop.flmsp'/>");
        Write("Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title='Alpha' Link='/Content/basics/alpha.htm'/><TocEntry Title='Beta' Link='/Content/basics/beta.htm'/><TocEntry Title='About' Link='/Content/menus/about.htm'/></CatapultToc>");
        Write("Project/Advanced/STATSDIRECT.flali", "<CatapultAliasFile><Map Name='ABOUT' Link='/Content/menus/about.htm'/></CatapultAliasFile>");
        Write("Project/Advanced/SD3ContextID.h", "#define ABOUT 1001\n");
        Write(".help-editor-recovery/retained-author-data.txt", "Recovery data must never enter an exported website.");
    }
}
