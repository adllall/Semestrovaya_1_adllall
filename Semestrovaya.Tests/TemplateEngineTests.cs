using Semestrovaya.Templating;
using Xunit;

namespace Semestrovaya.Tests;

public class TemplateEngineTests
{
    [Fact]
    public void Render_HandlesNestedBlocksWithoutReinterpretingModelText()
    {
        var engine = new TemplateEngine();
        var model = new
        {
            Show = true,
            Cards = new[] { new { Show = true, Title = "{{Show}}" }, new { Show = false, Title = "hidden" } }
        };

        const string template = "{{#if Show}}{{#each Cards}}{{#if Show}}{{Title}}{{/if}}{{/each}}{{/if}}";
        Assert.Equal("{{Show}}", engine.Render(template, model));
    }

    [Fact]
    public void Render_EncodesFormattedDescriptionText()
    {
        var engine = new TemplateEngine();
        var model = new { Description = DescriptionFormatter.Format("[b]<img>[/b] и [i]текст[/i]") };

        Assert.Equal("<strong>&lt;img&gt;</strong> и <em>текст</em>", engine.Render("{{{Description}}}", model));
        Assert.Equal("&lt;img&gt;", engine.Render("{{{Text}}}", new { Text = "<img>" }));
    }

    private readonly TemplateEngine _engine = new();

    [Fact]
    public void Render_ReplacesVariable()
    {
        string template = "Привет, {{Name}}!";

        string result = _engine.Render(
            template,
            new { Name = "Имя" });

        Assert.Equal("Привет, Имя!", result);
    }

    [Fact]
    public void Render_HandlesTrueCondition()
    {
        string template =
            "{{#if HasCards}}Есть карточки{{else}}Пусто{{/if}}";

        string result = _engine.Render(
            template,
            new { HasCards = true });

        Assert.Equal("Есть карточки", result);
    }

    [Fact]
    public void Render_HandlesFalseCondition()
    {
        string template =
            "{{#if HasCards}}Есть карточки{{else}}Пусто{{/if}}";

        string result = _engine.Render(
            template,
            new { HasCards = false });

        Assert.Equal("Пусто", result);
    }

    [Fact]
    public void Render_HandlesLoop()
    {
        string template =
            "{{#each Cards}}{{Title}};{{/each}}";

        var model = new
        {
            Cards = new[]
            {
                new { Title = "Карточка 1" },
                new { Title = "Карточка 2" }
            }
        };

        string result = _engine.Render(template, model);

        Assert.Equal("Карточка 1;Карточка 2;", result);
    }

    [Fact]
    public void Render_EscapesHtml()
    {
        string result = _engine.Render(
            "{{Title}}",
            new { Title = "<script>" });

        Assert.Equal("&lt;script&gt;", result);
    }
}
