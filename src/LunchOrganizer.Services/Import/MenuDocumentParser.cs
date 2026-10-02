using LunchOrganizer.Domain.Common;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LunchOrganizer.Services.Import;

/// <summary>
/// Parses the monthly-menu Word document (.docx) produced from the site's menu template into a
/// <see cref="ParsedMenuDocument"/>, using nothing but <see cref="System.IO.Compression.ZipArchive"/>
/// and <see cref="System.Xml.Linq"/> (plan decision D1: zero third-party dependencies — no
/// DocumentFormat.OpenXml or any other NuGet package). A .docx file is a zip archive; the visible
/// content lives in the <c>word/document.xml</c> entry as WordprocessingML.
///
/// <para>
/// Two document shapes are supported side by side, both observed in real sample files:
/// </para>
/// <para>
/// <b>Old (weekly) format:</b> the body is a flat sequence of top-level <c>w:tbl</c> elements, one
/// per working week, each with a header row (<c>Jour | MENU 1 | MENU 2 | MENU 3...</c>) followed by
/// day rows. A day cell carries no year (e.g. <c>"LUNDI 17.12"</c>) and a description cell holds a
/// single paragraph, possibly with <c>w:br</c> line breaks for a multi-line description.
/// </para>
/// <para>
/// <b>New (monthly) format:</b> there is no header row — every row is a day row, and column
/// position alone decides the menu number unless overridden (see below). A day cell carries a
/// 2- or 4-digit year (e.g. <c>"LUNDI 05.10.26"</c>) and may have leading blank paragraphs before
/// the day label. A description cell's FIRST non-empty paragraph may itself be a <c>"MENU n"</c>
/// label (removed from the description once read), and its LAST non-empty line may end with a
/// trailing price token (also removed, and captured as <see cref="ParsedMenuEntry.Price"/>).
/// </para>
///
/// <para>
/// The week-caption paragraphs that appear in the document body above each table are IGNORED
/// entirely and never read by this parser. They are hand-typed prose, and in real sample files
/// they were observed to name the wrong month or year — contradicting the table rows they
/// supposedly label. Every date produced by this parser is derived solely from the day-row cells
/// plus the caller-supplied <see cref="Parse"/> year (cross-checked, for the new format, against
/// the year encoded directly in the day cell — see <see cref="ErrorCodes.MenuImportYearMismatch"/>);
/// the captions are never consulted.
/// </para>
///
/// <para>
/// A day row's classification as "working day" vs. "weekend row to skip" is decided from the
/// document's OWN day-name label, never from the resolved date alone (see
/// <see cref="IsWeekendRow"/> for why). One consequence is that label-vs-date disagreement is
/// handled two different ways depending on severity: a row labelled Monday-Friday whose resolved
/// date still falls Monday-Friday, just on the "wrong" weekday, is only a soft
/// <see cref="WeekdayMismatch"/> (never thrown -- the caller turns it into a confirmation prompt).
/// But a row labelled Monday-Friday whose resolved date falls on a Saturday or Sunday is a hard,
/// fail-fast error (<see cref="ErrorCodes.MenuImportWeekdayMismatch"/>): that combination is
/// near-certain proof the caller selected the wrong year, and silently dropping it as a "skipped
/// non-working day" would delete real working-day data without telling anyone.
/// </para>
/// </summary>
public sealed class MenuDocumentParser : IMenuDocumentParser
{
    // The WordprocessingML main namespace. Every element name used below (w:tbl, w:tr, w:tc,
    // w:p, w:t, w:br, ...) lives in this namespace; "W" is just a short alias so the rest of the
    // class reads like the plan's own notation.
    private const string WordMainNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // The markup-compatibility namespace: used only to recognize (and skip) mc:Fallback subtrees.
    private const string MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static readonly XNamespace W = WordMainNamespace;
    private static readonly XNamespace Mc = MarkupCompatibilityNamespace;

