namespace Semestrovaya.Models;

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int SortOrder { get; set; }
}

public sealed class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string Role { get; set; } = "user";
}

public sealed class Favorite
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int CardId { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
