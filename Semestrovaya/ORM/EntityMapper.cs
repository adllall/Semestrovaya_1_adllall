using System.Reflection;
using Npgsql;

namespace Semestrovaya.ORM;

public static class EntityMapper
{
    public static T Map<T>(NpgsqlDataReader reader)
        where T : new()
    {
        T entity = new();

        foreach (PropertyInfo property in typeof(T).GetProperties())
        {
            if (!property.CanWrite)
                continue;

            string columnName = GetColumnName(property);

            int ordinal;

            try
            {
                ordinal = reader.GetOrdinal(columnName);
            }
            catch (IndexOutOfRangeException)
            {
                continue;
            }

            if (reader.IsDBNull(ordinal))
                continue;

            object value = reader.GetValue(ordinal);

            Type targetType = Nullable.GetUnderlyingType(
                property.PropertyType) ?? property.PropertyType;

            object converted = Convert.ChangeType(
                value, targetType);

            property.SetValue(entity, converted);
        }

        return entity;
    }

    public static Dictionary<string, object> ToDictionary<T>(
        T entity,
        bool includeId = false)
    {
        var values = new Dictionary<string, object>();

        foreach (PropertyInfo property in typeof(T).GetProperties())
        {
            if (!property.CanRead || property.SetMethod == null)
                continue;

            string column = GetColumnName(property);

            if (!includeId && column == "id")
                continue;

            values[column] = property.GetValue(entity!) ?? DBNull.Value;
        }

        return values;
    }

    private static string GetColumnName(PropertyInfo property)
    {
        string name = property.Name;

        return System.Text.RegularExpressions.Regex.Replace(
            name,
            "([a-z0-9])([A-Z])",
            "$1_$2").ToLowerInvariant();
    }
}