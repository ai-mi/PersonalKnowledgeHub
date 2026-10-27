namespace KnowledgeHub.Api.Infrastructure.Web;

public static class SecurityHeadersExtensions
{
    // SPA — собственный бандл без inline-скриптов и стилей; Swagger UI (только Development) их использует, поэтому CSP там не ставим.
    private const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; base-uri 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool includeContentSecurityPolicy)
    {
        return app.Use((context, next) =>
        {
            // OnStarting: заголовки добавляются и к ответам, которые формируют нижележащие middleware (статика, 429, ошибки).
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                if (includeContentSecurityPolicy)
                {
                    headers.ContentSecurityPolicy = ContentSecurityPolicy;
                }

                return Task.CompletedTask;
            });
            return next(context);
        });
    }
}
