using LunchOrganizer.Services.Abstractions;

namespace LunchOrganizer.Tests.TestSupport;

/// <summary>
/// No-op <see cref="IBookingChangeNotifier"/> that records every date it was asked to notify about,
/// for tests that want to assert a notification happened.
/// </summary>
public sealed class FakeBookingChangeNotifier : IBookingChangeNotifier
{
    public List<DateOnly> NotifiedDates { get; } = new();

    public event Action<DateOnly>? Changed;

    public void NotifyChanged(DateOnly date)
    {
        NotifiedDates.Add(date);
        Changed?.Invoke(date);
    }
}
