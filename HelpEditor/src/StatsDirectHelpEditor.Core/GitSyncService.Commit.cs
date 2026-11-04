using System.Text;

namespace StatsDirect.HelpEditor.Core;

public sealed partial class GitSyncService
{
    // A path-limited `git commit --only` rereads the working files after review.
    // Pin the reviewed tree instead, so a later save cannot enter that commit.
    private async Task<string> CommitReviewedTreeAsync(string[] paths, string message, string name, string email, bool website, CancellationToken ct)
    {
        string branch = (await RunAsync(RepositoryRoot, ["symbolic-ref", "--quiet", "HEAD"], ct)).Output.Trim();
        string parent = (await RunAsync(RepositoryRoot, ["rev-parse", "HEAD"], ct)).Output.Trim();
        string gitIndex = (await RunAsync(RepositoryRoot, ["rev-parse", "--path-format=absolute", "--git-path", "index"], ct)).Output.Trim();
        SafePaths.CheckNoLinks(gitIndex);
        string stem = gitIndex + ".helpeditor-" + Guid.NewGuid().ToString("N");
        string originalIndex = stem + ".original", privateIndex = stem + ".reviewed", mergeIndex = stem + ".merge";
        var originalEnv = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = originalIndex };
        var privateEnv = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = privateIndex };
        bool InScope(string path) => IsScopedSourcePath(path) || website && IsWebsiteCommitPath(path);
        try
        {
            File.Copy(gitIndex, originalIndex);
            var originalEntries = await IndexEntriesAsync(originalEnv, ct);
            if (originalEntries.Values.Any(value => !value.Split('\t')[0].EndsWith(" 0", StringComparison.Ordinal)))
                throw new InvalidOperationException("Resolve staged merge conflicts before committing help source.");
            await RunAsync(RepositoryRoot, ["read-tree", parent], ct, environment: privateEnv);
            string tree;
            // Git may read these files, but saves/renames cannot change their
            // bytes while they are staged and compared with the build receipt.
            var held = new List<FileStream>();
            try
            {
                var inputs = SourceFiles().Concat(website ? WebsiteFiles(Path.Combine(RepositoryRoot, WebsitePath)).Keys
                    .Append(WebsiteReceiptName).Select(path => WebsitePath + "/" + path).Concat(PreparedSharedPaths) : []);
                foreach (string relative in inputs.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                    held.Add(File.Open(SafePaths.Resolve(RepositoryRoot, relative), FileMode.Open, FileAccess.Read, FileShare.Read));
                await RejectContentFiltersAsync(paths, ct);
                // Windows Git otherwise reuses tracked directory casing even
                // after the private clone receives a freshly cased Docs tree.
                // Clear only this reviewed scope in the temporary index.
                if (website && preparedFreshWebsite)
                    await RunAsync(RepositoryRoot, ["rm", "--cached", "-r", "--ignore-unmatch", "--", WebsitePath], ct, environment: privateEnv);
                // Staging all authored pages and generated assets can exceed
                // 30 seconds on a cold or actively scanned disk. Cancellation
                // still stops this one bounded operation promptly.
                try { await RunAsync(RepositoryRoot, ["add", "--all", "--", .. paths], ct, timeoutSeconds: 180, environment: privateEnv); }
                catch (TimeoutException error)
                { throw new TimeoutException("Git did not finish staging the help source and generated website within three minutes. This attempt did not create a commit. Check local disk activity and try again.", error); }
                if (website) await VerifyWebsiteForCommitAsync(ct, privateEnv);
                var changed = await RunAsync(RepositoryRoot, ["diff", "--cached", "--quiet", "--exit-code", parent], ct, allowFailure: true, environment: privateEnv);
                if (changed.ExitCode == 0) throw new InvalidOperationException("There are no help-source changes to commit.");
                if (changed.ExitCode != 1) throw new InvalidOperationException("Git could not check the reviewed changes.");
                tree = (await RunAsync(RepositoryRoot, ["write-tree"], ct, environment: privateEnv)).Output.Trim();
            }
            finally { foreach (var stream in held) stream.Dispose(); }
            var identity = new Dictionary<string, string>
            {
                ["GIT_AUTHOR_NAME"] = name, ["GIT_AUTHOR_EMAIL"] = email,
                ["GIT_COMMITTER_NAME"] = name, ["GIT_COMMITTER_EMAIL"] = email
            };
            // User hooks cannot alter this already verified tree. No branch is
            // changed until the compare-and-set transaction below succeeds.
            string commit = (await RunAsync(RepositoryRoot, ["commit-tree", "--no-gpg-sign", tree, "-p", parent, "-m", message], ct, environment: identity)).Output.Trim();
            await UpdateReviewedBranchAsync(branch, parent, commit, message, ct);
            if (website && publicationLayout) reviewedPublicationCommit = commit;
            string? notice = null;
            try { await RefreshCommittedIndexAsync(gitIndex, mergeIndex, originalEntries, await IndexEntriesAsync(privateEnv, ct), InScope, branch, commit, ct); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            { notice = " The commit succeeded, but Git staging could not be refreshed. Your staged changes were preserved; review Git status before continuing."; }
            return (website ? "Committed saved help source and the regenerated GitHub website" : "Committed the help-source changes") + " to " + branch["refs/heads/".Length..] + "." + notice;
        }
        finally
        {
            foreach (string path in new[] { originalIndex, privateIndex, mergeIndex })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    private async Task UpdateReviewedBranchAsync(string branch, string parent, string commit, string message, CancellationToken ct)
    {
        string currentBranch = (await RunAsync(RepositoryRoot, ["symbolic-ref", "--quiet", "HEAD"], ct)).Output.Trim();
        if (currentBranch != branch) throw new InvalidOperationException("The active branch changed during review. No branch was updated; review the current state and retry.");
        // The branch name is pinned: a checkout after the check above cannot
        // redirect this update to a different branch. The old SHA is atomic.
        var update = await RunAsync(RepositoryRoot, ["update-ref", "-m", "help editor: " + message.Split('\n')[0], branch, commit, parent], ct, allowFailure: true);
        if (update.ExitCode != 0)
            throw new InvalidOperationException("The branch changed during review. No branch was updated by the editor; review the current state and retry. " + Redact(update.Error.Trim()));
    }

    private async Task<Dictionary<string, string>> IndexEntriesAsync(IReadOnlyDictionary<string, string> environment, CancellationToken ct)
    {
        string output = (await RunAsync(RepositoryRoot, ["ls-files", "--stage", "-z"], ct, environment: environment)).Output;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.Split('\t')[0].EndsWith(" 0", StringComparison.Ordinal) || !result.TryAdd(line[(line.IndexOf('\t') + 1)..], line))
                throw new InvalidOperationException("Resolve staged merge conflicts before committing help source.");
        }
        return result;
    }

    private async Task RejectContentFiltersAsync(string[] paths, CancellationToken ct)
    {
        // Newline normalization is supported. Arbitrary clean filters/ident can
        // change statistical text or generated HTML after its build was checked.
        var listed = await RunAsync(RepositoryRoot, ["ls-files", "--cached", "--others", "--exclude-standard", "-z", "--", .. paths], ct);
        var attrs = await RunAsync(RepositoryRoot, ["check-attr", "-z", "--stdin", "filter", "ident"], ct, input: listed.Output);
        string[] fields = attrs.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 2 < fields.Length; i += 3)
            if (fields[i + 2] is not ("unspecified" or "unset"))
                throw new InvalidOperationException("Custom Git content filters or ident expansion are not supported for verified help commits. Remove the attribute for " + fields[i] + " and rebuild before committing.");
    }

    private async Task RefreshCommittedIndexAsync(string index, string mergeIndex, Dictionary<string, string> original,
        Dictionary<string, string> committed, Func<string, bool> inScope, string branch, string commit, CancellationToken ct)
    {
        string indexLock = index + ".lock";
        bool ownsLock = false;
        try
        {
            using (var guard = new FileStream(indexLock, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsLock = true;
                await RequireCommittedHeadAsync();
                File.Copy(index, mergeIndex);
                var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = mergeIndex };
                var current = await IndexEntriesAsync(environment, ct);
                var records = new StringBuilder();
                foreach (string path in original.Keys.Union(committed.Keys).Where(inScope))
                {
                    // Preserve anything another Git process staged meanwhile,
                    // even within source. Update only still-unchanged entries.
                    if (current.GetValueOrDefault(path) != original.GetValueOrDefault(path)) continue;
                    records.Append(committed.GetValueOrDefault(path) ?? "0 " + new string('0', commit.Length) + "\t" + path).Append('\0');
                }
                if (records.Length > 0)
                    await RunAsync(RepositoryRoot, ["update-index", "-z", "--index-info"], ct, environment: environment, input: records.ToString());
                byte[] bytes = await File.ReadAllBytesAsync(mergeIndex, ct);
                await RequireCommittedHeadAsync();
                await guard.WriteAsync(bytes, ct); guard.Flush(flushToDisk: true);
            }
            File.Move(indexLock, index, overwrite: true);
            ownsLock = false;
        }
        finally { if (ownsLock && File.Exists(indexLock)) File.Delete(indexLock); }
        async Task RequireCommittedHeadAsync()
        {
            string currentBranch = (await RunAsync(RepositoryRoot, ["symbolic-ref", "--quiet", "HEAD"], ct)).Output.Trim();
            string currentHead = (await RunAsync(RepositoryRoot, ["rev-parse", "HEAD"], ct)).Output.Trim();
            if (currentBranch != branch || currentHead != commit)
                throw new InvalidOperationException("The branch changed after committing; its staging was preserved.");
        }
    }
}
