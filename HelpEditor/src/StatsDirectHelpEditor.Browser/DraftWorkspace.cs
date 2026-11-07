using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using StatsDirect.HelpEditor.Core;

sealed record DraftRequest(string Path, string Source, string Revision);
sealed record PreviewRequest(string Path, string Source, string Revision, string Target);
sealed record TopicDraft(string Path, string Title, string Source, string Revision, string BaseUrl);
sealed class DraftConflictException() : Exception("The saved draft changed in another tab. Reload the topic before saving or building a preview.");

sealed class DraftWorkspace : IDisposable
{
    private readonly HelpProject project;
    private readonly string assets;
    private readonly FileStream workspaceLock;
    private readonly object gate = new();
    private readonly Queue<string> previews = new();
    private readonly Dictionary<string, TopicInfo> topics;

    public string PreviewPath { get; }

    public DraftWorkspace(string workspacePath, string seedPath, string assetsPath)
    {
        string root = Path.GetFullPath(workspacePath);
        string marker = Path.Combine(root, ".statsdirect-browser-draft");
        string lockPath = Path.Combine(root, ".browser-lock");
        RequirePlainPath(marker);
        RequirePlainPath(lockPath);
        if (!File.Exists(marker) && Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any(path => path != lockPath))
            throw new InvalidOperationException("Choose an empty folder or a browser draft workspace.");

        Directory.CreateDirectory(root);
        workspaceLock = File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (File.Exists(marker))
            {
                if (File.ReadAllText(marker).Trim() != "1") throw new InvalidDataException("This browser draft workspace version is unsupported.");
            }
            else
            {
                using var ownership = File.Open(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                ownership.Write("1\n"u8);
            }
            var layout = new HelpEditorWorkspace(root, Path.Combine(root, "HelpSource"), seedPath);
            layout.PrepareDefaultProject();
            project = HelpProject.Open(layout.DefaultProjectPath);
            assets = assetsPath;
            topics = IndexTopics();
            PreviewPath = Path.Combine(root, ".previews-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(PreviewPath);
        }
        catch
        {
            workspaceLock.Dispose();
            throw;
        }
    }

    private static void RequirePlainPath(string path)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("The browser draft workspace cannot use linked files or folders: " + current);
        }
    }

    public object Summary()
    {
        lock (gate) return new
        {
            Topics = topics.Values.ToArray(),
            DesktopAppearance = project.GetAppearance(HelpBuildTarget.Desktop),
            WebsiteAppearance = project.GetAppearance(HelpBuildTarget.Public)
        };
    }

    public TopicDraft Read(string path)
    {
        lock (gate) return ReadTopic(path);
    }

    public TopicDraft Save(DraftRequest draft)
    {
        lock (gate)
        {
            var current = ReadTopic(draft.Path);
            RequireCurrentRevision(current, draft.Revision);
            var snapshot = project.SaveTopicPreservingFormat(current.Path, draft.Source, draft.Revision);
            var saved = current with { Source = snapshot.Source, Revision = snapshot.Revision };
            var elements = ParseTopic(saved.Source).Descendants();
            string? title = elements.FirstOrDefault(element => element.Name.LocalName == "title")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(title)) title = elements.FirstOrDefault(element => element.Name.LocalName == "h1")?.Value.Trim();
            title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(saved.Path) : Regex.Replace(title, @"\s+", " ");
            topics[saved.Path] = new(saved.Path, title);
            return saved with { Title = title };
        }
    }

    public object Preview(PreviewRequest draft)
    {
        lock (gate)
        {
            var current = ReadTopic(draft.Path);
            RequireCurrentRevision(current, draft.Revision);
            var target = draft.Target switch
            {
                "website" => HelpBuildTarget.Public,
                "desktop" => HelpBuildTarget.Desktop,
                _ => throw new ArgumentException("Choose Website or Desktop help.")
            };
            // The desktop editor applies this same source policy before it sends a draft to the builder.
            HelpSourceContent.ValidateForSave(ParseTopic(draft.Source));
            var id = Guid.NewGuid().ToString("N");
            string output = Path.Combine(PreviewPath, id);
            try
            {
                var result = HelpBuilder.Build(project, output, assets, target,
                    new Dictionary<string, string> { [current.Path] = draft.Source });
                previews.Enqueue(output);
                while (previews.Count > 4)
                {
                    // Windows can hold a preview file open while the browser reads it.
                    try { Directory.Delete(previews.Peek(), true); }
                    catch (IOException) { break; }
                    previews.Dequeue();
                }
                return new
                {
                    Url = $"/preview/{id}/{EncodePath(current.Path.ToLowerInvariant())}",
                    result.TopicCount,
                    WarningCount = result.Warnings.Count
                };
            }
            catch
            {
                if (Directory.Exists(output)) Directory.Delete(output, true);
                throw;
            }
        }
    }

    private Dictionary<string, TopicInfo> IndexTopics() => project.ListTopics().ToDictionary(topic => topic.Path, StringComparer.Ordinal);

    private static XDocument ParseTopic(string source)
    {
        using var reader = XmlReader.Create(new StringReader(source), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private TopicDraft ReadTopic(string path)
    {
        if (string.IsNullOrEmpty(path) || !topics.TryGetValue(path, out var topic)) throw new FileNotFoundException();
        var snapshot = project.ReadTopicSnapshot(topic.Path);
        return new(topic.Path, topic.Title, snapshot.Source, snapshot.Revision, "/source/" + EncodePath(topic.Path));
    }

    private static void RequireCurrentRevision(TopicDraft current, string revision)
    {
        if (current.Revision != revision) throw new DraftConflictException();
    }

    private static string EncodePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    public void Dispose()
    {
        try { Directory.Delete(PreviewPath, true); }
        finally { workspaceLock.Dispose(); }
    }
}
