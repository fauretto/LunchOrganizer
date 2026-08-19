using System.Globalization;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Email.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Email.Sending;

/// <summary>
/// Writes each outgoing email as an .eml file to a local "pickup" directory instead of sending it over
/// SMTP. Used both as the configured <see cref="IEmailSender"/> for local/dev delivery
/// (when <see cref="EmailOptions.Mode"/> is <see cref="EmailDeliveryMode.PickupDirectory"/>) and,
/// unconditionally, as the dry-run preview writer regardless of the configured delivery mode.
/// </summary>
public sealed class PickupDirectoryEmailSender(
    IOptionsMonitor<EmailOptions> options,
    ILogger<PickupDirectoryEmailSender> logger) : IEmailSender
{
    public async Task<OperationResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        try
        {
            var opts = options.CurrentValue;

            var directory = string.IsNullOrEmpty(opts.PickupDirectory) ? "./mail-drop" : opts.PickupDirectory;
            if (!Path.IsPathRooted(directory))
            {
                directory = Path.Combine(AppContext.BaseDirectory, directory);
            }

            Directory.CreateDirectory(directory);

            var kind = "lunch-summary";
            var datePart = "unknown-date";
            if (TryMatchSubject(message.Subject, opts.SubjectPrefix, opts.SubjectDateFormat, out var summaryDate))
            {
                datePart = summaryDate;
            }
            else if (TryMatchSubject(message.Subject, opts.ConfirmationSubjectPrefix, opts.SubjectDateFormat, out var confirmationDate))
            {
                kind = "confirmation";
                datePart = confirmationDate;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
            var fileName = kind == "confirmation"
                ? BuildConfirmationFileName(datePart, message.To, timestamp)
                : $"lunch-summary_{datePart}_{timestamp}.eml";
            var path = Path.Combine(directory, fileName);

            var mime = MimeMessageFactory.Create(message);
            await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                await mime.WriteToAsync(stream, ct);
            }

            logger.LogInformation("Wrote preview email to pickup directory at {Path}.", path);
            return OperationResult.Ok($"Written to {path}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write the email with subject {Subject} to the pickup directory.", message.Subject);
            return OperationResult.Fail($"Failed to write the email to the pickup directory: {ex.Message}");
        }
    }

    /// <summary>
    /// Tries to strip <paramref name="prefix"/> from <paramref name="subject"/> and parse the remainder as a
    /// date using <paramref name="dateFormat"/>. An empty configured prefix must not match every subject, so
    /// it is rejected up front rather than treated as a zero-length match.
    /// </summary>
    private static bool TryMatchSubject(string subject, string prefix, string dateFormat, out string datePart)
    {
        if (!string.IsNullOrEmpty(prefix) && subject.StartsWith(prefix, StringComparison.Ordinal))
        {
            var candidate = subject.Substring(prefix.Length);
            if (DateOnly.TryParseExact(candidate, dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                datePart = parsed.ToString("yyyy-MM-dd");
                return true;
            }
        }

        datePart = "unknown-date";
        return false;
    }

    private static string BuildConfirmationFileName(string datePart, IReadOnlyList<string> to, string timestamp)
    {
        var recipientSegment = to.Count > 0 ? BuildRecipientSegment(to[0]) : null;
        return string.IsNullOrEmpty(recipientSegment)
            ? $"confirmation_{datePart}_{timestamp}.eml"
            : $"confirmation_{datePart}_{recipientSegment}_{timestamp}.eml";
    }

    /// <summary>Derives a filesystem-safe segment from a recipient address's local part (before the '@').</summary>
    private static string BuildRecipientSegment(string address)
    {
        var at = address.IndexOf('@');
        var localPart = at >= 0 ? address.Substring(0, at) : address;
        var lowered = localPart.ToLower(CultureInfo.InvariantCulture);

        var sanitized = new char[lowered.Length];
        for (var i = 0; i < lowered.Length; i++)
        {
            var c = lowered[i];
            sanitized[i] = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' or '_' ? c : '-';
        }

        var result = new string(sanitized);
        return result.Length > 40 ? result.Substring(0, 40) : result;
    }
}
