using KnowledgeHub.Api.Modules.Identity;
using KnowledgeHub.Api.Modules.Jobs;
using KnowledgeHub.Api.Modules.Reminders;
using KnowledgeHub.IntegrationTests.Bot;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeHub.IntegrationTests.Reminders;

public sealed class DigestTests : BotTestBase
{
    /// <summary>
    /// Часы теста стоят в будущем (через два дня): воркер не сдвигает их вперёд, а возраст записей задаётся от них.
    /// Назад часы не идут, поэтому тест начинается в полночь UTC, а <see cref="TickAsync"/> по умолчанию переводит их на 09:00.
    /// </summary>
    private readonly DateTimeOffset _midnight = new(DateTime.UtcNow.Date.AddDays(2), TimeSpan.Zero);

    private DateTimeOffset NineAm => _midnight.AddHours(9);

    public DigestTests(ApiFactory factory) : base(factory)
    {
        factory.Email.Reset();
        Time.SetUtcNow(_midnight);
    }

    private FakeEmailSender Email => Factory.Email;

    private Task EnableDigestAsync(
        Guid userId, string[]? channels = null, DigestFrequency frequency = DigestFrequency.Daily, DayOfWeek day = DayOfWeek.Monday) =>
        WithDbAsync((db, _) => db.UserSettings.Where(s => s.UserId == userId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.DigestFrequency, frequency)
            .SetProperty(x => x.DigestDay, day)
            .SetProperty(x => x.DigestChannels, channels ?? ["telegram"]), Ct));

    /// <summary>Запись, «созданная» <paramref name="age"/> назад (по часам теста).</summary>
    private async Task<Guid> AddItemAsync(Guid userId, string title, TimeSpan age, bool read = false)
    {
        using var client = ClientFor(userId);
        var created = await Items.ItemEndpointsTests.CreateAsync(client, new { type = "note", title, body = "text" });
        var id = created.GetProperty("id").GetGuid();
        var createdAt = Time.GetUtcNow() - age;
        await WithDbAsync((db, _) => db.Items.Where(i => i.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.CreatedAt, createdAt)
            .SetProperty(i => i.ReadAt, read ? createdAt : null), Ct));
        return id;
    }

    private Task AddReminderAsync(Guid userId, Guid itemId, TimeSpan after) =>
        WithDbAsync(async (db, _) =>
        {
            db.Reminders.Add(new Reminder
            {
                Id = Guid.CreateVersion7(), UserId = userId, ItemId = itemId, Channel = NotificationChannel.Telegram,
                RemindAt = Time.GetUtcNow() + after, CreatedAt = Time.GetUtcNow(),
            });
            await db.SaveChangesAsync(Ct);
            return 0;
        });

    /// <summary>Тик планировщика («раз в час») в момент <paramref name="at"/> (по умолчанию 09:00 UTC) и выполнение всех поставленных задач.</summary>
    private async Task TickAsync(DateTimeOffset? at = null)
    {
        var target = at ?? NineAm;
        if (Time.GetUtcNow() < target)
        {
            Time.SetUtcNow(target);
        }

        await Services.GetRequiredService<RecurringScheduler>().ExecuteOnceAsync(typeof(DigestTask), "digest-due", Ct);
        while (await ProcessAsync())
        {
        }
    }

    [Fact]
    public async Task Digest_ContainsUnreadAndYearAgo_ButNotReadOrForeignOrFreshItems()
    {
        var userId = await LinkedUserAsync();
        await EnableDigestAsync(userId);
        await AddItemAsync(userId, "Старое непрочитанное", TimeSpan.FromDays(10));
        await AddItemAsync(userId, "Год назад", TimeSpan.FromDays(366), read: true);
        await AddItemAsync(userId, "Уже прочитано", TimeSpan.FromDays(10), read: true);
        await AddItemAsync(userId, "Совсем свежее", TimeSpan.FromDays(1));
        var other = Guid.CreateVersion7();
        using (await Factory.CreateClientForAsync(other))
        {
        }

        await AddItemAsync(other, "Чужое непрочитанное", TimeSpan.FromDays(10));

        await TickAsync();

        var text = Telegram.Sent.ShouldHaveSingleItem().Text;
        text.ShouldContain("Непрочитанное\n1. Старое непрочитанное");
        text.ShouldContain("Сохранено год назад\n1. Год назад");
        text.ShouldNotContain("Уже прочитано");
        text.ShouldNotContain("Совсем свежее");
        text.ShouldNotContain("Чужое непрочитанное");
        Email.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Digest_ListsOldestUnreadFirst_AndAtMostTen()
    {
        var userId = await LinkedUserAsync();
        await EnableDigestAsync(userId);
        for (var i = 0; i < 12; i++)
        {
            await AddItemAsync(userId, $"Запись {i:00}", TimeSpan.FromDays(100 - i));
        }

        await TickAsync();

        var text = Telegram.Sent.ShouldHaveSingleItem().Text;
        text.ShouldContain("1. Запись 00");
        text.ShouldContain("10. Запись 09");
        text.ShouldNotContain("Запись 10");
    }

    [Fact]
    public async Task Digest_ContainsUpcomingRemindersWithinAWeekInLocalTime()
    {
        var userId = await LinkedUserAsync();
        await EnableDigestAsync(userId);
        await WithDbAsync((db, _) => db.UserSettings.Where(s => s.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Timezone, "Etc/GMT-3"), Ct));
        var soon = await AddItemAsync(userId, "Скоро", TimeSpan.FromHours(1), read: true);
        var later = await AddItemAsync(userId, "Нескоро", TimeSpan.FromHours(1), read: true);
        // В Etc/GMT-3 (UTC+3) 09:00 наступает в 06:00 UTC.
        var sendTime = NineAm.AddHours(-3);
        Time.SetUtcNow(sendTime);
        await AddReminderAsync(userId, soon, TimeSpan.FromDays(2) + TimeSpan.FromHours(1));
        await AddReminderAsync(userId, later, TimeSpan.FromDays(8));

        await TickAsync(sendTime);

        var text = Telegram.Sent.ShouldHaveSingleItem().Text;
        text.ShouldContain("Ближайшие напоминания\n1. Скоро — ");
        text.ShouldNotContain("Нескоро");
        var expected = Time.GetUtcNow().AddDays(2).AddHours(1).ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM HH:mm");
        text.ShouldContain($"Скоро — {expected}");
    }

    [Fact]
    public async Task EmailChannel_SendsTheDigestByEmail()
    {
        var userId = await LinkedUserAsync();
        await EnableDigestAsync(userId, ["email"]);
        await AddItemAsync(userId, "Для письма", TimeSpan.FromDays(10));

        await TickAsync();

        var mail = Email.Sent.ShouldHaveSingleItem();
        mail.Subject.ShouldBe(DigestFormatter.Subject);
        mail.Html.ShouldContain("Для письма");
        mail.Text.ShouldContain("Для письма");
        Telegram.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task SecondRunOnTheSameDay_DoesNotSendAgain()
    {
        var userId = await LinkedUserAsync();
        await EnableDigestAsync(userId);
        await AddItemAsync(userId, "Раз", TimeSpan.FromDays(10));

        await TickAsync();
        Time.Advance(TimeSpan.FromHours(1));
        await TickAsync();
        Time.Advance(TimeSpan.FromHours(5));
        await TickAsync();

        Telegram.Sent.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task NextDay_SendsANewDigest()
    {
        var userId = await LinkedUserAsync();
        await EnableDigestAsync(userId);
        await AddItemAsync(userId, "Каждый день", TimeSpan.FromDays(10));

        await TickAsync();
        Time.Advance(TimeSpan.FromDays(1));
        await TickAsync();

        Telegram.Sent.Count.ShouldBe(2);
    }

    [Fact]
    public async Task EmptyDigest_IsNotSent()
    {
        var userId = await LinkedUserAsync();
        await EnableDigestAsync(userId, ["telegram", "email"]);
        await AddItemAsync(userId, "Свежее", TimeSpan.FromHours(2));
        await AddItemAsync(userId, "Прочитанное", TimeSpan.FromDays(10), read: true);

        await TickAsync();

        Telegram.Sent.ShouldBeEmpty();
        Email.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task BeforeNineLocalTime_NothingIsQueued()
    {
        var userId = await LinkedUserAsync();
        await EnableDigestAsync(userId);
        await AddItemAsync(userId, "Рано", TimeSpan.FromDays(10));

        await TickAsync(NineAm.AddMinutes(-1));

        Telegram.Sent.ShouldBeEmpty();
        (await AllJobsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task OffAndWeeklyOnAnotherDay_AreSkipped()
    {
        var offUser = await LinkedUserAsync(1);
        var weeklyUser = await LinkedUserAsync(2);
        await AddItemAsync(offUser, "Выключено", TimeSpan.FromDays(10));
        await AddItemAsync(weeklyUser, "Не сегодня", TimeSpan.FromDays(10));
        await EnableDigestAsync(weeklyUser, frequency: DigestFrequency.Weekly, day: NineAm.AddDays(1).DayOfWeek);

        await TickAsync();

        Telegram.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Weekly_IsSentOnTheChosenDay()
    {
        var userId = await LinkedUserAsync();
        await EnableDigestAsync(userId, frequency: DigestFrequency.Weekly, day: NineAm.DayOfWeek);
        await AddItemAsync(userId, "Раз в неделю", TimeSpan.FromDays(10));

        await TickAsync();

        Telegram.Sent.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task UnreadItems_GetReadButtons()
    {
        var userId = await LinkedUserAsync();
        await EnableDigestAsync(userId);
        var id = await AddItemAsync(userId, "С кнопкой", TimeSpan.FromDays(10));

        await TickAsync();

        Telegram.Sent.ShouldHaveSingleItem().Buttons.ShouldBe([Api.Modules.Bot.BotMessageFormatter.ReadButton(id, "Прочитано 1")]);
    }
}
