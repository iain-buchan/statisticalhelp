using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args.Length == 0 ? Path.Combine(AppContext.BaseDirectory, "../../../../../") : args[0]);
string evidence = Path.Combine(root, "evidence");
string fixtures = Path.Combine(evidence, "WebsitePublishFixture-" + Guid.NewGuid().ToString("N"));
string assets = Path.Combine(root, "src", "StatsDirectHelpEditor.App", "Web");
Directory.CreateDirectory(fixtures);
var results = new List<object>();
int passed = 0, failed = 0;
void Check(bool value, string name)
{
    if (value) passed++; else failed++;
    results.Add(new { name, passed = value });
    Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}");
}
async Task Reject(Func<Task> operation, string name)
{
    try { await operation(); Check(false, name); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Xml.XmlException or OperationCanceledException)
    { Check(true, name); }
}
async Task<string> Git(string cwd, params string[] arguments)
{
    var start = new ProcessStartInfo("git") { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
    foreach (string arg in new[] { "-c", "user.name=Iain Buchan", "-c", "user.email=buchan@liverpool.ac.uk", "-c", "commit.gpgsign=false", "-c", "core.quotepath=false" }.Concat(arguments)) start.ArgumentList.Add(arg);
    start.Environment["GIT_TERMINAL_PROMPT"] = "0";
    using var process = Process.Start(start)!;
    Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    string output = await stdout, error = await stderr;
    if (process.ExitCode != 0) throw new InvalidOperationException($"Git {string.Join(' ', arguments.Take(2))}: {error}");
    return output.Trim();
}
void Write(string folder, string relative, string content)
{
    string path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, new UTF8Encoding(false));
}
Dictionary<string, string> Digests(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(folder, path).Replace('\\', '/'), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);
bool SameDigests(Dictionary<string, string> before, string folder)
{
    var after = Digests(folder);
    return before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string? hash) && hash == pair.Value);
}

