namespace LunchOrganizer.Web.Components.Shared.Toasts;

public enum ToastKind
{
    Success,
    Error,
    Info
}

public sealed record ToastMessage(Guid Id, ToastKind Kind, string Text);
