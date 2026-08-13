namespace LunchOrganizer.Web.Components.Shared.Confirmation;

public sealed record ConfirmRequest(string Message, string Title, string ConfirmLabel, string CancelLabel, bool IsDestructive);
