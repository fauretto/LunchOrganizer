using LunchOrganizer.Web.Localization;

namespace LunchOrganizer.Tests.TestSupport;

/// <summary>
/// Trivial <see cref="IErrorMessageResolver"/> for tests that construct a ViewModel but never assert
/// on resolved error text: echoes the error code back (or "Unexpected" when none), ignoring message
/// args entirely.
/// </summary>
public sealed class FakeErrorMessageResolver : IErrorMessageResolver
{
    public string Resolve(string? errorCode, IReadOnlyList<object?>? messageArgs) => errorCode ?? "Unexpected";
}
