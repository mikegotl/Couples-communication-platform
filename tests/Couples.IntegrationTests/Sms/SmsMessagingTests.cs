using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Couples.Application;
using Couples.Application.Abstractions;
using Couples.Application.Messaging;
using Couples.Application.Pace;
using Couples.Application.Privacy;
using Couples.Application.Safety;
using Couples.Application.Sms;
using Couples.Application.Workspaces;
using Couples.Domain.Common;
using Couples.Domain.Consent;
using Couples.Domain.Messaging;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Sms;

/// <summary>
/// Partner messages by text (ADR-0020): opt-in with a code, texts follow the same pace and safety
/// rules as notices, replies by text go through preview and SEND, STOP stops everything, and no
/// number, code or message text reaches the logs.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed partial class SmsMessagingTests(PostgresFixture db)
{
    private readonly Phone phone = new();

    [Fact]
    public async Task SettingUpTextsNeedsAgreementAndTheTextedCode()
    {
        var clock = Clock();
        var (_, b) = await PairAsync(clock);
        var number = NewNumber();

        Assert.Equal(DomainError.WarningNotAcknowledged, (await Run<ISmsSettingsService, Result<SmsSettingsView>>(clock, s => s.StartAsync(b, number, false, agreedToTexts: false))).Error);
        Assert.Equal(DomainError.Validation, (await Run<ISmsSettingsService, Result<SmsSettingsView>>(clock, s => s.StartAsync(b, "not a number", false, true))).Error);
        Assert.Empty(phone.To(number));

        var started = await Run<ISmsSettingsService, Result<SmsSettingsView>>(clock, s => s.StartAsync(b, number, false, true));
        Assert.Equal(SmsSetupState.AwaitingCode, started.Value.State);
        Assert.Equal(DomainError.Validation, (await Run<ISmsSettingsService, Result<SmsSettingsView>>(clock, s => s.ConfirmAsync(b, "000000"))).Error);

        var confirmed = await Run<ISmsSettingsService, Result<SmsSettingsView>>(clock, s => s.ConfirmAsync(b, phone.LastCode(number)));
        Assert.Equal(SmsSetupState.On, confirmed.Value.State);
        Assert.Equal("•••• " + number[^4..], confirmed.Value.MaskedPhone);

        await using var context = Context(clock);
        var consent = await context.ConsentRecords.SingleAsync(c => c.UserId == b && c.Purpose == ConsentPurpose.SmsMessaging);
        Assert.Null(consent.WithdrawnAt);
        Assert.DoesNotContain(number, consent.ContentFingerprint!, StringComparison.Ordinal);

        // Turning off forgets the number and withdraws the consent.
        var off = await Run<ISmsSettingsService, Result<SmsSettingsView>>(clock, s => s.TurnOffAsync(b));
        Assert.Equal(SmsSetupState.Off, off.Value.State);
        await using var after = Context(clock);
        Assert.False(await after.SmsSettings.AnyAsync(s => s.UserId == b));
        Assert.NotNull((await after.ConsentRecords.SingleAsync(c => c.UserId == b && c.Purpose == ConsentPurpose.SmsMessaging)).WithdrawnAt);
    }

    [Fact]
    public async Task ANumberBelongsToOneAccount()
    {
        var clock = Clock();
        var (a, b) = await PairAsync(clock);
        var number = NewNumber();
        await TurnOnAsync(clock, a, number, includeText: false);

        Assert.True((await Run<ISmsSettingsService, Result<SmsSettingsView>>(clock, s => s.StartAsync(b, number, false, true))).Succeeded);
        Assert.Equal(DomainError.Conflict, (await Run<ISmsSettingsService, Result<SmsSettingsView>>(clock, s => s.ConfirmAsync(b, phone.LastCode(number)))).Error);
    }

    [Fact]
    public async Task NoticeTextsSayOnlyThatSomethingArrived()
    {
        var clock = Clock();
        var (a, b) = await PairAsync(clock);
        var number = await TurnOnAsync(clock, b, includeText: false);
        var secret = $"Our private words {Marker()}";

        await SendAsync(clock, a, secret);
        Assert.Equal(1, await DispatchAsync(clock));
        Assert.Equal(0, await DispatchAsync(clock)); // Never twice.

        var text = Assert.Single(phone.To(number));
        Assert.StartsWith("BridgeTogether: Your partner shared a message with you.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMessageItselfIsTextedOnlyWhenTheRecipientAskedForIt()
    {
        var clock = Clock();
        var (a, b) = await PairAsync(clock);
        var number = await TurnOnAsync(clock, b, includeText: true);
        var words = $"Shall we walk tonight? {Marker()}";

        // The sender is told before sharing that a withdrawn message can't leave the partner's phone.
        var draft = await Run<IMessagingService, Result<DraftView>>(clock, s => s.CreateDraftAsync(a, words));
        Assert.True((await Run<IMessagingService, Result<SharePreview>>(clock, s => s.PreviewShareAsync(a, draft.Value.Id))).Value.PartnerReadsByText);
        Assert.True((await Run<IMessagingService, Result<SharedMessageView>>(clock, s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint))).Succeeded);

        await DispatchAsync(clock);
        Assert.Contains(words, Assert.Single(phone.To(number)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TextsFollowMuteQuietHoursAndWithdrawal()
    {
        var clock = Clock();
        var (a, b) = await PairAsync(clock);
        var number = await TurnOnAsync(clock, b, includeText: true);

        // Muted: no notice, so no text.
        await Run<IPaceService, Result<PaceSettings>>(clock, s => s.SetMutedAsync(b, true));
        await SendAsync(clock, a, "while muted");
        await DispatchAsync(clock);
        Assert.Empty(phone.To(number));
        await Run<IPaceService, Result<PaceSettings>>(clock, s => s.SetMutedAsync(b, false));

        // Quiet hours (11:00-14:00 UTC, now 12:00): held until they end.
        await Run<IPaceService, Result<PaceSettings>>(clock, s => s.SetQuietHoursAsync(b, new TimeOnly(11, 0), new TimeOnly(14, 0), "UTC"));
        await SendAsync(clock, a, "during quiet hours");
        await DispatchAsync(clock);
        Assert.Empty(phone.To(number));
        clock.Advance(TimeSpan.FromHours(2.5));
        await DispatchAsync(clock);
        Assert.Contains("during quiet hours", Assert.Single(phone.To(number)), StringComparison.Ordinal);
        await Run<IPaceService, Result<PaceSettings>>(clock, s => s.SetQuietHoursAsync(b, null, null, "UTC"));

        // Withdrawn before it was texted: nothing goes out.
        var withdrawn = await SendAsync(clock, a, "changed my mind");
        Assert.True((await Run<IMessagingService, Result>(clock, s => s.RevokeAsync(a, withdrawn.Id))).Succeeded);
        await DispatchAsync(clock);
        Assert.Single(phone.To(number));
    }

    [Fact]
    public async Task NothingIsTextedToSomeoneWhoseSafetySettingsHoldJointFlows()
    {
        var clock = Clock();
        var (a, b) = await PairAsync(clock);
        var number = await TurnOnAsync(clock, b, includeText: true);
        await Run<ISafetyService, SafetyStatus>(clock, s => s.SetSafetyModeAsync(b, true));

        await SendAsync(clock, a, "hello");
        await DispatchAsync(clock);
        Assert.Empty(phone.To(number));

        // Their texts are not passed on either, and the reply gives nothing away.
        var reply = await InboundAsync(clock, number, "I'm fine, see you later");
        Assert.Equal(SmsText.Unavailable, reply);
        await using var context = Context(clock);
        Assert.False(await context.DraftMessages.AnyAsync(d => d.OwnerUserId == b));

        // Turning safety mode on after a notice was made still stops the text.
        await Run<ISafetyService, SafetyStatus>(clock, s => s.SetSafetyModeAsync(b, false));
        await SendAsync(clock, a, "second");
        await Run<ISafetyService, SafetyStatus>(clock, s => s.SetSafetyModeAsync(b, true));
        await DispatchAsync(clock);
        Assert.Empty(phone.To(number));
    }

    [Fact]
    public async Task ATextedReplyIsSharedOnlyAfterSend()
    {
        var clock = Clock();
        var (a, b) = await PairAsync(clock);
        var number = await TurnOnAsync(clock, b, includeText: true);
        var original = await SendAsync(clock, a, "Can we talk on Saturday?");
        var answer = $"Yes, Saturday morning works. {Marker()}";

        var echo = await InboundAsync(clock, number, answer);
        Assert.Equal(SmsText.ConfirmReply(answer), echo);
        Assert.Empty(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(clock, s => s.InboxAsync(a)));

        Assert.Equal(SmsText.Sent, await InboundAsync(clock, number, " send "));
        var received = Assert.Single(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(clock, s => s.InboxAsync(a)));
        Assert.Equal(answer, received.Body);

        await using var context = Context(clock);
        var draft = await context.DraftMessages.SingleAsync(d => d.OwnerUserId == b);
        Assert.Equal(original.Id, draft.InReplyToMessageId);
        Assert.Equal(DraftStatus.Shared, draft.Status);
        Assert.True(await context.ConsentRecords.AnyAsync(c => c.UserId == b && c.Purpose == ConsentPurpose.ShareMessage && c.SubjectId == draft.Id));

        // SEND again shares nothing more.
        Assert.Equal(SmsText.NothingWaiting, await InboundAsync(clock, number, "SEND"));
    }

    [Fact]
    public async Task AnotherTextReplacesTheWaitingReplyWhichStaysAPrivateDraft()
    {
        var clock = Clock();
        var (a, b) = await PairAsync(clock);
        var number = await TurnOnAsync(clock, b, includeText: false);
        await SendAsync(clock, a, "hi");

        await InboundAsync(clock, number, "first try, too sharp");
        await InboundAsync(clock, number, "second try, kinder");
        await InboundAsync(clock, number, "SEND");

        Assert.Equal("second try, kinder", Assert.Single(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(clock, s => s.InboxAsync(a))).Body);
        var drafts = await Run<IMessagingService, IReadOnlyList<DraftView>>(clock, s => s.ListDraftsAsync(b));
        Assert.Equal("first try, too sharp", Assert.Single(drafts).Body);
    }

    [Fact]
    public async Task SendAfterThirtyMinutesOrAnEditInTheAppSharesNothing()
    {
        var clock = Clock();
        var (a, b) = await PairAsync(clock);
        var number = await TurnOnAsync(clock, b, includeText: false);
        await SendAsync(clock, a, "hi");

        await InboundAsync(clock, number, "late reply");
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(SmsText.NothingWaiting, await InboundAsync(clock, number, "SEND"));

        await InboundAsync(clock, number, "edited later");
        var draft = Assert.Single(await Run<IMessagingService, IReadOnlyList<DraftView>>(clock, s => s.ListDraftsAsync(b)), d => d.Body == "edited later");
        await Run<IMessagingService, Result<DraftView>>(clock, s => s.EditDraftAsync(b, draft.Id, "edited later, softer"));
        Assert.Equal(SmsText.DraftChanged, await InboundAsync(clock, number, "SEND"));
        Assert.Empty(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(clock, s => s.InboxAsync(a)));
    }

    [Fact]
    public async Task APausedPartnerIsExplainedAndTheReplyKept()
    {
        var clock = Clock();
        var (a, b) = await PairAsync(clock);
        var number = await TurnOnAsync(clock, b, includeText: false);
        await Run<IPaceService, Result<PaceSettings>>(clock, s => s.PauseAsync(a, clock.UtcNow.AddDays(1)));

        var reply = await InboundAsync(clock, number, "thinking of you");
        Assert.StartsWith("BridgeTogether: Your partner has paused new messages until", reply, StringComparison.Ordinal);
        Assert.Equal("thinking of you", Assert.Single(await Run<IMessagingService, IReadOnlyList<DraftView>>(clock, s => s.ListDraftsAsync(b))).Body);
    }

    [Fact]
    public async Task StopEndsTextsUntilStart()
    {
        var clock = Clock();
        var (a, b) = await PairAsync(clock);
        var number = await TurnOnAsync(clock, b, includeText: false);

        Assert.Null(await InboundAsync(clock, number, "STOP"));
        Assert.Equal(SmsSetupState.Stopped, (await Run<ISmsSettingsService, SmsSettingsView>(clock, s => s.GetAsync(b))).State);
        await SendAsync(clock, a, "while stopped");
        await DispatchAsync(clock);
        Assert.Empty(phone.To(number));
        Assert.Null(await InboundAsync(clock, number, "a reply while stopped"));
        Assert.Empty(await Run<IMessagingService, IReadOnlyList<DraftView>>(clock, s => s.ListDraftsAsync(b)));

        // Only what arrives after START is texted; the message from while texts were stopped stays in the app.
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(SmsText.Restarted, await InboundAsync(clock, number, "start"));
        await SendAsync(clock, a, "after start");
        await DispatchAsync(clock);
        Assert.Single(phone.To(number));
    }

    [Fact]
    public async Task UnknownNumbersLearnNothing()
    {
        var clock = Clock();
        Assert.Equal(SmsText.NotSetUp, await InboundAsync(clock, NewNumber(), "hello?"));
        Assert.Null(await InboundAsync(clock, NewNumber(), "STOP"));
    }

    [Fact]
    public async Task EverythingIsClosedWhileTheFlagIsOff()
    {
        var clock = Clock();
        var (a, b) = await PairAsync(clock);
        var number = await TurnOnAsync(clock, b, includeText: true);
        await SendAsync(clock, a, "hello");

        Assert.Equal(0, await DispatchAsync(clock, flagOn: false));
        Assert.Empty(phone.To(number));
        Assert.Null(await InboundAsync(clock, number, "reply", flagOn: false));
        Assert.False((await Run<ISmsSettingsService, SmsSettingsView>(clock, s => s.GetAsync(b), flagOn: false)).Available);
        Assert.Equal(DomainError.NotFound, (await Run<ISmsSettingsService, Result<SmsSettingsView>>(clock, s => s.StartAsync(b, NewNumber(), false, true), flagOn: false)).Error);
    }

    [Fact]
    public async Task TextsPerDayAreCapped()
    {
        var clock = Clock();
        var (a, b) = await PairAsync(clock);
        var number = await TurnOnAsync(clock, b, includeText: false);
        for (var i = 0; i < 3; i++)
        {
            await SendAsync(clock, a, $"message {i}");
        }

        await DispatchAsync(clock, dailyCap: 2);
        Assert.Equal(2, phone.To(number).Count);
    }

    [Fact]
    public async Task NumbersCodesAndTextsNeverReachTheLogs()
    {
        var clock = Clock();
        var logs = new CapturingLoggerProvider();
        var (a, b) = await PairAsync(clock);
        var number = NewNumber();
        await Run<ISmsSettingsService, Result<SmsSettingsView>>(clock, s => s.StartAsync(b, number, true, true), logs: logs);
        var code = phone.LastCode(number);
        await Run<ISmsSettingsService, Result<SmsSettingsView>>(clock, s => s.ConfirmAsync(b, code), logs: logs);
        var words = $"Private message {Marker()}";
        var reply = $"Private reply {Marker()}";
        await SendAsync(clock, a, words);
        await DispatchAsync(clock, logs: logs);
        await InboundAsync(clock, number, reply, logs: logs);
        await InboundAsync(clock, number, "SEND", logs: logs);

        Assert.NotEmpty(logs.Lines);
        Assert.DoesNotContain(logs.Lines, l => l.Contains(number, StringComparison.Ordinal) || l.Contains(number[2..], StringComparison.Ordinal)
            || l.Contains(code, StringComparison.Ordinal) || l.Contains(words, StringComparison.Ordinal) || l.Contains(reply, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportIncludesTextSettingsAndDeletionErasesThem()
    {
        var clock = Clock();
        var (a, b) = await PairAsync(clock);
        var number = await TurnOnAsync(clock, b, includeText: false);
        await SendAsync(clock, a, "hi");
        await DispatchAsync(clock);

        var export = await Run<IPersonalDataService, PersonalDataExport>(clock, s => s.ExportAsync(b));
        Assert.Equal(number, export.Texts!.PhoneNumber);
        Assert.Equal(1, export.Texts.TextsSentLast90Days);
        Assert.DoesNotContain(number, JsonSerializer.Serialize(await Run<IPersonalDataService, PersonalDataExport>(clock, s => s.ExportAsync(a))), StringComparison.Ordinal);

        await Run<IPersonalDataService, Guid>(clock, s => s.RequestDeletionAsync(b));
        await Run<IPersonalDataService, int>(clock, s => s.ProcessPendingDeletionsAsync());
        await using var context = Context(clock);
        Assert.False(await context.SmsSettings.AnyAsync(s => s.UserId == b));
        Assert.False(await context.SmsDeliveries.AnyAsync(d => d.RecipientUserId == b));
    }

    private async Task<string> TurnOnAsync(FakeClock clock, Guid user, string? number = null, bool includeText = false)
    {
        number ??= NewNumber();
        Assert.True((await Run<ISmsSettingsService, Result<SmsSettingsView>>(clock, s => s.StartAsync(user, number, includeText, true))).Succeeded);
        Assert.True((await Run<ISmsSettingsService, Result<SmsSettingsView>>(clock, s => s.ConfirmAsync(user, phone.LastCode(number)))).Succeeded);
        phone.Clear(number);
        return number;
    }

    private Task<int> DispatchAsync(FakeClock clock, bool flagOn = true, int dailyCap = 20, CapturingLoggerProvider? logs = null) =>
        Run<ISmsDispatcher, int>(clock, s => s.DispatchDueAsync(), flagOn, dailyCap, logs);

    private Task<string?> InboundAsync(FakeClock clock, string from, string body, bool flagOn = true, CapturingLoggerProvider? logs = null) =>
        Run<ISmsInbound, string?>(clock, s => s.HandleAsync(from, body), flagOn, logs: logs);

    private async Task<SharedMessageView> SendAsync(FakeClock clock, Guid sender, string text)
    {
        var draft = await Run<IMessagingService, Result<DraftView>>(clock, s => s.CreateDraftAsync(sender, text));
        var shared = await Run<IMessagingService, Result<SharedMessageView>>(clock, s => s.ShareAsync(sender, draft.Value.Id, draft.Value.Fingerprint));
        Assert.True(shared.Succeeded, shared.Error?.ToString());
        return shared.Value;
    }

    private async Task<(Guid A, Guid B)> PairAsync(FakeClock clock)
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var workspace = await Run<IWorkspaceService, Result<WorkspaceSummary>>(clock, s => s.CreateAsync(a, "Ours"));
        var invite = await Run<IWorkspaceService, Result<CreatedInvitation>>(clock, s => s.CreateInvitationAsync(a, workspace.Value.Id));
        Assert.True((await Run<IWorkspaceService, Result<WorkspaceSummary>>(clock, s => s.AcceptInvitationAsync(b, invite.Value.Token))).Succeeded);
        return (a, b);
    }

    private FakeClock Clock() => new(db.Clock.UtcNow);

    private static string Marker() => $"marker-{Guid.NewGuid():N}";

    /// <summary>A unique, valid US-format number per test (the 555-01xx range is reserved for fiction).</summary>
    private static string NewNumber() => "+1555" + Random.Shared.Next(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private CouplesDbContext Context(FakeClock clock) => db.BuildServices(clock).CreateScope().ServiceProvider.GetRequiredService<CouplesDbContext>();

    private async Task<TResult> Run<TService, TResult>(FakeClock clock, Func<TService, Task<TResult>> call, bool flagOn = true, int dailyCap = 20, CapturingLoggerProvider? logs = null)
        where TService : notnull
    {
        await using var provider = db.BuildServices(clock, logs, services =>
        {
            services.AddSingleton<IFeatureFlags>(new Flags(flagOn));
            services.Configure<SmsOptions>(o =>
            {
                o.Provider = "Fake";
                o.PublicBaseUrl = new Uri("https://app.example.test");
                o.DailyTextsPerUser = dailyCap;
            });
            services.AddSingleton<ISmsSender>(phone);
        });
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private sealed class Flags(bool sms) : IFeatureFlags
    {
        public Task<bool> IsEnabledAsync(string flag, CancellationToken cancellationToken = default) =>
            Task.FromResult(sms && flag == FeatureFlags.SmsMessaging);
    }

    /// <summary>Every phone in one place: texts the app sent, by number.</summary>
    private sealed partial class Phone : ISmsSender
    {
        private readonly ConcurrentDictionary<string, List<string>> inbox = new();

        public Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
        {
            var list = inbox.GetOrAdd(toE164, _ => []);
            lock (list)
            {
                list.Add(body);
            }

            return Task.FromResult(new SmsSendResult(true, "SM" + Guid.NewGuid().ToString("N")));
        }

        public IReadOnlyList<string> To(string number)
        {
            var list = inbox.GetOrAdd(number, _ => []);
            lock (list)
            {
                return [.. list];
            }
        }

        public string LastCode(string number) => CodePattern().Match(To(number)[^1]).Groups[1].Value;

        public void Clear(string number) => inbox.TryRemove(number, out _);

        [GeneratedRegex(@"code: (\d{6})\.")]
        private static partial Regex CodePattern();
    }
}
