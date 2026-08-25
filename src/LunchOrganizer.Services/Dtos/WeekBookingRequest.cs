using LunchOrganizer.Domain.Identity;

namespace LunchOrganizer.Services.Dtos;

/// <summary>Request to apply Menu number <paramref name="MenuNumber"/> to every still-editable day of the week that has that menu. <paramref name="BookedBy"/> is the optional Windows user of the PC the booking was made from; <c>null</c> is normal.</summary>
public sealed record WeekBookingRequest(int EmployeeId, DateOnly Monday, int MenuNumber, PcUserInfo? BookedBy = null);
