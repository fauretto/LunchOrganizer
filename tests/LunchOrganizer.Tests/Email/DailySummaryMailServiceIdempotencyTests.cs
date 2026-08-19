using FluentAssertions;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Enums;
using LunchOrganizer.Email;
using LunchOrganizer.Email.Abstractions;
using LunchOrganizer.Email.InMemory;
using LunchOrganizer.Email.Rendering;
using LunchOrganizer.Email.Sending;
using LunchOrganizer.Email.Services;
using LunchOrganizer.Fakes.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunchOrganizer.Tests.Email;

public class DailySummaryMailServiceIdempotencyTests
{
    private static readonly DateOnly WorkingDate = new(2026, 8, 17); // Monday
    private static readonly DateOnly NonWorkingDate = new(2026, 8, 15); // Saturday

    private static DailySummaryMailService CreateService(
        out FakeEmailSender fakeSender,
        out InMemoryEmailLogRepository logRepo,
        EmailOptions options,
        FakeClock clock,
        InMemoryBookingRepository bookingRepo,
        IEmployeeConfirmationSender? confirmationSender = null)
    {
        var optionsMonitor = new TestOptionsMonitor<EmailOptions>(options);
        var builder = new DailySummaryBuilder(bookingRepo, optionsMonitor, clock);
        var renderer = new DailySummaryBodyRenderer();
        fakeSender = new FakeEmailSender();
        var previewSender = new PickupDirectoryEmailSender(new TestOptionsMonitor<EmailOptions>(options), NullLogger<PickupDirectoryEmailSender>.Instance);
        logRepo = new InMemoryEmailLogRepository(clock);

        return new DailySummaryMailService(
            builder,
            renderer,
            fakeSender,
            previewSender,
            logRepo,
            confirmationSender ?? new FakeEmployeeConfirmationSender(),
            optionsMonitor,
            clock,
            NullLogger<DailySummaryMailService>.Instance);
    }

    private static void SeedBookings(InMemoryBookingRepository repo, DateOnly date, int count)
    {
        var bookings = Enumerable.Range(1, count)
            .Select(i => TestData.Booking(i, $"Employee {i}", date, menuId: 1, menuNumber: 1, menuDescription: "Menu"))
            .ToArray();
        repo.Seed(bookings);
    }

