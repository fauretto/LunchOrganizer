namespace LunchOrganizer.Web.Security;

/// <summary>
/// Shared constants for the admin authorization policy.
///
/// Why a marker claim is needed: the app is hosted in IIS with Windows Authentication enabled,
/// and <c>IISServerOptions.AutomaticAuthentication</c> defaults to <c>true</c>, so IIS sets
/// <see cref="System.Security.Claims.ClaimsPrincipal"/> on <c>HttpContext.User</c> to the
/// caller's Windows identity before app middleware ever runs. <c>app.UseAuthentication()</c>
/// only overwrites <c>HttpContext.User</c> when the configured scheme (the admin cookie)
/// actually authenticates the request — when there is no admin cookie, the IIS-supplied Windows
/// principal survives untouched, and <c>Identity.IsAuthenticated</c> is <c>true</c> for it too.
/// That means "authenticated" alone can never be used to mean "admin": every domain user who
/// simply browses to the site while Windows Authentication is on is "authenticated". Only the
/// admin cookie sign-in path (<see cref="Endpoints.AdminAuthEndpoints"/>) issues this marker
/// claim, so requiring it is what actually distinguishes an admin who signed in with
/// admin-users.json credentials from a bare IIS Windows-authenticated visitor.
/// </summary>
public static class AdminAuthorization
{
    public const string PolicyName = "LunchOrganizerAdmin";
    public const string AdminClaimType = "lunchorganizer:admin";
    public const string AdminClaimValue = "true";
}
