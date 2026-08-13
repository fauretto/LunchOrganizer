namespace LunchOrganizer.Domain.Configuration;

public sealed class AdminUsersOptions
{
    // Bound from the root of admin-users.json, no wrapping section — see Program.cs (Web project) for how this is bound
    public const string SectionName = "";

    public List<AdminUser> Users { get; set; } = new();
}

public sealed class AdminUser
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
