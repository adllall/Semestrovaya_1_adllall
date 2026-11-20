using System.Net.Mail;
using System.Text.RegularExpressions;
using Npgsql;
using Semestrovaya.Models;
using Semestrovaya.Repositories;
using Semestrovaya.Security;
using Semestrovaya.Server;

namespace Semestrovaya.Controllers;

public sealed class AccountController(UserRepository users, SessionStore sessions)
{
    private static readonly Regex UsernamePattern = new(@"\A[a-zA-Z0-9_-]{3,50}\z", RegexOptions.CultureInvariant);
    private readonly AuthRequestLimiter _attempts = new();

    public void Map(Router router)
    {
        router.Map("GET", "/login", context => AccountView(context, register: false));
        router.Map("POST", "/login", LoginAsync);
        router.Map("GET", "/register", context => AccountView(context, register: true));
        router.Map("POST", "/register", RegisterAsync);
        router.Map("POST", "/logout", LogoutAsync);
    }

    public async Task EnsureAdminAsync()
    {
        string? password = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
        if (string.IsNullOrEmpty(password))
            return;

        string username = (Environment.GetEnvironmentVariable("ADMIN_USERNAME") ?? "admin").Trim().ToLowerInvariant();
        string email = (Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? "admin@example.test").Trim().ToLowerInvariant();
        if (RegistrationError(username, email, password) is { } error)
            throw new InvalidOperationException($"Начальный администратор: {error}");

        var existing = await users.FindByUsernameAsync(username);
        if (existing?.Role == "admin")
            return;
        if (existing != null)
            throw new InvalidOperationException("Имя начального администратора уже занято пользователем.");

        try
        {
            await users.CreateAsync(NewUser(username, email, password, "admin"));
        }
        catch (PostgresException problem) when (problem.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            if ((await users.FindByUsernameAsync(username))?.Role != "admin")
                throw new InvalidOperationException("Имя или почта начального администратора уже заняты.");
        }
    }

    private async Task LoginAsync(WebContext context)
    {
        SessionStore.CheckOrigin(context);
        var form = await context.ReadFormAsync();
        string username = form.GetValueOrDefault("username", "").Trim().ToLowerInvariant();
        string password = form.GetValueOrDefault("password", "");
        using var attempt = _attempts.Acquire(context, registration: false);
        var user = UsernamePattern.IsMatch(username) ? await users.FindByUsernameAsync(username) : null;
        if (password.Length > 128 || InputText.HasInvalidCharacters(password) ||
            !Passwords.Verify(password, user?.PasswordHash) || user == null)
        {
            await AccountView(context, register: false, "Неверное имя пользователя или пароль.", username, status: 401);
            return;
        }

        sessions.SignIn(context, user);
        context.Redirect(user.Role == "admin" ? Router.Admin : Router.Home);
    }

    private async Task RegisterAsync(WebContext context)
    {
        SessionStore.CheckOrigin(context);
        var form = await context.ReadFormAsync();
        string username = form.GetValueOrDefault("username", "").Trim().ToLowerInvariant();
        string email = form.GetValueOrDefault("email", "").Trim().ToLowerInvariant();
        string password = form.GetValueOrDefault("password", "");
        if (RegistrationError(username, email, password) is { } error)
        {
            await AccountView(context, register: true, error, username, email, status: 400);
            return;
        }

        using var attempt = _attempts.Acquire(context, registration: true);
        var user = NewUser(username, email, password, "user");
        try
        {
            user.Id = await users.CreateAsync(user);
        }
        catch (PostgresException problem) when (problem.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await AccountView(context, register: true, "Имя или почта уже используются.", username, email, status: 409);
            return;
        }

        sessions.SignIn(context, user);
        context.Redirect(Router.Home);
    }

    private async Task LogoutAsync(WebContext context)
    {
        var session = sessions.Require(context);
        SessionStore.CheckOrigin(context);
        var form = await context.ReadFormAsync();
        SessionStore.CheckToken(session, form.GetValueOrDefault("csrf"));
        sessions.SignOut(context);
        context.Redirect(Router.Home);
    }

    private static Task AccountView(WebContext context, bool register, string error = "",
        string username = "", string email = "", int status = 200) =>
        context.ViewAsync("account", new
        {
            Register = register,
            Heading = register ? "Создать аккаунт" : "Войти",
            Action = register ? "/register" : "/login",
            Error = error,
            Username = username.Length <= 50 ? username : "",
            Email = email.Length <= 255 ? email : ""
        }, status);

    private static string? RegistrationError(string username, string email, string password)
    {
        if (!UsernamePattern.IsMatch(username))
            return "Имя пользователя должно содержать 3–50 латинских букв, цифр, дефисов или подчёркиваний.";
        if (email.Length > 255 || InputText.HasInvalidCharacters(email) ||
            !MailAddress.TryCreate(email, out var address) || address.Address != email)
            return "Укажите корректный адрес электронной почты.";
        if (password.Length is < 10 or > 128)
            return "Пароль должен содержать от 10 до 128 символов.";
        if (InputText.HasInvalidCharacters(password))
            return "В пароле есть недопустимые управляющие символы.";
        return null;
    }

    private static User NewUser(string username, string email, string password, string role) => new()
    {
        Username = username,
        Email = email,
        PasswordHash = Passwords.Hash(password),
        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        Role = role
    };
}
