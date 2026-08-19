using FluentAssertions;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Email;
using LunchOrganizer.Email.Sending;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunchOrganizer.Tests.Email;

public class PickupDirectoryEmailSenderTests
{
    private static readonly EmailAddress From = new("Lunch Organizer", "lunch-organizer@cohu.com");

    [Fact]
    public async Task SendAsync_SummarySubject_ProducesLunchSummaryFileName()
    {
        var pickupDirectory = Path.Combine(Path.GetTempPath(), "LunchOrganizerTests", Guid.NewGuid().ToString("N"));
        var options = TestData.DefaultOptions(pickupDirectory: pickupDirectory);
        var sender = new PickupDirectoryEmailSender(new TestOptionsMonitor<EmailOptions>(options), NullLogger<PickupDirectoryEmailSender>.Instance);
        var message = new EmailMessage("COHU booked lunch for 17.08.2026", "<p>body</p>", "body", From, new[] { "kitchen@cohu.com" });

        try
        {
            await sender.SendAsync(message);

            var files = Directory.GetFiles(pickupDirectory, "*.eml");
            files.Should().ContainSingle();
            Path.GetFileName(files.Single()).Should().MatchRegex(@"^lunch-summary_2026-08-17_\d{8}-\d{9}\.eml$");
        }
        finally
        {
            if (Directory.Exists(pickupDirectory))
            {
                Directory.Delete(pickupDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SendAsync_ConfirmationSubject_ProducesConfirmationFileNameWithRecipient()
    {
        var pickupDirectory = Path.Combine(Path.GetTempPath(), "LunchOrganizerTests", Guid.NewGuid().ToString("N"));
        var options = TestData.DefaultOptions(pickupDirectory: pickupDirectory);
        var sender = new PickupDirectoryEmailSender(new TestOptionsMonitor<EmailOptions>(options), NullLogger<PickupDirectoryEmailSender>.Instance);
        var message = new EmailMessage("Confirmation - 17.08.2026", "<p>body</p>", "body", From, new[] { "Davy.Jacquet@example.com" });

        try
        {
            await sender.SendAsync(message);

            var files = Directory.GetFiles(pickupDirectory, "*.eml");
            files.Should().ContainSingle();
            Path.GetFileName(files.Single()).Should().MatchRegex(@"^confirmation_2026-08-17_davy\.jacquet_\d{8}-\d{9}\.eml$");
        }
        finally
        {
            if (Directory.Exists(pickupDirectory))
            {
                Directory.Delete(pickupDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SendAsync_ConfirmationSubjectWithEmptyTo_OmitsRecipientSegment()
    {
        var pickupDirectory = Path.Combine(Path.GetTempPath(), "LunchOrganizerTests", Guid.NewGuid().ToString("N"));
        var options = TestData.DefaultOptions(pickupDirectory: pickupDirectory);
        var sender = new PickupDirectoryEmailSender(new TestOptionsMonitor<EmailOptions>(options), NullLogger<PickupDirectoryEmailSender>.Instance);
        var message = new EmailMessage("Confirmation - 17.08.2026", "<p>body</p>", "body", From, Array.Empty<string>());

        try
        {
            await sender.SendAsync(message);

            var files = Directory.GetFiles(pickupDirectory, "*.eml");
            files.Should().ContainSingle();
            Path.GetFileName(files.Single()).Should().MatchRegex(@"^confirmation_2026-08-17_\d{8}-\d{9}\.eml$");
        }
        finally
        {
            if (Directory.Exists(pickupDirectory))
            {
                Directory.Delete(pickupDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SendAsync_UnrecognisedSubject_ProducesLunchSummaryUnknownDateFileName()
    {
        var pickupDirectory = Path.Combine(Path.GetTempPath(), "LunchOrganizerTests", Guid.NewGuid().ToString("N"));
        var options = TestData.DefaultOptions(pickupDirectory: pickupDirectory);
        var sender = new PickupDirectoryEmailSender(new TestOptionsMonitor<EmailOptions>(options), NullLogger<PickupDirectoryEmailSender>.Instance);
        var message = new EmailMessage("Some other subject", "<p>body</p>", "body", From, new[] { "kitchen@cohu.com" });

        try
        {
            await sender.SendAsync(message);

            var files = Directory.GetFiles(pickupDirectory, "*.eml");
            files.Should().ContainSingle();
            Path.GetFileName(files.Single()).Should().MatchRegex(@"^lunch-summary_unknown-date_\d{8}-\d{9}\.eml$");
        }
        finally
        {
            if (Directory.Exists(pickupDirectory))
            {
                Directory.Delete(pickupDirectory, recursive: true);
            }
        }
    }
}
