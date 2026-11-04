using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.Core;

// Exercise the real orchestrator and local bare Git transport. The bundle
// validator has its own independently assembled adversarial fixtures.
static class SharedGitChecks
{
    public static async Task Run(string editorRoot, string fixtures, Action<bool, string, object?> check)
    {
        Task<(Fixture Fixture, HelpPublicationOptions Options)> New() => NewFixture(editorRoot, fixtures);
        async Task<HelpPublicationException> Fail(Fixture fixture, HelpPublicationOptions options, Action<HelpPublicationProgress>? callback = null)
        {
            try
            {
                await HelpPublicationPublisher.PublishLocalForTestsAsync(HelpProject.Open(fixture.Source), fixture.Assets, options,
                    progress: callback == null ? null : new InlineProgress(callback));
                throw new Exception("A shared publication unexpectedly succeeded.");
            }
            catch (HelpPublicationException error) { return error; }
        }
        string Work(Fixture fixture) => JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(fixture.History, "*.json").Single()))
            .RootElement.GetProperty("Result").GetProperty("SnapshotDirectory").GetString()!;

        var (f, options) = await New(); string before = await f.Head(); var originalSource = f.SourceHashes(); var originalSeed = f.SeedHashes();
        var result = await HelpPublicationPublisher.PublishLocalForTestsAsync(HelpProject.Open(f.Source), f.Assets, options);
        string checkout = Path.Combine(f.Root, "VerifiedRemote"); await Git(f.Root, "clone", "--no-hardlinks", f.Remote, checkout);
        var verified = SharedContentPublisher.Verify(checkout, result.SharedContentManifestSha256);
        check(result.Completed && result.GitPushed && result.GitCommit == await f.Head() &&
            (await Git(f.Remote, "rev-parse", "main^")).Trim() == before,
            "Shared publication pushes website, desktop help and tutor in exactly one Git commit", result.GitCommit);
        var paths = (await Git(f.Remote, "diff-tree", "--no-commit-id", "--name-only", "-r", "main")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        check(paths.Any(p => p.StartsWith("Docs/")) && paths.Any(p => p.StartsWith("Published/DesktopHelp/")) &&
            paths.Any(p => p.StartsWith("Published/Tutor/")) && paths.Contains("Published/content-manifest.json") &&
            paths.All(p => p.StartsWith("Docs/") || p.StartsWith("Published/") || p.StartsWith("HelpEditor/HelpSource/")),
            "The shared Git commit includes all three outputs and only the authorised source/publication scopes", null);
        check(Same(Files(Path.Combine(checkout, "Published/DesktopHelp")), Files(f.Help)) &&
            Same(Files(Path.Combine(checkout, "Published/Tutor")), Files(Path.Combine(f.Application, "Learning/AuthoredTutor"))),
            "Published DesktopHelp and all six tutor files exactly match their Windows build inputs", null);
        check(verified.Manifest.Artifacts.Tutor.LessonCount == 12 && verified.Manifest.Artifacts.Tutor.QuestionCount == 35 &&
            verified.Manifest.Artifacts.Tutor.TrackCount == 5 && verified.Manifest.TutorSourceSha256 == Hash(options.TutorCoursePath!),
            "Shared publication preserves the original tutor IDs, counts, pathways and saved-source hash", null);
        check(f.SameSource(originalSource) && f.SameSeed(originalSeed), "Shared publication leaves maintained source and the user's checkout unchanged", null);

        options = options with { ExpectedDesktopSha256 = DesktopHelpPublisher.InspectDestination(f.Application).Sha256 };
        string firstId = verified.Manifest.PublicationId;
        var repeated = await HelpPublicationPublisher.PublishLocalForTestsAsync(HelpProject.Open(f.Source), f.Assets, options);
        await Git(checkout, "pull", "--ff-only"); verified = SharedContentPublisher.Verify(checkout, repeated.SharedContentManifestSha256);
        check(repeated.Completed && verified.Manifest.PublicationId != firstId && Same(Files(Path.Combine(checkout, "Published/Tutor")), Files(Path.Combine(f.Application, "Learning/AuthoredTutor"))),
            "A second shared publication replaces the complete prior bundle and retains byte-identical local tutor inputs", null);

        var ordinary = new GitSyncService(HelpProject.Open(Path.Combine(checkout, "HelpEditor/HelpSource")));
        var docsBefore = Files(Path.Combine(checkout, "Docs"));
        var sharedBefore = Files(Path.Combine(checkout, "Published"));
        bool ordinaryRefused = false;
        try { await ordinary.PrepareWebsiteAsync(f.Assets); }
        catch (InvalidOperationException) { ordinaryRefused = true; }
        check(ordinaryRefused && Same(docsBefore, Files(Path.Combine(checkout, "Docs"))) && Same(sharedBefore, Files(Path.Combine(checkout, "Published"))),
            "The ordinary website-only Git workflow refuses a shared repository before changing its Docs or manifest", null);

        string completeHead = await f.Head(), zipHash = Hash(f.Zip); var desktopBefore = Files(f.Help);
        var refused = await Fail(f, options with { PublishSharedContent = false, TutorCoursePath = null,
            ExpectedDesktopSha256 = DesktopHelpPublisher.InspectDestination(f.Application).Sha256 });
        check(!refused.Result.GitPushed && await f.Head() == completeHead && Hash(f.Zip) == zipHash && Same(desktopBefore, Files(f.Help)),
            "Legacy help-only publication refuses to leave an existing shared root manifest stale before changing delivery targets", null);

        var (tamper, tamperOptions) = await New(); string tamperHead = await tamper.Head();
        refused = await Fail(tamper, tamperOptions, progress =>
        {
            if (progress.Phase == "LocalTargets") File.AppendAllText(Path.Combine(Work(tamper), "Git/Published/Tutor/bank.mjs"), "// Changed after commit\n");
        });
        check(!refused.Result.GitPushed && await tamper.Head() == tamperHead,
            "Shared-file tampering after the private commit is detected before Git push", refused.InnerException?.Message);

        var (filter, filterOptions) = await New(); string filterHead = await filter.Head();
        // Force a non-identity Git clean filter after snapshotting but before
        // staging. Published/.gitattributes alone must not certify raw Git bytes.
        refused = await Fail(filter, filterOptions, progress =>
        {
            if (progress.Phase != "PrepareGit") return;
            string git = Path.Combine(Work(filter), "Git");
            Git(git, "config", "filter.sdhe-test.clean", "git hash-object --stdin").GetAwaiter().GetResult();
            Git(git, "config", "filter.sdhe-test.required", "true").GetAwaiter().GetResult();
            Write(Path.Combine(git, ".git/info/attributes"), "Published/Tutor/bank.mjs filter=sdhe-test\n");
        });
        check(!refused.Result.GitPushed && await filter.Head() == filterHead && !File.Exists(filter.Zip) && !Directory.Exists(filter.Help),
            "Git byte conversion of a shared file is rejected before local delivery or remote publication", refused.InnerException?.Message);

        var (dirty, dirtyOptions) = await New(); string dirtyHead = await dirty.Head();
        refused = await Fail(dirty, dirtyOptions, progress =>
        {
            if (progress.Phase != "PrepareGit") return;
            string git = Path.Combine(Work(dirty), "Git"); Write(Path.Combine(git, "unrelated.txt"), "Unrelated staged content\n");
            Git(git, "add", "unrelated.txt").GetAwaiter().GetResult();
        });
        check(!refused.Result.GitPushed && await dirty.Head() == dirtyHead && !File.Exists(dirty.Zip),
            "Enabling shared publication does not allow unrelated staged files into its commit", null);
    }

    internal static async Task<(Fixture Fixture, HelpPublicationOptions Options)> NewFixture(string editorRoot, string fixtures)
    {
        var fixture = new Fixture(Path.Combine(fixtures, "shared-" + Guid.NewGuid().ToString("N")[..8]), true);
        await fixture.Initialize();
        string seed = Path.Combine(editorRoot, "src/StatsDirectHelpEditor.App/TutorSeed/starter-course.json");
        string immutable = Path.Combine(fixture.Root, "TutorSeed/starter-course.json");
        Directory.CreateDirectory(Path.GetDirectoryName(immutable)!); File.Copy(seed, immutable);
        string course = Path.Combine(fixture.Root, "SavedTutor.json"); File.Copy(seed, course);
        // Supply inert host bindings for the unchanged packaged course. None of
        // their statistical code runs, and no authored teaching text is changed.
        var content = JsonNode.Parse(File.ReadAllText(seed))!.AsObject();
        foreach (var record in content["lessons"]!.AsArray().Concat(content["questions"]!.AsArray()))
        {
            string operation = record?["operation"]?.GetValue<string>() ?? "";
            if (operation.Length > 0) Write(Path.Combine(fixture.Application, "StatsDirectUI/Assets/Operations", operation + ".xml"), "<Operation/>\n");
            string help = record?["help"]?.GetValue<string>() ?? "";
            if (help.Length > 0 && !File.Exists(Path.Combine(fixture.Source, "Content", help)))
                Write(Path.Combine(fixture.Source, "Content", help), "<html><head><title>Fixture binding</title></head><body><p>Inert fixture.</p></body></html>\n");
        }
        return (fixture, fixture.Options with { PublishSharedContent = true, TutorCoursePath = course });
    }

    static void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); }
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    static Dictionary<string, string> Files(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'), Hash, StringComparer.Ordinal);
    static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(pair => b.GetValueOrDefault(pair.Key) == pair.Value);
    static async Task<string> Git(string cwd, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-c", "core.autocrlf=false" }.Concat(args)) start.ArgumentList.Add(arg);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); string result = await stdout, error = await stderr;
        if (process.ExitCode != 0) throw new IOException(error); return result.Trim();
    }
}
