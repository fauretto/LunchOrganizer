using FluentAssertions;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Email.Rendering;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Tests.Email;

/// <summary>
/// Covers implementation plan §6 cases 8-11: the confirmation carries the employee's own booking
/// details, HTML-encodes user-entered text, supports both languages, and — the privacy property the
/// feature exists to guarantee — never leaks another employee's name.
/// </summary>
public class EmployeeConfirmationBodyRendererTests
{
    private static readonly DateOnly Date = new(2026, 8, 17);

    private static EmployeeConfirmationBodyRenderer CreateRenderer(string currency = "CHF") =>
        new(new TestOptionsMonitor<AppOptions>(new AppOptions { Currency = currency }));

    private static DailySummaryDto SummaryWith(params EmployeeBookingConfirmationDto[] bookings) =>
        new(Date, bookings.Length, new List<DailySummaryMenuGroupDto>(), DateTimeOffset.UtcNow, bookings);

    [Fact]
    public void Render_BodyContainsTheEmployeesOwnNameMenuDescriptionAndPrice()
    {
        var options = TestData.DefaultOptions();
        var booking = new EmployeeBookingConfirmationDto(1, "Alice Martin", "alice@example.com", 2, "Chicken curry", 13.50m);
        var summary = SummaryWith(booking);

        var renderer = CreateRenderer();
        var rendered = renderer.Render(summary, booking, options);

        rendered.TextBody.Should().Contain("Alice Martin");
        rendered.TextBody.Should().Contain("Menu 2");
        rendered.TextBody.Should().Contain("Chicken curry");
        rendered.TextBody.Should().Contain("13.50");
        rendered.TextBody.Should().Contain("CHF");

        rendered.HtmlBody.Should().Contain("Alice Martin");
        rendered.HtmlBody.Should().Contain("Menu 2");
        rendered.HtmlBody.Should().Contain("Chicken curry");
        rendered.HtmlBody.Should().Contain("13.50");
        rendered.HtmlBody.Should().Contain("CHF");
    }

    [Fact]
    public void Render_DoesNotContainAnyOtherEmployeesName()
    {
        // The confirmation is built from a single EmployeeBookingConfirmationDto — never the full
        // summary's list — so this also guards against a future regression that accidentally wires
        // the renderer to summary.EmployeeBookings instead of the one booking it was given.
        var options = TestData.DefaultOptions();
        var ownBooking = new EmployeeBookingConfirmationDto(1, "Alice Martin", "alice@example.com", 1, "Chicken curry", 12.50m);
        var otherBooking = new EmployeeBookingConfirmationDto(2, "Bob Brown", "bob@example.com", 1, "Chicken curry", 12.50m);
        var summary = SummaryWith(ownBooking, otherBooking);

        var renderer = CreateRenderer();
        var rendered = renderer.Render(summary, ownBooking, options);

        rendered.TextBody.Should().Contain("Alice Martin");
        rendered.TextBody.Should().NotContain("Bob Brown");
        rendered.HtmlBody.Should().Contain("Alice Martin");
        rendered.HtmlBody.Should().NotContain("Bob Brown");
    }

    [Fact]
    public void Render_FrenchAndEnglish_ProduceTheirOwnWording()
    {
        var booking = new EmployeeBookingConfirmationDto(1, "Alice Martin", "alice@example.com", 1, "Description", 12.50m);
        var summary = SummaryWith(booking);
        var renderer = CreateRenderer();

        var frenchOptions = TestData.DefaultOptions();
        frenchOptions.Language = "fr";
        var frenchRendered = renderer.Render(summary, booking, frenchOptions);

        frenchRendered.TextBody.Should().Contain("Bonjour Alice Martin,").And.Contain("Bon appétit").And.Contain("Prix :");
        frenchRendered.TextBody.Should().NotContain("Hello").And.NotContain("Enjoy your meal");

        var englishOptions = TestData.DefaultOptions();
        englishOptions.Language = "en";
        var englishRendered = renderer.Render(summary, booking, englishOptions);

        englishRendered.TextBody.Should().Contain("Hello Alice Martin,").And.Contain("Enjoy your meal!").And.Contain("Price:");
        englishRendered.TextBody.Should().NotContain("Bonjour").And.NotContain("Bon appétit");
    }

    [Fact]
    public void Render_HtmlBody_EncodesAnEmployeeNameContainingHtml()
    {
        var options = TestData.DefaultOptions();
        var booking = new EmployeeBookingConfirmationDto(1, "<script>alert(1)</script>", "alice@example.com", 1, "Description", 12.50m);
        var summary = SummaryWith(booking);

        var renderer = CreateRenderer();
        var rendered = renderer.Render(summary, booking, options);

        rendered.HtmlBody.Should().NotContain("<script>alert(1)</script>");
        rendered.HtmlBody.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void Render_BuildsSubjectFromConfirmationPrefixAndSubjectDateFormat()
    {
        var options = TestData.DefaultOptions();
        options.ConfirmationSubjectPrefix = "Confirmation - ";
        options.SubjectDateFormat = "dd.MM.yyyy";
        var booking = new EmployeeBookingConfirmationDto(1, "Alice Martin", "alice@example.com", 1, "Description", 12.50m);
        var summary = SummaryWith(booking);

        var renderer = CreateRenderer();
        var rendered = renderer.Render(summary, booking, options);

        rendered.Subject.Should().Be("Confirmation - 17.08.2026");
    }
}
