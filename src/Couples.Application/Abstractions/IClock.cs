namespace Couples.Application.Abstractions;

/// <summary>Testable source of the current time.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
