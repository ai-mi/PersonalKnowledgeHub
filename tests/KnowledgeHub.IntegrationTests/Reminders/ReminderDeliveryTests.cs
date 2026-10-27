using KnowledgeHub.Api.Modules.Bot;
using KnowledgeHub.Api.Modules.Items;
using KnowledgeHub.Api.Modules.Jobs;
using KnowledgeHub.Api.Modules.Reminders;
using KnowledgeHub.IntegrationTests.Bot;
using KnowledgeHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeHub.IntegrationTests.Reminders;

public sealed class ReminderDeliveryTests : BotTestBase
{
    public ReminderDeliveryTests(ApiFactory factory) : base(factory) => factory.Email.Reset();

    private FakeEmailSender Email => Factory.Email;

    private async Task<Guid> CreateItemAsync(Guid userId, string title = "Service Bus")
    {
        using var client = ClientFor(userId);
        var item = await Items.ItemEndpointsTests.CreateAsync(client, new { type = "note", title, body = "text" });
        return item.GetProperty("id").GetGuid();
    }

    private Task<Guid> AddReminderAsync(Guid userId, Guid itemId, NotificationChannel channel, TimeSpan after) =>
        WithDbAsync(async (db, _) =>
        {
            var id = Guid.CreateVersion7();
            db.Reminders.Add(new Reminder
            {
                Id = id, UserId = userId, ItemId = itemId, Channel = channel, RemindAt = Time.GetUtcNow() + after, CreatedAt = Time.GetUtcNow(),
            });
            await db.SaveChangesAsync(Ct);
            return id;
        });

    /// <summary>Тик планировщика («раз в минуту») и выполнение всех поставленных задач.</summary>
    private async Task TickAsync()
    {
        await Services.GetRequiredService<RecurringScheduler>().ExecuteOnceAsync(typeof(DueRemindersTask), "reminders-due", Ct);
        while (await ProcessAsync())
        {
        }
    }

    private Task<Reminder> ReminderAsync(Guid id) =>
        WithDbAsync((db, _) => db.Reminders.AsNoTracking().SingleAsync(r => r.Id == id, Ct));

    [Fact]
    public async Task BeforeTheDueTime_NothingIsSent()
    {
        var userId = await LinkedUserAsync();
        var id = await AddReminderAsync(userId, await CreateItemAsync(userId), NotificationChannel.Telegram, TimeSpan.FromHours(1));

        await TickAsync();
        Time.Advance(TimeSpan.FromMinutes(59));
        await TickAsync();

        Telegram.Sent.ShouldBeEmpty();
        (await ReminderAsync(id)).SentAt.ShouldBeNull();
    }

