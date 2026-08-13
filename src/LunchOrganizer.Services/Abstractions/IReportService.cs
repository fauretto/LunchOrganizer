using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Services.Abstractions;

public interface IReportService
{
    /// <summary>Pass employeeId = null for "all employees".</summary>
    Task<ReportDto> GetReportAsync(int? employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
