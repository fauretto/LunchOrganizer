using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;
using LunchOrganizer.Web.Components.Shared.Confirmation;
using LunchOrganizer.Web.Components.Shared.Toasts;
using LunchOrganizer.Web.Formatting;
using LunchOrganizer.Web.Localization;
using LunchOrganizer.Web.Resources;
using Microsoft.Extensions.Localization;

namespace LunchOrganizer.Web.ViewModels;

/// <summary>
/// View model for the admin "Employees" tab. Registered scoped (one instance per Blazor Server
/// circuit) — mirrors <see cref="BookingViewModel"/>/<see cref="ReportViewModel"/>'s idioms, see
/// <see cref="InitializeAsync"/> remarks for why that matters. All in-progress form state (search
/// query, add-form open/values, edit-form open/values) lives here rather than on the component so
/// a language switch (which remounts the component tree but reuses this same scoped instance)
/// never wipes unsaved input (plan §6.6).
/// </summary>
public sealed class AdminEmployeesViewModel : ViewModelBase
{
    private readonly IEmployeeService _employeeService;
    private readonly IReportService _reportService;
    private readonly IBookingChangeNotifier _notifier;
    private readonly IStringLocalizer<Admin> _loc;
    private readonly IStringLocalizer<Shared> _sharedLoc;
    private readonly IErrorMessageResolver _errorResolver;
    private readonly IToastService _toastService;
    private readonly IConfirmDialogService _confirmDialogService;

    private bool _initialized;

    // Re-entrancy guard for the notifier's synchronous event — same rationale as
    // BookingViewModel.OnBookingChanged/ReportViewModel.OnReportRelevantChange: a rapid
    // double-notification while a reload is already in flight is ignored rather than queued.
    private bool _isHandlingChangeNotification;

    private IReadOnlyList<EmployeeDto> _allEmployees = Array.Empty<EmployeeDto>();
    private string _searchQuery = string.Empty;
    private bool _isAddFormOpen;
    private string _newEmployeeName = string.Empty;
    private string _newEmployeeEmail = string.Empty;
    private int? _editingEmployeeId;
    private string _editEmployeeName = string.Empty;
    private string _editEmployeeEmail = string.Empty;

    public AdminEmployeesViewModel(
        IEmployeeService employeeService,
        IReportService reportService,
        IBookingChangeNotifier notifier,
        IStringLocalizer<Admin> loc,
        IStringLocalizer<Shared> sharedLoc,
        IErrorMessageResolver errorResolver,
        IToastService toastService,
        IConfirmDialogService confirmDialogService)
    {
        _employeeService = employeeService;
        _reportService = reportService;
        _notifier = notifier;
        _loc = loc;
        _sharedLoc = sharedLoc;
        _errorResolver = errorResolver;
        _toastService = toastService;
        _confirmDialogService = confirmDialogService;

        _notifier.Changed += OnEmployeeDataChanged;
    }

    public override void Dispose()
    {
        _notifier.Changed -= OnEmployeeDataChanged;
        base.Dispose();
    }

    // ---- Public properties ----

    /// <summary>The full unfiltered list of employees as loaded from the service.</summary>
    public IReadOnlyList<EmployeeDto> AllEmployees
    {
        get => _allEmployees;
        private set => SetProperty(ref _allEmployees, value);
    }

    /// <summary>Bound directly to the search input. Public setter for two-way binding.</summary>
    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    /// <summary>
    /// Computed (not cached) client-side filter of <see cref="AllEmployees"/> by
    /// <see cref="SearchQuery"/>, using the same <see cref="NameSimilarity.Normalize"/>
    /// contains-matching pattern as <c>BookingViewModel.OnEmployeeQueryChangedAsync</c>. Because
    /// <see cref="SearchQuery"/>'s setter raises <c>PropertyChanged</c>, the bound component
    /// re-renders and reflects the filtered list on every keystroke with no extra reload.
    /// </summary>
    public IReadOnlyList<EmployeeDto> FilteredEmployees
    {
        get
        {
            if (string.IsNullOrEmpty(SearchQuery))
            {
                return AllEmployees;
            }

            var normalizedQuery = NameSimilarity.Normalize(SearchQuery);
            return AllEmployees
                .Where(e => NameSimilarity.Normalize(e.FullName).Contains(normalizedQuery, StringComparison.Ordinal))
                .ToList();
        }
    }

