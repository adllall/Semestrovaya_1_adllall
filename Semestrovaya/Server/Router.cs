namespace Semestrovaya.Server;

public class Router
{
    public const string Home = "/";
    public const string Gallery = "/spoiler";
    public const string Card = "/cards/{id}";
    public const string Admin = "/admin";

    private readonly List<Route> _routes = [];

    public void Map(string method, string path, Func<WebContext, Task> handler) =>
        _routes.Add(new Route(method, path, handler));

    public string? GetPagePath(string path)
    {
        string normalized = Normalize(path);
        if (Match(Card, normalized, out string id))
            return int.TryParse(id, out int value) && value > 0 ? "card.html" : null;

        return normalized switch
        {
            Home or Gallery => "spoiler.html",
            Admin => "admin.html",
            _ => null
        };
    }

    public async Task DispatchAsync(WebContext context)
    {
        string path = Normalize(context.Request.Url?.AbsolutePath ?? "/");
        string method = context.Request.HttpMethod is "HEAD" ? "GET" : context.Request.HttpMethod;
        var allowed = new List<string>();

        foreach (Route route in _routes)
        {
            if (!Match(route.Path, path, out string value))
                continue;
            if (route.Method == method)
            {
                context.RouteValue = value;
                await route.Handle(context);
                return;
            }

            allowed.Add(route.Method);
            if (route.Method == "GET")
                allowed.Add("HEAD");
        }

        if (allowed.Count > 0)
        {
            context.Response.Headers["Allow"] = string.Join(", ", allowed);
            throw new RequestException(405, "Метод запроса не поддерживается.");
        }
        throw new RequestException(404, "Страница не найдена.");
    }

    private static string Normalize(string path) => path.Length > 1 ? path.TrimEnd('/') : path;

    private static bool Match(string pattern, string path, out string value)
    {
        const string parameter = "{id}";
        value = "";
        if (!pattern.EndsWith(parameter, StringComparison.Ordinal))
            return path == pattern;

        string prefix = pattern[..^parameter.Length];
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        value = path[prefix.Length..];
        return value.Length > 0 && !value.Contains('/');
    }

    private sealed record Route(string Method, string Path, Func<WebContext, Task> Handle);
}