try
{
    string seed = Path.Combine(fixtures, "seed"), remote = Path.Combine(fixtures, "origin.git"), copy = Path.Combine(fixtures, "authoring");
    Directory.CreateDirectory(seed);
    const string topic = """
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd"><head><title>Publication example α</title><link rel="stylesheet" href="../resources/stylesheets/statsdirect.css" /></head><body><h1>Publication example α</h1><p id="Ref-Case">Updatedsearchword: before &amp; after.</p><p MadCap:conditions="Primary.WEBHELP">Publiconlysentinel</p><p MadCap:conditions="Primary.SWHELP">Desktoponlysentinel</p><p><a href="../contents.htm#Home">Contents</a></p><p><img src="../resources/images/sample.svg" alt="Example artwork" /></p><p><MadCap:equation>\(\frac{x_1+x_2}{2}\)</MadCap:equation></p><MadCap:dropDown class="rcode"><MadCap:dropDownHead><MadCap:dropDownHotspot>R code</MadCap:dropDownHotspot></MadCap:dropDownHead><MadCap:dropDownBody><pre xml:space="preserve">x &lt;- c(1, 2)
          mean(x)
        # λ stays Unicode</pre></MadCap:dropDownBody></MadCap:dropDown></body></html>
        """;
    Write(seed, "StatsDirect.flprj", "<CatapultProject />");
    Write(seed, "Content/contents.htm", "<html><head><title>Publication contents</title></head><body><h1 id=\"Home\">Publication contents</h1><p><a href=\"basics/example.htm#Ref-Case\">Example α</a></p></body></html>");
    Write(seed, "Content/basics/example.htm", topic);
    Write(seed, "Content/basics/retired.htm", "<html><head><title>Retired page</title></head><body>Retiredsearchword</body></html>");
    Write(seed, "Content/resources/stylesheets/statsdirect.css", "body { font: 10pt Arial; } p { color: #123456; }");
    Write(seed, "Content/resources/images/sample.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"10\"><rect width=\"20\" height=\"10\" fill=\"blue\" /></svg>");
    Write(seed, "Content/resources/masterpages/footer.flmsp", "<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><body><MadCap:pageFooter><p>Publicfooterword <a href=\"https://www.statsdirect.com/Download.aspx\">Download</a></p></MadCap:pageFooter></body></html>");
    Write(seed, "Project/Targets/Website.fltar", "<CatapultTarget MasterPage=\"/Content/resources/masterpages/footer.flmsp\" />");
    Write(seed, "Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc><TocEntry Title=\"Publication contents\" Link=\"/Content/contents.htm\"><TocEntry Title=\"Published example α\" Link=\"/Content/basics/example.htm#Ref-Case\" /></TocEntry></CatapultToc>");
    Write(seed, "Project/Advanced/STATSDIRECT.flali", "<CatapultAliasFile><Map Name=\"EXAMPLE\" Link=\"/Content/basics/example.htm#Ref-Case\" /></CatapultAliasFile>");
    Write(seed, "Project/Advanced/SD3ContextID.h", "#define EXAMPLE 1234\n");
    Write(seed, "RCode/example.R", "mean(c(1, 2))\n");
    Write(seed, "README.md", "Unrelated repository documentation.\n");
    Write(seed, "Docs/index.html", "Old Flare index");
    Write(seed, "Docs/legacy.htm", "Old Flare topic");
    Write(seed, "Docs/Data/Search.js", "Old Flare search");
    Write(seed, "Docs/CNAME", "help.example.invalid\n");
    Write(seed, "Docs/.nojekyll", "");
    await Git(fixtures, "init", "--bare", "--initial-branch=master", remote);
    await Git(seed, "init", "--initial-branch=master"); await Git(seed, "add", "."); await Git(seed, "commit", "-m", "Seed publication fixture");
    await Git(seed, "remote", "add", "origin", remote); await Git(seed, "push", "-u", "origin", "master");
    string masterBefore = await Git(remote, "rev-parse", "refs/heads/master");
    var project = await GitSyncService.CloneAsync(remote, copy);
    var sync = new GitSyncService(project);
    await sync.CreateBranchAsync("help-editor/website-publish");
    string docs = Path.Combine(copy, "Docs");
    byte[] originalCname = File.ReadAllBytes(Path.Combine(docs, "CNAME"));
    string initialHead = await Git(copy, "rev-parse", "HEAD");
    await Reject(() => sync.CommitWebsiteAsync("Unprepared publication", "Iain Buchan", "buchan@liverpool.ac.uk"), "Website commit requires a successful prepared build");
    await Reject(() => sync.PushWebsiteAsync(), "Website push refuses an unprepared, uncommitted build");
    Write(copy, "Docs/untracked-note.txt", "An untracked file must never be overwritten by publication.");
    var docsWithUntracked = Digests(docs);
    await Reject(() => sync.PrepareWebsiteAsync(assets), "Publication refuses to replace untracked files in Docs");
    Check(SameDigests(docsWithUntracked, docs) && await Git(copy, "rev-parse", "HEAD") == initialHead, "Refusing untracked website content leaves every file and Git history unchanged");
    File.Delete(Path.Combine(docs, "untracked-note.txt"));

    var prepared = await sync.PrepareWebsiteAsync(assets);
    Check(Path.GetFullPath(prepared.OutputDirectory).TrimEnd(Path.DirectorySeparatorChar).Equals(docs, StringComparison.OrdinalIgnoreCase) && prepared.TopicCount == 3 && prepared.SearchDocumentCount == 3, "Preparation regenerates all saved topics and search documents into repository Docs");
    Check(prepared.Warnings.Count == 0, "Valid publication fixture builds without warnings");
    Check(File.Exists(Path.Combine(docs, "index.html")) && !File.ReadAllText(Path.Combine(docs, "index.html")).Contains("Old Flare"), "Generated live entry point replaces the old tracked Flare index");
    Check(!File.Exists(Path.Combine(docs, "legacy.htm")) && !File.Exists(Path.Combine(docs, "Data", "Search.js")), "Old tracked Flare topics and obsolete search files are removed");
    Check(File.ReadAllBytes(Path.Combine(docs, "CNAME")).SequenceEqual(originalCname) && File.Exists(Path.Combine(docs, ".nojekyll")), "Publication preserves GitHub Pages CNAME and .nojekyll configuration");
    string generatedTopic = File.ReadAllText(Path.Combine(docs, "basics", "example.htm"));
    Check(generatedTopic.Contains("Publiconlysentinel") && !generatedTopic.Contains("Desktoponlysentinel") && generatedTopic.Contains("Publicfooterword"), "GitHub output uses Public conditions and the Website shared footer");
    Check(File.ReadAllText(Path.Combine(docs, "search-index.js")).Contains("updatedsearchword") && File.ReadAllText(Path.Combine(docs, "contents-data.js")).Contains("Ref-Case"), "Generated search and contents reflect saved source and case-sensitive anchors");
    Check(generatedTopic.Contains("../resources/help-editor-runtime/mathjax/tex-mml-svg.js") && File.Exists(Path.Combine(docs, "resources", "help-editor-runtime", "mathjax", "tex-mml-svg.js")), "Equations have local MathJax with correct nested-topic relative paths");
    Check(generatedTopic.Contains("../resources/images/sample.svg") && File.ReadAllBytes(Path.Combine(docs, "resources", "images", "sample.svg")).SequenceEqual(File.ReadAllBytes(Path.Combine(copy, "Content", "resources", "images", "sample.svg"))), "Generated artwork preserves SVG bytes and GitHub-subpath-safe relative links");
    Check(File.ReadAllText(Path.Combine(docs, "Data", "Alias.xml")).Contains("ResolvedId=\"1234\"") && generatedTopic.Contains("id=\"Ref-Case\""), "Context-help IDs and existing section anchors remain usable");
    Check(await Git(copy, "rev-parse", "HEAD") == initialHead && await Git(remote, "rev-parse", "refs/heads/master") == masterBefore, "Preparation never commits, pushes or changes the remote default branch");

    var preparedDigests = Digests(docs);
    string sourcePath = Path.Combine(copy, "Content", "basics", "example.htm");
    File.WriteAllText(sourcePath, topic.Replace("Updatedsearchword", "Changedafterprepare"));
    await Reject(() => sync.CommitWebsiteAsync("Stale source", "Iain Buchan", "buchan@liverpool.ac.uk"), "Changing saved source after preparation blocks a stale website commit");
    Check(await Git(copy, "rev-parse", "HEAD") == initialHead && SameDigests(preparedDigests, docs), "Stale-source rejection preserves Git history and prepared Docs");
    File.WriteAllText(sourcePath, topic);
    string docsTopicPath = Path.Combine(docs, "basics", "example.htm");
    File.WriteAllText(docsTopicPath, generatedTopic + "\n<!-- changed after prepare -->");
    await Reject(() => sync.CommitWebsiteAsync("Altered output", "Iain Buchan", "buchan@liverpool.ac.uk"), "Changing generated Docs after preparation blocks the website commit");
    File.WriteAllText(docsTopicPath, generatedTopic);
    Write(copy, "README.md", "Unrelated staged edit\n"); await Git(copy, "add", "--", "README.md");
    await Reject(() => sync.CommitWebsiteAsync("Unrelated staged changes", "Iain Buchan", "buchan@liverpool.ac.uk"), "Website commit refuses unrelated staged repository files");
    await Git(copy, "restore", "--staged", "--", "README.md"); await Git(copy, "restore", "--", "README.md");
    string recoveryPath = Path.Combine(copy, ".help-editor-recovery", "deleted-pages", "fixture", "original.htm");
    Write(copy, ".help-editor-recovery/deleted-pages/fixture/original.htm", "Local recovery source must stay local.");
    await Git(copy, "add", "-f", "--", ".help-editor-recovery");
    await Reject(() => sync.CommitWebsiteAsync("Staged recovery", "Iain Buchan", "buchan@liverpool.ac.uk"), "Website commit refuses manually staged deleted-page recovery archives");
    await Git(copy, "restore", "--staged", "--", ".help-editor-recovery");

    project.SaveTopic("basics/example.htm", topic.Replace("Updatedsearchword", "Committedsearchword"));
    var regenerated = await sync.PrepareWebsiteAsync(assets);
    Check(regenerated.TopicCount == 3 && File.ReadAllText(docsTopicPath).Contains("Committedsearchword") && !File.ReadAllText(docsTopicPath).Contains("Updatedsearchword"), "Preparing again after source edits refreshes the existing generated site with the newest saved content");
    await sync.CommitWebsiteAsync("Publish source and generated website", "Iain Buchan", "buchan@liverpool.ac.uk");
    string committedHead = await Git(copy, "rev-parse", "HEAD");
    string committedPaths = await Git(copy, "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD");
    Check(committedPaths.Contains("Content/basics/example.htm") && committedPaths.Contains("Docs/basics/example.htm") && committedPaths.Contains("Docs/search-index.js"), "One website commit records matching saved source, generated pages and rebuilt search");
    Check(await Git(copy, "log", "-1", "--format=%an <%ae>|%cn <%ce>") == "Iain Buchan <buchan@liverpool.ac.uk>|Iain Buchan <buchan@liverpool.ac.uk>", "Publication records Iain Buchan as author and committer");
    Check(!committedPaths.Contains("README.md") && !committedPaths.Contains(".help-editor-recovery") && File.Exists(recoveryPath), "Website commit excludes unrelated files and preserves local recovery data");
    Check(await Git(copy, "show", "HEAD:Content/basics/example.htm") == File.ReadAllText(sourcePath).Trim() && (await Git(copy, "show", "HEAD:Docs/basics/example.htm")).Contains("Committedsearchword"), "Committed source and website carry the same latest saved content");

    string validSavedSource = File.ReadAllText(sourcePath), validCommittedHtml = File.ReadAllText(docsTopicPath);
    File.WriteAllText(sourcePath, validSavedSource.Replace("Committedsearchword", "Uncommittedlaterword"));
    await Reject(() => sync.PushWebsiteAsync(), "Website push blocks source changed after the matching publication commit");
    File.WriteAllText(sourcePath, validSavedSource);
    File.WriteAllText(docsTopicPath, validCommittedHtml + "\n<!-- uncommitted output -->");
    await Reject(() => sync.PushWebsiteAsync(), "Website push blocks output changed after the matching publication commit");
    File.WriteAllText(docsTopicPath, validCommittedHtml);
    await new GitSyncService(HelpProject.Open(copy)).PushWebsiteAsync();
    Check(true, "A reopened Git service validates the committed publication receipt without rebuilding or losing provenance");
    Check(await Git(remote, "rev-parse", "refs/heads/help-editor/website-publish") == committedHead, "Explicit publication push sends the verified source-and-Docs commit to the working branch");
    var pushedStatus = await sync.StatusAsync();
    Check(pushedStatus.TrackingBranch == "origin/help-editor/website-publish" && pushedStatus.HasComparison && pushedStatus.Ahead == 0 && pushedStatus.Behind == 0, "Pinned publication push retains configured upstream tracking and synchronized cached counts");
    Check(await Git(remote, "rev-parse", "refs/heads/master") == masterBefore, "Working-branch publication never merges or updates the live default branch");
    Check((await sync.StatusAsync()).SourceChangeCount == 0 && !(await sync.StatusAsync()).Changes.Any(change => change.Path.StartsWith("Docs/", StringComparison.Ordinal)), "Successful publication leaves source and Docs clean");

    var beforeFailedBuild = Digests(docs);
    string missingAssets = Path.Combine(fixtures, "missing-runtime-assets");
    Directory.CreateDirectory(missingAssets);
    await Reject(() => sync.PrepareWebsiteAsync(missingAssets), "Missing offline MathJax prevents publication preparation");
    Check(SameDigests(beforeFailedBuild, docs) && await Git(copy, "rev-parse", "HEAD") == committedHead, "Missing runtime assets leave every verified Docs file and Git history unchanged");
    string receiptPath = Path.Combine(docs, ".statsdirect-help-website.json");
    string validReceipt = File.ReadAllText(receiptPath);
    File.WriteAllText(receiptPath, "{\"Schema\":1,\"SourceHash\":null,\"TopicCount\":1,\"Files\":null}");
    await Reject(() => sync.CommitWebsiteAsync("Malformed receipt", "Iain Buchan", "buchan@liverpool.ac.uk"), "A malformed or incomplete website receipt cannot certify a publication commit");
    await Reject(() => sync.PushWebsiteAsync(), "A malformed or incomplete website receipt cannot certify a publication push");
    File.WriteAllText(receiptPath, validReceipt);
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        await Reject(() => sync.PrepareWebsiteAsync(assets, cancelled.Token), "Cancelled publication preparation starts no build or Git mutation");
        Check(SameDigests(beforeFailedBuild, docs) && await Git(copy, "rev-parse", "HEAD") == committedHead, "Cancellation preserves the verified website and current commit");
    }
    File.WriteAllText(sourcePath, "<html><body>malformed source");
    await Reject(() => sync.PrepareWebsiteAsync(assets), "Malformed topic aborts publication preparation");
    Check(SameDigests(beforeFailedBuild, docs) && await Git(copy, "rev-parse", "HEAD") == committedHead, "A failed build leaves the complete previous Docs tree and Git history unchanged");
    File.WriteAllText(sourcePath, validSavedSource);

    File.Delete(Path.Combine(copy, "Content", "basics", "retired.htm"));
    var refreshed = await sync.PrepareWebsiteAsync(assets);
    Check(refreshed.TopicCount == 2 && refreshed.SearchDocumentCount == 2 && !File.Exists(Path.Combine(docs, "basics", "retired.htm")) && !File.ReadAllText(Path.Combine(docs, "search-index.js")).Contains("retiredsearchword"), "Rebuilding after page removal deletes its website file and search entry");
    await sync.CommitWebsiteAsync("Remove retired page and regenerate website", "Iain Buchan", "buchan@liverpool.ac.uk");
    string deletions = await Git(copy, "diff-tree", "--no-commit-id", "--name-status", "-r", "HEAD");
    Check(deletions.Contains("D\tContent/basics/retired.htm") && deletions.Contains("D\tDocs/basics/retired.htm"), "Page removal is committed for both source and generated website");
    await sync.PushWebsiteAsync();
    Check(await Git(remote, "rev-parse", "refs/heads/help-editor/website-publish") == await Git(copy, "rev-parse", "HEAD") && await Git(remote, "rev-parse", "refs/heads/master") == masterBefore, "Follow-on publication updates only its reviewed working branch");
    Check(File.ReadAllBytes(Path.Combine(docs, "CNAME")).SequenceEqual(originalCname) && File.Exists(Path.Combine(docs, ".nojekyll")) && File.Exists(recoveryPath), "Repeated publication retains Pages configuration and local recovery data");
    if (OperatingSystem.IsWindows())
    {
        project.SaveTopic("basics/example.htm", validSavedSource.Replace("Committedsearchword", "Retriedcopyword"));
        using (var locked = new FileStream(Path.Combine(docs, "contents-data.js"), FileMode.Open, FileAccess.Read, FileShare.Read))
            await Reject(() => sync.PrepareWebsiteAsync(assets), "A locked destination interrupts website replacement without falsely completing publication");
        await Reject(() => sync.CommitWebsiteAsync("Incomplete replacement", "Iain Buchan", "buchan@liverpool.ac.uk"), "Partially copied website cannot be committed as a complete publication");
        var retried = await sync.PrepareWebsiteAsync(assets);
        Check(retried.TopicCount == 2 && File.ReadAllText(docsTopicPath).Contains("Retriedcopyword"), "Releasing a locked destination permits a complete retry without discarding saved source");
        await sync.CommitWebsiteAsync("Complete interrupted website replacement", "Iain Buchan", "buchan@liverpool.ac.uk");
        await sync.PushWebsiteAsync();
        Check(await Git(remote, "rev-parse", "refs/heads/help-editor/website-publish") == await Git(copy, "rev-parse", "HEAD"), "Retried website can be committed and pushed only after its full copy succeeds");
    }
    string lastPushed = await Git(remote, "rev-parse", "refs/heads/help-editor/website-publish");
    string mutationScript = Path.Combine(fixtures, "mutate-local-head.ps1"), uploadScript = Path.Combine(fixtures, "mutate-before-upload.sh");
    string QuotePs(string value) => "'" + value.Replace("'", "''") + "'";
    string QuoteSh(string value) => "'" + value.Replace('\\', '/').Replace("'", "'\"'\"'") + "'";
    string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(validSavedSource.Replace("Committedsearchword", "Latercommittedsourceword")));
    File.WriteAllText(mutationScript,
        "[IO.File]::WriteAllText(" + QuotePs(sourcePath) + ", [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + payload + "')), [Text.UTF8Encoding]::new($false))\n" +
        "& git -C " + QuotePs(copy) + " add -- Content/basics/example.htm\nif ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }\n" +
        "& git -C " + QuotePs(copy) + " -c 'user.name=Iain Buchan' -c 'user.email=buchan@liverpool.ac.uk' -c commit.gpgsign=false commit --only -m 'Concurrent later source commit' -- Content/basics/example.htm\nexit $LASTEXITCODE\n");
    File.WriteAllText(uploadScript, "#!/bin/sh\npowershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + QuoteSh(mutationScript) + " >/dev/null || exit $?\nexec git upload-pack \"$@\"\n", new UTF8Encoding(false));
    await Git(copy, "config", "remote.origin.uploadpack", "sh " + QuoteSh(uploadScript));
    try { await sync.PushWebsiteAsync(); }
    finally { await Git(copy, "config", "--unset", "remote.origin.uploadpack"); }
    Check(await Git(copy, "rev-parse", "HEAD") != lastPushed && await Git(remote, "rev-parse", "refs/heads/help-editor/website-publish") == lastPushed, "Concurrent HEAD change during remote checking cannot push a later unverified source commit");
    await Reject(() => sync.PushWebsiteAsync(), "A clean but separately committed source change cannot bypass website freshness validation");
    Check(await Git(remote, "rev-parse", "refs/heads/help-editor/website-publish") == lastPushed, "Rejected stale committed-source push leaves the remote working branch unchanged");
    File.AppendAllText(Path.Combine(copy, ".git", "info", "exclude"), "\n/Content/basics/ignored.htm\n");
    Write(copy, "Content/basics/ignored.htm", "<html><head><title>Ignored source</title></head><body>Ignored source must not be published without its source.</body></html>");
    await sync.PrepareWebsiteAsync(assets);
    string beforeIgnoredCommit = await Git(copy, "rev-parse", "HEAD");
    await Reject(() => sync.CommitWebsiteAsync("Ignored source cannot be omitted", "Iain Buchan", "buchan@liverpool.ac.uk"), "A website built from ignored source cannot be committed without that source");
    Check(await Git(copy, "rev-parse", "HEAD") == beforeIgnoredCommit && await Git(remote, "rev-parse", "refs/heads/master") == masterBefore, "Ignored-source publication rejection preserves commit history and the default branch");
}
catch (Exception ex)
{
    failed++;
    results.Add(new { name = "Unexpected website publication check failure", passed = false, error = ex.ToString() });
    Console.Error.WriteLine(ex);
}
if (args.Contains("--full-source"))
{
    try
    {
        string seed = Path.Combine(fixtures, "full-source-seed"), remote = Path.Combine(fixtures, "full-source-origin.git"), copy = Path.Combine(fixtures, "full-source-authoring");
        string original = Path.Combine(root, "HelpSource");
        Directory.CreateDirectory(seed);
        foreach (string folder in new[] { "Content", "Project", "RCode" })
        {
            string source = Path.Combine(original, folder);
            if (!Directory.Exists(source)) continue;
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(seed, Path.GetRelativePath(original, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }
        File.Copy(Path.Combine(original, "StatsDirect.flprj"), Path.Combine(seed, "StatsDirect.flprj"));
        Write(seed, "Docs/index.html", "Old full-source Flare index");
        await Git(fixtures, "init", "--bare", "--initial-branch=master", remote);
        await Git(seed, "init", "--initial-branch=master"); await Git(seed, "add", "."); await Git(seed, "commit", "-m", "Seed complete real help source fixture");
        await Git(seed, "remote", "add", "origin", remote); await Git(seed, "push", "-u", "origin", "master");
        string masterBefore = await Git(remote, "rev-parse", "refs/heads/master");
        var project = await GitSyncService.CloneAsync(remote, copy);
        var service = new GitSyncService(project);
        await service.CreateBranchAsync("help-editor/full-source-publication");
        var sourceBefore = new[] { "Content", "Project", "RCode" }.Where(folder => Directory.Exists(Path.Combine(copy, folder))).ToDictionary(folder => folder, folder => Digests(Path.Combine(copy, folder)));
        var prepared = await service.PrepareWebsiteAsync(assets);
        string docs = Path.Combine(copy, "Docs");
        Check(prepared.TopicCount == 404 && prepared.SearchDocumentCount == 403, "Complete copied help publishes 404 topics and 403 searchable pages");
        Check(prepared.Warnings.Count == 0, "Complete copied help publication builds without warnings");
        Check(sourceBefore.All(pair => SameDigests(pair.Value, Path.Combine(copy, pair.Key))), "Preparing the complete website preserves every copied source file byte");
        using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(docs, ".statsdirect-help-website.json")));
        var listed = receipt.RootElement.GetProperty("Files").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal);
        var actual = Digests(docs);
        actual.Remove(".statsdirect-help-website.json");
        Check(listed.Count == actual.Count && listed.All(pair => actual.TryGetValue(pair.Key, out string? digest) && digest == pair.Value), "Complete website publication receipt certifies every generated Docs file hash");
        Check(!actual.ContainsKey(".statsdirect-help-output.json") && !actual.Keys.Any(path => path.Contains(".help-editor-recovery", StringComparison.Ordinal) || path.EndsWith(".log.zip", StringComparison.Ordinal)), "Complete website excludes builder-local receipts, recovery archives and Flare build logs");
        string contents = File.ReadAllText(Path.Combine(docs, "contents.htm")), f = File.ReadAllText(Path.Combine(docs, "distributions", "f.htm"));
        Check(contents.Contains("data-help-skin=\"flare\"") && contents.Contains("resources/images/sd3helplogo.png") && f.Contains("../resources/help-editor-runtime/mathjax/tex-mml-svg.js"), "Complete publication retains the selected Flare design, logo and nested relative equation assets");
        Check(File.Exists(Path.Combine(docs, "search-index.js")) && File.Exists(Path.Combine(docs, "contents-data.js")) && File.Exists(Path.Combine(docs, "Data", "Alias.xml")), "Complete publication includes fresh search, contents and application context mappings");
        await service.CommitWebsiteAsync("Publish all saved help source and regenerated GitHub website", "Iain Buchan", "buchan@liverpool.ac.uk");
        string commit = await Git(copy, "rev-parse", "HEAD");
        Check((await Git(copy, "ls-tree", "-r", "--name-only", "HEAD", "Docs")).Split('\n').Count() == listed.Count + 1, "The complete generated website and its provenance receipt are present in the same commit");
        await new GitSyncService(HelpProject.Open(copy)).PushWebsiteAsync();
        Check(await Git(remote, "rev-parse", "refs/heads/help-editor/full-source-publication") == commit && await Git(remote, "rev-parse", "refs/heads/master") == masterBefore, "Complete 404-topic publication reaches only the isolated working branch and leaves default production history unchanged");
        Check(await Git(copy, "log", "-1", "--format=%an <%ae>|%cn <%ce>") == "Iain Buchan <buchan@liverpool.ac.uk>|Iain Buchan <buchan@liverpool.ac.uk>", "Complete website publication records Iain as author and committer");
    }
    catch (Exception ex)
    {
        failed++;
        results.Add(new { name = "Unexpected full-source publication failure", passed = false, error = ex.ToString() });
        Console.Error.WriteLine(ex);
    }
}
Directory.CreateDirectory(evidence);
File.WriteAllText(Path.Combine(evidence, "website-publication-checks.json"), JsonSerializer.Serialize(new { passed, failed, fixtures, checkedAtUtc = DateTimeOffset.UtcNow, remoteType = "Isolated local bare repository only; no GitHub writes", results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{passed} website publication checks passed; {failed} failed");
return failed == 0 ? 0 : 1;
