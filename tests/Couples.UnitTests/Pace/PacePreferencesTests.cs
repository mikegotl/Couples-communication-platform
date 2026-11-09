using Couples.Domain.Common;
using Couples.Domain.Pace;

namespace Couples.UnitTests.Pace;

public sealed class PacePreferencesTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static PacePreferences Quiet(int startHour, int endHour, string zone = "UTC")
    {
        var p = PacePreferences.Default(Guid.NewGuid());
        Assert.True(p.SetQuietHours(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), zone, Noon).Succeeded);
        return p;
    }

    [Fact]
    public void NoQuietHoursShowsImmediately() =>
        Assert.Equal(Noon, PacePreferences.Default(Guid.NewGuid()).NotificationVisibleAt(Noon));

    [Fact]
    public void OutsideQuietHoursShowsImmediately() =>
        Assert.Equal(Noon, Quiet(22, 7).NotificationVisibleAt(Noon));

    [Fact]
    public void DaytimeWindowHoldsUntilItEnds() =>
        Assert.Equal(Noon.AddHours(2), Quiet(11, 14).NotificationVisibleAt(Noon));

    [Fact]
    public void OvernightWindowBeforeMidnightHoldsUntilNextMorning()
    {
        var late = new DateTimeOffset(2026, 11, 9, 23, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 7, 0, 0, TimeSpan.Zero), Quiet(22, 7).NotificationVisibleAt(late));
    }

    [Fact]
    public void OvernightWindowAfterMidnightHoldsUntilThatMorning()
    {
        var early = new DateTimeOffset(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 7, 0, 0, TimeSpan.Zero), Quiet(22, 7).NotificationVisibleAt(early));
    }

    [Fact]
    public void UsesTheUsersTimeZoneIncludingDaylightSaving()
    {
        // 06:30 UTC on 1 July is 07:30 BST, inside 22:00–08:00; quiet hours end at 08:00 BST, which is 07:00 UTC.
        var summer = new DateTimeOffset(2026, 7, 1, 6, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 7, 0, 0, TimeSpan.Zero), Quiet(22, 8, "Europe/London").NotificationVisibleAt(summer));

        // The same instant in winter is 06:30 GMT; quiet hours end at 08:00 GMT.
        var winter = new DateTimeOffset(2026, 12, 1, 6, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero), Quiet(22, 8, "Europe/London").NotificationVisibleAt(winter));
    }

    [Theory]
    [InlineData(22, null, "UTC")]
    [InlineData(null, 7, "UTC")]
    [InlineData(9, 9, "UTC")]
    [InlineData(22, 7, "Not/AZone")]
    [InlineData(22, 7, "")]
    public void RejectsInvalidQuietHours(int? start, int? end, string zone)
    {
        var p = PacePreferences.Default(Guid.NewGuid());
        var result = p.SetQuietHours(start is null ? null : new TimeOnly(start.Value, 0), end is null ? null : new TimeOnly(end.Value, 0), zone, Noon);
        Assert.Equal(DomainError.Validation, result.Error);
    }

    [Fact]
    public void PauseIsBoundedAndExpires()
    {
        var p = PacePreferences.Default(Guid.NewGuid());

        Assert.Equal(DomainError.Validation, p.Pause(Noon.AddMinutes(-1), Noon).Error);
        Assert.Equal(DomainError.Validation, p.Pause(Noon.AddDays(31), Noon).Error);
        Assert.True(p.Pause(Noon.AddDays(1), Noon).Succeeded);
        Assert.True(p.IsPaused(Noon.AddHours(23)));
        Assert.False(p.IsPaused(Noon.AddDays(1)));

        p.Resume(Noon);
        Assert.False(p.IsPaused(Noon));
    }
}
