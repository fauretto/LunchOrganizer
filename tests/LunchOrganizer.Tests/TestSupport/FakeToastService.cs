using LunchOrganizer.Web.Components.Shared.Toasts;

namespace LunchOrganizer.Tests.TestSupport;

/// <summary>
/// No-op <see cref="IToastService"/> for tests that construct a ViewModel but never assert on toast
/// content: always reports an empty toast list and ignores every show/dismiss call.
/// </summary>
public sealed class FakeToastService : IToastService
{
    public IReadOnlyList<ToastMessage> Toasts => Array.Empty<ToastMessage>();

    // This fake never raises Changed; explicit empty accessors satisfy the interface without a
    // backing field, so the compiler doesn't warn CS0067 for an event that is never fired.
    public event Action? Changed
    {
        add { }
        remove { }
    }

    public void ShowSuccess(string text)
    {
    }

    public void ShowError(string text)
    {
    }

    public void ShowInfo(string text)
    {
    }

    public void Dismiss(Guid id)
    {
    }
}
