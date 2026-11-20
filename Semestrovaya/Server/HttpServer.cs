using System.Net;
using Npgsql;

namespace Semestrovaya.Server;

public sealed class HttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Router _router;
    private readonly SemaphoreSlim _requestSlots;
    private readonly TimeSpan _bodyTimeout;

    public HttpServer(Router router, string prefix, int maxConcurrentRequests = 48, TimeSpan? bodyTimeout = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrentRequests);
        _router = router;
        _requestSlots = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        _bodyTimeout = bodyTimeout ?? TimeSpan.FromSeconds(8);
        _listener.Prefixes.Add(prefix);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _listener.Start();
        using var stop = cancellationToken.Register(_listener.Stop);
        Console.WriteLine("Сервер запущен.");
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (_requestSlots.Wait(0))
                _ = Task.Run(() => HandleAsync(context));
            else
                RejectOverload(context.Response);
        }
    }

    private async Task HandleAsync(HttpListenerContext listenerContext)
    {
        var context = new WebContext(listenerContext) { BodyTimeout = _bodyTimeout };
        try
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "same-origin";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; style-src 'self' 'unsafe-inline'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

            string path = context.Request.Url?.AbsolutePath ?? "/";
            if (path.StartsWith("/css/", StringComparison.Ordinal) ||
                path.StartsWith("/js/", StringComparison.Ordinal) ||
                path.StartsWith("/images/", StringComparison.Ordinal))
            {
                await ServeFileAsync(context, path);
            }
            else
            {
                context.Response.Headers["Cache-Control"] = "no-store";
                await _router.DispatchAsync(context);
            }
        }
        catch (RequestException error)
        {
            if (error.Status is 408 or 413)
                context.Response.KeepAlive = false;
            await TrySendErrorAsync(context, error);
        }
        catch (NpgsqlException error) when (error.IsTransient)
        {
            Console.Error.WriteLine($"PostgreSQL: {error.SqlState ?? error.GetType().Name}");
            context.Response.Headers["Retry-After"] = "3";
            await TrySendErrorAsync(context,
                new RequestException(503, "База данных временно недоступна. Попробуйте через несколько секунд."));
        }
        catch (Exception error) when (error is HttpListenerException or ObjectDisposedException)
        {
            // The client or the listener closed the connection.
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            await TrySendErrorAsync(context, new RequestException(500, "Ошибка сервера. Попробуйте ещё раз."));
        }
        finally
        {
            CloseResponse(context.Response);
            _requestSlots.Release();
        }
    }

    private static void RejectOverload(HttpListenerResponse response)
    {
        try
        {
            response.StatusCode = 503;
            response.Headers["Retry-After"] = "2";
            response.KeepAlive = false;
            response.ContentLength64 = 0;
        }
        catch (Exception error) when (error is HttpListenerException or IOException or ObjectDisposedException)
        {
            // The client may disconnect before receiving the response.
        }
        finally
        {
            CloseResponse(response);
        }
    }

    private static void CloseResponse(HttpListenerResponse response)
    {
        try
        {
            response.Close();
        }
        catch (Exception error) when (error is HttpListenerException or IOException or ObjectDisposedException)
        {
            // Failed connections don't affect other requests.
        }
    }

    private static async Task TrySendErrorAsync(WebContext context, RequestException error)
    {
        try
        {
            await context.ErrorAsync(error.Status, error.Message, error.Field);
        }
        catch (Exception responseError) when (responseError is HttpListenerException or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine(responseError.Message);
        }
    }

    private static async Task ServeFileAsync(WebContext context, string path)
    {
        if (context.Request.HttpMethod is not ("GET" or "HEAD"))
        {
            context.Response.Headers["Allow"] = "GET, HEAD";
            throw new RequestException(405, "Метод запроса не поддерживается.");
        }

        int start = path.IndexOf('/', 1);
        string folder = path[1..start];
        string name = path[(start + 1)..];
        string? type = (folder, Path.GetExtension(name).ToLowerInvariant()) switch
        {
            ("css", ".css") => "text/css; charset=utf-8",
            ("js", ".js") => "application/javascript; charset=utf-8",
            ("images", ".jpg" or ".jpeg") => "image/jpeg",
            ("images", ".png") => "image/png",
            ("images", ".webp") => "image/webp",
            ("images", ".svg") => "image/svg+xml",
            _ => null
        };

        if (type == null || name.Length > 255 ||
            name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_')))
            throw new RequestException(404, "Файл не найден.");

        string file = Path.Combine(AppContext.BaseDirectory, "wwwroot", folder, name);
        try
        {
            await using var stream = File.OpenRead(file);
            context.Response.ContentType = type;
            context.Response.ContentLength64 = stream.Length;
            if (context.Request.HttpMethod != "HEAD")
                await stream.CopyToAsync(context.Response.OutputStream);
        }
        catch (FileNotFoundException)
        {
            throw new RequestException(404, "Файл не найден.");
        }
    }

    public void Dispose() => _listener.Close();
}
