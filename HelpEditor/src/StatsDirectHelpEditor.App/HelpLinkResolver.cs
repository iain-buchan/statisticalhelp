using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public sealed record HelpLinkTarget(string? TopicPath, string? Fragment, Uri? ExternalUri);

/// <summary>Resolves authoring links without allowing a file/program launch.</summary>
public static class HelpLinkResolver
{
    public static HelpLinkTarget Resolve(HelpProject project, string? currentTopic, string href)
    {
        if (string.IsNullOrWhiteSpace(href)) throw new ArgumentException("The link has no destination.");
        href = href.Trim();
        if (href.Contains('\\') || href.Any(char.IsControl)) throw new ArgumentException("The link contains an invalid path.");
        string path;
        string? fragment;
        bool rootRelative;
        if (!href.StartsWith('/') && Uri.TryCreate(href, UriKind.Absolute, out var absolute))
        {
            if (absolute.Scheme is not ("http" or "https")) throw new ArgumentException("Only help pages and http/https web links can be opened.");
            if (absolute.UserInfo.Length != 0) throw new ArgumentException("Links with embedded sign-in details are not opened.");
            string host = absolute.Host.ToLowerInvariant();
            string escapedPath = absolute.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
            if (host == "help-project.invalid" && absolute.Scheme == "https" && absolute.IsDefaultPort)
                path = escapedPath;
            else if (host is "statsdirect.com" or "www.statsdirect.com" && escapedPath.StartsWith("help/", StringComparison.OrdinalIgnoreCase))
                path = escapedPath[5..];
            else if (host == "iain-buchan.github.io" && escapedPath.StartsWith("statisticalhelp/", StringComparison.OrdinalIgnoreCase))
                path = escapedPath[16..];
            else if (host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("This link does not point to the help source.");
            else return new HelpLinkTarget(null, null, absolute);
            fragment = DecodeFragment(absolute.GetComponents(UriComponents.Fragment, UriFormat.UriEscaped));
            rootRelative = true;
        }
        else if (href.StartsWith("//", StringComparison.Ordinal))
            return Resolve(project, currentTopic, "https:" + href);
        else
        {
            int hash = href.IndexOf('#');
            fragment = hash < 0 ? null : DecodeFragment(href[(hash + 1)..]);
            path = (hash < 0 ? href : href[..hash]).Split('?')[0];
            rootRelative = path.StartsWith('/');
        }
        path = Uri.UnescapeDataString(path);
        if (path.Contains('\\') || path.Contains(':') || path.Contains('#') || path.Contains('?') || path.Any(char.IsControl) || Uri.UnescapeDataString(path) != path)
            throw new ArgumentException("The link contains an invalid or multiply encoded path.");
        path = path.TrimStart('/');
        if (path.StartsWith("Content/", StringComparison.OrdinalIgnoreCase)) { path = path[8..]; rootRelative = true; }
        string topic;
        if (path.Length == 0)
            topic = rootRelative ? "contents.htm" : currentTopic ?? "contents.htm";
        else
        {
            var parts = rootRelative || currentTopic == null ? new List<string>() : currentTopic.Split('/').SkipLast(1).ToList();
            foreach (string part in path.Split('/'))
            {
                if (part is "" or ".") continue;
                if (part == "..")
                {
                    if (parts.Count == 0) throw new ArgumentException("The link goes outside the help Content folder.");
                    parts.RemoveAt(parts.Count - 1);
                }
                else parts.Add(part);
            }
            topic = string.Join('/', parts);
        }
        if (Path.GetExtension(topic).ToLowerInvariant() is not (".htm" or ".html"))
            throw new ArgumentException("Only HTML help pages can be opened in an editor window.");
        // Resolve only this file; an unrelated malformed page must not block navigation.
        topic = project.ResolveTopicPath(topic);
        return new HelpLinkTarget(topic, fragment, null);
    }

    private static string? DecodeFragment(string value)
    {
        if (value.Length == 0) return null;
        string decoded = Uri.UnescapeDataString(value);
        if (decoded.Any(char.IsControl)) throw new ArgumentException("The link contains an invalid section anchor.");
        return decoded;
    }
}
