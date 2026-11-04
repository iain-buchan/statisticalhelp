using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

[assembly: InternalsVisibleTo("StatsDirectHelpEditor.HelpPublicationChecks")]

namespace StatsDirect.HelpEditor.Core;

public sealed record HelpPublicationOptions
{
    public string WebsiteZipPath { get; init; } = "";
    public string GitHubRepositoryUrl { get; init; } = "";
    public string GitHubBranch { get; init; } = "";
    public string AuthorName { get; init; } = "";
    public string AuthorEmail { get; init; } = "";
    public string StatsDirectSource { get; init; } = "";
    public string? ExpectedDesktopSha256 { get; init; }
    public bool AdoptExistingFlareOutput { get; init; }
    public string? AdoptionReason { get; init; }
    public bool AllowBuildWarnings { get; init; }
    public bool AllowDefaultBranchPublication { get; init; }
    public bool PushToGitHub { get; init; } = true;
    public string? JournalDirectory { get; init; }
    public bool PublishSharedContent { get; init; }
    public string? TutorCoursePath { get; init; }
    public string CommitMessage { get; init; } = "Publish saved help source and rebuilt website";
}

public sealed record HelpPublicationProgress(string Phase, string Message);
public sealed record HelpPublicationResult(bool Completed, string SourceSha256,
    WebsiteZipExportResult? WebsiteZip, DesktopHelpPublishResult? DesktopHelp,
    string? GitCommit, bool GitPushed, string GitHubBranch, string PagesDeploymentState,
    string JournalPath, string? SnapshotDirectory, IReadOnlyDictionary<string, string> TargetStates)
{
    public string? WebsiteZipSha256 { get; init; }
    public TutorBuiltinPublishResult? Tutor { get; init; }
    public string? TutorSourceSha256 { get; init; }
    public string? SharedContentManifestPath { get; init; }
    public string? SharedContentManifestSha256 { get; init; }
}
public sealed class HelpPublicationException(string message, HelpPublicationResult result, Exception innerException)
    : IOException(message, innerException)
{ public HelpPublicationResult Result { get; } = result; }

