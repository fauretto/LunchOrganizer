using System.Globalization;
using System.Text.RegularExpressions;
using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;
using LunchOrganizer.Services.Import;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Services.Services;

/// <summary>
/// Parses, validates and (on request) commits a monthly menu .docx document, in line with the
/// preview-then-confirm flow described on <see cref="IMenuImportService"/>.
/// </summary>
public sealed class MenuImportService(
    IMenuDocumentParser parser,
    IMenuRepository menuRepo,
    IBookingChangeNotifier notifier,
    IOptionsMonitor<AppOptions> appOptions,
    ILogger<MenuImportService> logger) : IMenuImportService
{
    /// <summary>
    /// Parses <paramref name="docx"/> and runs every validation check shared by preview and
    /// import. Unlike <c>MenuService.AddMenuAsync</c>, there is deliberately no past-date check
    /// here (plan decision D4): a monthly file is a bulk data load routinely imported mid-month,
    /// and the existing UI already forbids editing or deleting past menus.
    /// </summary>
    private OperationResult<ParsedMenuDocument> ParseAndValidate(Stream docx, int year)
    {
        // The Web layer also checks IBrowserFile.Size before ever opening the stream; this is
        // defence-in-depth for any other caller that hands us the stream directly.
        if (docx.CanSeek && docx.Length > BusinessRules.MaxMenuImportBytes)
        {
            return OperationResult<ParsedMenuDocument>.Fail(
                "The uploaded file exceeds the maximum allowed size.",
                ErrorCodes.MenuImportFileTooLarge,
                new object?[] { BusinessRules.MaxMenuImportMegabytes });
        }

        ParsedMenuDocument parsed;
        try
        {
            parsed = parser.Parse(docx, year);
        }
        catch (MenuDocumentParseException ex)
        {
            return OperationResult<ParsedMenuDocument>.Fail("Parsing the menu document failed.", ex.ErrorCode, ex.MessageArgs);
        }
        catch (RegexMatchTimeoutException ex)
        {
            logger.LogWarning(ex, "Menu document parsing timed out.");
            return OperationResult<ParsedMenuDocument>.Fail("Parsing the menu document timed out.", ErrorCodes.MenuImportInvalidDocument);
        }
        catch (Exception ex)
        {
            // Ensures a malformed document can never surface as an unhandled exception inside a Blazor circuit.
            logger.LogError(ex, "Unexpected error while parsing a menu import document.");
            return OperationResult<ParsedMenuDocument>.Fail("Unexpected error while parsing the menu document.", ErrorCodes.MenuImportInvalidDocument);
        }

        var totalMenus = parsed.WorkingDays.Sum(d => d.Menus.Count);
        if (totalMenus == 0)
        {
            // A document consisting solely of Saturday/Sunday rows, or one whose description
            // cells are all blank, lands here.
            return OperationResult<ParsedMenuDocument>.Fail("The document contained no importable menu data.", ErrorCodes.MenuImportNoDataFound);
        }

        // Catches a week pasted twice, and a header row listing the same MENU number twice.
        // Kept as a plain loop/HashSet rather than LINQ so the "first offending date wins" logic stays readable.
        var seenDates = new HashSet<DateOnly>();
        var seenPairs = new HashSet<(DateOnly Date, int MenuNumber)>();
        foreach (var day in parsed.WorkingDays)
        {
            if (!seenDates.Add(day.Date))
            {
                return DuplicateInDocumentFailure(day.Date);
            }

            foreach (var entry in day.Menus)
            {
                if (!seenPairs.Add((day.Date, entry.MenuNumber)))
                {
                    return DuplicateInDocumentFailure(day.Date);
                }
            }
        }

        // Identical fallback rule to MenuService.AddMenuAsync, deliberately kept in sync.
        var max = appOptions.CurrentValue.MaxMenusPerDay <= 0
            ? BusinessRules.DefaultMaxMenusPerDay
            : appOptions.CurrentValue.MaxMenusPerDay;
        foreach (var day in parsed.WorkingDays)
        {
            if (day.Menus.Count > max)
            {
                return OperationResult<ParsedMenuDocument>.Fail(
                    $"A day in the document has more than {max} menus.",
                    ErrorCodes.MaxMenusPerDayReached,
                    new object?[] { max });
            }
        }

        return OperationResult<ParsedMenuDocument>.Ok(parsed);
    }

    private static OperationResult<ParsedMenuDocument> DuplicateInDocumentFailure(DateOnly date) =>
        OperationResult<ParsedMenuDocument>.Fail(
            "The document contains the same date/menu combination more than once.",
            ErrorCodes.MenuImportDuplicateInDocument,
            new object?[] { date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) });

    /// <summary>
    /// Parses and validates <paramref name="docx"/> and maps the result to a preview the UI can
    /// show the admin before they confirm the import. This does no I/O beyond reading the
    /// in-memory stream, so the body is synchronous internally; it only returns a <see cref="Task"/>
    /// to match <see cref="IMenuImportService"/>.
    /// </summary>
    public Task<OperationResult<MenuImportPreviewDto>> PreviewAsync(Stream docx, int year, CancellationToken ct = default)
    {
        try
        {
            var parseResult = ParseAndValidate(docx, year);
            if (!parseResult.IsSuccess)
            {
                return Task.FromResult(OperationResult<MenuImportPreviewDto>.Fail(parseResult.Message!, parseResult.ErrorCode, parseResult.MessageArgs));
            }

            var parsed = parseResult.Value!;

            // At least one such day exists: ParseAndValidate already rejected a zero-total-menus document.
            var daysWithMenus = parsed.WorkingDays.Where(d => d.Menus.Count >= 1).ToList();

            var dto = new MenuImportPreviewDto(
                parsed.WeeksParsed,
                daysWithMenus.Count,
                parsed.WorkingDays.Sum(d => d.Menus.Count),
                daysWithMenus.Min(d => d.Date),
                daysWithMenus.Max(d => d.Date),
                parsed.WeekdayMismatches
                    .Take(3)
                    .Select(m =>
                        $"{m.DocumentDayLabel} {m.Date.ToString("dd.MM", CultureInfo.InvariantCulture)} → {m.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} is a {m.ActualDayOfWeek}")
                    .ToList(),
                parsed.SkippedNonWorkingDays.Count);

            return Task.FromResult(OperationResult<MenuImportPreviewDto>.Ok(dto));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while previewing a menu import document.");
            return Task.FromResult(OperationResult<MenuImportPreviewDto>.Fail("Unexpected error while previewing the menu import.", ErrorCodes.MenuImportFailed));
        }
    }

    /// <summary>
    /// Re-runs the same parse-and-validate pipeline as <see cref="PreviewAsync"/> and, on success,
    /// commits every parsed menu as one all-or-nothing batch via <see cref="IMenuRepository.ImportAsync"/>,
    /// then notifies subscribers of every affected date.
    /// </summary>
    public async Task<OperationResult<MenuImportResultDto>> ImportAsync(Stream docx, int year, CancellationToken ct = default)
    {
        var parseResult = ParseAndValidate(docx, year);
        if (!parseResult.IsSuccess)
        {
            return OperationResult<MenuImportResultDto>.Fail(parseResult.Message!, parseResult.ErrorCode, parseResult.MessageArgs);
        }

        var parsed = parseResult.Value!;

        // Do not set Id, CreatedAtUtc, UpdatedAtUtc, or Version — the store defaults and the
        // DbContext's version-maintenance logic own those, exactly as the single-menu AddAsync path does.
        var entities = parsed.WorkingDays
            .SelectMany(day => day.Menus.Select(entry => new Menu
            {
                MenuDate = day.Date,
                MenuNumber = entry.MenuNumber,
                Description = entry.Description,
                Price = entry.Price
            }))
            .OrderBy(m => m.MenuDate)
            .ThenBy(m => m.MenuNumber)
            .ToList();

        try
        {
            await menuRepo.ImportAsync(entities, ct);
        }
        catch (MenuImportConflictException ex)
        {
            var conflictCount = ex.ConflictingDates.Count;
            var shownDates = ex.ConflictingDates.Take(10).Select(d => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
            var datesText = string.Join(", ", shownDates) + (conflictCount > 10 ? ", …" : string.Empty);

            // Expected admin-facing outcome (e.g. re-importing an overlapping file), not a defect.
            logger.LogInformation("Menu import aborted: {ConflictCount} date(s) already have menus: {ConflictingDates}.", conflictCount, datesText);

            return OperationResult<MenuImportResultDto>.Fail(
                "The database already contains menus for one or more of the imported dates.",
                ErrorCodes.MenuImportDuplicateMenusExist,
                new object?[] { conflictCount, datesText });
        }
        catch (OperationCanceledException)
        {
            throw; // never swallow cancellation
        }
        catch (Exception ex)
        {
            // The repository already rolled back on failure, so no partial data exists.
            logger.LogError(ex, "Unexpected error while importing menus for {FirstDate}..{LastDate}.", entities.Min(m => m.MenuDate), entities.Max(m => m.MenuDate));
            return OperationResult<MenuImportResultDto>.Fail("Unexpected error while importing the menu document.", ErrorCodes.MenuImportFailed);
        }

        // A subscriber throwing must not turn an already-committed import into a reported failure.
        try
        {
            foreach (var date in entities.Select(m => m.MenuDate).Distinct())
            {
                notifier.NotifyChanged(date);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A subscriber threw while handling menu-import change notifications; the import itself already committed successfully.");
        }

        var daysImported = entities.Select(m => m.MenuDate).Distinct().Count();
        var menusImported = entities.Count;
        var firstDate = entities.Min(m => m.MenuDate);
        var lastDate = entities.Max(m => m.MenuDate);
        var menusWithPriceCount = parsed.WorkingDays.Sum(d => d.Menus.Count(m => m.Price.HasValue));

        logger.LogInformation(
            "Imported {MenusImported} menu(s) across {DaysImported} day(s), {FirstDate}..{LastDate}, {MenusWithPrice} with an explicit price.",
            menusImported, daysImported, firstDate, lastDate, menusWithPriceCount);

        return OperationResult<MenuImportResultDto>.Ok(new MenuImportResultDto(
            parsed.WeeksParsed, daysImported, menusImported, firstDate, lastDate, parsed.SkippedNonWorkingDays.Count));
    }
}
