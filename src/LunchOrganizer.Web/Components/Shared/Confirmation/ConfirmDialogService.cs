using Microsoft.Extensions.Localization;

namespace LunchOrganizer.Web.Components.Shared.Confirmation;

/// <summary>
/// Default <see cref="IConfirmDialogService"/> implementation. Registered scoped (one instance per
/// circuit). Only one confirmation can be pending at a time, which is all this app ever needs
/// (no nested confirmations) — a second call while one is pending resolves the earlier one with
/// <c>false</c> first, rather than deadlocking either caller.
/// </summary>
public sealed class ConfirmDialogService : IConfirmDialogService
{
    // Fully qualified rather than "using LunchOrganizer.Web.Resources" + bare "Shared": this file's
    // own namespace (LunchOrganizer.Web.Components.Shared.Confirmation) nests under a sibling
    // namespace segment also named "Shared" (LunchOrganizer.Web.Components.Shared), which C#'s
    // namespace-member lookup resolves before the using-imported type, otherwise shadowing it
    // (CS0118: 'Shared' is a namespace but is used like a type).
    private readonly IStringLocalizer<LunchOrganizer.Web.Resources.Shared> _localizer;
    private readonly object _gate = new();
    private TaskCompletionSource<bool>? _pending;

    public ConfirmDialogService(IStringLocalizer<LunchOrganizer.Web.Resources.Shared> localizer)
    {
        _localizer = localizer;
    }

    public event Action<ConfirmRequest>? Requested;

    public Task<bool> ConfirmAsync(string message, string? title = null, string? confirmLabel = null, string? cancelLabel = null, bool isDestructive = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            _pending?.TrySetResult(false);
            _pending = tcs;
        }

        var request = new ConfirmRequest(
            message,
            title ?? _localizer["ConfirmDialogDefaultTitle"],
            confirmLabel ?? _localizer["ButtonConfirm"],
            cancelLabel ?? _localizer["ButtonCancel"],
            isDestructive);

        Requested?.Invoke(request);

        return tcs.Task;
    }

    /// <summary>
    /// Completes the currently pending confirmation, if any. Called by <see cref="ConfirmDialogHost"/>
    /// once the user picks Confirm/Cancel/Escape. Not part of the public interface — the host resolves
    /// this concrete type's instance directly since it is the only intended caller.
    /// </summary>
    internal void Resolve(bool result)
    {
        lock (_gate)
        {
            _pending?.TrySetResult(result);
            _pending = null;
        }
    }
}
