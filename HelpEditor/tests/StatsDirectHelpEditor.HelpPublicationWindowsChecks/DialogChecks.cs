using System.Drawing.Imaging;
using System.Reflection;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static partial class Program
{
    private static async Task RealDialogChecks()
    {
        string empty = FakeApplication("DialogEmpty", false), flare = FakeApplication("DialogFlare", true), malformed = FakeApplication("DialogMalformed", false);
        File.WriteAllText(Path.Combine(malformed, "StatsDirectUI", "StatsDirectUI.csproj"), "<Project><Unclosed>");
        var allBefore = Digests(empty).Concat(Digests(flare)).Concat(Digests(malformed)).ToDictionary(pair => pair.Key, pair => pair.Value);
        Type type = typeof(MainForm).Assembly.GetType("StatsDirect.HelpEditor.App.HelpPublicationDialog", true)!;
        T Get<T>(Form dialog, string name) => (T)type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        Form Create(HelpPublicationOptions selected) => (Form)Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.NonPublic, null, [selected], null)!;
        HelpPublicationOptions Result(Form dialog) => (HelpPublicationOptions)type.GetProperty("Options")!.GetValue(dialog)!;
        async Task Show(Form dialog)
        {
            dialog.ShowInTaskbar = false; dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new(-16000, 0);
            dialog.Show(form);
            dialog.Location = new(-16000, 0); // Product OnLoad sizing/clamping has already run.
            await Until(() => Get<DesktopHelpDestination?>(dialog, "inspected") != null);
        }
        var emptyInspected = DesktopHelpPublisher.InspectDestination(empty);
        using (Form dialog = Create(options with { StatsDirectSource = empty }))
        {
            await Show(dialog);
            Check(Get<Button>(dialog, "publish").Enabled && !Get<CheckBox>(dialog, "adoptFlare").Visible
                && Get<Label>(dialog, "inspection").Text.Contains(emptyInspected.Sha256),
                "The real dialog inspects an empty fake application destination and displays its exact SHA256");
            Get<TextBox>(dialog, "authorEmail").Text = "not-an-email";
            Check(!Get<Button>(dialog, "publish").Enabled && Get<Label>(dialog, "error").Text.Contains("email", StringComparison.OrdinalIgnoreCase),
                "The real dialog refuses a malformed author email before publication");
            Get<TextBox>(dialog, "authorEmail").Text = options.AuthorEmail;
            Get<TextBox>(dialog, "branch").Text = "invalid branch";
            Check(!Get<Button>(dialog, "publish").Enabled && Get<Label>(dialog, "error").Text.Contains("branch", StringComparison.OrdinalIgnoreCase),
                "The real dialog refuses invalid Git branch syntax without contacting GitHub");
            Get<TextBox>(dialog, "branch").Text = options.GitHubBranch;
            var publish = Get<Button>(dialog, "publish");
            var cancel = (Button)dialog.CancelButton!;
            var bounds = dialog.ClientRectangle;
            Rectangle Position(Control control) => dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            var work = Screen.FromControl(dialog).WorkingArea;
            Check(bounds.Contains(Position(publish)) && bounds.Contains(Position(cancel))
                && dialog.Width <= work.Width && dialog.Height <= work.Height,
                "Publish and Cancel remain readable inside the real dialog and current-DPI working area",
                new { dialog.DeviceDpi, dialog.Size, dialog.ClientSize, workingArea = work, publish = Position(publish), cancel = Position(cancel) });
            var layout = Get<TableLayoutPanel>(dialog, "layout");
            var clippedText = layout.Controls.Cast<Control>().Where(control => control.Visible && control.Text.Length > 0)
                .Where(control => control is Label or CheckBox or Button).Select(control =>
                {
                    Size preferred = control.GetPreferredSize(new Size(control.Width, 0));
                    return new { type = control.GetType().Name, control.Text, control.Size, preferred,
                        clipped = preferred.Height > control.Height || (control is Button && control.GetPreferredSize(Size.Empty).Width > control.Width) };
                }).Where(item => item.clipped).ToArray();
            Check(clippedText.Length == 0,
                "Real dialog field labels, consent text, destination summary and Choose buttons fit their current-DPI text", clippedText);
            string evidence = Path.Combine(root, "evidence", "HelpPublicationWindows"); Directory.CreateDirectory(evidence);
            using (var image = new Bitmap(dialog.Width, dialog.Height))
            { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(evidence, "publication-dialog.png"), ImageFormat.Png); }
            publish.PerformClick();
            var selected = Result(dialog);
            Check(selected.StatsDirectSource == empty && selected.ExpectedDesktopSha256 == emptyInspected.Sha256
                && !selected.AdoptExistingFlareOutput && selected.PushToGitHub,
                "The real dialog returns the inspected destination fingerprint without adopting or publishing anything");
            dialog.Close();
        }
        using (Form dialog = Create(options with { StatsDirectSource = empty, GitHubBranch = "master", AllowDefaultBranchPublication = false }))
        {
            await Show(dialog);
            Check(!Get<Button>(dialog, "publish").Enabled && Get<Label>(dialog, "error").Text.Contains("default-branch"),
                "The real dialog gates default-branch publication until explicitly acknowledged");
            Get<CheckBox>(dialog, "allowDefault").Checked = true;
            Check(Get<Button>(dialog, "publish").Enabled, "Explicit default-branch acknowledgement enables the reviewed dialog choices");
            Get<Button>(dialog, "publish").PerformClick();
            Check(Result(dialog).AllowDefaultBranchPublication && Result(dialog).ExpectedDesktopSha256 == emptyInspected.Sha256,
                "The returned options retain explicit default-branch consent and the inspected SHA");
            dialog.Close();
        }
        var flareInspected = DesktopHelpPublisher.InspectDestination(flare);
        Check(flareInspected.Ownership == DesktopHelpOwnership.FlareNeedsAdoption,
            "The dialog test uses an isolated recognised Flare output, not an actual StatsDirect repository");
        using (Form dialog = Create(options with { StatsDirectSource = flare }))
        {
            await Show(dialog);
            Check(Get<CheckBox>(dialog, "adoptFlare").Visible && !Get<Button>(dialog, "publish").Enabled
                && Get<Label>(dialog, "inspection").Text.Contains(flareInspected.Sha256),
                "Recognised Flare files require explicit adoption and show their exact inspected fingerprint");
            Get<CheckBox>(dialog, "adoptFlare").Checked = true;
            Check(Get<Button>(dialog, "publish").Enabled, "Acknowledging Flare replacement enables the otherwise valid choices");
            Get<Button>(dialog, "publish").PerformClick();
            var selected = Result(dialog);
            Check(selected.AdoptExistingFlareOutput && !string.IsNullOrWhiteSpace(selected.AdoptionReason)
                && selected.ExpectedDesktopSha256 == flareInspected.Sha256,
                "Flare adoption returns its reason and the reviewed fingerprint for later core revalidation");
            dialog.Close();
        }
        using (Form dialog = Create(options with { StatsDirectSource = malformed }))
        {
            dialog.ShowInTaskbar = false; dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new(-16000, 0);
            dialog.Show(form);
            dialog.Location = new(-16000, 0);
            await Until(() => Get<int>(dialog, "inspectionGeneration") > 0 && !Get<Label>(dialog, "inspection").Text.StartsWith("Inspecting", StringComparison.Ordinal));
            Check(Get<DesktopHelpDestination?>(dialog, "inspected") == null && !Get<Button>(dialog, "publish").Enabled
                && !string.IsNullOrWhiteSpace(Get<Label>(dialog, "inspection").Text),
                "Malformed fake project XML is reported inline and keeps Publish disabled without an unhandled UI exception",
                new { inspected = Get<DesktopHelpDestination?>(dialog, "inspected"), enabled = Get<Button>(dialog, "publish").Enabled, inspection = Get<Label>(dialog, "inspection").Text });
            dialog.Close();
        }
        Check(allBefore.All(pair => File.Exists(pair.Key) && Digest(pair.Key) == pair.Value)
            && allBefore.Count == Digests(empty).Count + Digests(flare).Count + Digests(malformed).Count,
            "Inspecting and accepting real dialog options leaves all fake application/help bytes untouched");
    }

    private static string FakeApplication(string name, bool flare)
    {
        string repository = Path.Combine(fixture, name), project = Path.Combine(repository, "StatsDirectUI");
        Directory.CreateDirectory(Path.Combine(project, "Assets", "Operations"));
        File.WriteAllText(Path.Combine(project, "StatsDirectUI.csproj"),
            "<Project><PropertyGroup><AssemblyName>StatsDirect</AssemblyName><RootNamespace>StatsDirect.UI</RootNamespace></PropertyGroup><ItemGroup><Content Include='Assets/**'/></ItemGroup></Project>");
        string help = Path.Combine(project, "Assets", "Help"); Directory.CreateDirectory(help);
        if (flare)
        {
            Directory.CreateDirectory(Path.Combine(help, "Data"));
            File.WriteAllText(Path.Combine(help, "index.html"), "<html><head><title>Old fake help</title></head><body>Preserve until explicit publication.</body></html>");
            File.WriteAllText(Path.Combine(help, "index.mcwebhelp"), "<WebHelpSystem/>");
            File.WriteAllText(Path.Combine(help, "Data", "HelpSystem.xml"), "<HelpSystem/>");
            File.WriteAllText(Path.Combine(help, "Data", "Alias.xml"), "<CatapultAliasFile/>");
        }
        return repository;
    }
}
