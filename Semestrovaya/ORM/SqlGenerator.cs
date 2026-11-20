using System.Text.RegularExpressions;

namespace Semestrovaya.ORM;

public static class SqlGenerator
{
    private static string ValidateName(string name)
    {
        if (!Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
        {
            throw new ArgumentException(
                "Недопустимое имя таблицы или столбца.");
        }

        return $"\"{name}\"";
    }

    public static string SelectAll(string table)
    {
        return $"SELECT * FROM {ValidateName(table)}";
    }

    public static string SelectById(string table)
    {
        return $"SELECT * FROM {ValidateName(table)} WHERE id = @id";
    }

    public static string Insert(
        string table,
        IEnumerable<string> columns)
    {
        var names = columns.ToList();

        if (names.Count == 0)
            throw new ArgumentException("Нет столбцов для добавления.");

        string fields = string.Join(", ",
            names.Select(ValidateName));

        string parameters = string.Join(", ",
            names.Select(name => "@" + name));

        return $"""
                INSERT INTO {ValidateName(table)} ({fields})
                VALUES ({parameters})
                RETURNING id
                """;
    }

    public static string Update(
        string table,
        IEnumerable<string> columns)
    {
        var names = columns.Where(c => c != "id").ToList();

        if (names.Count == 0)
            throw new ArgumentException("Нет столбцов для обновления.");

        string assignments = string.Join(", ",
            names.Select(name =>
                $"{ValidateName(name)} = @{name}"));

        return $"""
                UPDATE {ValidateName(table)}
                SET {assignments}
                WHERE id = @id
                """;
    }

    public static string Delete(string table)
    {
        return $"DELETE FROM {ValidateName(table)} WHERE id = @id";
    }
}
