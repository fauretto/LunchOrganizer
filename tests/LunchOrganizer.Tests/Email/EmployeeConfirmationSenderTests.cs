using FluentAssertions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Email;
using LunchOrganizer.Email.Abstractions;
using LunchOrganizer.Email.Rendering;
using LunchOrganizer.Email.Sending;
using LunchOrganizer.Email.Services;
using LunchOrganizer.Fakes.Services;
using LunchOrganizer.Services.Dtos;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunchOrganizer.Tests.Email;

/// <summary>
/// Covers the requirement's core rules (implementation plan §6, cases 1-7): who gets a confirmation,
/// who is silently skipped, and that one bad employee never stops the others.
/// </summary>
public class EmployeeConfirmationSenderTests
{
    private static readonly DateOnly Date = new(2026, 8, 17);

    private static EmployeeConfirmationSender CreateSender(
        EmailOptions options,
        out FakeEmailSender fakeSender,
        out PickupDirectoryEmailSender previewSender)
    {
        var appOptionsMonitor = new TestOptionsMonitor<AppOptions>(new AppOptions { Currency = "CHF" });
        var renderer = new EmployeeConfirmationBodyRenderer(appOptionsMonitor);
        fakeSender = new FakeEmailSender();
        previewSender = new PickupDirectoryEmailSender(new TestOptionsMonitor<EmailOptions>(options), NullLogger<PickupDirectoryEmailSender>.Instance);

        return new EmployeeConfirmationSender(
            renderer,
            fakeSender,
            previewSender,
            new TestOptionsMonitor<EmailOptions>(options),
            NullLogger<EmployeeConfirmationSender>.Instance);
    }

    private static DailySummaryDto SummaryWith(params EmployeeBookingConfirmationDto[] bookings) =>
        new(Date, bookings.Length, new List<DailySummaryMenuGroupDto>(), DateTimeOffset.UtcNow, bookings);

    private static EmployeeBookingConfirmationDto BookingFor(int employeeId, string name, string? email, int menuNumber = 1, string? description = "Menu", decimal price = 12.50m) =>
        new(employeeId, name, email, menuNumber, description, price);

