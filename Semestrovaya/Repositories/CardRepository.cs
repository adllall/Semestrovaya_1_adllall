using Semestrovaya.Models;
using Semestrovaya.ORM;

namespace Semestrovaya.Repositories;

public sealed class CardRepository() : Repository<Card>("cards")
{
    public async Task<IReadOnlyList<Card>> GetCatalogAsync()
    {
        return (await GetAllAsync()).OrderBy(card => card.Title, StringComparer.Create(
            System.Globalization.CultureInfo.GetCultureInfo("ru"), ignoreCase: true)).ToArray();
    }
}

public class Repository<T>(string table) where T : new()
{
    private readonly DbContext _db = new();

    public Task<List<T>> GetAllAsync() => _db.GetAllAsync<T>(table);
    public Task<T?> GetByIdAsync(int id) => _db.GetByIdAsync<T>(table, id);
    public Task<int> CreateAsync(T entity) => _db.InsertAsync(table, entity);
    public Task<int> UpdateAsync(int id, T entity) => _db.UpdateAsync(table, id, entity);
    public Task<int> DeleteAsync(int id) => _db.DeleteAsync(table, id);
}
