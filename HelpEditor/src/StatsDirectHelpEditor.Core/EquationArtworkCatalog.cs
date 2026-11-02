using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace StatsDirect.HelpEditor.Core;

/// <summary>
/// Content-addressed artwork for source equations verified against an existing
/// Flare build. A changed or new equation deliberately misses this catalogue.
/// The editor's independent renderer remains responsible for that equation.
/// </summary>
public sealed class EquationArtworkCatalog
{
    public const string OutputFolder = "resources/help-editor-runtime/equation-artwork/";
    private readonly string folder;
    private readonly Dictionary<string, ArtworkEntry> entries;

    private EquationArtworkCatalog(string folder, Dictionary<string, ArtworkEntry> entries)
    {
        this.folder = folder;
        this.entries = entries;
    }

    public static EquationArtworkCatalog Load(string runtimeAssetsDirectory)
    {
        var folder = Path.Combine(Path.GetFullPath(runtimeAssetsDirectory), "equation-artwork");
        var manifest = Path.Combine(folder, "catalog.json");
        if (!File.Exists(manifest)) return new(folder, []);
        SafePaths.CheckNoLinks(manifest);
        var data = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(manifest), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (data is null || data.SchemaVersion != 1) throw new InvalidDataException("Unsupported equation artwork catalogue.");
        var entries = new Dictionary<string, ArtworkEntry>(StringComparer.Ordinal);
        foreach (var entry in data.Entries)
        {
            if (entry.SourceKey != Hash(entry.CanonicalSource) || entry.File != entry.ArtworkSha256 + ".svg" ||
                entry.ArtworkSha256.Length != 64 || entry.ArtworkSha256.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("The equation artwork catalogue contains an invalid content address.");
            var file = Path.Combine(folder, entry.File);
            SafePaths.CheckNoLinks(file);
            if (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))) != entry.ArtworkSha256)
                throw new InvalidDataException("Equation artwork does not match its catalogue: " + entry.File);
            ValidateSvg(file);
            if (entries.TryGetValue(entry.SourceKey, out var existing) && existing.ArtworkSha256 != entry.ArtworkSha256)
                throw new InvalidDataException("The same equation source has conflicting verified artwork.");
            entries[entry.SourceKey] = entry;
        }
        return new(folder, entries);
    }

    /// <summary>Copies only verified, self-contained SVG artwork through the builder's owned-file writer.</summary>
    public void CopyArtwork(Action<string, string> copy)
    {
        foreach (var entry in entries.Values.DistinctBy(e => e.File)) copy(Path.Combine(folder, entry.File), OutputFolder + entry.File);
    }

    public XElement? TryCreateEquation(XElement sourceEquation, string topicOutputPath)
    {
        if (!entries.TryGetValue(SourceKey(sourceEquation), out var entry)) return null;
        var depth = topicOutputPath.Replace('\\', '/').Count(c => c == '/');
        var image = new XElement("img", new XAttribute("src", string.Concat(Enumerable.Repeat("../", depth)) + OutputFolder + entry.File),
            new XAttribute("alt", ""), new XAttribute("aria-hidden", "true"), new XAttribute("class", "help-equation-original"));
        var accessible = new XElement("span", new XAttribute("class", "help-equation-accessible help-equation-live"));
        if (sourceEquation.HasElements)
            accessible.Add(sourceEquation.Elements().Select(e => new XElement(e)));
        else
        {
            var source = sourceEquation.Value.Trim();
            var delimiter = source.StartsWith("$$", StringComparison.Ordinal) ? "$$" : "$";
            accessible.Value = source.Length >= delimiter.Length * 2 && source.StartsWith(delimiter, StringComparison.Ordinal) && source.EndsWith(delimiter, StringComparison.Ordinal)
                ? delimiter + HelpBuilder.PrepareLatexForRendering(source[delimiter.Length..^delimiter.Length]) + delimiter : source;
        }
        // Keep the image's intrinsic SVG size and ordinary inline baseline.
        // A middle-aligned inline-block changes both row height and placement.
        // Put the out-of-flow accessibility content first: after an oversized
        // image its static-position placeholder creates an extra empty line.
        var wrapper = new XElement("span", new XAttribute("class", "help-equation help-equation-artwork help-math-ignore"),
            new XAttribute("data-equation-source-key", entry.SourceKey), accessible, image);
        // Flare applies equation styling to the replaced image. Applying it
        // to an inline wrapper also enlarges the wrapper's text strut, adding
        // descender space below each 12pt image and shifting following rows.
        if (sourceEquation.Attribute("style") is { } style) image.SetAttributeValue("style", style.Value);
        return wrapper;
    }

    public static string SourceKey(XElement equation) => Hash(CanonicalSource(equation));

    // The same length-prefixed UTF-16 representation is implemented in
    // equation-artwork.js. Namespace prefixes, declarations and XML comments
    // do not affect rendering. Equivalent StatsDirect/Flare authoring names
    // retain the original addresses. Actual text, attributes and child order do.
    public static string CanonicalSource(XElement equation)
    {
        var builder = new StringBuilder();
        void Part(string value) { value = value.Replace("\r\n", "\n").Replace('\r', '\n'); builder.Append(value.Length).Append(':').Append(value); }
        void Element(XElement element)
        {
            builder.Append('E'); Part(HelpSourceNamespaces.ArtworkNamespace(element.Name.NamespaceName)); Part(element.Name.LocalName);
            var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration).OrderBy(a => HelpSourceNamespaces.ArtworkNamespace(a.Name.NamespaceName), StringComparer.Ordinal).ThenBy(a => a.Name.LocalName, StringComparer.Ordinal).ToArray();
            builder.Append(attributes.Length).Append(':');
            foreach (var attribute in attributes) { Part(HelpSourceNamespaces.ArtworkNamespace(attribute.Name.NamespaceName)); Part(attribute.Name.LocalName); Part(attribute.Value); }
            var text = new StringBuilder();
            void Flush() { if (text.Length == 0) return; builder.Append('T'); Part(text.ToString()); text.Clear(); }
            foreach (var node in element.Nodes())
                if (node is XText value) text.Append(value.Value);
                else if (node is XElement child) { Flush(); Element(child); }
            Flush(); builder.Append('Z');
        }
        Element(equation); return builder.ToString();
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void ValidateSvg(string file)
    {
        var svg = HelpProject.Parse(File.ReadAllText(file));
        if (svg.Root?.Name != XName.Get("svg", "http://www.w3.org/2000/svg") ||
            svg.Descendants().Any(e => e.Name.LocalName is "script" or "foreignObject" or "iframe" or "image") ||
            svg.Root.DescendantsAndSelf().Attributes().Any(a => a.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
                (a.Name.LocalName is "href" or "src") && !a.Value.StartsWith('#')))
            throw new InvalidDataException("Equation artwork must be a self-contained SVG without executable or external content.");
    }

    public const string Css = """
        .help-equation.help-equation-artwork{display:inline!important;vertical-align:baseline!important;overflow:visible!important}
        .help-equation-artwork>img.help-equation-original{display:inline;vertical-align:baseline;border:0;padding:0;margin:0;max-width:none;height:auto}
        .help-equation-accessible{position:absolute!important;width:1px!important;height:1px!important;padding:0!important;margin:-1px!important;overflow:hidden!important;clip:rect(0,0,0,0)!important;white-space:nowrap!important;border:0!important}
        """;

    private sealed record Catalog(int SchemaVersion, ArtworkEntry[] Entries);
    private sealed record ArtworkEntry(string SourceKey, string CanonicalSource, string File, string ArtworkSha256);
}
