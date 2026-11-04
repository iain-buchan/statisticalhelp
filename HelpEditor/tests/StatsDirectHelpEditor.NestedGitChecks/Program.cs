using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args[0]), evidence = Path.Combine(root, "evidence/HelpEditor100Release/NestedGit");
string fixture = Path.Combine(evidence, "Fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
var checks = new List<object>(); int failed = 0;
void Check(bool ok, string name) { checks.Add(new { name, passed = ok }); Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }
async Task Reject(Func<Task> action, string name) { try { await action(); Check(false, name); } catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or InvalidOperationException) { Check(true, name); } }
void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); }
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
Dictionary<string, string> Files(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(directory, path).Replace('\\', '/'), Hash, StringComparer.Ordinal);
Dictionary<string, string> Source(string directory) => Files(directory).Where(pair => pair.Key.StartsWith("Content/") || pair.Key.StartsWith("RCode/") || pair.Key == "StatsDirect.flprj" || pair.Key.StartsWith("Project/") && !pair.Key.StartsWith("Project/Users/") && !pair.Key.StartsWith("Project/Destinations/")).ToDictionary();
async Task<string> Git(string cwd, params string[] args)
{
    var start = new ProcessStartInfo("git") { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    foreach (string arg in new[] { "-c", "user.name=Fixture Author", "-c", "user.email=author@example.test", "-c", "commit.gpgsign=false", "-c", "core.longpaths=true" }.Concat(args)) start.ArgumentList.Add(arg);
    start.Environment["GIT_ALLOW_PROTOCOL"] = "file"; start.Environment["GIT_TERMINAL_PROMPT"] = "0";
    using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
    string result = await stdout, error = await stderr; if (process.ExitCode != 0) throw new InvalidOperationException(error); return result.Trim();
}
try
{
    string seed = Path.Combine(fixture, "seed"), remote = Path.Combine(fixture, "origin.git"); Directory.CreateDirectory(seed);
    string nested = Path.Combine(seed, "HelpEditor/HelpSource");
    Write(Path.Combine(nested, "Content/contents.htm"), "<html><head><title>Contents</title></head><body><h1>Contents</h1><p><a href='basics/example.htm'>Example</a></p></body></html>\n");
    Write(Path.Combine(nested, "Content/basics/example.htm"), "<html><head><title>Nested example</title></head><body><h1>Nested example</h1><p>Original body α</p></body></html>\n");
    Write(Path.Combine(nested, "Content/resources/stylesheets/statsdirect.css"), "body{font:10pt Arial;}\n");
    Write(Path.Combine(nested, "Project/TOCs/STATSDIRECT.fltoc"), "<CatapultToc><TocEntry Title='Contents' Link='/Content/contents.htm'/></CatapultToc>\n");
    Write(Path.Combine(nested, "Project/Advanced/STATSDIRECT.flali"), "<CatapultAliasFile><Map Name='CONTENTS' Link='/Content/contents.htm'/></CatapultAliasFile>\n");
    Write(Path.Combine(nested, "Project/Advanced/SD3ContextID.h"), "#define CONTENTS 1000\n");
    Write(Path.Combine(nested, "StatsDirect.flprj"), "<CatapultProject/>\n");
    Write(Path.Combine(nested, HelpProject.MarkerFile), "{\"prototypeWorkingCopy\":true,\"trackedSeed\":\"preserve exactly\"}\n");
    Write(Path.Combine(nested, "RCode/example.R"), "mean(c(1,2))\n");
    Write(Path.Combine(nested, "Project/Users/legacy.xml"), "<local>Unrelated tracked legacy metadata</local>\n");
    Write(Path.Combine(seed, "Content/legacy.htm"), "Legacy root stays unchanged\n");
    Write(Path.Combine(seed, "Project/TOCs/STATSDIRECT.fltoc"), "<CatapultToc/>\n");
    Write(Path.Combine(seed, "Docs/index.html"), "Previous website\n"); Write(Path.Combine(seed, "Docs/CNAME"), "help.example.invalid\n"); Write(Path.Combine(seed, "Docs/.nojekyll"), "");
    Write(Path.Combine(seed, ".gitattributes"), "* text=auto\nHelpEditor/HelpSource/** -text\n");
    Write(Path.Combine(seed, "README.md"), "Unrelated repository documentation\n");
    await Git(fixture, "init", "--bare", "--initial-branch=master", remote); await Git(seed, "init", "--initial-branch=master");
    await Git(seed, "add", "."); await Git(seed, "commit", "-m", "Seed nested publication"); await Git(seed, "remote", "add", "origin", remote); await Git(seed, "push", "-u", "origin", "master");
    string clone = Path.Combine(fixture, "publication"); var checkout = await GitSyncService.CloneLocalPublicationForTestsAsync(remote, "master", clone);
    var sync = new GitSyncService(checkout.Project, checkout.Layout);
    await Git(clone, "config", "core.autocrlf", "false");
    Check(checkout.Layout.SourceRelativePrefix == "HelpEditor/HelpSource" && checkout.BaseCommit == await Git(remote, "rev-parse", "master"), "Private publication clone prefers canonical nested source and pins the selected branch/base commit");
    Check(File.Exists(Path.Combine(clone, ".git/shallow")) && !File.Exists(Path.Combine(clone, ".git/objects/info/alternates")), "Private local test clone is shallow and has no shared object storage");
    Check((await new GitSyncService(checkout.Project).StatusAsync()).IsRepository && (await sync.StatusAsync()).IsRepository, "The ordinary constructor recognizes the fixed canonical nested source without enabling publication opt-in");
    await Reject(() => GitSyncService.ClonePublicationAsync(remote, "master", Path.Combine(fixture, "forbidden")), "Production publication clone refuses local paths without contacting a network");
    await Reject(() => GitSyncService.CloneLocalPublicationForTestsAsync("https://github.com/example/test", "master", Path.Combine(fixture, "forbidden")), "Local bare test seam refuses HTTPS URLs before Git starts");
    try { _ = new GitSyncService(checkout.Project, new(clone, "../outside")); Check(false, "Traversal layout is rejected"); } catch (ArgumentException) { Check(true, "Traversal layout is rejected"); }
    var identityConfig = Path.Combine(fixture, "identity.gitconfig"); Write(identityConfig, "[user]\n name = Fixture Global Name\n email = global@example.test\n");
    string? originalGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
    try { Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", identityConfig); Check(await GitSyncService.ReadDefaultAuthorIdentityAsync() == new GitAuthorIdentity("Fixture Global Name", "global@example.test"), "Default author identity reads global Git configuration without requiring a repository"); Write(identityConfig, ""); Check(await GitSyncService.ReadDefaultAuthorIdentityAsync() == new GitAuthorIdentity("", ""), "Absent global author identity remains blank"); }
    finally { Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", originalGlobal); }
    string page = checkout.Project.ReadTopic("basics/example.htm"); checkout.Project.SaveTopic("basics/example.htm", page.Replace("Original body", "Reviewed body"));
    Check((await sync.StatusAsync()).SourceChangeCount == 1, "Nested source changes are classified using the explicit layout");
    Write(Path.Combine(checkout.Project.RootPath, "RCode/example.R"), "mean(c(1,2))\r\n");
    string publicOutput = Path.Combine(fixture, "Public"); var built = HelpBuilder.Build(checkout.Project, publicOutput, Path.Combine(root, "src/StatsDirectHelpEditor.App/Web"), HelpBuildTarget.Public);
    var expectedOutput = Files(publicOutput); var expectedSource = Source(checkout.Project.RootPath); string oldDocs = Hash(Path.Combine(clone, "Docs/index.html"));
    Write(Path.Combine(publicOutput, "contents.htm"), "Tampered prepared body");
    await Reject(() => sync.PrepareWebsiteFromBuildAsync(publicOutput, expectedOutput, expectedSource, built.TopicCount, built.SearchDocumentCount, built.Warnings), "Prepared publication refuses a changed public file before overwriting Docs");
    Check(Hash(Path.Combine(clone, "Docs/index.html")) == oldDocs, "Rejected prepared artifact preserves the previous Docs tree");
    built = HelpBuilder.Build(checkout.Project, publicOutput, Path.Combine(root, "src/StatsDirectHelpEditor.App/Web"), HelpBuildTarget.Public); expectedOutput = Files(publicOutput);
    await Reject(() => sync.PrepareWebsiteFromBuildAsync(publicOutput, expectedOutput, expectedSource, built.TopicCount, built.SearchDocumentCount, ["Review this warning"]), "Prepared warnings require explicit review opt-in");
    Write(Path.Combine(clone, "Docs/.gitattributes"), "*.html text eol=crlf\n");
    await Reject(() => sync.PrepareWebsiteFromBuildAsync(publicOutput, expectedOutput, expectedSource, built.TopicCount, built.SearchDocumentCount, built.Warnings), "Publication refuses existing incompatible custom Docs Git rules");
    Check(File.ReadAllText(Path.Combine(clone, "Docs/.gitattributes")) == "*.html text eol=crlf\n", "Refusing custom Docs Git rules preserves the exact authored rules");
    File.Delete(Path.Combine(clone, "Docs/.gitattributes"));
    string cnameBefore = Hash(Path.Combine(clone, "Docs/CNAME"));
    await sync.PrepareWebsiteFromBuildAsync(publicOutput, expectedOutput, expectedSource, built.TopicCount, built.SearchDocumentCount, built.Warnings);
    Check(Hash(Path.Combine(clone, "Docs/build-info.json")) == expectedOutput["build-info.json"], "Root Docs consumes the exact prepared build instead of compiling a third artifact");
    Check(Hash(Path.Combine(clone, "Docs/CNAME")) == cnameBefore && File.Exists(Path.Combine(clone, "Docs/.nojekyll")) && !File.Exists(Path.Combine(clone, "Docs/.statsdirect-help-output.json")), "Root Docs preserves deployment metadata and omits the private builder marker");
    Check(File.ReadAllText(Path.Combine(clone, "Docs/.gitattributes")) == "* -text\n", "Reviewed publication pins generated Docs bytes with publisher-owned Git metadata");
    Write(Path.Combine(clone, "README.md"), "Unrelated staged change\n"); await Git(clone, "add", "README.md"); string beforeReject = await sync.ReadHeadCommitAsync();
    await Reject(() => sync.CommitWebsiteAsync("Must refuse", "Fixture Author", "author@example.test"), "Prepared nested publication refuses unrelated staged repository files");
    Check(await sync.ReadHeadCommitAsync() == beforeReject && (await Git(clone, "diff", "--cached", "--name-only")) == "README.md", "Commit refusal preserves HEAD and unrelated staging");
    await Git(clone, "restore", "--staged", "README.md"); await Git(clone, "restore", "README.md");
    Write(Path.Combine(clone, ".git/info/attributes"), "HelpEditor/HelpSource/RCode/example.R text eol=lf\n");
    string beforeFilter = await sync.ReadHeadCommitAsync(), rBefore = Hash(Path.Combine(checkout.Project.RootPath, "RCode/example.R"));
    await Reject(() => sync.CommitWebsiteAsync("Must reject newline conversion", "Fixture Author", "author@example.test"), "Raw index OID verification refuses Git newline conversion of frozen R source");
    Check(await sync.ReadHeadCommitAsync() == beforeFilter && Hash(Path.Combine(checkout.Project.RootPath, "RCode/example.R")) == rBefore, "Refusing Git conversion preserves HEAD and every original R source byte");
    File.Delete(Path.Combine(clone, ".git/info/attributes"));
    await sync.CommitWebsiteAsync("Reviewed nested source and website", "Fixture Author", "author@example.test"); string published = await sync.ReadHeadCommitAsync();
    var committedPaths = (await Git(clone, "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")).Split('\n');
    Check(committedPaths.Contains("HelpEditor/HelpSource/Content/basics/example.htm") && committedPaths.Any(path => path.StartsWith("Docs/")) && committedPaths.All(path => path.StartsWith("Docs/") || path.StartsWith("HelpEditor/HelpSource/")), "One commit contains only canonical nested source and root Docs");
    Check((await Git(clone, "show", "HEAD:Content/legacy.htm")) == "Legacy root stays unchanged", "Canonical nested publication leaves the legacy root source unchanged");
    await Reject(() => sync.PushWebsiteAsync(), "Ordinary website push still refuses the default branch");
    await Reject(() => sync.PushPreparedWebsiteAsync(false), "Dedicated publication also requires explicit default-branch opt-in");
    Write(Path.Combine(clone, "README.md"), "An unrelated commit inserted after help review\n"); await Git(clone, "add", "README.md"); await Git(clone, "commit", "-m", "Unrelated inserted commit");
    string inserted = await sync.ReadHeadCommitAsync();
    await Reject(() => sync.PushPreparedWebsiteAsync(inserted, true), "Stored review pin refuses an unrelated inserted commit even if the caller supplies the new HEAD");
    Check(await Git(remote, "rev-parse", "master") == checkout.BaseCommit, "Refusing an inserted commit leaves the remote release branch unchanged");
    await Git(clone, "reset", "--hard", published);
    await sync.PushPreparedWebsiteAsync(published, true);
    Check(await Git(remote, "rev-parse", "master") == published, "Reviewed publication pushes the exact committed SHA to the local default branch");
    string fresh = Path.Combine(fixture, "fresh-crlf");
    string crlfConfig = Path.Combine(fixture, "crlf.gitconfig"); Write(crlfConfig, "[core]\n autocrlf = true\n");
    GitPublicationCheckout freshCheckout;
    try { Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", crlfConfig); freshCheckout = await GitSyncService.CloneLocalPublicationForTestsAsync(remote, "master", fresh); }
    finally { Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", originalGlobal); }
    await Git(fresh, "config", "core.autocrlf", "true");
    Check(File.ReadAllText(Path.Combine(fresh, "README.md")).Contains('\r') && Hash(Path.Combine(fresh, "Docs/contents.htm")) == Hash(Path.Combine(clone, "Docs/contents.htm")), "Fresh clone applies normal CRLF checkout while publisher-owned Docs remains exact");
    var freshSync = new GitSyncService(freshCheckout.Project, freshCheckout.Layout);
    try { await freshSync.PushPreparedWebsiteAsync(true); Check(true, "Fresh autocrlf checkout verifies committed source/Docs receipt and can repeat the exact prepared push"); } catch (Exception error) { Check(false, "Fresh autocrlf checkout verifies committed source/Docs receipt: " + error.Message); }
    Check(Files(Path.Combine(clone, "Docs")).All(pair => Hash(Path.Combine(fresh, "Docs", pair.Key)) == pair.Value), "Every Docs file including R/code and receipt survives a fresh autocrlf checkout byte-for-byte");
    Check(Source(checkout.Project.RootPath).All(pair => Hash(Path.Combine(freshCheckout.Project.RootPath, pair.Key)) == pair.Value), "Every canonical authored input including CRLF R source survives the fresh checkout byte-for-byte");
    Check(File.ReadAllText(Path.Combine(fresh, "Docs/basics/example.htm")).Contains("Reviewed body"), "Fresh normalized Docs retains the reviewed statistical topic body");
    await Git(clone, "remote", "set-url", "origin", "https://github.com/example/never-contact-this");
    await Reject(() => sync.PushPreparedWebsiteAsync(true), "Local test publication refuses an origin changed to HTTPS before any network request");
    await Git(clone, "remote", "set-url", "origin", new Uri(remote).AbsoluteUri);
    await Git(clone, "switch", "-c", "unexpected-publication-branch");
    await Reject(() => sync.PushPreparedWebsiteAsync(true), "Publication refuses an active branch changed from the user's selected clone branch");
    await Git(clone, "switch", "master");
    string advance = Path.Combine(fixture, "advance"); await Git(fixture, "clone", remote, advance); Write(Path.Combine(advance, "README.md"), "A concurrent author advanced the remote\n"); await Git(advance, "add", "README.md"); await Git(advance, "commit", "-m", "Concurrent remote change"); await Git(advance, "push", "origin", "master");
    string remoteAdvanced = await Git(remote, "rev-parse", "master"); await Reject(() => sync.PushPreparedWebsiteAsync(true), "Publication refuses a remote branch advanced after the reviewed clone");
    Check(await Git(remote, "rev-parse", "master") == remoteAdvanced, "Refused publication preserves the newer remote commit without force-pushing");

    string? originalProtocols = Environment.GetEnvironmentVariable("GIT_ALLOW_PROTOCOL");
    try
    {
        Environment.SetEnvironmentVariable("GIT_ALLOW_PROTOCOL", "file");
        string normal = Path.Combine(fixture, "ordinary-clone"); var normalProject = await GitSyncService.CloneAsync(remote, normal); var normalSync = new GitSyncService(normalProject);
        Check(Path.GetRelativePath(normal, normalProject.RootPath).Replace('\\', '/') == "HelpEditor/HelpSource" && normalProject.ReadTopic("basics/example.htm").Contains("Reviewed body"), "Normal Clone project opens the authoritative nested source rather than stale root Content");
        Check(Hash(Path.Combine(normalProject.RootPath, HelpProject.MarkerFile)) == Hash(Path.Combine(seed, "HelpEditor/HelpSource", HelpProject.MarkerFile)) && (await normalSync.StatusAsync()).Changes.Count == 0, "Normal clone preserves a tracked source marker and starts clean without writing local metadata into it");
        string excludes = File.ReadAllText(Path.Combine(normal, ".git/info/exclude"));
        Check(excludes.Contains("/HelpEditor/HelpSource/" + HelpProject.MarkerFile) && excludes.Contains("/HelpEditor/HelpSource/.help-editor-recovery/"), "Normal nested clone roots marker and recovery exclusions at the selected source prefix");
        await Reject(() => normalSync.PushAsync(), "Ordinary nested clone still refuses the default branch");
        await Reject(() => normalSync.PushPreparedWebsiteAsync(true), "Ordinary nested session cannot enable the dedicated publication API");
        await normalSync.CreateBranchAsync("author/normal-menu");
        normalProject.SaveTopic("basics/example.htm", normalProject.ReadTopic("basics/example.htm").Replace("Reviewed body", "Normal menu body"));
        Check((await normalSync.StatusAsync()).SourceChangeCount == 1, "Normal nested status classifies the edited canonical page as source");
        await normalSync.CommitAsync("Normal nested source edit", "Fixture Author", "author@example.test");
        Check(await Git(normal, "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD") == "HelpEditor/HelpSource/Content/basics/example.htm", "Normal source commit scopes only the nested authored page");
        await normalSync.PrepareWebsiteAsync(Path.Combine(root, "src/StatsDirectHelpEditor.App/Web"));
        Check(File.ReadAllText(Path.Combine(normal, "Docs/basics/example.htm")).Contains("Normal menu body") && File.ReadAllText(Path.Combine(normal, "Docs/.gitattributes")) == "* -text\n", "Normal nested website preparation updates root Docs and retains exact-byte metadata");
        await normalSync.CommitWebsiteAsync("Normal nested website", "Fixture Author", "author@example.test"); await normalSync.PushWebsiteAsync();
        Check(await Git(remote, "rev-parse", "refs/heads/author/normal-menu") == await normalSync.ReadHeadCommitAsync() && await Git(remote, "rev-parse", "master") == remoteAdvanced, "Normal nested website push updates only the working branch and leaves the live default unchanged");
        string arbitrary = Path.Combine(normal, "Other/HelpSource"); Write(Path.Combine(arbitrary, HelpProject.MarkerFile), "{}"); Write(Path.Combine(arbitrary, "Content/a.htm"), "<html><body>A</body></html>"); Write(Path.Combine(arbitrary, "Project/TOCs/STATSDIRECT.fltoc"), "<CatapultToc/>");
        Check(!(await new GitSyncService(HelpProject.Open(arbitrary)).StatusAsync()).IsRepository, "An arbitrary nested marked folder still cannot bypass the independent Git-root guard");
    }
    finally { Environment.SetEnvironmentVariable("GIT_ALLOW_PROTOCOL", originalProtocols); }
}
catch (Exception error) { Check(false, "Unexpected nested Git test error: " + error); }
File.WriteAllText(Path.Combine(evidence, "checks.json"), JsonSerializer.Serialize(new { passed = failed == 0, assertions = checks.Count, failed, fixture, checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{checks.Count-failed}/{checks.Count} nested Git checks passed; {failed} failed"); return failed == 0 ? 0 : 1;
