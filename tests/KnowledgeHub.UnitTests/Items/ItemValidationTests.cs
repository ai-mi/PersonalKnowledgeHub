using KnowledgeHub.Api.Modules.Items;

namespace KnowledgeHub.UnitTests.Items;

public class ItemValidationTests
{
    private static string Long(int length) => new('x', length);

    [Fact]
    public void ValidNote_HasNoErrors()
    {
        var errors = ItemValidation.ValidateCreate(new CreateItemCommand(ItemType.Note, "t", Body: "text"), out var type, out var url);

        errors.ShouldBeEmpty();
        type.ShouldBe(ItemType.Note);
        url.ShouldBeNull();
    }

    [Fact]
    public void Link_RequiresUrl_AndReturnsNormalizedOne()
    {
        ItemValidation.ValidateCreate(new CreateItemCommand(ItemType.Link), out _, out _).ShouldContainKey("url");

        var errors = ItemValidation.ValidateCreate(
            new CreateItemCommand(ItemType.Link, Url: "https://Example.com/a/?utm_source=x#f"), out _, out var url);

        errors.ShouldBeEmpty();
        url.ShouldBe("https://example.com/a");
    }

    [Fact]
    public void Tags_LongerThanLimitAfterNormalization_AreRejected()
    {
        var ok = ItemValidation.ValidateCreate(
            new CreateItemCommand(ItemType.Note, Tags: ["#" + Long(TagName.MaxLength), "", "  "]), out _, out _);
        var tooLong = ItemValidation.ValidateCreate(
            new CreateItemCommand(ItemType.Note, Tags: [Long(TagName.MaxLength + 1)]), out _, out _);

        ok.ShouldBeEmpty();
        tooLong.ShouldContainKey("tags");
    }

    [Fact]
    public void Link_RejectsNonHttpUrl()
    {
        ItemValidation.ValidateCreate(new CreateItemCommand(ItemType.Link, Url: "ftp://example.com"), out _, out _)
            .ShouldContainKey("url");
    }

    [Fact]
    public void Link_RejectsTooLongUrl()
    {
        var errors = ItemValidation.ValidateCreate(
            new CreateItemCommand(ItemType.Link, Url: "https://example.com/" + Long(Item.MaxUrlLength)), out _, out _);

        errors.ShouldContainKey("url");
    }

    [Fact]
    public void Type_IsRequired_AndDocumentIsRejected()
    {
        ItemValidation.ValidateCreate(new CreateItemCommand(null), out _, out _).ShouldContainKey("type");
        ItemValidation.ValidateCreate(new CreateItemCommand(ItemType.Document), out _, out _).ShouldContainKey("type");
    }

    [Theory]
    [InlineData(Item.MaxTitleLength, false)]
    [InlineData(Item.MaxTitleLength + 1, true)]
    public void Title_IsLimited(int length, bool invalid)
    {
        var errors = ItemValidation.ValidateContent(ItemType.Note, Long(length), null, null, null);

        errors.ContainsKey("title").ShouldBe(invalid);
    }

    [Theory]
    [InlineData(Item.MaxUserNoteLength, false)]
    [InlineData(Item.MaxUserNoteLength + 1, true)]
    public void UserNote_IsLimited(int length, bool invalid)
    {
        var errors = ItemValidation.ValidateContent(ItemType.Link, null, null, Long(length), null);

        errors.ContainsKey("userNote").ShouldBe(invalid);
    }

    [Theory]
    [InlineData(Item.MaxCodeLanguageLength, false)]
    [InlineData(Item.MaxCodeLanguageLength + 1, true)]
    public void CodeLanguage_IsLimited(int length, bool invalid)
    {
        var errors = ItemValidation.ValidateContent(ItemType.Snippet, null, null, null, Long(length));

        errors.ContainsKey("codeLanguage").ShouldBe(invalid);
    }

    [Theory]
    [InlineData(ItemType.Note, Item.MaxBodyLength, false)]
    [InlineData(ItemType.Note, Item.MaxBodyLength + 1, true)]
    [InlineData(ItemType.Snippet, Item.MaxBodyLength + 1, true)]
    [InlineData(ItemType.Link, Item.MaxBodyLength + 1, false)]
    public void Body_IsLimitedOnlyForNotesAndSnippets(ItemType type, int length, bool invalid)
    {
        var errors = ItemValidation.ValidateContent(type, null, Long(length), null, null);

        errors.ContainsKey("body").ShouldBe(invalid);
    }
}
