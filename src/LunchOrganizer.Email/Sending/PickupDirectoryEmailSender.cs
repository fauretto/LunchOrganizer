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

            var datePart = "unknown-date";
            if (message.Subject.StartsWith(opts.SubjectPrefix, StringComparison.Ordinal))
            {
                var candidate = message.Subject.Substring(opts.SubjectPrefix.Length);
                if (DateOnly.TryParseExact(candidate, opts.SubjectDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    datePart = parsed.ToString("yyyy-MM-dd");
                }
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
            var fileName = $"lunch-summary_{datePart}_{timestamp}.eml";
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
}