/// <summary>One saved snapshot supplies the website ZIP, Git website/source commit and application desktop help.</summary>
public static class HelpPublicationPublisher
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private const string OwnedMarker = ".statsdirect-help-publication-owned.json";
    private static readonly string[] SourcePaths = ["Content", "Project", "RCode", "StatsDirect.flprj"];
    private const string PublicationInfo = "resources/help-editor-runtime/publication.json";

    public static Task<HelpPublicationResult> PublishAsync(HelpProject project, string runtimeAssetsDirectory,
        HelpPublicationOptions options, CancellationToken cancellationToken = default,
        IProgress<HelpPublicationProgress>? progress = null) =>
        Task.Run(() => PublishCoreAsync(project, runtimeAssetsDirectory, options, false, cancellationToken, progress), cancellationToken);

    // The normal API/CLI never accepts filesystem or file:// remotes.
    internal static Task<HelpPublicationResult> PublishLocalForTestsAsync(HelpProject project, string runtimeAssetsDirectory,
        HelpPublicationOptions options, CancellationToken cancellationToken = default,
        IProgress<HelpPublicationProgress>? progress = null) =>
        Task.Run(() => PublishCoreAsync(project, runtimeAssetsDirectory, options, true, cancellationToken, progress), cancellationToken);

    private static async Task<HelpPublicationResult> PublishCoreAsync(HelpProject project, string runtimeAssetsDirectory,
        HelpPublicationOptions options, bool localTestRemote, CancellationToken ct, IProgress<HelpPublicationProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(options);
        string assets = Path.GetFullPath(runtimeAssetsDirectory), zip = ValidateZip(project, assets, options);
        ValidateOptions(options, localTestRemote);
        Dictionary<string,string>? originalTutor = null;
        if(options.PublishSharedContent)
        {
            if(string.IsNullOrWhiteSpace(options.TutorCoursePath)) throw new ArgumentException("Choose the saved tutor course to publish with help.");
            var tutorInspection=SharedContentPublisher.InspectTutor(options.TutorCoursePath,options.StatsDirectSource,assets);
            if(!tutorInspection.CanPublish) throw new InvalidDataException(string.Join("\n",tutorInspection.ReviewIssues.Select(issue=>issue.Path+": "+issue.Message)));
            originalTutor=SharedContentPublisher.InspectTutorDestination(options.StatsDirectSource);
        }
        var inspected = DesktopHelpPublisher.InspectDestination(options.StatsDirectSource);
        if(options.PublishSharedContent)
        {
            string tutorPath=Path.GetFullPath(options.TutorCoursePath!);
            if(SafePaths.Same(tutorPath,zip) || SafePaths.IsWithin(tutorPath,project.RootPath) || SafePaths.IsWithin(tutorPath,assets) ||
                SafePaths.IsWithin(tutorPath,inspected.DestinationDirectory) || SafePaths.IsWithin(tutorPath,Path.Combine(inspected.RepositoryDirectory,"Learning/AuthoredTutor")))
                throw new InvalidOperationException("Keep saved tutor authoring source outside help/runtime inputs and publication outputs; only its six generated tutor files may be published.");
        }
        if (inspected.Ownership == DesktopHelpOwnership.Unrecognised)
            throw new IOException(string.Join("\n", inspected.Problems));
        if (inspected.FileCount > 0 && !string.Equals(inspected.Sha256, options.ExpectedDesktopSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Review the existing desktop help and provide its exact inspected hash before publishing all help.");
        if (inspected.Ownership == DesktopHelpOwnership.FlareNeedsAdoption &&
            (!options.AdoptExistingFlareOutput || string.IsNullOrWhiteSpace(options.AdoptionReason) || options.AdoptionReason.Length > 2000))
            throw new InvalidOperationException("The existing Flare desktop output needs explicit adoption and review notes.");
        string? originalZipSha256 = File.Exists(zip) ? Hash(zip) : null;
        ct.ThrowIfCancellationRequested();

        string history = Path.GetFullPath(options.JournalDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatsDirect", "HelpEditor", "PublicationHistory"));
        if (SafePaths.IsWithin(history, project.RootPath) || SafePaths.IsWithin(history, assets) || SafePaths.IsWithin(history, options.StatsDirectSource))
            throw new InvalidOperationException("Publication history must be separate from source, runtime assets and the StatsDirect repository.");
        WorkspaceProtectionSettings.Load().RequireUnprotected(history, "Publication history"); SafePaths.CheckNoLinks(history);
        Directory.CreateDirectory(history);
        string operation = Guid.NewGuid().ToString("N"), journalPath = SafePaths.Resolve(history, operation + ".json");
        string tempParent = WorkspaceProtectionSettings.Load().FindSafeTemporaryParent(project.RootPath, assets, options.StatsDirectSource, history);
        string work = Path.Combine(tempParent, "sdhe-pub-" + operation[..12]);
        Directory.CreateDirectory(work); SafePaths.AtomicWrite(Path.Combine(work, OwnedMarker), JsonSerializer.Serialize(new { operation, journalPath }));
        var states = new Dictionary<string, string>(StringComparer.Ordinal) { ["WebsiteZip"] = "Not started", ["DesktopHelp"] = "Not started", ["GitHub"] = "Not started" };
        if(options.PublishSharedContent){states["Tutor"]="Not started";states["SharedContent"]="Not started";}
        string sourceSha = "", pages = "Not published", phase = "Starting";
        WebsiteZipExportResult? zipResult = null; DesktopHelpPublishResult? desktopResult = null;
        string? commit = null, zipSha = null; bool pushed = false, completed = false;
        Snapshot? snapshot = null;
        SharedContentPublisher.PreparedTutorArtifact? preparedTutor = null;
        TutorBuiltinPublishResult? tutorResult = null;
        SharedContentVerification? shared = null;
        HelpPublicationResult Result() => new(completed, sourceSha, zipResult, desktopResult, commit, pushed,
            options.GitHubBranch, pages, journalPath, Directory.Exists(work) ? work : null,
            new Dictionary<string, string>(states, StringComparer.Ordinal)) { WebsiteZipSha256 = zipSha, Tutor = tutorResult,
                TutorSourceSha256 = preparedTutor?.SourceSha256, SharedContentManifestPath = shared == null ? null : SharedContentPublisher.ManifestRelativePath,
                SharedContentManifestSha256 = shared?.ManifestSha256 };
        void Journal(string nextPhase, string message)
        {
            phase = nextPhase;
            SafePaths.AtomicWrite(journalPath, JsonSerializer.Serialize(new
            {
                SchemaVersion = 1, Operation = operation, UpdatedAtUtc = DateTimeOffset.UtcNow, Phase = phase, Message = message,
                SourceProject = project.RootPath, RuntimeAssets = assets, Options = options, Result = Result(),
                OriginalWebsiteZipSha256 = originalZipSha256,
                SourceFiles = snapshot?.SourceFiles, RuntimeFiles = snapshot?.RuntimeFiles
            }, Json));
            progress?.Report(new(nextPhase, message));
        }

        try
        {
            Journal("Snapshot", "Capturing one saved help source and offline runtime snapshot; open drafts are excluded.");
            snapshot = Snapshot.Capture(project, assets, work, ct);
            sourceSha = snapshot.SourceSha256;
            if(options.PublishSharedContent)
            {
                Journal("TutorSnapshot","Validating and exporting the saved tutor course once; changed/new material needs its recorded author review.");
                preparedTutor=SharedContentPublisher.PrepareTutor(options.TutorCoursePath!,options.StatsDirectSource,assets,work);
                states["Tutor"]="Prepared from saved course; not delivered"; snapshot.RequireUnchanged(ct);
            }
            Journal("Clone", "Preparing a private checkout of the selected GitHub branch; your working checkout is unchanged.");
            string checkout = Path.Combine(work, "Git");
            GitPublicationCheckout cloned = localTestRemote
                ? await GitSyncService.CloneLocalPublicationForTestsAsync(options.GitHubRepositoryUrl, options.GitHubBranch, checkout, ct)
                : await GitSyncService.ClonePublicationAsync(options.GitHubRepositoryUrl, options.GitHubBranch, checkout, ct);
            MirrorSavedSource(snapshot, cloned.Project.RootPath, ct);
            var git = new GitSyncService(cloned.Project, cloned.Layout);
            snapshot.RequireUnchanged(ct);
            Journal("Build", "Building Public and Desktop once from the frozen snapshot, before replacing any delivery target.");
            var publicBuild = await Task.Run(() => PreparedHelpArtifact.Build(snapshot.Project, Path.Combine(work, "Public"), snapshot.Assets,
                HelpBuildTarget.Public, sourceSha), ct);
            var desktopBuild = await Task.Run(() => PreparedHelpArtifact.Build(snapshot.Project, Path.Combine(work, "Desktop"), snapshot.Assets,
                HelpBuildTarget.Desktop, sourceSha), ct);
            snapshot.RequireUnchanged(ct);
            // Validate both complete payloads, including private-file exclusion
            // and numeric context mappings, before changing any target.
            DesktopHelpPublisher.ValidatePreparedArtifact(snapshot.Project, publicBuild, HelpBuildTarget.Public);
            DesktopHelpPublisher.ValidatePreparedArtifact(snapshot.Project, desktopBuild);
            DesktopHelpPublishResult? portable = null;
            if(options.PublishSharedContent)
            {
                portable=DesktopHelpPublisher.CreatePortableReceipt(snapshot.Project,desktopBuild.Result,snapshot.Assets,sourceSha);
                desktopBuild=desktopBuild with {Files=Files(desktopBuild.Result.OutputDirectory)};
            }
            if (!options.AllowBuildWarnings && publicBuild.Result.Warnings.Concat(desktopBuild.Result.Warnings).Any())
                throw new InvalidDataException("Review help build warnings before publication:\n" + string.Join("\n", publicBuild.Result.Warnings.Concat(desktopBuild.Result.Warnings).Distinct()));

            Journal("PrepareGit", "Preparing root Docs and its matching saved source in the private checkout.");
            await git.PrepareWebsiteFromBuildAsync(publicBuild.Result.OutputDirectory, publicBuild.Files, snapshot.SourceFiles,
                publicBuild.Result.TopicCount, publicBuild.Result.SearchDocumentCount, publicBuild.Result.Warnings, ct,
                allowWarnings: options.AllowBuildWarnings, prepareSharedContent: options.PublishSharedContent);
            if(preparedTutor!=null)
            {
                preparedTutor.Verify(); string stageRoot=Path.Combine(work,"Shared");Directory.CreateDirectory(stageRoot);
                string docs=SafePaths.Resolve(cloned.Layout.GitRoot,"Docs");SharedContentPublisher.Copy(docs,Path.Combine(stageRoot,"Docs"),Files(docs),ct);
                SharedContentPublisher.Copy(desktopBuild.Result.OutputDirectory,Path.Combine(stageRoot,"Published/DesktopHelp"),desktopBuild.Files,ct);
                SharedContentPublisher.Copy(preparedTutor.Result.OutputDirectory,Path.Combine(stageRoot,"Published/Tutor"),preparedTutor.Payload,ct);
                SafePaths.AtomicWrite(Path.Combine(stageRoot,"Published/.gitattributes"),"* -text\n");
                shared=SharedContentPublisher.WriteManifest(stageRoot,Guid.ParseExact(operation,"N").ToString("D"),sourceSha,preparedTutor.SourceSha256,publicBuild.Result,portable!,preparedTutor.Result);
                await git.PrepareSharedContentAsync(Path.Combine(stageRoot,"Published"),SharedContentPublisher.Files(Path.Combine(stageRoot,"Published")),ct);
                states["SharedContent"]="Verified source, website, portable Desktop and tutor prepared privately";
            }
            snapshot.RequireUnchanged(ct); publicBuild.Verify(); desktopBuild.Verify();
            preparedTutor?.Verify();
            await git.CommitWebsiteAsync(options.CommitMessage, options.AuthorName, options.AuthorEmail, ct);
            commit = await git.ReadHeadCommitAsync(ct); states["GitHub"] = "Source and Docs committed privately; not pushed";
            Journal("LocalTargets", "Verified source and Docs are committed privately; delivering the matching website ZIP and desktop help.");
            snapshot.RequireUnchanged(ct);
            zipResult = ExportPreparedZip(publicBuild, zip, originalZipSha256, ct);
            zipSha = Hash(zipResult.ZipPath);
            states["WebsiteZip"] = "Verified"; Journal("WebsiteZip", "Website ZIP verified and saved: " + zip);
            snapshot.RequireUnchanged(ct); desktopBuild.Verify();
            desktopResult = await Task.Run(() => DesktopHelpPublisher.PublishPrepared(snapshot.Project, options.StatsDirectSource,
                snapshot.Assets, desktopBuild, new DesktopHelpPublishOptions
                {
                    ExpectedDestinationSha256 = options.ExpectedDesktopSha256,
                    AdoptExistingFlareOutput = options.AdoptExistingFlareOutput, AdoptionReason = options.AdoptionReason,
                    AllowBuildWarnings = options.AllowBuildWarnings
                }, ct), ct);
            states["DesktopHelp"] = "Verified"; Journal("DesktopHelp", "Desktop help verified in the application source build chain.");
            if(preparedTutor!=null)
            {
                Journal("Tutor","Delivering the identical prepared tutor inputs to the Windows build chain.");
                tutorResult=SharedContentPublisher.DeliverTutor(preparedTutor,options.StatsDirectSource,originalTutor,ct);
                states["Tutor"]="Verified";Journal("Tutor","Tutor build inputs verified; no numerical code was executed.");
            }
            void VerifyDeliveredTargets()
            {
                snapshot.RequireUnchanged(ct); preparedTutor?.Verify();
                try { VerifyZip(publicBuild,zipResult.ZipPath,ct); }
                catch(Exception error) when(error is IOException or InvalidDataException) {states["WebsiteZip"]="Changed since verified delivery";throw;}
                try
                {
                    var verified=DesktopHelpPublisher.VerifyDestination(options.StatsDirectSource);
                    if(verified.ReceiptSha256!=desktopResult.ReceiptSha256 || verified.PublicationSnapshotSha256!=sourceSha)
                        throw new IOException("Delivered desktop help no longer matches this operation's receipt.");
                }
                catch(Exception error) when(error is IOException or InvalidDataException or JsonException) {states["DesktopHelp"]="Changed since verified delivery";throw;}
                if(tutorResult!=null)
                {
                    try
                    {
                        var delivered=SharedContentPublisher.InspectTutorDestination(options.StatsDirectSource);
                        if(delivered==null || !delivered.OrderBy(pair=>pair.Key).SequenceEqual(preparedTutor!.Payload.OrderBy(pair=>pair.Key)))
                            throw new IOException("Delivered tutor inputs no longer match the prepared export.");
                    }
                    catch(Exception error) when(error is IOException or InvalidDataException or JsonException) {states["Tutor"]="Changed since verified delivery";throw;}
                }
            }
            VerifyDeliveredTargets();
            if (options.PushToGitHub)
            {
                preparedTutor?.Verify();
                if(shared!=null) SharedContentPublisher.Verify(cloned.Layout.GitRoot,shared.ManifestSha256);
                Journal("Push", "All local artifacts verify. Publishing the exact reviewed source/Docs commit to the selected GitHub branch.");
                // Progress callbacks and other applications may edit a local
                // target after its delivery; recheck after the final handoff.
                VerifyDeliveredTargets();
                await git.PushPreparedWebsiteAsync(commit, options.AllowDefaultBranchPublication, ct);
                pushed = true; states["GitHub"] = "Verified source and Docs commit pushed";
                if(shared!=null) states["SharedContent"]="Verified shared manifest/Desktop/tutor committed and pushed with source and Docs";
                pages = "Commit pushed; GitHub Pages deployment has not been verified";
                Journal("Pushed", "The source and Docs commit was pushed. GitHub Pages deployment is a separate asynchronous step.");
                VerifyDeliveredTargets();
                completed = true;
            }
            else { pages = "Not pushed; private commit retained"; states["GitHub"] = "Prepared only; network publication not requested"; }
            Journal(completed ? "Completed" : "Prepared", completed
                ? "Website ZIP, desktop help and the pushed source/Docs commit all verify against one snapshot. Pages deployment remains unverified."
                : "Website ZIP and desktop help verify. The source/Docs commit is retained privately and has not been pushed.");
            if (completed)
            {
                try
                {
                    DeleteOwnedWork(work, tempParent, operation);
                    Journal("Completed", "All three publication targets completed. Temporary build checkout removed; the verification journal is retained.");
                }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
                {
                    states["TemporaryFiles"] = "Completed outputs verified; cleanup needs attention: " + cleanupError.Message;
                    try { Journal("Completed", "All three targets completed; some temporary files were retained for inspection."); }
                    catch (Exception historyError) when (historyError is IOException or UnauthorizedAccessException) { }
                }
            }
            return Result();
        }
        catch (Exception error)
        {
            try { Journal("Failed", "Stopped during " + phase + ": " + error.Message + " Existing completed targets are recorded; the private snapshot is retained for inspection/retry."); }
            catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException) { }
            throw new HelpPublicationException("Help publication did not complete. " + error.Message + "\nReview the saved target states and snapshot: " + journalPath, Result(), error);
        }
    }

    private static void ValidateOptions(HelpPublicationOptions options, bool localRemote)
    {
        if (!localRemote && (!Uri.TryCreate(options.GitHubRepositoryUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || !System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"\A/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?\z")))
            throw new ArgumentException("Choose an HTTPS GitHub repository URL without credentials or a query.");
        if (string.IsNullOrWhiteSpace(options.GitHubBranch) || options.GitHubBranch.StartsWith('-') || options.GitHubBranch.Any(char.IsControl))
            throw new ArgumentException("Choose the GitHub branch to publish.");
        if (string.IsNullOrWhiteSpace(options.AuthorName) || options.AuthorName.IndexOfAny(['\r', '\n', '<', '>']) >= 0 ||
            !System.Text.RegularExpressions.Regex.IsMatch(options.AuthorEmail, @"\A[^\s<>@]+@[^\s<>@]+\z"))
            throw new ArgumentException("Enter a valid Git author name and email address.");
        if (string.IsNullOrWhiteSpace(options.CommitMessage)) throw new ArgumentException("Enter a publication commit message.");
        if (options.PushToGitHub && !options.AllowDefaultBranchPublication && options.GitHubBranch is "main" or "master")
            throw new InvalidOperationException("Publishing the default branch needs the explicit default-branch publication option.");
    }

    private static string ValidateZip(HelpProject project, string assets, HelpPublicationOptions options)
    {
        if (!Path.IsPathFullyQualified(options.WebsiteZipPath) || !Path.GetExtension(options.WebsiteZipPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a full website ZIP filename.");
        string zip = Path.GetFullPath(options.WebsiteZipPath); SafePaths.CheckNoLinks(zip);
        if (SafePaths.IsWithin(zip, project.RootPath) || SafePaths.IsWithin(zip, assets) || SafePaths.IsWithin(zip, options.StatsDirectSource))
            throw new InvalidOperationException("Save the website ZIP outside help source, runtime assets and the StatsDirect source repository.");
        if (Directory.Exists(zip)) throw new IOException("The website ZIP filename is occupied by a directory.");
        return zip;
    }

    private static void MirrorSavedSource(Snapshot snapshot, string destination, CancellationToken ct)
    {
        // Only a fresh private clone is passed here. The user checkout/source is never mirrored in place.
        foreach (string scope in SourcePaths)
        {
            string target = SafePaths.Resolve(destination, scope);
            if (Directory.Exists(target)) DeleteCheckedTree(target); else if (File.Exists(target)) File.Delete(target);
        }
        foreach (var file in snapshot.SourceFiles)
        {
            ct.ThrowIfCancellationRequested(); string target = SafePaths.Resolve(destination, file.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(SafePaths.Resolve(snapshot.Project.RootPath, file.Key), target);
        }
        RequireFiles(snapshot.SourceFiles, SourceFiles(destination), "The private Git source does not match the saved snapshot.");
    }

    private static WebsiteZipExportResult ExportPreparedZip(PreparedHelpArtifact artifact, string destination, string? expectedBefore, CancellationToken ct)
    {
        artifact.Verify(); string parent = Path.GetDirectoryName(destination)!; SafePaths.CheckNoLinks(parent); Directory.CreateDirectory(parent);
        string temporary = Path.Combine(parent, ".sdhe-publication-" + Guid.NewGuid().ToString("N") + ".tmp");
        var files = artifact.Files.Where(pair => ReaderFile(pair.Key)).ToArray();
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested(); var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                    using var content = entry.Open(); using var input = File.OpenRead(SafePaths.Resolve(artifact.Result.OutputDirectory, file.Key)); input.CopyTo(content);
                }
            VerifyZip(artifact, temporary, ct); artifact.Verify(); ct.ThrowIfCancellationRequested(); SafePaths.CheckNoLinks(destination);
            if ((File.Exists(destination) ? Hash(destination) : null) != expectedBefore) throw new IOException("The selected website ZIP changed while publication was being prepared. It was preserved.");
            File.Move(temporary, destination, true);
            return new(destination, artifact.Result.TopicCount, artifact.Result.SearchDocumentCount, files.Length, new FileInfo(destination).Length, artifact.Result.Warnings);
        }
        finally { if (File.Exists(temporary)) { SafePaths.CheckNoLinks(temporary); File.Delete(temporary); } }
    }

    private static void VerifyZip(PreparedHelpArtifact artifact, string path, CancellationToken ct)
    {
        var expected = artifact.Files.Where(pair => ReaderFile(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        using var zip = ZipFile.OpenRead(path); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (!seen.Add(entry.FullName) || !expected.Remove(entry.FullName, out string? sha)) throw new InvalidDataException("The website ZIP contains an unexpected/duplicate reader file.");
            using var stream = entry.Open(); if (Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() != sha) throw new InvalidDataException("The website ZIP reader bytes differ from the prepared Public artifact.");
        }
        if (expected.Count != 0) throw new InvalidDataException("The website ZIP is missing prepared Public reader files.");
    }

    // Must match the generated Git reader files: builder/website ownership markers are private, not website payload.
    internal static bool ReaderFile(string path) => path is not (".statsdirect-help-output.json" or ".statsdirect-help-website.json" or ".gitattributes" or "CNAME" or ".nojekyll");
    internal static Dictionary<string, string> Files(string root, bool skipPersonalProject = false)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(root)) return result;
        void Walk(string directory)
        {
            SafePaths.CheckNoLinks(directory);
            foreach (string file in Directory.EnumerateFiles(directory))
            { SafePaths.CheckNoLinks(file); result.Add(Path.GetRelativePath(root, file).Replace('\\', '/'), Hash(file)); }
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (skipPersonalProject && SafePaths.Same(directory, root) &&
                    new[] { "Users", "Destinations" }.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)) continue;
                Walk(child);
            }
        }
        Walk(root); return result;
    }
    private static Dictionary<string, string> SourceFiles(string source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string scope in SourcePaths)
        {
            string path = SafePaths.Resolve(source, scope);
            if (File.Exists(path)) result.Add(scope, Hash(path));
            else foreach (var file in Files(path, skipPersonalProject: scope == "Project"))
            {
                string relative = scope + "/" + file.Key;
                if (relative.StartsWith("Project/Users/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("Project/Destinations/", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(relative, file.Value);
            }
        }
        return result;
    }
    internal static string Hash(string file) { SafePaths.CheckNoLinks(file); using var input = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(); }
    internal static void RequireFiles(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual, string message)
    { if (expected.Count != actual.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out string? hash) || hash != pair.Value)) throw new IOException(message); }
    private static void DeleteCheckedTree(string path)
    {
        SafePaths.CheckNoLinks(path);
        foreach (string child in Directory.EnumerateDirectories(path)) DeleteCheckedTree(child);
        foreach (string file in Directory.EnumerateFiles(path)) { SafePaths.CheckNoLinks(file); File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly); File.Delete(file); }
        Directory.Delete(path);
    }
    private static void DeleteOwnedWork(string work, string parent, string operation)
    {
        if (!SafePaths.Same(Path.GetDirectoryName(work)!, parent) || Path.GetFileName(work) != "sdhe-pub-" + operation[..12]) throw new IOException("Publication staging ownership is invalid.");
        using var marker = JsonDocument.Parse(File.ReadAllText(SafePaths.Resolve(work, OwnedMarker)));
        if (marker.RootElement.GetProperty("operation").GetString() != operation) throw new IOException("Publication staging marker changed; retained for review.");
        DeleteCheckedTree(work);
    }

    private sealed record Snapshot(HelpProject Project, string Assets, string OriginalSource, string OriginalAssets,
        IReadOnlyDictionary<string, string> SourceFiles, IReadOnlyDictionary<string, string> RuntimeFiles, string SourceSha256)
    {
        internal static Snapshot Capture(HelpProject project, string assets, string work, CancellationToken ct)
        {
            var sourceFiles = HelpPublicationPublisher.SourceFiles(project.RootPath); var runtimeFiles = Files(assets);
            if (!sourceFiles.Keys.Any(path => path.StartsWith("Content/")) || runtimeFiles.Count == 0) throw new InvalidDataException("Saved help source or offline runtime assets are missing.");
            if (sourceFiles.Keys.Any(path => path.Split('/').Any(part =>
                new[] { ".git", ".codex", ".help-editor-recovery", ".editor-profile", ".tutor-preview-profile", "auth.json", "credentials.json", "account-state.json", "authoring-preferences.json" }.Contains(part, StringComparer.OrdinalIgnoreCase)) ||
                new[] { ".pfx", ".p12", ".pem", ".key" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
                throw new InvalidDataException("Move private account, recovery or certificate data outside the authored help inputs before publishing.");
            string source = Path.Combine(work, "Source"), runtime = Path.Combine(work, "Runtime");
            Copy(project.RootPath, source, sourceFiles); Copy(assets, runtime, runtimeFiles);
            SafePaths.AtomicWrite(Path.Combine(source, HelpProject.MarkerFile), "{\"prototypeWorkingCopy\":true,\"publicationSnapshot\":true}");
            string inventory = string.Join('\n', sourceFiles.Select(pair => ("Source/" + pair.Key, pair.Value))
                .Concat(runtimeFiles.Select(pair => ("Runtime/" + pair.Key, pair.Value)))
                .Append(("Builder/StatsDirectHelpEditor.Core.dll", Hash(typeof(HelpBuilder).Assembly.Location)))
                .OrderBy(pair => pair.Item1, StringComparer.Ordinal).Select(pair => pair.Item1 + "\0" + pair.Item2));
            var snapshot = new Snapshot(HelpProject.Open(source), runtime, project.RootPath, assets, sourceFiles, runtimeFiles,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inventory))).ToLowerInvariant());
            snapshot.RequireUnchanged(ct); return snapshot;
            void Copy(string origin, string destination, IReadOnlyDictionary<string, string> manifest)
            {
                foreach (var file in manifest)
                {
                    ct.ThrowIfCancellationRequested(); string target = SafePaths.Resolve(destination, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var input = new FileStream(SafePaths.Resolve(origin, file.Key), FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None); input.CopyTo(output);
                }
            }
        }
        internal void RequireUnchanged(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            RequireFiles(SourceFiles, HelpPublicationPublisher.SourceFiles(OriginalSource), "Saved help changed since the publication snapshot was captured.");
            RequireFiles(RuntimeFiles, Files(OriginalAssets), "Offline runtime assets changed since the publication snapshot was captured.");
            RequireFiles(SourceFiles, HelpPublicationPublisher.SourceFiles(Project.RootPath), "The private saved-source snapshot changed.");
            RequireFiles(RuntimeFiles, Files(Assets), "The private offline runtime snapshot changed.");
        }
    }

    internal sealed record PreparedHelpArtifact(BuildResult Result, IReadOnlyDictionary<string, string> Files, string SourceSha256)
    {
        internal static PreparedHelpArtifact Build(HelpProject source, string output, string assets, HelpBuildTarget target, string digest)
        {
            var result = HelpBuilder.Build(source, output, assets, target);
            string info = SafePaths.Resolve(output, PublicationInfo);
            if (File.Exists(info)) throw new InvalidDataException("The generated publication information path is already occupied.");
            Directory.CreateDirectory(Path.GetDirectoryName(info)!);
            File.WriteAllText(info, JsonSerializer.Serialize(new { schemaVersion = 1, sourceSha256 = digest, target = target.ToString() }, Json), new UTF8Encoding(false));
            string marker = SafePaths.Resolve(output, ".statsdirect-help-output.json");
            var ownership = JsonNode.Parse(File.ReadAllText(marker))!.AsObject(); ownership["files"]!.AsArray().Add(PublicationInfo);
            File.WriteAllText(marker, ownership.ToJsonString(), new UTF8Encoding(false));
            return new(result, HelpPublicationPublisher.Files(output), digest);
        }
        internal void Verify() => RequireFiles(Files, HelpPublicationPublisher.Files(Result.OutputDirectory), "A prepared help artifact changed before publication.");
    }
}
