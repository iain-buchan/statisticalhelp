using System.Text.Json;
using System.Security.Cryptography;
using System.Reflection;
using Microsoft.Web.WebView2.Core;
using StatsDirect.HelpEditor.App.AppHelp;
using StatsDirect.HelpEditor.Core;

internal static class Program
{
    private static readonly List<object> results = [];
    private static int passed, failed;
    private static HelpService service = null!;
    private static string root = "", fixture = "";
    private static void Check(bool value, string name)
    {
        if (value) passed++; else failed++;
        results.Add(new { name, passed = value });
        Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}");
    }
    private static async Task<JsonElement> Js(string script) => JsonDocument.Parse(await service.View.Browser.CoreWebView2.ExecuteScriptAsync(script)).RootElement.Clone();
    private static async Task Until(string condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if ((await Js(condition)).ValueKind == JsonValueKind.True) return;
            await Task.Delay(50);
        }
        throw new TimeoutException(condition);
    }
    private static IEnumerable<Control> Descendants(Control control) => new[] { control }.Concat(control.Controls.Cast<Control>().SelectMany(Descendants));
    private static async Task NativeUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline) { if (condition()) return; await Task.Delay(50); }
        throw new TimeoutException("The native Index controls did not reach the expected state.");
    }
    private static void NativeKey(Control target, Keys key)
    {
        var message = Message.Create(target.Handle, 0x0100, (IntPtr)(int)key, IntPtr.Zero);
        if (!target.PreProcessMessage(ref message)) typeof(Control).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, [message]);
    }
    private static async Task CheckKeywordIndex()
    {
        var index = Descendants(service.View).OfType<ToolStrip>().SelectMany(strip => strip.Items.OfType<ToolStripDropDownButton>()).Single(button => button.Text == "Index");
        var contents = index.Owner!.Items.OfType<ToolStripButton>().Single(button => button.Text == "Contents");
        await NativeUntil(() => index.Enabled);
        await Js("window.indexPreviewSnapshot={url:location.href,history:history.length};window.indexPreviewToken='retained'");
        contents.PerformClick();
        await Until("innerWidth<1280 && document.body.classList.contains('navigation-open') && !!document.activeElement.closest('#contents')");
        index.ShowDropDown();
        var popup = index.DropDown;
        var panel = popup.Items.OfType<ToolStripControlHost>().Single().Control;
        var filter = panel.Controls.OfType<TextBox>().Single();
        var list = panel.Controls.OfType<ListBox>().Single();
        await NativeUntil(() => popup.Visible && filter.Focused);
        Check((await Js("!document.body.classList.contains('navigation-open')")).GetBoolean(),
            "App preview opening Index closes the compact Contents drawer");
        Check(list.Items.Count == 4, "App preview Index lists authored keywords and keeps both destinations of a shared keyword");
        filter.Text = "Shared keyword";
        Check(list.Items.Count == 2 && list.Items.Cast<object>().Any(item => item.ToString()!.Contains("One")) && list.Items.Cast<object>().Any(item => item.ToString()!.Contains("Two")),
            "App preview distinguishes multiple pages for the same index keyword");
        contents.PerformClick();
        await Until("document.body.classList.contains('navigation-open') && !!document.activeElement.closest('#contents')");
        Check(!popup.Visible, "App preview opening Contents closes the Index popup");
        index.ShowDropDown(); await NativeUntil(() => popup.Visible && filter.Focused);
        NativeKey(filter, Keys.Escape);
        await NativeUntil(() => !popup.Visible && index.Selected);
        Check((await Js("location.href===window.indexPreviewSnapshot.url && history.length===window.indexPreviewSnapshot.history && window.indexPreviewToken==='retained'")).GetBoolean(),
            "Escape from the App preview Index returns to its toolbar without changing the reading page or history");
        index.ShowDropDown();
        contents.PerformClick();
        await Until("document.body.classList.contains('navigation-open') && !!document.activeElement.closest('#contents')");
        Check(!popup.Visible, "App preview Contents cancels an Index opening still awaiting the reader");
        index.ShowDropDown(); await NativeUntil(() => popup.Visible && filter.Focused);
        filter.Text = "Second authored keyword";
        Check(list.Items.Count == 1, "The App preview Index filter selects a keyword independently of page titles");
        NativeKey(filter, Keys.Enter);
        await Until("location.pathname==='/two.htm' && document.readyState==='complete'");
        Check(!popup.Visible, "Enter opens the selected index topic in the existing App browser and closes its popup");
        service.View.Browser.CoreWebView2.GoBack();
        await Until("location.pathname==='/one.htm' && location.hash==='#Ref-Case' && document.readyState==='complete'");
    }
    [STAThread]
    private static int Main(string[] args)
    {
        root = Path.GetFullPath(args[0]);
        fixture = Path.Combine(Path.GetTempPath(), "sdhe-apphelp-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(fixture);
        ApplicationConfiguration.Initialize();
        using var owner = new Form { Text = "Isolated app-help host fixture", StartPosition = FormStartPosition.Manual, Location = new Point(-16000, -16000), Size = new Size(2400, 1400), ShowInTaskbar = false };
        owner.Shown += async (_, _) =>
        {
            try { await Run(owner); }
            catch (Exception ex) { failed++; results.Add(new { name = "Unexpected app-help host error", passed = false, error = ex.ToString() }); Console.Error.WriteLine(ex); }
            finally { service?.Dispose(); owner.Close(); }
        };
        Application.Run(owner);
        File.WriteAllText(Path.Combine(root, "evidence", "app-help-preview-checks.json"), JsonSerializer.Serialize(new { passed, failed, checkedAtUtc = DateTimeOffset.UtcNow, fixture, results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{passed} app-help preview checks passed; {failed} failed");
        return failed == 0 ? 0 : 1;
    }
    private static async Task Run(Form owner)
    {
        string source = Path.Combine(fixture, "source");
        void Write(string relative, string text) { string path = Path.Combine(source, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        Write(HelpProject.MarkerFile, "{\"prototypeWorkingCopy\":true}");
        Write("StatsDirect.flprj", "<CatapultProject />");
        Write("Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title=\"Contents\" Link=\"/Content/contents.htm\"/><TocEntry Title=\"One\" Link=\"/Content/one.htm\"/><TocEntry Title=\"Two\" Link=\"/Content/two.htm\"/></CatapultToc>");
        Write("Content/contents.htm", "<html><head><title>Fixture contents</title></head><body><h1>Fixture contents</h1><a href=\"one.htm\">One</a></body></html>");
        Write("Content/one.htm", "<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>One</title></head><body><h1>One<MadCap:keyword term=\"First authored keyword;Shared keyword\" /></h1><p id=\"Ref-Case\">Searchsentinel</p><p MadCap:conditions=\"Primary.SWHELP\">Desktoponlysentinel</p><p MadCap:conditions=\"Primary.WEBHELP\">Publiconlysentinel</p><MadCap:dropDown class=\"rcode\"><MadCap:dropDownHead><MadCap:dropDownHotspot>R code</MadCap:dropDownHotspot></MadCap:dropDownHead><MadCap:dropDownBody><pre>x &lt;- 1\nmean(x)</pre></MadCap:dropDownBody></MadCap:dropDown><p><MadCap:equation>\\(x^2\\)</MadCap:equation></p><a href=\"two.htm#Target\">Two</a>" + string.Concat(Enumerable.Repeat("<p>Long content retains the reading position.</p>", 130)) + "</body></html>");
        Write("Content/two.htm", "<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>Two</title></head><body><h1>Two<MadCap:keyword term=\"Second authored keyword;Shared keyword\" /></h1><p id=\"Target\">Second topic</p></body></html>");
        var project = HelpProject.Open(source);
        string output = Path.Combine(fixture, "Desktop");
        var built = HelpBuilder.Build(project, output, Path.Combine(root, "src", "StatsDirectHelpEditor.App", "Web"), HelpBuildTarget.Desktop);
        Check(built.TopicCount == 3 && built.Warnings.Count == 0, "App host consumes a real independent Desktop help build");
        var workspace = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 240 };
        var ribbon = new Panel { Dock = DockStyle.Top, Height = 60 };
        owner.Controls.Add(workspace); owner.Controls.Add(ribbon); owner.PerformLayout();
        int fullWidth = workspace.Width;
        string profile = Path.Combine(fixture, "browser-profile");
        service = new HelpService(profile); int closed = 0; service.Closed += () => closed++;
        service.Show(owner, "one.htm#Ref-Case", output);
        await service.View.Ready.WaitAsync(TimeSpan.FromSeconds(45));
        await Until("location.pathname==='/one.htm' && location.hash==='#Ref-Case' && document.readyState==='complete'");
        Check(!owner.IsMdiContainer && service.IsDocked && service.DockHost.Panel.Dock == DockStyle.Right && workspace.Width < fullWidth, "Exact app help host docks beside a non-MDI fill workspace");
        Check(workspace.Right <= service.DockHost.Divider.Left && service.DockHost.Divider.Right <= service.DockHost.Panel.Left && service.DockHost.Panel.Top >= ribbon.Bottom && ribbon.Width == owner.ClientSize.Width, "Help pane and splitter reserve space without covering the editor or full-width ribbon");
        Check(service.View.Font.Name == "Arial" && service.View.Font.SizeInPoints == 10, "App help chrome uses the app's Arial 10 font");
        var buttons = Descendants(service.View).OfType<ToolStrip>().SelectMany(strip => strip.Items.Cast<ToolStripItem>()).Select(item => item.Text).ToArray();
        Check(new[] { "Help", "Pop out", "Close", "Back", "Forward", "Home", "Contents", "Index", "Print", "Search:", "Search" }.All(text => buttons.Contains(text)), "App heading, navigation, search, print and pop-out controls match StatsDirect");
        Check((await Js("document.body.innerText.includes('Desktoponlysentinel') && !document.body.innerText.includes('Publiconlysentinel') && !!document.querySelector('#help-topic-body') && !document.querySelector('body > header').getClientRects().length")).GetBoolean(), "App view renders Desktop topics and source conditions without the embedded website toolbar");
        await Until("!!document.querySelector('mjx-container svg')");
        Check(true, "Offline mathematics renders inside the exact app browser host");
        await CheckKeywordIndex();
        var browser = service.View.Browser.CoreWebView2;
        await Js("window.appPreviewToken='persistent';document.querySelector('details').open=true;document.getElementById('topic').scrollTop=300;");
        int originalWidth = service.DockHost.Panel.Width;
        await service.ToggleModeAsync();
        Check(!service.IsDocked && service.Window?.Owner == owner && workspace.Width == fullWidth, "Pop out restores editor width and opens an owned floating app help window");
        Check(ReferenceEquals(browser, service.View.Browser.CoreWebView2) && (await Js("window.appPreviewToken==='persistent' && document.querySelector('details').open")).GetBoolean(), "Pop out preserves browser identity and expanded R code");
        await service.ToggleModeAsync();
        await Until("document.getElementById('topic').scrollTop >= 299");
        Check(service.IsDocked && ReferenceEquals(browser, service.View.Browser.CoreWebView2), "Dock preserves the page, browser and original reading position");
        service.Show(owner, "two.htm#Target", output); await Until("location.pathname==='/two.htm' && location.hash==='#Target'");
        Check(browser.CanGoBack, "Independent app-preview topic navigation preserves browser history and anchors");
        browser.GoBack(); await Until("location.pathname==='/one.htm'");
        service.View.Search("Searchsentinel");
        await Until("location.hash.includes('searchQuery') && document.getElementById('results').textContent.includes('One')");
        Check(true, "The actual app Search command works with the independent rebuilt index");
        service.Hide();
        Check(!service.IsVisible && workspace.Width == fullWidth && closed == 1, "Closing the pane releases editor space and emits one authoring Closed event");
        service.Hide(); Check(closed == 1, "Repeated Hide does not emit duplicate close events");
        service.Show(owner, "one.htm", output); await Until("location.pathname==='/one.htm'");
        Check(service.IsDocked && service.DockHost.Panel.Width == originalWidth && ReferenceEquals(browser, service.View.Browser.CoreWebView2), "Reopening retains the pane width and browser session");
        await service.ToggleModeAsync(); service.Window!.Close();
        Check(!service.IsVisible && closed == 2, "The floating window's X emits the same authoring Closed event");
        string installedProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatsDirect", "HtmlHelp");
        Check(!browser.Environment.UserDataFolder.Equals(installedProfile, StringComparison.OrdinalIgnoreCase) && browser.Environment.UserDataFolder.Equals(profile, StringComparison.OrdinalIgnoreCase), "Editor preview test profile is isolated from installed StatsDirect and the live editor");
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "StatsDirectHelpEditor.App", "AppHelp", "provenance.json"))).RootElement;
        var snapshotFiles = manifest.GetProperty("files").EnumerateArray().ToArray();
        Check(snapshotFiles.Any(file => file.GetProperty("snapshot").GetString() == "HelpIndexMenu.cs") && snapshotFiles.All(file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"src","StatsDirectHelpEditor.App","AppHelp",file.GetProperty("snapshot").GetString()!)))).Equals(file.GetProperty("snapshotSha256").GetString(),StringComparison.OrdinalIgnoreCase)), "Every native host snapshot, including Index, matches its recorded source provenance hash");
    }
}
