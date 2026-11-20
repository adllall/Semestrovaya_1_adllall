using Npgsql;

namespace Semestrovaya.Repositories;

public static class DatabaseConnection
{
    public static async Task<NpgsqlConnection> OpenAsync()
    {
        string? password =
            Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Не задан пароль PostgreSQL (POSTGRES_PASSWORD)");
        }

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost",
            Port = int.Parse(Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432"),
            Database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "response_game",
            Username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres",
            Password = password,
            Timeout = 5,
            CommandTimeout = 8
        }.ConnectionString;

        var connection = new NpgsqlConnection(connectionString);

        try
        {
            await connection.OpenAsync();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
