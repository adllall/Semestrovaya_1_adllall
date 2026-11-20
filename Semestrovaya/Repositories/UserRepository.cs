using Npgsql;
using Semestrovaya.Models;
using Semestrovaya.ORM;

namespace Semestrovaya.Repositories;

public sealed class UserRepository() : Repository<User>("users")
{
    public async Task<User?> FindByUsernameAsync(string username)
    {
        await using var connection = await DatabaseConnection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT * FROM users WHERE lower(username) = @username ORDER BY id LIMIT 1", connection);
        command.Parameters.AddWithValue("username", username.ToLowerInvariant());
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? EntityMapper.Map<User>(reader) : null;
    }
}
