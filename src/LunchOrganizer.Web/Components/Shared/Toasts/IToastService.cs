namespace LunchOrganizer.Web.Components.Shared.Toasts;

/// <summary>
/// Scoped (one per circuit) store of transient toast notifications. This service only ever stores
/// already-localized plain strings — it never knows about resource keys; callers (ViewModels, later
/// stages) are responsible for localizing the text before calling in here.
/// </summary>
public interface IToastService
{
    IReadOnlyList<ToastMessage> Toasts { get; }

    event Action? Changed;

    void ShowSuccess(string text);

    void ShowError(string text);

    void ShowInfo(string text);

    void Dismiss(Guid id);
}
