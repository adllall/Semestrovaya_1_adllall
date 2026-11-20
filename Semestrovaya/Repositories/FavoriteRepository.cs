using Npgsql;
using Semestrovaya.Models;
using Semestrovaya.ORM;

namespace Semestrovaya.Repositories;

public sealed class FavoriteRepository
{
    public async Task<List<Favorite>> GetForUserAsync(int userId)
    {
        await using var connection = await DatabaseConnection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT * FROM favorites WHERE user_id = @user_id ORDER BY updated_at DESC, id", connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync();
        var favorites = new List<Favorite>();
        while (await reader.ReadAsync())
            favorites.Add(EntityMapper.Map<Favorite>(reader));
        return favorites;
    }

    public async Task SaveAsync(int userId, int cardId, string note)
    {
        await using var connection = await DatabaseConnection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO favorites (user_id, card_id, note) VALUES (@user_id, @card_id, @note)
            ON CONFLICT (user_id, card_id) DO UPDATE
            SET note = EXCLUDED.note, updated_at = CURRENT_TIMESTAMP AT TIME ZONE 'UTC'
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("card_id", cardId);
        command.Parameters.AddWithValue("note", note);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int userId, int cardId)
    {
        await using var connection = await DatabaseConnection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM favorites WHERE user_id = @user_id AND card_id = @card_id", connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("card_id", cardId);
        await command.ExecuteNonQueryAsync();
    }
}
