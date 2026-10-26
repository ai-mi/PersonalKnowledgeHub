using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Items;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace KnowledgeHub.UnitTests.Bot;

public sealed class MessageParserTests
{
    private static MessageEntity Entity(MessageEntityType type, string text, string span, string? url = null) =>
        new() { Type = type, Offset = text.IndexOf(span, StringComparison.Ordinal), Length = span.Length, Url = url };

    private static Message Text(string text, params MessageEntity[] entities) =>
        new() { Text = text, Entities = entities };

    [Fact]
    public void LinkWithCaptionAndHashtags_BecomesLinkWithNoteAndTags()
    {
        const string text = "https://example.com/post Хорошая статья про шину #azure #dotnet";
        var message = Text(text,
            Entity(MessageEntityType.Url, text, "https://example.com/post"),
            Entity(MessageEntityType.Hashtag, text, "#azure"),
            Entity(MessageEntityType.Hashtag, text, "#dotnet"));

        var item = MessageParser.Parse(message)!.Item!;

        item.Type.ShouldBe(ItemType.Link);
        item.Url.ShouldBe("https://example.com/post");
        item.UserNote.ShouldBe("Хорошая статья про шину");
        item.Tags.ShouldBe(["azure", "dotnet"]);
    }

    [Fact]
    public void TextLink_UsesHiddenUrlAndKeepsAnchorTextInNote()
    {
        const string text = "Прочитать позже";
        var message = Text(text, Entity(MessageEntityType.TextLink, text, "Прочитать", "https://example.com/hidden"));

        var item = MessageParser.Parse(message)!.Item!;

        item.Type.ShouldBe(ItemType.Link);
        item.Url.ShouldBe("https://example.com/hidden");
        item.UserNote.ShouldBe("Прочитать позже");
    }

    [Fact]
    public void UrlWithoutScheme_GetsHttps()
    {
        const string text = "example.com/page";
        var message = Text(text, Entity(MessageEntityType.Url, text, text));

        var item = MessageParser.Parse(message)!.Item!;

        item.Url.ShouldBe("https://example.com/page");
        item.UserNote.ShouldBeNull();
    }

    [Fact]
    public void PlainText_BecomesNoteWithBodyAndTags()
    {
        const string text = "Идея для статьи #idea\nвторая строка";
        var message = Text(text, Entity(MessageEntityType.Hashtag, text, "#idea"));

        var item = MessageParser.Parse(message)!.Item!;

        item.Type.ShouldBe(ItemType.Note);
        item.Url.ShouldBeNull();
        item.Body.ShouldBe("Идея для статьи\nвторая строка");
        item.Tags.ShouldBe(["idea"]);
    }

    [Fact]
    public void SeveralLinks_FirstIsTheItemAndTheRestStayInTheNote()
    {
        const string text = "https://a.example.com/1 и ещё https://b.example.com/2";
        var message = Text(text,
            Entity(MessageEntityType.Url, text, "https://a.example.com/1"),
            Entity(MessageEntityType.Url, text, "https://b.example.com/2"));

        var item = MessageParser.Parse(message)!.Item!;

        item.Url.ShouldBe("https://a.example.com/1");
        item.UserNote.ShouldBe("и ещё https://b.example.com/2");
    }

    [Fact]
    public void Document_UsesCaptionAsNoteAndHashtagsAsTags()
    {
        const string caption = "Спецификация #spec";
        var message = new Message
        {
            Document = new Document { FileId = "file-1", FileName = "spec.pdf", FileSize = 1234 },
            Caption = caption,
            CaptionEntities = [Entity(MessageEntityType.Hashtag, caption, "#spec")],
        };

        var parsed = MessageParser.Parse(message)!;

        parsed.Item.ShouldBeNull();
        parsed.Document!.FileId.ShouldBe("file-1");
        parsed.Document.FileName.ShouldBe("spec.pdf");
        parsed.Document.FileSize.ShouldBe(1234);
        parsed.Document.Metadata.UserNote.ShouldBe("Спецификация");
        parsed.Document.Metadata.Tags.ShouldBe(["spec"]);
    }