    /// <summary>Bound directly to whether the Add form is expanded. Public setter for two-way binding.</summary>
    public bool IsAddFormOpen
    {
        get => _isAddFormOpen;
        set => SetProperty(ref _isAddFormOpen, value);
    }

    /// <summary>Bound to the Add form's name input. Public setter for two-way binding.</summary>
    public string NewEmployeeName
    {
        get => _newEmployeeName;
        set => SetProperty(ref _newEmployeeName, value);
    }

    /// <summary>Bound to the Add form's email input. Public setter for two-way binding.</summary>
    public string NewEmployeeEmail
    {
        get => _newEmployeeEmail;
        set => SetProperty(ref _newEmployeeEmail, value);
    }

    /// <summary>Null means no edit form is open; a non-null id means the edit form is open for that employee.</summary>
    public int? EditingEmployeeId
    {
        get => _editingEmployeeId;
        set => SetProperty(ref _editingEmployeeId, value);
    }

    /// <summary>Bound to the Edit form's name input. Public setter for two-way binding.</summary>
    public string EditEmployeeName
    {
        get => _editEmployeeName;
        set => SetProperty(ref _editEmployeeName, value);
    }

    /// <summary>Bound to the Edit form's email input. Public setter for two-way binding.</summary>
    public string EditEmployeeEmail
    {
        get => _editEmployeeEmail;
        set => SetProperty(ref _editEmployeeEmail, value);
    }

    // ---- Public methods ----

    /// <summary>
    /// Re-fetches the employee list on every call so that changes made elsewhere (e.g. a new
    /// employee registered from the Booking page) are reflected each time this panel is (re)mounted.
    /// Only the employee list is refreshed; in-progress form state (<see cref="SearchQuery"/>,
    /// <see cref="IsAddFormOpen"/>, the add/edit field values) is never touched here, so it is
    /// preserved across a remount (e.g. a language switch, which remounts the whole component tree
    /// but reuses this same scoped ViewModel instance).
    /// </summary>
    public Task InitializeAsync()
    {
        // Always re-fetch the list on every (re)mount so that employees added elsewhere — e.g. a
        // new employee registered from the Booking page — are reflected when returning to this
        // panel. In-progress form state (SearchQuery, IsAddFormOpen, the add/edit field values)
        // lives in separate fields that this method never touches, so it is still preserved across
        // a language-switch remount (which reuses this same scoped ViewModel instance).
        _initialized = true;
        return ReloadEmployeesAsync();
    }

    public void OpenAddForm()
    {
        IsAddFormOpen = true;
        NewEmployeeName = string.Empty;
        NewEmployeeEmail = string.Empty;
    }

    public void CancelAddForm()
    {
        IsAddFormOpen = false;
        NewEmployeeName = string.Empty;
        NewEmployeeEmail = string.Empty;
    }

    public Task SubmitAddEmployeeAsync() => RunGuardedAsync(async () =>
    {
        var registerResult = await _employeeService.RegisterAsync(NewEmployeeName.Trim());

        if (!registerResult.IsSuccess)
        {
            _toastService.ShowError(_errorResolver.Resolve(registerResult.ErrorCode, registerResult.MessageArgs));
            return;
        }

        var registered = registerResult.Value!;

        if (!string.IsNullOrWhiteSpace(NewEmployeeEmail))
        {
            var emailResult = await _employeeService.UpdateAsync(registered.Id, registered.FullName, NewEmployeeEmail.Trim());

            await ReloadEmployeesAsync();

            if (!emailResult.IsSuccess)
            {
                // The employee was still registered — don't lose that fact, but surface that the
                // email specifically didn't attach.
                _toastService.ShowError(_errorResolver.Resolve(emailResult.ErrorCode, emailResult.MessageArgs));
                return;
            }
        }
        else
        {
            await ReloadEmployeesAsync();
        }

        // No dedicated "employee registered" toast resx key exists in scope — closing the form and
        // the employee appearing in the now-reloaded list is sufficient visible confirmation
        // (mirroring BookingViewModel.RegisterNewEmployeeAsync's own comment about this).
        IsAddFormOpen = false;
        NewEmployeeName = string.Empty;
        NewEmployeeEmail = string.Empty;
    });

    public void OpenEditForm(EmployeeDto employee)
    {
        EditingEmployeeId = employee.Id;
        EditEmployeeName = employee.FullName;
        EditEmployeeEmail = employee.Email ?? string.Empty;
    }

