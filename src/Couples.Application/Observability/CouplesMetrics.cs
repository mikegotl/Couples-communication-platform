using System.Diagnostics.Metrics;

namespace Couples.Application.Observability;

/// <summary>
/// Anonymous operational counters. Tags are fixed, low-cardinality values (an outcome or an
/// area), never ids, text, workspace names or anything about safety. Exported only when an
/// OpenTelemetry endpoint is configured (ADR-0014).
/// </summary>
public static class CouplesMetrics
{
    public const string MeterName = "BridgeTogether";

    private static readonly Meter Meter = new(MeterName, "1.0");

    public static readonly Counter<long> MessagesShared = Meter.CreateCounter<long>("bt.messages.shared", description: "Messages delivered to a partner");

    public static readonly Counter<long> MessagesWithdrawn = Meter.CreateCounter<long>("bt.messages.withdrawn", description: "Shared messages withdrawn by their sender");

    public static readonly Counter<long> ShareBlocked = Meter.CreateCounter<long>("bt.messages.share_blocked", description: "Sends that did not go through, by reason (paused, rate_limited)");

    public static readonly Counter<long> CoachRequests = Meter.CreateCounter<long>("bt.coach.requests", description: "Coach requests by outcome");

    public static readonly Counter<long> PilotEnrollments = Meter.CreateCounter<long>("bt.pilot.enrollments", description: "Pilot enrollments and withdrawals, by action");

    public static readonly Counter<long> FeedbackSubmitted = Meter.CreateCounter<long>("bt.feedback.submitted", description: "Feedback entries by area");

    public static readonly Counter<long> AccountsDeleted = Meter.CreateCounter<long>("bt.accounts.deleted", description: "Completed account deletions");
}
