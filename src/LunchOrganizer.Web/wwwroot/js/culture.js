// Writes the ASP.NET Core culture cookie. RequestLocalizationMiddleware reads it back on the
// reload that immediately follows this write (AppHeader.razor forces a full page reload right
// after calling this), and on any later request (new tab, browser/app restart).
export function setCultureCookie(cookieName, cookieValue) {
    document.cookie = cookieName + "=" + encodeURIComponent(cookieValue) + ";path=/;max-age=31536000;samesite=lax";
}
