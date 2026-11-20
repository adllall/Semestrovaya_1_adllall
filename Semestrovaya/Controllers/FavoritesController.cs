using Npgsql;
using Semestrovaya.Repositories;
using Semestrovaya.Security;
using Semestrovaya.Server;

namespace Semestrovaya.Controllers;

public sealed class FavoritesController(FavoriteRepository favorites, CardRepository cards, SessionStore sessions)
{
    public void Map(Router router)
    {
        router.Map("GET", "/favorites", ListAsync);
        router.Map("POST", "/favorites/{id}", SaveAsync);
        router.Map("POST", "/favorites/remove/{id}", DeleteAsync);
    }

    private async Task ListAsync(WebContext context)
    {
        if (sessions.Find(context) is not { } session)
        {
            context.Redirect("/login");
            return;
        }

        var items = await favorites.GetForUserAsync(session.UserId);
        var catalog = (await cards.GetAllAsync()).ToDictionary(card => card.Id);
        var entries = items.Where(item => catalog.ContainsKey(item.CardId)).Select(item => new
        {
            item.CardId,
            item.Note,
            catalog[item.CardId].Title,
            session.CsrfToken
        });
        await context.ViewAsync("favorites", new { Favorites = entries, session.Username, session.CsrfToken });
    }

    private async Task SaveAsync(WebContext context)
    {
        var session = sessions.Require(context);
        SessionStore.CheckOrigin(context);
        var form = await context.ReadFormAsync();
        SessionStore.CheckToken(session, form.GetValueOrDefault("csrf"));

        if (context.ItemId == 0)
            throw new RequestException(404, "Карточка не найдена.");

        string note = form.GetValueOrDefault("note", "").Trim();
        InputText.Check(note, "note", multiline: true);
        if (note.Length > 500)
            throw new RequestException(400, "Заметка должна содержать не более 500 символов.");

        try
        {
            await favorites.SaveAsync(session.UserId, context.ItemId, note);
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new RequestException(404, "Карточка не найдена.");
        }
        context.Redirect($"/cards/{context.ItemId}?saved=1");
    }

    private async Task DeleteAsync(WebContext context)
    {
        var session = sessions.Require(context);
        SessionStore.CheckOrigin(context);
        var form = await context.ReadFormAsync();
        SessionStore.CheckToken(session, form.GetValueOrDefault("csrf"));

        if (context.ItemId == 0)
            throw new RequestException(404, "Карточка не найдена.");
        await favorites.DeleteAsync(session.UserId, context.ItemId);
        context.Redirect("/favorites");
    }
}
