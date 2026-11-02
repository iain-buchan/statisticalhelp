using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace StatsDirect.HelpEditor.Core;

/// <summary>The editor's authoring vocabulary and its supported Flare import namespace.</summary>
public static class HelpSourceNamespaces
{
    public const string CurrentUri = "https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd";
    public const string LegacyUri = "http://www.madcapsoftware.com/Schemas/MadCap.xsd";
    public const string PreferredPrefix = "sdh";
    public static readonly XNamespace Current = CurrentUri;
    public static readonly XNamespace Legacy = LegacyUri;

    public static bool IsAuthoringNamespace(string uri) => uri is CurrentUri or LegacyUri;
    internal static bool IsElement(XElement element, string localName) =>
        element.Name.LocalName == localName && IsAuthoringNamespace(element.Name.NamespaceName);

    internal static IEnumerable<XAttribute> Attributes(XElement element, string localName) =>
        element.Attributes().Where(attribute => attribute.Name.LocalName == localName && IsAuthoringNamespace(attribute.Name.NamespaceName));

    // Existing verified artwork addresses include the original namespace. Keep
    // those addresses stable when the equivalent authoring vocabulary changes.
    internal static string ArtworkNamespace(string uri) => IsAuthoringNamespace(uri) ? LegacyUri : uri;

    /// <summary>Change namespace declarations alone, retaining source spelling, comments, entities and whitespace.</summary>
    internal static string NormalizeSource(string source)
    {
        var lineStarts = new List<int> { 0 };
        for (int index = 0; index < source.Length; index++)
        {
            if (source[index] == '\r')
            {
                if (index + 1 < source.Length && source[index + 1] == '\n') index++;
                lineStarts.Add(index + 1);
            }
            else if (source[index] == '\n') lineStarts.Add(index + 1);
        }
        var replacements = new List<(int Start, int Length)>();
        using (var reader = XmlReader.Create(new StringReader(source), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 }))
        {
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                while (reader.MoveToNextAttribute())
                {
                    if (reader.NamespaceURI != XNamespace.Xmlns.NamespaceName || reader.Value != LegacyUri) continue;
                    var location = (IXmlLineInfo)reader;
                    int offset = lineStarts[location.LineNumber - 1] + location.LinePosition - 1 + reader.Name.Length;
                    while (char.IsWhiteSpace(source[offset])) offset++;
                    offset++; // '='; source has already been checked by the XML reader.
                    while (char.IsWhiteSpace(source[offset])) offset++;
                    char quote = source[offset++];
                    replacements.Add((offset, source.IndexOf(quote, offset) - offset));
                }
                reader.MoveToElement();
            }
        }
        if (replacements.Count == 0) return source;
        var result = new StringBuilder(source);
        foreach (var replacement in replacements.OrderByDescending(replacement => replacement.Start))
            result.Remove(replacement.Start, replacement.Length).Insert(replacement.Start, CurrentUri);
        string normalized = result.ToString();
        // A mixed import can legally contain the same attribute in both
        // vocabularies. Do not silently discard either value during migration.
        try { _ = HelpProject.Parse(normalized); }
        catch (XmlException exception)
        {
            throw new InvalidDataException("The source contains conflicting legacy and StatsDirect authoring attributes. Resolve the duplicate attributes before saving.", exception);
        }
        return normalized;
    }
}
