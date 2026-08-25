using LunchOrganizer.Domain.Identity;

namespace LunchOrganizer.Services.Dtos;

/// <summary>Request to book (or change) a single employee's lunch for one day. <paramref name="BookedBy"/> is the optional Windows user of the PC the booking was made from; <c>null</c> is normal.</summary>
public sealed record BookingRequest(int EmployeeId, DateOnly BookingDate, int MenuId, PcUserInfo? BookedBy = null);
