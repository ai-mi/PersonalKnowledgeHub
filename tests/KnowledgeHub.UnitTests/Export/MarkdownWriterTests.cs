using KnowledgeHub.Api.Modules.Export;
using KnowledgeHub.Api.Modules.Items;
using YamlDotNet.Serialization;

namespace KnowledgeHub.UnitTests.Export;

public sealed class MarkdownWriterTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 27, 9, 30, 0, TimeSpan.Zero);

    private static Item NewItem(ItemType type = ItemType.Note, string? title = "Title", string? body = null) => new()
    {
        Id = Guid.Parse("0199aaaa-bbbb-7ccc-8ddd-eeeeffff0000"),
        Type = type,
        Title = title,
        Body = body,
        CreatedAt = Created,
        UpdatedAt = Created,
    };

    /// <summary>Разбирает front matter тем же YAML-парсером, которым его прочтёт пользователь.</summary>
    private static (Dictionary<string, object> Meta, string Body) Parse(string markdown)
    {
        markdown.ShouldStartWith("---\n");
        var end = markdown.IndexOf("\n---\n", StringComparison.Ordinal);
        end.ShouldBeGreaterThan(0);
        var meta = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object>>(markdown[4..(end + 1)]);
        return (meta, markdown[(end + 5)..]);
    }

    [Theory]
    [InlineData("Привет, мир!", "privet-mir")]
    [InlineData("Щука и ёж: съезд", "schuka-i-ezh-sezd")]
    [InlineData("Azure Service Bus", "azure-service-bus")]
    [InlineData("  --C# & .NET--  ", "c-net")]
    [InlineData("日本語", "item")]
    [InlineData("", "item")]
    [InlineData(null, "item")]
    public void Slug_TransliteratesCyrillicAndKeepsLatin(string? text, string expected) =>
        MarkdownWriter.Slug(text).ShouldBe(expected);

    [Fact]
    public void Slug_IsAtMost60CharactersWithoutTrailingDash()
    {
        var slug = MarkdownWriter.Slug(new string('a', 59) + " bbbbbb");

        slug.Length.ShouldBeLessThanOrEqualTo(60);
        slug.ShouldNotEndWith("-");
    }

    [Fact]
    public void FileName_UsesSlugAndFirst8CharactersOfId() =>
        MarkdownWriter.FileName(NewItem(title: "Заметка про Azure")).ShouldBe("items/zametka-pro-azure-0199aaaa.md");

    [Fact]
    public void FileName_FallsBackToUrlWhenThereIsNoTitle()
    {
        var item = NewItem(ItemType.Link, title: null);
        item.Url = "https://example.com/post";

        MarkdownWriter.FileName(item).ShouldBe("items/https-example-com-post-0199aaaa.md");
    }

    [Theory]
    [InlineData("Azure: Service Bus")]
    [InlineData("He said \"hi\" and 'bye'")]
    [InlineData("- starts with a dash")]
    [InlineData("# not a comment")]
    [InlineData("multi\nline")]
    [InlineData("true")]
    [InlineData("123")]
    [InlineData("a: b: c # d")]
    public void Render_EscapesTheTitleSoThatYamlRoundTrips(string title)
    {
        var (meta, _) = Parse(MarkdownWriter.Render(NewItem(title: title), null));

        meta["title"].ShouldBe(title);
    }

    [Fact]
    public void Render_WritesAllMetadataFields()
    {
        var item = NewItem(ItemType.Snippet, "Snip", "var x = 1;");
        item.CodeLanguage = "csharp";
        item.ReadAt = Created.AddDays(1);
        item.Tags.Add(new Tag { Name = "b" });
        item.Tags.Add(new Tag { Name = "a" });
        item.Collections.Add(new Collection { Name = "Work" });

        var (meta, _) = Parse(MarkdownWriter.Render(item, "files/x/doc.pdf"));

        meta["id"].ShouldBe("0199aaaa-bbbb-7ccc-8ddd-eeeeffff0000");
        meta["type"].ShouldBe("snippet");
        meta["tags"].ShouldBeAssignableTo<IEnumerable<object>>().Cast<string>().ShouldBe(["a", "b"]);
        meta["collections"].ShouldBeAssignableTo<IEnumerable<object>>().Cast<string>().ShouldBe(["Work"]);
        DateTimeOffset.Parse((string)meta["created"]).ShouldBe(Created);
        DateTimeOffset.Parse((string)meta["read"]).ShouldBe(Created.AddDays(1));
        meta["code_language"].ShouldBe("csharp");
        meta["file"].ShouldBe("files/x/doc.pdf");
    }

    [Fact]
    public void Render_OmitsEmptyOptionalFields()
    {
        var (meta, _) = Parse(MarkdownWriter.Render(NewItem(), null));

        meta.Keys.ShouldNotContain("url");
        meta.Keys.ShouldNotContain("read");
        meta.Keys.ShouldNotContain("file");
        meta.Keys.ShouldNotContain("code_language");
    }

    [Fact]
    public void Render_PutsUserNoteBeforeTheText()
    {
        var item = NewItem(body: "the body");
        item.UserNote = "my note";

        var (_, rest) = Parse(MarkdownWriter.Render(item, null));

        rest.ShouldBe("\nmy note\n\nthe body\n");
    }

    [Fact]
    public void Render_WrapsSnippetCodeInAFencedBlockWithTheLanguage()
    {
        var item = NewItem(ItemType.Snippet, body: "Console.WriteLine(1);");
        item.CodeLanguage = "csharp";

        var (_, rest) = Parse(MarkdownWriter.Render(item, null));

        rest.ShouldBe("\n```csharp\nConsole.WriteLine(1);\n```\n");
    }

    [Fact]
    public void Render_UsesALongerFenceWhenTheCodeContainsBackticks()
    {
        var item = NewItem(ItemType.Snippet, body: "```\ninner\n```");

        var (_, rest) = Parse(MarkdownWriter.Render(item, null));

        rest.ShouldBe("\n````\n```\ninner\n```\n````\n");
    }

    [Fact]
    public void Render_DoesNotFenceTheTextOfNotes()
    {
        var (_, rest) = Parse(MarkdownWriter.Render(NewItem(body: "plain"), null));

        rest.ShouldBe("\nplain\n");
    }
}
