#nullable disable
// Snapshot of StatsDirect Windows help host (MIT).
// Original: StatsDirectUI/UI/HtmlHelp/HelpCatalog.cs
// See provenance.json for source hashes and the deliberately limited editor integration changes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace StatsDirect.HelpEditor.App.AppHelp;

/// <summary>Uses Flare's published aliases, including the numeric IDs in operation XML.</summary>
internal sealed class HelpCatalog
{
    internal const string Origin = "https://help.statsdirect.invalid";
    internal string Directory { get; }
    internal IReadOnlyDictionary<string, string> Topics => topics;
    private readonly Dictionary<string, string> topics = new(StringComparer.OrdinalIgnoreCase);

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
