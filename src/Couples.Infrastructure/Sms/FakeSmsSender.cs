using System.Collections.Concurrent;
using Couples.Application.Abstractions;
using Couples.Application.Sms;

namespace Couples.Infrastructure.Sms;

/// <summary>
/// Development provider: keeps the last texts in memory for the /dev/texts page instead of sending
/// anything. Never registered unless <c>Sms:Provider</c> is <c>Fake</c>.
/// </summary>
public sealed class FakeSmsOutbox(IClock clock) : ISmsSender
{
    private const int Keep = 200;
    private readonly ConcurrentQueue<FakeText> texts = new();

    public IReadOnlyList<FakeText> Texts => [.. texts.Reverse()];

    public Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        Record(new FakeText(clock.UtcNow, "app", toE164, body));
        return Task.FromResult(new SmsSendResult(true, "fake-" + Guid.NewGuid().ToString("N")[..12]));
    }

    /// <summary>A text a developer "sent" from a phone on the dev page, for the conversation view.</summary>
    public void RecordInbound(string fromE164, string body) => Record(new FakeText(clock.UtcNow, fromE164, "app", body));

    private void Record(FakeText text)
    {
        texts.Enqueue(text);
        while (texts.Count > Keep && texts.TryDequeue(out _))
        {
        }
    }
}

public sealed record FakeText(DateTimeOffset At, string From, string To, string Body);
