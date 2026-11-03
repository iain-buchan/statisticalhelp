using System.Text.Json;
using StatsDirect.HelpEditor.Core;

bool help = args.Length == 0 || args.Length == 1 && (args[0] is "--help" or "-h")
    || args.Length == 2 && (args[0] is "build" or "list" or "prepare-workspace" or "export-website" or "validate-course" or "export-course" or "publish-tutor") && (args[1] is "--help" or "-h");
if (help)
{
    Console.WriteLine("""
        StatsDirect Help Builder — command-line help compilation

        Build:
          StatsDirectHelpBuilder build --project <working-copy> --output <folder>
            [--assets <App/Web>] [--target DesktopHelp|Website|GitHub]
        List:
          StatsDirectHelpBuilder list --project <working-copy>
        Export a freshly rebuilt Website as a flat ZIP:
          StatsDirectHelpBuilder export-website --project <working-copy> --zip <website.zip>
            [--assets <App/Web>]
        Installed first-run working copy:
          StatsDirectHelpBuilder prepare-workspace
        Check an editable tutor course or export a learner-importable pack:
          StatsDirectHelpBuilder validate-course --course <source.json>
          StatsDirectHelpBuilder export-course --course <source.json> --output <course-pack.json>
        Publish reviewed built-in tutor inputs for the next StatsDirect build:
          StatsDirectHelpBuilder publish-tutor --course <source.json> --statsdirect-source <repository>
            [--baseline <immutable-starter.json>]

        DesktopHelp is the default target. Desktop/Public are accepted aliases.
        Website and GitHub use the same Public HTML, footer and source conditions.
        Assets default to the sibling App/Web folder when deployed in App/Builder.
        Framework-dependent builds need the .NET 10 runtime; the installer includes it.
        Building help does not require the .NET SDK.
        Builds use saved source in a marked working copy. They do not open a GUI,
        invoke Flare, publish, or change source files.
        export-website uses saved source and atomically replaces its ZIP only after success.
        prepare-workspace creates an installed user's first copy only; existing work is preserved.
        Successful results are JSON on stdout; errors go to stderr with exit code 1.
        """);
    return 0;
}
try
{
    string command = args[0].ToLowerInvariant();
    if (command == "prepare-workspace")
    {
        if (args.Length != 1) throw new ArgumentException("prepare-workspace does not accept options.");
        var workspace = HelpEditorWorkspace.Resolve(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")));
        workspace.PrepareDefaultProject();
        Console.WriteLine(JsonSerializer.Serialize(new { WorkspaceRoot = workspace.RootPath, ProjectPath = workspace.DefaultProjectPath }));
        return 0;
    }
    if (command is not ("build" or "list" or "export-website" or "validate-course" or "export-course" or "publish-tutor")) throw new ArgumentException("Unknown command. Use --help.");
    var allowed = command switch { "build" => new[] { "--project", "--output", "--assets", "--target" }, "export-website" => ["--project", "--zip", "--assets"], "validate-course" => ["--course"], "export-course" => ["--course", "--output"], "publish-tutor" => ["--course", "--statsdirect-source", "--baseline"], _ => ["--project"] };
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int index = 1; index < args.Length; index += 2)
    {
        string name = args[index];
        if (!allowed.Contains(name)) throw new ArgumentException("Unknown option for " + command + ": " + name);
        if (options.ContainsKey(name)) throw new ArgumentException("Duplicate option: " + name);
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Missing value for " + name);
        options.Add(name, args[index + 1]);
    }
    string Get(string name, string? fallback = null) => options.GetValueOrDefault(name) ?? fallback ?? throw new ArgumentException("Missing " + name);
    if (command == "publish-tutor")
    {
        var course = TutorCourseDocument.Open(Get("--course"));
        string repo = Path.GetFullPath(Get("--statsdirect-source"));
        if (!File.Exists(Path.Combine(repo, "Learning", "build.mjs")) || !File.Exists(Path.Combine(repo, "StatsDirectUI", "StatsDirectUI.csproj"))) throw new InvalidDataException("Choose the StatsDirect source repository.");
        string sourceAssets = Path.Combine(repo, "StatsDirectUI", "Assets");
        string baselinePath = Get("--baseline", Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "TutorSeed", "starter-course.json")));
        var original = TutorCourseDocument.Open(baselinePath);
        if (course.Content["authoring"] is not System.Text.Json.Nodes.JsonObject authoring) course.Content["authoring"] = authoring = new();
        authoring["builtinBaseline"] = TutorBuiltinPublisher.CreateBaseline(original.Content,
            Directory.EnumerateFiles(Path.Combine(sourceAssets, "Operations"), "*.xml", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension).OfType<string>(),
            Directory.EnumerateFiles(Path.Combine(sourceAssets, "Help"), "*.htm", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(Path.Combine(sourceAssets, "Help"), path).Replace('\\', '/')));
        var published = TutorBuiltinPublisher.Publish(course, Path.Combine(repo, "Learning", "AuthoredTutor"));
        Console.WriteLine(JsonSerializer.Serialize(published, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    if (command is "validate-course" or "export-course")
    {
        var course = TutorCourseDocument.Open(Get("--course"));
        var issues = course.Validate(); bool valid = !issues.Any(issue => issue.Severity == "Error");
        string? output = null;
        if (command == "export-course") { output = Path.GetFullPath(Get("--output")); course.Export(output); }
        Console.WriteLine(JsonSerializer.Serialize(new { Command = command, ValidForExport = valid, Source = course.FilePath, Output = output, Lessons = course.GetItems("lessons").Count, Questions = course.GetItems("questions").Count, Issues = issues }, new JsonSerializerOptions { WriteIndented = true }));
        return valid ? 0 : 1;
    }
    var project = HelpProject.Open(Get("--project"));
    if (command == "list")
    {
        Console.WriteLine(JsonSerializer.Serialize(project.ListTopics(), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    if (command == "export-website")
    {
        string exportAssets = options.TryGetValue("--assets", out var chosenExportAssets) ? chosenExportAssets : DefaultAssets();
        var exported = WebsiteZipExporter.Export(project, Get("--zip"), exportAssets);
        Console.WriteLine(JsonSerializer.Serialize(exported, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    string requested = Get("--target", "DesktopHelp");
    var (target, targetName) = requested.ToLowerInvariant() switch
    {
        "desktop" or "desktophelp" => (HelpBuildTarget.Desktop, "DesktopHelp"),
        "public" or "website" => (HelpBuildTarget.Public, "Website"),
        "github" => (HelpBuildTarget.Public, "GitHub"),
        _ => throw new ArgumentException("Target must be DesktopHelp, Website or GitHub (Desktop/Public aliases are accepted).")
    };
    string assets = options.TryGetValue("--assets", out var chosenAssets) ? chosenAssets : DefaultAssets();
    var result = HelpBuilder.Build(project, Get("--output"), assets, target);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Target = targetName,
        SourceProject = project.RootPath,
        result.OutputDirectory,
        result.TopicCount,
        result.SearchDocumentCount,
        result.Warnings
    }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

static string DefaultAssets()
{
    string sibling = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Web"));
    if (Directory.Exists(sibling)) return sibling;
    string bundled = Path.Combine(AppContext.BaseDirectory, "Web");
    if (Directory.Exists(bundled)) return bundled;
    // A developer's bin folder can use its own source assets. A deployed
    // package never silently falls back to an unrelated project or current cwd.
    for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
    {
        if (!File.Exists(Path.Combine(directory.FullName, "StatsDirectHelpEditor.Cli.csproj"))) continue;
        string source = Path.GetFullPath(Path.Combine(directory.FullName, "..", "StatsDirectHelpEditor.App", "Web"));
        if (Directory.Exists(source)) return source;
        break;
    }
    throw new DirectoryNotFoundException("The help builder's Web assets are missing. Restore the sibling App/Web folder, or specify --assets <folder>.");
}
