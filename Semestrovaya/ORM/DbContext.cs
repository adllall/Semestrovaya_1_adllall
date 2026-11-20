
using Npgsql;
using Semestrovaya.Repositories;

namespace Semestrovaya.ORM;

public class DbContext(NpgsqlDataSource? dataSource = null)
{
    private async Task<NpgsqlConnection> OpenConnectionAsync() => dataSource == null
        ? await DatabaseConnection.OpenAsync()
        : await dataSource.OpenConnectionAsync();

    public async Task<List<T>> GetAllAsync<T>(string table)
        where T : new()
    {
        var result = new List<T>();

        await using var connection =
            await OpenConnectionAsync();

        string sql = SqlGenerator.SelectAll(table);

        await using var command = new NpgsqlCommand(
            sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(EntityMapper.Map<T>(reader));
        }

        return result;
    }

    public async Task<T?> GetByIdAsync<T>(
        string table,
        int id)
        where T : new()
    {
        await using var connection =
            await OpenConnectionAsync();

        string sql = SqlGenerator.SelectById(table);

        await using var command = new NpgsqlCommand(
            sql, connection);

        command.Parameters.AddWithValue("id", id);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return EntityMapper.Map<T>(reader);
        }

        return default;
    }

    public async Task<int> InsertAsync<T>(
        string table,
        T entity)
    {
        var values = EntityMapper.ToDictionary(entity);

        string sql = SqlGenerator.Insert(
            table, values.Keys);

        await using var connection =
            await OpenConnectionAsync();

        await using var command = new NpgsqlCommand(
            sql, connection);

        foreach (var item in values)
        {
            command.Parameters.AddWithValue(
                item.Key, item.Value);
        }

        object? result = await command.ExecuteScalarAsync();

        return Convert.ToInt32(result);
    }

    public async Task<int> UpdateAsync<T>(
        string table,
        int id,
        T entity)
    {
        var values = EntityMapper.ToDictionary(entity);

        string sql = SqlGenerator.Update(
            table, values.Keys);

        await using var connection =
            await OpenConnectionAsync();

        await using var command = new NpgsqlCommand(
            sql, connection);

        foreach (var item in values)
        {
            command.Parameters.AddWithValue(
                item.Key, item.Value);
        }

        command.Parameters.AddWithValue("id", id);

        return await command.ExecuteNonQueryAsync();
    }

    public async Task<int> DeleteAsync(
        string table,
        int id)
    {
        await using var connection =
            await OpenConnectionAsync();

        string sql = SqlGenerator.Delete(table);

        await using var command = new NpgsqlCommand(
            sql, connection);

        command.Parameters.AddWithValue("id", id);

        return await command.ExecuteNonQueryAsync();
    }
}
