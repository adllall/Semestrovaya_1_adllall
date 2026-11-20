using System.Net;
using Semestrovaya.Server;

namespace Semestrovaya.Security;

internal sealed class AuthRequestLimiter
{
    private const int WindowSeconds = 60;
    private const int MaxClients = 4096;
    private readonly SemaphoreSlim _slots = new(4, 4);
    private readonly Dictionary<(IPAddress Ip, bool Registration), Window> _windows = new();
    private long _nextCleanup;

    public IDisposable Acquire(WebContext context, bool registration)
    {
        IPAddress address = context.Request.RemoteEndPoint?.Address ?? IPAddress.None;
        long now = Environment.TickCount64;
        int max = registration ? 12 : 20;

        lock (_windows)
        {
            if (now >= _nextCleanup)
            {
                foreach (var key in _windows.Where(entry => entry.Value.Expires <= now).Select(entry => entry.Key).ToArray())
                    _windows.Remove(key);
                _nextCleanup = now + WindowSeconds * 1000;
            }

            var client = (address, registration);
            if (!_windows.TryGetValue(client, out var window) || window.Expires <= now)
            {
                if (window == null && _windows.Count >= MaxClients)
                    throw RetryLater(context, WindowSeconds);
                window = new Window(now + WindowSeconds * 1000);
                _windows[client] = window;
            }
            if (window.Attempts >= max)
                throw RetryLater(context, (int)((window.Expires - now + 999) / 1000));

            if (!_slots.Wait(0))
                throw RetryLater(context, 2);
            window.Attempts++;
        }

        return new Slot(_slots);
    }

    private static RequestException RetryLater(WebContext context, int seconds)
    {
        context.Response.Headers["Retry-After"] = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new RequestException(429, $"Слишком много запросов. Повторите попытку через {seconds} сек.");
    }

    private sealed class Window(long expires)
    {
        public long Expires { get; } = expires;
        public int Attempts { get; set; }
    }

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        public void Dispose() => slots.Release();
    }
}
