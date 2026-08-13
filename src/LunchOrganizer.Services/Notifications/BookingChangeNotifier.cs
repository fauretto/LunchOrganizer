using LunchOrganizer.Services.Abstractions;

namespace LunchOrganizer.Services.Notifications;

/// <summary>
/// Thread-safe singleton implementation of <see cref="IBookingChangeNotifier"/>. Safe to subscribe to,
/// unsubscribe from, and invoke concurrently from any thread. Subscribers must marshal to their own UI
/// thread themselves before updating UI state.
/// </summary>
public sealed class BookingChangeNotifier : IBookingChangeNotifier
{
    private readonly object _gate = new();
    private Action<DateOnly>? _changed;

    public event Action<DateOnly>? Changed
    {
        add
        {
            lock (_gate)
            {
                _changed += value;
            }
        }
        remove
        {
            lock (_gate)
            {
                _changed -= value;
            }
        }
    }

    public void NotifyChanged(DateOnly date)
    {
        Action<DateOnly>? handlerSnapshot;
        lock (_gate)
        {
            handlerSnapshot = _changed;
        }

        handlerSnapshot?.Invoke(date);
    }
}
