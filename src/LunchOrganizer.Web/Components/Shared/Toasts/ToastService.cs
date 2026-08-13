using System.Collections.Concurrent;

namespace LunchOrganizer.Web.Components.Shared.Toasts;

/// <summary>
/// Default <see cref="IToastService"/> implementation. Registered scoped (one instance per circuit).
/// Keeps an internal list of currently-visible toasts and auto-dismisses each one after
/// <see cref="AutoDismissAfter"/> using a <see cref="System.Threading.Timer"/> per toast.
/// </summary>
public sealed class ToastService : IToastService, IDisposable
{
    private static readonly TimeSpan AutoDismissAfter = TimeSpan.FromSeconds(6);

    private readonly object _gate = new();
    private readonly List<ToastMessage> _toasts = [];
    private readonly ConcurrentDictionary<Guid, Timer> _timers = new();
    private bool _disposed;

    public IReadOnlyList<ToastMessage> Toasts
    {
        get
        {
            lock (_gate)
            {
                return _toasts.ToArray();
            }
        }
    }

    public event Action? Changed;

    public void ShowSuccess(string text) => Show(ToastKind.Success, text);

    public void ShowError(string text) => Show(ToastKind.Error, text);

    public void ShowInfo(string text) => Show(ToastKind.Info, text);

    public void Dismiss(Guid id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _toasts.RemoveAll(t => t.Id == id) > 0;
        }

        if (_timers.TryRemove(id, out var timer))
        {
            timer.Dispose();
        }

        // If the toast was already removed (manual dismiss racing the auto-dismiss timer, or a
        // duplicate timer fire), there is nothing new to announce — this guards against a double
        // Changed notification for the same dismissal.
        if (removed)
        {
            Changed?.Invoke();
        }
    }

    private void Show(ToastKind kind, string text)
    {
        var toast = new ToastMessage(Guid.NewGuid(), kind, text);

        lock (_gate)
        {
            _toasts.Add(toast);
        }

        var timer = new Timer(_ => Dismiss(toast.Id), state: null, AutoDismissAfter, Timeout.InfiniteTimeSpan);
        _timers[toast.Id] = timer;

        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var timer in _timers.Values)
        {
            timer.Dispose();
        }

        _timers.Clear();
    }
}
