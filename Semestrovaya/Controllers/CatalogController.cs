using Semestrovaya.Models;
using Semestrovaya.Repositories;
using Semestrovaya.Security;
using Semestrovaya.Server;
using Semestrovaya.Templating;

namespace Semestrovaya.Controllers;

public sealed class CatalogController(CardRepository cards, Repository<Category> categories,
    FavoriteRepository favorites, SessionStore sessions)
{
    public void Map(Router router)
    {
        router.Map("GET", Router.Home, GalleryAsync);
        router.Map("GET", Router.Gallery, GalleryAsync);
        router.Map("GET", Router.Card, CardAsync);
        router.Map("GET", "/api/cards", ListAsync);
    }

    private async Task GalleryAsync(WebContext context)
    {
        var catalog = await cards.GetCatalogAsync();
        var session = sessions.Find(context);
        var types = catalog.Select(card => card.CardType).Distinct().OrderBy(type => type)
            .Select(type => new { Name = type });
        await context.ViewAsync("spoiler", new
        {
            Cards = catalog,
            Types = types,
            CardCount = catalog.Count,
            LoggedIn = session != null,
            IsAdmin = session?.IsAdmin == true,
            Username = session?.Username,
            CsrfToken = session?.CsrfToken
        });
    }

    private async Task CardAsync(WebContext context)
    {
        var card = context.ItemId > 0 ? await cards.GetByIdAsync(context.ItemId) : null;
        if (card == null)
            throw new RequestException(404, "Карточка не найдена.");

        var category = await categories.GetByIdAsync(card.CategoryId);
        var session = sessions.Find(context);
        var favorite = session == null ? null :
            (await favorites.GetForUserAsync(session.UserId)).Find(item => item.CardId == card.Id);
        await context.ViewAsync("card", new
        {
            card.Id,
            card.Title,
            card.ImageUrl,
            card.CardType,
            Category = category?.Name,
            Description = DescriptionFormatter.Format(card.Description),
            LoggedIn = session != null,
            CsrfToken = session?.CsrfToken,
            Saved = favorite != null,
            Note = favorite?.Note,
            Success = context.Request.QueryString["saved"] == "1"
        });
    }

    private async Task ListAsync(WebContext context)
    {
        var catalog = await cards.GetCatalogAsync();
        await context.JsonAsync(catalog.Select(card => new
        {
            card.Id,
            card.CategoryId,
            card.Title,
            card.Description,
            image = card.ImageUrl,
            type = card.CardType
        }));
    }
}
