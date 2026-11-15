using System.Globalization;
using System.Security.Cryptography;
using Couples.Application;
using Couples.Application.Abstractions;
using Couples.Application.Safety;
using Couples.Application.Sms;
using Couples.Domain.Auditing;
using Couples.Domain.Common;
using Couples.Domain.Consent;
using Couples.Domain.Sms;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Couples.Infrastructure.Sms;

/// <summary>Whether texts can be used in this deployment: the flag is on and a provider is configured.</summary>
internal sealed class SmsAvailability(IFeatureFlags flags, IOptions<SmsOptions> options)
{
    public SmsOptions Options => options.Value;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        options.Value.IsConfigured && await flags.IsEnabledAsync(FeatureFlags.SmsMessaging, cancellationToken);

    public string Link(string path)
    {
        var baseUrl = options.Value.PublicBaseUrl?.ToString().TrimEnd('/') ?? string.Empty;
        return baseUrl + path;
    }
}

internal sealed partial class SmsSettingsService(
    CouplesDbContext db,
    IClock clock,
    SmsAvailability availability,
    ISmsSender sender,
    ISafetyGate safety,
    ILogger<SmsSettingsService> logger) : ISmsSettingsService
{
    public async Task<SmsSettingsView> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await availability.IsAvailableAsync(cancellationToken))
        {
            return new SmsSettingsView(false, SmsSetupState.Off, null, false, false, null);
        }

        var row = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        return await ViewAsync(userId, row, cancellationToken);
    }

    public async Task<Result<SmsSettingsView>> StartAsync(Guid userId, string? phone, bool includeMessageText, bool agreedToTexts, CancellationToken cancellationToken = default)
    {
        if (!await availability.IsAvailableAsync(cancellationToken))
        {
            return DomainError.NotFound;
        }

        if (!agreedToTexts)
        {
            return DomainError.WarningNotAcknowledged;
        }

        if (!PhoneNumber.TryNormalize(phone, availability.Options.DefaultCallingCode, out var e164))
        {
            return DomainError.Validation;
        }

        var now = clock.UtcNow;
        var row = await db.SmsSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (row is null)
        {
            row = SmsSettings.Start(userId, now);
            db.SmsSettings.Add(row);
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var issued = row.IssueCode(e164, code, includeMessageText, now);
        if (!issued.Succeeded)
        {
            return issued.Error!.Value;
        }

        var saved = await SaveAsync(cancellationToken);
        if (!saved.Succeeded)
        {
            return saved.Error!.Value;
        }

        var result = await sender.SendAsync(e164, SmsText.Code(code), cancellationToken);
        LogCodeSent(logger, userId, result.Accepted);
        return Result.Ok(await ViewAsync(userId, row, cancellationToken));
    }

    public async Task<Result<SmsSettingsView>> ConfirmAsync(Guid userId, string? code, CancellationToken cancellationToken = default)
    {
        if (!await availability.IsAvailableAsync(cancellationToken))
        {
            return DomainError.NotFound;
        }

        var row = await db.SmsSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (row is null)
        {
            return DomainError.NotFound;
        }

        var now = clock.UtcNow;
        var confirmed = row.Confirm(code, now);
        if (!confirmed.Succeeded)
        {
            // A wrong code still counts against the attempts left.
            await SaveAsync(cancellationToken);
            return confirmed.Error!.Value;
        }

        if (await db.SmsSettings.AnyAsync(s => s.UserId != userId && s.PhoneE164 == row.PhoneE164 && s.VerifiedAt != null, cancellationToken))
        {
            return DomainError.Conflict;
        }

        db.ConsentRecords.Add(ConsentRecord.Grant(userId, null, ConsentPurpose.SmsMessaging, userId, ContentFingerprint.Of(row.PhoneE164), now));
        db.AuditEvents.Add(AuditEvent.Record(userId, AuditActions.SmsTurnedOn, null, null, now));
        var saved = await SaveAsync(cancellationToken);
        if (!saved.Succeeded)
        {
            return saved.Error!.Value;
        }

        LogTurnedOn(logger, userId);
        return Result.Ok(await ViewAsync(userId, row, cancellationToken));
    }

    public async Task<Result<SmsSettingsView>> SetIncludeMessageTextAsync(Guid userId, bool include, CancellationToken cancellationToken = default)
    {
        if (!await availability.IsAvailableAsync(cancellationToken))
        {
            return DomainError.NotFound;
        }

        var row = await db.SmsSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (row is null)
        {
            return DomainError.NotFound;
        }

        row.SetIncludeMessageText(include, clock.UtcNow);
        var saved = await SaveAsync(cancellationToken);
        return saved.Succeeded ? Result.Ok(await ViewAsync(userId, row, cancellationToken)) : saved.Error!.Value;
    }

    public async Task<Result<SmsSettingsView>> TurnOffAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Allowed even when texts are no longer available, so a number can always be removed.
        var now = clock.UtcNow;
        var removed = await db.SmsSettings.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        if (removed > 0)
        {
            var consent = await db.ConsentRecords
                .Where(c => c.UserId == userId && c.Purpose == ConsentPurpose.SmsMessaging && c.WithdrawnAt == null)
                .ToListAsync(cancellationToken);
            consent.ForEach(c => c.Withdraw(now));
            db.AuditEvents.Add(AuditEvent.Record(userId, AuditActions.SmsTurnedOff, null, null, now));
            await db.SaveChangesAsync(cancellationToken);
            LogTurnedOff(logger, userId);
        }

        return Result.Ok(await GetAsync(userId, cancellationToken));
    }

    private async Task<SmsSettingsView> ViewAsync(Guid userId, SmsSettings? row, CancellationToken cancellationToken)
    {
        var held = await safety.UserBlocksJointFlowsAsync(userId, cancellationToken);
        var service = availability.Options.Provider == "Twilio" ? availability.Options.Twilio.FromNumber : null;
        if (row is null)
        {
            return new SmsSettingsView(true, SmsSetupState.Off, null, false, held, service);
        }

        var state = row switch
        {
            { IsVerified: true, OptedOutAt: not null } => SmsSetupState.Stopped,
            { IsVerified: true } => SmsSetupState.On,
            { CodeHash: not null } => SmsSetupState.AwaitingCode,
            _ => SmsSetupState.Off,
        };
        return new SmsSettingsView(true, state, PhoneNumber.Mask(row.PhoneE164), row.IncludeMessageText, held, service);
    }

    private async Task<Result> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(DomainError.Conflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Result.Fail(DomainError.Conflict);
        }
    }

    // Ids only: never the number or the code.
    [LoggerMessage(Level = LogLevel.Information, Message = "Text verification code issued for user {UserId}; provider accepted: {Accepted}")]
    private static partial void LogCodeSent(ILogger logger, Guid userId, bool accepted);

    [LoggerMessage(Level = LogLevel.Information, Message = "Texts turned on for user {UserId}")]
    private static partial void LogTurnedOn(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Texts turned off for user {UserId}")]
    private static partial void LogTurnedOff(ILogger logger, Guid userId);
}
