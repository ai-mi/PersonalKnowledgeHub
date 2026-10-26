using KnowledgeHub.Api.Common;

namespace KnowledgeHub.UnitTests.Common;

public class ErrorsTests
{
    [Fact]
    public void NotFound_WithoutDetail_UsesTitleAsMessage()
    {
        var error = Errors.NotFound();

        error.Status.ShouldBe(404);
        error.Detail.ShouldBeNull();
        error.Message.ShouldBe("Not Found");
    }

    [Fact]
    public void Conflict_KeepsDetail()
    {
        var error = Errors.Conflict("Already exists");

        error.Status.ShouldBe(409);
        error.Detail.ShouldBe("Already exists");
        error.Message.ShouldBe("Already exists");
    }

    [Fact]
    public void Validation_KeepsAllFieldErrors()
    {
        var error = Errors.Validation(new Dictionary<string, string[]>
        {
            ["title"] = ["Too long"],
            ["url"] = ["Required", "Must be http(s)"],
        });

        error.Status.ShouldBe(400);
        error.Errors!["url"].Length.ShouldBe(2);
    }
}