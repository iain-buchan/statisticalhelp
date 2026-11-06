using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args[0]);
string fixture = Path.Combine(root, "evidence", "InstalledWorkspaceFixture-" + Guid.NewGuid().ToString("N"));
string installation = Path.Combine(fixture, "Program Files", "StatsDirect Help Editor");
string user = Path.Combine(fixture, "User Data");
var checks = new List<object>();
void Check(bool passed, string name) { checks.Add(new { name, passed }); Console.WriteLine((passed ? "PASS " : "FAIL ") + name); }
void Write(string path, string value) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value, new UTF8Encoding(false)); }
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
string Seed(string application, string text = "Installed sample café α")
{
    Write(Path.Combine(application, HelpEditorWorkspace.InstallMarkerFile), "{\"schemaVersion\":1}");
    string seed = Path.Combine(application, "HelpSource");
    Write(Path.Combine(seed, HelpProject.MarkerFile), "{\"workingCopy\":true}");
    Write(Path.Combine(seed, "Content", "contents.htm"), "<html><head><title>Contents</title></head><body><h1>Contents</h1><p>" + text + "</p></body></html>");
    Write(Path.Combine(seed, "Project", "TOCs", "STATSDIRECT.fltoc"), "<CatapultToc><TocEntry Title='Contents' Link='/Content/contents.htm'/></CatapultToc>");
    Write(Path.Combine(seed, "RCode", "example.R"), "x <- 1:3\nmean(x)\n");
    Write(Path.Combine(seed, "StatsDirect.flprj"), "<CatapultProject/>");
    return seed;
}
Directory.CreateDirectory(fixture);
string report = Path.Combine(root, "evidence", "installed-workspace-checks.json");
Write(report, JsonSerializer.Serialize(new { completed = false, passed = 0, failed = 0 }));
try
{
    string dev = Path.Combine(fixture, "Developer");
    Directory.CreateDirectory(Path.Combine(dev, "HelpSource"));
    var development = HelpEditorWorkspace.Resolve(Path.Combine(dev, "App"), user);
    Check(development.RootPath == dev && development.DefaultProjectPath == Path.Combine(dev, "HelpSource") && development.SeedProjectPath == null,
        "The existing developer App/HelpSource layout keeps its own writable root");
    string sample = Path.Combine(fixture, "Sample layout");
    Directory.CreateDirectory(Path.Combine(sample, "SampleProject"));
    Check(HelpEditorWorkspace.Resolve(Path.Combine(sample, "bin"), user).DefaultProjectPath == Path.Combine(sample, "SampleProject"),
        "Isolated native-test SampleProject discovery remains supported");
    string seed = Seed(installation);
    Write(Path.Combine(seed, ".git", "config"), "private config");
    Write(Path.Combine(seed, ".help-editor-recovery", "deleted.htm"), "private archive");
    Write(Path.Combine(seed, "Build", "old.htm"), "old output");
    Write(Path.Combine(seed, "Project", "Users", "private.fluser"), "personal recent files");
    Write(Path.Combine(seed, "Project", "Destinations", "live.fldes"), "live publishing destination");
    string image = Path.Combine(seed, "Content", "test.png");
    File.WriteAllBytes(image, [0, 1, 2, 127, 128, 255]);
    File.SetAttributes(image, File.GetAttributes(image) | FileAttributes.ReadOnly);
    var seedHashes = Directory.GetFiles(seed, "*", SearchOption.AllDirectories).ToDictionary(path => path, Hash);
    var workspace = HelpEditorWorkspace.Resolve(installation, user);
    string expectedRoot = Path.Combine(user, "StatsDirect", "HelpEditor");
    Check(workspace.RootPath == expectedRoot && workspace.DefaultProjectPath == Path.Combine(expectedRoot, "HelpSource"),
        "Installed applications resolve profiles, builds and working copies beneath per-user application data");
    Check(!Directory.Exists(expectedRoot), "Resolving the installation does not create or alter a workspace");
    workspace.PrepareDefaultProject();
    var opened = HelpProject.Open(workspace.DefaultProjectPath);
    Check(opened.ListTopics().Count == 1 && opened.LoadContents().Count == 1,
        "First run creates a complete, marked and readable help working copy");
    Check(opened.ReadTopic("contents.htm").Contains("café α") && File.ReadAllText(Path.Combine(workspace.DefaultProjectPath, "RCode", "example.R")) == "x <- 1:3\nmean(x)\n",
        "Authored Unicode topics and exact R source are preserved in the first copy");
    string copiedImage = Path.Combine(workspace.DefaultProjectPath, "Content", "test.png");
    Check(Hash(copiedImage) == Hash(image) && (File.GetAttributes(copiedImage) & FileAttributes.ReadOnly) == 0,
        "Binary artwork remains byte-identical and installed read-only attributes do not prevent editing");
    Check(!Directory.Exists(Path.Combine(workspace.DefaultProjectPath, ".git")) && !Directory.Exists(Path.Combine(workspace.DefaultProjectPath, ".help-editor-recovery")) && !Directory.Exists(Path.Combine(workspace.DefaultProjectPath, "Build")),
        "Git configuration, recovery archives and generated output are excluded from the installed seed");
    Check(!Directory.Exists(Path.Combine(workspace.DefaultProjectPath, "Project", "Users")) && !Directory.Exists(Path.Combine(workspace.DefaultProjectPath, "Project", "Destinations")),
        "Personal Flare settings and live publishing destinations are not copied to new workspaces");
    Check(seedHashes.All(pair => File.Exists(pair.Key) && Hash(pair.Key) == pair.Value),
        "First run changes no file in the read-only installation seed");
    string authorTopic = "<html><head><title>My work</title></head><body><p>Author edits</p></body></html>";
    opened.SaveTopic("contents.htm", authorTopic);
    Write(Path.Combine(workspace.DefaultProjectPath, "my-own.txt"), "Keep this");
    Write(Path.Combine(seed, "Content", "contents.htm"), "<html><head><title>Upgrade seed</title></head><body><p>New installer</p></body></html>");
    workspace.PrepareDefaultProject();
    Check(opened.ReadTopic("contents.htm") == authorTopic && File.ReadAllText(Path.Combine(workspace.DefaultProjectPath, "my-own.txt")) == "Keep this",
        "Subsequent launches and upgraded seeds never overwrite an existing author's work");
    Check(!Directory.GetDirectories(expectedRoot, ".seed-*").Any(), "Successful initialization leaves no staging directories");

    string originalWorkspace = Path.Combine(fixture, "Existing authoring workspace α");
    string originalHelp = Path.Combine(originalWorkspace, "HelpSource");
    Write(Path.Combine(originalHelp, HelpProject.MarkerFile), "{}");
    Write(Path.Combine(originalHelp, "Content", "contents.htm"), authorTopic);
    Write(Path.Combine(originalHelp, "Project", "TOCs", "STATSDIRECT.fltoc"),
        "<CatapultToc><TocEntry Title='My work' Link='/Content/contents.htm'/></CatapultToc>");
    string tutor = Path.Combine(originalWorkspace, "TutorSource", "starter-course.json");
    Write(tutor, "{\"id\":\"my-tutor-course\"}");
    string preference = Path.Combine(originalWorkspace, "Settings", "authoring-preferences.json");
    Write(preference, JsonSerializer.Serialize(new { helpPublication = new { tutorCoursePath = tutor } }));
    var originalHashes = Directory.GetFiles(originalWorkspace, "*", SearchOption.AllDirectories).ToDictionary(path => path, Hash);
    string location = Path.Combine(expectedRoot, HelpEditorWorkspace.WorkspaceLocationFile);
    void SelectWorkspace(string path) => Write(location, JsonSerializer.Serialize(new { schemaVersion = 1, workspaceRoot = path }));
    SelectWorkspace(originalWorkspace);
    var redirected = HelpEditorWorkspace.Resolve(installation, user);
    redirected.PrepareDefaultProject();
    Check(redirected.RootPath == originalWorkspace && redirected.DefaultProjectPath == originalHelp && redirected.SeedProjectPath == null,
        "An installed editor selects the existing help, tutor and publication workspace without seeding");
    Check(originalHashes.All(pair => Hash(pair.Key) == pair.Value) &&
        Directory.GetFiles(originalWorkspace, "*", SearchOption.AllDirectories).Length == originalHashes.Count &&
        !Directory.Exists(Path.Combine(expectedRoot, "TutorSource")),
        "Selecting an existing workspace preserves all original bytes and creates no duplicate tutor source");
    Check(HelpEditorWorkspace.Resolve(Path.Combine(dev, "App"), user).RootPath == dev,
        "The installed workspace setting leaves developer layouts unchanged");

    foreach (string target in new[] { "Relative root", Path.Combine(fixture, "Missing workspace"), seed })
    {
        SelectWorkspace(target);
        bool refused = false;
        try { _ = HelpEditorWorkspace.Resolve(installation, user); }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException) { refused = true; }
        Check(refused, "An invalid configured location is refused without falling back");
    }
    Write(location, "{broken");
    bool malformedLocationRejected = false;
    try { _ = HelpEditorWorkspace.Resolve(installation, user); } catch (InvalidDataException) { malformedLocationRejected = true; }
    Check(malformedLocationRejected && opened.ReadTopic("contents.htm") == authorTopic,
        "Invalid workspace JSON preserves the other workspace instead of switching to it");
    SelectWorkspace(originalWorkspace);
    Check(HelpEditorWorkspace.Resolve(installation, user).RootPath == originalWorkspace && originalHashes.All(pair => Hash(pair.Key) == pair.Value),
        "The per-user workspace choice remains effective with an updated installation seed");

    string brokenInstallation = Path.Combine(fixture, "Broken installation");
    string brokenSeed = Seed(brokenInstallation);
    File.WriteAllText(Path.Combine(brokenSeed, "Content", "contents.htm"), "<html><body>malformed");
    var broken = HelpEditorWorkspace.Resolve(brokenInstallation, Path.Combine(fixture, "Broken user"));
    bool invalidRejected = false;
    try { broken.PrepareDefaultProject(); } catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException) { invalidRejected = true; }
    Check(invalidRejected && !Directory.Exists(broken.DefaultProjectPath), "A malformed installed seed cannot leave a partial default working copy");

    string parallelInstallation = Path.Combine(fixture, "Parallel installation");
    Seed(parallelInstallation);
    var parallel = HelpEditorWorkspace.Resolve(parallelInstallation, Path.Combine(fixture, "Parallel user"));
    await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(parallel.PrepareDefaultProject)));
    Check(HelpProject.Open(parallel.DefaultProjectPath).ListTopics().Count == 1 && !Directory.GetDirectories(parallel.RootPath, ".seed-*").Any(),
        "Simultaneous first launches converge on one complete copy without overwriting or partial staging");

    Write(Path.Combine(parallelInstallation, HelpEditorWorkspace.InstallMarkerFile), "{\"schemaVersion\":2}");
    bool versionRejected = false;
    try { _ = HelpEditorWorkspace.Resolve(parallelInstallation, user); } catch (InvalidDataException) { versionRejected = true; }
    Check(versionRejected, "An unsupported installation layout is refused rather than guessing writable program paths");
}
catch (Exception ex) { Check(false, "Installed workspace checks complete: " + ex); }
int failed = checks.Count(check => !JsonSerializer.SerializeToElement(check).GetProperty("passed").GetBoolean());
Write(report, JsonSerializer.Serialize(new { checkedAtUtc = DateTimeOffset.UtcNow, completed = true, passed = checks.Count - failed, failed, fixture, checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{checks.Count - failed} installed workspace checks passed; {failed} failed");
return failed == 0 ? 0 : 1;
