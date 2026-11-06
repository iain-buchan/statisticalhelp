using System.Xml.Linq;

namespace StatsDirect.HelpEditor.Core;

/// <summary>
/// The help-source policy for executable attributes and embedded page content.
/// This is deliberately not a general-purpose sanitizer for arbitrary HTML or CSS.
/// Escaped examples, R text, MathML, known inert JSON data blocks and the inert
/// legacy head-script placeholders are documentation and remain unchanged.
/// </summary>
public static class HelpSourceContent
{
    private static readonly HashSet<string> UrlAttributes = new(StringComparer.OrdinalIgnoreCase)
    { "href", "src", "action", "formaction", "background", "cite", "longdesc", "poster", "data" };

    public static void ValidateForSave(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Root == null) return;
        foreach (var element in document.Root.DescendantsAndSelf())
        {
            if (IsActiveElement(element))
                throw new InvalidDataException("Help pages cannot contain executable script or embedded iframe/object/embed content. Remove the element before saving: " + element.Name.LocalName);
            foreach (var attribute in element.Attributes())
                if (IsActiveAttribute(attribute))
                    throw new InvalidDataException("Help pages cannot contain event-handler attributes or javascript URLs. Remove the attribute before saving: " + attribute.Name);
        }
    }

    /// <summary>Clean a parsed import or generated fragment; the original source on disk is never changed.</summary>
    public static int StripActiveContent(XElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        int removed = 0;
        foreach (var element in root.Descendants().Where(IsActiveElement).ToArray())
        { element.Remove(); removed++; }
        foreach (var attribute in root.DescendantsAndSelf().Attributes().Where(IsActiveAttribute).ToArray())
        { attribute.Remove(); removed++; }
        return removed;
    }

    private static bool IsActiveAttribute(XAttribute attribute) => !attribute.IsNamespaceDeclaration &&
        (attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
         UrlAttributes.Contains(attribute.Name.LocalName) && IsJavaScriptUrl(attribute.Value));

    private static bool IsJavaScriptUrl(string value)
    {
        // The XML parser has already decoded character references. Match the
        // URL parser's ASCII tab/newline removal and leading C0/space trimming.
        string normalized = value.Replace("\t", "").Replace("\r", "").Replace("\n", "");
        int first = 0;
        while (first < normalized.Length && normalized[first] <= ' ') first++;
        return normalized.AsSpan(first).StartsWith("javascript:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActiveElement(XElement element)
    {
        string name = element.Name.LocalName.ToLowerInvariant();
        if (name is "iframe" or "object" or "embed") return true;
        if (name != "script") return false;
        bool hasSource = element.Attributes().Any(attribute => attribute.Name.LocalName.Equals("src", StringComparison.OrdinalIgnoreCase));
        if (!hasSource && IsInertJsonData(element)) return false;
        // Allow only the known inert Flare scaffold, not a JavaScript lexer:
        // line terminators and comment delimiters must never hide executable code.
        string text = element.Value.Trim();
        return element.Parent?.Name.LocalName.Equals("head", StringComparison.OrdinalIgnoreCase) != true ||
            hasSource ||
            element.HasElements || text.Length != 0 && text is not ("/*<![CDATA[*//*]]>*/" or "/**//**/");
    }

    private static bool IsInertJsonData(XElement element)
    {
        if (element.Name.NamespaceName is not ("" or "http://www.w3.org/1999/xhtml")) return false;
        var types = element.Attributes().Where(attribute => attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (types.Length != 1) return false; // Refuse ambiguous XML attributes that collapse in HTML.
        string type = types[0].Value.Trim(' ', '\t', '\r', '\n', '\f');
        return type.Equals("application/json", StringComparison.OrdinalIgnoreCase) || type.Equals("application/ld+json", StringComparison.OrdinalIgnoreCase);
    }
}
