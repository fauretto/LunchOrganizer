using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Services.Abstractions;

/// <summary>
/// Parses a weekly-menu Word document (.docx) into structured data, without ever touching the
/// database. Implementations throw <see cref="LunchOrganizer.Services.Import.MenuDocumentParseException"/>
/// for every anticipated problem — a malformed file or unrecognized/invalid data — so callers can
/// convert parsing failures into a single, well-known error channel.
/// </summary>
public interface IMenuDocumentParser
{
    /// <summary>
    /// Parses the given .docx document into a <see cref="ParsedMenuDocument"/>.
    /// </summary>
    /// <param name="docx">
    /// The document's content stream. Must be readable and seekable; the parser never writes to
    /// it and never touches the database.
    /// </param>
    /// <param name="year">
    /// The year the user selected for the FIRST week in the document. Later weeks may resolve
    /// into <paramref name="year"/> + 1 (a December→January rollover).
    /// </param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="LunchOrganizer.Services.Import.MenuDocumentParseException">
    /// Thrown for every anticipated parsing problem (bad file, bad data).
    /// </exception>
    ParsedMenuDocument Parse(Stream docx, int year);
}
