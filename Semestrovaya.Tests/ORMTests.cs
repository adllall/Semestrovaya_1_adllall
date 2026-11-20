
using Semestrovaya.ORM;
using Xunit;

namespace Semestrovaya.Tests;

public class OrmTests
{
    [Fact]
    public void SelectAll_GeneratesCorrectSql()
    {
        string sql = SqlGenerator.SelectAll("cards");

        Assert.Equal("SELECT * FROM \"cards\"", sql);
    }

    [Fact]
    public void SelectById_GeneratesCorrectSql()
    {
        string sql = SqlGenerator.SelectById("cards");

        Assert.Equal(
            "SELECT * FROM \"cards\" WHERE id = @id",
            sql);
    }

    [Fact]
    public void Insert_GeneratesCorrectSql()
    {
        string sql = SqlGenerator.Insert(
            "cards",
            new[] { "title", "description" });

        Assert.Contains(
            "INSERT INTO \"cards\"",
            sql);

        Assert.Contains(
            "\"title\", \"description\"",
            sql);

        Assert.Contains(
            "VALUES (@title, @description)",
            sql);

        Assert.Contains("RETURNING id", sql);
    }

    [Fact]
    public void Update_GeneratesCorrectSql()
    {
        string sql = SqlGenerator.Update(
            "cards",
            new[] { "id", "title", "description" });

        Assert.Contains(
            "UPDATE \"cards\"",
            sql);

        Assert.Contains(
            "\"title\" = @title",
            sql);

        Assert.Contains(
            "\"description\" = @description",
            sql);

        Assert.DoesNotContain(
            "\"id\" = @id",
            sql);

        Assert.Contains("WHERE id = @id", sql);
    }

    [Fact]
    public void Delete_GeneratesCorrectSql()
    {
        string sql = SqlGenerator.Delete("cards");

        Assert.Equal(
            "DELETE FROM \"cards\" WHERE id = @id",
            sql);
    }

    [Fact]
    public void SqlGenerator_RejectsInvalidTableName()
    {
        Assert.Throws<ArgumentException>(() =>
            SqlGenerator.SelectAll("cards; DROP TABLE cards"));
    }

    [Fact]
    public void EntityMapper_ConvertsObjectToDictionary()
    {
        var card = new TestCard
        {
            Id = 1,
            CategoryId = 2,
            Title = "Тестовая карточка"
        };

        var result = EntityMapper.ToDictionary(card);

        Assert.False(result.ContainsKey("id"));

        Assert.Equal(2, result["category_id"]);

        Assert.Equal(
            "Тестовая карточка",
            result["title"]);
    }

    private class TestCard
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string Title { get; set; } = "";
    }
}
