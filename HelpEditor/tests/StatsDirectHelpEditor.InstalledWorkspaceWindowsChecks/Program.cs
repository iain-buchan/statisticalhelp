using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static class Program
{
    private static readonly List<object> Checks = [];
    private static int failed;
    private static MainForm form = null!;
    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        // Chromium profiles create their own deeply nested storage. Keep the
        // isolated user-data fixture independent of the checkout's path length.
        string fixture = Path.Combine(Path.GetTempPath(), "sdhe-install-" + Guid.NewGuid().ToString("N"));
        string application = Path.Combine(fixture, "Installed program");
        string seed = Path.Combine(application, "HelpSource");
        void Write(string relative, string content) { string path = Path.Combine(seed, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); }
        Write(HelpProject.MarkerFile, "{}");
        const string page = "<html><head><title>Installed example</title></head><body><h1>Installed example</h1><p>Original example</p><img src='resources/images/curve.png'/></body></html>";
        Write("Content/contents.htm", page);
        Write("Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title='Installed example' Link='/Content/contents.htm'/></CatapultToc>");
        Directory.CreateDirectory(Path.Combine(seed, "Content", "resources", "images"));
        File.Copy(Path.Combine(root, "HelpSource", "Content", "resources", "Images", "normalcurves.png"), Path.Combine(seed, "Content", "resources", "images", "curve.png"));
        File.WriteAllText(Path.Combine(application, HelpEditorWorkspace.InstallMarkerFile), "{\"schemaVersion\":1}");
        string originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(seed, "Content", "contents.htm"))));
        File.SetAttributes(Path.Combine(seed, "Content", "contents.htm"), FileAttributes.ReadOnly);
        var workspace = HelpEditorWorkspace.Resolve(application, Path.Combine(fixture, "Isolated user data"));
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Check(false, "No unhandled installed-host exception: " + e.Exception);
        form = new MainForm(workspaceOverride: workspace) { SuppressDialogs = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, WindowState = FormWindowState.Normal, Location = new(-16000, 0), Size = new(1600, 1000) };
        form.Shown += async (_, _) =>
        {
            try
            {
                await form.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
                Check(form.LastError == null && form.CurrentProject.RootPath == workspace.DefaultProjectPath, "Installed startup creates and opens the per-user working copy without a supplied project argument");
                Check(form.ArtifactRoot == workspace.RootPath && !form.ArtifactRoot.StartsWith(application, StringComparison.OrdinalIgnoreCase), "Installed runtime stores mutable state outside its program installation");
                Check(form.EditorWebView.CoreWebView2.Environment.UserDataFolder == Path.Combine(workspace.RootPath, ".editor-profile"), "The real WebView2 editor profile uses the isolated per-user workspace");
                var until = DateTime.UtcNow.AddSeconds(10);
                bool imageLoaded = false;
                do { imageLoaded = await Bool("document.querySelector('#visual-editor img')?.naturalWidth===446"); if (!imageLoaded) await Task.Delay(50); } while (!imageLoaded && DateTime.UtcNow < until);
                Check(imageLoaded, "First installed startup maps and displays source artwork from the writable copy");
                string edited = page.Replace("Original example", "Saved author edit");
                await form.EditorWebView.ExecuteScriptAsync("HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(edited) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
                await form.SaveCurrentTopicAsync();
                Check(form.CurrentProject.ReadTopic("contents.htm") == edited && !form.IsDirty, "An author can save the installed example without elevation or changing the seed");
                await form.BuildHelpAsync();
                Check(form.LastBuildResult?.TopicCount == 1 && form.LastBuildResult.OutputDirectory == Path.Combine(workspace.RootPath, "Build", "Desktop") && File.Exists(Path.Combine(workspace.RootPath, "Build", "Desktop", "search-index.js")), "The installed editor rebuilds help and search beneath the writable user workspace");
                await form.SwitchWorkspaceAsync("desktop");
                Check(form.AppPreviewWebView?.CoreWebView2.Environment.UserDataFolder.StartsWith(workspace.RootPath, StringComparison.OrdinalIgnoreCase) == true, "The docked StatsDirect preview also keeps its browser profile outside the install folder");
                Check(!Directory.Exists(Path.Combine(application, "Build")) && !Directory.Exists(Path.Combine(application, ".editor-profile")) && originalHash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(seed, "Content", "contents.htm")))), "Startup, editing and previews write nothing into the immutable program seed");
                await form.SwitchWorkspaceAsync("edit");
                workspace.PrepareDefaultProject();
                Check(form.CurrentProject.ReadTopic("contents.htm") == edited, "Preparing an existing installed workspace retains the saved author edit");
                Check(form.LastError == null, "Installed editing, help generation and preview report no hidden host errors");
            }
            catch (Exception ex) { Check(false, "Installed native checks complete: " + ex); }
            finally
            {
                File.WriteAllText(Path.Combine(root, "evidence", "installed-workspace-windows-checks.json"), JsonSerializer.Serialize(new { checkedAtUtc = DateTimeOffset.UtcNow, completed = true, passed = Checks.Count - failed, failed, fixture, checks = Checks }, new JsonSerializerOptions { WriteIndented = true }));
                typeof(MainForm).GetField("closingApproved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, true);
                form.Close();
            }
        };
        Application.Run(form);
        Console.WriteLine($"{Checks.Count - failed} installed native checks passed; {failed} failed");
        return failed == 0 ? 0 : 1;
    }
    private static async Task<bool> Bool(string expression) => await form.EditorWebView.ExecuteScriptAsync("Boolean(" + expression + ")") == "true";
    private static void Check(bool passed, string name) { Checks.Add(new { name, passed }); if (!passed) failed++; Console.WriteLine((passed ? "PASS " : "FAIL ") + name); }
}
