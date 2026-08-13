using LunchOrganizer.Domain.Common;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Services.Abstractions;

public interface IBookingService
{
    Task<WeekViewDto> GetWeekViewAsync(int employeeId, WeekIdentifier week, CancellationToken ct = default);
    Task<OperationResult<BookingDto>> BookDayAsync(BookingRequest request, CancellationToken ct = default);
    Task<OperationResult> CancelDayAsync(int employeeId, DateOnly date, CancellationToken ct = default);
    Task<OperationResult<WeekBookingResultDto>> BookWeekAsync(WeekBookingRequest request, CancellationToken ct = default);
}
