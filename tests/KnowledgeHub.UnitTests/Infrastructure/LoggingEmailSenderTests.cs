using KnowledgeHub.Api.Infrastructure.Email;
using KnowledgeHub.Api.Modules.Reminders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeHub.UnitTests.Infrastructure;

/// <summary>
/// Локальный запуск без Azure Communication Services: при <c>Email:LogOnly</c> письма не отправляются, а пишутся в журнал,
/// чтобы вручную проверять код подтверждения адреса, дайджест и напоминания.
/// </summary>
public sealed class LoggingEmailSenderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task Send_WritesRecipientSubjectAndTextToTheLog_InsteadOfSending()
    {
        var logger = new RecordingLogger<LoggingEmailSender>();

        await new LoggingEmailSender(logger).SendAsync("to@example.com", "Код", "<p>html</p>", "Ваш код: 123456", Ct);

        var (level, message) = logger.Entries.ShouldHaveSingleItem();
        level.ShouldBe(LogLevel.Warning);
        message.ShouldContain("to@example.com");
        message.ShouldContain("Код");
        message.ShouldContain("Ваш код: 123456");
    }

    private static IEmailSender ResolveSender(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddRemindersModule(configuration);
        return services.BuildServiceProvider().GetRequiredService<IEmailSender>();
    }

    [Fact]
    public void LogOnly_RegistersTheLoggingSender()
    {
        ResolveSender(new() { ["Email:LogOnly"] = "true" }).ShouldBeOfType<LoggingEmailSender>();
    }

    [Fact]
    public void ByDefault_TheAzureSenderIsRegistered()
    {
        ResolveSender([]).ShouldBeOfType<AcsEmailSender>();
        ResolveSender(new() { ["Email:LogOnly"] = "false" }).ShouldBeOfType<AcsEmailSender>();
    }
}
