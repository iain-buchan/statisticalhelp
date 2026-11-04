using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static partial class Program
{
    private static async Task RealSharedDialogChecks()
    {
        string application = FakeApplication("DialogShared", true), coursePath = options.TutorCoursePath!;
        var course = TutorCourseDocument.Open(coursePath);
        string operationRoot = Path.Combine(application, "StatsDirectUI/Assets/Operations"), helpRoot = Path.Combine(application, "StatsDirectUI/Assets/Help");
        foreach (var record in course.GetItems("lessons").Concat(course.GetItems("questions")))
        {
            string operation = record["operation"]?.GetValue<string>() ?? "", help = record["help"]?.GetValue<string>() ?? "";
            if (operation.Length > 0) File.WriteAllText(Path.Combine(operationRoot, operation + ".xml"), "<Operation/>");
            if (help.Length > 0)
            {
                string file = Path.Combine(helpRoot, help.Split('#', '?')[0]); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                if (!File.Exists(file)) File.WriteAllText(file, "<html><body>Isolated tutor-help binding fixture.</body></html>");
            }
        }
        string editedPath = Path.Combine(fixture, "UnreviewedTutor.json"); var edited = JsonNode.Parse(File.ReadAllText(coursePath))!.AsObject();
        edited["questions"]![0]!["stem"] = "An unreviewed external fixture change."; File.WriteAllText(editedPath, edited.ToJsonString());
        var before = Digests(application); string savedCourse = File.ReadAllText(coursePath), savedEdited = File.ReadAllText(editedPath);
        Type type = typeof(MainForm).Assembly.GetType("StatsDirect.HelpEditor.App.HelpPublicationDialog", true)!;
        T Get<T>(Form target, string name) => (T)type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        using Form dialog = (Form)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
            [options with { StatsDirectSource = application, PublishSharedContent = true }], null)!;
        dialog.ShowInTaskbar = false; dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new(-16000, 0); dialog.Show(form); dialog.Location = new(-16000, 0);
        await Until(() => Get<DesktopHelpDestination?>(dialog, "inspected") != null && Get<TutorBuiltinInspection?>(dialog, "inspectedTutor") != null);
        var inspected = Get<TutorBuiltinInspection>(dialog, "inspectedTutor"); var warning = Get<Label>(dialog, "tutorInspection");
        Check(dialog.Text == "Publish content" && Get<TextBox>(dialog, "tutorCourse").Text == coursePath && inspected.CanPublish,
            "The actual shared dialog inspects the selected saved original tutor without silently substituting another course");
        Check(inspected.ReviewIssues.Any(issue => issue.Severity == "Warning") && warning.Text.Contains("Warning:", StringComparison.Ordinal)
            && inspected.ReviewIssues.Where(issue => issue.Severity == "Warning").All(issue => warning.Text.Contains(issue.Message, StringComparison.Ordinal)),
            "The actual shared dialog displays the original pending-review warnings without inventing expert approval", warning.Text);
        Check(!Get<Button>(dialog, "publish").Enabled && Get<CheckBox>(dialog, "adoptFlare").Visible,
            "Valid tutor material does not bypass the separate explicit desktop Flare-adoption gate");
        Get<CheckBox>(dialog, "adoptFlare").Checked = true;
        await Until(() => Get<Button>(dialog, "publish").Enabled);
        Get<TextBox>(dialog, "tutorCourse").Text = editedPath;
        await Until(() => Get<TutorBuiltinInspection?>(dialog, "inspectedTutor") is { CanPublish: false });
        Check(!Get<Button>(dialog, "publish").Enabled && Get<Label>(dialog, "tutorInspection").Text.Contains("Error:", StringComparison.Ordinal),
            "An unreviewed changed saved course disables real-dialog publication while preserving the draft course file");
        Get<TextBox>(dialog, "tutorCourse").Text = Path.Combine(fixture, "MissingSharedTutor.json");
        await Until(() => Get<Label>(dialog, "tutorInspection").Text.Contains("existing saved tutor", StringComparison.OrdinalIgnoreCase));
        Check(!Get<Button>(dialog, "publish").Enabled && Get<TutorBuiltinInspection?>(dialog, "inspectedTutor") == null,
            "A missing explicit tutor selection remains inline and cannot publish or fall back to the default");
        string malformedCourse = Path.Combine(fixture, "MalformedSharedTutor.json"); File.WriteAllText(malformedCourse, "{\"schemaVersion\":99}");
        Get<TextBox>(dialog, "tutorCourse").Text = malformedCourse;
        await Until(() => Get<TutorBuiltinInspection?>(dialog, "inspectedTutor") is { CanPublish: false });
        Check(!Get<Button>(dialog, "publish").Enabled && Get<Label>(dialog, "tutorInspection").Text.Contains("Error:", StringComparison.Ordinal),
            "A malformed saved course schema is an inline non-publishable result without an unhandled UI exception");
        Get<TextBox>(dialog, "tutorCourse").Text = coursePath;
        await Until(() => Get<TutorBuiltinInspection?>(dialog, "inspectedTutor") is { CanPublish: true } && Get<Button>(dialog, "publish").Enabled);
        var publish = Get<Button>(dialog, "publish"); var cancel = (Button)dialog.CancelButton!;
        Rectangle Bounds(Control control) => dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
        var scroller = dialog.Controls.OfType<Panel>().Single(panel => panel.AutoScroll);
        Check(dialog.ClientRectangle.Contains(Bounds(publish)) && dialog.ClientRectangle.Contains(Bounds(cancel))
            && publish.GetPreferredSize(Size.Empty).Width <= publish.Width && cancel.GetPreferredSize(Size.Empty).Width <= cancel.Width
            && scroller.AutoScroll && warning.AutoSize && warning.Height >= warning.GetPreferredSize(new Size(warning.Width, 0)).Height,
            "Shared tutor inspection rows can scroll while readable Publish and Cancel buttons remain visible at the actual DPI",
            new { dialog.DeviceDpi, dialog.Size, dialog.ClientSize, publish = Bounds(publish), cancel = Bounds(cancel), warningSize = warning.Size });
        string evidence = Path.Combine(root, "evidence/HelpPublicationWindows"); Directory.CreateDirectory(evidence);
        using (var image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(evidence, "shared-publication-dialog.png"), ImageFormat.Png); }
        publish.PerformClick(); var selected = (HelpPublicationOptions)type.GetProperty("Options")!.GetValue(dialog)!;
        Check(selected.PublishSharedContent && selected.TutorCoursePath == coursePath && selected.AdoptExistingFlareOutput
            && selected.ExpectedDesktopSha256 == Get<DesktopHelpDestination>(dialog, "inspected").Sha256,
            "The shared dialog returns the exact reviewed tutor path, shared flag and inspected desktop fingerprint without publishing");
        dialog.Close();
        Check(before.Count == Digests(application).Count && before.All(pair => Digest(pair.Key) == pair.Value)
            && File.ReadAllText(coursePath) == savedCourse && File.ReadAllText(editedPath) == savedEdited,
            "Shared-dialog inspection and acceptance leave all source, fake application and tutor bytes unchanged");
    }
}
