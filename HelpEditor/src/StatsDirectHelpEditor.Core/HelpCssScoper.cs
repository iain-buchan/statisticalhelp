using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StatsDirect.HelpEditor.Core;

/// <summary>
/// Scopes an ordinary topic stylesheet to the help article. This deliberately
/// parses rule boundaries, quoted values and nested grouping rules.
/// Font definitions and keyframe names remain stylesheet-global, as in CSSOM.
/// </summary>
public static class HelpCssScoper
{
    private static readonly HashSet<string> Groups = new(StringComparer.OrdinalIgnoreCase)
        { "media", "supports", "layer", "container", "document", "-moz-document", "scope", "starting-style" };
    private static readonly HashSet<string> Definitions = new(StringComparer.OrdinalIgnoreCase)
        { "font-face", "font-feature-values", "font-palette-values", "counter-style", "property", "page", "viewport", "-ms-viewport" };

    public static string Scope(string css, string rootSelector = "#help-topic-body", Func<string, string>? rewriteUrl = null)
    {
        ArgumentNullException.ThrowIfNull(css);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootSelector);
        if (rootSelector.IndexOfAny([',', ';', '{', '}']) >= 0)
            throw new ArgumentException("Use one root selector without rule delimiters.", nameof(rootSelector));
        rootSelector = rootSelector.Trim();
        // Validate the same string/bracket syntax used by authored selectors.
        SplitSelectors(rootSelector);
        var parser = new Parser(css, rootSelector);
        var scoped = parser.Rules(false);
        return rewriteUrl is null ? scoped : RewriteUrls(scoped, rewriteUrl);
    }

    private sealed class Parser(string css, string root)
    {
        private int at;

        public string Rules(bool inside)
        {
            var output = new StringBuilder();
            while (true)
            {
                int trivia = at;
                while (at < css.Length)
                {
                    if (char.IsWhiteSpace(css[at]) || css[at] == '\uFEFF') at++;
                    else if (CommentAt(css, at)) at = EndComment(css, at);
                    else break;
                }
                output.Append(css, trivia, at - trivia);
                if (at == css.Length)
                {
                    if (inside) throw Bad("Missing closing rule brace", at);
                    return output.ToString();
                }
                if (css[at] == '}')
                {
                    if (!inside) throw Bad("Unexpected closing rule brace", at);
                    at++; return output.ToString();
                }
                int start = at;
                var stack = new Stack<char>();
                while (at < css.Length)
                {
                    char c = css[at];
                    if (CommentAt(css, at)) { at = EndComment(css, at); continue; }
                    if (c is '\'' or '"') { at = EndString(css, at); continue; }
                    if (c == '\\') { at = EndEscape(css, at); continue; }
                    if (stack.Count == 0 && c is '{' or ';' or '}') break;
                    Group(stack, c, at); at++;
                }
                if (stack.Count != 0) throw Bad("Unclosed selector or at-rule group", at);
                if (at == css.Length) throw Bad("A CSS rule must end with a block or semicolon", at);
                var prelude = css[start..at].Trim();
                if (prelude.Length == 0) throw Bad("Empty CSS rule", start);
                string? name = prelude[0] == '@' ? AtName(prelude) : null;
                char delimiter = css[at++];
                if (delimiter == '}') throw Bad("Missing opening rule brace", at - 1);
                if (delimiter == ';')
                {
                    if (name is null) throw Bad("An ordinary selector requires a declaration block", start);
                    if (name.Equals("import", StringComparison.OrdinalIgnoreCase))
                        throw Bad("Resolve and scope imported stylesheets before combining topic CSS", start);
                    if (!name.Equals("namespace", StringComparison.OrdinalIgnoreCase)) output.Append(prelude).Append(';');
                    continue;
                }
                if (name is null) output.Append(string.Join(',', SplitSelectors(prelude).Select(s => ScopeSelector(s, root)))).Append('{').Append(RawBlock()).Append('}');
                else if (name.Equals("namespace", StringComparison.OrdinalIgnoreCase)) throw Bad("A namespace rule cannot have a block", start);
                else if (Groups.Contains(name)) output.Append(prelude).Append('{').Append(Rules(true)).Append('}');
                else if (Definitions.Contains(name) || Regex.IsMatch(name, @"^(?:-[\w]+-)?keyframes$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    output.Append(prelude).Append('{').Append(RawBlock()).Append('}');
                else throw Bad("Unsupported block at-rule @" + name, start);
            }
        }

        private string RawBlock()
        {
            int start = at; var stack = new Stack<char>(); stack.Push('{');
            while (at < css.Length)
            {
                char c = css[at];
                if (CommentAt(css, at)) { at = EndComment(css, at); continue; }
                if (c is '\'' or '"') { at = EndString(css, at); continue; }
                if (c == '\\') { at = EndEscape(css, at); continue; }
                Group(stack, c, at); at++;
                if (stack.Count == 0) return css[start..(at - 1)];
            }
            throw Bad("Missing closing declaration brace", at);
        }
    }

    private static string AtName(string prelude)
    {
        int end = 1; while (end < prelude.Length && (char.IsLetterOrDigit(prelude[end]) || prelude[end] is '-' or '_')) end++;
        if (end == 1) throw Bad("Missing at-rule name", 0);
        return prelude[1..end];
    }

    private static List<string> SplitSelectors(string text)
    {
        var selectors = new List<string>(); var stack = new Stack<char>(); int start = 0;
        for (int i = 0; i < text.Length;)
        {
            char c = text[i];
            if (CommentAt(text, i)) { i = EndComment(text, i); continue; }
            if (c is '\'' or '"') { i = EndString(text, i); continue; }
            if (c == '\\') { i = EndEscape(text, i); continue; }
            if (c == ',' && stack.Count == 0) { Add(i); start = ++i; continue; }
            Group(stack, c, i); i++;
        }
        if (stack.Count != 0) throw Bad("Unclosed selector group", text.Length);
        Add(text.Length); return selectors;
        void Add(int end)
        {
            var selector = text[start..end].Trim();
            if (selector.Length == 0) throw Bad("Empty selector in a selector list", start);
            selectors.Add(selector);
        }
    }

    private static string ScopeSelector(string selector, string root)
    {
        var output = new StringBuilder(); var stack = new Stack<char>(); bool replaced = false;
        for (int i = 0; i < selector.Length;)
        {
            int start = i; char c = selector[i];
            if (CommentAt(selector, i)) i = EndComment(selector, i);
            else if (c is '\'' or '"') i = EndString(selector, i);
            else if (c == '\\') i = EndEscape(selector, i);
            else if (stack.Count == 0 && selector.AsSpan(i).StartsWith("body", StringComparison.OrdinalIgnoreCase) &&
                (i == 0 || char.IsWhiteSpace(selector[i - 1]) || selector[i - 1] is '>' or '+' or '~') &&
                (i + 4 == selector.Length || char.IsWhiteSpace(selector[i + 4]) || selector[i + 4] is '>' or '+' or '~' or '.' or '#' or '[' or ':' || CommentAt(selector, i + 4)))
            { output.Append(root); replaced = true; i += 4; continue; }
            else { Group(stack, c, i); i++; }
            output.Append(selector, start, i - start);
        }
        selector = output.ToString();
        if (replaced || selector.Equals(root, StringComparison.Ordinal) || selector.StartsWith(root + " ", StringComparison.Ordinal) || selector.StartsWith(root + ">", StringComparison.Ordinal)) return selector;
        if (selector.AsSpan().StartsWith("html", StringComparison.OrdinalIgnoreCase) &&
            (selector.Length == 4 || !IsIdentifier(selector[4])))
        {
            int end = 4; var groups = new Stack<char>();
            while (end < selector.Length)
            {
                char c = selector[end];
                if (groups.Count == 0 && (char.IsWhiteSpace(c) || c is '>' or '+' or '~')) break;
                if (CommentAt(selector, end)) { end = EndComment(selector, end); continue; }
                if (c is '\'' or '"') { end = EndString(selector, end); continue; }
                if (c == '\\') { end = EndEscape(selector, end); continue; }
                Group(groups, c, end); end++;
            }
            return selector[..end] + " " + root + selector[end..];
        }
        return root + " " + selector;
    }

    private static string RewriteUrls(string css, Func<string, string> rewrite)
    {
        var output = new StringBuilder();
        for (int i = 0; i < css.Length;)
        {
            int start = i;
            if (CommentAt(css, i)) i = EndComment(css, i);
            else if (css[i] is '\'' or '"') i = EndString(css, i);
            else if (css[i] == '\\') i = EndEscape(css, i);
            else if ((i == 0 || !IsIdentifier(css[i - 1])) && css.AsSpan(i).StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            {
                i += 4; while (i < css.Length && char.IsWhiteSpace(css[i])) i++;
                string url;
                if (i < css.Length && css[i] is '\'' or '"')
                {
                    int end = EndString(css, i); url = DecodeEscapes(css[(i + 1)..(end - 1)]); i = end;
                    while (i < css.Length && char.IsWhiteSpace(css[i])) i++;
                    if (i == css.Length || css[i] != ')') throw Bad("Unexpected content after a quoted URL", i);
                }
                else
                {
                    int valueStart = i;
                    while (i < css.Length && css[i] != ')')
                    {
                        if (css[i] == '\\') i = EndEscape(css, i);
                        else if (css[i] is '(' or '\'' or '"') throw Bad("Invalid unquoted URL", i);
                        else i++;
                    }
                    url = DecodeEscapes(css[valueStart..i].Trim());
                }
                if (i == css.Length) throw Bad("Unclosed URL", i);
                var rewritten = rewrite(url) ?? throw new InvalidDataException("A URL rewriter returned null.");
                output.Append("url(\"").Append(EscapeString(rewritten)).Append("\")"); i++; continue;
            }
            else i++;
            output.Append(css, start, i - start);
        }
        return output.ToString();
    }

    private static string DecodeEscapes(string text)
    {
        var output = new StringBuilder();
        for (int i = 0; i < text.Length;)
        {
            if (text[i] != '\\') { output.Append(text[i++]); continue; }
            i++;
            if (i == text.Length) throw Bad("Trailing URL escape", i);
            if (text[i] is '\r' or '\n' or '\f') { if (text[i++] == '\r' && i < text.Length && text[i] == '\n') i++; continue; }
            int start = i; while (i < text.Length && i - start < 6 && Uri.IsHexDigit(text[i])) i++;
            if (i > start)
            {
                int code = int.Parse(text.AsSpan(start, i - start), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                output.Append(code == 0 || code > 0x10ffff || code is >= 0xd800 and <= 0xdfff ? "\ufffd" : char.ConvertFromUtf32(code));
                if (i < text.Length && char.IsWhiteSpace(text[i])) { if (text[i++] == '\r' && i < text.Length && text[i] == '\n') i++; }
            }
            else output.Append(text[i++]);
        }
        return output.ToString();
    }

    private static string EscapeString(string value)
    {
        var output = new StringBuilder();
        foreach (char c in value)
            if (c is '\\' or '"') output.Append('\\').Append(c);
            else if (c < 0x20 || c == 0x7f) output.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
            else output.Append(c);
        return output.ToString();
    }

    private static bool IsIdentifier(char c) => char.IsLetterOrDigit(c) || c is '-' or '_' or '\\' || c >= 0x80;
    private static bool CommentAt(string text, int at) => at + 1 < text.Length && text[at] == '/' && text[at + 1] == '*';
    private static int EndComment(string text, int at)
    {
        int end = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
        return end < 0 ? throw Bad("Unclosed CSS comment", at) : end + 2;
    }
    private static int EndString(string text, int at)
    {
        char quote = text[at++];
        while (at < text.Length)
        {
            if (text[at] == quote) return at + 1;
            if (text[at] == '\\') at = EndEscape(text, at);
            else if (text[at] is '\r' or '\n' or '\f') throw Bad("Unescaped newline in a CSS string", at);
            else at++;
        }
        throw Bad("Unclosed CSS string", at);
    }
    private static int EndEscape(string text, int at)
    {
        if (at + 1 >= text.Length) throw Bad("Trailing CSS escape", at);
        return text[at + 1] == '\r' && at + 2 < text.Length && text[at + 2] == '\n' ? at + 3 : at + 2;
    }
    private static void Group(Stack<char> stack, char c, int at)
    {
        if (c is '(' or '[' or '{') stack.Push(c);
        else if (c is ')' or ']' or '}')
        {
            char opening = c == ')' ? '(' : c == ']' ? '[' : '{';
            if (stack.Count == 0 || stack.Pop() != opening) throw Bad("Unbalanced CSS bracket", at);
        }
    }
    private static InvalidDataException Bad(string message, int at) => new(message + " at character " + at.ToString(CultureInfo.InvariantCulture) + ".");
}
