using StatsDirect.HelpEditor.App.AppHelp;

internal static class CorruptIndexPreviewChecks
{
    internal static async Task Run(Form owner, string assets, string output, Action<bool, string> check)
    {
        string fixture = Path.Combine(output, "corrupt-index-preview");
        string copy = Path.Combine(fixture, "Help");
        foreach (string source in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(copy, Path.GetRelativePath(assets, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
        }

        (string Name, string Json)[] cases =
        [
            ("malformed JSON", "{ not valid JSON"),
            ("an unsafe absolute target", """{"schemaVersion":1,"entries":[{"term":"Unsafe","title":"Unsafe","path":"https://example.com/"}]}""")
        ];
        for (int number = 0; number < cases.Length; number++)
        {
            var test = cases[number];
            File.WriteAllText(Path.Combine(copy, "keyword-index.json"), test.Json);
            using var frame = new Form
            {
                Text = "Corrupt preview Index regression", Width = 900, Height = 780, ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual, Location = new Point(-17000, -17000)
            };
            using var view = new HelpView(new HelpCatalog(copy), Path.Combine(fixture, "profile-" + number));
            frame.Controls.Add(view);
            var escaped = new List<Exception>();
            // Opening is async void: capture the real UI-thread escape so a
            // regression fails this private fixture rather than showing a dialog.
            System.Threading.ThreadExceptionEventHandler handler = (_, e) => escaped.Add(e.Exception);
            Application.ThreadException += handler;
            try
            {
                view.ShowTopic("one.htm#Ref-Case");
                frame.Show(owner);
                await view.Ready.WaitAsync(TimeSpan.FromSeconds(45));
                var core = view.Browser.CoreWebView2;
                var index = view.Controls.OfType<ToolStrip>().SelectMany(strip => strip.Items.OfType<ToolStripDropDownButton>())
                    .Single(button => button.Text == "Index");
                var status = view.Controls.OfType<StatusStrip>().Single().Items[0];
                await Wait(() => index.Enabled && core.Source.EndsWith("/one.htm#Ref-Case", StringComparison.Ordinal));
                await core.ExecuteScriptAsync("window.corruptIndexSnapshot={url:location.href,history:history.length};window.corruptIndexToken='retained';");
                index.ShowDropDown();
                await Wait(() => escaped.Count > 0 || status.Text?.StartsWith("Could not open the help index:", StringComparison.Ordinal) == true);
                check(escaped.Count == 0, $"App preview Index handles {test.Name} without an escaped UI exception" +
                    (escaped.Count == 0 ? "" : $": {escaped[0].GetType().Name}: {escaped[0].Message}"));
                check(status.Text?.StartsWith("Could not open the help index:", StringComparison.Ordinal) == true, $"App preview Index reports {test.Name} in its native status line");
                check(!index.DropDown.Visible, $"App preview Index does not open a picker for {test.Name}");
                string unchanged = await core.ExecuteScriptAsync("location.href===window.corruptIndexSnapshot.url && history.length===window.corruptIndexSnapshot.history && window.corruptIndexToken==='retained'");
                check(unchanged == "true", $"App preview Index retains the URL, history and document after {test.Name}");
            }
            finally
            {
                Application.ThreadException -= handler;
                frame.Close();
            }
        }
    }

    private static async Task Wait(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 150; attempt++)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("The corrupt preview Index did not reach an observable result.");
    }
}
