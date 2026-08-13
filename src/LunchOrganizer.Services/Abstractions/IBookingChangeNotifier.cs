namespace LunchOrganizer.Services.Abstractions;

/// <summary>
/// Singleton, thread-safe notification hub. Any repository/service call that commits a booking, menu, or
/// price change invokes <see cref="NotifyChanged"/> from whatever thread the write happened on. Subscribers
/// (typically per-circuit ViewModels) are responsible for marshalling back to their own UI thread (e.g. via
/// Blazor's InvokeAsync(StateHasChanged)) before touching UI state (plan §11.9).
/// </summary>
public interface IBookingChangeNotifier
{
    event Action<DateOnly>? Changed;
    void NotifyChanged(DateOnly date);
}
