using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace StatsDirect.HelpEditor.Core;

public enum DesktopHelpOwnership { Missing, Empty, EditorOwned, EditorBuildOutput, FlareNeedsAdoption, Unrecognised }
public sealed record DesktopHelpFile(string Path, long Bytes, string Sha256);
public sealed record DesktopHelpDestination(string RepositoryDirectory, string DestinationDirectory,
    DesktopHelpOwnership Ownership, string Sha256, IReadOnlyList<DesktopHelpFile> Files,
    IReadOnlyList<string> Problems, bool ConsumerGuardInstalled)
{ public int FileCount => Files.Count; }
public sealed record DesktopHelpPublishProgress(string Phase, string Message);
public sealed record DesktopHelpPublishOptions
{
    public string? ExpectedDestinationSha256 { get; init; }
    public bool AdoptExistingFlareOutput { get; init; }
    public string? AdoptionReason { get; init; }
    public bool AllowBuildWarnings { get; init; }
}
public sealed record DesktopHelpPublishResult(string DestinationDirectory, string ReceiptPath,
    string ReceiptSha256, string SourceSha256, int TopicCount, int SearchDocumentCount, int ContextCount,
    IReadOnlyList<string> Warnings, string PendingMarkerPath, bool ConsumerGuardInstalled)
{
    public string OutputDirectory => DestinationDirectory;
    public string? PublicationSnapshotSha256 { get; init; }
}
public sealed record DesktopHelpRecoveryResult(string DestinationDirectory, string Outcome, bool PendingMarkerCleared);

/// <summary>Explicitly delivers a verified Desktop build into one selected StatsDirect source tree.</summary>
public static class DesktopHelpPublisher
{
    public const string ReceiptFileName = ".statsdirect-desktop-help.json";
    public const string PendingMarkerFileName = ".statsdirect-desktop-help.publishing.json";
    public const string ConsumerGuardRelativePath = "BuildTools/StatsDirectDesktopHelp.targets";
    private const string Format = "statsdirect-desktop-help-v1";
    private const string BuilderMarker = ".statsdirect-help-output.json";
    private const string LockName = ".statsdirect-desktop-help.lock";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    /// <summary>Import this artifact explicitly from StatsDirectUI.csproj. The publisher never modifies a project file.</summary>
    public static string ConsumerGuardXml => """
        <Project>
          <Target Name="CheckStatsDirectDesktopHelpPublication" BeforeTargets="BeforeBuild;GetCopyToOutputDirectoryItems;CopyFilesToOutputDirectory">
            <Error Condition="Exists('$(MSBuildThisFileDirectory)../.statsdirect-desktop-help.publishing.json')" Text="Desktop help publication is incomplete. Recover or finish it with StatsDirect Help Editor before building StatsDirect." />
          </Target>
        </Project>
        """ + "\n";

    public static string PendingMarkerPath(string statsDirectSource) => Path.Combine(Repository(statsDirectSource), PendingMarkerFileName);

    /// <summary>Consumers must call this or import ConsumerGuardXml; an unmodified StatsDirect build does not inspect the marker.</summary>
    public static void EnsureNoPendingPublication(string statsDirectSource)
    {
        string repo = Repository(statsDirectSource);
        string marker = Path.Combine(repo, PendingMarkerFileName);
        if (File.Exists(marker) || Directory.Exists(marker)) throw new IOException("Desktop help publication is incomplete. Recover it before consuming or rebuilding the help.");
    }