    [Fact]
    public async Task RunAsync_ConcurrentCallsForSameDate_ProduceExactlyOneSend()
    {
        var options = TestData.DefaultOptions();
        var clock = new FakeClock();
        var bookingRepo = new InMemoryBookingRepository();
        SeedBookings(bookingRepo, WorkingDate, 3);

        var service = CreateService(out var fakeSender, out var logRepo, options, clock, bookingRepo);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.RunAsync(WorkingDate, dryRun: false)));

        results.Count(r => r.Status == EmailSendStatus.Sent && r.SuggestedExitCode == 0).Should().Be(1);
        results.Count(r => r.SuggestedExitCode == 3).Should().Be(7);
        fakeSender.SentMessages.Count.Should().Be(1);

        var logEntry = await logRepo.GetAsync(WorkingDate);
        logEntry.Should().NotBeNull();
        logEntry!.Status.Should().Be(EmailSendStatus.Sent);
    }

    [Fact]
    public async Task RunAsync_SecondSequentialCallForSameDate_ReturnsAlreadyHandled()
    {
        var options = TestData.DefaultOptions();
        var clock = new FakeClock();
        var bookingRepo = new InMemoryBookingRepository();
        SeedBookings(bookingRepo, WorkingDate, 2);

        var service = CreateService(out var fakeSender, out var logRepo, options, clock, bookingRepo);

        var first = await service.RunAsync(WorkingDate, dryRun: false);
        var second = await service.RunAsync(WorkingDate, dryRun: false);

        first.SuggestedExitCode.Should().Be(0);
        second.SuggestedExitCode.Should().Be(3);
        fakeSender.SentMessages.Count.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_DryRun_NeverReservesOrCompletesEmailLog()
    {
        var options = TestData.DefaultOptions();
        var clock = new FakeClock();
        var bookingRepo = new InMemoryBookingRepository();
        SeedBookings(bookingRepo, WorkingDate, 3);

        var service = CreateService(out var fakeSender, out var logRepo, options, clock, bookingRepo);

        (await logRepo.GetAsync(WorkingDate)).Should().BeNull();

        var result = await service.RunAsync(WorkingDate, dryRun: true);

        (await logRepo.GetAsync(WorkingDate)).Should().BeNull();
        fakeSender.SentMessages.Should().BeEmpty();
        result.SuggestedExitCode.Should().Be(0);
        result.BookingCount.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_NoBookingsForDate_RecordsSkippedAndReturnsExitCode2()
    {
        var options = TestData.DefaultOptions();
        var clock = new FakeClock();
        var bookingRepo = new InMemoryBookingRepository();

        var service = CreateService(out _, out var logRepo, options, clock, bookingRepo);

        var result = await service.RunAsync(WorkingDate, dryRun: false);

        result.SuggestedExitCode.Should().Be(2);
        result.Status.Should().Be(EmailSendStatus.Skipped);

        var logEntry = await logRepo.GetAsync(WorkingDate);
        logEntry.Should().NotBeNull();
        logEntry!.Status.Should().Be(EmailSendStatus.Skipped);
    }

    [Fact]
    public async Task RunAsync_NonWorkingDay_SkipsWithoutReservingEmailLog()
    {
        var options = TestData.DefaultOptions();
        var clock = new FakeClock();
        var bookingRepo = new InMemoryBookingRepository();

        var service = CreateService(out _, out var logRepo, options, clock, bookingRepo);

        var result = await service.RunAsync(NonWorkingDate, dryRun: false);

        result.SuggestedExitCode.Should().Be(2);
        (await logRepo.GetAsync(NonWorkingDate)).Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_NoRecipientsConfigured_ReturnsFailedAndRecordsFailure()
    {
        var options = TestData.DefaultOptions();
        options.Recipients = new List<string>();
        var clock = new FakeClock();
        var bookingRepo = new InMemoryBookingRepository();
        SeedBookings(bookingRepo, WorkingDate, 2);

        var service = CreateService(out _, out var logRepo, options, clock, bookingRepo);

        var result = await service.RunAsync(WorkingDate, dryRun: false);

        result.SuggestedExitCode.Should().Be(1);
        result.Status.Should().Be(EmailSendStatus.Failed);

        var logEntry = await logRepo.GetAsync(WorkingDate);
        logEntry.Should().NotBeNull();
        logEntry!.Status.Should().Be(EmailSendStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_SuccessfulSend_RecordsSentWithRecipientsAndBookingCount()
    {
        var options = TestData.DefaultOptions();
        var clock = new FakeClock();
        var bookingRepo = new InMemoryBookingRepository();
        SeedBookings(bookingRepo, WorkingDate, 4);

        var service = CreateService(out var fakeSender, out var logRepo, options, clock, bookingRepo);

        var result = await service.RunAsync(WorkingDate, dryRun: false);

        result.SuggestedExitCode.Should().Be(0);
        fakeSender.SentMessages.Count.Should().Be(1);

        var logEntry = await logRepo.GetAsync(WorkingDate);
        logEntry.Should().NotBeNull();
        logEntry!.Status.Should().Be(EmailSendStatus.Sent);
        logEntry.BookingCount.Should().Be(4);
        logEntry.Recipients.Should().Contain("kitchen@cohu.com");
    }

    [Fact]
    public async Task RunAsync_ConfirmationSenderThrows_StillReportsSentWithExitCodeZero()
    {
        // D2: a confirmation-sending failure must never change the run's own outcome. The summary
        // send itself succeeds; only the human-readable Message gains a note that confirmations
        // could not be sent — Status, BookingCount and SuggestedExitCode are all decided by the
        // summary alone.
        var options = TestData.DefaultOptions();
        var clock = new FakeClock();
        var bookingRepo = new InMemoryBookingRepository();
        SeedBookings(bookingRepo, WorkingDate, 3);
        var throwingConfirmationSender = new FakeEmployeeConfirmationSender(throws: new InvalidOperationException("boom"));

        var service = CreateService(out var fakeSender, out var logRepo, options, clock, bookingRepo, throwingConfirmationSender);

        var result = await service.RunAsync(WorkingDate, dryRun: false);

        result.Status.Should().Be(EmailSendStatus.Sent);
        result.SuggestedExitCode.Should().Be(0);
        result.BookingCount.Should().Be(3);
        fakeSender.SentMessages.Count.Should().Be(1);

        var logEntry = await logRepo.GetAsync(WorkingDate);
        logEntry!.Status.Should().Be(EmailSendStatus.Sent);
    }
}
