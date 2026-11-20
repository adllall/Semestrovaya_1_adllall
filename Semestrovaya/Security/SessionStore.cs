using System.Security.Cryptography;
using Semestrovaya.Models;
using Semestrovaya.Server;

namespace Semestrovaya.Security;

public sealed class SessionStore
{
    private const string CookieName = "response_session";
    private const int LifetimeSeconds = 43_200;
    private const int MaxSessions = 8192;
    private readonly Dictionary<string, UserSession> _sessions = new();
    private DateTimeOffset _nextCleanup;

    public UserSession? Find(WebContext context)
    {
        string? token = context.Request.Cookies[CookieName]?.Value;
        if (token is not { Length: 64 })
            return null;

        lock (_sessions)
        {
            if (!_sessions.TryGetValue(token, out var session))
                return null;
            if (session.ExpiresAt > DateTimeOffset.UtcNow)
                return session;
            _sessions.Remove(token);
            return null;
        }
    }

    public UserSession Require(WebContext context, bool admin = false)
    {
        var session = Find(context) ?? throw new RequestException(401, "Войдите в аккаунт.");
        if (admin && !session.IsAdmin)
            throw new RequestException(403, "Требуются права администратора.");
        return session;
    }

    public static void CheckToken(UserSession session, string? token)
    {
        // Token bytes are random and represented by a fixed-length hexadecimal string.
        if (token?.Length == session.CsrfToken.Length &&
            CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(token), System.Text.Encoding.UTF8.GetBytes(session.CsrfToken)))
            return;
        throw new RequestException(403, "Откройте страницу заново и повторите действие.");
    }

    public static void CheckOrigin(WebContext context)
    {
        string? origin = context.Request.Headers["Origin"];
        if (context.Request.Headers["Sec-Fetch-Site"] == "cross-site" ||
            origin != null && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.GetLeftPart(UriPartial.Authority),
                    context.Request.Url?.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)))
        {
            throw new RequestException(403, "Источник запроса не разрешён.");
        }
    }

    public void SignIn(WebContext context, User user)
    {
        string token = NewToken();
        var now = DateTimeOffset.UtcNow;
        lock (_sessions)
        {
            RevokeCookieSession(context);
            if (now >= _nextCleanup || _sessions.Count >= MaxSessions)
            {
                foreach (string key in _sessions.Where(entry => entry.Value.ExpiresAt <= now).Select(entry => entry.Key).ToArray())
                    _sessions.Remove(key);
                _nextCleanup = now.AddMinutes(1);
            }
            if (_sessions.Count >= MaxSessions)
                _sessions.Remove(_sessions.MinBy(entry => entry.Value.ExpiresAt).Key);
            _sessions[token] = new UserSession(user.Id, user.Username, user.Role == "admin",
                NewToken(), now.AddSeconds(LifetimeSeconds));
        }
        SetCookie(context, token, LifetimeSeconds);
    }

    public void SignOut(WebContext context)
    {
        lock (_sessions)
            RevokeCookieSession(context);
        SetCookie(context, "", 0);
    }

    private void RevokeCookieSession(WebContext context)
    {
        if (context.Request.Cookies[CookieName]?.Value is { } token)
            _sessions.Remove(token);
    }

    private static void SetCookie(WebContext context, string token, int age)
    {
        string secure = context.Request.IsSecureConnection || Environment.GetEnvironmentVariable("APP_SECURE_COOKIES") == "true"
            ? "; Secure" : "";
        context.Response.AppendHeader("Set-Cookie",
            $"{CookieName}={token}; Path=/; Max-Age={age}; HttpOnly; SameSite=Lax{secure}");
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}

public sealed record UserSession(int UserId, string Username, bool IsAdmin, string CsrfToken, DateTimeOffset ExpiresAt);