    [Fact]
    public async Task SendAllAsync_EmployeeWithEmail_ReceivesExactlyOneConfirmation()
    {
        var options = TestData.DefaultOptions();
        var sender = CreateSender(options, out var fakeSender, out _);
        var summary = SummaryWith(BookingFor(1, "Alice Martin", "alice@example.com"));

        var outcome = await sender.SendAllAsync(summary, dryRun: false);

        outcome.Should().Be(new ConfirmationSendOutcome(Sent: 1, SkippedNoEmail: 0, Failed: 0));
        fakeSender.SentMessages.Should().ContainSingle();
        fakeSender.SentMessages.Single().To.Should().ContainSingle().Which.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task SendAllAsync_EmployeeWithNullEmail_ReceivesNoneAndIsCountedAsSkipped()
    {
        var options = TestData.DefaultOptions();
        var sender = CreateSender(options, out var fakeSender, out _);
        var summary = SummaryWith(BookingFor(1, "Bob Brown", email: null));

        var outcome = await sender.SendAllAsync(summary, dryRun: false);

        outcome.Should().Be(new ConfirmationSendOutcome(Sent: 0, SkippedNoEmail: 1, Failed: 0));
        fakeSender.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAllAsync_EmployeeWithWhitespaceEmail_IsSkipped()
    {
        var options = TestData.DefaultOptions();
        var sender = CreateSender(options, out var fakeSender, out _);
        var summary = SummaryWith(BookingFor(1, "Carla Dupont", email: "   "));

        var outcome = await sender.SendAllAsync(summary, dryRun: false);

        outcome.Should().Be(new ConfirmationSendOutcome(Sent: 0, SkippedNoEmail: 1, Failed: 0));
        fakeSender.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAllAsync_EmployeeWithMalformedEmail_IsSkippedNotAttempted()
    {
        var options = TestData.DefaultOptions();
        var sender = CreateSender(options, out var fakeSender, out _);
        // "alice@" has no domain and is one of the few forms MimeKit's lenient MailboxAddress parser
        // reliably rejects (verified: bare tokens without '@' are accepted as local-part-only
        // addresses by that parser, so this test deliberately uses a form it does reject).
        var summary = SummaryWith(BookingFor(1, "Dana Fischer", email: "alice@"));

        var outcome = await sender.SendAllAsync(summary, dryRun: false);

        outcome.Should().Be(new ConfirmationSendOutcome(Sent: 0, SkippedNoEmail: 1, Failed: 0));
        fakeSender.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAllAsync_MixOfValidAndInvalidAddresses_SkipDoesNotShortCircuitTheLoop()
    {
        var options = TestData.DefaultOptions();
        var sender = CreateSender(options, out var fakeSender, out _);
        var summary = SummaryWith(
            BookingFor(1, "Alice Martin", "alice@example.com"),
            BookingFor(2, "Bob Brown", email: null),
            BookingFor(3, "Carla Dupont", email: "   "),
            BookingFor(4, "Dana Fischer", email: "alice@"),
            BookingFor(5, "Eve Nguyen", "eve@example.com"));

        var outcome = await sender.SendAllAsync(summary, dryRun: false);

        outcome.Should().Be(new ConfirmationSendOutcome(Sent: 2, SkippedNoEmail: 3, Failed: 0));
        fakeSender.SentMessages.Select(m => m.To.Single()).Should().BeEquivalentTo(new[] { "alice@example.com", "eve@example.com" });
    }

    [Fact]
    public async Task SendAllAsync_OneEmployeeSendFails_LaterEmployeesAreStillSent()
    {
        var options = TestData.DefaultOptions();
        var appOptionsMonitor = new TestOptionsMonitor<AppOptions>(new AppOptions { Currency = "CHF" });
        var renderer = new EmployeeConfirmationBodyRenderer(appOptionsMonitor);
        var failingSender = new SelectivelyFailingEmailSender(failFor: "bob@example.com");
        var previewSender = new PickupDirectoryEmailSender(new TestOptionsMonitor<EmailOptions>(options), NullLogger<PickupDirectoryEmailSender>.Instance);

        var sender = new EmployeeConfirmationSender(
            renderer,
            failingSender,
            previewSender,
            new TestOptionsMonitor<EmailOptions>(options),
            NullLogger<EmployeeConfirmationSender>.Instance);

        var summary = SummaryWith(
            BookingFor(1, "Alice Martin", "alice@example.com"),
            BookingFor(2, "Bob Brown", "bob@example.com"),
            BookingFor(3, "Carla Dupont", "carla@example.com"));

        var outcome = await sender.SendAllAsync(summary, dryRun: false);

        outcome.Should().Be(new ConfirmationSendOutcome(Sent: 2, SkippedNoEmail: 0, Failed: 1));
        failingSender.Attempted.Should().BeEquivalentTo(new[] { "alice@example.com", "bob@example.com", "carla@example.com" });
    }

    [Fact]
    public async Task SendAllAsync_DryRun_RoutesToPickupDirectoryAndSendsNothingForReal()
    {
        var pickupDirectory = Path.Combine(Path.GetTempPath(), "LunchOrganizerTests", Guid.NewGuid().ToString("N"));
        var options = TestData.DefaultOptions(pickupDirectory: pickupDirectory);
        var sender = CreateSender(options, out var fakeSender, out _);
        var summary = SummaryWith(
            BookingFor(1, "Alice Martin", "alice@example.com"),
            BookingFor(2, "Eve Nguyen", "eve@example.com"));

        try
        {
            var outcome = await sender.SendAllAsync(summary, dryRun: true);

            outcome.Should().Be(new ConfirmationSendOutcome(Sent: 2, SkippedNoEmail: 0, Failed: 0));
            fakeSender.SentMessages.Should().BeEmpty();
            Directory.Exists(pickupDirectory).Should().BeTrue();
            Directory.GetFiles(pickupDirectory, "*.eml").Should().HaveCount(2);
        }
        finally
        {
            if (Directory.Exists(pickupDirectory))
            {
                Directory.Delete(pickupDirectory, recursive: true);
            }
        }
    }

    /// <summary>Fails <see cref="SendAsync"/> for one specific recipient address and succeeds for every other.</summary>
    private sealed class SelectivelyFailingEmailSender(string failFor) : IEmailSender
    {
        public List<string> Attempted { get; } = new();

        public Task<OperationResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            var recipient = message.To.Single();
            Attempted.Add(recipient);

            return Task.FromResult(string.Equals(recipient, failFor, StringComparison.OrdinalIgnoreCase)
                ? OperationResult.Fail("Simulated mailbox failure.")
                : OperationResult.Ok("Sent."));
        }
    }
}