    [Fact]
    public async Task AfterTheDueTime_TelegramReminderIsSentExactlyOnce()
    {
        var userId = await LinkedUserAsync();
        var itemId = await CreateItemAsync(userId, "Service Bus");
        var id = await AddReminderAsync(userId, itemId, NotificationChannel.Telegram, TimeSpan.FromHours(1));

        Time.Advance(TimeSpan.FromHours(2));
        await TickAsync();
        await TickAsync();
        Time.Advance(TimeSpan.FromMinutes(1));
        await TickAsync();

        var message = Telegram.Sent.ShouldHaveSingleItem();
        message.ChatId.ShouldBe(ChatId);
        message.Text.ShouldBe("Напоминание: Service Bus");
        message.Buttons.ShouldBe([BotMessageFormatter.ReadButton(itemId)]);
        Email.Sent.ShouldBeEmpty();
        (await ReminderAsync(id)).SentAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task BothChannels_SendTelegramAndEmail()
    {
        var userId = await LinkedUserAsync();
        await AddReminderAsync(userId, await CreateItemAsync(userId), NotificationChannel.Both, TimeSpan.FromMinutes(5));

        Time.Advance(TimeSpan.FromMinutes(6));
        await TickAsync();

        Telegram.Sent.ShouldHaveSingleItem();
        var mail = Email.Sent.ShouldHaveSingleItem();
        mail.To.ShouldBe($"{userId:N}@test.local");
        mail.Subject.ShouldBe("Напоминание: Service Bus");
        mail.Text.ShouldBe("Напоминание: Service Bus");
    }

    [Fact]
    public async Task EmailChannel_UsesTheAddressFromSettings()
    {
        var userId = await LinkedUserAsync();
        await WithDbAsync((db, _) => db.UserSettings.Where(s => s.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, "notify@example.com"), Ct));
        await AddReminderAsync(userId, await CreateItemAsync(userId), NotificationChannel.Email, TimeSpan.FromMinutes(5));

        Time.Advance(TimeSpan.FromMinutes(6));
        await TickAsync();

        Email.Sent.ShouldHaveSingleItem().To.ShouldBe("notify@example.com");
        Telegram.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task EmailWithoutAddress_StillSendsTelegram()
    {
        var userId = await LinkedUserAsync();
        await WithDbAsync(async (db, _) =>
        {
            await db.UserSettings.Where(s => s.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, (string?)null), Ct);
            return await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, (string?)null), Ct);
        });
        var id = await AddReminderAsync(userId, await CreateItemAsync(userId), NotificationChannel.Both, TimeSpan.FromMinutes(5));

        Time.Advance(TimeSpan.FromMinutes(6));
        await TickAsync();

        Telegram.Sent.ShouldHaveSingleItem();
        Email.Sent.ShouldBeEmpty();
        (await ReminderAsync(id)).SentAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task EmailFailure_DoesNotBlockTelegramOrRepeatTheReminder()
    {
        var userId = await LinkedUserAsync();
        await AddReminderAsync(userId, await CreateItemAsync(userId), NotificationChannel.Both, TimeSpan.FromMinutes(5));
        Email.Fail = true;

        Time.Advance(TimeSpan.FromMinutes(6));
        await TickAsync();
        Time.Advance(TimeSpan.FromHours(1));
        await TickAsync();

        Telegram.Sent.ShouldHaveSingleItem();
        (await AllJobsAsync()).ShouldAllBe(j => j.Status == JobStatus.Completed);
    }

    [Fact]
    public async Task UserWithoutTelegramLink_ReminderIsMarkedSentWithoutRetries()
    {
        var userId = Guid.CreateVersion7();
        using (await Factory.CreateClientForAsync(userId))
        {
        }

        var id = await AddReminderAsync(userId, await CreateItemAsync(userId), NotificationChannel.Telegram, TimeSpan.FromMinutes(5));

        Time.Advance(TimeSpan.FromMinutes(6));
        await TickAsync();
        await TickAsync();

        Telegram.Sent.ShouldBeEmpty();
        (await ReminderAsync(id)).SentAt.ShouldNotBeNull();
        (await AllJobsAsync()).ShouldHaveSingleItem().Status.ShouldBe(JobStatus.Completed);
    }

    [Fact]
    public async Task RunningTheJobTwice_DoesNotDuplicateTheReminder()
    {
        var userId = await LinkedUserAsync();
        var id = await AddReminderAsync(userId, await CreateItemAsync(userId), NotificationChannel.Telegram, TimeSpan.FromMinutes(5));
        Time.Advance(TimeSpan.FromMinutes(6));
        await TickAsync();

        await WithDbAsync((_, queue) => queue.EnqueueAsync(SendReminderJob.JobType, new SendReminderPayload(id), userId, dedupKey: "manual-repeat", ct: Ct));
        await TickAsync();

        Telegram.Sent.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task TitleWithMarkup_IsEscaped()
    {
        var userId = await LinkedUserAsync();
        await AddReminderAsync(userId, await CreateItemAsync(userId, "<script>alert(1)</script>"), NotificationChannel.Both, TimeSpan.FromMinutes(5));

        Time.Advance(TimeSpan.FromMinutes(6));
        await TickAsync();

        Telegram.Sent.ShouldHaveSingleItem().Text.ShouldBe("Напоминание: &lt;script&gt;alert(1)&lt;/script&gt;");
        Email.Sent.ShouldHaveSingleItem().Html.ShouldNotContain("<script>");
    }

    [Fact]
    public async Task OnlyDueRemindersAreSent()
    {
        var userId = await LinkedUserAsync();
        var itemId = await CreateItemAsync(userId);
        var due = await AddReminderAsync(userId, itemId, NotificationChannel.Telegram, TimeSpan.FromMinutes(5));
        var notYet = await AddReminderAsync(userId, itemId, NotificationChannel.Telegram, TimeSpan.FromDays(1));

        Time.Advance(TimeSpan.FromMinutes(6));
        await TickAsync();

        (await ReminderAsync(due)).SentAt.ShouldNotBeNull();
        (await ReminderAsync(notYet)).SentAt.ShouldBeNull();
        Telegram.Sent.ShouldHaveSingleItem();
    }
}
