namespace Couples.Domain.Rewards;

/// <summary>One shared message as the score sees it. Built in memory; never stored.</summary>
public readonly record struct MessageSignal(DateTimeOffset SharedAt, Guid SenderUserId, bool IsReply, bool Warm);

/// <summary>A week's couple-level score. Shared by both partners; never split per person.</summary>
public sealed record WeekScore(DateOnly WeekStart, int ConnectedDays, bool BackAndForth, int Messages, int WarmMessages, int StayingInTouch, int BothVoices, int KindWords)
{
    public int Total => StayingInTouch + BothVoices + KindWords;
}

/// <summary>
/// How the shared "connection rhythm" score works (ADR-0017). Kind and hard to game by volume:
/// <list type="bullet">
/// <item>Staying in touch (up to 40): days in the week you exchanged a message, counting at most three. Daily messaging earns nothing extra.</item>
/// <item>Both voices (up to 20): someone replied to the other, or you both shared something, at least once in the week.</item>
/// <item>Kind words (up to 40): the share of the week's messages with warm or appreciative wording. A share, so more messages don't help.</item>
/// </list>
/// Weeks run Monday to Sunday, UTC.
/// </summary>
public static class ConnectionScore
{
    public const int StayingInTouchMax = 40;
    public const int BothVoicesMax = 20;
    public const int KindWordsMax = 40;

    /// <summary>Connected days that earn full "staying in touch" points. A few days a week is plenty.</summary>
    public const int DaysForFullRhythm = 3;

    /// <summary>Credits for one finished week: one per 10 points, plus a bonus for growing by 10 or more.</summary>
    public const int GrowthBonus = 2;

    public static DateOnly WeekStartOf(DateTimeOffset moment)
    {
        var day = DateOnly.FromDateTime(moment.UtcDateTime);
        var offset = ((int)day.DayOfWeek + 6) % 7; // Monday = 0
        return day.AddDays(-offset);
    }

    public static DateTimeOffset StartOf(DateOnly weekStart) => new(weekStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    public static WeekScore Score(DateOnly weekStart, IEnumerable<MessageSignal> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var from = StartOf(weekStart);
        var to = from.AddDays(7);
        var week = messages.Where(m => m.SharedAt >= from && m.SharedAt < to).ToList();

        var days = week.Select(m => DateOnly.FromDateTime(m.SharedAt.UtcDateTime)).Distinct().Count();
        var backAndForth = week.Any(m => m.IsReply) || week.Select(m => m.SenderUserId).Distinct().Count() >= 2;
        var warm = week.Count(m => m.Warm);

        var touch = (int)Math.Round(StayingInTouchMax * Math.Min(days, DaysForFullRhythm) / (double)DaysForFullRhythm);
        var voices = backAndForth ? BothVoicesMax : 0;
        var kind = week.Count == 0 ? 0 : (int)Math.Round(KindWordsMax * warm / (double)week.Count);
        return new WeekScore(weekStart, days, backAndForth, week.Count, warm, touch, voices, kind);
    }

    public static int CreditsFor(WeekScore week, WeekScore? previous)
    {
        ArgumentNullException.ThrowIfNull(week);
        var credits = week.Total / 10;
        if (previous is not null && week.Total >= previous.Total + 10 && week.Total >= 40)
        {
            credits += GrowthBonus;
        }

        return credits;
    }

    /// <summary>The longest run of whole days with no message between <paramref name="from"/> and <paramref name="now"/>.</summary>
    public static int LongestQuietStretch(IEnumerable<MessageSignal> messages, DateTimeOffset from, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var days = messages
            .Where(m => m.SharedAt >= from && m.SharedAt <= now)
            .Select(m => DateOnly.FromDateTime(m.SharedAt.UtcDateTime))
            .Distinct()
            .Order()
            .ToList();
        var first = DateOnly.FromDateTime(from.UtcDateTime);
        var last = DateOnly.FromDateTime(now.UtcDateTime);
        var longest = 0;
        var previous = first.AddDays(-1);
        foreach (var day in days.Append(last.AddDays(1)))
        {
            longest = Math.Max(longest, day.DayNumber - previous.DayNumber - 1);
            previous = day;
        }

        return longest;
    }
}
