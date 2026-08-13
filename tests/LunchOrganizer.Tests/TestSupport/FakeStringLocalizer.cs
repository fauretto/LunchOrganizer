using Microsoft.Extensions.Localization;

namespace LunchOrganizer.Tests.TestSupport;

/// <summary>
/// Minimal generic <see cref="IStringLocalizer{T}"/> for tests that construct a ViewModel but never
/// assert on actual localized text: echoes the resource key back as both name and value.
/// </summary>
public sealed class FakeStringLocalizer<T> : IStringLocalizer<T>
{
    public LocalizedString this[string name] => new(name, name);

    public LocalizedString this[string name, params object[] arguments] => new(name, name);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Enumerable.Empty<LocalizedString>();
}