    public void CancelEditForm()
    {
        EditingEmployeeId = null;
    }

    public Task SubmitEditEmployeeAsync() => RunGuardedAsync(async () =>
    {
        if (EditingEmployeeId is null)
        {
            return;
        }

        var id = EditingEmployeeId.Value;
        var result = await _employeeService.UpdateAsync(
            id,
            EditEmployeeName.Trim(),
            string.IsNullOrWhiteSpace(EditEmployeeEmail) ? null : EditEmployeeEmail.Trim());

        await ReloadEmployeesAsync();

        if (!result.IsSuccess)
        {
            _toastService.ShowError(_errorResolver.Resolve(result.ErrorCode, result.MessageArgs));
            return;
        }

        // No dedicated "updated" toast key exists — the list refreshing with the new values is the
        // confirmation (consistent with the Add flow's reasoning above).
        EditingEmployeeId = null;
    });

    public Task DeleteOrDeactivateEmployeeAsync(EmployeeDto employee) => RunGuardedAsync(async () =>
    {
        // The common "no history" path no longer touches the report service at all — the decision
        // now comes from HasBookingsAsync, a purpose-built check. The report call below survives
        // ONLY in the has-bookings branch, solely to obtain the booking count for the confirmation
        // message.
        var hasBookings = await _employeeService.HasBookingsAsync(employee.Id);

        bool confirmed;
        if (!hasBookings)
        {
            confirmed = await _confirmDialogService.ConfirmAsync(
                _loc["EmployeesDeletableConfirmTemplate", employee.FullName],
                confirmLabel: _sharedLoc["ButtonDelete"],
                isDestructive: true);
        }
        else
        {
            var report = await _reportService.GetReportAsync(employee.Id, DateOnly.MinValue, DateOnly.MaxValue);

            confirmed = await _confirmDialogService.ConfirmAsync(
                _loc["EmployeesNonDeletableConfirmTemplate", employee.FullName, report.Lines.Count],
                confirmLabel: _loc["EmployeesDeactivateButton"],
                isDestructive: false);
        }

        if (!confirmed)
        {
            return;
        }

        var result = await _employeeService.DeleteOrDeactivateWithOutcomeAsync(employee.Id);

        await ReloadEmployeesAsync();

        // The outcome is now reported directly by the service (DeleteOrDeactivateWithOutcomeAsync's
        // EmployeeDeletionOutcome), so no inference from the reloaded list is needed any more.
        if (!result.IsSuccess)
        {
            _toastService.ShowError(_errorResolver.Resolve(result.ErrorCode, result.MessageArgs));
            return;
        }

        switch (result.Value)
        {
            case EmployeeDeletionOutcome.Deleted:
                _toastService.ShowSuccess(_loc["EmployeesDeletedToast", employee.FullName]);
                break;

            case EmployeeDeletionOutcome.Deactivated:
                _toastService.ShowSuccess(_loc["EmployeesDeactivatedToast", employee.FullName]);
                break;
        }
    });

    // ---- Private helpers ----

    private async Task ReloadEmployeesAsync()
    {
        AllEmployees = await _employeeService.GetAllAsync(includeInactive: true);
    }

    private void OnEmployeeDataChanged(DateOnly changedDate)
    {
        try
        {
            if (!_initialized || _isHandlingChangeNotification)
            {
                return;
            }

            _isHandlingChangeNotification = true;

            // IBookingChangeNotifier.Changed is a plain synchronous Action<DateOnly> — do not await
            // here. ViewModelComponentBase already marshals PropertyChanged to the UI thread via its
            // own InvokeAsync(StateHasChanged), so no dispatcher call is needed from this ViewModel.
            // Unconditional on the date argument — there's no "week" concept here, an employee's
            // ever-booked status/booking count can change from any booking write anywhere.
            _ = ReloadAfterNotificationAsync();
        }
        catch (Exception)
        {
            // An exception from a notification-driven refresh must never crash the circuit.
        }
    }

    private async Task ReloadAfterNotificationAsync()
    {
        try
        {
            await ReloadEmployeesAsync();
        }
        catch (Exception)
        {
            // Swallow — see OnEmployeeDataChanged.
        }
        finally
        {
            _isHandlingChangeNotification = false;
        }
    }
}
