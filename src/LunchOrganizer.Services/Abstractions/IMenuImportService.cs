using LunchOrganizer.Domain.Common;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Services.Abstractions;

/// <summary>
/// Imports a monthly menu .docx document. There are two methods, not one, because the
/// weekday-mismatch confirmation (plan §2.6) must happen between parsing and committing: the UI
/// previews the parsed document first, lets the admin confirm or cancel, and only then commits.
/// Both methods run the same parse-and-validate pipeline internally; only <see cref="ImportAsync"/>
/// writes to the database.
/// </summary>
public interface IMenuImportService
{
    /// <summary>
    /// Parses and validates <paramref name="docx"/> without writing anything, so the UI can show
    /// the admin what would be imported (including any weekday/label mismatches) before they
    /// confirm. The caller owns <paramref name="docx"/>: it must be readable and seekable, and the
    /// caller must rewind it (<c>Position = 0</c>) before calling <see cref="ImportAsync"/> with the
    /// same content — the uploaded file is read from the browser only once and buffered by the
    /// caller (e.g. into a <see cref="MemoryStream"/>).
    /// </summary>
    Task<OperationResult<MenuImportPreviewDto>> PreviewAsync(Stream docx, int year, CancellationToken ct = default);

    /// <summary>
    /// Re-runs the same parse-and-validate pipeline as <see cref="PreviewAsync"/> and, if it
    /// succeeds, commits every parsed menu in one all-or-nothing transaction. The caller owns
    /// <paramref name="docx"/> and must rewind it (<c>Position = 0</c>) after a prior
    /// <see cref="PreviewAsync"/> call before invoking this method, since the stream is read again
    /// from the start.
    /// </summary>
    Task<OperationResult<MenuImportResultDto>> ImportAsync(Stream docx, int year, CancellationToken ct = default);
}
