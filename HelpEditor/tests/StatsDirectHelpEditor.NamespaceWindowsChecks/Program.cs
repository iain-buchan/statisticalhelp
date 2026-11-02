using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static class Program
{
    private static readonly List<object> checks = [];
    private static MainForm form = null!;
    private static int failed;
    private const string Topic = "basics/imported.htm";
    private static string fixture = "", sourceRoot = "", evidence = "", imported = "";

    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        evidence = Path.Combine(root, "evidence");
        fixture = Path.Combine(evidence, "NamespaceWindowsFixture-" + Guid.NewGuid().ToString("N"));
        sourceRoot = Path.Combine(fixture, "HelpSource");
        CreateFixture();
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, exception) => Check(false, "Unhandled native UI exception: " + exception.Exception);
        var workspace = new HelpEditorWorkspace(fixture, sourceRoot, null);
        form = new MainForm(sourceRoot, initialTopic: Topic, workspaceOverride: workspace)
        {
            SuppressDialogs = true, WindowState = FormWindowState.Normal, ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0), Size = new Size(1500, 950)
        };
        form.Shown += async (_, _) =>
        {
            try { await RunChecks(); }
            catch (Exception exception) { Check(false, "Unexpected namespace save regression failure: " + exception); }
            finally
            {
                var report = new { passed = failed == 0, checkedAtUtc = DateTimeOffset.UtcNow, fixture, checks = checks.Count, failed, results = checks };
                File.WriteAllText(Path.Combine(evidence, "namespace-windows-checks.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(JsonSerializer.Serialize(new { report.passed, report.checks, report.failed, report.fixture }));
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
        await form.OpenTopicAsync(Topic);
        Check(form.ArtifactRoot == fixture && form.CurrentProject.RootPath == sourceRoot && await Source() == imported && !form.IsDirty,
            "The native editor opens imported legacy XHTML unchanged in an isolated workspace and browser profile");
        Check(await Bool("document.querySelectorAll('.equation-widget').length===1"), "An imported legacy equation is editable before migration");
        string first = imported.Replace("BASELINE_TOKEN", "Firstsourceedit");
        await SetSource(first);
        // A browser textarea normalizes line endings on explicit source edits.
        // Compare the exact accepted buffer, rather than its injected spelling.
        first = await Source();
        await form.SaveCurrentTopicAsync();
        string expected = first.Replace(HelpSourceNamespaces.LegacyUri, HelpSourceNamespaces.CurrentUri);
        string disk = form.CurrentProject.ReadTopic(Topic);
        Check(disk == expected && await Source() == expected && Field<string>("loadedDiskSource") == expected && !form.IsDirty,
            "Saving an imported HTML-source edit updates disk, source buffer and host conflict baseline to the canonical namespace");
        Check((await Source()).Contains("<pre xml:space='preserve'><![CDATA[x <- c(1, 2)\n  mean(x)]]></pre>") &&
            XDocument.Parse(disk).Descendants(HelpSourceNamespaces.Current + "equation").Single().Value == "$\\frac{x}{2}$",
            "Namespace migration keeps equation text and the exact protected R block");
        string file = Path.Combine(sourceRoot, "Content", Topic.Replace('/', Path.DirectorySeparatorChar));
        DateTime timestamp = File.GetLastWriteTimeUtc(file);
        await form.SaveCurrentTopicAsync();
        Check(File.GetLastWriteTimeUtc(file) == timestamp && form.CurrentProject.ReadTopic(Topic) == expected && await Source() == expected && !form.IsDirty,
            "A repeated Save acknowledges the canonical baseline without a false conflict or rewrite");
        string second = expected.Replace("Firstsourceedit", "Secondsourceedit");
        await SetSource(second);
        await form.SaveCurrentTopicAsync();
        Check(form.CurrentProject.ReadTopic(Topic) == second && await Source() == second && Field<string>("loadedDiskSource") == second && !form.IsDirty,
            "A second source edit saves successfully against the canonical disk baseline");
        await form.OpenTopicAsync(Topic);
        Check(await Source() == second && await Bool("document.querySelectorAll('.equation-widget').length===1") && form.LastError == null && !form.IsDirty,
            "Reopening the migrated page retains edits and its editable equation without native errors");
    }

    private static T Field<T>(string name) => (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    private static Task Js(string script) => form.EditorWebView.ExecuteScriptAsync(script);
    private static async Task<string> Source() => JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync("window.HelpEditor.getSource()"))!;
    private static async Task<bool> Bool(string script) => JsonSerializer.Deserialize<bool>(await form.EditorWebView.ExecuteScriptAsync(script));
    private static Task SetSource(string source) => Js("HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(source) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
    private static void Check(bool passed, string name) { checks.Add(new { name, passed }); if (!passed) failed++; Console.WriteLine((passed ? "PASS " : "FAIL ") + name); }
    private static void CreateFixture()
    {
        void Write(string relative, string source)
        {
            string path = Path.Combine(sourceRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, source, new UTF8Encoding(false));
        }
        Write(HelpProject.MarkerFile, "{\"purpose\":\"isolated namespace native save checks\"}");
        imported = "<?xml version='1.0' encoding='utf-8'?>\r\n<html xmlns:Old='" + HelpSourceNamespaces.LegacyUri + "'><head><title>Imported namespace</title></head><body><h1>Imported namespace</h1><p>BASELINE_TOKEN &amp; &#945;.</p><Old:equation>$\\frac{x}{2}$</Old:equation><pre xml:space='preserve'><![CDATA[x <- c(1, 2)\r\n  mean(x)]]></pre></body></html>\r\n";
        Write("Content/" + Topic, imported);
        Write("Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title='Imported namespace' Link='/Content/basics/imported.htm'/></CatapultToc>");
    }
}
