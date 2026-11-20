using Semestrovaya.Controllers;
using Semestrovaya.Models;
using Semestrovaya.Repositories;
using Semestrovaya.Security;
using Semestrovaya.Server;

var cards = new CardRepository();
var categories = new Repository<Category>("categories");
var users = new UserRepository();
var favorites = new FavoriteRepository();
var sessions = new SessionStore();
var accounts = new AccountController(users, sessions);

var router = new Router();
accounts.Map(router);
new CatalogController(cards, categories, favorites, sessions).Map(router);
new AdminController(cards, categories, sessions).Map(router);
new FavoritesController(favorites, cards, sessions).Map(router);

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

try
{
    await accounts.EnsureAdminAsync();
    string prefix = Environment.GetEnvironmentVariable("HTTP_PREFIX") ?? "http://localhost:8080/";
    using var server = new HttpServer(router, prefix);
    await server.StartAsync(shutdown.Token);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Ошибка запуска: {error.Message}");
    return 1;
}
