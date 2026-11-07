#nullable disable
// Snapshot of StatsDirect Windows help host (MIT).
// Original: StatsDirectUI/UI/HtmlHelp/HelpCatalog.cs
// See provenance.json for source hashes and the deliberately limited editor integration changes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace StatsDirect.HelpEditor.App.AppHelp;

/// <summary>Uses Flare's published aliases, including the numeric IDs in operation XML.</summary>
internal sealed class HelpCatalog
{
    internal const string Origin = "https://help.statsdirect.invalid";
    internal string Directory { get; }
    internal IReadOnlyDictionary<string, string> Topics => topics;
    internal bool HasKeywordIndex => File.Exists(Path.Combine(Directory, "keyword-index.json"));
    internal IReadOnlyList<HelpIndexEntry> IndexEntries => HasKeywordIndex ? indexEntries ??= LoadIndexEntries() : Array.Empty<HelpIndexEntry>();
    private readonly Dictionary<string, string> topics = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<HelpIndexEntry> indexEntries;

    internal HelpCatalog(string directory)
    {
        Directory = Path.GetFullPath(directory);
        if (!File.Exists(Path.Combine(Directory, "index.html")))
            throw new FileNotFoundException("The HTML help files are missing. Run StatsDirectSetup.exe to repair the installation.");
        foreach (var map in XDocument.Load(Path.Combine(Directory, "Data", "Alias.xml")).Descendants("Map"))
        {
            string link = (string)map.Attribute("Link");
            foreach (string key in new[] { (string)map.Attribute("Name"), (string)map.Attribute("ResolvedId") })
                if (!string.IsNullOrWhiteSpace(key) && link != null) topics[key] = link;
        }
    }

    private IReadOnlyList<HelpIndexEntry> LoadIndexEntries()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, "keyword-index.json")));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out int schemaVersion) || schemaVersion != 1 ||
                !root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The help keyword index has an unsupported or missing schema. Rebuild the desktop help.");

            var result = new List<HelpIndexEntry>();
            var seen = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            int number = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                number++;
                string term = ReadIndexString(entry, "term", number);
                string title = ReadIndexString(entry, "title", number);
                string target = ReadIndexString(entry, "path", number);
                if (Uri.TryCreate(target, UriKind.Absolute, out _) || target.StartsWith("//", StringComparison.Ordinal) ||
                    target.Contains('\\') || target.Contains('\0'))
                    throw new InvalidDataException($"Help keyword index entry {number} does not name a packaged topic.");
                Uri page;
                try { page = Resolve(target); }
                catch (Exception ex) when (ex is ArgumentException or UriFormatException or NotSupportedException)
                {
                    throw new InvalidDataException($"Help keyword index entry {number} has an invalid topic path.", ex);
                }
                string path = page?.AbsolutePath.TrimStart('/');
                if (path == null || path.Equals("index.html", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("resources/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Data/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Help keyword index entry {number} refers to a missing or unsafe topic.");
                path += page.Fragment;
                if (!seen.TryGetValue(term, out var targets)) seen[term] = targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (targets.Add(path)) result.Add(new HelpIndexEntry(term, title, path));
            }
            var english = StringComparer.Create(CultureInfo.GetCultureInfo("en-GB"), true);
            return result.OrderBy(entry => entry.Term, english).ThenBy(entry => entry.Title, english)
                .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The help keyword index contains invalid JSON. Rebuild the desktop help.", ex);
        }
    }

    private static string ReadIndexString(JsonElement entry, string name, int number)
    {
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Help keyword index entry {number} has no valid {name}.");
        string text;
        try { text = value.GetString(); }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException($"Help keyword index entry {number} has invalid {name} text.", ex);
        }
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException($"Help keyword index entry {number} has no valid {name}.");
        return text.Trim();
    }

    internal Uri Resolve(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic) || topic == "0") topic = "1000";
        if (topics.TryGetValue(topic, out string mapped)) topic = mapped;
        if (Uri.TryCreate(topic, UriKind.Absolute, out var absolute))
        {
            if (absolute.Host.Equals("www.statsdirect.com", StringComparison.OrdinalIgnoreCase) || absolute.Host.Equals("statsdirect.com", StringComparison.OrdinalIgnoreCase))
            {
                if (!absolute.AbsolutePath.StartsWith("/help/", StringComparison.OrdinalIgnoreCase)) return null;
                topic = absolute.AbsolutePath[6..] + absolute.Fragment;
            }
            else if (absolute.Host.Equals("iain-buchan.github.io", StringComparison.OrdinalIgnoreCase) && absolute.AbsolutePath.StartsWith("/statisticalhelp/", StringComparison.OrdinalIgnoreCase))
                topic = absolute.AbsolutePath[17..] + absolute.Fragment;
            else if (IsLocal(absolute)) topic = absolute.PathAndQuery + absolute.Fragment;
            else return null;
        }
        // Accept only packaged pages, never file URLs, scripts or path traversal.
        string path = Uri.UnescapeDataString(topic.Split('#', '?')[0]).TrimStart('/');
        if (path.StartsWith("Content/", StringComparison.OrdinalIgnoreCase)) path = path[8..];
        if (path.Contains('\\') || path.Contains(':') || Array.Exists(path.Split('/'), p => p is "." or "..")) return null;
        string full = Path.GetFullPath(Path.Combine(Directory, path));
        string ext = Path.GetExtension(full);
        if (!full.StartsWith(Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            ext is not (".htm" or ".html") || !File.Exists(full)) return null;
        int fragment = topic.IndexOf('#');
        return new Uri(Origin + "/" + path + (fragment >= 0 ? topic[fragment..] : ""));
    }

    internal static bool IsLocal(Uri uri) => uri.GetLeftPart(UriPartial.Authority) == Origin;
    internal static bool IsLocal(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && IsLocal(parsed);
    internal static bool IsExternal(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "https" or "http" or "mailto" && !IsLocal(parsed);
}

internal sealed record HelpIndexEntry(string Term, string Title, string Path);
