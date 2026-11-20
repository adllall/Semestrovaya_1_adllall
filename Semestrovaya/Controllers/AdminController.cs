using System.Text.RegularExpressions;
using Npgsql;
using Semestrovaya.Models;
using Semestrovaya.Repositories;
using Semestrovaya.Security;
using Semestrovaya.Server;
using Semestrovaya.Templating;

namespace Semestrovaya.Controllers;

public sealed partial class AdminController(CardRepository cards, Repository<Category> categories, SessionStore sessions)
{
    [GeneratedRegex(@"\A/images/[a-zA-Z0-9_-]+\.(?i:jpg|jpeg|png|webp)\z", RegexOptions.CultureInvariant)]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"\A[\p{L}\p{N} ,()/\-–—]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex TypePattern();

    public void Map(Router router)
    {
        router.Map("GET", Router.Admin, PageAsync);
        router.Map("POST", "/api/cards", context => SaveCardAsync(context, creating: true));
        router.Map("PUT", "/api/cards/{id}", context => SaveCardAsync(context, creating: false));
        router.Map("DELETE", "/api/cards/{id}", DeleteCardAsync);
        router.Map("GET", "/api/categories", ListCategoriesAsync);
        router.Map("POST", "/api/categories", context => SaveCategoryAsync(context, creating: true));
        router.Map("PUT", "/api/categories/{id}", context => SaveCategoryAsync(context, creating: false));
        router.Map("DELETE", "/api/categories/{id}", DeleteCategoryAsync);
    }

    private async Task PageAsync(WebContext context)
    {
        if (sessions.Find(context) == null)
        {
            context.Redirect("/login");
            return;
        }

        var session = sessions.Require(context, admin: true);
        await context.ViewAsync("admin", new { session.Username, session.CsrfToken });
    }

    private async Task ListCategoriesAsync(WebContext context)
    {
        sessions.Require(context, admin: true);
        var entries = await categories.GetAllAsync();
        await context.JsonAsync(entries.OrderBy(entry => entry.SortOrder).ThenBy(entry => entry.Name));
    }

    private async Task SaveCardAsync(WebContext context, bool creating)
    {
        RequireWrite(context);
        if (!creating && context.ItemId == 0)
            throw new RequestException(400, "Неверный ID карточки.");

        var card = await context.ReadJsonAsync<Card>();
        card.Title = card.Title?.Trim() ?? "";
        card.Description = card.Description?.Trim() ?? "";
        card.ImageUrl = card.ImageUrl?.Trim() ?? "";
        card.CardType = card.CardType?.Trim() ?? "";

        InputText.Check(card.Title, "title");
        InputText.Check(card.Description, "description", multiline: true);
        if (card.Title.Length is < 2 or > 200)
            throw new RequestException(400, "Название должно содержать от 2 до 200 символов.", "title");
        if (card.Description.Length is < 1 or > 2000 || !DescriptionFormatter.HasText(card.Description))
            throw new RequestException(400, "Описание должно содержать от 1 до 2000 символов.", "description");
        if (card.ImageUrl.Length > 255 || !ImagePattern().IsMatch(card.ImageUrl))
            throw new RequestException(400, "Укажите путь к изображению вида /images/card.jpg.", "imageUrl");
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot", "images", card.ImageUrl[8..])))
            throw new RequestException(400, "Изображение с таким именем не найдено.", "imageUrl");
        if (card.CardType.Length is < 1 or > 100 || !TypePattern().IsMatch(card.CardType))
            throw new RequestException(400, "Некорректный тип карточки.", "cardType");
        if (card.CategoryId <= 0)
            throw InvalidCategory();

        try
        {
            if (creating)
            {
                int id = await cards.CreateAsync(card);
                context.Response.Headers["Location"] = $"/cards/{id}";
                await context.JsonAsync(new { id }, 201);
                return;
            }

            if (await cards.UpdateAsync(context.ItemId, card) == 0)
                throw new RequestException(404, "Карточка не найдена.");
            await context.JsonAsync(new { success = true });
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw InvalidCategory();
        }
    }

    private async Task DeleteCardAsync(WebContext context)
    {
        RequireWrite(context);
        if (context.ItemId == 0)
            throw new RequestException(400, "Неверный ID карточки.");
        if (await cards.DeleteAsync(context.ItemId) == 0)
            throw new RequestException(404, "Карточка не найдена.");
        await context.JsonAsync(new { success = true });
    }

    private async Task SaveCategoryAsync(WebContext context, bool creating)
    {
        RequireWrite(context);
        if (!creating && context.ItemId == 0)
            throw InvalidCategory();

        var category = await context.ReadJsonAsync<Category>();
        category.Name = category.Name?.Trim() ?? "";
        category.Description = category.Description?.Trim() ?? "";

        InputText.Check(category.Name, "name");
        InputText.Check(category.Description, "description", multiline: true);
        if (category.Name.Length is < 2 or > 100)
            throw new RequestException(400, "Название категории должно содержать от 2 до 100 символов.", "name");
        if (category.Description.Length is < 1 or > 1000)
            throw new RequestException(400, "Описание категории должно содержать от 1 до 1000 символов.", "description");
        if (category.SortOrder is < 0 or > 9999)
            throw new RequestException(400, "Порядок должен быть целым числом от 0 до 9999.", "sortOrder");

        try
        {
            if (creating)
            {
                int id = await categories.CreateAsync(category);
                await context.JsonAsync(new { id }, 201);
                return;
            }

            if (await categories.UpdateAsync(context.ItemId, category) == 0)
                throw new RequestException(404, "Категория не найдена.");
            await context.JsonAsync(new { success = true });
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new RequestException(409, "Категория с таким названием уже существует.", "name");
        }
    }

    private async Task DeleteCategoryAsync(WebContext context)
    {
        RequireWrite(context);
        if (context.ItemId == 0)
            throw InvalidCategory();

        try
        {
            if (await categories.DeleteAsync(context.ItemId) == 0)
                throw new RequestException(404, "Категория не найдена.");
            await context.JsonAsync(new { success = true });
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new RequestException(409, "Сначала перенесите карточки из этой категории.");
        }
    }

    private void RequireWrite(WebContext context)
    {
        var session = sessions.Require(context, admin: true);
        SessionStore.CheckOrigin(context);
        SessionStore.CheckToken(session, context.Request.Headers["X-CSRF-Token"]);
    }

    private static RequestException InvalidCategory() => new(400, "Выберите существующую категорию.", "categoryId");
}
