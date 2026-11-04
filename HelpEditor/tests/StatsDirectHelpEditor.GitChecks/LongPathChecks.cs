using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using StatsDirect.HelpEditor.Core;

internal static class LongPathChecks
{
    internal static async Task RunAsync(string root, string fixture, Action<bool, string> check)
    {
        string parent = Path.Combine(fixture, "Windows long root");
        string repository = Path.Combine(parent, new string('p', Math.Max(1, 160 - parent.Length - 1)));
        Directory.CreateDirectory(repository);
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        void Write(string relative, string value)
        {
            string path = Path.Combine(repository, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value);
        }
        async Task<string> Git(params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = repository, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-c", "user.name=Long path fixture", "-c", "user.email=long-path@example.invalid", "-c", "commit.gpgsign=false" }.Concat(arguments)) start.ArgumentList.Add(argument);
            start.Environment["GIT_TERMINAL_PROMPT"] = "0";
            using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
            string output = await stdout, error = await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException("Isolated long-path Git fixture: " + string.Join(" ", arguments.Take(2)) + ": " + error);
            return output.Trim();
        }
        try
        {
            Write(HelpProject.MarkerFile, "{}"); Write("StatsDirect.flprj", "<CatapultProject/>");
            Write("Content/contents.htm", "<html><head><title>Long path example</title></head><body><h1>Long path example</h1><p>Keep this authored source.</p></body></html>");
            Write("Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title='Example' Link='/Content/contents.htm'/></CatapultToc>");
            await Git("init", "--initial-branch=help-editor/long-path");
            await Git("config", "--local", "core.longpaths", "false");
            await Git("add", "."); await Git("commit", "-m", "Seed isolated long-path fixture");
            string source = Path.Combine(repository, "Content", "contents.htm"), originalHash = Hash(source);
            var sync = new GitSyncService(HelpProject.Open(repository));
            await sync.PrepareWebsiteAsync(Path.Combine(root, "src", "StatsDirectHelpEditor.App", "Web"));
            string artwork = Directory.GetFiles(Path.Combine(repository, "Docs", "resources", "help-editor-runtime", "equation-artwork"), "*.svg").Order(StringComparer.Ordinal).First();
            string relativeArtwork = Path.GetRelativePath(repository, artwork).Replace('\\', '/');
            check(artwork.Length > 260, "Long-root fixture exercises a real generated equation-artwork path beyond the Windows legacy path limit");
            await sync.CommitWebsiteAsync("Publish verified long-root website", "Fixture author", "author@example.invalid");
            string tracked = await Git("ls-tree", "-r", "--name-only", "HEAD", "--", relativeArtwork);
            check(tracked == relativeArtwork && Hash(source) == originalHash, "Verified website commit includes long generated artwork without changing authored source");
            check(await Git("config", "--local", "--get", "core.longpaths") == "false", "Editor long-path support is per command and leaves the repository's Git configuration unchanged");
            File.WriteAllText(Path.Combine(fixture, "long-path-verification.json"), JsonSerializer.Serialize(new { repository, artwork, absolutePathLength = artwork.Length, relativeArtwork, sourceUnchanged = Hash(source) == originalHash, repositoryLongPathsSetting = "false", passed = true }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            check(false, "Windows long-root website publication completes without an unexpected exception: " + error);
            File.WriteAllText(Path.Combine(fixture, "long-path-verification.json"), JsonSerializer.Serialize(new { repository, passed = false, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
