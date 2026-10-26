using KnowledgeHub.Api.Modules.Items;

namespace KnowledgeHub.UnitTests.Items;

public sealed class TagNameTests
{
    [Theory]
    [InlineData("azure", "azure")]
    [InlineData("  azure  ", "azure")]
    [InlineData("#azure", "azure")]
    [InlineData("##azure", "azure")]
    [InlineData("# azure", "azure")]
    [InlineData("service bus", "service-bus")]
    [InlineData("  service \t  bus  ", "service-bus")]
    [InlineData("C#", "C#")]
    [InlineData("Azure", "Azure")]
    [InlineData("#", "")]
    [InlineData("   ", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_AppliesTheTagRules(string? raw, string expected) =>
        TagName.Normalize(raw).ShouldBe(expected);

    [Fact]
    public void Comparer_IgnoresCase()
    {
        TagName.Comparer.Equals("Azure", "azure").ShouldBeTrue();
        TagName.Comparer.Equals("azure", "aws").ShouldBeFalse();
    }

    [Fact]
    public void Sort_OrdersWithoutRegardToCase() =>
        TagName.Sort(["beta", "Alpha", "gamma"]).ShouldBe(["Alpha", "beta", "gamma"]);
}
