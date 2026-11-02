using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static class Program
{
    private static readonly List<object> Results = [];
    private static int passed, failed;
    private static MainForm form = null!;
    private static string fixture = "", evidence = "";

    [STAThread]
    private static int Main(string[] args)
    {
        string artifact = Path.GetFullPath(args[0]);
        string testRoot = Path.Combine(artifact, "tests", "StatsDirectHelpEditor.ContentsWindowsChecks");
        fixture = Path.Combine(testRoot, "HelpSource");
        evidence = Path.Combine(artifact, "evidence");
        Directory.CreateDirectory(evidence);
        CreateFixture(fixture);
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Check(false, "Unhandled native UI exception: " + e.Exception);
        form = new MainForm(fixture) { SuppressDialogs = true, WindowState = FormWindowState.Normal, StartPosition = FormStartPosition.Manual, Location = new Point(-15000, 0), Size = new Size(1600, 1000) };
        form.Shown += async (_, _) =>
        {
            try
            {
                await form.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
                Check(form.IsEditorReady && form.LastError == null, "Native contents fixture opens without host errors");
                Check(!form.EditorWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled, "Browser refresh shortcuts cannot bypass the native unsaved-work guards");
                Check(form.ArtifactRoot == testRoot, "Native test uses an isolated profile and build root");
                Check(form.CurrentTopicPath == "basics/alpha.htm", "First linked child opens beneath an unlinked section");

                Select("Beta");
                await InvokeTask("MoveContentsAsync", -1);
                Check(Titles(Section("Section A")) == "Beta|Alpha|Gamma", "Move up updates sibling order on disk");
                Check(SelectedModel().Title == "Beta", "Move up preserves the selected entry");
                string edge = ReadToc();
                await InvokeTask("MoveContentsAsync", -1);
                Check(ReadToc() == edge, "Moving the first sibling up is a harmless no-op");
                await InvokeTask("MoveContentsAsync", 1);
                Check(Titles(Section("Section A")) == "Alpha|Beta|Gamma", "Move down updates and persists sibling order");

                Select("Beta");
                await InvokeTask("IndentContentsAsync");
                Check(Section("Section A").Elements("TocEntry").Single(e => Title(e) == "Alpha").Elements("TocEntry").Single().Attribute("Title")!.Value == "Beta", "Indent nests an entry under the preceding sibling");
                Check(SelectedModel().Title == "Beta", "Indent keeps the moved entry selected and visible");
                Check(Entry("Beta").Attribute("CustomMetadata")!.Value == "preserve-beta", "Moving retains Flare metadata on the entry");
                await InvokeTask("OutdentContentsAsync");
                Check(Titles(Section("Section A")) == "Alpha|Beta|Gamma", "Outdent places the entry immediately after its former parent");
                Check(!Entry("Alpha").Elements("TocEntry").Any(), "Outdent removes the old child rather than duplicating it");
                Select("Section A");
                edge = ReadToc();
                await InvokeTask("OutdentContentsAsync");
                Check(ReadToc() == edge, "Outdenting a root section is a harmless no-op");
                Select("Gamma");
                edge = ReadToc();
                await InvokeTask("MoveContentsAsync", 1);
                Check(ReadToc() == edge, "Moving the last sibling down is a harmless no-op");

                Select("Section B");
                await InvokeTask("IndentContentsAsync");
                Check(Section("Section A").Elements("TocEntry").Any(e => Title(e) == "Section B"), "A whole section can be nested");
                Check(Entry("Section B").Elements("TocEntry").Single().Attribute("Title")!.Value == "Delta", "Nesting retains the section's descendants");
                await InvokeTask("OutdentContentsAsync");
                Check(Toc().Root!.Elements("TocEntry").Select(Title).SequenceEqual(new[] { "Section A", "Section B" }), "A section can be restored to the root without losing order");

                // Exercise the same persistence layer used after modal title/page choosers,
                // without accepting UI prompts or adding test hooks to the application.
                Select("Beta");
                var beta = SelectedModel();
                beta.Title = "Beta renamed – λ";
                SaveToc(beta.Id);
                Check(Entry("Beta renamed – λ").Attribute("Link")!.Value == "/Content/basics/beta.htm", "Renaming changes the label while retaining its destination");
                Check(SelectedModel().Title == "Beta renamed – λ", "Renamed Unicode title appears in the native tree");
                beta.Link = "basics/gamma.htm#example-anchor";
                SaveToc(beta.Id);
                Check(Entry("Beta renamed – λ").Attribute("Link")!.Value.EndsWith("gamma.htm#example-anchor"), "Contents destination edits preserve a fragment");

                Select("Section A");
                var added = new TocNode { Title = "Added section" };
                Invoke("InsertAtSelection", added);
                SaveToc(added.Id);
                Check(Section("Section A").Elements("TocEntry").Any(e => Title(e) == "Added section"), "A new section is added under the selected unlinked section");
                var link = new TocNode { Title = "Existing Delta", Link = "basics/delta.htm" };
                Invoke("InsertAtSelection", link);
                SaveToc(link.Id);
                Check(Entry("Added section").Elements("TocEntry").Single().Attribute("Link")!.Value.EndsWith("delta.htm"), "An existing page can be linked beneath a new section");
                Select("Added section");
                Field<List<TocNode>>("toc").Single(n => n.Title == "Section A").Children.Remove(SelectedModel());
                SaveToc(null);
                Check(!Toc().Descendants("TocEntry").Any(e => Title(e) == "Added section") && File.Exists(Path.Combine(fixture, "Content/basics/delta.htm")), "Removing a contents branch preserves its page files");

                Select("Section A");
                string created = form.CurrentProject.CreateTopic("Native contents page " + Guid.NewGuid().ToString("N")[..6], "basics");
                var newPage = new TocNode { Title = "New linked page", Link = created };
                Invoke("InsertAtSelection", newPage);
                SaveToc(newPage.Id);
                Check(File.Exists(Path.Combine(fixture, "Content", created)) && Entry("New linked page") != null, "A newly created page is added to the selected section");
                form.CurrentProject.DeleteTopic(created);
                Invoke("ReloadProjectLists");
                Check(!File.Exists(Path.Combine(fixture, "Content", created)) && !Toc().Descendants("TocEntry").Any(e => Title(e) == "New linked page"), "Deleting an unreferenced page removes its contents entry");

                Select("Alpha");
                string savedToc = ReadToc();
                SelectedModel().Title = "";
                bool invalidBlocked = false;
                try { SaveToc(null); } catch (Exception) { invalidBlocked = true; }
                Check(invalidBlocked && ReadToc() == savedToc, "Invalid contents changes do not replace the saved tree");
                Check(FindTree("Alpha") != null, "A rejected save restores the native tree from disk");
                Check(Section("Section A").Attribute("BreakType")!.Value == "chapter" && Entry("Beta renamed – λ").Attribute("CustomMetadata")!.Value == "preserve-beta", "Repeated contents edits retain section and entry metadata");

                Select("Section A");
                await InvokeTask("CreateTopicInContentsAsync", "Freshly created native page", "basics");
                string freshPath = form.CurrentTopicPath!;
                Check(form.Text.StartsWith("Freshly created native page"), "New page opens with its real title immediately");
                Check(Field<ListBox>("pages").Items.Cast<TopicInfo>().Any(t => t.Path == freshPath), "A new page appears in All pages before its first edit/save");
                await form.OpenTopicAsync("basics/alpha.htm");
                form.CurrentProject.DeleteTopic(freshPath);
                Invoke("ReloadProjectLists");

                var brokenToc = Toc();
                brokenToc.Root!.Add(new XElement("TocEntry", new XAttribute("Title", "Broken destination"), new XAttribute("Link", "/Content/basics/missing.htm")));
                brokenToc.Save(Path.Combine(fixture, "Project/TOCs/STATSDIRECT.fltoc"));
                Invoke("ReloadProjectLists");
                Select("Section A");
                bool contentsRejected = false;
                try { await InvokeTask("CreateTopicInContentsAsync", "Native page kept after contents error", "basics"); }
                catch (InvalidOperationException ex) { contentsRejected = ex.Message.Contains("available in All pages"); }
                string keptPath = "basics/native_page_kept_after_contents_error.htm";
                Check(contentsRejected && File.Exists(Path.Combine(fixture, "Content", keptPath)), "A contents failure reports that the newly created page was retained");
                Check(Field<ListBox>("pages").Items.Cast<TopicInfo>().Any(t => t.Path == keptPath), "A page retained after a contents failure remains discoverable in All pages");
                brokenToc = Toc();
                brokenToc.Descendants("TocEntry").Single(e => Title(e) == "Broken destination").Remove();
                brokenToc.Save(Path.Combine(fixture, "Project/TOCs/STATSDIRECT.fltoc"));
                Invoke("ReloadProjectLists");
                form.CurrentProject.DeleteTopic(keptPath);
                Invoke("ReloadProjectLists");

                await form.OpenTopicAsync("basics/alpha.htm");
                string source = form.CurrentProject.ReadTopic("basics/alpha.htm");
                string dirty = source.Replace("BASELINE_CONTEXT", "DIRTY_CONTEXT");
                await form.EditorWebView.ExecuteScriptAsync("window.HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(dirty) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
                await Wait(async () => form.IsDirty && await JsBool("window.HelpEditor.getState().dirty"));
                Check(form.IsDirty, "Native host receives the source editor's unsaved state");
                string invalidProject = Path.Combine(testRoot, "InvalidProject");
                CreateFixture(invalidProject);
                File.WriteAllText(Path.Combine(invalidProject, "Content/basics/beta.htm"), "<html><body>broken");
                bool invalidProjectBlocked = false;
                try { await form.OpenProjectAsync(invalidProject); } catch { invalidProjectBlocked = true; }
                Check(invalidProjectBlocked && form.CurrentProject.RootPath == fixture && form.CurrentTopicPath == "basics/alpha.htm", "A malformed candidate topic does not replace the active project or page");
                Check(await JsString("window.HelpEditor.getSource()") == dirty && form.IsDirty, "Opening a malformed copy preserves the unsaved editor buffer and dirty flag");
                CreateFixture(invalidProject);
                File.WriteAllText(Path.Combine(invalidProject, "Project/TOCs/STATSDIRECT.fltoc"), "<CatapultToc><TocEntry");
                bool invalidTocBlocked = false;
                try { await form.OpenProjectAsync(invalidProject); } catch { invalidTocBlocked = true; }
                Check(invalidTocBlocked && form.CurrentProject.RootPath == fixture && await JsString("window.HelpEditor.getSource()") == dirty && form.IsDirty, "A malformed candidate contents tree also preserves the current dirty page");
                bool missingPageBlocked = false;
                try { await form.OpenTopicAsync("basics/missing.htm"); } catch { missingPageBlocked = true; }
                Check(missingPageBlocked && form.CurrentTopicPath == "basics/alpha.htm" && SelectedModel().Title == "Alpha", "A missing destination restores the selection to the current page");
                Check(await JsString("window.HelpEditor.getSource()") == dirty && form.IsDirty, "A failed page selection leaves unsaved source intact");
                await form.SwitchWorkspaceAsync("public");
                Check(await JsString("window.HelpEditor.getSource()") == dirty && form.IsDirty, "Switching to Public preserves unsaved source exactly");
                string publicPreview = ReadOutputPreviewTopic();
                Check(publicPreview.Contains("PUBLIC_ONLY") && !publicPreview.Contains("DESKTOP_ONLY"), "Native Public selection filters the generated preview to public content");
                await form.SwitchWorkspaceAsync("desktop");
                string desktopPreview = ReadOutputPreviewTopic();
                Check(form.IsAppPreviewVisible && desktopPreview.Contains("DESKTOP_ONLY") && !desktopPreview.Contains("PUBLIC_ONLY"), "Native Desktop selection refreshes the app preview without reopening the page");
                Check(await JsString("window.HelpEditor.getSource()") == dirty && form.IsDirty, "Switching targets in Preview retains dirty source and the unsaved indicator");
                await form.SaveCurrentTopicAsync();
                Check(form.CurrentProject.ReadTopic("basics/alpha.htm") == dirty && !form.IsDirty, "Saving after target switches persists only the user's source edits");
                await form.SwitchWorkspaceAsync("public");
                await form.OpenTopicAsync("basics/beta.htm");
                Check(await JsBool("window.HelpEditor.getState().buildTarget === 'Public'"), "Opening another page forwards the currently selected target");
                FindTree("Gamma").EnsureVisible();
                Field<TreeView>("contents").SelectedNode = FindTree("Gamma");
                // A linked selection also refreshes the complete output views.
                // Wait until the native guard releases the tree before issuing
                // another user action, rather than manipulating disabled UI.
                await Wait(async () => { await Task.CompletedTask; return form.CurrentTopicPath == "basics/gamma.htm" && !Field<bool>("busy"); });
                Check(form.CurrentTopicPath == "basics/gamma.htm", "Selecting a linked native contents entry opens its page");
                Field<TreeView>("contents").SelectedNode = FindTree("Section A");
                await Task.Delay(50);
                Check(form.CurrentTopicPath == "basics/gamma.htm", "Selecting an unlinked section leaves the current page intact");

                await form.OpenTopicAsync("basics/alpha.htm");
                await Wait(async () => { await Task.CompletedTask; return !Field<bool>("busy"); });
                await form.SwitchWorkspaceAsync("edit");
                string appliedBeforeDraft = form.CurrentProject.ReadTopic("basics/alpha.htm");
                await form.EditorWebView.ExecuteScriptAsync("window.HelpEditor.setOutputView('formatted');document.querySelector('.equation-widget').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));document.getElementById('equation-field').value=" + JsonSerializer.Serialize(@"\frac{a+b}{n}") + ";");
                await Wait(() => JsBool("window.HelpEditor.getState().equationDialogOpen && window.HelpEditor.getState().equationDraftDirty"));
                await Wait(async () => { await Task.CompletedTask; return !Field<TreeView>("contents").Enabled; });
                Check(!form.MainMenuStrip!.Enabled && !Field<ToolStrip>("contentsBar").Enabled && !Field<TreeView>("contents").Enabled && form.EditorWebView.Enabled, $"An open equation acts modally across the native shell while MathLive remains usable (menu={form.MainMenuStrip.Enabled}, contentsToolbar={Field<ToolStrip>("contentsBar").Enabled}, contents={Field<TreeView>("contents").Enabled}, editor={form.EditorWebView.Enabled}, busy={Field<bool>("busy")})");
                bool draftSaveBlocked = false;
                try { await form.SaveCurrentTopicAsync(); } catch (InvalidOperationException) { draftSaveBlocked = true; }
                Check(draftSaveBlocked && form.CurrentProject.ReadTopic("basics/alpha.htm") == appliedBeforeDraft, "Native Save cannot silently omit an unapplied equation draft");
                bool draftPageBlocked = false;
                try { await form.OpenTopicAsync("basics/beta.htm"); } catch (InvalidOperationException) { draftPageBlocked = true; }
                Check(draftPageBlocked && form.CurrentTopicPath == "basics/alpha.htm" && await JsBool("window.HelpEditor.getState().equationDraftDirty"), "A native page change is blocked without losing the equation draft");
                bool draftProjectBlocked = false;
                try { await form.OpenProjectAsync(fixture); } catch (InvalidOperationException) { draftProjectBlocked = true; }
                Check(draftProjectBlocked && await JsBool("window.HelpEditor.getState().equationDialogOpen"), "Reopening a project also requires the equation to be applied or cancelled");
                bool draftBuildBlocked = false;
                try { await form.BuildHelpAsync(); } catch (InvalidOperationException) { draftBuildBlocked = true; }
                Check(draftBuildBlocked && form.LastBuildResult == null, "Build does not proceed with an unapplied equation draft");
                form.Close();
                await Task.Delay(100);
                Check(!form.IsDisposed && await JsBool("window.HelpEditor.getState().equationDraftDirty"), "Closing the native window cannot discard an open equation draft");
                await form.EditorWebView.ExecuteScriptAsync("document.dispatchEvent(new KeyboardEvent('keydown',{key:'s',ctrlKey:true,bubbles:true,cancelable:true}));");
                await Task.Delay(100);
                Check(form.CurrentProject.ReadTopic("basics/alpha.htm") == appliedBeforeDraft && await JsBool("!document.getElementById('equation-error').hidden && document.getElementById('equation-dialog').open"), "Ctrl+S inside MathLive explains Apply/Cancel and leaves the saved page untouched");
                await form.EditorWebView.ExecuteScriptAsync("document.getElementById('equation-apply').click()");
                await Wait(() => JsBool("!window.HelpEditor.getState().equationDialogOpen && window.HelpEditor.getState().dirty"));
                await Wait(async () => { await Task.CompletedTask; return Field<TreeView>("contents").Enabled; });
                await form.SaveCurrentTopicAsync();
                string appliedAfterDraft = form.CurrentProject.ReadTopic("basics/alpha.htm");
                Check(appliedAfterDraft != appliedBeforeDraft && appliedAfterDraft.Contains("frac{a+b}{n}"), "Applying the draft re-enables the shell and lets Save persist the edited equation");
                await form.EditorWebView.ExecuteScriptAsync("document.querySelector('.equation-widget').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));document.getElementById('equation-field').value='x^{99}';document.getElementById('equation-cancel').click();");
                await Wait(() => JsBool("!window.HelpEditor.getState().equationDialogOpen"));
                await form.SaveCurrentTopicAsync();
                Check(form.CurrentProject.ReadTopic("basics/alpha.htm") == appliedAfterDraft && !form.IsDirty, "Cancel discards only the new equation draft and preserves the applied page");

                string immediateEdit = appliedAfterDraft.Replace("DIRTY_CONTEXT", "IMMEDIATE_CONTEXT");
                await form.EditorWebView.ExecuteScriptAsync("window.HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(immediateEdit) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
                Invoke("SetDirty", false); // Simulate the native message still being in flight.
                await InvokeTask("RefreshEditorStateAsync");
                Check(form.IsDirty, "The authoritative renderer snapshot recovers unsaved state even if its native message is delayed");
                await form.SaveCurrentTopicAsync();
                Invoke("SetBusy", true);
                form.Close();
                await Task.Delay(50);
                Check(!form.IsDisposed, "The native window cannot close in the middle of a busy build/save/Git action");
                Invoke("SetBusy", false);

                string conflictPath = Path.Combine(fixture, "Content/basics/alpha.htm");
                string baseline = form.CurrentProject.ReadTopic("basics/alpha.htm");
                string retainedLocal = baseline.Replace("</body>", "<p>Retained local edit</p></body>");
                await form.EditorWebView.ExecuteScriptAsync("window.HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(retainedLocal) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
                string external = baseline.Replace("</body>", "<p>External editor change</p></body>");
                File.WriteAllText(conflictPath, external);
                bool pageConflictBlocked = false;
                try { await form.SaveCurrentTopicAsync(); } catch (InvalidOperationException ex) { pageConflictBlocked = ex.Message.Contains("changed on disk"); }
                Check(pageConflictBlocked && File.ReadAllText(conflictPath) == external, "Saving an older page cannot overwrite a newer external file");
                Check(await JsString("window.HelpEditor.getSource()") == retainedLocal && form.IsDirty, "A page-save conflict preserves the user's dirty editing buffer");
                await form.OpenTopicAsync("basics/alpha.htm");
                Check(await JsString("window.HelpEditor.getSource()") == external && !form.IsDirty, "Explicitly reopening loads the newer disk file as the new baseline");
                string merged = external.Replace("</body>", "<p>Merged local edit</p></body>");
                await form.EditorWebView.ExecuteScriptAsync("window.HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(merged) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
                await form.SaveCurrentTopicAsync();
                Check(File.ReadAllText(conflictPath) == merged && merged.Contains("External editor change"), "Saving reviewed edits after reopening preserves the external change");

                Select("Alpha");
                SelectedModel().Title = "Stale attempted rename";
                var externalToc = Toc();
                externalToc.Root!.Add(new XElement("TocEntry", new XAttribute("Title", "External contents entry"), new XAttribute("Link", "/Content/basics/delta.htm")));
                externalToc.Save(Path.Combine(fixture, "Project/TOCs/STATSDIRECT.fltoc"));
                string externalTocSource = ReadToc();
                bool contentsConflictBlocked = false;
                try { SaveToc(null); } catch (InvalidOperationException ex) { contentsConflictBlocked = ex.Message.Contains("changed on disk"); }
                Check(contentsConflictBlocked && ReadToc() == externalTocSource, "A stale contents edit cannot overwrite a newer external tree");
                Check(FindTree("External contents entry") != null && FindTree("Alpha") != null, "A contents conflict reloads the external entries and rejects the stale command");
                var externalModel = Field<List<TocNode>>("toc").Single(n => n.Title == "External contents entry");
                Field<List<TocNode>>("toc").Remove(externalModel);
                SaveToc(null);
                await CheckRecoveryAsync();
                Check(form.LastError == null, "No native host errors during contents and target-switch checks" + (form.LastError == null ? "" : ": " + form.LastError));
            }
            catch (Exception ex) { Check(false, "Unexpected contents check failure: " + ex); }
            finally
            {
                File.WriteAllText(Path.Combine(evidence, "contents-windows-checks.json"), JsonSerializer.Serialize(new { passed, failed, results = Results }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{passed} contents/target native checks passed; {failed} failed");
                Invoke("SetDirty", false);
                Invoke("SetBusy", false);
                typeof(MainForm).GetField("closingApproved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
                form.Close();
            }
        };
        Application.Run(form);
        return failed == 0 ? 0 : 1;
    }

    private static async Task CheckRecoveryAsync()
    {
        var fileMenu = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(m => (m.Text ?? "").Replace("&", "") == "File");
        Check(fileMenu.DropDownItems.OfType<ToolStripMenuItem>().Any(m => m.Text == "Deleted pages…"), "File menu makes deleted-page recovery discoverable");
        string title = "Recoverable λ " + Guid.NewGuid().ToString("N")[..8];
        string path = form.CurrentProject.CreateTopic(title, "basics");
        string source = form.CurrentProject.ReadTopic(path).Replace("Write the new help page here.", "Recovery exact bytes λ &amp; β.")
            .Replace("</body>", "<MadCap:equation>$\\frac{x+1}{n}$</MadCap:equation><pre xml:space=\"preserve\">x &lt;- c(1, 2)\n  mean(x)</pre></body>");
        form.CurrentProject.SaveTopic(path, source);
        var tree = form.CurrentProject.LoadContents();
        tree[1].Children.Add(new TocNode { Title = title, Link = path });
        form.CurrentProject.SaveContents(tree);
        Invoke("ReloadProjectLists");
        await form.OpenTopicAsync(path);
        await form.SwitchWorkspaceAsync("public");
        await form.BuildHelpAsync();
        byte[] bytes = File.ReadAllBytes(Path.Combine(fixture, "Content", path));
        string originalToc = ReadToc();
        form.CurrentProject.DeleteTopic(path);
        Invoke("ReloadProjectLists");
        await form.OpenTopicAsync("basics/alpha.htm");
        var deleted = form.CurrentProject.ListDeletedTopics().Single(r => r.OriginalPath == path && r.CanRestore);
        Check(!File.Exists(Path.Combine(fixture, "Content", path)) && !Field<ListBox>("pages").Items.Cast<TopicInfo>().Any(t => t.Path == path), "A deleted page leaves the native page list and remains recoverable");
        Check(HelpProject.Open(fixture).ListDeletedTopics().Any(r => r.Id == deleted.Id && r.OriginalPath == path), "Deleted-page records remain available after reopening the project");

        string bufferBeforeChooser = await JsString("window.HelpEditor.getSource()");
        using (var dialog = new DeletedPagesDialog(form.CurrentProject.ListDeletedTopics()) { StartPosition = FormStartPosition.Manual, Location = new Point(-15000, 0) })
        {
            dialog.Show(form);
            await Task.Delay(50);
            var list = DialogField<ListView>(dialog, "deletedPages");
            list.Items.Cast<ListViewItem>().Single(i => ((DeletedTopicInfo)i.Tag!).Id == deleted.Id).Selected = true;
            var restore = DialogField<Button>(dialog, "restore");
            Check(list.Columns.Count == 3 && list.SelectedItems[0].SubItems[1].Text == path && list.SelectedItems[0].SubItems[2].Text.Length > 0 && restore.Enabled,
                "Recovery chooser identifies pages by title, original path and local deletion date");
            Check(list.Columns.Cast<ColumnHeader>().Sum(c => c.Width) <= list.ClientSize.Width && restore.Height >= dialog.Font.Height,
                "Recovery columns and action labels fit the actual DPI-scaled dialog");
            Check(list.Columns[2].Width >= TextRenderer.MeasureText(list.SelectedItems[0].SubItems[2].Text, list.Font).Width &&
                dialog.Width >= (int)Math.Ceiling(760 * dialog.DeviceDpi / 96d), "The deletion date fits in full at this PC's actual display DPI");
            using (var capture = new Bitmap(dialog.Width, dialog.Height))
            {
                dialog.DrawToBitmap(capture, new Rectangle(Point.Empty, dialog.Size));
                capture.Save(Path.Combine(evidence, "recovery-chooser.png"));
            }
            ((Button)dialog.CancelButton!).PerformClick();
        }
        Check(!File.Exists(Path.Combine(fixture, "Content", path)) && await JsString("window.HelpEditor.getSource()") == bufferBeforeChooser,
            "Closing the recovery chooser does not restore a page or change the current buffer");
        Check(await form.RestoreDeletedPageAsync(deleted.Id) && form.CurrentTopicPath == path && !form.IsDirty && File.ReadAllBytes(Path.Combine(fixture, "Content", path)).SequenceEqual(bytes) && ReadToc() == originalToc,
            "Restoring through the native host opens exact page bytes and restores its original Contents tree");
        Check(Field<ListBox>("pages").Items.Cast<TopicInfo>().Any(t => t.Path == path) && Nodes(Field<TreeView>("contents").Nodes).Any(n => n.Text == title),
            "Restored pages immediately reappear in All pages and Contents");
        Check(Field<Label>("workspaceCaption").Text.Contains("out of date") && !form.CurrentProject.ListDeletedTopics().Any(r => r.Id == deleted.Id),
            "Restoration marks generated help stale and removes the restored record from the recovery list");

        form.CurrentProject.DeleteTopic(path);
        var second = form.CurrentProject.ListDeletedTopics().Single(r => r.OriginalPath == path && r.CanRestore);
        tree = form.CurrentProject.LoadContents();
        tree[0].Title = "Newer Contents title λ";
        tree.Add(new TocNode { Title = "Later Contents entry", Link = "basics/delta.htm" });
        form.CurrentProject.SaveContents(tree);
        Invoke("ReloadProjectLists");
        await form.OpenTopicAsync("basics/alpha.htm");
        await form.SwitchWorkspaceAsync("edit");
        string alphaSource = form.CurrentProject.ReadTopic("basics/alpha.htm");

        Invoke("SetBusy", true);
        bool busyBlocked = false;
        try { await form.RestoreDeletedPageAsync(second.Id); } catch (InvalidOperationException) { busyBlocked = true; }
        finally { Invoke("SetBusy", false); }
        Check(busyBlocked && !File.Exists(Path.Combine(fixture, "Content", path)), "Recovery cannot overlap a busy build, save or Git action");
        await form.EditorWebView.ExecuteScriptAsync("HelpEditor.setTab('visual');HelpEditor.openEquation();document.getElementById('equation-field').value='x^{123}';");
        Check(!await form.RestoreDeletedPageAsync(second.Id) && await JsBool("HelpEditor.getState().equationDialogOpen && HelpEditor.getState().equationDraftDirty") && !File.Exists(Path.Combine(fixture, "Content", path)),
            "Recovery preserves an unapplied MathLive draft and does not restore behind it");
        await form.EditorWebView.ExecuteScriptAsync("document.getElementById('equation-cancel').click();HelpEditor.openLink();document.getElementById('link-text').value='Pending link';");
        Check(!await form.RestoreDeletedPageAsync(second.Id) && await JsBool("HelpEditor.getState().dialogKind==='link' && document.getElementById('link-text').value==='Pending link'") && !File.Exists(Path.Combine(fixture, "Content", path)),
            "Recovery preserves an unapplied hyperlink draft");
        await form.EditorWebView.ExecuteScriptAsync("document.getElementById('link-cancel').click()");
        string draft = alphaSource.Replace("</body>", "<p>Pending recovery draft λ</p></body>");
        await form.EditorWebView.ExecuteScriptAsync("HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(draft) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
        await Wait(async () => form.IsDirty && await JsBool("HelpEditor.getState().dirty"));
        using (var cancel = new OwnedPromptResponse(form, 2))
        {
            Check(!await form.RestoreDeletedPageAsync(second.Id) && cancel.Responses == 1, "Cancel at the ordinary save prompt cancels page recovery");
        }
        Check(form.CurrentTopicPath == "basics/alpha.htm" && form.IsDirty && await JsString("HelpEditor.getSource()") == draft && !File.Exists(Path.Combine(fixture, "Content", path)),
            "Cancelled recovery preserves the dirty page, saved file and archived page");
        using (var discard = new OwnedPromptResponse(form, 7))
        {
            Check(await form.RestoreDeletedPageAsync(second.Id) && discard.Responses == 1, "Approved discard lets the requested recovery proceed without a duplicate prompt");
        }
        Check(ReadToc().Contains("Newer Contents title λ") && ReadToc().Contains("Later Contents entry") && ReadToc().Contains(title) && form.CurrentProject.ReadTopic("basics/alpha.htm") == alphaSource,
            "Recovery retains later Contents edits and the original saved page after a discarded draft");

        form.CurrentProject.DeleteTopic(path);
        var conflicting = form.CurrentProject.ListDeletedTopics().Single(r => r.OriginalPath == path && r.CanRestore);
        string replacement = source.Replace(title, "Replacement page");
        File.WriteAllText(Path.Combine(fixture, "Content", path), replacement);
        bool overwriteBlocked = false;
        try { await form.RestoreDeletedPageAsync(conflicting.Id); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { overwriteBlocked = true; }
        Check(overwriteBlocked && File.ReadAllText(Path.Combine(fixture, "Content", path)) == replacement && form.CurrentProject.ListDeletedTopics().Any(r => r.Id == conflicting.Id),
            "A restore collision preserves the newer file and the recoverable archive");
        using (var dialog = new DeletedPagesDialog(form.CurrentProject.ListDeletedTopics()) { StartPosition = FormStartPosition.Manual, Location = new Point(-15000, 0) })
        {
            dialog.Show(form); await Task.Delay(50);
            var list = DialogField<ListView>(dialog, "deletedPages");
            list.Items.Cast<ListViewItem>().Single(i => ((DeletedTopicInfo)i.Tag!).Id == conflicting.Id).Selected = true;
            Check(!DialogField<Button>(dialog, "restore").Enabled && !string.IsNullOrWhiteSpace(DialogField<Label>(dialog, "detail").Text),
                "Unavailable recovery records remain visible with an explanation and disabled Restore");
            dialog.Close();
        }
        string savedRecoveryPath = form.CurrentProject.CreateTopic(title + " saved", "basics");
        form.CurrentProject.DeleteTopic(savedRecoveryPath);
        var savedRecovery = form.CurrentProject.ListDeletedTopics().Single(r => r.OriginalPath == savedRecoveryPath && r.CanRestore);
        await form.OpenTopicAsync("basics/alpha.htm");
        string savedDraft = alphaSource.Replace("</body>", "<p>Save before recovery λ</p></body>");
        await form.EditorWebView.ExecuteScriptAsync("HelpEditor.setTab('source');document.getElementById('source-editor').value=" + JsonSerializer.Serialize(savedDraft) + ";document.getElementById('source-editor').dispatchEvent(new Event('input',{bubbles:true}));");
        await Wait(async () => form.IsDirty && await JsBool("HelpEditor.getState().dirty"));
        using (var save = new OwnedPromptResponse(form, 6))
        {
            Check(await form.RestoreDeletedPageAsync(savedRecovery.Id) && save.Responses == 1, "Save at the ordinary prompt preserves edits before restoring the chosen page");
        }
        Check(form.CurrentProject.ReadTopic("basics/alpha.htm") == savedDraft && form.CurrentTopicPath == savedRecoveryPath && !form.IsDirty,
            "Successful recovery keeps the saved draft and opens the requested recovered page");
    }

    private static T DialogField<T>(DeletedPagesDialog dialog, string name) => (T)typeof(DeletedPagesDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
    private static T Field<T>(string name) => (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    private static object? Invoke(string name, params object?[] args)
    {
        try { return typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
    }
    private static async Task InvokeTask(string name, params object?[] args) => await (Task)Invoke(name, args)!;
    private static void SaveToc(string? id) => Invoke("SaveTocAndRefresh", new object?[] { id });
    private static IEnumerable<TreeNode> Nodes(TreeNodeCollection nodes) => nodes.Cast<TreeNode>().SelectMany(n => new[] { n }.Concat(Nodes(n.Nodes)));
    private static TreeNode FindTree(string title) => Nodes(Field<TreeView>("contents").Nodes).Single(n => n.Text == title);
    private static TocNode SelectedModel() => (TocNode)Field<TreeView>("contents").SelectedNode!.Tag!;
    private static void Select(string title)
    {
        typeof(MainForm).GetField("changingSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
        try { Field<TreeView>("contents").SelectedNode = FindTree(title); }
        finally { typeof(MainForm).GetField("changingSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, false); }
    }
    private static XDocument Toc() => XDocument.Load(Path.Combine(fixture, "Project/TOCs/STATSDIRECT.fltoc"));
    private static XElement Entry(string title) => Toc().Descendants("TocEntry").Single(e => Title(e) == title);
    private static XElement Section(string title) => Toc().Root!.Elements("TocEntry").Single(e => Title(e) == title);
    private static string Titles(XElement parent) => string.Join("|", parent.Elements("TocEntry").Select(Title));
    private static string Title(XElement e) => (string)e.Attribute("Title")!;
    private static string ReadToc() => File.ReadAllText(Path.Combine(fixture, "Project/TOCs/STATSDIRECT.fltoc"));
    private static string ReadOutputPreviewTopic() => File.ReadAllText(Path.Combine(
        form.LastOutputPreviewResult?.OutputDirectory ?? throw new InvalidOperationException("The selected view did not generate an output preview."),
        form.CurrentTopicPath!.ToLowerInvariant()));
    private static async Task<bool> JsBool(string script) => JsonSerializer.Deserialize<bool>(await form.EditorWebView.ExecuteScriptAsync(script));
    private static async Task<string> JsString(string script) => JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync(script))!;
    private static async Task Wait(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!await condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("The native editor did not reach the expected state."); await Task.Delay(30); }
    }
    private static void Check(bool value, string name)
    {
        if (value) passed++; else failed++;
        Results.Add(new { name, passed = value });
        Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}");
    }
    private static void CreateFixture(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "Content/basics"));
        Directory.CreateDirectory(Path.Combine(root, "Content/resources/stylesheets"));
        Directory.CreateDirectory(Path.Combine(root, "Project/TOCs"));
        File.WriteAllText(Path.Combine(root, HelpProject.MarkerFile), "{\"purpose\":\"Owned native contents test fixture\"}");
        File.WriteAllText(Path.Combine(root, "Content/resources/stylesheets/statsdirect.css"), "body{font:10pt Arial}");
        foreach (string name in new[] { "alpha", "beta", "gamma", "delta" })
            File.WriteAllText(Path.Combine(root, $"Content/basics/{name}.htm"), $"<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>{name}</title></head><body><h1>{name}</h1><p>BASELINE_CONTEXT</p><p id=\"example-anchor\" MadCap:conditions=\"Primary.SWHELP\">DESKTOP_ONLY</p><p MadCap:conditions=\"Primary.WEBHELP\">PUBLIC_ONLY</p><MadCap:equation>$x^2$</MadCap:equation></body></html>");
        File.WriteAllText(Path.Combine(root, "Project/TOCs/STATSDIRECT.fltoc"), "<CatapultToc Version=\"1\"><TocEntry Title=\"Section A\" BreakType=\"chapter\"><TocEntry Title=\"Alpha\" Link=\"/Content/basics/alpha.htm\"/><TocEntry Title=\"Beta\" Link=\"/Content/basics/beta.htm\" CustomMetadata=\"preserve-beta\"/><TocEntry Title=\"Gamma\" Link=\"/Content/basics/gamma.htm\"/></TocEntry><TocEntry Title=\"Section B\"><TocEntry Title=\"Delta\" Link=\"/Content/basics/delta.htm\"/></TocEntry></CatapultToc>");
    }

    /// <summary>Answers only this fixture's ordinary Unsaved page prompt.</summary>
    private sealed class OwnedPromptResponse : IDisposable
    {
        private readonly IntPtr owner;
        private readonly int response;
        private readonly Hook callback;
        private readonly IntPtr hook;
        public int Responses { get; private set; }
        public OwnedPromptResponse(Form ownerForm, int response)
        {
            owner = ownerForm.Handle; this.response = response; callback = OnHook;
            hook = SetWindowsHookEx(5, callback, IntPtr.Zero, GetCurrentThreadId());
            if (hook == IntPtr.Zero) throw new InvalidOperationException("Could not attach the owned save-prompt handler.");
        }
        private IntPtr OnHook(int code, IntPtr window, IntPtr data)
        {
            if (code == 5 && GetWindow(window, 4) == owner)
            {
                var title = new StringBuilder(128); var kind = new StringBuilder(64);
                GetWindowText(window, title, title.Capacity); GetClassName(window, kind, kind.Capacity);
                if (title.ToString() == "Unsaved page" && kind.ToString() == "#32770") { Responses++; PostMessage(window, 0x0111, (IntPtr)response, IntPtr.Zero); }
            }
            return CallNextHookEx(hook, code, window, data);
        }
        public void Dispose() { UnhookWindowsHookEx(hook); GC.KeepAlive(callback); }
        private delegate IntPtr Hook(int code, IntPtr window, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, Hook procedure, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr window, IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    }
}
