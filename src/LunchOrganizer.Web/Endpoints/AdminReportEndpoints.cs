using System.Globalization;
using System.Text;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;
using LunchOrganizer.Web.Resources;
using Microsoft.Extensions.Localization;

namespace LunchOrganizer.Web.Endpoints;

/// <summary>
/// Plain ASP.NET Core minimal-API endpoint (not a Blazor component) that streams the admin report
/// as a CSV file download. Lives outside the Blazor render-mode boundary so a plain anchor
/// navigation (no SignalR circuit required) triggers the browser's native file download via
/// Results.File's Content-Disposition header.
/// </summary>
public static class AdminReportEndpoints
{
    public static IEndpointRouteBuilder MapAdminReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/report/export.csv", HandleExportAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> HandleExportAsync(
        int? employeeId, DateOnly from, DateOnly to, IReportService reportService, IStringLocalizer<Admin> loc, CancellationToken ct)
    {
        var report = await reportService.GetReportAsync(employeeId, from, to, ct);
        var csv = BuildCsv(report, loc);
        // UTF-8 BOM so Excel opens accented French text correctly.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return Results.File(bytes, "text/csv", $"lunch-report-{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.csv");
    }

    private static string BuildCsv(ReportDto report, IStringLocalizer<Admin> loc)
    {
        var sb = new StringBuilder();

        // Header row: reuse the same resx keys as the on-screen table. The request's
        // CultureInfo.CurrentCulture/CurrentUICulture is already resolved by
        // app.UseRequestLocalization() earlier in the pipeline, so this localizer call yields a
        // French header row for a French admin's browser automatically.
        sb.Append(QuoteCsvField(loc["ReportColumnEmployee"])).Append(',')
          .Append(QuoteCsvField(loc["ReportColumnDate"])).Append(',')
          .Append(QuoteCsvField(loc["ReportColumnDay"])).Append(',')
          .Append(QuoteCsvField(loc["ReportColumnMenu"])).Append(',')
          .Append(QuoteCsvField(loc["ReportColumnDescription"])).Append(',')
          .Append(QuoteCsvField(loc["ReportColumnPrice"])).Append("\r\n");

        foreach (var line in report.Lines)
        {
            // Date: invariant "yyyy-MM-dd" — a machine-parseable data export, not a display surface.
            var dateInvariant = line.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // Day name: a language concern, not a machine-parseable value, so this one field IS
            // culture-formatted even though date/price are not.
            var dayLocalized = line.Date.ToString("dddd", CultureInfo.CurrentCulture);

            // Price: plain invariant-culture decimal with a dot, so a spreadsheet can sum it —
            // never PriceFormatter's "CHF 12.50" string here.
            var priceInvariant = line.Price.ToString("0.00", CultureInfo.InvariantCulture);

            sb.Append(QuoteCsvField(line.EmployeeName)).Append(',')
              .Append(QuoteCsvField(dateInvariant)).Append(',')
              .Append(QuoteCsvField(dayLocalized)).Append(',')
              .Append(QuoteCsvField(line.MenuNumber.ToString(CultureInfo.InvariantCulture))).Append(',')
              .Append(QuoteCsvField(line.MenuDescription)).Append(',')
              .Append(QuoteCsvField(priceInvariant)).Append("\r\n");
        }

        // Blank line, then a total row.
        sb.Append("\r\n");
        sb.Append(QuoteCsvField(loc["ReportPeriodTotalLabel"])).Append(",,,,,")
          .Append(QuoteCsvField(report.Total.ToString("0.00", CultureInfo.InvariantCulture))).Append("\r\n");

        return sb.ToString();
    }

    /// <summary>Quotes a CSV field per RFC 4180: wraps in double-quotes and doubles internal double-quotes whenever the field contains a comma, double-quote, or newline.</summary>
    private static string QuoteCsvField(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        var needsQuoting = field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r');
        if (!needsQuoting)
        {
            return field;
        }

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }
}
