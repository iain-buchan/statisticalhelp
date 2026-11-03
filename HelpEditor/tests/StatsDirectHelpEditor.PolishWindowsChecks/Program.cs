using System.Reflection;
using System.Text.Json;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static class Program
{
    private static MainForm form = null!;
    private static readonly List<object> results = [];
    private static int passed, failed, unhandled;
    private static string root = "", fixture = "", evidence = "";
    private const string PathName = "basics/alpha.htm";
    private const string Saved = "<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>Alpha</title></head><body><h1>Alpha</h1><p>Original wording with <a href=\"beta.htm#section\">linked context</a> and <b>important</b> meaning.</p><MadCap:equation>$x^2$</MadCap:equation><pre>mean(c(1, 2, 3))</pre><p>Last paragraph.</p></body></html>";

    [STAThread]
    private static int Main(string[] args)
    {
        root = System.IO.Path.GetFullPath(args[0]);
        evidence = System.IO.Path.Combine(root, "evidence");
        string testRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sdhe-polish-" + Guid.NewGuid().ToString("N")[..8]);
        fixture = System.IO.Path.Combine(testRoot, "HelpSource");
        CreateFixture();
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => { unhandled++; Check(false, "Unhandled UI exception", e.Exception.ToString()); };
        using var main = new MainForm(fixture, _ => throw new InvalidOperationException("Unexpected external launch"), workspaceOverride: new HelpEditorWorkspace(testRoot, fixture, null))
        { SuppressDialogs = true, WindowState = FormWindowState.Normal, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, 0), ShowInTaskbar = false, Size = new Size(1600, 1000) };
        form = main;
        form.Shown += async (_, _) =>
        {
            try
            {
                await form.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
                await form.OpenTopicAsync(PathName);
                await CheckDraftApplication();
                await CheckNativeReview();
                await CheckRequestLifecycle();
                await CheckProtectedComparisonProposal();
            }
            catch (Exception e) { Check(false, "Unexpected polish integration failure", e.ToString()); }
            finally
            {
                Check(unhandled == 0, "No unhandled UI errors");
                Check(form.CurrentProject.ReadTopic(PathName) == Saved, "No check saves or rewrites the help fixture");
                Directory.CreateDirectory(evidence);
                File.WriteAllText(System.IO.Path.Combine(evidence, "polish-windows-checks.json"), JsonSerializer.Serialize(new { checkedAtUtc = DateTimeOffset.UtcNow, fixture, passed, failed, results }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{passed} native polish checks passed; {failed} failed");
                typeof(MainForm).GetField("closingApproved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
                form.Close();
            }
        };
        Application.Run(form);
        return failed == 0 ? 0 : 1;
    }

    private static async Task CheckDraftApplication()
    {
        string draft = Saved.Replace("Original wording", "My unsaved wording");
        string revised = draft.Replace("My unsaved wording", "Clearer draft wording");
        await SetSource(draft);
        Check(await Source() == draft && await Bool("HelpEditor.getState().dirty"), "Review can start with the current unsaved draft");
        await Js("document.getElementById('source-editor').setSelectionRange(30,50,'backward')");
        Check(await Bool(CallApply(PathName, draft, revised)), "A reviewed source applies to its exact current draft");
        Check(await Source() == revised && await Bool("HelpEditor.getState().dirty"), "Accepted wording remains an unsaved draft");
        Check(await Bool("HelpEditor.getState().tab==='source' && document.getElementById('source-editor').selectionStart===30 && document.getElementById('source-editor').selectionEnd===50 && document.getElementById('source-editor').selectionDirection==='backward'"), "Applying changes preserves HTML source view and selection direction");
        Check(form.CurrentProject.ReadTopic(PathName) == Saved && Field<string?>("loadedDiskSource") == Saved, "Accept leaves saved bytes and the disk-conflict baseline untouched");
        await Js("HelpEditor.undo()");
        Check(await Source() == draft && await Bool("HelpEditor.getState().dirty"), "One normal Undo restores the exact unsaved pre-review draft");
        await Js("HelpEditor.undo()");
        Check(await Source() == Saved && !await Bool("HelpEditor.getState().dirty"), "Earlier typing remains in the existing Undo history");
        await Js("HelpEditor.redo();HelpEditor.redo()");
        Check(await Source() == revised, "Redo restores the complete accepted batch");

        int history = await Int("HelpEditor.getState().historyIndex");
        string stale = await Error(CallApply(PathName, draft, Saved));
        Check(stale.Contains("changed", StringComparison.OrdinalIgnoreCase) && await Source() == revised && history == await Int("HelpEditor.getState().historyIndex"), "A stale review cannot replace newer typing or damage Undo");
        string wrongPage = await Error(CallApply("basics/beta.htm", revised, Saved));
        Check(wrongPage.Contains("page", StringComparison.OrdinalIgnoreCase) && await Source() == revised, "A review from another page is refused");
        string malformed = await Error(CallApply(PathName, revised, "<html><body><p>broken</body></html>"));
        Check(malformed.Length > 0 && await Source() == revised && history == await Int("HelpEditor.getState().historyIndex"), "Malformed replacement is rejected before draft or history mutation");
        Check(await Bool(CallApply(PathName, revised, revised)) && history == await Int("HelpEditor.getState().historyIndex"), "An unchanged review adds no artificial Undo entry");
        await Js("document.getElementById('link-dialog').showModal()");
        string modal = await Error(CallApply(PathName, revised, Saved));
        Check(modal.Contains("dialog", StringComparison.OrdinalIgnoreCase) && await Source() == revised, "An open link/equation draft prevents replacing the editing document");
        await Js("document.getElementById('link-dialog').close()");

        await Js("HelpEditor.setTab('visual')");
        string visualBefore = await Source();
        string visualAfter = visualBefore.Replace("Clearer draft wording", "Readable wording");
        await Js(CallApply(PathName, visualBefore, visualAfter));
        Check(await Source() == visualAfter && await Bool("HelpEditor.getState().tab==='visual'"), "Formatted view remains selected after accepting changes");
        Check(await Bool("document.getElementById('visual-editor').querySelector('a').dataset.originalHref==='beta.htm#section' && HelpEditor.getState().protectedCount>=1 && document.getElementById('visual-editor').querySelector('pre').textContent==='mean(c(1, 2, 3))'"), "Rerender preserves the internal hyperlink, equation widget and code text");
        await Js("HelpEditor.undo()");
        Check(await Source() == visualBefore, "Undo in Formatted view restores the exact source including protected material");
    }

    private static async Task CheckNativeReview()
    {
        string draft = await Source();
        var snapshot = HelpPolishSnapshot.Create(PathName, draft);
        var first = snapshot.Blocks.First(block => block.Text.Contains("Clearer draft wording"));
        var last = snapshot.Blocks.First(block => block.Text == "Last paragraph.");
        var review = snapshot.Review([
            new(first.Id, first.Text, first.Text.Replace("Clearer draft wording", "Concise wording"), "Use direct wording."),
            new(last.Id, last.Text, "Final paragraph.", "Use consistent terminology.")]);
        var context = HelpPolishContext.Create(form.CurrentProject, snapshot);
        using var client = new FakeClient();
        using var dialog = new PolishReviewDialog(snapshot, context, client, _ => throw new InvalidOperationException("Unexpected sign-in launch")) { SuppressDialogs = true };
        dialog.Show(form);
        dialog.Location = new Point(-16000, 0);
        await Until(() => !Button(dialog, "Stop").Enabled);
        Check(Button(dialog, "Suggest changes").Enabled && !Button(dialog, "Apply accepted changes").Enabled, "Connected review opens with no invented changes and no possible apply");
        Check(Descendants(dialog).OfType<Label>().Any(label => label.Text.Contains("All 3 help pages")), "Review explains access to the whole help corpus, including the unlisted page");
        Check(dialog.AcceptButton == null || !ReferenceEquals(dialog.AcceptButton, Button(dialog, "Apply accepted changes")), "Pressing Enter cannot implicitly apply unreviewed changes");
        dialog.SetReview(review);
        var grid = Control<DataGridView>(dialog, "Suggested changes");
        var diff = Control<RichTextBox>(dialog, "Tracked changes");
        Check(grid.Rows.Count == 2 && grid.Rows.Cast<DataGridViewRow>().All(row => (string?)row.Cells[0].Value == "Pending") && dialog.AcceptedChangeIds.Count == 0,
            "Every suggestion starts Pending and requires an explicit acceptance");
        Check(!Button(dialog, "Apply accepted changes").Enabled && await Source() == draft, "Showing tracked changes leaves the current draft untouched");
        int deleted = diff.Text.IndexOf("Clearer", StringComparison.Ordinal), added = diff.Text.IndexOf("Concise", StringComparison.Ordinal);
        diff.Select(deleted, "Clearer".Length);
        bool oldStyle = diff.SelectionFont?.Strikeout == true && diff.SelectionColor.R > diff.SelectionColor.G;
        diff.Select(added, "Concise".Length);
        bool newStyle = diff.SelectionFont?.Underline == true && diff.SelectionColor.G > diff.SelectionColor.R;
        Check(deleted >= 0 && added >= 0 && oldStyle && newStyle, "Tracked text uses red strike-through for deletions and green underline for additions");
        Button(dialog, "Accept change").PerformClick();
        Check(dialog.AcceptedChangeIds.SequenceEqual([review.Changes[0].Id]) && Button(dialog, "Apply accepted changes").Enabled && grid.CurrentRow?.Index == 1,
            "Accept marks one change and advances to the next pending change");
        Button(dialog, "Reject change").PerformClick();
        Check(dialog.AcceptedChangeIds.SequenceEqual([review.Changes[0].Id]) && (string?)grid.Rows[1].Cells[0].Value == "Rejected", "Reject keeps the original text and excludes that change from application");
        Button(dialog, "Reject all").PerformClick();
        Check(dialog.AcceptedChangeIds.Count == 0 && !Button(dialog, "Apply accepted changes").Enabled, "Reject all removes every accepted change without touching the draft");
        Button(dialog, "Accept all").PerformClick();
        Check(dialog.AcceptedChangeIds.Count == 2 && grid.Rows.Cast<DataGridViewRow>().All(row => (string?)row.Cells[0].Value == "Accepted"), "Accept all is an explicit decision over the complete review");
        bool foreignRejected = false;
        try { dialog.SetReview(HelpPolishSnapshot.Create(PathName, draft).Review([])); } catch (InvalidOperationException) { foreignRejected = true; }
        Check(foreignRejected && dialog.Review == review && dialog.AcceptedChangeIds.Count == 2, "A review from another snapshot cannot replace the displayed decisions");
        var previousSize = dialog.Size;
        dialog.Size = dialog.MinimumSize; dialog.PerformLayout();
        Rectangle bounds = dialog.RectangleToScreen(dialog.ClientRectangle);
        var buttons = Descendants(dialog).OfType<Button>().Where(button => button.Visible).ToArray();
        Check(buttons.All(button => bounds.Contains(button.RectangleToScreen(button.ClientRectangle))) && diff.ClientSize.Height >= dialog.Font.Height * 3,
            "The minimum DPI-scaled dialog keeps every action visible and leaves readable tracked text", new { dialog.DeviceDpi, dialog.Size, Diff = diff.ClientSize });
        Check(buttons.All(button => button.ClientSize.Width >= TextRenderer.MeasureText(button.Text, button.Font).Width), "Every action caption fits its actual DPI-scaled button");
        Check(grid.ColumnHeadersHeight >= grid.Font.Height && grid.Columns[0].Width >= TextRenderer.MeasureText("Accepted", grid.Font).Width && grid.RowTemplate.Height >= grid.Font.Height,
            "Decision header, values and rows fit the actual display font");
        dialog.Size = previousSize;
        Button(dialog, "Reject all").PerformClick();
        grid.CurrentCell = grid.Rows[0].Cells[0]; Button(dialog, "Accept change").PerformClick();
        string[] accepted = dialog.AcceptedChangeIds.ToArray();
        Button(dialog, "Apply accepted changes").PerformClick();
        Check(dialog.DialogResult == DialogResult.OK && accepted.Length == 1 && await Source() == draft, "Apply returns only accepted IDs; the dialog never directly alters the page");
        await form.ApplyPolishReviewAsync(snapshot, review, accepted);
        string expected = review.Apply(accepted);
        Check(await Source() == expected && form.IsDirty && !expected.Contains("Final paragraph."), "Native integration applies accepted wording only as an unsaved draft");
        await Js("HelpEditor.undo()");
        Check(await Source() == draft, "Native application is one normal Undo step");
        bool wrongSnapshot = false;
        try { await form.ApplyPolishReviewAsync(HelpPolishSnapshot.Create(PathName, draft), review, accepted); }
        catch (InvalidOperationException) { wrongSnapshot = true; }
        Check(wrongSnapshot && await Source() == draft, "Native integration refuses mismatched review and snapshot identities");
        await SetSource(draft.Replace("Clearer draft wording", "A later manual edit"));
        bool newerDraft = false;
        try { await form.ApplyPolishReviewAsync(snapshot, review, accepted); }
        catch (InvalidOperationException) { newerDraft = true; }
        Check(newerDraft && (await Source()).Contains("A later manual edit"), "A completed review cannot overwrite later manual edits");
    }

    private static IEnumerable<Control> Descendants(Control owner) => owner.Controls.Cast<Control>().SelectMany(control => new[] { control }.Concat(Descendants(control)));
    private static T Control<T>(Control owner, string name) where T : Control => Descendants(owner).OfType<T>().Single(control => control.AccessibleName == name);
    private static Button Button(Control owner, string name) => Control<Button>(owner, name);
    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 400; i++) { if (condition()) return; await Task.Delay(25); }
        throw new TimeoutException("Native review did not finish its action.");
    }

    private sealed class FakeClient : IPolishChatGptClient
    {
        public Func<HelpPolishContext, HelpPolishSnapshot, string, CancellationToken, Task<HelpPolishReview>>? ReviewRequest { get; set; }
        public bool IsSignedIn => true;
        public string Status => "Connected to ChatGPT (owned test double)";
        public string? LoginUrl => null;
        public event Action? Changed { add { } remove { } }
        public Task InitializeAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<string?> SignInAsync(CancellationToken token = default) => throw new InvalidOperationException("Unexpected sign-in");
        public Task CancelSignInAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<HelpPolishReview> PolishAsync(HelpPolishContext context, HelpPolishSnapshot snapshot, string instructions, CancellationToken token = default) =>
            ReviewRequest?.Invoke(context, snapshot, instructions, token) ?? throw new InvalidOperationException("Unexpected live review");
        public void Dispose() { }
    }

    private static async Task CheckRequestLifecycle()
    {
        string before = await Source();
        var snapshot = HelpPolishSnapshot.Create(PathName, before);
        var block = snapshot.Blocks.First(item => item.Text == "Last paragraph.");
        var lateReview = snapshot.Review([new(block.Id, block.Text, "Final paragraph.", "Use consistent terminology.")]);
        var context = HelpPolishContext.Create(form.CurrentProject, snapshot);
        using var client = new FakeClient();
        var pending = new TaskCompletionSource<HelpPolishReview>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken requestToken = default;
        client.ReviewRequest = (_, _, _, token) => { requestToken = token; return pending.Task; };
        using var dialog = new PolishReviewDialog(snapshot, context, client, _ => throw new InvalidOperationException("Unexpected browser"));
        dialog.Show(form); dialog.Location = new Point(-16000, 0);
        await Until(() => Button(dialog, "Suggest changes").Enabled);
        Button(dialog, "Suggest changes").PerformClick();
        await Until(() => Button(dialog, "Stop").Enabled);
        Check(!Button(dialog, "Suggest changes").Enabled && !Button(dialog, "Apply accepted changes").Enabled && !Control<TextBox>(dialog, "Polishing instructions").Enabled,
            "An active request prevents duplicate requests, changing instructions or premature apply");
        Button(dialog, "Stop").PerformClick();
        Check(requestToken.IsCancellationRequested, "Stop cancels the active request token");
        pending.SetResult(lateReview); // Deliberately emulate a provider that reports success after Stop.
        await Until(() => !Button(dialog, "Stop").Enabled);
        Check(dialog.Review == null && !Button(dialog, "Apply accepted changes").Enabled && await Source() == before,
            "Late success after Stop cannot expose or apply cancelled suggestions");
        client.ReviewRequest = (_, _, _, _) => Task.FromException<HelpPolishReview>(new InvalidOperationException("Owned service failure"));
        Button(dialog, "Suggest changes").PerformClick();
        await Until(() => !Button(dialog, "Stop").Enabled);
        Check(dialog.LastError == "Owned service failure" && Button(dialog, "Suggest changes").Enabled && await Source() == before,
            "A failed request reports its error inline and leaves retry available without editing the page");
        client.ReviewRequest = (_, _, _, _) => Task.FromResult(snapshot.Review([]));
        Button(dialog, "Suggest changes").PerformClick();
        await Until(() => !Button(dialog, "Stop").Enabled);
        Check(dialog.Review?.Changes.Count == 0 && dialog.LastError == null && !Button(dialog, "Apply accepted changes").Enabled,
            "A successful retry with no changes clears the error and does not invent an edit");
        pending = new TaskCompletionSource<HelpPolishReview>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ReviewRequest = (_, _, _, token) => { requestToken = token; return pending.Task; };
        Button(dialog, "Suggest changes").PerformClick();
        await Until(() => Button(dialog, "Stop").Enabled);
        Button(dialog, "Close").PerformClick();
        Check(requestToken.IsCancellationRequested && !dialog.IsDisposed, "Closing an active review first requests cancellation");
        pending.SetResult(lateReview);
        await Until(() => dialog.IsDisposed);
        Check(await Source() == before, "Stop and close suppresses late suggestions and preserves the current draft");
    }

    private static async Task CheckProtectedComparisonProposal()
    {
        string before = await Source();
        string draft = before.Replace("</body>", "<p>Evidence: P &lt; 0.05.</p></body>");
        await SetSource(draft);
        // Source typing commits its Undo snapshot after a debounce. Exercise
        // the ordinary Undo/Redo commands before comparing history, so the
        // fixture's own pending typing cannot be mistaken for a review edit.
        await Js("HelpEditor.undo()");
        Check(await Source() == before, "The comparison fixture's new typing is one ordinary Undo step");
        await Js("HelpEditor.redo()");
        Check(await Source() == draft && await Bool("HelpEditor.getState().dirty"), "Redo restores the comparison fixture's exact unsaved draft before review");
        int history = await Int("HelpEditor.getState().historyIndex");
        string initialEditorState = await String("JSON.stringify(HelpEditor.getState())");
        var snapshot = HelpPolishSnapshot.Create(PathName, draft);
        var block = snapshot.Blocks.Single(item => item.Text == "Evidence: P < 0.05.");
        var context = HelpPolishContext.Create(form.CurrentProject, snapshot);
        using var client = new FakeClient
        {
            ReviewRequest = (_, _, _, _) => Task.FromResult(snapshot.Review([
                new(block.Id, block.Text, "Evidence: P > 0.05.", "Owned invalid statistical proposal.")]))
        };
        using var dialog = new PolishReviewDialog(snapshot, context, client, _ => throw new InvalidOperationException("Unexpected browser")) { SuppressDialogs = true };
        dialog.Show(form); dialog.Location = new Point(-16000, 0);
        await Until(() => Button(dialog, "Suggest changes").Enabled);
        Button(dialog, "Suggest changes").PerformClick();
        await Until(() => !Button(dialog, "Stop").Enabled);
        Check(dialog.LastError?.Contains("comparison", StringComparison.OrdinalIgnoreCase) == true && dialog.Review == null && !Button(dialog, "Apply accepted changes").Enabled,
            "A mocked AI proposal reversing a P-value comparison is rejected inline without exposing Apply");
        string actualDraft = await Source(), actualSaved = form.CurrentProject.ReadTopic(PathName);
        string? actualBaseline = Field<string?>("loadedDiskSource");
        int actualHistory = await Int("HelpEditor.getState().historyIndex");
        string finalEditorState = await String("JSON.stringify(HelpEditor.getState())");
        Check(actualDraft == draft, "Rejecting a changed numerical comparison preserves the exact current draft",
            new { expected = draft, actual = actualDraft });
        Check(actualSaved == Saved, "Rejecting a changed numerical comparison preserves the exact saved page",
            new { expected = Saved, actual = actualSaved });
        Check(actualBaseline == Saved, "Rejecting a changed numerical comparison preserves the exact disk-conflict baseline",
            new { expected = Saved, actual = actualBaseline });
        Check(actualHistory == history && finalEditorState == initialEditorState,
            "Rejecting a changed numerical comparison preserves settled Undo history and complete editor state",
            new { historyBefore = history, historyAfter = actualHistory, initialEditorState, finalEditorState });
        Button(dialog, "Close").PerformClick();
        await Js("HelpEditor.undo()");
        Check(await Source() == before, "The invalid comparison proposal leaves one ordinary Undo for the author's own draft edit");
    }

    private static string CallApply(string path, string before, string after) => "HelpEditor.applyReviewedSource(" + JsonSerializer.Serialize(path) + "," + JsonSerializer.Serialize(before) + "," + JsonSerializer.Serialize(after) + ")";
    private static async Task<string> Error(string script) => await String("(()=>{try{" + script + ";return '';}catch(e){return String(e.message||e)}})()");
    private static Task Js(string script) => form.EditorWebView.ExecuteScriptAsync(script);
    private static async Task<string> String(string script) => JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync(script)) ?? "";
    private static async Task<bool> Bool(string script) => JsonSerializer.Deserialize<bool>(await form.EditorWebView.ExecuteScriptAsync(script));
    private static async Task<int> Int(string script) => JsonSerializer.Deserialize<int>(await form.EditorWebView.ExecuteScriptAsync(script));
    private static Task<string> Source() => String("HelpEditor.getSource()");
    private static async Task SetSource(string value) => await Js("HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(value) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
    private static T Field<T>(string name) => (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    private static void Check(bool ok, string name, object? detail = null) { if (ok) passed++; else failed++; results.Add(new { name, passed = ok, detail }); Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}"); }
    private static void CreateFixture()
    {
        Directory.CreateDirectory(System.IO.Path.Combine(fixture, "Content", "basics"));
        Directory.CreateDirectory(System.IO.Path.Combine(fixture, "Project", "TOCs"));
        File.WriteAllText(System.IO.Path.Combine(fixture, HelpProject.MarkerFile), "{\"purpose\":\"Owned polish native tests\"}");
        File.WriteAllText(System.IO.Path.Combine(fixture, "Content", "contents.htm"), "<html><head><title>Contents</title></head><body><h1>Contents</h1></body></html>");
        File.WriteAllText(System.IO.Path.Combine(fixture, "Content", "basics", "alpha.htm"), Saved);
        File.WriteAllText(System.IO.Path.Combine(fixture, "Content", "basics", "beta.htm"), "<html><head><title>Beta</title></head><body><h1 id=\"section\">Beta</h1><p>Context on a different page.</p></body></html>");
        File.WriteAllText(System.IO.Path.Combine(fixture, "Project", "TOCs", "STATSDIRECT.fltoc"), "<CatapultToc><TocEntry Title=\"Contents\" Link=\"/Content/contents.htm\"/><TocEntry Title=\"Alpha\" Link=\"/Content/basics/alpha.htm\"/></CatapultToc>");
    }
}
