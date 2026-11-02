using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace StatsDirect.HelpEditor.Core;

public enum HelpBuildTarget { Desktop, Public }
public record BuildResult(string OutputDirectory, int TopicCount, int SearchDocumentCount, IReadOnlyList<string> Warnings);

/// <summary>A small, independent HTML builder for the subset of Flare used by StatsDirect.</summary>
public static class HelpBuilder
{
    private const string OutputMarker = ".statsdirect-help-output.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string TopicCss = "body{margin:0;padding:1em;font:10pt Arial,sans-serif;line-height:normal;-webkit-font-smoothing:antialiased;-moz-osx-font-smoothing:grayscale}img,svg{max-width:100%;height:auto}details.rcode{margin:1em 0}details.rcode>div{margin-left:18px}summary{cursor:pointer}details.rcode>summary{font-weight:bold;color:#2c5f8a}details.rcode>summary{list-style:none}details.rcode>summary::-webkit-details-marker{display:none}details.rcode>summary:before{content:\"\";display:inline-block;width:.45em;height:.45em;border-right:.15em solid #2c5f8a;border-bottom:.15em solid #2c5f8a;margin-left:.1em;margin-right:.65em;vertical-align:.12em;transform:rotate(-45deg);transition:transform .15s ease}details.rcode[open]>summary:before{transform:rotate(45deg);vertical-align:.22em}.help-topic-nav{font-size:9pt;margin-bottom:1em}.help-equation{display:inline-block;max-width:100%;overflow:visible;vertical-align:middle}mjx-container{max-width:100%}.help-equation mjx-assistive-mml{width:1px!important;height:1px!important}";
    private const string MathConfiguration = "<script>window.MathJax={tex:{inlineMath:[['$','$'],['\\\\(','\\\\)']],displayMath:[['$$','$$'],['\\\\[','\\\\]']]},options:{skipHtmlTags:['script','noscript','style','textarea','pre','code'],ignoreHtmlClass:'help-math-ignore',processHtmlClass:'help-equation-live'},svg:{fontCache:'local'}};</script>";
    private const string ScopedStylesPath = "resources/help-editor-runtime/topic.css";

    public static BuildResult Build(HelpProject project, string outputDirectory, string runtimeAssetsDirectory, HelpBuildTarget target = HelpBuildTarget.Desktop,
        IReadOnlyDictionary<string, string>? topicOverrides = null)
    {
        var output = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar);
        ValidateOutput(project, output);
        var appearance = project.GetAppearance(target);
        var assets = Path.GetFullPath(runtimeAssetsDirectory);
        SafePaths.CheckNoLinks(assets);
        var mathjax = FindMathJax(assets);
        var equationArtwork = EquationArtworkCatalog.Load(assets);
        var oldManifest = Path.Combine(output, OutputMarker);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any() && !File.Exists(oldManifest))
            throw new InvalidOperationException("Build into an empty folder or a previous StatsDirectHelpEditor build.");
        var previous = File.Exists(oldManifest) ? JsonSerializer.Deserialize<OutputReceipt>(File.ReadAllText(oldManifest), JsonOptions)?.Files ?? [] : [];
        var previousFiles = previous.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(output);
        var generated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var completed = false;
        var warnings = new List<string>();
        var topics = project.ListTopics();
        var drafts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var draftTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (topicOverrides != null)
        {
            foreach (var draft in topicOverrides)
            {
                var topic = topics.SingleOrDefault(topic => topic.Path.Equals(draft.Key, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("The preview draft does not belong to this project's topics: " + draft.Key);
                var document = HelpProject.Parse(draft.Value);
                if (document.Root?.Name.LocalName != "html" || !document.Descendants().Any(element => element.Name.LocalName == "body"))
                    throw new InvalidDataException("A preview draft must contain an HTML root and body.");
                drafts.Add(topic.Path, draft.Value);
                string? title = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "title")?.Value.Trim();
                if (string.IsNullOrWhiteSpace(title)) title = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "h1")?.Value.Trim();
                draftTitles[topic.Path] = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(topic.Path) : Regex.Replace(title, @"\s+", " ");
            }
            topics = topics.Select(topic => draftTitles.TryGetValue(topic.Path, out string? title) ? topic with { Title = title } : topic).ToList();
        }
        var sourceFiles = Directory.GetFiles(project.ContentPath, "*", SearchOption.AllDirectories);
        // Reuse the existing Flare artwork unchanged. Generic/new project
        // fixtures without branding retain a text title and no broken image.
        var assetPaths = sourceFiles.Select(file => Path.GetRelativePath(project.ContentPath, file).Replace('\\', '/').ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        string? logoPath = assetPaths.Contains("resources/images/sd3helplogo.png") ? "resources/images/sd3helplogo.png" : null;
        string? iconPath = assetPaths.Contains("resources/images/sd3.ico") ? "resources/images/sd3.ico" : null;
        var reserved = new HashSet<string>(["index.html", "contents-data.js", "search-index.js", "help-viewer.css", "help-viewer.js", "build-info.json", OutputMarker, "data/alias.xml"], StringComparer.OrdinalIgnoreCase);
        foreach (var file in sourceFiles)
        {
            var relative = Path.GetRelativePath(project.ContentPath, file).Replace('\\', '/').ToLowerInvariant();
            if (reserved.Contains(relative) || relative.StartsWith("resources/help-editor-runtime/", StringComparison.Ordinal))
                throw new InvalidDataException("Source file conflicts with a generated help file: " + relative + ". Rename the source file before building.");
        }
        var outputTopics = topics.ToDictionary(t => t.Path.ToLowerInvariant(), t => t, StringComparer.OrdinalIgnoreCase);
        if (outputTopics.Count != topics.Count) throw new InvalidDataException("Two source topics produce the same lowercase output path.");

        void Write(string relative, string content)
        {
            var path = SafePaths.Resolve(output, relative);
            RequireOwnedOutput(path, relative);
            generated.Add(relative.Replace('\\', '/'));
            SafePaths.AtomicWrite(path, content);
        }

        void Copy(string source, string relative)
        {
            SafePaths.CheckNoLinks(source);
            var dest = SafePaths.Resolve(output, relative);
            RequireOwnedOutput(dest, relative);
            generated.Add(relative.Replace('\\', '/'));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, true);
        }