    [Fact]
    public void LinkInCaption_OfDocument_StaysInNoteAndDocumentWins()
    {
        const string caption = "см. https://example.com";
        var message = new Message
        {
            Document = new Document { FileId = "f", FileName = "a.txt" },
            Caption = caption,
            CaptionEntities = [Entity(MessageEntityType.Url, caption, "https://example.com")],
        };

        var parsed = MessageParser.Parse(message)!;

        parsed.Item.ShouldBeNull();
        parsed.Document.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyText_HasNothingToSave(string text) =>
        MessageParser.Parse(Text(text)).ShouldBeNull();

    [Fact]
    public void OnlyHashtags_HaveNothingToSave()
    {
        const string text = "#azure #dotnet";
        var message = Text(text,
            Entity(MessageEntityType.Hashtag, text, "#azure"),
            Entity(MessageEntityType.Hashtag, text, "#dotnet"));

        MessageParser.Parse(message).ShouldBeNull();
    }

    [Fact]
    public void MalformedEntityRange_IsIgnored()
    {
        var message = Text("короткий", new MessageEntity { Type = MessageEntityType.Url, Offset = 5, Length = 100 });

        var item = MessageParser.Parse(message)!.Item!;

        item.Type.ShouldBe(ItemType.Note);
        item.Body.ShouldBe("короткий");
    }

    [Theory]
    [InlineData("/start abc123", "start", "abc123")]
    [InlineData("/start", "start", "")]
    [InlineData("/Start@KhBot  abc_-9 ", "start", "abc_-9")]
    [InlineData("/search service bus", "search", "service bus")]
    public void ParseCommand_SplitsNameAndArgument(string text, string name, string argument)
    {
        var command = MessageParser.ParseCommand(Text(text))!;

        command.Name.ShouldBe(name);
        command.Argument.ShouldBe(argument);
    }

    [Theory]
    [InlineData("обычный текст")]
    [InlineData("текст со слешем / посередине")]
    public void ParseCommand_NotACommand_ReturnsNull(string text) =>
        MessageParser.ParseCommand(Text(text)).ShouldBeNull();

    [Fact]
    public void IgnoredEntities_DoNotAffectTheResult()
    {
        const string text = "жирный текст https://a.example.com";
        var message = Text(text,
            new MessageEntity { Type = MessageEntityType.Bold, Offset = 0, Length = 6 },
            new MessageEntity { Type = MessageEntityType.Hashtag, Offset = -1, Length = 3 },
            new MessageEntity { Type = MessageEntityType.Hashtag, Offset = 0, Length = 0 },
            Entity(MessageEntityType.Url, text, "https://a.example.com"));

        var item = MessageParser.Parse(message)!.Item!;

        item.Url.ShouldBe("https://a.example.com");
        item.UserNote.ShouldBe("жирный текст");
        item.Tags.ShouldBeEmpty();
    }

    [Fact]
    public void TextLinks_OnlyTheFirstUsableOneCounts()
    {
        const string text = "пусто первая вторая";
        var message = Text(text,
            Entity(MessageEntityType.TextLink, text, "пусто", " "),
            Entity(MessageEntityType.TextLink, text, "первая", "https://first.example.com"),
            Entity(MessageEntityType.TextLink, text, "вторая", "https://second.example.com"));

        var item = MessageParser.Parse(message)!.Item!;

        item.Url.ShouldBe("https://first.example.com");
    }

    [Fact]
    public void UrlAfterTextLink_IsKeptInTheNote()
    {
        const string text = "якорь https://other.example.com";
        var message = Text(text,
            Entity(MessageEntityType.TextLink, text, "якорь", "https://first.example.com"),
            Entity(MessageEntityType.Url, text, "https://other.example.com"));

        var item = MessageParser.Parse(message)!.Item!;

        item.Url.ShouldBe("https://first.example.com");
        item.UserNote.ShouldBe("якорь https://other.example.com");
    }

    [Fact]
    public void OverlappingEntities_AreRemovedOnce()
    {
        const string text = "https://example.com/#frag конец";
        var message = Text(text,
            Entity(MessageEntityType.Url, text, "https://example.com/#frag"),
            Entity(MessageEntityType.Hashtag, text, "#frag"));

        var item = MessageParser.Parse(message)!.Item!;

        item.UserNote.ShouldBe("конец");
        item.Tags.ShouldBe(["frag"]);
    }

    [Fact]
    public void DocumentWithoutName_GetsDefaultName()
    {
        var message = new Message { Document = new Document { FileId = "f" } };

        var document = MessageParser.Parse(message)!.Document!;

        document.FileName.ShouldBe("file");
        document.Metadata.UserNote.ShouldBeNull();
    }

    [Fact]
    public void CaptionOnly_UsesCaptionEntities()
    {
        const string caption = "https://c.example.com";
        var message = new Message { Caption = caption, CaptionEntities = [Entity(MessageEntityType.Url, caption, caption)] };

        MessageParser.Parse(message)!.Item!.Url.ShouldBe(caption);
    }

    [Fact]
    public void NoEntitiesAtAll_IsAPlainNote() =>
        MessageParser.Parse(new Message { Text = "просто" })!.Item!.Body.ShouldBe("просто");

    [Fact]
    public void MessageWithoutText_HasNothingToSave() =>
        MessageParser.Parse(new Message()).ShouldBeNull();

    [Fact]
    public void ParseCommand_WithoutText_ReturnsNull() =>
        MessageParser.ParseCommand(new Message()).ShouldBeNull();
}