    public static DesktopHelpDestination InspectDestination(string statsDirectSource)
    {
        string repo = Repository(statsDirectSource), destination = Destination(repo);
        EnsureNoPendingPublication(repo);
        var files = Capture(destination);
        var problems = new List<string>();
        DesktopHelpOwnership kind;
        if (!Directory.Exists(destination)) kind = DesktopHelpOwnership.Missing;
        else if (files.Count == 0) kind = DesktopHelpOwnership.Empty;
        else if (File.Exists(Path.Combine(destination, ReceiptFileName)))
        {
            try { VerifyOwned(destination); kind = DesktopHelpOwnership.EditorOwned; }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException) { kind = DesktopHelpOwnership.Unrecognised; problems.Add(error.Message); }
        }
        else if (IsBuilderOutput(destination, files)) kind = DesktopHelpOwnership.EditorBuildOutput;
        else if (IsFlareOutput(destination, files)) kind = DesktopHelpOwnership.FlareNeedsAdoption;
        else { kind = DesktopHelpOwnership.Unrecognised; problems.Add("This is not a wholly attributable help output. It will not be overwritten."); }
        return new(repo, destination, kind, TreeHash(files), files, problems, HasConsumerGuard(repo));
    }

    /// <summary>Verify the complete delivered tree before a consumer uses it. Never repairs or publishes output.</summary>
    public static DesktopHelpPublishResult VerifyDestination(string statsDirectSource)
    {
        string repo = Repository(statsDirectSource), destination = Destination(repo);
        EnsureNoPendingPublication(repo); VerifyOwned(destination);
        var receipt = ReadJson<DeliveryReceipt>(Path.Combine(destination, ReceiptFileName));
        string path = Path.Combine(destination, ReceiptFileName);
        return new(destination, path, FileHash(path), receipt.SourceSha256, receipt.TopicCount,
            receipt.SearchDocumentCount, receipt.Contexts.Count, [], Path.Combine(repo, PendingMarkerFileName), HasConsumerGuard(repo))
            { PublicationSnapshotSha256 = receipt.PublicationSnapshotSha256 };
    }

    public static DesktopHelpPublishResult Publish(HelpProject project, string statsDirectSource,
        string runtimeAssetsDirectory, DesktopHelpPublishOptions? options = null,
        CancellationToken cancellationToken = default, IProgress<DesktopHelpPublishProgress>? progress = null) =>
        PublishCore(project, statsDirectSource, runtimeAssetsDirectory, null, options, cancellationToken, progress);

    internal static DesktopHelpPublishResult PublishPrepared(HelpProject project, string statsDirectSource,
        string runtimeAssetsDirectory, HelpPublicationPublisher.PreparedHelpArtifact prepared, DesktopHelpPublishOptions? options = null,
        CancellationToken cancellationToken = default) =>
        PublishCore(project, statsDirectSource, runtimeAssetsDirectory, prepared, options, cancellationToken, null);

    internal static void ValidatePreparedArtifact(HelpProject project, HelpPublicationPublisher.PreparedHelpArtifact prepared,
        HelpBuildTarget expectedTarget = HelpBuildTarget.Desktop)
    {
        prepared.Verify();
        _ = ValidateArtifact(project, prepared.Result.OutputDirectory, prepared.Result, expectedTarget);
        if (Capture(prepared.Result.OutputDirectory).Any(file => !PublicFile(file.Path)))
            throw new InvalidDataException("The prepared help artifact contains private/source or unsupported files.");
    }

    private static DesktopHelpPublishResult PublishCore(HelpProject project, string statsDirectSource,
        string runtimeAssetsDirectory, HelpPublicationPublisher.PreparedHelpArtifact? prepared, DesktopHelpPublishOptions? options,
        CancellationToken cancellationToken, IProgress<DesktopHelpPublishProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(project); options ??= new();
        string repo = Repository(statsDirectSource), destination = Destination(repo), parent = Path.GetDirectoryName(destination)!;
        using var publishLock = AcquireLock(repo);
        var before = InspectDestination(repo);
        if (before.Ownership == DesktopHelpOwnership.Unrecognised) throw new IOException(string.Join("\n", before.Problems));
        if (before.FileCount > 0 && !SameHash(options.ExpectedDestinationSha256, before.Sha256))
            throw new IOException("Review the current desktop output and pass its exact ExpectedDestinationSha256 before replacing it.");
        if (before.Ownership == DesktopHelpOwnership.FlareNeedsAdoption &&
            (!options.AdoptExistingFlareOutput || string.IsNullOrWhiteSpace(options.AdoptionReason) || options.AdoptionReason.Length > 2000))
            throw new InvalidOperationException("Replacing recognised Flare output requires explicit adoption of the inspected hash and a recorded reason.");
        cancellationToken.ThrowIfCancellationRequested();
        string sourceHash = InputsHash(project.RootPath, runtimeAssetsDirectory), projectHash = FileHash(Path.Combine(repo, "StatsDirectUI/StatsDirectUI.csproj"));
        string token = Guid.NewGuid().ToString("N");
        string temporaryParent = WorkspaceProtectionSettings.Load().FindSafeTemporaryParent(project.RootPath, repo);
        string temporary = Path.Combine(temporaryParent, "StatsDirectDesktopHelp-" + token);
        string built = Path.Combine(temporary, "Help");
        string incoming = Path.Combine(parent, ".Help-desktop-stage-" + token), previous = Path.Combine(parent, ".Help-desktop-previous-" + token);
        string marker = Path.Combine(repo, PendingMarkerFileName);
        bool markerWritten = false, activationStarted = false;
        Journal? transaction = null;
        try
        {
            progress?.Report(new("Building", "Building saved DesktopHelp into isolated staging."));
            BuildResult build;
            if (prepared == null) build = HelpBuilder.Build(project, built, runtimeAssetsDirectory, HelpBuildTarget.Desktop);
            else
            {
                prepared.Verify(); CopyTree(prepared.Result.OutputDirectory, built, cancellationToken);
                build = prepared.Result with { OutputDirectory = built }; prepared.Verify();
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (build.Warnings.Count > 0 && !options.AllowBuildWarnings) throw new InvalidDataException("Review build warnings before desktop delivery:\n" + string.Join("\n", build.Warnings));
            var contexts = ValidateArtifact(project, built, build);
            progress?.Report(new("Built", "Desktop entry, topics, search and numeric context mappings are verified."));
            AssertInputs(project, runtimeAssetsDirectory, sourceHash, repo, projectHash);
            var generated = Capture(built);
            if (generated.Any(file => !PublicFile(file.Path))) throw new InvalidDataException("The desktop build contains private/source or unsupported files; delivery was refused.");
            var receipt = new DeliveryReceipt(1, Format, DateTimeOffset.UtcNow, sourceHash, "index.html", build.TopicCount,
                build.SearchDocumentCount, contexts, SourceFileHash(project, "Project/Advanced/STATSDIRECT.flali"),
                SourceFileHash(project, "Project/Advanced/SD3ContextID.h"), before.Ownership.ToString(), before.Sha256,
                before.Ownership == DesktopHelpOwnership.FlareNeedsAdoption, generated.ToArray(), prepared?.SourceSha256);
            WriteJson(Path.Combine(built, ReceiptFileName), receipt);
            VerifyOwned(built);
            Directory.CreateDirectory(parent); SafePaths.CheckNoLinks(parent);
            if (Directory.Exists(incoming) || Directory.Exists(previous)) throw new IOException("A desktop publication staging name is already occupied.");
            CopyTree(built, incoming, cancellationToken);
            VerifyOwned(incoming);
            progress?.Report(new("Staged", "Verified desktop output is ready for activation."));
            cancellationToken.ThrowIfCancellationRequested();
            AssertInputs(project, runtimeAssetsDirectory, sourceHash, repo, projectHash);
            if (TreeHash(Capture(destination)) != before.Sha256) throw new IOException("Desktop help changed after inspection. No current output was replaced.");
            var journal = new Journal(1, Format, token, before.Ownership is not DesktopHelpOwnership.Missing,
                before.Sha256, TreeHash(Capture(incoming)), before.Files.ToArray(), Capture(incoming).ToArray(),
                options.AdoptExistingFlareOutput && before.Ownership == DesktopHelpOwnership.FlareNeedsAdoption ? options.AdoptionReason : null, temporary);
            transaction = journal;
            WriteNewMarker(marker, journal); markerWritten = true;
            progress?.Report(new("BeforeActivation", "The pending marker is set; consumers must honour their installed build guard."));
            cancellationToken.ThrowIfCancellationRequested();
            if (TreeHash(Capture(destination)) != before.Sha256) throw new IOException("Desktop help changed immediately before activation. The newer output was preserved.");
            activationStarted = true;
            if (Directory.Exists(destination)) Directory.Move(destination, previous);
            progress?.Report(new("PreviousRetired", "The previous complete output is held only for this transaction."));
            Directory.Move(incoming, destination);
            progress?.Report(new("Activated", "Verifying the activated desktop output."));
            cancellationToken.ThrowIfCancellationRequested();
            VerifyOwned(destination); AssertInputs(project, runtimeAssetsDirectory, sourceHash, repo, projectHash);
            if (TreeHash(Capture(destination)) != journal.NewSha256) throw new IOException("Activated desktop help changed before verification completed.");
            if (journal.AdoptionReason != null) WriteJson(Path.Combine(repo, ".statsdirect-desktop-help-adoption.json"),
                new { schemaVersion = 1, adoptedAtUtc = DateTimeOffset.UtcNow, previousFlareSha256 = before.Sha256, reason = journal.AdoptionReason });
            if (Directory.Exists(previous)) DeleteOwned(previous, parent, ".Help-desktop-previous-" + token, journal.PreviousFiles);
            File.Delete(marker); markerWritten = false;
            string receiptPath = Path.Combine(destination, ReceiptFileName);
            return new(destination, receiptPath, FileHash(receiptPath), sourceHash, build.TopicCount,
                build.SearchDocumentCount, contexts.Count, build.Warnings, marker, HasConsumerGuard(repo))
                { PublicationSnapshotSha256 = prepared?.SourceSha256 };
        }
        catch
        {
            if (markerWritten && !activationStarted && transaction != null)
            {
                // No destination move has happened. A newer external edit is
                // authoritative; discard only this request's verified staging.
                if (Directory.Exists(incoming)) DeleteOwned(incoming, parent, ".Help-desktop-stage-" + token, transaction.NewFiles);
                File.Delete(marker); markerWritten = false;
            }
            else if (markerWritten) RecoverLocked(repo, preferPrevious: true);
            throw;
        }
        finally
        {
            if (!File.Exists(marker) && Directory.Exists(incoming)) DeleteOwned(incoming, parent, ".Help-desktop-stage-" + token);
            if (Directory.Exists(temporary)) DeleteOwned(temporary, temporaryParent, "StatsDirectDesktopHelp-" + token);
        }
    }

    public static DesktopHelpRecoveryResult RecoverPending(string statsDirectSource)
    {
        string repo = Repository(statsDirectSource);
        using var publishLock = AcquireLock(repo);
        return RecoverLocked(repo, preferPrevious: false);
    }

    private static DesktopHelpRecoveryResult RecoverLocked(string repo, bool preferPrevious)
    {
        string marker = Path.Combine(repo, PendingMarkerFileName), destination = Destination(repo), parent = Path.GetDirectoryName(destination)!;
        if (!File.Exists(marker)) return new(destination, "Nothing pending", true);
        var journal = ReadJson<Journal>(marker);
        if (journal.SchemaVersion != 1 || journal.Format != Format || string.IsNullOrEmpty(journal.Token) || !Regex.IsMatch(journal.Token, "\\A[0-9a-f]{32}\\z") ||
            !ValidFiles(journal.PreviousFiles) || !ValidFiles(journal.NewFiles) || string.IsNullOrWhiteSpace(journal.TemporaryRoot) ||
            !SameHash(journal.PreviousSha256, TreeHash(journal.PreviousFiles)) || !SameHash(journal.NewSha256, TreeHash(journal.NewFiles)))
            throw new InvalidDataException("The desktop recovery marker is invalid. Existing output has been preserved.");
        string incoming = Path.Combine(parent, ".Help-desktop-stage-" + journal.Token), previous = Path.Combine(parent, ".Help-desktop-previous-" + journal.Token);
        var current = Capture(destination);
        bool isNew = Directory.Exists(destination) && TreeHash(current) == journal.NewSha256;
        bool isOld = Directory.Exists(destination) && TreeHash(current) == journal.PreviousSha256;
        bool oldAvailable = Directory.Exists(previous) && TreeHash(Capture(previous)) == journal.PreviousSha256;
        if (Directory.Exists(previous) && !oldAvailable) throw new IOException("The retained previous desktop output changed. Recovery has preserved both trees.");
        if (preferPrevious && isNew && (oldAvailable || !journal.HadPrevious))
        {
            if (Directory.Exists(incoming)) throw new IOException("Desktop recovery staging is occupied; both complete trees were preserved.");
            Directory.Move(destination, incoming); isNew = false;
        }
        string outcome;
        if (isNew)
        {
            VerifyOwned(destination); outcome = "Completed verified activation";
            if (journal.AdoptionReason != null) WriteJson(Path.Combine(repo, ".statsdirect-desktop-help-adoption.json"),
                new { schemaVersion = 1, adoptedAtUtc = DateTimeOffset.UtcNow, previousFlareSha256 = journal.PreviousSha256, reason = journal.AdoptionReason });
            if (oldAvailable) DeleteOwned(previous, parent, ".Help-desktop-previous-" + journal.Token, journal.PreviousFiles);
        }
        else if (isOld)
        {
            outcome = "Preserved previous complete output";
            if (oldAvailable) DeleteOwned(previous, parent, ".Help-desktop-previous-" + journal.Token, journal.PreviousFiles);
        }
        else if (!Directory.Exists(destination) && oldAvailable)
        { Directory.Move(previous, destination); outcome = "Restored previous complete output"; }
        else if (!Directory.Exists(destination) && !journal.HadPrevious) outcome = "Cancelled first publication";
        else throw new IOException("The desktop destination does not match either recorded snapshot. Recovery left all data intact.");
        if (Directory.Exists(incoming)) DeleteOwned(incoming, parent, ".Help-desktop-stage-" + journal.Token, journal.NewFiles);
        if (Directory.Exists(journal.TemporaryRoot))
        {
            string temporaryParent = WorkspaceProtectionSettings.Load().FindSafeTemporaryParent(repo);
            DeleteOwned(journal.TemporaryRoot, temporaryParent, "StatsDirectDesktopHelp-" + journal.Token,
                journal.NewFiles.Select(file => file with { Path = "Help/" + file.Path }).ToArray());
        }
        File.Delete(marker);
        return new(destination, outcome, true);
    }

    private static string Repository(string path)
    {
        string root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); SafePaths.CheckNoLinks(root);
        string project = Path.Combine(root, "StatsDirectUI/StatsDirectUI.csproj"); SafePaths.CheckNoLinks(project);
        if (!File.Exists(project) || !Directory.Exists(Path.Combine(root, "StatsDirectUI/Assets/Operations")))
            throw new InvalidDataException("Choose a StatsDirect source tree containing StatsDirectUI and its Operations assets.");
        var xml = ParseXml(File.ReadAllBytes(project));
        if (xml.Root?.Name.LocalName != "Project" || !xml.Descendants().Any(element => element.Name.LocalName == "AssemblyName" && element.Value == "StatsDirect") ||
            !xml.Descendants().Any(element => element.Name.LocalName == "RootNamespace" && element.Value.StartsWith("StatsDirect", StringComparison.Ordinal)) ||
            !xml.Descendants().Attributes("Include").Any(attribute => attribute.Value.Replace('\\', '/').Contains("Assets/", StringComparison.Ordinal)))
            throw new InvalidDataException("The selected project does not have the StatsDirect application identity/content contract.");
        SafePaths.CheckNoLinks(Path.Combine(root, "StatsDirectUI/Assets/Operations"));
        return root;
    }

    private static string Destination(string repo)
    { string value = Path.Combine(repo, "StatsDirectUI/Assets/Help"); SafePaths.CheckNoLinks(value); if (File.Exists(value)) throw new IOException("The help destination is a file."); return value; }
    private static FileStream AcquireLock(string repo)
    { string file = Path.Combine(repo, LockName); SafePaths.CheckNoLinks(file); return new(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose); }
    private static bool HasConsumerGuard(string repo)
    {
        string guard = Path.Combine(repo, ConsumerGuardRelativePath); SafePaths.CheckNoLinks(guard);
        if (!File.Exists(guard) || File.ReadAllText(guard).Replace("\r\n", "\n") != ConsumerGuardXml) return false;
        var project = ParseXml(File.ReadAllBytes(Path.Combine(repo, "StatsDirectUI/StatsDirectUI.csproj")));
        return project.Descendants().Any(element => element.Name.LocalName == "Import" && element.Attribute("Condition") == null &&
            (string?)element.Attribute("Project") is string relative && SafePaths.Same(Path.Combine(repo, "StatsDirectUI", relative.Replace('/', Path.DirectorySeparatorChar)), guard));
    }

    private static bool IsFlareOutput(string root, IReadOnlyList<DesktopHelpFile> files) =>
        files.All(file => PublicFile(file.Path) || BuildRecord(file.Path)) &&
        File.Exists(Path.Combine(root, "index.mcwebhelp")) && File.Exists(Path.Combine(root, "Data/HelpSystem.xml")) &&
        File.Exists(Path.Combine(root, "Data/Alias.xml")) && File.Exists(Path.Combine(root, "index.html"));
    private static bool IsBuilderOutput(string root, IReadOnlyList<DesktopHelpFile> files)
    {
        try
        {
            if (!files.All(file => PublicFile(file.Path))) return false;
            using var marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, BuilderMarker)));
            var owned = marker.RootElement.GetProperty("files").EnumerateArray().Select(item => item.GetString()!).Append(BuilderMarker).Order(StringComparer.Ordinal).ToArray();
            return owned.SequenceEqual(files.Select(file => file.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal) && File.Exists(Path.Combine(root, "index.html"));
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or KeyNotFoundException) { return false; }
    }

    private static IReadOnlyList<ContextMap> ValidateArtifact(HelpProject project, string root, BuildResult build,
        HelpBuildTarget expectedTarget = HelpBuildTarget.Desktop)
    {
        foreach (string file in new[] { "index.html", "contents-data.js", "search-index.js", "help-viewer.js", "help-viewer.css", "Data/Alias.xml", "build-info.json" })
            if (!File.Exists(SafePaths.Resolve(root, file))) throw new InvalidDataException("The Desktop build is incomplete: " + file);
        var topics = project.ListTopics();
        if (build.TopicCount != topics.Count || topics.Any(topic => !File.Exists(SafePaths.Resolve(root, topic.Path.ToLowerInvariant()))))
            throw new InvalidDataException("The Desktop build does not contain every saved topic.");
        using var info = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "build-info.json")));
        if (info.RootElement.GetProperty("Target").GetString() != expectedTarget.ToString()) throw new InvalidDataException("The help artifact target does not match its requested publication target.");
        string search = File.ReadAllText(Path.Combine(root, "search-index.js")); const string prefix = "window.StatsDirectSearch=";
        if (!search.StartsWith(prefix, StringComparison.Ordinal) || !search.TrimEnd().EndsWith(';')) throw new InvalidDataException("Desktop search data are incomplete.");
        using var index = JsonDocument.Parse(search[prefix.Length..].Trim().TrimEnd(';'));
        var documents = index.RootElement.GetProperty("documents");
        if (documents.GetArrayLength() != build.SearchDocumentCount || documents.EnumerateArray().Any(item => !File.Exists(SafePaths.Resolve(root, item.GetProperty("url").GetString()!))))
            throw new InvalidDataException("Desktop search does not match the generated topics.");
        string sourceAlias = Path.Combine(project.RootPath, "Project/Advanced/STATSDIRECT.flali"), header = Path.Combine(project.RootPath, "Project/Advanced/SD3ContextID.h");
        if (!File.Exists(sourceAlias) || !File.Exists(header)) throw new InvalidDataException("Desktop delivery needs the saved alias map and SD3ContextID.h header.");
        var expected = ParseXml(File.ReadAllBytes(sourceAlias)).Descendants("Map").ToArray();
        var ids = Regex.Matches(File.ReadAllText(header), @"(?m)^\s*#define\s+(\w+)\s+(\d+)\b").ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);
        var maps = ParseXml(File.ReadAllBytes(Path.Combine(root, "Data/Alias.xml"))).Descendants("Map").Select(element => new ContextMap((string?)element.Attribute("Name") ?? "", (string?)element.Attribute("Link") ?? "", (string?)element.Attribute("ResolvedId"))).ToArray();
        if (maps.Length != expected.Length || maps.Select(map => map.Name).Distinct(StringComparer.Ordinal).Count() != maps.Length) throw new InvalidDataException("Desktop context mappings are missing or duplicated.");
        foreach (var map in expected)
        {
            string name = (string?)map.Attribute("Name") ?? "", link = HelpProject.NormalizeTocLink((string?)map.Attribute("Link")) ?? "";
            int cut = link.IndexOfAny(['#', '?']); string topic = (cut < 0 ? link : link[..cut]).ToLowerInvariant();
            string wanted = topic + (cut < 0 ? "" : link[cut..]);
            var actual = maps.SingleOrDefault(item => item.Name == name);
            if (actual == null || actual.Link != wanted || !File.Exists(SafePaths.Resolve(root, topic)) || ids.TryGetValue(name, out var id) && actual.ResolvedId != id)
                throw new InvalidDataException("The Desktop context mapping does not match saved source: " + name);
        }
        return maps;
    }

    private static void VerifyOwned(string root)
    {
        var receipt = ReadJson<DeliveryReceipt>(Path.Combine(root, ReceiptFileName));
        if (receipt.SchemaVersion != 1 || receipt.Format != Format || receipt.EntryPage != "index.html" || receipt.TopicCount <= 0 || receipt.SearchDocumentCount < 0 ||
            !ValidFiles(receipt.Files) || receipt.Contexts == null || !ValidHash(receipt.SourceSha256))
            throw new InvalidDataException("The desktop-delivery receipt is invalid.");
        var actual = Capture(root).Where(file => file.Path != ReceiptFileName).ToArray();
        if (!SameFiles(actual, receipt.Files) || actual.Any(file => !PublicFile(file.Path))) throw new InvalidDataException("Desktop output differs from its complete delivery receipt; no files will be overwritten.");
        if (!File.Exists(Path.Combine(root, receipt.EntryPage)) || !actual.Any(file => file.Path == "Data/Alias.xml") || !actual.Any(file => file.Path == "search-index.js"))
            throw new InvalidDataException("The recorded desktop output is incomplete.");
    }

    private static List<DesktopHelpFile> Capture(string root)
    {
        var files = new List<DesktopHelpFile>(); if (!Directory.Exists(root)) return files;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            string folder = pending.Pop(); SafePaths.CheckNoLinks(folder);
            foreach (string path in Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.Ordinal))
            {
                SafePaths.CheckNoLinks(path);
                if (Directory.Exists(path)) { pending.Push(path); continue; }
                var info = new FileInfo(path); if (info.Length > 100_000_000) throw new InvalidDataException("A desktop asset exceeds the 100 MB safety limit.");
                files.Add(new(Path.GetRelativePath(root, path).Replace('\\', '/'), info.Length, FileHash(path)));
                if (files.Count > 30_000) throw new InvalidDataException("Desktop output exceeds the file-count limit.");
            }
        }
        if (files.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count) throw new InvalidDataException("Desktop output contains case-colliding files.");
        return files.OrderBy(file => file.Path, StringComparer.Ordinal).ToList();
    }

    private static string InputsHash(string project, string assets)
    {
        var all = new List<DesktopHelpFile>();
        foreach (var pair in new[] { ("Content/", Path.Combine(project, "Content")), ("Project/", Path.Combine(project, "Project")), ("Runtime/", Path.GetFullPath(assets)) })
            all.AddRange(Capture(pair.Item2).Select(file => file with { Path = pair.Item1 + file.Path }));
        return TreeHash(all);
    }
    private static void AssertInputs(HelpProject project, string assets, string expected, string repo, string expectedProject)
    { if (InputsHash(project.RootPath, assets) != expected || FileHash(Path.Combine(repo, "StatsDirectUI/StatsDirectUI.csproj")) != expectedProject) throw new IOException("Saved help, runtime assets or the selected application project changed during publication. The previous help is preserved."); }
    private static string? SourceFileHash(HelpProject project, string relative) => File.Exists(SafePaths.Resolve(project.RootPath, relative)) ? FileHash(SafePaths.Resolve(project.RootPath, relative)) : null;
    private static string FileHash(string file) { SafePaths.CheckNoLinks(file); using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static string TreeHash(IEnumerable<DesktopHelpFile> files) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', files.OrderBy(file => file.Path, StringComparer.Ordinal).Select(file => file.Path + "\0" + file.Bytes + "\0" + file.Sha256))))).ToLowerInvariant();
    private static bool SameHash(string? left, string right) => left != null && left.Equals(right, StringComparison.OrdinalIgnoreCase);
    private static bool SameFiles(IEnumerable<DesktopHelpFile> left, IEnumerable<DesktopHelpFile> right) => TreeHash(left) == TreeHash(right);
    private static bool ValidHash(string? value) => value != null && Regex.IsMatch(value, "\\A[0-9a-f]{64}\\z");
    private static bool ValidFiles(DesktopHelpFile[]? files) => files != null && files.Length <= 30_000 &&
        files.All(file => file != null && !string.IsNullOrWhiteSpace(file.Path) && !Path.IsPathRooted(file.Path) &&
            !file.Path.Contains('\\') && !file.Path.Contains(':') && !file.Path.Split('/').Any(part => part is "" or "." or "..") &&
            file.Bytes >= 0 && file.Bytes <= 100_000_000 && ValidHash(file.Sha256)) &&
        files.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == files.Length;

    private static bool BuildRecord(string relative) => relative.EndsWith(".output", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".mclog", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".log.zip", StringComparison.OrdinalIgnoreCase);
    private static bool PublicFile(string relative)
    {
        var parts = relative.Split('/'); string name = parts[^1], extension = Path.GetExtension(name).ToLowerInvariant();
        if (parts.Any(part => new[] { ".git", ".svn", ".hg", ".codex", "Settings", "Project", "Content", "TutorSource", "ChatGPT" }.Contains(part, StringComparer.OrdinalIgnoreCase)
            || new[] { ".editor-profile", ".tutor-preview-profile", ".app-help-preview-profile", ".browser-preview-profile", ".help-editor-recovery" }.Any(prefix => part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))) return false;
        if (new[] { ".statsdirect-help-editor.json", "authoring-preferences.json", "auth.json", "credentials.json", "account-state.json" }.Contains(name, StringComparer.OrdinalIgnoreCase) || name.StartsWith(".env", StringComparison.OrdinalIgnoreCase) || BuildRecord(relative)) return false;
        if (relative == BuilderMarker || relative == ReceiptFileName) return true;
        if (name.Equals("LICENSE", StringComparison.OrdinalIgnoreCase) || name.Equals("NOTICE", StringComparison.OrdinalIgnoreCase)) return true;
        if ((extension is ".txt" or ".md") && new[] { "LICENSE", "NOTICE", "THIRD-PARTY" }.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return true;
        if (extension == ".props") return name.EndsWith(".js.props", StringComparison.OrdinalIgnoreCase);
        return new[] { ".htm", ".html", ".js", ".css", ".map", ".xml", ".json", ".mcwebhelp", ".png", ".gif", ".jpg", ".jpeg", ".svg", ".ico", ".bmp", ".webp", ".cur", ".woff", ".woff2", ".ttf", ".eot" }.Contains(extension);
    }
    private static XDocument ParseXml(byte[] bytes)
    { using var stream = new MemoryStream(bytes, false); using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 }); return XDocument.Load(reader); }
    private static T ReadJson<T>(string path)
    { SafePaths.CheckNoLinks(path); if (new FileInfo(path).Length > 20_000_000) throw new InvalidDataException("Desktop control metadata is oversized."); return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Desktop control metadata is invalid."); }
    private static void WriteJson<T>(string path, T value) => SafePaths.AtomicWrite(path, JsonSerializer.Serialize(value, Json) + "\n");
    private static void WriteNewMarker(string path, Journal journal)
    {
        SafePaths.CheckNoLinks(path); string temporary = path + "." + journal.Token + ".tmp";
        try { using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(journal, Json))); file.Flush(true); } File.Move(temporary, path); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void CopyTree(string source, string destination, CancellationToken token)
    { Directory.CreateDirectory(destination); foreach (var file in Capture(source)) { token.ThrowIfCancellationRequested(); string target = SafePaths.Resolve(destination, file.Path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(SafePaths.Resolve(source, file.Path), target); } }
    private static void DeleteOwned(string directory, string parent, string expectedName, IReadOnlyList<DesktopHelpFile>? expectedFiles = null)
    {
        if (!SafePaths.Same(Path.GetDirectoryName(directory)!, parent) || Path.GetFileName(directory) != expectedName) throw new IOException("Unsafe desktop staging cleanup path.");
        var files = Capture(directory); if (expectedFiles != null && !SameFiles(files, expectedFiles)) throw new IOException("Desktop staging changed; it was retained for review rather than deleted.");
        SafePaths.CheckNoLinks(directory); Directory.Delete(directory, recursive: true);
    }

    private sealed record ContextMap(string Name, string Link, string? ResolvedId);
    private sealed record DeliveryReceipt(int SchemaVersion, string Format, DateTimeOffset PublishedAtUtc,
        string SourceSha256, string EntryPage, int TopicCount, int SearchDocumentCount, IReadOnlyList<ContextMap> Contexts,
        string? AliasSourceSha256, string? ContextHeaderSha256, string PreviousKind, string PreviousSha256,
        bool AdoptedFlareOutput, DesktopHelpFile[] Files, string? PublicationSnapshotSha256 = null);
    private sealed record Journal(int SchemaVersion, string Format, string Token, bool HadPrevious,
        string PreviousSha256, string NewSha256, DesktopHelpFile[] PreviousFiles, DesktopHelpFile[] NewFiles, string? AdoptionReason, string TemporaryRoot);
}
