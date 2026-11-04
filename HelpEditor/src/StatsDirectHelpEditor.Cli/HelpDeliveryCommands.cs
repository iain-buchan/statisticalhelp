using System.Text.Json;
using StatsDirect.HelpEditor.Core;

internal static class HelpDeliveryCommands
{
    internal static int? TryRun(string[] arguments, Func<string> defaultAssets)
    {
        string command = arguments[0].ToLowerInvariant();
        if (command is not ("compare-source" or "compare-flare-source" or
            "inspect-desktop-help" or "publish-desktop-help" or
            "verify-desktop-help" or "recover-desktop-help" or "publish-help" or "publish-content")) return null;

        string[] permitted = command switch
        {
            "compare-source" => ["--left", "--right"],
            "compare-flare-source" => ["--flare", "--working"],
            "publish-desktop-help" => ["--project", "--statsdirect-source", "--assets",
                "--expected-destination-sha256", "--adopt-flare-output", "--adoption-reason", "--allow-warnings"],
            "publish-help" => ["--project", "--zip", "--github-url", "--github-branch", "--author-name", "--author-email",
                "--statsdirect-source", "--expected-destination-sha256", "--adopt-flare-output", "--adoption-reason",
                "--allow-default-branch", "--prepare-only", "--journal-directory", "--allow-warnings", "--assets"],
            "publish-content" => ["--project", "--zip", "--github-url", "--github-branch", "--author-name", "--author-email",
                "--statsdirect-source", "--expected-destination-sha256", "--adopt-flare-output", "--adoption-reason",
                "--allow-default-branch", "--prepare-only", "--journal-directory", "--allow-warnings", "--assets", "--tutor-course"],
            _ => ["--statsdirect-source"]
        };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 1; index < arguments.Length; index++)
        {
            string key = arguments[index];
            if (!permitted.Contains(key)) throw new ArgumentException("Unknown option for " + command + ": " + key);
            if (values.ContainsKey(key) || flags.Contains(key)) throw new ArgumentException("Duplicate option: " + key);
            if (key is "--adopt-flare-output" or "--allow-warnings" or "--allow-default-branch" or "--prepare-only")
            {
                flags.Add(key);
                continue;
            }
            if (++index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index]) || arguments[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Missing value for " + key);
            values.Add(key, arguments[index]);
        }
        string Required(string key) => values.GetValueOrDefault(key) ?? throw new ArgumentException("Missing " + key);

        object result;
        if (command is "publish-help" or "publish-content")
        {
            // Parse every required option before opening source or starting any
            // Git/network operation. A failed publication still emits its honest
            // per-target receipt and returns a non-zero status.
            var options = new HelpPublicationOptions
            {
                WebsiteZipPath = Required("--zip"), GitHubRepositoryUrl = Required("--github-url"),
                GitHubBranch = Required("--github-branch"), AuthorName = Required("--author-name"),
                AuthorEmail = Required("--author-email"), StatsDirectSource = Required("--statsdirect-source"),
                ExpectedDesktopSha256 = values.GetValueOrDefault("--expected-destination-sha256"),
                AdoptExistingFlareOutput = flags.Contains("--adopt-flare-output"),
                AdoptionReason = values.GetValueOrDefault("--adoption-reason"),
                AllowDefaultBranchPublication = flags.Contains("--allow-default-branch"),
                PushToGitHub = !flags.Contains("--prepare-only"),
                AllowBuildWarnings = flags.Contains("--allow-warnings"),
                JournalDirectory = values.GetValueOrDefault("--journal-directory"),
                PublishSharedContent = command == "publish-content",
                TutorCoursePath = command == "publish-content" ? Required("--tutor-course") : null,
                CommitMessage = command == "publish-content" ? "Publish saved help and reviewed tutor content" : "Publish saved help source and rebuilt website"
            };
            var source = HelpProject.Open(Required("--project"));
            try
            {
                result = HelpPublicationPublisher.PublishAsync(source,
                    values.GetValueOrDefault("--assets") ?? defaultAssets(), options).GetAwaiter().GetResult();
            }
            catch (HelpPublicationException error)
            {
                Console.WriteLine(JsonSerializer.Serialize(error.Result, new JsonSerializerOptions { WriteIndented = true }));
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }
        else if (command is "compare-source" or "compare-flare-source")
        {
            string left = Required(command == "compare-source" ? "--left" : "--flare");
            string right = Required(command == "compare-source" ? "--right" : "--working");
            result = HelpSourceComparer.Compare(left, right);
        }
        else
        {
            string repository = Required("--statsdirect-source");
            result = command switch
            {
                "inspect-desktop-help" => DesktopHelpPublisher.InspectDestination(repository),
                "verify-desktop-help" => DesktopHelpPublisher.VerifyDestination(repository),
                "recover-desktop-help" => DesktopHelpPublisher.RecoverPending(repository),
                _ => DesktopHelpPublisher.Publish(
                    HelpProject.Open(Required("--project")), repository,
                    values.GetValueOrDefault("--assets") ?? defaultAssets(),
                    new DesktopHelpPublishOptions
                    {
                        ExpectedDestinationSha256 = values.GetValueOrDefault("--expected-destination-sha256"),
                        AdoptExistingFlareOutput = flags.Contains("--adopt-flare-output"),
                        AdoptionReason = values.GetValueOrDefault("--adoption-reason"),
                        AllowBuildWarnings = flags.Contains("--allow-warnings")
                    })
            };
        }
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
