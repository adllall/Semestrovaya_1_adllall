using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Semestrovaya.Templating;

namespace Semestrovaya.Server;

public sealed class WebContext(HttpListenerContext context)
{
    private const int MaxBodyBytes = 32_768;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict
    };
    private static readonly TemplateEngine Templates = new();

    public HttpListenerRequest Request => context.Request;
    public HttpListenerResponse Response => context.Response;
    internal TimeSpan BodyTimeout { get; init; } = TimeSpan.FromSeconds(8);
    public string RouteValue { get; internal set; } = "";
    public bool IsApi => Request.Url?.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal) == true;
    public int ItemId => int.TryParse(RouteValue, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0
        ? id : 0;

    public async Task<T> ReadJsonAsync<T>()
    {
        if (!HasMediaType("application/json"))
            throw new RequestException(415, "Ожидается запрос в формате JSON.");

        try
        {
            return JsonSerializer.Deserialize<T>(await ReadBodyAsync(), JsonOptions)
                ?? throw new RequestException(400, "Данные отсутствуют.");
        }
        catch (JsonException)
        {
            throw new RequestException(400, "Неверный формат данных.");
        }
    }

    public async Task<Dictionary<string, string>> ReadFormAsync()
    {
        if (!HasMediaType("application/x-www-form-urlencoded"))
            throw new RequestException(415, "Ожидаются данные формы.");

        var form = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string field in (await ReadBodyAsync()).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = field.IndexOf('=');
            if (separator <= 0 || form.Count == 32 ||
                !form.TryAdd(DecodeFormValue(field[..separator]), DecodeFormValue(field[(separator + 1)..])))
            {
                throw new RequestException(400, "Некорректные поля формы.");
            }
        }
        return form;
    }

    private static string DecodeFormValue(string value)
    {
        var bytes = new byte[Utf8.GetByteCount(value)];
        int written = 0;
        for (int start = 0; start < value.Length;)
        {
            if (value[start] == '%')
            {
                if (start + 2 >= value.Length ||
                    !byte.TryParse(value.AsSpan(start + 1, 2), NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture, out byte decoded))
                    throw new RequestException(400, "Некорректная кодировка формы.");
                bytes[written++] = decoded;
                start += 3;
                continue;
            }

            if (value[start] == '+')
            {
                bytes[written++] = (byte)' ';
                start++;
                continue;
            }

            int length = value.AsSpan(start).IndexOfAny('%', '+');
            if (length < 0)
                length = value.Length - start;
            written += Utf8.GetBytes(value.AsSpan(start, length), bytes.AsSpan(written));
            start += length;
        }

        return DecodeUtf8(bytes.AsSpan(0, written));
    }

    private bool HasMediaType(string type) => string.Equals(
        Request.ContentType?.Split(';', 2)[0].Trim(), type, StringComparison.OrdinalIgnoreCase);

    private async Task<string> ReadBodyAsync()
    {
        if (Request.ContentLength64 > MaxBodyBytes)
            throw new RequestException(413, "Запрос слишком большой.");

        using var timeout = new CancellationTokenSource(BodyTimeout);
        using var body = new MemoryStream();
        var buffer = new byte[4096];
        try
        {
            int count;
            while ((count = await Request.InputStream.ReadAsync(buffer, timeout.Token).AsTask().WaitAsync(timeout.Token)) != 0)
            {
                if (body.Length + count > MaxBodyBytes)
                    throw new RequestException(413, "Запрос слишком большой.");
                body.Write(buffer, 0, count);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new RequestException(408, "Запрос передавался слишком долго. Попробуйте ещё раз.");
        }

        return DecodeUtf8(body.GetBuffer().AsSpan(0, (int)body.Length));
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new RequestException(400, "Запрос должен использовать кодировку UTF-8.");
        }
    }

    public Task JsonAsync(object value, int status = 200) =>
        SendAsync(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), "application/json; charset=utf-8", status);

    public async Task ViewAsync(string view, object model, int status = 200)
    {
        string template = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Views", view + ".html"));
        string html = Templates.Render(template, model);
        await SendAsync(Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", status);
    }

    public async Task ErrorAsync(int status, string message, string? field = null)
    {
        if (IsApi)
        {
            await JsonAsync(new { message, field }, status);
            return;
        }

        try
        {
            await ViewAsync("error", new { ErrorCode = status, ErrorMessage = message }, status);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException)
        {
            string html = "<!doctype html><html lang=\"ru\"><meta charset=\"utf-8\"><title>Ошибка</title>" +
                $"<h1>{status}</h1><p>{WebUtility.HtmlEncode(message)}</p><a href=\"/\">Вернуться к галерее</a></html>";
            await SendAsync(Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", status);
        }
    }

    public void Redirect(string location)
    {
        Response.StatusCode = 303;
        Response.RedirectLocation = location;
        Response.ContentLength64 = 0;
    }

    public async Task SendAsync(byte[] data, string type, int status = 200)
    {
        Response.StatusCode = status;
        Response.ContentType = type;
        Response.ContentLength64 = data.Length;
        if (Request.HttpMethod != "HEAD")
            await Response.OutputStream.WriteAsync(data);
    }
}

public sealed class RequestException(int status, string message, string? field = null) : Exception(message)
{
    public int Status { get; } = status;
    public string? Field { get; } = field;
}
