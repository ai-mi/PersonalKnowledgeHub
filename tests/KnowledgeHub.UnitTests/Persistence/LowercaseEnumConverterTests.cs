using KnowledgeHub.Api.Infrastructure.Persistence;
using KnowledgeHub.Api.Modules.Identity;

namespace KnowledgeHub.UnitTests.Persistence;

public class LowercaseEnumConverterTests
{
    [Theory]
    [InlineData(DigestFrequency.Off, "off")]
    [InlineData(DigestFrequency.Daily, "daily")]
    [InlineData(DigestFrequency.Weekly, "weekly")]
    public void ConvertsToLowercaseString_AndBack(DigestFrequency value, string text)
    {
        var converter = new LowercaseEnumConverter<DigestFrequency>();

        converter.ConvertToProvider(value).ShouldBe(text);
        converter.ConvertFromProvider(text).ShouldBe(value);
    }

    [Fact]
    public void DayOfWeek_UsesFullLowercaseName()
    {
        new LowercaseEnumConverter<DayOfWeek>().ConvertToProvider(DayOfWeek.Friday).ShouldBe("friday");
    }

    [Fact]
    public void ReadingIsCaseInsensitive()
    {
        new LowercaseEnumConverter<DigestFrequency>().ConvertFromProvider("WEEKLY").ShouldBe(DigestFrequency.Weekly);
    }

    [Fact]
    public void UnknownValue_Throws()
    {
        Should.Throw<ArgumentException>(() => new LowercaseEnumConverter<DigestFrequency>().ConvertFromProvider("hourly"));
    }
}
