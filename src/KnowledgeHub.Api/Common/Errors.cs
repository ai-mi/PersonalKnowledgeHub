namespace KnowledgeHub.Api.Common;

/// <summary>Бизнес-ошибка, которая превращается в ProblemDetails с нужным статусом.</summary>
public sealed class AppException(
    int status,
    string title,
    string? detail = null,
    IDictionary<string, string[]>? errors = null) : Exception(detail ?? title)
{
    public int Status { get; } = status;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
    public IDictionary<string, string[]>? Errors { get; } = errors;
}

public static class Errors
{
    public static AppException NotFound(string? detail = null) =>
        new(StatusCodes.Status404NotFound, "Not Found", detail);

    public static AppException Validation(IDictionary<string, string[]> errors) =>
        new(StatusCodes.Status400BadRequest, "One or more validation errors occurred.", errors: errors);

    public static AppException Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]> { [field] = [message] });

    public static AppException Conflict(string? detail = null) =>
        new(StatusCodes.Status409Conflict, "Conflict", detail);
}