using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CentralPackageMigrator.Core.Editing;

/// <summary>
/// Raw XML text plus a line table, so XDocument line info can be mapped back to exact
/// character offsets. All edits are made against the original text, which keeps every
/// byte outside the edited spans untouched (indentation, comments, line endings).
/// </summary>
internal sealed class XmlSource
{
    private readonly int[] _lineStarts;

    private XmlSource(string text, XDocument document)
    {
        Text = text;
        Document = document;
        _lineStarts = ComputeLineStarts(text);
    }

    public string Text { get; }

    public XDocument Document { get; }

    public XElement Root => Document.Root!;

    /// <summary>Parses <paramref name="text"/>; throws <see cref="XmlException"/> on malformed XML.</summary>
    public static XmlSource Parse(string text)
    {
        var document = XDocument.Parse(text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        if (document.Root is null)
        {
            throw new XmlException("Document has no root element.");
        }

        return new XmlSource(text, document);
    }

    /// <summary>Offset of the '&lt;' that starts <paramref name="element"/>.</summary>
    public int ElementStart(XElement element)
    {
        int offset = OffsetOf(element);

        // XmlReader reports the position of the element name; step back to '<'.
        while (offset > 0 && Text[offset] != '<')
        {
            offset--;
        }

        return offset;
    }

    /// <summary>Offset just past the start tag's '&gt;'.</summary>
    public int StartTagEnd(XElement element) => FindTagEnd(ElementStart(element));

    /// <summary>Offset just past the end of the element (end tag or self-closing start tag).</summary>
    public int ElementEnd(XElement element)
    {
        int startTagEnd = StartTagEnd(element);
        if (Text[startTagEnd - 2] == '/')
        {
            return startTagEnd;
        }

        int depth = 0;
        int i = startTagEnd;
        while (i < Text.Length)
        {
            int lt = Text.IndexOf('<', i);
            if (lt < 0)
            {
                break;
            }

            if (StartsWith(lt, "<!--"))
            {
                i = IndexAfter(lt + 4, "-->");
            }
            else if (StartsWith(lt, "<![CDATA["))
            {
                i = IndexAfter(lt + 9, "]]>");
            }
            else if (StartsWith(lt, "<?"))
            {
                i = IndexAfter(lt + 2, "?>");
            }
            else if (StartsWith(lt, "<!"))
            {
                i = IndexAfter(lt + 2, ">");
            }
            else if (Text[lt + 1] == '/')
            {
                int gt = IndexAfter(lt, ">");
                if (depth == 0)
                {
                    return gt;
                }

                depth--;
                i = gt;
            }
            else
            {
                int tagEnd = FindTagEnd(lt);
                if (Text[tagEnd - 2] != '/')
                {
                    depth++;
                }

                i = tagEnd;
            }
        }

        throw new XmlException($"Could not find the end of element <{element.Name.LocalName}>.");
    }

    /// <summary>Offset of the '&lt;' of the element's end tag. Only valid for non-self-closing elements.</summary>
    public int EndTagStart(XElement element) => Text.LastIndexOf('<', ElementEnd(element) - 1);

    /// <summary>Span of an attribute from the start of its name to just past its closing quote.</summary>
    public (int Start, int End) AttributeSpan(XAttribute attribute)
    {
        int start = OffsetOf(attribute);
        int eq = Text.IndexOf('=', start);
        int i = eq + 1;
        while (char.IsWhiteSpace(Text[i]))
        {
            i++;
        }

        char quote = Text[i];
        int close = Text.IndexOf(quote, i + 1);
        return (start, close + 1);
    }

    /// <summary>Span of an attribute's value, excluding quotes.</summary>
    public (int Start, int End) AttributeValueSpan(XAttribute attribute)
    {
        var (_, end) = AttributeSpan(attribute);
        int close = end - 1;
        int open = Text.LastIndexOf(Text[close], close - 1);
        return (open + 1, close);
    }

    /// <summary>Span of an element's inner content (between start and end tags).</summary>
    public (int Start, int End) InnerSpan(XElement element) => (StartTagEnd(element), EndTagStart(element));

    /// <summary>
    /// Span that removes the element together with its whole line when it is the only
    /// content on that line; otherwise just the element itself.
    /// </summary>
    public (int Start, int End) ElementLineSpan(XElement element)
    {
        int start = ElementStart(element);
        int end = ElementEnd(element);

        int lineStart = start;
        while (lineStart > 0 && Text[lineStart - 1] is ' ' or '\t')
        {
            lineStart--;
        }

        int lineEnd = end;
        while (lineEnd < Text.Length && Text[lineEnd] is ' ' or '\t')
        {
            lineEnd++;
        }

        bool startsLine = lineStart == 0 || Text[lineStart - 1] is '\n' or '\r';
        bool endsLine = lineEnd == Text.Length || Text[lineEnd] is '\n' or '\r';
        if (!startsLine || !endsLine)
        {
            return (start, end);
        }

        if (lineEnd < Text.Length && Text[lineEnd] == '\r')
        {
            lineEnd++;
        }

        if (lineEnd < Text.Length && Text[lineEnd] == '\n')
        {
            lineEnd++;
        }

        return (lineStart, lineEnd);
    }

    /// <summary>Whitespace immediately preceding <paramref name="offset"/> on the same line.</summary>
    public string IndentationAt(int offset)
    {
        int i = offset;
        while (i > 0 && Text[i - 1] is ' ' or '\t')
        {
            i--;
        }

        bool startsLine = i == 0 || Text[i - 1] is '\n' or '\r';
        return startsLine ? Text[i..offset] : string.Empty;
    }

    /// <summary>True if only spaces/tabs precede <paramref name="offset"/> on its line.</summary>
    public bool IsFirstOnLine(int offset)
    {
        int i = offset;
        while (i > 0 && Text[i - 1] is ' ' or '\t')
        {
            i--;
        }

        return i == 0 || Text[i - 1] is '\n' or '\r';
    }

    /// <summary>Offset of the start of the line containing <paramref name="offset"/>.</summary>
    public int LineStartOf(int offset)
    {
        int i = offset;
        while (i > 0 && Text[i - 1] is not '\n' and not '\r')
        {
            i--;
        }

        return i;
    }

    /// <summary>1-based line number of an element.</summary>
    public static int LineOf(XObject node) =>
        node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;

    private int OffsetOf(XObject node)
    {
        var info = (IXmlLineInfo)node;
        if (!info.HasLineInfo())
        {
            throw new InvalidOperationException("XML node has no line information.");
        }

        return _lineStarts[info.LineNumber - 1] + info.LinePosition - 1;
    }

    private int FindTagEnd(int lt)
    {
        char quote = '\0';
        for (int i = lt + 1; i < Text.Length; i++)
        {
            char c = Text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i + 1;
            }
        }

        throw new XmlException("Unterminated tag.");
    }

