using KnowledgeHub.Api.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Extensions;

namespace KnowledgeHub.Api.Infrastructure.Web;

public static class ProblemDetailsExtensions
{
    public static IServiceCollection AddProblemDetailsHandling(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<AppExceptionHandler>();
        return services;
    }

    public static IApplicationBuilder UseProblemDetailsHandling(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }
}

public sealed class AppExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not AppException app)
        {
            return false;
        }

        var problem = app.Errors is null
            ? new Microsoft.AspNetCore.Mvc.ProblemDetails()
            : new HttpValidationProblemDetails(app.Errors);
        problem.Status = app.Status;
        problem.Title = app.Title;
        problem.Detail = app.Detail;

        httpContext.Response.StatusCode = app.Status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}