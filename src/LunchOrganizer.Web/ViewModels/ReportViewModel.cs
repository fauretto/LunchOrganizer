using System.Globalization;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Web.ViewModels;

/// <summary>
/// View model for the admin "Report" tab (Stage — admin build). Registered scoped (one instance per
/// Blazor Server circuit) — mirrors <see cref="BookingViewModel"/>'s idioms exactly, see
/// <see cref="InitializeAsync"/> remarks for why that matters.
/// </summary>
public sealed class ReportViewModel : ViewModelBase
{
    private readonly IReportService _reportService;
    private readonly IEmployeeService _employeeService;
    private readonly IClock _clock;
    private readonly IBookingChangeNotifier _notifier;

    private bool _initialized;

    // Re-entrancy guard for the notifier's synchronous event — same rationale as
    // BookingViewModel.OnBookingChanged: a rapid double-notification while a reload is already in
    // flight is ignored rather than queued.
    private bool _isHandlingChangeNotification;

    private IReadOnlyList<EmployeeDto> _employees = Array.Empty<EmployeeDto>();
    private int? _selectedEmployeeId;
    private DateOnly _from;
    private DateOnly _to;
    private ReportDto? _report;

    public ReportViewModel(
        IReportService reportService,
        IEmployeeService employeeService,
        IClock clock,
        IBookingChangeNotifier notifier)
    {
        _reportService = reportService;
        _employeeService = employeeService;
        _clock = clock;
        _notifier = notifier;

        _notifier.Changed += OnReportRelevantChange;
    }

    public override void Dispose()
    {
        _notifier.Changed -= OnReportRelevantChange;
        base.Dispose();
    }

    // ---- Public properties ----

    public IReadOnlyList<EmployeeDto> Employees
    {
        get => _employees;
        private set => SetProperty(ref _employees, value);
    }

    /// <summary>Bound by the employee filter's &lt;select&gt;. Public setter for two-way binding.</summary>
    public int? SelectedEmployeeId
    {
        get => _selectedEmployeeId;
        set => SetProperty(ref _selectedEmployeeId, value);
    }

    /// <summary>Bound by the start-date filter input. Public setter for two-way binding.</summary>
    public DateOnly From
    {
        get => _from;
        set => SetProperty(ref _from, value);
    }

    /// <summary>Bound by the end-date filter input. Public setter for two-way binding.</summary>
    public DateOnly To
    {
        get => _to;
        set => SetProperty(ref _to, value);
    }

    public ReportDto? Report
    {
        get => _report;
        private set => SetProperty(ref _report, value);
    }

    /// <summary>
    /// URL for the CSV export endpoint reflecting the current filters. Computed on demand (not
    /// cached) — cheap string work, and this always stays in sync with the latest filter values.
    /// </summary>
    public string ExportCsvUrl
    {
        get
        {
            var url = $"/admin/report/export.csv?from={From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
            if (SelectedEmployeeId is not null)
            {
                url += $"&employeeId={SelectedEmployeeId.Value}";
            }

            return url;
        }
    }

    // ---- Public methods ----

    /// <summary>
    /// Idempotent: on a page remount (e.g. a language switch, which remounts the whole component
    /// tree but reuses this same scoped ViewModel instance) this returns immediately without
    /// touching <see cref="From"/>/<see cref="To"/>/<see cref="Report"/>/etc. Only the very first
    /// call does real async work.
    /// </summary>
    public Task InitializeAsync()
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        return InitializeCoreAsync();
    }

    private async Task InitializeCoreAsync()
    {
        Employees = await _employeeService.GetAllAsync(includeInactive: true);

        // Deliberate, documented choice: the report's natural period is monthly, not weekly, so
        // default to the current calendar month rather than wiring IWeekService here.
        var today = _clock.Today;
        From = new DateOnly(today.Year, today.Month, 1);
        To = From.AddMonths(1).AddDays(-1);

        await ReloadReportAsync();

        _initialized = true;
    }

    public Task ReloadReportAsync() => RunGuardedAsync(async () =>
    {
        Report = await _reportService.GetReportAsync(SelectedEmployeeId, From, To);
    });

    // ---- Private helpers ----

    private void OnReportRelevantChange(DateOnly changedDate)
    {
        try
        {
            if (!_initialized || _isHandlingChangeNotification)
            {
                return;
            }

            var relevant = changedDate >= From && changedDate <= To;
            if (!relevant)
            {
                return;
            }

            _isHandlingChangeNotification = true;

            // IBookingChangeNotifier.Changed is a plain synchronous Action<DateOnly> — do not await
            // here. ViewModelComponentBase already marshals PropertyChanged to the UI thread via its
            // own InvokeAsync(StateHasChanged), so no dispatcher call is needed from this ViewModel.
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
            await ReloadReportAsync();
        }
        catch (Exception)
        {
            // Swallow — see OnReportRelevantChange.
        }
        finally
        {
            _isHandlingChangeNotification = false;
        }
    }
}
