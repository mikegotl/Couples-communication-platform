using Couples.Domain.Common;

namespace Couples.Web.Components.Pages.Workspace;

internal static class WorkspaceMessages
{
    public static string Describe(DomainError? error) => error switch
    {
        DomainError.AlreadyInWorkspace => "You're already in a workspace. Leave it first if you want to join a different one.",
        DomainError.WorkspaceFull => "This workspace already has two members.",
        DomainError.InvitationInvalid => "This invite link isn't valid. It may have expired, been used already, or been replaced by a newer link. Ask for a new one.",
        DomainError.CannotAcceptOwnInvitation => "This is your own invite link. Send it to your partner so they can join.",
        DomainError.Validation => "Please check what you entered and try again.",
        DomainError.Conflict => "Something changed while you were doing this. Please refresh and try again.",
        _ => "We couldn't find that workspace.",
    };
}
