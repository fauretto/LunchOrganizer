namespace LunchOrganizer.Services.Dtos;

/// <summary>Request to apply Menu number <paramref name="MenuNumber"/> to every still-editable day of the week that has that menu.</summary>
public sealed record WeekBookingRequest(int EmployeeId, DateOnly Monday, int MenuNumber);
