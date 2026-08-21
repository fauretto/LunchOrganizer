namespace LunchOrganizer.Services.Import;

/// <summary>
/// Thrown by <c>MenuDocumentParser</c> (and any other <c>IMenuDocumentParser</c> implementation)
/// for every anticipated parsing problem — a document that isn't a valid .docx, a table shape the
/// parser doesn't recognize, a date that doesn't parse or isn't chronological, and so on.
/// <c>MenuImportService</c> (a sibling service, not part of this file) catches this exception and
/// converts it into a failed <c>OperationResult</c> for the caller.
/// </summary>
public sealed class MenuDocumentParseException(string errorCode, string message, IReadOnlyList<object?>? messageArgs = null)
    : Exception(message)
{
    /// <summary>
    /// The <see cref="LunchOrganizer.Domain.Common.ErrorCodes"/> value identifying this failure.
    /// The UI maps this to a localized, user-facing message; the base <see cref="Exception.Message"/>
    /// passed to the constructor is only the English developer-facing fallback and must never be
    /// shown to an end user as-is (same contract as <c>OperationResult.Message</c>).
    /// </summary>
    public string ErrorCode { get; } = errorCode;

    /// <summary>
    /// The localization arguments for <see cref="ErrorCode"/>'s resource string (same contract as
    /// <c>OperationResult.MessageArgs</c>), or <see langword="null"/> when the error code takes no arguments.
    /// </summary>
    public IReadOnlyList<object?>? MessageArgs { get; } = messageArgs;
}
