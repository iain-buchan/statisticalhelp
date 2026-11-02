using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static class Program
{
    private static Form dialog = null!;
    private static HelpProject project = null!;
    private static string root = "", fixture = "", evidence = "";
    private static readonly List<object> results = [];
    private static readonly List<HelpReplaceResult> notifications = [];
    private static int passed, failed, uiExceptions;
    private static Dictionary<string, string> baseline = [];

    [STAThread]
    private static int Main(string[] args)
    {
        root = Path.GetFullPath(args[0]); evidence = Path.Combine(root, "evidence");
        fixture = Path.Combine(evidence, "SearchReplaceWindowsFixture-" + Guid.NewGuid().ToString("N"));
        CreateFixture(); baseline = Digests(); project = HelpProject.Open(fixture);
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => { uiExceptions++; Check(false, "Unhandled fixture UI exception", e.Exception.ToString()); };
        var type = typeof(MainForm).Assembly.GetType("StatsDirect.HelpEditor.App.SearchReplaceDialog", throwOnError: true)!;
        Func<HelpReplaceResult, Task> changed = result => { notifications.Add(result); return Task.CompletedTask; };
        dialog = (Form)Activator.CreateInstance(type, [project, changed])!;
        type.GetProperty("SuppressDialogs")?.SetValue(dialog, true);
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(-16000, 0);
        dialog.ShowInTaskbar = false;
        dialog.Shown += async (_, _) =>
        {
            try { await RunChecks(); }
            catch (Exception ex) { Check(false, "Unexpected native search/replace test failure", ex.ToString()); }
            finally { dialog.Close(); }
        };
        using var owner = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0), ShowInTaskbar = false, Size = new Size(1200, 850) };
        owner.Shown += async (_, _) =>
        {
            try
            {
                dialog.ShowDialog(owner);
                await RunMainFormIntegration();
            }
            catch (Exception ex) { Check(false, "Unexpected MainForm search/replace integration failure", ex.ToString()); }
            finally { owner.Close(); }
        };
        Application.Run(owner);
        var requested = type.GetProperty("RequestedMatch")?.GetValue(dialog) as HelpSearchMatch;
        Check(requested?.TopicPath == "nested/b.htm", "Open page returns the selected source match without writing it");
        Check(SameDigests(baseline), "Closing the reviewed search dialog does not write additional source changes");
        Check(uiExceptions == 0, "No unhandled UI exceptions during search, replacement and Undo");
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "search-replace-windows-checks.json"), JsonSerializer.Serialize(new { checkedAtUtc = DateTimeOffset.UtcNow, passed, failed, fixture, results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{passed} native search/replace checks passed; {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static async Task RunChecks()
    {
        var find = Control<TextBox>("Find text");
        var replacement = Control<TextBox>("Replace with");
        var matchCase = Control<CheckBox>("Match case");
        var wholeWords = Control<CheckBox>("Whole words");
        var scope = Control<ComboBox>("Search scope");
        var grid = Control<DataGridView>("Search results");
        Check(!matchCase.Checked && !wholeWords.Checked && scope.SelectedIndex == 0, "Search defaults to case-insensitive ordinary page text");
        Check(ReferenceEquals(dialog.AcceptButton, Button("Find all")) && !ReferenceEquals(dialog.AcceptButton, Button("Replace all")), "Enter defaults to finding and cannot apply a bulk replacement");
        Check(!Button("Replace selected").Enabled && !Button("Replace all").Enabled, "Replacement requires a completed match preview");
        Check(!Button("Undo last replace").Enabled, "A fresh project has no invented replacement Undo");

        find.Text = "alpha"; replacement.Text = "omega";
        await Find();
        Check(grid.Rows.Count >= 7 && SameDigests(baseline), "Find all previews matches across saved pages without changing any bytes");
        Check(grid.Rows.Cast<DataGridViewRow>().SelectMany(row => row.Cells.Cast<DataGridViewCell>()).Any(cell => (cell.Value?.ToString() ?? "").Contains("unlisted", StringComparison.OrdinalIgnoreCase)), "Search includes a page absent from the Contents tree");
        Check(grid.Columns.OfType<DataGridViewCheckBoxColumn>().Count() == 1 && grid.Columns.Cast<DataGridViewColumn>().Where(column => column is not DataGridViewCheckBoxColumn).All(column => column.ReadOnly), "Only occurrence-selection checkboxes are editable in the result preview");
        Check(grid.ColumnHeadersHeight >= grid.Font.Height && grid.RowTemplate.Height >= grid.Font.Height, "Result headers and rows fit the actual DPI-scaled font", new { dialog.DeviceDpi, grid.ColumnHeadersHeight, RowHeight = grid.RowTemplate.Height, FontHeight = grid.Font.Height });
        Check(grid.Columns.Cast<DataGridViewColumn>().All(column => column.Width >= TextRenderer.MeasureText(column.HeaderText, grid.Font).Width),
            "Every result column header fits at the actual display DPI", grid.Columns.Cast<DataGridViewColumn>().Select(column => new { column.HeaderText, column.Width, TextWidth = TextRenderer.MeasureText(column.HeaderText, grid.Font).Width }).ToArray());
        CheckButtonGeometry();
        int scopeTextWidth = scope.Items.Cast<object>().Select(item => TextRenderer.MeasureText(item.ToString(), scope.Font).Width).Max();
        Check(scope.Width >= scopeTextWidth + (int)Math.Ceiling(20 * scope.DeviceDpi / 96d), "Both search-scope choices fit the actual DPI-scaled dropdown", new { scope.Width, scopeTextWidth, scope.DeviceDpi });
        var initialSize = dialog.Size;
        dialog.Size = dialog.MinimumSize;
        dialog.PerformLayout();
        Check(grid.ClientSize.Height >= Math.Max(grid.Font.Height * 3, 150 * dialog.DeviceDpi / 96d) && grid.ClientSize.Width > 300 * dialog.DeviceDpi / 96d &&
            Descendants(dialog).OfType<Button>().Where(button => button.Visible).All(button => button.RectangleToScreen(button.ClientRectangle).Bottom <= dialog.RectangleToScreen(dialog.ClientRectangle).Bottom),
            "The minimum-size dialog retains usable results and visible action buttons", new { dialog.DeviceDpi, dialog.Size, grid.ClientSize });
        dialog.Size = initialSize;
        dialog.PerformLayout();

        Button("Select none").PerformClick(); grid.EndEdit();
        await Until(() => !Button("Replace selected").Enabled);
        Check(SelectedCount(grid) == 0 && Button("Replace all").Enabled, "Select none disables selected replacement while retaining explicit Replace all");
        Button("Select all").PerformClick(); grid.EndEdit();
        Check(SelectedCount(grid) == grid.Rows.Count, "Select all checks each previewed occurrence");
        Button("Select none").PerformClick();
        int selectedColumn = grid.Columns.OfType<DataGridViewCheckBoxColumn>().Single().Index;
        grid.Rows[0].Cells[selectedColumn].Value = true; grid.EndEdit();
        await Until(() => Button("Replace selected").Enabled);
        int beforeOccurrences = Occurrences("alpha"), beforeNotifications = notifications.Count;
        Button("Replace selected").PerformClick();
        await Until(() => notifications.Count > beforeNotifications && Button("Find all").Enabled);
        Check(notifications[^1].Replacements == 1 && notifications[^1].ChangedPages == 1 && Occurrences("alpha") == beforeOccurrences - 1, "Replace selected applies exactly one checked occurrence");
        Check(Occurrences("omega") == 1 && Button("Undo last replace").Enabled, "A successful reviewed replacement enables batch Undo");
        find.Clear();
        Check(Button("Undo last replace").Enabled, "Batch Undo remains available independently of the current search query");
        beforeNotifications = notifications.Count;
        Button("Undo last replace").PerformClick();
        await Until(() => notifications.Count > beforeNotifications && !Button("Undo last replace").Enabled);
        Check(SameDigests(baseline) && !Button("Undo last replace").Enabled && LastError() == null, "Undo last replace restores all original bytes with an empty search and consumes that Undo");

        find.Text = "alpha";
        await Find();
        replacement.Text = "delta";
        Check(!Button("Replace selected").Enabled && !Button("Replace all").Enabled, "Editing replacement text invalidates the old review before any write");
        await Find();
        find.Text = "alph";
        Check(!Button("Replace selected").Enabled && !Button("Replace all").Enabled, "Editing search text invalidates the old occurrence selection");
        find.Text = "alpha"; replacement.Text = "omega";
        wholeWords.Checked = true;
        await Find(); int wholeWordMatches = grid.Rows.Count;
        wholeWords.Checked = false;
        await Find();
        Check(wholeWordMatches < grid.Rows.Count, "Whole words changes the match preview without replacing substrings");
        matchCase.Checked = true;
        await Find(); int exactCaseMatches = grid.Rows.Count;
        matchCase.Checked = false;
        await Find();
        Check(exactCaseMatches < grid.Rows.Count, "Match case distinguishes alpha from Alpha and ALPHA");

        beforeNotifications = notifications.Count;
        Button("Replace all").PerformClick();
        await Until(() => notifications.Count > beforeNotifications && Button("Find all").Enabled);
        Check(notifications[^1].ChangedPages == 3 && notifications[^1].Replacements > 1, "Replace all updates every matched page, including the unlisted page");
        string a = File.ReadAllText(Path.Combine(fixture, "Content", "a.htm"));
        string b = File.ReadAllText(Path.Combine(fixture, "Content", "nested", "b.htm"));
        Check(a.Contains("<pre>alpha in R code</pre>") && a.Contains("alpha^2") && a.Contains("<!-- alpha comment -->") && b.Contains("https://example.invalid/alpha"), "Default page-text replacement leaves code, equations, comments and link attributes unchanged");
        beforeNotifications = notifications.Count;
        Button("Undo last replace").PerformClick();
        await Until(() => notifications.Count > beforeNotifications && Button("Find all").Enabled);
        Check(SameDigests(baseline), "Undo after Replace all restores every page exactly");

        matchCase.Checked = true;
        replacement.Text = "alpha";
        await Find();
        if (Button("Replace all").Enabled) { Button("Replace all").PerformClick(); await Until(() => Button("Find all").Enabled); }
        Check(SameDigests(baseline) && notifications.Count == 4 && !Button("Undo last replace").Enabled, "Replacing text with itself creates no source change or invented Undo");
        matchCase.Checked = false;

        find.Text = "https://example.invalid/alpha"; replacement.Text = "https://example.invalid/beta";
        await Find();
        Check(grid.Rows.Count == 0, "Ordinary page-text search does not include hidden link attributes");
        scope.SelectedIndex = 1;
        await Find();
        Check(grid.Rows.Count == 1 && SameDigests(baseline), "Explicit HTML source search can review a link URL without modifying it");
        replacement.Text = "<";
        await Find();
        Button("Replace all").PerformClick();
        await Until(() => Button("Find all").Enabled && LastError() != null);
        Check(SameDigests(baseline) && notifications.Count == 4 && grid.Rows.Count == 0 && !Button("Replace all").Enabled, "Invalid source replacement reports an inline error and clears the unsafe review without writes");

        replacement.Text = "https://example.invalid/beta";
        await Find();
        string changedPath = Path.Combine(fixture, "Content", "nested", "b.htm");
        byte[] originalPage = File.ReadAllBytes(changedPath);
        File.AppendAllText(changedPath, "<!-- external edit -->");
        byte[] externalEdit = File.ReadAllBytes(changedPath);
        Button("Replace all").PerformClick();
        await Until(() => Button("Find all").Enabled && LastError() != null);
        Check(File.ReadAllBytes(changedPath).SequenceEqual(externalEdit) && notifications.Count == 4 && !Button("Replace all").Enabled, "A stale preview cannot overwrite an external edit and requires a fresh review");
        File.WriteAllBytes(changedPath, originalPage);
        await Find();
        Check(LastError() == null && grid.Rows.Count == 1, "A fresh search recovers from an inline error without reopening the dialog");
        File.AppendAllText(changedPath, "<!-- edit after Find before Open -->");
        byte[] changedBeforeOpen = File.ReadAllBytes(changedPath);
        grid.CurrentCell = grid.Rows[0].Cells.Cast<DataGridViewCell>().First(cell => cell.OwningColumn is not DataGridViewCheckBoxColumn);
        grid.Rows[0].Selected = true;
        await Until(() => Button("Open page").Enabled);
        Button("Open page").PerformClick();
        Check(dialog.Visible && dialog.GetType().GetProperty("RequestedMatch")?.GetValue(dialog) == null && LastError() != null && grid.Rows.Count == 0 &&
            File.ReadAllBytes(changedPath).SequenceEqual(changedBeforeOpen) && notifications.Count == 4,
            "Opening a stale result keeps the dialog open, preserves the external edit and requests a fresh search");
        File.WriteAllBytes(changedPath, originalPage);
        await Find();
        Check(LastError() == null && grid.Rows.Count == 1, "Find all recovers an open-match review after stale source rejection");
        Check(notifications.Count == 4 && SameDigests(baseline), "Preview-only option changes cause no replacement callback or source writes");
        Check(ReferenceEquals(dialog.CancelButton, Button("Close")), "Escape closes the search dialog through its ordinary Close action");
        grid.CurrentCell = grid.Rows[0].Cells.Cast<DataGridViewCell>().First(cell => cell.OwningColumn is not DataGridViewCheckBoxColumn);
        grid.Rows[0].Selected = true;
        await Until(() => Button("Open page").Enabled);
        Button("Open page").PerformClick();
    }

    private static IEnumerable<Control> Descendants(Control control) => new[] { control }.Concat(control.Controls.Cast<Control>().SelectMany(Descendants));

    private static async Task RunMainFormIntegration()
    {
        string testRoot = Path.Combine(root, "tests", "StatsDirectHelpEditor.SearchReplaceWindowsChecks");
        string sourceRoot = Path.Combine(testRoot, "HelpSource");
        foreach (string file in Directory.GetFiles(fixture, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(sourceRoot, Path.GetRelativePath(fixture, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
        var sourceBytes = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        using var main = new MainForm(sourceRoot, _ => throw new InvalidOperationException("Unexpected external launch in integration fixture"))
        { SuppressDialogs = true, WindowState = FormWindowState.Normal, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0), ShowInTaskbar = false, Size = new Size(1600, 1000) };
        main.Show();
        try
        {
            await main.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
            await main.OpenTopicAsync("a.htm");
            Check(main.ArtifactRoot == testRoot && main.EditorWebView.CoreWebView2.Environment.UserDataFolder.StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "MainForm integration uses an isolated help source and renderer profile");
            var edit = main.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item => (item.Text ?? "").Replace("&", "") == "Edit");
            var command = edit.DropDownItems.OfType<ToolStripMenuItem>().Single(item => (item.Text ?? "").Contains("all pages") && !(item.Text ?? "").StartsWith("Undo"));
            var undoCommand = edit.DropDownItems.OfType<ToolStripMenuItem>().Single(item => (item.Text ?? "").StartsWith("Undo last replace"));
            Check(command.ShortcutKeys == (Keys.Control | Keys.Shift | Keys.H) && !undoCommand.Enabled, "Edit menu exposes global Find/replace with Ctrl+Shift+H and accurate initial Undo state");

            var originalProject = main.CurrentProject;
            string originalSource = originalProject.ReadTopic("a.htm");
            var preview = originalProject.SearchReplacePreview(new HelpSearchOptions("alpha", "omega", WholeWords: true));
            var applied = originalProject.ApplySearchReplace(preview, preview.Matches.Select(match => match.Id));
            await InvokeMain(main, "RefreshAfterSearchReplaceAsync", applied);
            string updatedSource = originalProject.ReadTopic("a.htm");
            Check(applied.ChangedPages == 3 && await MainSource(main) == updatedSource && MainField<string>(main, "loadedDiskSource") == updatedSource && !main.IsDirty, "Bulk callback reloads the changed current page and its saved-source concurrency snapshot");
            Check(MainField<ListBox>(main, "pages").Items.OfType<TopicInfo>().Any(topic => topic.Path == "a.htm" && topic.Title == "omega title") && main.Text.StartsWith("omega title"), "Bulk replacement refreshes page titles in the catalog and native window");
            Check(ReferenceEquals(originalProject, main.CurrentProject) && main.CurrentProject.CanUndoSearchReplace && undoCommand.Enabled, "Refreshing after replacement retains the project instance and batch Undo");

            await InvokeMain(main, "UndoSearchReplaceAsync");
            Check(sourceBytes.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) && await MainSource(main) == originalSource && MainField<string>(main, "loadedDiskSource") == originalSource, "MainForm Undo restores every original byte and refreshes the open page");
            Check(ReferenceEquals(originalProject, main.CurrentProject) && !originalProject.CanUndoSearchReplace && !undoCommand.Enabled, "MainForm Undo consumes only the latest batch while retaining project identity");

            var match = preview.Matches.First(item => item.TopicPath == "a.htm" && item.SourceOffset > originalSource.IndexOf("<body>", StringComparison.Ordinal));
            await InvokeMain(main, "OpenSearchMatchAsync", match);
            int expectedStart = NormalizeTextarea(originalSource[..match.SourceOffset]).Length;
            int expectedEnd = expectedStart + NormalizeTextarea(originalSource.Substring(match.SourceOffset, match.SourceLength)).Length;
            string selectionJson = await main.EditorWebView.ExecuteScriptAsync("(()=>{const t=document.getElementById('source-editor');return {tab:HelpEditor.getState().tab,start:t.selectionStart,end:t.selectionEnd,text:t.value.slice(t.selectionStart,t.selectionEnd)};})()");
            var selection = JsonDocument.Parse(selectionJson).RootElement;
            Check(selection.GetProperty("tab").GetString() == "source" && selection.GetProperty("start").GetInt32() == expectedStart && selection.GetProperty("end").GetInt32() == expectedEnd && selection.GetProperty("text").GetString() == NormalizeTextarea(originalSource.Substring(match.SourceOffset, match.SourceLength)), "Open match selects the exact HTML source occurrence after CRLF normalization");

            string currentPagePath = Path.Combine(sourceRoot, "Content", "a.htm");
            File.AppendAllText(currentPagePath, "<!-- external change before opening match -->");
            byte[] externalPage = File.ReadAllBytes(currentPagePath);
            bool staleMatchRejected = false;
            try { await InvokeMain(main, "OpenSearchMatchAsync", match); }
            catch (InvalidOperationException) { staleMatchRejected = true; }
            Check(staleMatchRejected && File.ReadAllBytes(currentPagePath).SequenceEqual(externalPage) &&
                await MainSource(main) == originalProject.ReadTopic("a.htm") && MainField<string>(main, "loadedDiskSource") == originalProject.ReadTopic("a.htm") && !main.IsDirty,
                "MainForm rejects an old match digest while retaining the newly loaded source and external edit");
            File.WriteAllBytes(currentPagePath, sourceBytes[currentPagePath]);
            await InvokeMain(main, "OpenSearchMatchAsync", match);

            int opened = 0;
            using (var timer = new System.Windows.Forms.Timer { Interval = 25 })
            {
                timer.Tick += (_, _) =>
                {
                    var popup = main.OwnedForms.SingleOrDefault(form => form.GetType().Name == "SearchReplaceDialog");
                    if (popup == null) return;
                    opened++;
                    timer.Stop();
                    popup.Close();
                };
                timer.Start();
                await main.EditorWebView.ExecuteScriptAsync("document.dispatchEvent(new KeyboardEvent('keydown',{key:'h',ctrlKey:true,shiftKey:true,bubbles:true,cancelable:true}));");
                await UntilMain(() => opened == 1 && !MainField<bool>(main, "busy"));
            }
            Check(opened == 1 && await MainSource(main) == originalSource && !main.IsDirty, "Ctrl+Shift+H from the HTML editor opens and closes the owned global dialog without changing source");

            string draft = NormalizeTextarea(originalSource.Replace("alpha.", "draft retained."));
            await main.EditorWebView.ExecuteScriptAsync("HelpEditor.setOutputView('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(draft) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
            await UntilMain(() => main.IsDirty);
            var other = originalProject.SearchReplacePreview(new HelpSearchOptions("beta alpha ALPHA.", "beta omega ALPHA.", MatchCase: true));
            var changedOther = originalProject.ApplySearchReplace(other, other.Matches.Select(item => item.Id));
            await InvokeMain(main, "RefreshAfterSearchReplaceAsync", changedOther);
            Check(changedOther.ChangedPages == 1 && !changedOther.ChangedTopicPaths.Contains("a.htm") && await MainSource(main) == draft && main.IsDirty, "Refreshing a different replaced page preserves the current unsaved editor buffer");
            var restoredOther = originalProject.UndoLastSearchReplace();
            await InvokeMain(main, "RefreshAfterSearchReplaceAsync", restoredOther);
            Check(await MainSource(main) == draft && main.IsDirty && sourceBytes.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "Refreshing an Undo of another page also preserves the unsaved buffer and original source bytes");
            Check(main.LastError == null, "MainForm edit-menu, callback, Undo and source navigation complete without native host errors", main.LastError);
        }
        finally
        {
            typeof(MainForm).GetField("closingApproved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, true);
            main.Close();
        }
    }

    private static string NormalizeTextarea(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    private static T MainField<T>(MainForm main, string name) => (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
    private static Task InvokeMain(MainForm main, string name, params object[] arguments) => (Task)typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, arguments)!;
    private static async Task<string> MainSource(MainForm main) => JsonSerializer.Deserialize<string>(await main.EditorWebView.ExecuteScriptAsync("HelpEditor.getSource()"))!;
    private static async Task UntilMain(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < timeout) { if (condition()) { await Task.Delay(75); if (condition()) return; } await Task.Delay(25); }
        throw new TimeoutException("The owned MainForm integration action did not finish.");
    }
    private static T Control<T>(string name) where T : Control => Descendants(dialog).OfType<T>().Single(control => control.AccessibleName == name);
    private static Button Button(string name) => Control<Button>(name);
    private static string? LastError() => dialog.GetType().GetProperty("LastError")?.GetValue(dialog) as string;
    private static int SelectedCount(DataGridView grid)
    {
        int column = grid.Columns.OfType<DataGridViewCheckBoxColumn>().Single().Index;
        return grid.Rows.Cast<DataGridViewRow>().Count(row => row.Cells[column].Value is true);
    }
    private static async Task Find()
    {
        Button("Find all").PerformClick();
        await Until(() => Button("Find all").Enabled);
        await Task.Delay(75);
    }
    private static async Task Until(Func<bool> condition)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < until)
        { if (condition()) { await Task.Delay(50); if (condition()) return; } await Task.Delay(25); }
        var grid = Control<DataGridView>("Search results");
        throw new TimeoutException("The owned search/replace fixture did not finish its action. Rows=" + grid.Rows.Count +
            ", CurrentRow=" + grid.CurrentRow?.Index + ", CurrentCell=" + grid.CurrentCell?.ColumnIndex +
            ", Open=" + Button("Open page").Enabled + ", ReplaceAll=" + Button("Replace all").Enabled +
            ", LastError=" + dialog.GetType().GetProperty("LastError")?.GetValue(dialog));
    }
    private static void CheckButtonGeometry()
    {
        var buttons = Descendants(dialog).OfType<Button>().Where(button => button.Visible).ToArray();
        Check(buttons.All(button => button.ClientSize.Width >= TextRenderer.MeasureText(button.Text, button.Font).Width + button.Padding.Horizontal && button.ClientSize.Height >= button.Font.Height),
            "Search/replace button labels fit their actual DPI-scaled controls", buttons.Select(button => new { button.Text, button.Size, button.Font.Height }).ToArray());
    }
    private static int Occurrences(string text) => Directory.GetFiles(Path.Combine(fixture, "Content"), "*.htm", SearchOption.AllDirectories).Sum(path => Regex.Matches(File.ReadAllText(path), Regex.Escape(text), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count);
    private static Dictionary<string, string> Digests() => Directory.GetFiles(fixture, "*", SearchOption.AllDirectories).ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    private static bool SameDigests(Dictionary<string, string> before) => before.Count == Directory.GetFiles(fixture, "*", SearchOption.AllDirectories).Length && before.All(pair => File.Exists(pair.Key) && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))) == pair.Value);
    private static void Check(bool condition, string name, object? detail = null)
    { if (condition) passed++; else failed++; results.Add(new { name, passed = condition, detail }); Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}"); }
    private static void CreateFixture()
    {
        Directory.CreateDirectory(Path.Combine(fixture, "Content", "nested"));
        Directory.CreateDirectory(Path.Combine(fixture, "Project", "TOCs"));
        File.WriteAllText(Path.Combine(fixture, HelpProject.MarkerFile), "{\"prototypeWorkingCopy\":true}");
        File.WriteAllText(Path.Combine(fixture, "StatsDirect.flprj"), "<CatapultProject />");
        File.WriteAllText(Path.Combine(fixture, "Project", "TOCs", "STATSDIRECT.fltoc"), "<CatapultToc><TocEntry Title=\"Alpha\" Link=\"/Content/a.htm\"/><TocEntry Title=\"Beta\" Link=\"/Content/nested/b.htm\"/></CatapultToc>");
        File.WriteAllText(Path.Combine(fixture, "Content", "a.htm"), "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>Alpha title</title></head><body><p>alpha Alpha alphabet &amp; alpha.</p><p><b>formatted alpha</b></p><pre>alpha in R code</pre><MadCap:equation>alpha^2</MadCap:equation><!-- alpha comment --></body></html>\r\n", new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(fixture, "Content", "nested", "b.htm"), "<html><head><title>Beta alpha</title></head><body><p>beta alpha ALPHA.</p><a href=\"https://example.invalid/alpha\">A useful link</a></body></html>\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(fixture, "Content", "unlisted.htm"), "<html><head><title>Unlisted page</title></head><body><p>unlisted alpha</p></body></html>\n", new UTF8Encoding(false));
    }
}
