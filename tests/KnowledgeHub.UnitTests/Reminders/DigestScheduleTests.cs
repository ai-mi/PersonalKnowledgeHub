using KnowledgeHub.Api.Modules.Identity;
using KnowledgeHub.Api.Modules.Reminders;

namespace KnowledgeHub.UnitTests.Reminders;

public sealed class DigestScheduleTests
{
    private static UserSettings Settings(
        string timezone, DigestFrequency frequency = DigestFrequency.Daily, DayOfWeek day = DayOfWeek.Monday) =>
        new() { UserId = Guid.CreateVersion7(), Timezone = timezone, DigestFrequency = frequency, DigestDay = day };

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Moscow_BeforeNine_IsNotDue()
    {
        // 05:59 UTC = 08:59 в Москве (UTC+3).
        DigestSchedule.IsDue(Settings("Europe/Moscow"), Utc(2026, 10, 27, 5, 59), out _).ShouldBeFalse();
    }

    [Fact]
    public void Moscow_AtNine_IsDueWithLocalDate()
    {
        DigestSchedule.IsDue(Settings("Europe/Moscow"), Utc(2026, 10, 27, 6, 0), out var date).ShouldBeTrue();
        date.ShouldBe(new DateOnly(2026, 10, 27));
    }

    [Fact]
    public void LocalDateDiffersFromUtcDate()
    {
        // 23:30 UTC 26 октября = 09:30 27 октября в Сиднее (UTC+11).
        DigestSchedule.IsDue(Settings("Australia/Sydney"), Utc(2026, 10, 26, 22, 30), out var date).ShouldBeTrue();
        date.ShouldBe(new DateOnly(2026, 10, 27));
    }

    [Fact]
    public void StaysDueLaterInTheDay_SoAMissedHourIsNotLost()
    {
        DigestSchedule.IsDue(Settings("UTC"), Utc(2026, 10, 27, 17, 0), out var date).ShouldBeTrue();
        date.ShouldBe(new DateOnly(2026, 10, 27));
    }

    [Fact]
    public void Off_IsNeverDue()
    {
        DigestSchedule.IsDue(Settings("UTC", DigestFrequency.Off), Utc(2026, 10, 27, 12, 0), out _).ShouldBeFalse();
    }

    [Fact]
    public void Weekly_OnAnotherDay_IsNotDue()
    {
        // 27 октября 2026 — вторник.
        DigestSchedule.IsDue(Settings("UTC", DigestFrequency.Weekly, DayOfWeek.Monday), Utc(2026, 10, 27, 12, 0), out _).ShouldBeFalse();
    }

    [Fact]
    public void Weekly_OnTheChosenDay_IsDue()
    {
        DigestSchedule.IsDue(Settings("UTC", DigestFrequency.Weekly, DayOfWeek.Tuesday), Utc(2026, 10, 27, 9, 0), out _).ShouldBeTrue();
    }

    [Fact]
    public void Weekly_UsesTheLocalDayOfWeek()
    {
        // 22:00 UTC во вторник = 09:00 среды в Сиднее.
        var settings = Settings("Australia/Sydney", DigestFrequency.Weekly, DayOfWeek.Wednesday);
        DigestSchedule.IsDue(settings, Utc(2026, 10, 27, 22, 0), out var date).ShouldBeTrue();
        date.ShouldBe(new DateOnly(2026, 10, 28));
    }

    [Fact]
    public void NewYork_FollowsDaylightSavingTime()
    {
        var settings = Settings("America/New_York");

        // Летом UTC-4: 09:00 = 13:00 UTC.
        DigestSchedule.IsDue(settings, Utc(2026, 7, 1, 12, 59), out _).ShouldBeFalse();
        DigestSchedule.IsDue(settings, Utc(2026, 7, 1, 13, 0), out _).ShouldBeTrue();

        // Зимой UTC-5: 09:00 = 14:00 UTC.
        DigestSchedule.IsDue(settings, Utc(2026, 12, 1, 13, 59), out _).ShouldBeFalse();
        DigestSchedule.IsDue(settings, Utc(2026, 12, 1, 14, 0), out _).ShouldBeTrue();
    }

    [Fact]
    public void UnknownTimeZone_IsNotDue()
    {
        DigestSchedule.IsDue(Settings("Mars/Olympus"), Utc(2026, 10, 27, 12, 0), out _).ShouldBeFalse();
    }
}
