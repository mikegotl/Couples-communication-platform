using System.Security.Cryptography;
using System.Text;
using Couples.Application.Abstractions;
using Couples.Application.Observability;
using Couples.Application.Pilot;
using Couples.Domain.Common;
using Couples.Domain.Pilot;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Couples.Infrastructure.Pilot;

internal sealed class PilotService(CouplesDbContext db, IClock clock, IOptions<PilotOptions> options) : IPilotService
{
    public async Task<PilotStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default) =>
        ToStatus(await db.PilotEnrollments.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == userId, cancellationToken));

    public async Task<bool> IsEnrolledAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.PilotEnrollments.AnyAsync(
            e => e.UserId == userId && e.WithdrawnAt == null
                && e.TermsVersion == PilotDocuments.TermsVersion && e.PrivacyNoticeVersion == PilotDocuments.PrivacyNoticeVersion,
            cancellationToken);

    public async Task<Result<PilotStatus>> EnrollAsync(Guid userId, string termsVersion, string privacyNoticeVersion, string? accessCode, CancellationToken cancellationToken = default)
    {
        var existing = await db.PilotEnrollments.FirstOrDefaultAsync(e => e.UserId == userId, cancellationToken);

        // Someone already in the pilot who is re-agreeing to updated documents needs no code or place.
        if (existing is null || existing.WithdrawnAt is not null)
        {
            if (!CodeMatches(accessCode))
            {
                return DomainError.Validation;
            }

            var active = await db.PilotEnrollments.CountAsync(e => e.WithdrawnAt == null, cancellationToken);
            if (active >= options.Value.MaxParticipants)
            {
                return DomainError.PilotFull;
            }
        }

        var now = clock.UtcNow;
        if (existing is null)
        {
            var created = PilotEnrollment.Create(userId, termsVersion, privacyNoticeVersion, now);
            if (!created.Succeeded)
            {
                return created.Error!.Value;
            }

            db.PilotEnrollments.Add(created.Value);
            existing = created.Value;
        }
        else
        {
            var consented = existing.Consent(termsVersion, privacyNoticeVersion, now);
            if (!consented.Succeeded)
            {
                return consented.Error!.Value;
            }
        }

        db.AuditEvents.Add(Couples.Domain.Auditing.AuditEvent.Record(userId, Couples.Domain.Auditing.AuditActions.PilotEnrolled, null, null, now));
        await db.SaveChangesAsync(cancellationToken);
        CouplesMetrics.PilotEnrollments.Add(1, new KeyValuePair<string, object?>("action", "enrolled"));
        return Result.Ok(ToStatus(existing));
    }

    public async Task<PilotStatus> WithdrawAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var existing = await db.PilotEnrollments.FirstOrDefaultAsync(e => e.UserId == userId, cancellationToken);
        if (existing is { WithdrawnAt: null })
        {
            var now = clock.UtcNow;
            existing.Withdraw(now);
            db.AuditEvents.Add(Couples.Domain.Auditing.AuditEvent.Record(userId, Couples.Domain.Auditing.AuditActions.PilotWithdrawn, null, null, now));
            await db.SaveChangesAsync(cancellationToken);
            CouplesMetrics.PilotEnrollments.Add(1, new KeyValuePair<string, object?>("action", "withdrawn"));
        }

        return ToStatus(existing);
    }

    private bool CodeMatches(string? accessCode)
    {
        var expected = options.Value.AccessCodeSha256;
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes((accessCode ?? string.Empty).Trim()));
        byte[] expectedBytes;
        try
        {
            expectedBytes = Convert.FromHexString(expected.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(actual, expectedBytes);
    }

    private static PilotStatus ToStatus(PilotEnrollment? e) => e is null
        ? new PilotStatus(false, false, false, null)
        : new PilotStatus(e.IsCurrent, e.WithdrawnAt is null && !e.IsCurrent, e.WithdrawnAt is not null, e.ConsentedAt);
}
