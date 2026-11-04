using System.Text;

namespace StatsDirect.HelpEditor.Core;

public sealed partial class GitSyncService
{
    private const string SharedPath = "Published";
    private Dictionary<string, string>? originalSharedFiles;
    private Dictionary<string, string>? preparedSharedFiles;
    private string? preparedSharedManifestSha256;

    private static bool IsSharedPath(string path) => path == SharedPath || path.StartsWith(SharedPath + "/", StringComparison.Ordinal);
    private bool IsWebsiteCommitPath(string path) => IsWebsitePath(path) || preparedSharedFiles != null && IsSharedPath(path);
    private string[] WebsiteCommitScope => preparedSharedFiles == null
        ? ScopedSourcePaths.Append(WebsitePath).ToArray() : [.. ScopedSourcePaths, WebsitePath, SharedPath];
    private IEnumerable<string> PreparedSharedPaths => preparedSharedFiles?.Keys.Select(path => SharedPath + "/" + path) ?? [];

    private Dictionary<string, string> SharedFiles() => Directory.Exists(SafePaths.Resolve(RepositoryRoot, SharedPath))
        ? CheckedFiles(SafePaths.Resolve(RepositoryRoot, SharedPath)).ToDictionary(
            path => Path.GetRelativePath(SafePaths.Resolve(RepositoryRoot, SharedPath), path).Replace('\\', '/'), FileHash, StringComparer.Ordinal)
        : new(StringComparer.Ordinal);

    // Capture before Docs is replaced: the preceding publication's website
    // inventory must still be valid at this point, as well as its shared data.
    private void CaptureSharedBeforeWebsitePreparation(bool prepareSharedContent)
    {
        if (preparedSharedFiles != null) throw new InvalidOperationException("Shared content is already prepared. Start a new publication to rebuild it.");
        var current = SharedFiles();
        if (current.Count != 0 && !prepareSharedContent)
            throw new InvalidOperationException("This repository publishes help and tutor content together. Use Publish content; the existing website was not changed.");
        if (originalSharedFiles != null)
        {
            RequireSameShared(originalSharedFiles, current, "Published content changed while preparing the website.");
            return;
        }
        if (current.Count != 0) SharedContentPublisher.Verify(RepositoryRoot);
        originalSharedFiles = current;
    }

