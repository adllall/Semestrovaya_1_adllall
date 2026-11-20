using System.Collections;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Semestrovaya.Templating;

public class TemplateEngine
{
    private static readonly Regex Tokens = new(@"\{\{\{([^{}]+)\}\}\}|\{\{([^{}]+)\}\}", RegexOptions.Compiled);

    public string Render(string template, object? model)
    {
        var matches = Tokens.Matches(template);
        int tokenIndex = 0;
        int position = 0;

        List<Node> ReadBlock(out string? stop)
        {
            var nodes = new List<Node>();
            while (tokenIndex < matches.Count)
            {
                var match = matches[tokenIndex++];
                if (match.Index > position)
                    nodes.Add(new TextNode(template[position..match.Index]));
                position = match.Index + match.Length;

                if (match.Groups[1].Success)
                {
                    nodes.Add(new ValueNode(match.Groups[1].Value.Trim(), true));
                    continue;
                }

                string token = match.Groups[2].Value.Trim();
                if (token is "else" or "/if" or "/each")
                {
                    stop = token;
                    return nodes;
                }

                if (token.StartsWith("#each ", StringComparison.Ordinal))
                {
                    var body = ReadBlock(out stop);
                    if (stop != "/each")
                        throw new FormatException("Для блока each требуется закрывающий тег.");
                    nodes.Add(new EachNode(token[6..].Trim(), body));
                }
                else if (token.StartsWith("#if ", StringComparison.Ordinal))
                {
                    var yes = ReadBlock(out stop);
                    var no = stop == "else" ? ReadBlock(out stop) : [];
                    if (stop != "/if")
                        throw new FormatException("Для блока if требуется закрывающий тег.");
                    nodes.Add(new IfNode(token[4..].Trim(), yes, no));
                }
                else
                {
                    nodes.Add(new ValueNode(token, false));
                }
            }
            if (position < template.Length)
            {
                nodes.Add(new TextNode(template[position..]));
                position = template.Length;
            }
            stop = null;
            return nodes;
        }

        var root = ReadBlock(out string? unmatched);
        if (unmatched != null)
            throw new FormatException("Неожиданный закрывающий тег шаблона.");

        var html = new StringBuilder(template.Length);
        RenderNodes(root, model, html);
        return html.ToString();
    }

    private static void RenderNodes(IEnumerable<Node> nodes, object? model, StringBuilder html)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    html.Append(text.Text);
                    break;
                case ValueNode variable:
                    object? value = GetValue(model, variable.Path);
                    html.Append(variable.Raw && value is EncodedHtml safe
                        ? safe.Value
                        : WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""));
                    break;
                case IfNode conditional:
                    RenderNodes(IsTruthy(GetValue(model, conditional.Path)) ? conditional.Yes : conditional.No, model, html);
                    break;
                case EachNode loop when GetValue(model, loop.Path) is IEnumerable items and not string:
                    foreach (object? item in items)
                        RenderNodes(loop.Body, item, html);
                    break;
            }
        }
    }

    private static object? GetValue(object? current, string path)
    {
        foreach (string part in path.Split('.'))
        {
            if (current == null)
                return null;

            if (current is IDictionary dictionary)
            {
                current = dictionary[part];
                continue;
            }

            var property = current.GetType().GetProperty(part,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            current = property?.GetValue(current);
        }

        return current;
    }

    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool boolean => boolean,
        string text => text.Length > 0,
        int number => number != 0,
        long number => number != 0,
        IEnumerable items => items.Cast<object?>().Any(),
        _ => true
    };

    private abstract record Node;
    private sealed record TextNode(string Text) : Node;
    private sealed record ValueNode(string Path, bool Raw) : Node;
    private sealed record IfNode(string Path, List<Node> Yes, List<Node> No) : Node;
    private sealed record EachNode(string Path, List<Node> Body) : Node;
}

public sealed record EncodedHtml(string Value);
