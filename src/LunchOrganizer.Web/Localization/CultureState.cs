using System.Globalization;
using LunchOrganizer.Domain.Configuration;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Web.Localization;

/// <summary>
/// Per-request/per-circuit read-only view of the culture that
/// <c>RequestLocalizationMiddleware</c> already chose for this request, plus validation of a
/// requested culture against the configured supported list. Blazor Server derives its culture
/// from the request, not from mutating <see cref="CultureInfo.CurrentCulture"/> on a circuit's
/// thread, so changing language is done by writing the culture cookie and reloading the page
/// (see AppHeader.razor) rather than by mutating state here.
/// </summary>
public sealed class CultureState
{
    private readonly IOptionsMonitor<AppOptions> _appOptions;

    public CultureState(IOptionsMonitor<AppOptions> appOptions)
    {
        _appOptions = appOptions;

        // RequestLocalizationMiddleware has already set CurrentCulture/CurrentUICulture for this
        // circuit's very first render, from the culture cookie or Accept-Language header.
        Current = CultureInfo.CurrentUICulture;
    }

    public CultureInfo Current { get; }

    public bool IsSupported(string cultureName)
    {
        ArgumentException.ThrowIfNullOrEmpty(cultureName);

        var supportedCultures = _appOptions.CurrentValue.SupportedCultures;
        return supportedCultures.Any(c => string.Equals(c, cultureName, StringComparison.OrdinalIgnoreCase));
    }
}
