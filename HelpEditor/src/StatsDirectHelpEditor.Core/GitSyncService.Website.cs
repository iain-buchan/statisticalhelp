using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StatsDirect.HelpEditor.Core;

public record WebsitePublication(string OutputDirectory, int TopicCount, int SearchDocumentCount, IReadOnlyList<string> Warnings);

public sealed partial class GitSyncService
{
    private const string WebsitePath = "Docs";
    private const string WebsiteReceiptName = ".statsdirect-help-website.json";
    private const string BuilderReceiptName = ".statsdirect-help-output.json";
    private static readonly string[] WebsiteMetadata = ["CNAME", ".nojekyll"];

    public static bool IsWebsitePath(string path) => path.Replace('\\', '/') is var clean &&
        (clean.Equals(WebsitePath, StringComparison.Ordinal) || clean.StartsWith(WebsitePath + "/", StringComparison.Ordinal));

    /// <summary>Rebuild Public help before committing. Only the independent checkout's Docs is replaced.</summary>
    public async Task<WebsitePublication> PrepareWebsiteAsync(string runtimeAssetsDirectory, CancellationToken ct = default)
    {
        var status = await RequireRepositoryAsync(ct);
        await RejectOutsideStagedChangesAsync(ct, includeWebsite: true);
        var gitDirectory = await RunAsync(project.RootPath, ["rev-parse", "--absolute-git-dir"], ct);
        string pendingPath = SafePaths.Resolve(gitDirectory.Output.Trim(), "statsdirect-help-website-pending.json");
        string docs = SafePaths.Resolve(project.RootPath, WebsitePath);
        var before = WebsiteFiles(docs);
        bool previousPrepared = TryReadWebsiteReceipt(docs, out var previous) && SameFiles(before, previous!.Files);
        PendingWebsiteCopy? pending = null;
        try { if (File.Exists(pendingPath)) pending = JsonSerializer.Deserialize<PendingWebsiteCopy>(File.ReadAllText(pendingPath)); }
        catch (JsonException) { }
        bool resumable = pending?.Before != null && pending.After != null && before.All(pair => WebsiteMetadata.Contains(pair.Key, StringComparer.Ordinal) ||
            pending.Before.TryGetValue(pair.Key, out string? oldHash) && oldHash == pair.Value ||
            pending.After.TryGetValue(pair.Key, out string? newHash) && newHash == pair.Value);
        var edited = status.Changes.Where(change => IsWebsitePath(change.Path) || change.OriginalPath is { } original && IsWebsitePath(original))
            .Where(change => !WebsiteMetadata.Any(name => change.Path == WebsitePath + "/" + name)).ToArray();
        if (edited.Length > 0 && !previousPrepared && !resumable)
            throw new InvalidOperationException("Docs has changes outside a complete editor-generated website. Save those changes separately or restore them before rebuilding; nothing was overwritten.");

        var tracked = await RunAsync(project.RootPath, ["ls-files", "-z", "--", WebsitePath], ct);
        var owned = tracked.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(path => path[(WebsitePath.Length + 1)..]).ToHashSet(StringComparer.Ordinal);
        if (previousPrepared) owned.UnionWith(previous!.Files.Keys);
        if (resumable) { owned.UnionWith(pending!.Before!.Keys); owned.UnionWith(pending!.After!.Keys); }
        var unowned = before.Keys.Where(path => !owned.Contains(path) && !WebsiteMetadata.Contains(path, StringComparer.Ordinal)).ToArray();
        if (unowned.Length > 0)
            throw new InvalidOperationException("Docs contains untracked files that would be replaced. Move them elsewhere before rebuilding: " + string.Join(", ", unowned.Take(5)));

        // Git-private staging permits a failed source build to leave Docs intact.
        string staging = SafePaths.Resolve(gitDirectory.Output.Trim(), "statsdirect-help-website-build");
        string sourceHash = SourceHash();
        var built = await Task.Run(() => HelpBuilder.Build(project, staging, runtimeAssetsDirectory, HelpBuildTarget.Public), ct);
        ct.ThrowIfCancellationRequested();
        if (sourceHash != SourceHash()) throw new InvalidOperationException("Help source changed during the website build. Rebuild before committing; Docs was not changed.");
        if (!SameFiles(before, WebsiteFiles(docs))) throw new InvalidOperationException("Docs changed during the website build. Rebuild after reviewing those changes; nothing was overwritten.");

        var generated = WebsiteFiles(staging);
        generated.Remove(BuilderReceiptName);
        generated.Remove(WebsiteReceiptName);
        if (!generated.ContainsKey("index.html") || !generated.ContainsKey("search-index.js") || !generated.ContainsKey("contents-data.js") || built.TopicCount == 0)
            throw new InvalidDataException("The website build did not produce a complete site. Docs was not changed.");
        Directory.CreateDirectory(docs);
        // All paths are checked before the first mutation, including obsolete files.
        foreach (string path in before.Keys.Union(generated.Keys)) SafePaths.Resolve(docs, path);
        // This is ownership evidence for retry, never a publishable completion receipt.
        var intended = new Dictionary<string, string>(generated, StringComparer.Ordinal);
        if (!before.ContainsKey(".nojekyll")) intended[".nojekyll"] = Convert.ToHexString(SHA256.HashData([]));
        SafePaths.AtomicWrite(pendingPath, JsonSerializer.Serialize(new PendingWebsiteCopy(before, intended)));
        foreach (string path in generated.Keys)
        {
            ct.ThrowIfCancellationRequested();
            string destination = SafePaths.Resolve(docs, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temporary = destination + ".helpeditor-publish-" + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.Copy(SafePaths.Resolve(staging, path), temporary); File.Move(temporary, destination, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        foreach (string path in before.Keys.Except(generated.Keys).Where(path => !WebsiteMetadata.Contains(path, StringComparer.Ordinal)))
            File.Delete(SafePaths.Resolve(docs, path));
        if (!File.Exists(Path.Combine(docs, ".nojekyll"))) SafePaths.AtomicWrite(Path.Combine(docs, ".nojekyll"), "");
        ct.ThrowIfCancellationRequested();
        if (sourceHash != SourceHash()) throw new InvalidOperationException("Source changed while the website was copied. Rebuild before committing or pushing.");
        var receipt = new WebsiteReceipt(1, sourceHash, built.TopicCount, WebsiteFiles(docs));
        SafePaths.AtomicWrite(Path.Combine(docs, WebsiteReceiptName), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        File.Delete(pendingPath);
        return new WebsitePublication(docs, built.TopicCount, built.SearchDocumentCount, built.Warnings);
    }

    /// <summary>Saved source and its freshly generated website enter the same commit.</summary>
    public async Task<string> CommitWebsiteAsync(string message, string authorName, string authorEmail, CancellationToken ct = default)
    {
        await RequireRepositoryAsync(ct);
        VerifyWebsite();
        return await CommitScopedAsync(message, authorName, authorEmail, includeWebsite: true, ct);
    }

    /// <summary>A working-branch push is ready for review; merging to master triggers the existing Pages deployment.</summary>
    public async Task<string> PushWebsiteAsync(CancellationToken ct = default)
    {
        var status = await RequireRepositoryAsync(ct);
        string verifiedCommit = (await RunAsync(project.RootPath, ["rev-parse", "HEAD"], ct)).Output.Trim();
        VerifyWebsite();
        if (status.Changes.Any(change => change.IsSource || IsWebsitePath(change.Path) || change.OriginalPath is { } old && IsWebsitePath(old)))
            throw new InvalidOperationException("Saved source or website files have uncommitted changes. Rebuild and commit the source and website together before pushing.");
        var committed = await RunAsync(project.RootPath, ["show", verifiedCommit + ":" + WebsitePath + "/" + WebsiteReceiptName], ct, allowFailure: true);
        TryReadWebsiteReceipt(SafePaths.Resolve(project.RootPath, WebsitePath), out var current);
        WebsiteReceipt? saved = null;
        try { if (committed.ExitCode == 0) saved = JsonSerializer.Deserialize<WebsiteReceipt>(committed.Output); }
        catch (JsonException) { }
        // Git can normalize CRLF/LF in JSON. Compare its data, not serialized newlines.
        if (saved?.Schema != current!.Schema || saved.SourceHash != current.SourceHash || saved.TopicCount != current.TopicCount || saved.Files is null || !SameFiles(saved.Files, current.Files))
            throw new InvalidOperationException("The current commit has no matching generated website. Rebuild and commit before pushing.");
        return await PushCoreAsync(verifiedCommit, status.Branch, ct);
    }

    private async Task VerifyWebsiteForCommitAsync(CancellationToken ct, IReadOnlyDictionary<string, string>? environment = null)
    {
        VerifyWebsite();
        // An ignored source file must not influence a site while being omitted from its commit.
        var indexed = await RunAsync(project.RootPath, ["ls-files", "-z", "--", .. SourcePaths, WebsitePath], ct, environment: environment);
        var expected = SourceFiles().Concat(WebsiteFiles(SafePaths.Resolve(project.RootPath, WebsitePath)).Keys.Select(path => WebsitePath + "/" + path))
            .Append(WebsitePath + "/" + WebsiteReceiptName).ToHashSet(StringComparer.Ordinal);
        var actual = indexed.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(actual)) throw new InvalidOperationException("Some source or website files are ignored or not staged. Check .gitignore before committing; the website and source must be saved together.");
    }

    private void VerifyWebsite()
    {
        string docs = SafePaths.Resolve(project.RootPath, WebsitePath);
        if (!TryReadWebsiteReceipt(docs, out var receipt) || receipt!.Schema != 1 || receipt.TopicCount == 0 || receipt.SourceHash != SourceHash() || !SameFiles(receipt.Files, WebsiteFiles(docs)))
            throw new InvalidOperationException("The generated GitHub website is missing, changed or out of date. Rebuild the website before committing or pushing.");
    }

    private static bool TryReadWebsiteReceipt(string docs, out WebsiteReceipt? receipt)
    {
        receipt = null;
        string path = SafePaths.Resolve(docs, WebsiteReceiptName);
        if (!File.Exists(path)) return false;
        try { receipt = JsonSerializer.Deserialize<WebsiteReceipt>(File.ReadAllText(path)); return receipt?.Files != null; }
        catch (JsonException) { return false; }
    }

    private string SourceHash()
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string path in SourceFiles())
        {
            digest.AppendData(Encoding.UTF8.GetBytes(path + "\0" + FileHash(SafePaths.Resolve(project.RootPath, path)) + "\n"));
        }
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    private IEnumerable<string> SourceFiles() => SourcePaths.SelectMany(path =>
    {
        string full = SafePaths.Resolve(project.RootPath, path);
        return Directory.Exists(full) ? CheckedFiles(full).Select(file => Path.GetRelativePath(project.RootPath, file).Replace('\\', '/'))
            : File.Exists(full) ? new[] { path } : [];
    }).Order(StringComparer.Ordinal);

    private static Dictionary<string, string> WebsiteFiles(string directory) => !Directory.Exists(directory) ? new(StringComparer.Ordinal)
        : CheckedFiles(directory).Where(file => Path.GetRelativePath(directory, file).Replace('\\', '/') != WebsiteReceiptName)
            .ToDictionary(file => Path.GetRelativePath(directory, file).Replace('\\', '/'), FileHash, StringComparer.Ordinal);

    private static IEnumerable<string> CheckedFiles(string directory)
    {
        SafePaths.CheckNoLinks(directory);
        // Never recurse through a junction before checking it.
        foreach (string file in Directory.EnumerateFiles(directory)) { SafePaths.CheckNoLinks(file); yield return file; }
        foreach (string child in Directory.EnumerateDirectories(directory))
        { SafePaths.CheckNoLinks(child); foreach (string file in CheckedFiles(child)) yield return file; }
    }

    private static string FileHash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static bool SameFiles(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) => a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out string? hash) && hash == pair.Value);
    private sealed record WebsiteReceipt(int Schema, string SourceHash, int TopicCount, Dictionary<string, string> Files);
    private sealed record PendingWebsiteCopy(Dictionary<string, string> Before, Dictionary<string, string> After);
}
