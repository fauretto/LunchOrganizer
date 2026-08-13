using LunchOrganizer.Web.Resources;
using Microsoft.Extensions.Localization;

namespace LunchOrganizer.Web.Localization;

/// <summary>
/// The ONLY place an <c>OperationResult.ErrorCode</c> gets turned into user-facing text anywhere
/// in the app. Callers must never display <c>OperationResult.Message</c> directly — that is an
/// English developer-facing fallback, not localized, not meant for end users.
/// </summary>
public interface IErrorMessageResolver
{
    string Resolve(string? errorCode, IReadOnlyList<object?>? messageArgs);
}

public sealed class ErrorMessageResolver : IErrorMessageResolver
{
    private const string UnexpectedKey = "Unexpected";

    private readonly IStringLocalizer<Errors> _localizer;

    public ErrorMessageResolver(IStringLocalizer<Errors> localizer)
    {
        _localizer = localizer;
    }

    public string Resolve(string? errorCode, IReadOnlyList<object?>? messageArgs)
    {
        // IStringLocalizer's params-args indexer is declared as non-nullable object[]; individual
        // args are still allowed to be null at runtime (string.Format tolerates null arguments),
        // so the null-forgiving cast here only reconciles the nullable-reference annotation.
        object[] args = messageArgs?.Select(a => a!).ToArray() ?? [];

        if (string.IsNullOrEmpty(errorCode))
        {
            return _localizer[UnexpectedKey, args];
        }

        var localized = _localizer[errorCode, args];
        if (localized.ResourceNotFound)
        {
            return _localizer[UnexpectedKey, args];
        }

        return localized;
    }
}
