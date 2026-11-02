using System.Text.Json;
using System.Reflection;
using System.Xml.Linq;
using StatsDirect.HelpEditor.App;

internal static class Program
{
    private static readonly List<object> Checks = [];
    private static int passed, failed;
    private static void Check(bool ok, string name)
    {
        if (ok) passed++; else failed++;
        Checks.Add(new { name, passed = ok });
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}");
    }
    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        string evidence = Path.Combine(root, "evidence");
        string sourceRoot = Path.Combine(root, Directory.Exists(Path.Combine(root, "HelpSource")) ? "HelpSource" : "SampleProject");
        string testRoot = Path.Combine(root, "tests", "StatsDirectHelpEditor.WindowsChecks");
        // Keep the native renderer profile outside the user's running editor.
        // The explicitly selected source below is read-only during these checks.
        Directory.CreateDirectory(Path.Combine(testRoot, "HelpSource"));
        File.WriteAllText(Path.Combine(testRoot, "HelpSource", "TEST-ONLY.txt"), "Native check storage-root marker. The harness opens an explicit source copy; this is not the maintained help source.");
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => { Check(false, "Unhandled UI exception: " + e.Exception); };
        var form = new MainForm(sourceRoot) { SuppressDialogs = true, StartPosition = FormStartPosition.Manual, WindowState = FormWindowState.Normal, ShowInTaskbar = false, Location = new Point(-16000, 0) };
        form.Shown += async (_, _) =>
        {
            try
            {
                await form.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
                Check(form.IsEditorReady && form.LastError == null, "Native WebView2 editor initializes without errors");
                Check(form.ArtifactRoot == testRoot, "Native editor checks use an isolated renderer profile");
                Check(form.CurrentProject.ListTopics().Count == 404, "Native shell loads the full copied project");
                await form.OpenTopicAsync("basic_descriptive_statistics/time_series.htm");
                string original = form.CurrentProject.ReadTopic("basic_descriptive_statistics/time_series.htm");
                string unchanged = JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync("window.HelpEditor.getSource()"))!;
                Check(unchanged == original, "Native editor returns an untouched real topic exactly");
                Check(await ScriptBool(form, "document.querySelectorAll('.equation-widget').length === 1"), "Native MathLive renders a multiline equation widget");
                string coreFixture = Directory.GetDirectories(evidence, "CoreFixture-*").OrderByDescending(Directory.GetLastWriteTimeUtc).First();
                string fixtureParent = Path.Combine(evidence, "NativeFixture-" + Guid.NewGuid().ToString("N"));
                foreach (string file in Directory.EnumerateFiles(coreFixture, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(fixtureParent, Path.GetRelativePath(coreFixture, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
                await form.OpenProjectAsync(fixtureParent);
                string path = "basics/example.htm";
                await form.OpenTopicAsync(path);
                string before = form.CurrentProject.ReadTopic(path);
                string nativeMarker = "Nativeeditedcontext_" + Guid.NewGuid().ToString("N");
                string changed = before.Replace("Before &amp; after", nativeMarker + " Before &amp; after");
                Check(changed != before, "Native save check changes the fixture rather than reusing a previous edit");
                await form.EditorWebView.ExecuteScriptAsync("window.HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(changed) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
                await form.SaveCurrentTopicAsync();
                Check(form.CurrentProject.ReadTopic(path) == changed, "Native Save writes edited XHTML to the selected working copy");
                Check(!form.IsDirty, "Native Save clears the unsaved indicator");
                await form.OpenTopicAsync(path);
                Check(await ScriptBool(form, "window.HelpEditor.getSource().includes(" + JsonSerializer.Serialize(nativeMarker) + ")"), "Reopening a page retains the saved edits");
                await form.EditorWebView.ExecuteScriptAsync("window.HelpEditor.setTab('source');document.getElementById('source-editor').value='<html><body>broken';document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
                bool invalidBlocked = false;
                try { await form.SaveCurrentTopicAsync(); } catch { invalidBlocked = true; }
                Check(invalidBlocked && form.CurrentProject.ReadTopic(path) == changed, "Invalid XHTML is blocked without replacing the saved file");
                await form.EditorWebView.ExecuteScriptAsync("window.HelpEditor.load(" + JsonSerializer.Serialize(new { path, title = "Fixture", source = changed, baseUrl = "https://help-project.invalid/basics/example.htm" }) + ");");
                await form.SaveCurrentTopicAsync();
                await form.EditorWebView.ExecuteScriptAsync("window.HelpEditor.setTab('visual');document.querySelector('.equation-widget').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));");
                Check(await ScriptBool(form, "document.getElementById('equation-dialog').open"), "Native equation dialog opens on an existing equation");
                await form.EditorWebView.ExecuteScriptAsync("document.getElementById('equation-field').value='\\\\frac{a+b}{n}';document.getElementById('equation-apply').click();");
                await form.SaveCurrentTopicAsync();
                var saved = XDocument.Parse(form.CurrentProject.ReadTopic(path));
                XNamespace mc = "http://www.madcapsoftware.com/Schemas/MadCap.xsd";
                Check(saved.Descendants(mc + "equation").Single().Value.Contains("frac{a+b}{n}"), "Native MathLive editing saves back into a Flare equation");
                var sourceDocument = XDocument.Parse(changed);
                Check(saved.Descendants("pre").Select(p => p.Value).SequenceEqual(sourceDocument.Descendants("pre").Select(p => p.Value)), "Native formatted save preserves every R/code block, whitespace and Unicode");
                Check(saved.Descendants(mc + "keyword").Select(k => (string?)k.Attribute("term")).SequenceEqual(sourceDocument.Descendants(mc + "keyword").Select(k => (string?)k.Attribute("term"))), "Native formatted save preserves all keyword metadata");
                await form.OpenTopicAsync(path);
                await form.SwitchWorkspaceAsync("public");
                Check(form.LastOutputPreviewResult != null && File.ReadAllText(Path.Combine(form.LastOutputPreviewResult.OutputDirectory, path.ToLowerInvariant())).Contains("frac{a+b}{n}"), "Generated Browser view receives the updated equation");
                await form.OpenProjectAsync(sourceRoot);
                await form.OpenTopicAsync("rates/incidence_and_prevalence.htm");
                using var screenshot = File.Create(Path.Combine(evidence, "native-editor.png"));
                await form.EditorWebView.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, screenshot);
                Check(form.LastError == null, "No native host errors during the edit/save/reopen flow");
            }
            catch (Exception ex) { Check(false, "Unexpected native check failure: " + ex); }
            finally
            {
                File.WriteAllText(Path.Combine(evidence, "windows-checks.json"), JsonSerializer.Serialize(new { passed, failed, results = Checks }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{passed} native checks passed; {failed} failed");
                // This harness owns the form and must also close after a failed
                // assertion, when intentionally broken/draft source may remain.
                typeof(MainForm).GetField("closingApproved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
                form.Close();
            }
        };
        Application.Run(form);
        return failed == 0 ? 0 : 1;
    }
    private static async Task<bool> ScriptBool(MainForm form, string source) => JsonSerializer.Deserialize<bool>(await form.EditorWebView.ExecuteScriptAsync(source));
}
