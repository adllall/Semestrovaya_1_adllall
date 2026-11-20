using System.Net;
using System.Text.RegularExpressions;

namespace Semestrovaya.Templating;

public static partial class DescriptionFormatter
{
    [GeneratedRegex(@"\[(b|i)\](.*?)\[/\1\]", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Markup();

    public static bool HasText(string description) =>
        !string.IsNullOrWhiteSpace(Markup().Replace(description, "$2"));

    public static EncodedHtml Format(string description)
    {
        string encoded = WebUtility.HtmlEncode(description);
        return new EncodedHtml(Markup().Replace(encoded, match =>
        {
            string tag = match.Groups[1].Value == "b" ? "strong" : "em";
            return $"<{tag}>{match.Groups[2].Value}</{tag}>";
        }));
    }
}
