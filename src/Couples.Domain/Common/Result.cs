namespace Couples.Domain.Common;

/// <summary>Outcome of an operation that can fail for an expected, user-facing reason.</summary>
public class Result
{
    protected Result(DomainError? error) => Error = error;

    public DomainError? Error { get; }

    public bool Succeeded => Error is null;

    public static Result Ok() => new(null);

    public static Result Fail(DomainError error) => new(error);

    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);
}

/// <summary>Outcome carrying a value on success.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Factory methods are the intended construction API.")]
public sealed class Result<T> : Result
{
    private readonly T? value;

    private Result(T? value, DomainError? error)
        : base(error) => this.value = value;

    public T Value => Succeeded
        ? value!
        : throw new InvalidOperationException($"Result failed with {Error}; there is no value.");

    public static Result<T> Ok(T value) => new(value, null);

    public static new Result<T> Fail(DomainError error) => new(default, error);

    public static implicit operator Result<T>(DomainError error) => Fail(error);
}

/// <summary>
/// Expected failure reasons. Messages are safe to show to the acting user and never reveal
/// whether another user's workspace or data exists.
/// </summary>
public enum DomainError
{
    NotFound,
    Validation,
    AlreadyInWorkspace,
    WorkspaceFull,
    InvitationInvalid,
    CannotAcceptOwnInvitation,
    Conflict,
    ConsentMismatch,
    NoPartner,
    WarningNotAcknowledged,
    PartnerPaused,
}