    // Matches a day-row's first cell, e.g. "LUNDI, 17.12", "Jeudi 18/12.", or (new monthly format)
    // "LUNDI 05.10.26" / "LUNDI 05.10.2026". Deliberately loose (day word can be anything
    // alphabetic; separators between day-of-month/month/year can be '.', '-' or '/') because the
    // template has been retyped by hand across many months and its exact punctuation is not
    // reliable. The year is optional and, when present, is captured in group "y" as either 2 or 4
    // digits (see MatchedDayRow.DocumentYear for how it is interpreted). A 250ms timeout guards
    // against pathological input on an otherwise attacker-uncontrolled but hand-authored document.
    private static readonly Regex DayCellRegex = new(
        @"^(?<day>\p{L}+)\s*,?\s*(?<d>\d{1,2})\s*[.\-/]\s*(?<m>\d{1,2})(?:\s*[.\-/]\s*(?<y>\d{4}|\d{2}))?\.?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    // Matches a header cell (old format) OR an in-cell label paragraph (new format) declaring
    // which menu number a column/cell holds, e.g. "MENU 1".
    private static readonly Regex MenuHeaderRegex = new(
        @"^\s*MENU\s*(?<n>\d+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    // Matches a trailing price token at the end of a description's last line, e.g. "14.80-",
    // "14,80.", "CHF 14.80", "Fr. 14.80–". The token itself is an optional CHF/Fr./Fr prefix, then
    // a 1-3 digit integer part + 2 decimal digits (separated by '.' or ','), then optional trailing
    // dash/dot/en-dash "noise" characters some months' authors append, all anchored to the end of
    // the line and required to be preceded by either the start of the line or whitespace (so a
    // size/count suffix glued onto a word, e.g. "Pizza 14cm" or "(2pcs)", never matches: those have
    // no decimal separator followed by exactly two digits). A 250ms timeout guards against
    // pathological input on an otherwise attacker-uncontrolled but hand-authored document.
    private static readonly Regex TrailingPriceRegex = new(
        @"(?<=^|\s)(?:(?:CHF|Fr\.|Fr)\s*)?(?<price>\d{1,3}[.,]\d{2})[\-.–]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    // Collapses any run of whitespace (including the '\n' introduced by multi-line cell
    // handling) down to a single space, to get the one-line form DayCellRegex/MenuHeaderRegex
    // are matched against.
    private static readonly Regex WhitespaceRunRegex = new(
        @"\s+",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    // French and English day names, matched case-insensitively and after diacritics have been
    // stripped from the document's word (see StripDiacritics). An unrecognized word here is not
    // an error -- see the weekday cross-check comment below.
    private static readonly Dictionary<string, DayOfWeek> DayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LUNDI"] = DayOfWeek.Monday,
        ["MARDI"] = DayOfWeek.Tuesday,
        ["MERCREDI"] = DayOfWeek.Wednesday,
        ["JEUDI"] = DayOfWeek.Thursday,
        ["VENDREDI"] = DayOfWeek.Friday,
        ["SAMEDI"] = DayOfWeek.Saturday,
        ["DIMANCHE"] = DayOfWeek.Sunday,
        ["MONDAY"] = DayOfWeek.Monday,
        ["TUESDAY"] = DayOfWeek.Tuesday,
        ["WEDNESDAY"] = DayOfWeek.Wednesday,
        ["THURSDAY"] = DayOfWeek.Thursday,
        ["FRIDAY"] = DayOfWeek.Friday,
        ["SATURDAY"] = DayOfWeek.Saturday,
        ["SUNDAY"] = DayOfWeek.Sunday,
    };

    private readonly ILogger<MenuDocumentParser> _logger;

    /// <summary>
    /// Parameterless constructor for call sites (tests, ad-hoc tools) that have no logger to
    /// supply; logs go nowhere via <see cref="NullLogger{T}"/>. Production DI registration
    /// (<c>ServicesServiceCollectionExtensions</c>) resolves the other constructor instead.
    /// </summary>
    public MenuDocumentParser()
        : this(NullLogger<MenuDocumentParser>.Instance)
    {
    }

    public MenuDocumentParser(ILogger<MenuDocumentParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// One recognized day row before its calendar year has been resolved: everything the first
    /// XML pass can determine on its own (which table it came from, its raw label, its
    /// day-of-month/month numbers, the year encoded directly in the cell if any, and its
    /// already-extracted, already-ordered menu entries). Kept internal to the single-pass
    /// construction described in the class summary: the final year (and hence the final
    /// <see cref="DateOnly"/>) can only be known once every row up to this one has been seen, in
    /// document order, by the rollover algorithm.
    /// </summary>
    private sealed record MatchedDayRow(
        int TableIndex,
        string RawLabel,
        string DayWordRaw,
        int Month,
        int Day,
        int? DocumentYear,
        IReadOnlyList<ParsedMenuEntry> Menus);

    /// <inheritdoc />
    public ParsedMenuDocument Parse(Stream docx, int year)
    {
        if (year < 1 || year > 9998)
        {
            throw new MenuDocumentParseException(
                ErrorCodes.MenuImportInvalidDate,
                $"The import year {year} is outside the supported range (1-9998).");
        }

        var document = OpenDocumentXml(docx);

        var body = document.Root?.Element(W + "body");
        if (body is null)
        {
            throw new MenuDocumentParseException(
                ErrorCodes.MenuImportNoDataFound,
                "The document has no <w:body> element; it contains no menu data.");
        }

        // Top-level tables only, in document order. Nested tables (inside a cell) and anything
        // living inside an mc:Fallback subtree are deliberately never reached: Elements() only
        // walks direct children of <w:body>, and a nested table is a descendant of a <w:tc>, not
        // of <w:body> itself.
        var tables = body.Elements(W + "tbl").ToList();

        var matchedRows = new List<MatchedDayRow>();
        var anyInCellLabelUsed = false;
        for (var tableIndex = 0; tableIndex < tables.Count; tableIndex++)
        {
            CollectMatchedRows(tables[tableIndex], tableIndex, matchedRows, ref anyInCellLabelUsed);
        }

        var resolvedDays = ResolveDates(matchedRows, year);

        if (resolvedDays.Count == 0)
        {
            // Zero DAY ROWS is this parser's problem. Zero MENUS (every day row present but every
            // description cell blank) is a different, sibling concern owned by MenuImportService
            // and must not be checked here.
            throw new MenuDocumentParseException(
                ErrorCodes.MenuImportNoDataFound,
                "The document contains no recognizable day rows.");
        }

        var workingDays = new List<ParsedMenuDay>();
        var skippedNonWorkingDays = new List<ParsedMenuDay>();
        var weekdayMismatches = new List<WeekdayMismatch>();

        foreach (var (row, date) in resolvedDays)
        {
            var parsedDay = new ParsedMenuDay(date, row.RawLabel, row.Menus);

            foreach (var menu in parsedDay.Menus)
            {
                if (menu.Price is { } extractedPrice)
                {
                    _logger.LogDebug(
                        "Extracted price for {Date:yyyy-MM-dd}, menu {MenuNumber}: {Price}.",
                        date, menu.MenuNumber, extractedPrice);
                }
            }

            var strippedDayWord = StripDiacritics(row.DayWordRaw);
            DayOfWeek? labelDayOfWeek = DayNames.TryGetValue(strippedDayWord, out var mappedDayOfWeek)
                ? mappedDayOfWeek
                : null;

            if (IsWeekendRow(labelDayOfWeek, date))
            {
                skippedNonWorkingDays.Add(parsedDay);
                continue;
            }

            if (labelDayOfWeek is not null && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                // labelDayOfWeek can only be Monday..Friday here -- IsWeekendRow already filtered
                // out Saturday/Sunday labels above. A row the document itself labels as a working
                // day resolving onto a weekend date is near-certain proof the caller selected the
                // wrong import year, not a genuine weekend row -- this must be a hard, fail-fast
                // error, consistent with the chronology and invalid-date errors above, and NOT
                // silently folded into SkippedNonWorkingDays (that would just be this same data
                // -loss bug wearing a different name).
                //
                // Deliberately NOT included in MessageArgs: date.DayOfWeek's English name. These
                // args get interpolated into a culture-specific (French/English) resource string
                // in the Web layer; hard-coding an English "Saturday"/"Sunday" here would leak
                // untranslated English text into the French UI. Do not "helpfully" add the day
                // name to MessageArgs -- let the resource string itself name the day, in whatever
                // language it is rendered in.
                throw new MenuDocumentParseException(
                    ErrorCodes.MenuImportWeekdayMismatch,
                    $"Day row '{row.RawLabel}' is labelled as a working day but resolves to {date:yyyy-MM-dd}, a {date.DayOfWeek}.",
                    new object?[] { row.RawLabel, date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) });
            }

            workingDays.Add(parsedDay);

            if (labelDayOfWeek is { } recognizedLabelDayOfWeek && recognizedLabelDayOfWeek != date.DayOfWeek)
            {
                // Soft warning only -- NEVER throw here. This is a real, verified scenario in one
                // of the two sample documents analysed for this feature: a row's typed label
                // disagreed with the date its cell actually encoded (the document had been
                // authored against the wrong intended year), while the resolved date still fell
                // Monday-Friday. The upstream caller turns this into a user confirmation prompt; a
                // hard error here would wrongly reject a document that is otherwise perfectly
                // importable. Contrast with the hard error above: this branch is only reachable
                // when the resolved date is ALSO a weekday, never a Saturday/Sunday.
                weekdayMismatches.Add(new WeekdayMismatch(date, row.RawLabel, date.DayOfWeek));
            }
        }

        var weeksParsed = matchedRows.Select(r => r.TableIndex).Distinct().Count();

        var documentYearsFound = matchedRows
            .Select(r => r.DocumentYear)
            .Where(y => y.HasValue)
            .Select(y => y!.Value)
            .Distinct()
            .OrderBy(y => y)
            .ToList();
        var menusWithPriceCount = workingDays.Sum(d => d.Menus.Count(m => m.Price.HasValue));

        _logger.LogInformation(
            "Parsed menu document: {TableCount} table(s), {DayRowCount} day row(s), in-cell menu labels used: {InCellLabelsUsed}, document year(s) found: {DocumentYears}, menu(s) with an explicit price: {MenusWithPrice}.",
            tables.Count, matchedRows.Count, anyInCellLabelUsed, string.Join(",", documentYearsFound), menusWithPriceCount);

        return new ParsedMenuDocument(weeksParsed, workingDays, skippedNonWorkingDays, weekdayMismatches);
    }

    /// <summary>
    /// Opens <paramref name="docx"/> as a zip archive and loads its <c>word/document.xml</c>
    /// entry. A single catch net around zip-open, entry-open, and XML parsing is what makes a
    /// renamed .pdf/.doc/.txt file and a truncated .docx all fail cleanly with one, single,
    /// well-known error code instead of an unhandled exception type leaking out of this class.
    /// </summary>
    private static XDocument OpenDocumentXml(Stream docx)
    {
        try
        {
            // leaveOpen: true because the caller owns the stream (the service layer re-reads it
            // for a second pass after this parse); disposing the ZipArchive must only release its
            // own central-directory bookkeeping, never close the underlying stream.
            using var archive = new ZipArchive(docx, ZipArchiveMode.Read, leaveOpen: true);

            var entry = archive.Entries.FirstOrDefault(
                e => string.Equals(e.FullName, "word/document.xml", StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                throw new MenuDocumentParseException(
                    ErrorCodes.MenuImportInvalidDocument,
                    "The archive has no 'word/document.xml' entry; it is not a valid .docx file.");
            }

            using var entryStream = entry.Open();
            return XDocument.Load(entryStream, LoadOptions.None);
        }
        catch (InvalidDataException)
        {
            // Not a valid zip archive at all (e.g. a .pdf, a .doc, a plain .txt renamed to .docx).
            throw new MenuDocumentParseException(
                ErrorCodes.MenuImportInvalidDocument,
                "The file could not be read as a .docx (zip) archive.");
        }
        catch (XmlException)
        {
            // A valid zip but the document.xml entry is not well-formed XML (e.g. truncated).
            throw new MenuDocumentParseException(
                ErrorCodes.MenuImportInvalidDocument,
                "The document's word/document.xml entry is not well-formed XML.");
        }
        catch (IOException)
        {
            // Covers a truncated/corrupt zip whose local headers don't match its central
            // directory, surfaced by ZipArchive/DeflateStream as IOException rather than
            // InvalidDataException in some corruption patterns.
            throw new MenuDocumentParseException(
                ErrorCodes.MenuImportInvalidDocument,
                "The document could not be read; the archive appears to be corrupt or truncated.");
        }
    }

    /// <summary>
    /// Walks one top-level table's rows exactly once, identifying its header row (for
    /// column-to-menu-number mapping, old format only) and appending every recognized day row to
    /// <paramref name="matchedRows"/>, complete with its already-extracted, already-ordered menu
    /// entries. This is the only place cell text is extracted from the XML tree; nothing here is
    /// re-walked in a later pass. <paramref name="anyInCellLabelUsed"/> is set to <see langword="true"/>
    /// the first time any description cell's own first paragraph is used as its "MENU n" label
    /// (new format), purely for the end-of-parse summary log.
    /// </summary>
    private static void CollectMatchedRows(XElement table, int tableIndex, List<MatchedDayRow> matchedRows, ref bool anyInCellLabelUsed)
    {
        var rows = table.Elements(W + "tr").ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var rowInfos = new List<(List<XElement> Cells, string Cell0Raw, string Cell0Single)>();
        foreach (var row in rows)
        {
            var cells = row.Elements(W + "tc").ToList();
            if (cells.Count == 0)
            {
                continue; // defensive: a row with no cells carries no data to look at
            }

            var cell0Raw = ExtractCellText(cells[0]);
            rowInfos.Add((cells, cell0Raw, ToSingleLine(cell0Raw)));
        }

        // The header row is the FIRST row whose cell 0 does not look like a day label. This
        // tolerates both the normal "Jour | MENU 1 | ..." header and the new monthly template,
        // which has no header row at all (every row matches the day regex, so headerCells stays
        // null and every column falls back to positional numbering, possibly overridden per-cell
        // by an in-cell "MENU n" label -- see the loop below).
        List<XElement>? headerCells = null;
        foreach (var info in rowInfos)
        {
            if (!DayCellRegex.IsMatch(info.Cell0Single))
            {
                headerCells = info.Cells;
                break;
            }
        }

        var columnMenuNumbers = BuildColumnMenuNumberMap(headerCells);

        foreach (var info in rowInfos)
        {
            var match = DayCellRegex.Match(info.Cell0Single);
            if (!match.Success)
            {
                // Not a day row: this covers both the header row identified above and any other
                // spacer/caption row that might live inside the table. Skipped without error.
                continue;
            }

            var dayWordRaw = match.Groups["day"].Value;
            var day = int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);

            int? documentYear = null;
            if (match.Groups["y"].Success)
            {
                var yearText = match.Groups["y"].Value;
                var parsedYear = int.Parse(yearText, CultureInfo.InvariantCulture);
                // A 2-digit year ("26") means 2000+26; a 4-digit year ("2026") is used as-is.
                documentYear = yearText.Length == 2 ? 2000 + parsedYear : parsedYear;
            }

            var menus = new List<ParsedMenuEntry>();
            for (var columnIndex = 1; columnIndex < info.Cells.Count; columnIndex++)
            {
                var paragraphLines = ExtractNormalizedParagraphLines(info.Cells[columnIndex]);
                if (paragraphLines.Count == 0)
                {
                    continue; // a blank description cell contributes no entry for this column
                }

                // In-cell "MENU n" label (new format): when the cell's FIRST non-empty paragraph
                // fully matches MenuHeaderRegex, it is a label, not part of the description -- use
                // its number and remove the paragraph from the description.
                int? inCellMenuNumber = null;
                var labelMatch = MenuHeaderRegex.Match(paragraphLines[0]);
                if (labelMatch.Success && int.TryParse(labelMatch.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var labelNumber))
                {
                    inCellMenuNumber = labelNumber;
                    anyInCellLabelUsed = true;
                    paragraphLines = paragraphLines.Skip(1).ToList();

                    if (paragraphLines.Count == 0)
                    {
                        // A label with nothing after it contributes no entry -- same as a blank cell.
                        continue;
                    }
                }

                var price = ExtractTrailingPrice(ref paragraphLines);

                if (paragraphLines.Count == 0)
                {
                    // The only remaining line WAS the price token itself (e.g. a cell whose last
                    // paragraph held only "14.80-"); nothing is left to describe the menu with.
                    continue;
                }

                var description = string.Join(", ", paragraphLines);

                // Menu number priority: in-cell label > header-row map > column position.
                var menuNumber = inCellMenuNumber
                    ?? (columnMenuNumbers.TryGetValue(columnIndex, out var mapped) ? mapped : columnIndex);

                menus.Add(new ParsedMenuEntry(menuNumber, description, price));
            }

            menus.Sort((a, b) => a.MenuNumber.CompareTo(b.MenuNumber));

            matchedRows.Add(new MatchedDayRow(tableIndex, info.Cell0Raw, dayWordRaw, month, day, documentYear, menus));
        }
    }

    /// <summary>
    /// Inspects the LAST element of <paramref name="paragraphLines"/> for a trailing price token
    /// (see <see cref="TrailingPriceRegex"/>). When found and the parsed value is in the accepted
    /// range (0, 1000], the token is stripped from that line (dropping the line entirely if it
    /// becomes empty) and the price is returned; <paramref name="paragraphLines"/> is replaced
    /// in-place with the updated list. A price outside the accepted range, or no match at all, is
    /// simply ignored: the text is left untouched and <see langword="null"/> is returned.
    /// </summary>
    private static decimal? ExtractTrailingPrice(ref List<string> paragraphLines)
    {
        var lastIndex = paragraphLines.Count - 1;
        var lastLine = paragraphLines[lastIndex];

        var match = TrailingPriceRegex.Match(lastLine);
        if (!match.Success)
        {
            return null;
        }

        var priceText = match.Groups["price"].Value.Replace(',', '.');
        if (!decimal.TryParse(priceText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price)
            || price <= 0 || price > 1000)
        {
            // Out-of-range "price-shaped" text (e.g. a stray measurement) -- keep the text as-is.
            return null;
        }

        // TrimEnd(',') guards against a last line that was itself produced by NormalizeCellText
        // joining multiple w:br-separated sub-lines with ", " (e.g. "Garniture, 14.80-" from a
        // single paragraph) -- stripping the price token alone would otherwise leave a dangling
        // trailing comma.
        var strippedLine = lastLine[..match.Index].TrimEnd().TrimEnd(',').TrimEnd();

        var updatedLines = new List<string>(paragraphLines);
        if (strippedLine.Length == 0)
        {
            updatedLines.RemoveAt(lastIndex);
        }
        else
        {
            updatedLines[lastIndex] = strippedLine;
        }

        paragraphLines = updatedLines;
        return price;
    }

    /// <summary>
    /// Builds the column-index -> menu-number map for one table from its header row (or an empty
    /// map when there is none, meaning every column falls back to its own position, possibly
    /// overridden per-cell by an in-cell label). Columns that don't match "MENU n" are simply left
    /// unmapped rather than causing an error -- combined with the positional fallback applied by
    /// the caller, this is what makes the parser tolerant of the template's 2-column and 4-column
    /// variants alike.
    /// </summary>
    private static Dictionary<int, int> BuildColumnMenuNumberMap(List<XElement>? headerCells)
    {
        var map = new Dictionary<int, int>();
        if (headerCells is null)
        {
            return map;
        }

        for (var columnIndex = 1; columnIndex < headerCells.Count; columnIndex++)
        {
            var text = ToSingleLine(ExtractCellText(headerCells[columnIndex]));
            var match = MenuHeaderRegex.Match(text);
            if (match.Success && int.TryParse(match.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var menuNumber))
            {
                map[columnIndex] = menuNumber;
            }
        }

        return map;
    }

    /// <summary>
    /// Resolves the calendar year for every matched row, across ALL tables, in document order --
    /// not per-table, because a December week and a January week can legitimately live in two
    /// separate top-level tables, and both cases must be handled by the same single sweep. For a
    /// row whose cell encoded its own year (new format), the resolved date's year is cross-checked
    /// against it once rollover has been applied -- a mismatch is a hard, fail-fast error (see
    /// <see cref="ErrorCodes.MenuImportYearMismatch"/>): the document itself disagrees with the
    /// year the caller selected, so silently trusting the caller's year would misfile real data.
    /// </summary>
    private List<(MatchedDayRow Row, DateOnly Date)> ResolveDates(List<MatchedDayRow> matchedRows, int year)
    {
        var resolved = new List<(MatchedDayRow Row, DateOnly Date)>(matchedRows.Count);

        var currentYear = year;
        (int Month, int Day)? previousKey = null;
        var rolloverUsed = false;
        var previousResolvedDate = default(DateOnly);

        foreach (var row in matchedRows)
        {
            var key = (row.Month, row.Day);
            if (previousKey is { } previous && IsBefore(key, previous))
            {
                // The ONLY accepted backwards step is December -> January: a genuine year
                // rollover. Any other backwards step means the weeks are out of chronological
                // order in the document, or there's a day/month typo -- and that must be a hard
                // error, never silently absorbed.
                if (previous.Month != 12 || key.Month != 1)
                {
                    throw new MenuDocumentParseException(
                        ErrorCodes.MenuImportDatesNotChronological,
                        $"Row '{row.RawLabel}' is not chronologically after {previousResolvedDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}.",
                        new object?[] { previousResolvedDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), row.RawLabel });
                }

                // At most ONE rollover per document: a monthly import file can cross at most one
                // year boundary. A second "backwards" step is always an error, never another
                // rollover, so a multi-year jump can never happen silently.
                if (rolloverUsed)
                {
                    throw new MenuDocumentParseException(
                        ErrorCodes.MenuImportDatesNotChronological,
                        $"Row '{row.RawLabel}' would require a second year rollover after {previousResolvedDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}.",
                        new object?[] { previousResolvedDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), row.RawLabel });
                }

                currentYear++;
                rolloverUsed = true;
            }

            // Month range MUST be validated before calling DateTime.DaysInMonth: that method
            // throws ArgumentOutOfRangeException for an out-of-range month, and that must never
            // be allowed to escape this parser as an unhandled exception.
            if (row.Month < 1 || row.Month > 12)
            {
                throw new MenuDocumentParseException(
                    ErrorCodes.MenuImportInvalidDate,
                    $"Row '{row.RawLabel}' has an invalid month.",
                    new object?[] { row.RawLabel });
            }

            var daysInMonth = DateTime.DaysInMonth(currentYear, row.Month);
            if (row.Day < 1 || row.Day > daysInMonth)
            {
                throw new MenuDocumentParseException(
                    ErrorCodes.MenuImportInvalidDate,
                    $"Row '{row.RawLabel}' has an invalid day of month.",
                    new object?[] { row.RawLabel });
            }

            var date = new DateOnly(currentYear, row.Month, row.Day);

            if (row.DocumentYear is { } documentYear && documentYear != date.Year)
            {
                // Near-certain proof the caller selected the wrong import year: the document's own
                // day cell states a year that disagrees with the one just resolved. Must fail fast,
                // the same way MenuImportWeekdayMismatch does, rather than silently importing under
                // the wrong year.
                _logger.LogWarning(
                    "Day row {RawLabel} states year {DocumentYear} but the selected import year is {SelectedYear} (resolved date {Date:yyyy-MM-dd}).",
                    row.RawLabel, documentYear, year, date);

                throw new MenuDocumentParseException(
                    ErrorCodes.MenuImportYearMismatch,
                    $"Day row '{row.RawLabel}' belongs to year {documentYear} according to the document, but the selected import year is {year}.",
                    new object?[] { row.RawLabel, year, documentYear });
            }

            previousKey = key;
            previousResolvedDate = date;
            resolved.Add((row, date));
        }

        return resolved;
    }

    /// <summary>Compares two (month, day) keys, month first then day, without allocating a full date.</summary>
    private static bool IsBefore((int Month, int Day) a, (int Month, int Day) b) =>
        a.Month != b.Month ? a.Month < b.Month : a.Day < b.Day;

    /// <summary>
    /// Decides whether a resolved day row is a genuine weekend row (plan §9: skip and report,
    /// see <c>ParsedMenuDocument.SkippedNonWorkingDays</c>), using the document's OWN day-name
    /// label as the source of truth wherever it is available -- deliberately NOT the resolved
    /// <paramref name="date"/> alone. <paramref name="date"/> depends entirely on the
    /// caller-supplied import year (see the rollover algorithm in <see cref="ResolveDates"/>): if
    /// classification instead trusted <c>date.DayOfWeek</c> on its own, picking the wrong year
    /// would silently reclassify real Monday-Friday rows (e.g. one the document labels
    /// "MERCREDI") as weekend rows and delete them from the import with no error and no trace --
    /// this is the exact bug this method exists to prevent.
    ///
    /// <para>
    /// A row only counts as a weekend row when the document's own label says Saturday/Sunday, or
    /// when the label could not be recognized at all and the resolved date happens to land on a
    /// weekend (the only fallback available when there is no label to trust). A row labelled
    /// Monday-Friday is NEVER a weekend row here, whatever year was selected -- see the caller for
    /// the hard error that fires instead when such a row resolves onto a weekend date.
    /// </para>
    /// </summary>
    private static bool IsWeekendRow(DayOfWeek? labelDayOfWeek, DateOnly date) =>
        labelDayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
        || (labelDayOfWeek is null && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>
    /// Extracts the visible text of one table cell (<c>w:tc</c>): every paragraph's text, joined
    /// with '\n' between paragraphs, then whitespace-normalized (see remarks). Returns
    /// <see cref="string.Empty"/> for a blank/whitespace-only cell.
    /// </summary>
    private static string ExtractCellText(XElement tc)
    {
        var paragraphTexts = tc.Elements(W + "p").Select(ExtractParagraphText);
        var joined = string.Join('\n', paragraphTexts);
        return NormalizeCellText(joined);
    }

    /// <summary>
    /// Extracts one table cell's paragraphs as SEPARATE normalized, non-empty, single-line
    /// strings -- unlike <see cref="ExtractCellText"/>, which joins every paragraph into one
    /// string, this keeps paragraph boundaries visible to the caller. That is what lets a day
    /// row's description cell be inspected paragraph-by-paragraph: its first paragraph for an
    /// in-cell "MENU n" label, and its last for a trailing price. A blank paragraph (including any
    /// leading empty paragraphs some new-format cells have before their real content) contributes
    /// nothing to the result. Each returned line is independently run through
    /// <see cref="NormalizeCellText"/>, so a paragraph that itself contains <c>w:br</c>-induced
    /// line breaks still collapses to the same single comma-joined line
    /// <see cref="MenuHeaderRegex"/>/<see cref="TrailingPriceRegex"/> are matched against.
    /// </summary>
    private static List<string> ExtractNormalizedParagraphLines(XElement tc)
    {
        var lines = new List<string>();
        foreach (var paragraph in tc.Elements(W + "p"))
        {
            var normalized = NormalizeCellText(ExtractParagraphText(paragraph));
            if (normalized.Length > 0)
            {
                lines.Add(normalized);
            }
        }

        return lines;
    }

    /// <summary>
    /// Recursively walks one paragraph's element tree (via Elements(), never a flat
    /// DescendantNodes() walk) so that specific subtrees -- field-code source text, tracked
    /// deletions, and mc:Fallback alternates -- can be skipped in their entirety rather than
    /// merely having their own text nodes ignored.
    /// </summary>
    private static string ExtractParagraphText(XElement paragraph)
    {
        var builder = new StringBuilder();
        AppendElementText(paragraph, builder);
        return builder.ToString();
    }

    private static void AppendElementText(XElement element, StringBuilder builder)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name == W + "t")
            {
                builder.Append(child.Value);
            }
            else if (child.Name == W + "br" || child.Name == W + "cr")
            {
                // Multi-line descriptions are represented as w:br elements inside a single
                // paragraph, not as multiple paragraphs -- this is the one place a line break
                // enters the text.
                builder.Append('\n');
            }
            else if (child.Name == W + "tab")
            {
                builder.Append(' ');
            }
            else if (child.Name == W + "instrText" || child.Name == W + "delText" || child.Name == W + "delInstrText")
            {
                // Field-code source text and tracked-deletion text are never visible content;
                // skip the whole subtree rather than recursing into it.
            }
            else if (child.Name == Mc + "Fallback")
            {
                // mc:Fallback holds an alternate rendering for older Word versions; the real
                // content lives elsewhere in the tree, so skip this subtree to avoid duplicating
                // (or corrupting) the extracted text.
            }
            else
            {
                // Anything else (runs, hyperlinks, bookmarks, proofing markers, ...) is
                // transparent: recurse into its children to find the w:t elements inside.
                AppendElementText(child, builder);
            }
        }
    }

    /// <summary>
    /// Normalizes joined paragraph text: NBSP/narrow-NBSP become plain spaces, all line-ending
    /// styles become '\n', each line is trimmed, leading/trailing blank lines are dropped, any
    /// run of 2+ consecutive blank lines collapses to a single blank line, and the final lines
    /// are joined with a single space so the stored description is always a single readable line.
    /// </summary>
    private static string NormalizeCellText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace('\u00A0', ' ').Replace('\u202F', ' ');
        normalized = normalized.Replace("\r\n", "\n").Replace('\r', '\n');

        var lines = normalized.Split('\n').Select(line => line.Trim()).ToList();

        var start = 0;
        var end = lines.Count - 1;
        while (start <= end && lines[start].Length == 0)
        {
            start++;
        }

        while (end >= start && lines[end].Length == 0)
        {
            end--;
        }

        if (start > end)
        {
            return string.Empty;
        }

        var trimmedLines = lines.GetRange(start, end - start + 1);

        var collapsed = new List<string>(trimmedLines.Count);
        var previousWasBlank = false;
        foreach (var line in trimmedLines)
        {
            var isBlank = line.Length == 0;
            if (isBlank && previousWasBlank)
            {
                continue; // collapse a run of 2+ blank lines into one
            }

            collapsed.Add(line);
            previousWasBlank = isBlank;
        }

        return string.Join(", ", collapsed.Where(l => l.Length > 0));
    }

    /// <summary>
    /// Collapses a possibly multi-line cell string down to one line by replacing every run of
    /// whitespace (including the embedded '\n' from multi-line handling) with a single space.
    /// This is the form <see cref="DayCellRegex"/> and <see cref="MenuHeaderRegex"/> are matched
    /// against.
    /// </summary>
    private static string ToSingleLine(string text) => WhitespaceRunRegex.Replace(text, " ").Trim();

    /// <summary>
    /// Strips combining diacritical marks from <paramref name="s"/> (e.g. "Août" -> "Aout") so a
    /// day-name lookup is accent-insensitive regardless of how the document's author typed it.
    /// </summary>
    private static string StripDiacritics(string s)
    {
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