    private bool StartsWith(int offset, string value) =>
        string.CompareOrdinal(Text, offset, value, 0, value.Length) == 0;

    private int IndexAfter(int from, string value)
    {
        int index = Text.IndexOf(value, from, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new XmlException($"Expected '{value}'.");
        }

        return index + value.Length;
    }

    // XmlReader treats "\r\n", "\n" and a lone "\r" as line breaks.
    private static int[] ComputeLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                starts.Add(i + 1);
            }
            else if (c == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return [.. starts];
    }
}

/// <summary>
/// A set of non-overlapping replacements applied to a string in one pass.
/// </summary>
internal sealed class TextEdits
{
    private readonly List<(int Start, int End, string Replacement)> _edits = [];

    public int Count => _edits.Count;

    public void Replace(int start, int end, string replacement)
    {
        // Identical edits (e.g. two operations targeting the same attribute) collapse into one.
        if (_edits.Any(e => e.Start == start && e.End == end && e.Replacement == replacement))
        {
            return;
        }

        _edits.Add((start, end, replacement));
    }

    public void Remove(int start, int end) => Replace(start, end, string.Empty);

    public void Insert(int offset, string text) => Replace(offset, offset, text);

    public string ApplyTo(string text)
    {
        var ordered = _edits
            .OrderByDescending(e => e.Start)
            .ThenByDescending(e => e.End)
            .ToList();

        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].End > ordered[i - 1].Start)
            {
                throw new InvalidOperationException("Overlapping edits were produced for the same file.");
            }
        }

        var sb = new StringBuilder(text);
        foreach (var (start, end, replacement) in ordered)
        {
            sb.Remove(start, end - start);
            sb.Insert(start, replacement);
        }

        return sb.ToString();
    }
}

/// <summary>
/// Small text helpers shared by the planner and the edit engine.
/// </summary>
internal static class TextFormat
{
    /// <summary>Returns "\r\n" if the text uses CRLF line endings, otherwise <paramref name="fallback"/> or "\n".</summary>
    public static string DetectNewLine(string text, string? fallback = null)
    {
        int lf = text.IndexOf('\n', StringComparison.Ordinal);
        if (lf < 0)
        {
            return fallback ?? "\n";
        }

        return lf > 0 && text[lf - 1] == '\r' ? "\r\n" : "\n";
    }

    /// <summary>Escapes a value for use inside a double-quoted XML attribute.</summary>
    public static string EscapeAttribute(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>Lowercase hex SHA-256 of the UTF-8 encoding of <paramref name="text"/>.</summary>
    public static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
