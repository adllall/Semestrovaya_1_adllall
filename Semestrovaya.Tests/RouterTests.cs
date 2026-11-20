using Semestrovaya.Server;
using Xunit;

namespace Semestrovaya.Tests;

public class RouterTests
{
    private readonly Router _router = new();

    [Fact]
    public void Root_OpensGallery()
    {
        string? result = _router.GetPagePath("/");

        Assert.Equal("spoiler.html", result);
    }

    [Fact]
    public void Admin_OpensAdminPage()
    {
        string? result = _router.GetPagePath("/admin");

        Assert.Equal("admin.html", result);
    }

    [Fact]
    public void Card_OpensCardPage()
    {
        string? result = _router.GetPagePath("/cards/1");

        Assert.Equal("card.html", result);
    }

    [Fact]
    public void InvalidCardId_ReturnsNull()
    {
        string? result = _router.GetPagePath("/cards/abc");

        Assert.Null(result);
    }

    [Fact]
    public void UnknownPage_ReturnsNull()
    {
        string? result = _router.GetPagePath("/unknown");

        Assert.Null(result);
    }

    [Fact]
    public void NegativeCardId_ReturnsNull()
    {
        string? result = _router.GetPagePath("/cards/-1");

        Assert.Null(result);
    }
}