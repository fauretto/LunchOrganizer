using FluentAssertions;
using LunchOrganizer.Email.Rendering;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Tests.Email;

public class DailySummaryBodyRendererTests
{
    [Fact]
    public void Render_BuildsSubjectFromConfiguredPrefixAndDateFormat()
    {
        var options = TestData.DefaultOptions();
        options.SubjectPrefix = "TEST-PREFIX ";
        options.SubjectDateFormat = "yyyy/MM/dd";

        var summary = new DailySummaryDto(new DateOnly(2026, 8, 17), 1,
            new List<DailySummaryMenuGroupDto> { new(1, 1, "Description", new List<string> { "Employee" }) },
            DateTimeOffset.UtcNow);

        var renderer = new DailySummaryBodyRenderer();
        var rendered = renderer.Render(summary, options);

        rendered.Subject.Should().Be("TEST-PREFIX 2026/08/17");
    }

    [Fact]
    public void Render_WithDefaultCohuConfiguration_ProducesTheDocumentedSubject()
    {
        var options = TestData.DefaultOptions();
        options.SubjectPrefix = "COHU booked lunch for ";
        options.SubjectDateFormat = "dd.MM.yyyy";

        var summary = new DailySummaryDto(new DateOnly(2026, 8, 17), 1,
            new List<DailySummaryMenuGroupDto> { new(1, 1, "Description", new List<string> { "Employee" }) },
            DateTimeOffset.UtcNow);

        var renderer = new DailySummaryBodyRenderer();
        var rendered = renderer.Render(summary, options);

        rendered.Subject.Should().Be("COHU booked lunch for 17.08.2026");
    }

    [Fact]
    public void Render_FrenchBody_ContainsExpectedFrenchWording()
    {
        var options = TestData.DefaultOptions();
        options.Language = "fr";

        var summary = new DailySummaryDto(new DateOnly(2026, 8, 17), 2,
            new List<DailySummaryMenuGroupDto> { new(1, 1, "Description", new List<string> { "Employee One", "Employee Two" }) },
            DateTimeOffset.UtcNow);

        var renderer = new DailySummaryBodyRenderer();
        var rendered = renderer.Render(summary, options);

        // The HtmlBody passes all localized text through System.Net.WebUtility.HtmlEncode, which turns
        // non-ASCII characters (e.g. "é") into numeric character references. So the raw literal
        // "Récapitulatif" only survives in TextBody; for HtmlBody we check the accent-free remainder.
        rendered.TextBody.Should().Contain("Récapitulatif").And.Contain("repas");
        rendered.HtmlBody.Should().Contain("capitulatif").And.Contain("repas");
        rendered.TextBody.Should().NotContain("lunches");
        rendered.HtmlBody.Should().NotContain("lunches");
    }

    [Fact]
    public void Render_EnglishBody_ContainsExpectedEnglishWording()
    {
        var options = TestData.DefaultOptions();
        options.Language = "en";

        var summary = new DailySummaryDto(new DateOnly(2026, 8, 17), 2,
            new List<DailySummaryMenuGroupDto> { new(1, 1, "Description", new List<string> { "Employee One", "Employee Two" }) },
            DateTimeOffset.UtcNow);

        var renderer = new DailySummaryBodyRenderer();
        var rendered = renderer.Render(summary, options);

        rendered.TextBody.Should().Contain("Booked lunches summary").And.Contain("lunches");
        rendered.HtmlBody.Should().Contain("Booked lunches summary").And.Contain("lunches");
        rendered.TextBody.Should().NotContain("Récapitulatif");
        rendered.HtmlBody.Should().NotContain("Récapitulatif");
    }

    [Fact]
    public void Render_TextBody_ListsEmployeesUnderTheirMenuWithDescription()
    {
        var options = TestData.DefaultOptions();

        var summary = new DailySummaryDto(new DateOnly(2026, 8, 17), 2,
            new List<DailySummaryMenuGroupDto> { new(1, 1, "Chicken curry", new List<string> { "Alice Anderson", "Bob Brown" }) },
            DateTimeOffset.UtcNow);

        var renderer = new DailySummaryBodyRenderer();
        var rendered = renderer.Render(summary, options);

        rendered.TextBody.Should().Contain("Chicken curry");
        rendered.TextBody.Should().Contain("Alice Anderson");
        rendered.TextBody.Should().Contain("Bob Brown");
    }

    [Fact]
    public void Render_HtmlBody_HtmlEncodesEmployeeNamesAndDescriptions()
    {
        var options = TestData.DefaultOptions();

        var summary = new DailySummaryDto(new DateOnly(2026, 8, 17), 1,
            new List<DailySummaryMenuGroupDto> { new(1, 1, "Salad <fresh>", new List<string> { "Smith & Sons" }) },
            DateTimeOffset.UtcNow);

        var renderer = new DailySummaryBodyRenderer();
        var rendered = renderer.Render(summary, options);

        rendered.HtmlBody.Should().Contain("Smith &amp; Sons");
        rendered.HtmlBody.Should().Contain("Salad &lt;fresh&gt;");
        rendered.HtmlBody.Should().NotContain("<fresh>");
    }

    [Fact]
    public void Render_HtmlBody_OmitsFlexboxGridAndExternalImages()
    {
        var options = TestData.DefaultOptions();

        var summary = new DailySummaryDto(new DateOnly(2026, 8, 17), 1,
            new List<DailySummaryMenuGroupDto> { new(1, 1, "Description", new List<string> { "Employee" }) },
            DateTimeOffset.UtcNow);

        var renderer = new DailySummaryBodyRenderer();
        var rendered = renderer.Render(summary, options);

        rendered.HtmlBody.Should().NotContain("display:flex");
        rendered.HtmlBody.Should().NotContain("display: flex");
        rendered.HtmlBody.Should().NotContain("display:grid");
        rendered.HtmlBody.Should().NotContain("display: grid");
        rendered.HtmlBody.Should().NotContain("<img");
    }

    [Fact]
    public void Render_MissingDescription_ShowsNoDescriptionPlaceholder()
    {
        var frenchOptions = TestData.DefaultOptions();
        frenchOptions.Language = "fr";

        var frenchSummary = new DailySummaryDto(new DateOnly(2026, 8, 17), 1,
            new List<DailySummaryMenuGroupDto> { new(1, 1, null, new List<string> { "Employee" }) },
            DateTimeOffset.UtcNow);

        var renderer = new DailySummaryBodyRenderer();
        var frenchRendered = renderer.Render(frenchSummary, frenchOptions);

        frenchRendered.TextBody.Should().Contain("(pas de description)");
        frenchRendered.HtmlBody.Should().Contain("(pas de description)");
        frenchRendered.TextBody.Should().NotContain("null");
        frenchRendered.HtmlBody.Should().NotContain("null");

        var englishOptions = TestData.DefaultOptions();
        englishOptions.Language = "en";

        var englishSummary = new DailySummaryDto(new DateOnly(2026, 8, 17), 1,
            new List<DailySummaryMenuGroupDto> { new(1, 1, null, new List<string> { "Employee" }) },
            DateTimeOffset.UtcNow);

        var englishRendered = renderer.Render(englishSummary, englishOptions);

        englishRendered.TextBody.Should().Contain("(no description)");
        englishRendered.HtmlBody.Should().Contain("(no description)");
    }
}
