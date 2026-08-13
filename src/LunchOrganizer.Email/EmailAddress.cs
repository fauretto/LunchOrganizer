namespace LunchOrganizer.Email;

/// <summary>A display name plus an email address, used as the sender of an <see cref="EmailMessage"/>.</summary>
public sealed record EmailAddress(string Name, string Address);