        void RequireOwnedOutput(string path, string relative)
        {
            if (File.Exists(path) && !previousFiles.Contains(relative.Replace('\\', '/')) && !generated.Contains(relative.Replace('\\', '/')))
                throw new InvalidOperationException("An unowned file would be overwritten in the build folder: " + relative + ". Move that file or choose a separate build folder.");
        }

        try
        {
        // The HTML topics are rebuilt below; copy artwork and other ordinary assets.
        foreach (var file in sourceFiles)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".htm" or ".html" or ".flmsp" or ".flsnp") continue;
            Copy(file, Path.GetRelativePath(project.ContentPath, file).Replace('\\', '/').ToLowerInvariant());
        }
        foreach (var file in Directory.EnumerateFiles(mathjax, "*", SearchOption.AllDirectories))
            Copy(file, "resources/help-editor-runtime/mathjax/" + Path.GetRelativePath(mathjax, file).Replace('\\', '/'));
        equationArtwork.CopyArtwork(Copy);
        if (appearance == "flare")
        {
            var sourceCss = sourceFiles.FirstOrDefault(file => Path.GetRelativePath(project.ContentPath, file).Replace('\\', '/').Equals("resources/stylesheets/statsdirect.css", StringComparison.OrdinalIgnoreCase));
            Write(ScopedStylesPath, sourceCss == null ? "" : HelpCssScoper.Scope(File.ReadAllText(sourceCss), rewriteUrl: value => RewriteStyleUrl(value, project)));
        }


        var sharedFooter = project.ReadSharedFooter(target);
        var searchDocuments = new List<SearchDocument>();
        foreach (var topic in topics)
        {
            var document = HelpProject.Parse(drafts.TryGetValue(topic.Path, out string? draft) ? draft : project.ReadTopic(topic.Path));
            var body = document.Descendants().First(e => e.Name.LocalName == "body");
            ApplyConditions(body, target);
            var keywordText = string.Join(" ", body.Descendants().Where(e => HelpSourceNamespaces.IsElement(e, "keyword")).Select(e => (string?)e.Attribute("term") ?? ""));
            var text = NormalizeText(string.Join(" ", body.DescendantNodes().OfType<XText>().Where(t => !t.Ancestors().Any(e => e.Name.LocalName is "script" or "style")).Select(t => t.Value)));
            var outputPath = topic.Path.ToLowerInvariant();
            var transformed = new XElement(body);
            Transform(transformed, topic.Path, project, warnings, equationArtwork);
            string footerHtml = "";
            if (sharedFooter != null)
            {
                var footer = XElement.Parse("<footer class=\"help-shared-footer\">" + sharedFooter.Html + "</footer>", LoadOptions.PreserveWhitespace);
                ApplyConditions(footer, target);
                Transform(footer, sharedFooter.SourcePath, project, warnings, equationArtwork, outputPath);
                footerHtml = footer.ToString(SaveOptions.DisableFormatting);
            }
            var css = Relative(outputPath, "resources/stylesheets/statsdirect.css");
            var runtime = Relative(outputPath, "resources/help-editor-runtime/mathjax/tex-mml-svg.js");
            var home = Relative(outputPath, "index.html");
            var topicBody = string.Concat(transformed.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting))) + footerHtml;
            var mathHead = MathConfiguration + "<script defer src=\"" + runtime + "\"></script>";
            string html;
            if (appearance == "flare")
            {
                var rootRelative = Relative(outputPath, "index.html")[..^"index.html".Length];
                var scopedHead = "<link rel=\"stylesheet\" href=\"" + Relative(outputPath, ScopedStylesPath) + "\"><style>" +
                    HelpCssScoper.Scope(TopicCss + EquationArtworkCatalog.Css + ViewerFiles.TopicAppearanceCss) + "</style>" + mathHead;
                html = ViewerFiles.FlareTopicHtml(topic.Title, outputPath, scopedHead, topicBody, rootRelative,
                    logoPath == null ? null : Relative(outputPath, logoPath), iconPath == null ? null : Relative(outputPath, iconPath));
            }
            else
            {
                html = "<!doctype html>\n<html lang=\"en\" data-help-skin=\"" + appearance + "\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
                    "<title>" + WebUtility.HtmlEncode(topic.Title) + "</title>" + ViewerFiles.IconLink(iconPath == null ? null : Relative(outputPath, iconPath)) + "<link rel=\"stylesheet\" href=\"" + css + "\">" +
                    "<style>" + TopicCss + EquationArtworkCatalog.Css + ViewerFiles.TopicAppearanceCss + "</style><script>" + StandaloneTopicNavigation(home, outputPath) + ViewerFiles.TopicNavigationJavaScript + "</script>" +
                    mathHead + "</head><body class=\"help-math-ignore\"><nav class=\"help-topic-nav\"><a href=\"" + home + "\" target=\"_top\">Contents and search</a></nav>" + topicBody + "</body></html>";
            }
            Write(outputPath, html);
            searchDocuments.Add(new SearchDocument(searchDocuments.Count, outputPath, topic.Title, keywordText, text));
        }

        var toc = project.LoadContents();
        var viewerToc = ConvertToc(toc, target, outputTopics, warnings);
        Write("contents-data.js", "window.StatsDirectContents=" + JsonSerializer.Serialize(viewerToc, JsonOptions) + ";");
        var search = CreateIndex(searchDocuments);
        Write("search-index.js", "window.StatsDirectSearch=" + JsonSerializer.Serialize(search, JsonOptions) + ";");
        var homeTopic = topics.FirstOrDefault(t => t.Path.Equals("contents.htm", StringComparison.OrdinalIgnoreCase)) ?? topics.FirstOrDefault();
        Write("index.html", appearance == "flare" && homeTopic != null
            ? FlareIndexNavigation(homeTopic.Path.ToLowerInvariant(), logoPath, iconPath)
            : ViewerFiles.HtmlForAppearance(appearance, logoPath, iconPath));
        Write("help-viewer.css", ViewerFiles.Css);
        Write("help-viewer.js", ViewerFiles.JavaScript);
        Write("Data/Alias.xml", CreateAliases(project, outputTopics, warnings));
        Write("build-info.json", JsonSerializer.Serialize(new { Builder = "StatsDirectHelpEditor prototype", Target = target.ToString(), Appearance = appearance, BuiltAtUtc = DateTimeOffset.UtcNow, TopicCount = topics.Count, SearchDocumentCount = searchDocuments.Count, Warnings = warnings.Distinct().ToArray() }, new JsonSerializerOptions { WriteIndented = true }));

        // Delete only obsolete files named by this builder's own previous receipt.
        foreach (var file in previous.Except(generated, StringComparer.OrdinalIgnoreCase))
        {
            var stale = SafePaths.Resolve(output, file);
            if (File.Exists(stale)) File.Delete(stale);
        }
        completed = true;
        return new BuildResult(output, topics.Count, searchDocuments.Count, warnings.Distinct().ToArray());
        }
        finally
        {
            // Even a failed transform leaves an attributable build folder that
            // can be rebuilt. Track only paths this invocation actually owned.
            var receiptFiles = completed ? generated : previous.Concat(generated).ToHashSet(StringComparer.OrdinalIgnoreCase);
            SafePaths.AtomicWrite(oldManifest, JsonSerializer.Serialize(new OutputReceipt(receiptFiles.Order(StringComparer.OrdinalIgnoreCase).ToArray()), JsonOptions));
        }
    }

    private static void ValidateOutput(HelpProject project, string output)
    {
        SafePaths.CheckNoLinks(output);
        if (SafePaths.Same(output, project.RootPath) || SafePaths.IsWithin(project.RootPath, output) ||
            SafePaths.IsWithin(output, project.ContentPath) || SafePaths.IsWithin(output, Path.Combine(project.RootPath, "Project")) ||
            SafePaths.IsWithin(output, Path.Combine(project.RootPath, "RCode")) || SafePaths.IsWithin(output, Path.Combine(project.RootPath, ".help-editor-recovery")) ||
            SafePaths.IsWithin(output, @"C:\Develop\SD3Help\StatsDirect") ||
            SafePaths.IsWithin(output, @"C:\Users\ieb21\source\repos\statsdirect") || SafePaths.IsWithin(output, @"C:\temp"))
            throw new InvalidOperationException("Choose a separate prototype build folder, outside source files and released artifacts.");
    }

    private static string FlareIndexNavigation(string topicPath, string? logoPath, string? iconPath)
    {
        var title = "StatsDirect Help";
        var identity = logoPath == null ? WebUtility.HtmlEncode(title) : "<img class=\"help-logo\" src=\"" + WebUtility.HtmlEncode(logoPath) + "\" width=\"228\" height=\"33\" alt=\"StatsDirect StatisticalHelp\">";
        return "<!doctype html><html lang=\"en\" data-help-skin=\"flare\"><head><meta charset=\"utf-8\"><title>" + title + "</title>" + ViewerFiles.IconLink(iconPath) +
            "<script>location.replace(" + JsonSerializer.Serialize(topicPath) + "+location.search+location.hash);</script></head><body><a href=\"" + WebUtility.HtmlEncode(topicPath) + "\">" + identity + "</a></body></html>";
    }

    private static string RewriteStyleUrl(string value, HelpProject project)
    {
        if (value.StartsWith('#') || value.StartsWith("//", StringComparison.Ordinal) || Uri.TryCreate(value, UriKind.Absolute, out _)) return value;
        int cut = value.IndexOfAny(['?', '#']);
        string local = cut < 0 ? value : value[..cut], suffix = cut < 0 ? "" : value[cut..];
        var full = SafePaths.Resolve(project.ContentPath, "resources/stylesheets/" + Uri.UnescapeDataString(local), allowDot: true);
        return Relative(ScopedStylesPath, Path.GetRelativePath(project.ContentPath, full).Replace('\\', '/')) + suffix;
    }

    private static string StandaloneTopicNavigation(string home, string topicPath)
    {
        // Direct links get the same navigation shell as the contents page.
        // Embedded topics and explicit engineering previews remain bare, so
        // links inside the viewer cannot create nested navigation panes.
        return "if(window.parent===window&&new URLSearchParams(location.search).get('content')!=='1'){location.replace(" +
            JsonSerializer.Serialize(home) + "+'#topic='+encodeURIComponent(" + JsonSerializer.Serialize(topicPath) + "+location.hash));}";
    }

    private static string FindMathJax(string assets)
    {
        foreach (var candidate in new[] { Path.Combine(assets, "mathjax"), Path.Combine(assets, "mathjax", "es5"), Path.Combine(assets, "vendor", "mathjax"), Path.Combine(assets, "vendor", "mathjax", "es5") })
            if (File.Exists(Path.Combine(candidate, "tex-mml-svg.js"))) return candidate;
        throw new FileNotFoundException("Offline MathJax assets are missing (mathjax/tex-mml-svg.js).");
    }

    private static bool Included(string? conditions, HelpBuildTarget target)
    {
        if (conditions is null) return true;
        var parts = conditions.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
        return !parts.Any(p => p.Equals(target == HelpBuildTarget.Desktop ? "Primary.WEBHELP" : "Primary.SWHELP", StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyConditions(XElement root, HelpBuildTarget target)
    {
        foreach (var e in root.Descendants().ToList())
            if (HelpSourceNamespaces.Attributes(e, "conditions").Any(attribute => !Included(attribute.Value, target))) e.Remove();
    }

    private static void Transform(XElement body, string path, HelpProject project, List<string> warnings, EquationArtworkCatalog artwork, string? linkOutputPath = null)
    {
        foreach (var e in body.Descendants().ToList())
        {
            if (e.Parent is null) continue;
            // Source content is documentation, not executable page code. The
            // builder supplies its own offline equation/navigation scripts.
            if (e.Name.LocalName.Equals("script", StringComparison.OrdinalIgnoreCase)) { e.Remove(); continue; }
            if (HelpSourceNamespaces.IsAuthoringNamespace(e.Name.NamespaceName))
            {
                switch (e.Name.LocalName)
                {
                    case "keyword": e.Remove(); continue;
                    case "equation":
                        var original = artwork.TryCreateEquation(e, linkOutputPath ?? path);
                        if (original != null) { e.ReplaceWith(original); continue; }
                        var equation = new XElement("span", new XAttribute("class", "help-equation help-equation-live"));
                        if (e.Attribute("style") is { } style) equation.SetAttributeValue("style", style.Value);
                        if (e.HasElements) equation.Add(e.Nodes().Select(CloneNode));
                        else equation.Add(new XText(PrepareDelimitedEquation(e.Value)));
                        e.ReplaceWith(equation); continue;
                    case "dropDown": e.Name = "details"; break;
                    case "dropDownHead": e.Name = "summary"; break;
                    case "dropDownHotspot": e.ReplaceWith(e.Nodes().Select(CloneNode)); continue;
                    case "dropDownBody": e.Name = "div"; break;
                    default:
                        warnings.Add("Unsupported help authoring element " + e.Name.LocalName + " in " + path + "; content retained.");
                        e.Name = "span"; break;
                }
            }
        }
        // Include the fragment root (in particular the shared footer) and any
        // cloned equation children. Reader HTML has no authoring vocabulary.
        foreach (var attribute in body.DescendantsAndSelf().Attributes().Where(attribute =>
            attribute.IsNamespaceDeclaration && HelpSourceNamespaces.IsAuthoringNamespace(attribute.Value) ||
            HelpSourceNamespaces.IsAuthoringNamespace(attribute.Name.NamespaceName) ||
            attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)).ToList()) attribute.Remove();
        // Flare's hotspot owns the chevron immediately before its label. The
        // equivalent summary must not expose source-formatting indentation as
        // an extra visible space between that chevron and the R logo.
        foreach (var summary in body.Descendants("summary"))
        {
            while (summary.FirstNode is XText leading && string.IsNullOrWhiteSpace(leading.Value)) leading.Remove();
            while (summary.LastNode is XText trailing && string.IsNullOrWhiteSpace(trailing.Value)) trailing.Remove();
        }
        foreach (var attribute in body.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName is "href" or "src").ToList())
        {
            if (attribute.Parent?.Attribute("class")?.Value.Split(' ').Contains("help-equation-original") == true) continue;
            var value = attribute.Value;
            if (value.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) { attribute.Remove(); continue; }
            if (attribute.Name.LocalName == "href" && attribute.Parent?.Name.LocalName.Equals("a", StringComparison.OrdinalIgnoreCase) == true &&
                Uri.TryCreate(value, UriKind.Absolute, out var external) && external.Scheme is "http" or "https")
            {
                // Topic pages live in the help viewer's iframe. External pages
                // belong in a new browser window, not inside that frame.
                attribute.Parent.SetAttributeValue("target", "_blank");
                var rel = ((string?)attribute.Parent.Attribute("rel") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
                foreach (var token in new[] { "noopener", "noreferrer" }) if (!rel.Contains(token, StringComparer.OrdinalIgnoreCase)) rel.Add(token);
                attribute.Parent.SetAttributeValue("rel", string.Join(" ", rel));
                continue;
            }
            if (value.StartsWith('#') || value.Contains(':') || value.StartsWith("//")) continue;
            try
            {
                var cut = value.IndexOfAny(['#', '?']);
                var suffix = cut < 0 ? "" : value[cut..];
                var source = cut < 0 ? value : value[..cut];
                if (string.IsNullOrWhiteSpace(source)) continue;
                var logical = source.Replace('\\', '/');
                if (logical.StartsWith("/Content/", StringComparison.OrdinalIgnoreCase)) logical = logical[9..];
                else if (logical.StartsWith('/')) logical = logical.TrimStart('/');
                else logical = Path.Combine(Path.GetDirectoryName(path) ?? "", logical);
                var full = SafePaths.Resolve(project.ContentPath, Uri.UnescapeDataString(logical), allowDot: true);
                if (!File.Exists(full)) warnings.Add("Missing link from " + path + ": " + value);
                var relative = Path.GetRelativePath(project.ContentPath, full).Replace('\\', '/').ToLowerInvariant();
                attribute.Value = Relative(linkOutputPath ?? path, relative) + suffix;
                if (attribute.Parent?.Name.LocalName == "a" && (string?)attribute.Parent.Attribute("target") == "_popup") attribute.Parent.SetAttributeValue("target", null);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException) { warnings.Add("Invalid local link from " + path + ": " + value); }
        }
        // XML permits <div/>; HTML does not. Emit explicit closing tags for
        // all non-void elements so an empty source block cannot absorb a page.
        foreach (var e in body.DescendantsAndSelf())
            if (e.IsEmpty && e.Name.LocalName.ToLowerInvariant() is not ("area" or "base" or "br" or "col" or "embed" or "hr" or "img" or "input" or "link" or "meta" or "param" or "source" or "track" or "wbr")) e.Add(new XText(""));
        // CDATA is ordinary text in XHTML/XML, but an HTML parser treats its
        // delimiters as a bogus comment outside MathML. Preserve its visible
        // text (including R operators and indentation) using normal escaping.
        foreach (var cdata in body.DescendantNodes().OfType<XCData>().ToList()) cdata.ReplaceWith(new XText(cdata.Value));
    }

    private static XNode CloneNode(XNode node) => node switch
    {
        XElement e => new XElement(e), XCData c => new XCData(c.Value), XText t => new XText(t.Value), XComment c => new XComment(c.Value), XProcessingInstruction p => new XProcessingInstruction(p.Target, p.Data), _ => throw new InvalidDataException("Unsupported XML node in a topic.")
    };

    // Flare's MathJax 2 accepted top-level \\\\ as visual row breaks. MathJax 3
    // needs an explicit environment. Adapt generated output only; source XML,
    // mathematical symbols, legacy font switches and sized slashes stay intact.
    public static string PrepareLatexForRendering(string latex)
    {
        ArgumentNullException.ThrowIfNull(latex);
        var lines = new List<string>();
        int start = 0, braces = 0, environments = 0;
        for (int i = 0; i < latex.Length; i++)
        {
            char c = latex[i];
            if (c == '\\')
            {
                var environment = Regex.Match(latex[i..], @"^\\(begin|end)\s*\{([^}]+)\}");
                if (environment.Success)
                {
                    environments += environment.Groups[1].Value == "begin" ? 1 : -1;
                    i += environment.Length - 1;
                    continue;
                }
                if (i + 1 < latex.Length && latex[i + 1] == '\\')
                {
                    if (braces == 0 && environments == 0) { lines.Add(latex[start..i].Trim()); start = i + 2; }
                    i++;
                    continue;
                }
                if (i + 1 < latex.Length && latex[i + 1] is '{' or '}') i++;
            }
            else if (c == '{') braces++;
            else if (c == '}') braces--;
        }
        if (lines.Count == 0) return latex;
        lines.Add(latex[start..].Trim());
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return "\\begin{array}{l}\n" + string.Join(" \\\\\n", lines.Select(line => "\\displaystyle " + line)) + "\n\\end{array}";
    }

    private static string PrepareDelimitedEquation(string source)
    {
        var text = source.Trim();
        var delimiter = text.StartsWith("$$", StringComparison.Ordinal) && text.EndsWith("$$", StringComparison.Ordinal) ? "$$" : "$";
        if (!text.StartsWith(delimiter, StringComparison.Ordinal) || !text.EndsWith(delimiter, StringComparison.Ordinal)) return source;
        var latex = text[delimiter.Length..^delimiter.Length];
        var normalized = PrepareLatexForRendering(latex);
        return normalized == latex ? source : delimiter + normalized + delimiter;
    }

    private static string Relative(string from, string to)
    {
        var directory = Path.GetDirectoryName(from);
        return Path.GetRelativePath(string.IsNullOrEmpty(directory) ? "." : directory, to).Replace('\\', '/').ToLowerInvariant();
    }
    private static string NormalizeText(string value) => Regex.Replace(value, @"\s+", " ").Trim();

    private static List<ViewerNode> ConvertToc(IEnumerable<TocNode> nodes, HelpBuildTarget target, Dictionary<string, TopicInfo> topics, List<string> warnings)
    {
        var result = new List<ViewerNode>();
        foreach (var n in nodes)
        {
            if (n.OriginalAttributes.Any(attribute => attribute.Key.LocalName == "conditions" &&
                HelpSourceNamespaces.IsAuthoringNamespace(attribute.Key.NamespaceName) && !Included(attribute.Value, target))) continue;
            string? link = n.Link;
            if (link is not null)
            {
                var cut = link.IndexOfAny(['#', '?']);
                var part = (cut < 0 ? link : link[..cut]).ToLowerInvariant();
                if (!topics.ContainsKey(part)) { warnings.Add("Missing contents topic: " + link); link = null; }
                else link = part + (cut < 0 ? "" : link[cut..]);
            }
            result.Add(new ViewerNode(n.Title, link, ConvertToc(n.Children, target, topics, warnings)));
        }
        return result;
    }

    private static SearchData CreateIndex(List<SearchDocument> documents)
    {
        var terms = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
        void Add(string text, int id, int weight)
        {
            foreach (var token in Tokens(text).Distinct())
            {
                if (!terms.TryGetValue(token, out var postings)) terms[token] = postings = [];
                postings[id] = postings.GetValueOrDefault(id) + weight;
            }
        }
        foreach (var doc in documents) { Add(doc.Text, doc.Id, 1); Add(doc.Keywords, doc.Id, 6); Add(doc.Title, doc.Id, 10); }
        return new SearchData(documents.Select(d => new SearchDisplay(d.Url, d.Title, d.Text.Length <= 280 ? d.Text : d.Text[..280] + "…")).ToList(), terms.ToDictionary(t => t.Key, t => t.Value.Select(p => new[] { p.Key, p.Value }).ToArray()));
    }

    private static IEnumerable<string> Tokens(string text)
    {
        var lower = new string(text.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
        return Regex.Matches(lower, @"[\p{L}\p{Nd}]+").Select(m => m.Value).Where(t => t.Length > 1 || char.IsDigit(t[0]));
    }

    private static string CreateAliases(HelpProject project, Dictionary<string, TopicInfo> topics, List<string> warnings)
    {
        var aliases = Path.Combine(project.RootPath, "Project/Advanced/STATSDIRECT.flali");
        var header = Path.Combine(project.RootPath, "Project/Advanced/SD3ContextID.h");
        var ids = File.Exists(header) ? Regex.Matches(File.ReadAllText(header), @"(?m)^\s*#define\s+(\w+)\s+(\d+)\b").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value) : [];
        var root = new XElement("CatapultAliasFile", new XAttribute("Version", "1"));
        if (File.Exists(aliases)) foreach (var map in XDocument.Load(aliases).Descendants("Map"))
        {
            var name = (string?)map.Attribute("Name") ?? "";
            var link = HelpProject.NormalizeTocLink((string?)map.Attribute("Link")) ?? "";
            var cut = link.IndexOfAny(['#', '?']);
            var part = (cut < 0 ? link : link[..cut]).ToLowerInvariant();
            if (!topics.ContainsKey(part)) { warnings.Add("Missing context-help topic: " + link); continue; }
            var element = new XElement("Map", new XAttribute("Name", name), new XAttribute("Link", part + (cut < 0 ? "" : link[cut..])));
            if (ids.TryGetValue(name, out var id)) element.Add(new XAttribute("ResolvedId", id));
            root.Add(element);
        }
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
    }

    private record ViewerNode(string Title, string? Link, List<ViewerNode> Children);
    private record SearchDocument(int Id, string Url, string Title, string Keywords, string Text);
    private record SearchDisplay(string Url, string Title, string Excerpt);
    private record SearchData(List<SearchDisplay> Documents, Dictionary<string, int[][]> Terms);
    private record OutputReceipt(string[] Files);
}
