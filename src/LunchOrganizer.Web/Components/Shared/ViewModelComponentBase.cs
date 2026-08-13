using System.ComponentModel;
using Microsoft.AspNetCore.Components;

namespace LunchOrganizer.Web.Components.Shared;

/// <summary>
/// Base component for MVVM-style pages/components: takes an <see cref="INotifyPropertyChanged"/> view model,
/// subscribes to its PropertyChanged event on init, re-renders via InvokeAsync(StateHasChanged) whenever the
/// view model changes, and unsubscribes on dispose.
/// </summary>
public abstract class ViewModelComponentBase : ComponentBase, IDisposable
{
    /// <summary>The view model driving this component. Must be set by the derived component (e.g. injected or passed as a parameter).</summary>
    protected abstract INotifyPropertyChanged ViewModel { get; }

    private bool _subscribed;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _subscribed = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribed = false;
        }

        GC.SuppressFinalize(this);
    }
}
