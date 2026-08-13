namespace LunchOrganizer.Domain.Common;

/// <summary>
/// Represents the outcome of an operation that can fail in an expected way.
/// This type IS the "expected failure" channel: callers should use it instead of
/// throwing/catching exceptions for anticipated business or validation failures.
/// The localization channel for failures is <see cref="ErrorCode"/> combined with
/// <see cref="MessageArgs"/>: the UI maps the error code to a localized resource string and
/// formats it with the supplied arguments. <see cref="Message"/> is a developer-facing
/// fallback written in English only and must never be shown to an end user as-is.
/// Instances are immutable and can only be created via the <see cref="Ok"/> and
/// <see cref="Fail(string, string?)"/> factory methods.
/// </summary>
public sealed class OperationResult
{
    /// <summary>Whether the operation completed successfully.</summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// An optional developer-facing message describing the outcome, in English.
    /// This is a fallback for logs/diagnostics only; it must never be shown to an end user
    /// as-is. User-facing text must be produced by localizing <see cref="ErrorCode"/> with
    /// <see cref="MessageArgs"/>.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// An optional stable, machine-readable code identifying the failure reason. Together with
    /// <see cref="MessageArgs"/>, this is the localization channel: the UI maps this code to a
    /// resource string per the active culture and formats it with the arguments.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Optional positional arguments to interpolate into the localized string that
    /// <see cref="ErrorCode"/> maps to (e.g. a cut-off time, a count, a name). Null or empty
    /// when the localized message takes no arguments.
    /// </summary>
    public IReadOnlyList<object?>? MessageArgs { get; }

    private OperationResult(bool isSuccess, string? message, string? errorCode, IReadOnlyList<object?>? messageArgs)
    {
        IsSuccess = isSuccess;
        Message = message;
        ErrorCode = errorCode;
        MessageArgs = messageArgs;
    }

    /// <summary>Creates a successful result, with an optional descriptive message.</summary>
    public static OperationResult Ok(string? message = null) => new(true, message, null, null);

    /// <summary>Creates a failed result with a required developer-facing message and an optional error code.</summary>
    public static OperationResult Fail(string message, string? errorCode = null) => new(false, message, errorCode, null);

    /// <summary>
    /// Creates a failed result with a required developer-facing message, an error code and the
    /// arguments needed to localize the message the error code maps to.
    /// </summary>
    public static OperationResult Fail(string message, string? errorCode, IReadOnlyList<object?>? messageArgs) =>
        new(false, message, errorCode, messageArgs);
}

/// <summary>
/// Represents the outcome of an operation that produces a value of type <typeparamref name="T"/>
/// and can fail in an expected way. This type IS the "expected failure" channel: callers should
/// use it instead of throwing/catching exceptions for anticipated business or validation failures.
/// The localization channel for failures is <see cref="ErrorCode"/> combined with
/// <see cref="MessageArgs"/>: the UI maps the error code to a localized resource string and
/// formats it with the supplied arguments. <see cref="Message"/> is a developer-facing
/// fallback written in English only and must never be shown to an end user as-is.
/// Instances are immutable and can only be created via the <see cref="Ok(T)"/>,
/// <see cref="Ok(T, string?)"/> and <see cref="Fail(string, string?)"/> factory methods.
/// </summary>
public sealed class OperationResult<T>
{
    /// <summary>Whether the operation completed successfully.</summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// An optional developer-facing message describing the outcome, in English.
    /// This is a fallback for logs/diagnostics only; it must never be shown to an end user
    /// as-is. User-facing text must be produced by localizing <see cref="ErrorCode"/> with
    /// <see cref="MessageArgs"/>.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// An optional stable, machine-readable code identifying the failure reason. Together with
    /// <see cref="MessageArgs"/>, this is the localization channel: the UI maps this code to a
    /// resource string per the active culture and formats it with the arguments.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Optional positional arguments to interpolate into the localized string that
    /// <see cref="ErrorCode"/> maps to (e.g. a cut-off time, a count, a name). Null or empty
    /// when the localized message takes no arguments.
    /// </summary>
    public IReadOnlyList<object?>? MessageArgs { get; }

    /// <summary>The resulting value when <see cref="IsSuccess"/> is true; otherwise the default value.</summary>
    public T? Value { get; }

    private OperationResult(bool isSuccess, T? value, string? message, string? errorCode, IReadOnlyList<object?>? messageArgs)
    {
        IsSuccess = isSuccess;
        Value = value;
        Message = message;
        ErrorCode = errorCode;
        MessageArgs = messageArgs;
    }

    /// <summary>Creates a successful result carrying the given value.</summary>
    public static OperationResult<T> Ok(T value) => new(true, value, null, null, null);

    /// <summary>Creates a successful result carrying the given value, with an optional descriptive message.</summary>
    public static OperationResult<T> Ok(T value, string? message) => new(true, value, message, null, null);

    /// <summary>Creates a failed result with a required developer-facing message and an optional error code.</summary>
    public static OperationResult<T> Fail(string message, string? errorCode = null) => new(false, default, message, errorCode, null);

    /// <summary>
    /// Creates a failed result with a required developer-facing message, an error code and the
    /// arguments needed to localize the message the error code maps to.
    /// </summary>
    public static OperationResult<T> Fail(string message, string? errorCode, IReadOnlyList<object?>? messageArgs) =>
        new(false, default, message, errorCode, messageArgs);
}