    /// <summary>Stage a fully prepared shared bundle only in a reviewed, private publication clone.</summary>
    public async Task PrepareSharedContentAsync(string preparedPublishedDirectory,
        IReadOnlyDictionary<string, string> expectedFiles, CancellationToken ct = default)
    {
        if (!publicationLayout || factoryCloneRoot == null || !SafePaths.Same(factoryCloneRoot, RepositoryRoot) || publicationBaseCommit == null || expectedPublicationOrigin == null || expectedPublicationBranch == null ||
            !Directory.Exists(Path.Combine(RepositoryRoot, ".git")))
            throw new InvalidOperationException("Shared publication requires the private checkout created by the publication workflow.");
        if (originalSharedFiles == null) throw new InvalidOperationException("Prepare the matching website before shared content.");
        if (preparedSharedFiles != null) throw new InvalidOperationException("Shared content has already been prepared.");
        await RequireRepositoryAsync(ct); await RequireOriginAsync(ct);
        preparedPublishedDirectory = Path.GetFullPath(preparedPublishedDirectory);
        SafePaths.CheckNoLinks(preparedPublishedDirectory);
        string destination = SafePaths.Resolve(RepositoryRoot, SharedPath);
        if (SafePaths.IsWithin(preparedPublishedDirectory, destination) || SafePaths.IsWithin(destination, preparedPublishedDirectory))
            throw new InvalidOperationException("Prepared content must be separate from the publication checkout.");
        Dictionary<string, string> InputFiles() => CheckedFiles(preparedPublishedDirectory).ToDictionary(
            path => Path.GetRelativePath(preparedPublishedDirectory, path).Replace('\\', '/'), FileHash, StringComparer.Ordinal);
        AssertExpectedFiles(expectedFiles, InputFiles(), "Prepared shared content changed before publication.");
        if (!expectedFiles.ContainsKey("content-manifest.json")) throw new InvalidDataException("The shared manifest is missing.");
        if (expectedFiles.ContainsKey(".gitattributes") && File.ReadAllText(Path.Combine(preparedPublishedDirectory, ".gitattributes")) != "* -text\n")
            throw new InvalidDataException("Shared Git attributes must preserve the exact prepared bytes.");
        RequireSameShared(originalSharedFiles, SharedFiles(), "Existing Published content changed during review.");

        string gitDirectory = Path.Combine(RepositoryRoot, ".git");
        string staging = SafePaths.Resolve(gitDirectory, "statsdirect-shared-staging-" + Guid.NewGuid().ToString("N"));
        string backup = SafePaths.Resolve(gitDirectory, "statsdirect-shared-before-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        bool installed = false, movedPrevious = false, completed = false;
        Dictionary<string, string>? installedFiles = null;
        try
        {
            foreach (string relative in expectedFiles.Keys)
            {
                ct.ThrowIfCancellationRequested();
                string target = SafePaths.Resolve(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(SafePaths.Resolve(preparedPublishedDirectory, relative), target);
            }
            AssertExpectedFiles(expectedFiles, InputFiles(), "Prepared shared content changed while it was copied.");
            var staged = CheckedFiles(staging).ToDictionary(path => Path.GetRelativePath(staging, path).Replace('\\', '/'), FileHash, StringComparer.Ordinal);
            AssertExpectedFiles(expectedFiles, staged, "Staged shared content differs from its prepared inventory.");
            SafePaths.AtomicWrite(Path.Combine(staging, ".gitattributes"), "* -text\n");
            staged[".gitattributes"] = FileHash(Path.Combine(staging, ".gitattributes"));
            installedFiles = staged;
            RequireSameShared(originalSharedFiles, SharedFiles(), "Existing Published content changed before replacement.");
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(destination)) { Directory.Move(destination, backup); movedPrevious = true; }
            Directory.Move(staging, destination); installed = true;
            var verified = SharedContentPublisher.Verify(RepositoryRoot, staged["content-manifest.json"]);
            var installedSnapshot = SharedFiles();
            RequireSameShared(installedFiles, installedSnapshot, "Shared content changed while it was activated in the private checkout.");
            preparedSharedFiles = installedSnapshot;
            preparedSharedManifestSha256 = verified.ManifestSha256;
            completed = true;
        }
        catch
        {
            if (installed)
            {
                // Preserve unexpected concurrent files as well as the previous
                // bundle if a private checkout was changed during verification.
                RequireSameShared(installedFiles!, SharedFiles(), "Shared staging changed during recovery; the previous bundle remains in private Git recovery storage.");
                SafePaths.CheckNoLinks(destination); Directory.Delete(destination, true);
            }
            if (movedPrevious) Directory.Move(backup, destination);
            preparedSharedFiles = null; preparedSharedManifestSha256 = null;
            throw;
        }
        finally
        {
            foreach (string owned in completed ? new[] { staging, backup } : new[] { staging })
            {
                if (!Directory.Exists(owned)) continue;
                if (!SafePaths.IsWithin(owned, gitDirectory)) throw new IOException("Shared staging escaped its private Git directory.");
                SafePaths.CheckNoLinks(owned); Directory.Delete(owned, true);
            }
        }
    }

    private void VerifySharedForCommit()
    {
        if (preparedSharedFiles == null)
        {
            if (SharedFiles().Count != 0) throw new InvalidOperationException("This repository has shared desktop and tutor content. Use Publish content to update it with the website.");
            return;
        }
        RequireSameShared(preparedSharedFiles, SharedFiles(), "Prepared shared files changed before committing.");
        SharedContentPublisher.Verify(RepositoryRoot, preparedSharedManifestSha256);
    }

    private bool VerifySharedForPush()
    {
        if (preparedSharedFiles != null) VerifySharedForCommit();
        else if (SharedFiles().Count != 0) SharedContentPublisher.Verify(RepositoryRoot);
        else return false;
        return true;
    }

    private static void RequireSameShared(IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual, string message)
    {
        if (expected.Count != actual.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out var hash) ||
            !hash.Equals(pair.Value, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException(message);
    }
}
