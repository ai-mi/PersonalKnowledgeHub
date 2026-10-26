using KnowledgeHub.Api.Common;
using KnowledgeHub.Api.Infrastructure.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace KnowledgeHub.UnitTests.Common;

public class AppExceptionHandlerTests
{
    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Captured { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Captured = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Captured = context;
            return ValueTask.FromResult(true);
        }
    }

    [Theory]
    [MemberData(nameof(Errors_))]
    public async Task MapsAppExceptionToStatus(AppException exception, int expectedStatus)
    {
        var service = new CapturingProblemDetailsService();
        var context = new DefaultHttpContext();

        var handled = await new AppExceptionHandler(service)
            .TryHandleAsync(context, exception, TestContext.Current.CancellationToken);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(expectedStatus);
        service.Captured!.ProblemDetails.Status.ShouldBe(expectedStatus);
    }

    [Fact]
    public async Task ValidationError_ProducesValidationProblemDetails()
    {
        var service = new CapturingProblemDetailsService();

        await new AppExceptionHandler(service).TryHandleAsync(
            new DefaultHttpContext(), Errors.Validation("title", "Too long"), TestContext.Current.CancellationToken);

        var details = service.Captured!.ProblemDetails.ShouldBeOfType<HttpValidationProblemDetails>();
        details.Errors["title"].ShouldBe(["Too long"]);
    }

    [Fact]
    public async Task UnknownException_IsNotHandled()
    {
        var handled = await new AppExceptionHandler(new CapturingProblemDetailsService())
            .TryHandleAsync(new DefaultHttpContext(), new InvalidOperationException(), TestContext.Current.CancellationToken);

        handled.ShouldBeFalse();
    }

    public static TheoryData<AppException, int> Errors_() => new()
    {
        { Errors.NotFound(), 404 },
        { Errors.Validation("f", "m"), 400 },
        { Errors.Conflict(), 409 },
    };
}