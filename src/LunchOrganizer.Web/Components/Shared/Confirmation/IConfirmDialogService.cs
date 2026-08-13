namespace LunchOrganizer.Web.Components.Shared.Confirmation;

/// <summary>
/// Scoped (one per circuit) confirmation-dialog plumbing. This service contains zero hard-coded
/// business wording — callers pass already-localized, consequence-stating text (plan §6.6); only
/// the structural button/title defaults fall back to generic <c>Shared</c> resource keys.
/// </summary>
public interface IConfirmDialogService
{
    event Action<ConfirmRequest>? Requested;

    Task<bool> ConfirmAsync(string message, string? title = null, string? confirmLabel = null, string? cancelLabel = null, bool isDestructive = false);
}
