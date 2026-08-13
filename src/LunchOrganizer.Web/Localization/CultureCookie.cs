using Microsoft.AspNetCore.Localization;

namespace LunchOrganizer.Web.Localization;

/// <summary>
/// Helper for JS-interop-based cookie writes from the language-switch component. This cookie is
/// the single source of truth for the chosen language: the language switch writes it and reloads
/// the page, and <c>RequestLocalizationMiddleware</c>'s cookie provider reads it back both on
/// that immediate reload and on any later request (new tab, browser/app restart).
/// </summary>
public static class CultureCookie
{
    // CookieRequestCultureProvider.DefaultCookieName is a static readonly field, not a compile-time
    // constant, so this can't be a const despite the field's naming — static readonly instead.
    public static readonly string CookieName = CookieRequestCultureProvider.DefaultCookieName;

    public static string Build(string culture) =>
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture));
}
