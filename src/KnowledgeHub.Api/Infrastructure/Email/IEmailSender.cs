namespace KnowledgeHub.Api.Infrastructure.Email;

public interface IEmailSender
{
    /// <param name="html">HTML-версия письма; пользовательский текст в ней должен быть экранирован.</param>
    /// <param name="text">Текстовая версия для клиентов без HTML.</param>
    /// <exception cref="InvalidOperationException">Отправка e-mail не настроена.</exception>
    Task SendAsync(string to, string subject, string html, string text, CancellationToken ct);
}
