using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using StatsDirect.HelpEditor.App;

internal static class WebViewPolicyChecks
{
    private static readonly List<object> Checks = [];
    private static int failed;
    private static void Check(bool value, string name, object? detail = null)
    { Checks.Add(new { name, passed = value, detail }); if (!value) failed++; Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}"); }
    internal static async Task<bool> RunAsync(string root)
    {
        string fixture = Path.Combine(Path.GetTempPath(), "sdhe-policy-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(fixture); File.WriteAllText(Path.Combine(fixture, "index.html"), "<!doctype html><html><body>Offline policy</body></html>");
        File.WriteAllText(Path.Combine(fixture, "image.svg"), "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><rect width='2' height='2' fill='blue'/></svg>");
        using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new(-16000, 0), Size = new(800, 600) };
        using var browser = new WebView2 { Dock = DockStyle.Fill }; form.Controls.Add(browser);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        form.Shown += async (_, _) =>
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(fixture, "profile"));
                await browser.EnsureCoreWebView2Async(environment).WaitAsync(TimeSpan.FromSeconds(45));
                var core = browser.CoreWebView2;
                var policy = typeof(MainForm).Assembly.GetType("StatsDirect.HelpEditor.App.HelpWebViewSettings", true)!;
                var apply = policy.GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static)!;
                var map = policy.GetMethod("MapLocalFolder", BindingFlags.NonPublic | BindingFlags.Static)!;
                apply.Invoke(null, [core, true]);
                var s = core.Settings;
                Check(!s.AreHostObjectsAllowed && s.IsWebMessageEnabled && !s.AreBrowserAcceleratorKeysEnabled && !s.AreDevToolsEnabled && !s.IsStatusBarEnabled && !s.IsGeneralAutofillEnabled && !s.IsPasswordAutosaveEnabled, "Authoring policy enables only the required message bridge");
                map.Invoke(null, [core, "help-editor.invalid", fixture]); map.Invoke(null, [core, "help-project.invalid", fixture]);
                var ready = new TaskCompletionSource(); core.NavigationCompleted += (_, e) => { if (e.IsSuccess) ready.TrySetResult(); else ready.TrySetException(new IOException(e.WebErrorStatus.ToString())); };
                core.Navigate("https://help-editor.invalid/index.html"); await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
                string script = "(async()=>{const image=await new Promise(r=>{const i=new Image();i.onload=()=>r(i.naturalWidth===2);i.onerror=()=>r(false);i.src='https://help-project.invalid/image.svg';document.body.append(i);});let blocked=false;try{await fetch('https://help-project.invalid/image.svg');}catch{blocked=true;}const same=(await fetch('/image.svg')).ok;return {image,blocked,same};})()";
                string result = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = script, awaitPromise = true, returnByValue = true })).WaitAsync(TimeSpan.FromSeconds(30));
                using var parsed = JsonDocument.Parse(result); var value = parsed.RootElement.GetProperty("result").GetProperty("value");
                Check(value.GetProperty("image").GetBoolean(), "DenyCors still displays cross-origin source SVG artwork");
                Check(value.GetProperty("blocked").GetBoolean(), "DenyCors refuses cross-origin script fetch of project files");
                Check(value.GetProperty("same").GetBoolean(), "Same-origin editor runtime fetch remains available");
                apply.Invoke(null, [core, false]);
                Check(!core.Settings.IsWebMessageEnabled && !core.Settings.AreHostObjectsAllowed && !core.Settings.AreDevToolsEnabled, "Preview policy disables the authoring bridge and native host objects");
            }
            catch (Exception ex) { Check(false, "Policy checks completed", ex.ToString()); }
            finally
            {
                string evidence = Path.Combine(root, "evidence", "SourceContent-20261104"); Directory.CreateDirectory(evidence);
                File.WriteAllText(Path.Combine(evidence, "webview-policy-checks.json"), JsonSerializer.Serialize(new { completedAtUtc = DateTimeOffset.UtcNow, fixture, deviceDpi = form.DeviceDpi, passed = failed == 0, count = Checks.Count, checks = Checks }, new JsonSerializerOptions { WriteIndented = true }));
                form.Close(); finished.TrySetResult();
            }
        };
        form.Show();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(120));
        return failed == 0;
    }
}
