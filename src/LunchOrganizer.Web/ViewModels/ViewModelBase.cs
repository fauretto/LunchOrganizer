using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LunchOrganizer.Web.ViewModels;

/// <summary>
/// Base class for page/component view models: INotifyPropertyChanged plumbing, an IsBusy flag, and a
/// SemaphoreSlim(1,1)-guarded RunGuardedAsync that prevents double-submit on user commands (plan §11.9).
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged, IDisposable
{
    private readonly SemaphoreSlim _guard = new(1, 1);
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Runs <paramref name="action"/> only if no other guarded action is currently running on this view model.
    /// A concurrent call while one is already in flight is a no-op (silently ignored) — this is what stops a
    /// double-click from starting two saves. Sets <see cref="IsBusy"/> for the duration.
    /// </summary>
    protected async Task RunGuardedAsync(Func<Task> action)
    {
        if (!await _guard.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action().ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
            _guard.Release();
        }
    }

    public virtual void Dispose()
    {
        _guard.Dispose();
        GC.SuppressFinalize(this);
    }
}
