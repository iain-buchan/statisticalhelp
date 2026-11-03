using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;

internal static partial class Program
{
    private static async Task CheckEmbeddedPreviewAsync()
    {
        string disk = Hash(file), before = form.Document.Content.ToJsonString();
        var tabs = (TabControl)typeof(StatsDirect.HelpEditor.App.TutorEditorForm).GetField("tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var browser = (WebView2)typeof(StatsDirect.HelpEditor.App.TutorEditorForm).GetField("preview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var navigation = new List<object>();
        browser.CoreWebView2InitializationCompleted += (_, e) =>
        {
            navigation.Add(new { kind = "initialized", e.IsSuccess });
            if (!e.IsSuccess) return;
            browser.CoreWebView2.NavigationStarting += (_, n) => navigation.Add(new { kind = "starting", n.Uri, n.NavigationId, n.Cancel });
            browser.CoreWebView2.NavigationCompleted += (_, n) => navigation.Add(new { kind = "completed", n.NavigationId, n.IsSuccess, error = n.WebErrorStatus.ToString() });
        };
        form.SelectItem("questions", "question-1"); tabs.SelectedIndex = 2;
        File.WriteAllText(Path.Combine(fixture, "embedded-preview-expected.html"), form.BuildPreviewHtml());
        async Task<bool> Bool(string expression)
        {
            if (browser.CoreWebView2 == null) return false;
            return await browser.CoreWebView2.ExecuteScriptAsync("Boolean(" + expression + ")") == "true";
        }
        async Task Wait(Func<Task<bool>> condition)
        {
            var watch = Stopwatch.StartNew();
            while (!await condition())
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(35))
                {
                    object? Field(string name) => typeof(StatsDirect.HelpEditor.App.TutorEditorForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form);
                    string? html = browser.CoreWebView2 == null ? null : await browser.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML").WaitAsync(TimeSpan.FromSeconds(3));
                    File.WriteAllText(Path.Combine(fixture, "embedded-preview-diagnostic.json"), JsonSerializer.Serialize(new { tabs.SelectedIndex, ready = Field("previewReady"), busy = Field("busy"), rawMode = Field("rawMode"), formVisible = form.Visible, browserVisible = browser.Visible, browser.IsHandleCreated, coreCreated = browser.CoreWebView2 != null, browser.Source, html, form.LastError, navigation }));
                    throw new TimeoutException("Embedded learner preview did not reach the expected state: " + form.LastError);
                }
                await Task.Delay(40);
            }
        }
        await Wait(() => Bool("document.querySelectorAll('input[name=answer]').length===2"));
        Check(form.LastError == null && browser.CoreWebView2 != null, "First-use embedded WebView2 preview initializes in its isolated writable workspace");
        await browser.CoreWebView2!.ExecuteScriptAsync("document.querySelector('button').click()");
        Check(await Bool("document.getElementById('feedback').hidden && !document.querySelector('input:checked')"), "Embedded preview does not reveal an answer before a choice is selected");
        await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('input[value=B]').click();document.querySelector('button').click()");
        Check(await Bool("!document.getElementById('feedback').hidden && document.getElementById('feedback').textContent.startsWith('Review this choice.') && document.getElementById('feedback').textContent.includes('This inverts the rate.')"), "Wrong MCQ choice shows its actual option feedback inside the native preview");
        await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('input[value=A]').click();document.querySelector('button').click()");
        Check(await Bool("document.getElementById('feedback').textContent.startsWith('Correct.') && document.getElementById('feedback').textContent.includes('12/600 = 0.02.')"), "Correct MCQ choice follows the authored key inside the native preview");
        form.SelectItem("lessons", "lesson-1");
        await Wait(() => Bool("document.querySelector('h1')?.textContent === 'Health lesson' && document.querySelectorAll('input[name=answer]').length===0"));
        Check(await Bool("document.querySelector('main').textContent.includes('Distinguish risk and rate.') && document.querySelector('a').href==='https://www.cdc.gov/'"), "Embedded preview navigation replaces question state with lesson content and its source link");
        await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('a').click()");
        await Task.Delay(80);
        Check(await Bool("location.protocol==='data:' && document.querySelector('h1')?.textContent==='Health lesson'"), "Source navigation is intercepted without replacing the embedded learner preview");
        form.SelectItem("questions", "question-1");
        await Wait(() => Bool("document.querySelectorAll('input[name=answer]').length===2"));
        Check(await Bool("document.getElementById('feedback').hidden && !document.querySelector('input:checked')"), "Revisiting a question starts a fresh preview with no saved learner attempt");
        Check(!form.HasUnsavedChanges && Hash(file) == disk && form.Document.Content.ToJsonString() == before, "Native preview use and answer checking never modify course source or create a learner record");
    }
}
