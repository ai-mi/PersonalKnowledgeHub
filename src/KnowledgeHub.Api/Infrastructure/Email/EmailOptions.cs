namespace KnowledgeHub.Api.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Строка подключения Azure Communication Services (в Azure — из Key Vault).</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Адрес отправителя на Azure Managed Domain, например <c>DoNotReply@xxxx.azurecomm.net</c>.</summary>
    public string From { get; set; } = "";
}
