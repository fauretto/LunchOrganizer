using System.Globalization;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Enums;
using LunchOrganizer.Domain.Identity;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Email.Validation;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;
using LunchOrganizer.Web.Components.Shared.Toasts;
using LunchOrganizer.Web.Formatting;
using LunchOrganizer.Web.Localization;
using LunchOrganizer.Web.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Web.ViewModels;

/// <summary>
/// Single source of truth for the booking page (Stage 3). Registered scoped (one instance per
/// Blazor Server circuit) — see <see cref="InitializeAsync"/> remarks for why that matters.
/// </summary>
public sealed class BookingViewModel : ViewModelBase
{
    private readonly IBookingService _bookingService;
    private readonly IEmployeeService _employeeService;
    private readonly IWeekService _weekService;
    private readonly IBookingChangeNotifier _notifier;
    private readonly IClock _clock;
    private readonly IOptionsMonitor<AppOptions> _appOptions;
    private readonly IStringLocalizer<Booking> _loc;
    private readonly IErrorMessageResolver _errorResolver;
    private readonly IToastService _toastService;
    private readonly IPcUserContext? _pcUserContext;

    private bool _initialized;

    // Re-entrancy guard for the notifier's synchronous event: a rapid double-notification while a
    // reload is already in flight is ignored rather than queued. Acceptable — the in-flight reload
    // already targets the freshest state at the time it started, and any notification that arrives
    // while it is running will normally describe a change already reflected once it completes; a
    // genuinely missed update is caught by whatever triggers the next notification.
    private bool _isHandlingChangeNotification;

    private WeekIdentifier? _currentWeek;
    private WeekIdentifier? _selectedWeek;
    private WeekViewDto? _selectedWeekView;
    private DayViewDto? _todayView;
    private string _employeeQuery = string.Empty;
    private string _newEmployeeEmail = string.Empty;
    private IReadOnlyList<EmployeeDto> _suggestions = Array.Empty<EmployeeDto>();
    private EmployeeDto? _selectedEmployee;
    private bool _showRegisterPrompt;
    private IReadOnlyList<EmployeeDto> _similarNames = Array.Empty<EmployeeDto>();
    private IReadOnlyDictionary<DateOnly, int?> _pendingSelections = new Dictionary<DateOnly, int?>();
    private int? _wholeWeekMenuNumber;
    private WeekBookingResultDto? _lastWholeWeekResult;
    private TimeSpan? _remainingToday;
    private IReadOnlyList<(DateOnly Date, string LocalizedReason)> _skippedDaysWithLocalizedReasons =
        Array.Empty<(DateOnly Date, string LocalizedReason)>();

    public BookingViewModel(
        IBookingService bookingService,
        IEmployeeService employeeService,
        IWeekService weekService,
        IBookingChangeNotifier notifier,
        IClock clock,
        IOptionsMonitor<AppOptions> appOptions,
        IStringLocalizer<Booking> loc,
        IErrorMessageResolver errorResolver,
        IToastService toastService,
        IPcUserContext? pcUserContext = null)
    {
        _bookingService = bookingService;
        _employeeService = employeeService;
        _weekService = weekService;
        _notifier = notifier;
        _clock = clock;
        _appOptions = appOptions;
        _loc = loc;
        _errorResolver = errorResolver;
        _toastService = toastService;
        _pcUserContext = pcUserContext;

        _notifier.Changed += OnBookingChanged;
    }

    public override void Dispose()
    {
        _notifier.Changed -= OnBookingChanged;
        base.Dispose();
    }

    // ---- Public properties ----

    /// <summary>This week ("today"'s week). Fetched once, during the first <see cref="InitializeAsync"/> call.</summary>
    public WeekIdentifier? CurrentWeek
    {
        get => _currentWeek;
        private set => SetProperty(ref _currentWeek, value);
    }

    /// <summary>The week currently shown in the grid.</summary>
    public WeekIdentifier? SelectedWeek
    {
        get => _selectedWeek;
        private set => SetProperty(ref _selectedWeek, value);
    }

    /// <summary>Grid data for <see cref="SelectedWeek"/>. Requires <see cref="SelectedEmployee"/>.</summary>
    public WeekViewDto? SelectedWeekView
    {
        get => _selectedWeekView;
        private set => SetProperty(ref _selectedWeekView, value);
    }

    /// <summary>Today's day data for the banner, independent of whichever week the grid shows.</summary>
    public DayViewDto? TodayView
    {
        get => _todayView;
        private set => SetProperty(ref _todayView, value);
    }

    /// <summary>
    /// Bound to the autocomplete input text. Public setter deliberately (unlike most properties
    /// here) so the input can two-way-bind directly; <see cref="OnEmployeeQueryChangedAsync"/> also
    /// assigns it defensively so the two always agree once that call completes.
    /// </summary>
    public string EmployeeQuery
    {
        get => _employeeQuery;
        set => SetProperty(ref _employeeQuery, value);
    }

    /// <summary>
    /// Bound to the register-prompt's email input. Public setter (unlike <see cref="ShowRegisterPrompt"/>)
    /// because it two-way-binds directly from the page.
    /// </summary>
    public string NewEmployeeEmail
    {
        get => _newEmployeeEmail;
        set => SetProperty(ref _newEmployeeEmail, value);
    }

    /// <summary>Whether <see cref="NewEmployeeEmail"/> currently holds a well-formed address.</summary>
    public bool IsNewEmployeeEmailValid => EmailAddressValidation.IsValidFormat(NewEmployeeEmail);

    public IReadOnlyList<EmployeeDto> Suggestions
    {
        get => _suggestions;
        private set => SetProperty(ref _suggestions, value);
    }

    public EmployeeDto? SelectedEmployee
    {
        get => _selectedEmployee;
        private set => SetProperty(ref _selectedEmployee, value);
    }

    public bool ShowRegisterPrompt
    {
        get => _showRegisterPrompt;
        private set => SetProperty(ref _showRegisterPrompt, value);
    }

    public IReadOnlyList<EmployeeDto> SimilarNames
    {
        get => _similarNames;
        private set => SetProperty(ref _similarNames, value);
    }

    /// <summary>
    /// Day -> pending menu id (null means "no lunch that day"). Seeded from
    /// <see cref="SelectedWeekView"/>'s per-day <c>SelectedMenuId</c> every time a new week view is
    /// loaded (including after a save/apply reload, which is correct: at that point the persisted
    /// state IS what the pending selections should show). Mutated locally by grid clicks via
    /// <see cref="SetPendingSelection"/> without calling any service until <see cref="SaveAsync"/>.
    /// </summary>
    public IReadOnlyDictionary<DateOnly, int?> PendingSelections
    {
        get => _pendingSelections;
        private set => SetProperty(ref _pendingSelections, value);
    }

    /// <summary>
    /// Bound to the "apply Menu N to the whole week" control. Public setter (no dedicated command
    /// method mutates this) so a dropdown can two-way-bind directly.
    /// </summary>
    public int? WholeWeekMenuNumber
    {
        get => _wholeWeekMenuNumber;
        set => SetProperty(ref _wholeWeekMenuNumber, value);
    }

    /// <summary>Raw result of the last whole-week apply, for rendering the applied/skipped summary.</summary>
    public WeekBookingResultDto? LastWholeWeekResult
    {
        get => _lastWholeWeekResult;
        private set => SetProperty(ref _lastWholeWeekResult, value);
    }

    /// <summary>
    /// Localized, display-ready skip reasons for the last whole-week apply, keyed by date.
    /// <see cref="SkippedDayDto.Reason"/> is raw English and must never be displayed — this is the
    /// only sanctioned way to show why a day was skipped. Computed and stored once, right after the
    /// apply's reload, from the freshly reloaded <see cref="SelectedWeekView"/> (not recomputed
    /// later, since by then the grid may have moved on to a different week).
    /// </summary>
    public IReadOnlyList<(DateOnly Date, string LocalizedReason)> SkippedDaysWithLocalizedReasons
    {
        get => _skippedDaysWithLocalizedReasons;
        private set => SetProperty(ref _skippedDaysWithLocalizedReasons, value);
    }

    /// <summary>
    /// Remaining time until cut-off for today, from <see cref="IClock"/> and
    /// <see cref="AppOptions.BookingCutOffLocalTime"/>. Null when there is no meaningful positive
    /// remaining time (cut-off already passed, <see cref="TodayView"/> not loaded, or today is not
    /// editable). Deliberate simplification: computed once whenever <see cref="TodayView"/> is
    /// (re)loaded rather than via a live ticking countdown; a later stage may add periodic refresh.
    /// </summary>
    public TimeSpan? RemainingToday
    {
        get => _remainingToday;
        private set => SetProperty(ref _remainingToday, value);
    }

    /// <summary>
    /// Derived convenience for the whole-week menu-number selector: distinct menu numbers across all
    /// days of <see cref="SelectedWeekView"/>, ascending. Computed on the fly (not cached) since the
    /// underlying data is small and this keeps it trivially always-consistent with the current week
    /// view; exposed here rather than left to the page so the union logic isn't duplicated in a view.
    /// </summary>
    public IReadOnlyList<int> AvailableMenuNumbersForWeek =>
        SelectedWeekView is null
            ? Array.Empty<int>()
            : SelectedWeekView.Days
                .SelectMany(d => d.Menus)
                .Select(m => m.MenuNumber)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

    // ---- Public methods ----

    /// <summary>
    /// The <see cref="_initialized"/> guard makes repeat calls within one circuit's lifetime cheap
    /// (e.g. if a component re-renders and calls this again — such a call returns immediately
    /// without touching <see cref="SelectedWeek"/>/<see cref="PendingSelections"/>/etc). A language
    /// switch is different: it is a full page reload (<c>NavigateTo(..., forceLoad: true)</c>) that
    /// creates a brand-new circuit and a brand-new <see cref="BookingViewModel"/> instance, so
    /// <see cref="_initialized"/> starts false again there too and this method does real work once
    /// more. The equivalent state (selected week and employee) is restored via the
    /// <paramref name="weekMonday"/>/<paramref name="employeeId"/> parameters, whose values the page
    /// itself sourced from its own URL query string.
    /// </summary>
    public Task InitializeAsync(DateOnly? weekMonday = null, int? employeeId = null)
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        return InitializeCoreAsync(weekMonday, employeeId);
    }

    private async Task InitializeCoreAsync(DateOnly? weekMonday, int? employeeId)
    {
        CurrentWeek = await _weekService.GetCurrentWeekAsync();

        try
        {
            if (weekMonday.HasValue)
            {
                var resolved = await _weekService.GetWeekIdentifierAsync(weekMonday.Value);

                // Same defensive past-week clamp as GoToWeekAsync: a stale/hand-edited URL must not be
                // able to point the grid at a past week.
                SelectedWeek = CurrentWeek is not null && resolved.Monday < CurrentWeek.Monday
                    ? CurrentWeek
                    : resolved;
            }
            else
            {
                SelectedWeek ??= CurrentWeek;
            }

            if (employeeId.HasValue)
            {
                var employee = await _employeeService.GetByIdAsync(employeeId.Value);
                if (employee is not null && employee.IsActive)
                {
                    await SelectEmployeeInternalAsync(employee);
                }
            }
        }
        catch (Exception)
        {
            // The week/employee query-string values are user-editable (and, on a language switch, are
            // simply round-tripped from the URL this same page wrote) and can be stale or malformed. A
            // failure while restoring must never break page load or surface an error toast — it degrades
            // silently to the default state (current week, no employee selected) instead.
            SelectedWeek ??= CurrentWeek;
        }

        _initialized = true;
    }

    public Task OnEmployeeQueryChangedAsync(string fragment) => RunGuardedAsync(async () =>
    {
        EmployeeQuery = fragment;

        var options = _appOptions.CurrentValue;
        if (fragment.Length < options.AutocompleteMinChars)
        {
            Suggestions = Array.Empty<EmployeeDto>();
            SimilarNames = Array.Empty<EmployeeDto>();
            ShowRegisterPrompt = false;
            NewEmployeeEmail = string.Empty;
            return;
        }

        var all = await _employeeService.GetAllAsync(includeInactive: false);

        var normalizedFragment = NameSimilarity.Normalize(fragment);
        var matches = all
            .Where(e => NameSimilarity.Normalize(e.FullName).Contains(normalizedFragment, StringComparison.Ordinal))
            .OrderBy(e => e.FullName, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
            .Take(options.AutocompleteMaxResults)
            .ToList();

        Suggestions = matches;

        if (matches.Count == 0)
        {
            SimilarNames = all
                .Where(e => NameSimilarity.IsSimilar(e.FullName, fragment))
                .Take(5)
                .ToList();
            ShowRegisterPrompt = true;
        }
        else
        {
            SimilarNames = Array.Empty<EmployeeDto>();
            ShowRegisterPrompt = false;
        }
    });

    public Task SelectEmployeeAsync(EmployeeDto employee) => RunGuardedAsync(() => SelectEmployeeInternalAsync(employee));

    public async Task<bool> RegisterNewEmployeeAsync(string fullName, string email)
    {
        // Invalid address: reject before ever calling the service, and without a toast — the page
        // shows an inline hint instead, and a toast for a field the user is still typing would be noise.
        if (!EmailAddressValidation.IsValidFormat(email))
        {
            return false;
        }

        var result = false;

        await RunGuardedAsync(async () =>
        {
            var opResult = await _employeeService.RegisterAsync(fullName, email);

            if (opResult.IsSuccess)
            {
                await SelectEmployeeInternalAsync(opResult.Value!);

                // Deliberately no success toast for this specific path: there is no dedicated
                // "employee registered" resource key in scope for this task, and the register
                // prompt panel disappearing in favor of the booking grid already confirms success.
                result = true;
            }
            else
            {
                _toastService.ShowError(_errorResolver.Resolve(opResult.ErrorCode, opResult.MessageArgs));
                result = false;
            }
        });

        return result;
    }

    public Task GoToThisWeekAsync() => RunGuardedAsync(async () =>
    {
        if (CurrentWeek is null)
        {
            return;
        }

        SelectedWeek = CurrentWeek;

        if (SelectedEmployee is not null)
        {
            await ReloadSelectedWeekViewAsync();
        }
    });

    public Task GoToNextWeekAsync() => RunGuardedAsync(async () =>
    {
        var baseWeek = SelectedWeek ?? CurrentWeek;
        if (baseWeek is null)
        {
            return;
        }

        SelectedWeek = await _weekService.GetNextWeekAsync(baseWeek);

        if (SelectedEmployee is not null)
        {
            await ReloadSelectedWeekViewAsync();
        }
    });

    public Task GoToWeekAsync(DateOnly anyDateInWeek) => RunGuardedAsync(async () =>
    {
        var resolved = await _weekService.GetWeekIdentifierAsync(anyDateInWeek);

        // Defensive client-side clamp: past weeks are rejected even though nothing in the UI
        // should normally allow picking a past date.
        SelectedWeek = CurrentWeek is not null && resolved.Monday < CurrentWeek.Monday
            ? CurrentWeek
            : resolved;

        if (SelectedEmployee is not null)
        {
            await ReloadSelectedWeekViewAsync();
        }
    });

    /// <summary>
    /// Synchronous, local-only mutation (no service call) — not wrapped in <see cref="ViewModelBase.RunGuardedAsync"/>.
    /// </summary>
    public void SetPendingSelection(DateOnly date, int? menuId)
    {
        var day = SelectedWeekView?.Days.FirstOrDefault(d => d.Date == date);
        if (day is null || day.EditState != DayEditState.Editable)
        {
            return;
        }

        var current = PendingSelections.TryGetValue(date, out var pending) ? pending : day.SelectedMenuId;

        // Toggle-to-clear: clicking the already-selected menu again clears to "no lunch that day".
        var newValue = current == menuId ? null : menuId;

        var updated = new Dictionary<DateOnly, int?>(PendingSelections)
        {
            [date] = newValue
        };

        PendingSelections = updated;
    }

    public Task SaveAsync() => RunGuardedAsync(async () =>
    {
        if (SelectedEmployee is null || SelectedWeekView is null)
        {
            return;
        }

        var employee = SelectedEmployee;
        var view = SelectedWeekView;

        var changedDates = new List<DateOnly>();
        var attemptedCount = 0;
        var anyFailed = false;
        int? lastChangedMenuNumber = null;

        // Resolved once for the whole save, not once per day — it is the same PC user for every
        // day being saved here. A null/empty result is normal and must not affect the booking.
        var bookedBy = _pcUserContext is null ? PcUserInfo.Empty : await _pcUserContext.GetCurrentAsync();

        // Sequential, ascending-date order — never Task.WhenAll — mirrors the whole-week booking's
        // ordering discipline, applied here for consistency even though these calls are independent.
        foreach (var day in view.Days)
        {
            if (day.EditState != DayEditState.Editable)
            {
                continue;
            }

            var pending = PendingSelections.TryGetValue(day.Date, out var p) ? p : day.SelectedMenuId;
            if (pending == day.SelectedMenuId)
            {
                continue;
            }

            attemptedCount++;

            if (pending is not null)
            {
                var bookResult = await _bookingService.BookDayAsync(new BookingRequest(employee.Id, day.Date, pending.Value, bookedBy));
                if (bookResult.IsSuccess)
                {
                    changedDates.Add(day.Date);
                    lastChangedMenuNumber = bookResult.Value?.MenuNumber;
                }
                else
                {
                    anyFailed = true;
                }
            }
            else
            {
                var cancelResult = await _bookingService.CancelDayAsync(employee.Id, day.Date);
                if (cancelResult.IsSuccess)
                {
                    changedDates.Add(day.Date);
                    lastChangedMenuNumber = null; // cancellation: no menu number to show
                }
                else
                {
                    anyFailed = true;
                }
            }
        }

        // Reload regardless of partial failure, so the grid always reflects actual persisted state.
        await ReloadSelectedWeekViewAsync();
        if (changedDates.Contains(_clock.Today))
        {
            await ReloadTodayViewAsync();
        }

        if (attemptedCount == 0)
        {
            // Nothing to save — silently no-op, no toast.
            return;
        }

        if (anyFailed)
        {
            _toastService.ShowError(_loc["SubmitFailureToast"]);
            return;
        }

        if (changedDates.Count == 1)
        {
            if (lastChangedMenuNumber is not null)
            {
                var dayLabel = changedDates[0].ToString("dddd d MMMM", CultureInfo.CurrentCulture);
                _toastService.ShowSuccess(_loc["SubmitSuccessToastTemplate", lastChangedMenuNumber.Value, dayLabel]);
            }
            else
            {
                // The single change was a cancellation: SubmitSuccessToastTemplate assumes a booked
                // menu number to display, so fall back to the "N day(s) changed" phrasing with count = 1.
                _toastService.ShowSuccess(_loc["SubmitSuccessToastMultipleTemplate", 1]);
            }
        }
        else
        {
            _toastService.ShowSuccess(_loc["SubmitSuccessToastMultipleTemplate", changedDates.Count]);
        }
    });

    public Task ApplyMenuToWeekAsync() => RunGuardedAsync(async () =>
    {
        if (SelectedEmployee is null || SelectedWeek is null || WholeWeekMenuNumber is null)
        {
            return;
        }

        var employee = SelectedEmployee;
        var week = SelectedWeek;
        var menuNumber = WholeWeekMenuNumber.Value;

        // A null/empty PC user is normal and must not affect the booking.
        var bookedBy = _pcUserContext is null ? PcUserInfo.Empty : await _pcUserContext.GetCurrentAsync();

        var result = await _bookingService.BookWeekAsync(new WeekBookingRequest(employee.Id, week.Monday, menuNumber, bookedBy));

        await ReloadSelectedWeekViewAsync();
        if (SelectedWeekView is not null && SelectedWeekView.Days.Any(d => d.Date == _clock.Today))
        {
            await ReloadTodayViewAsync();
        }

        if (!result.IsSuccess)
        {
            _toastService.ShowError(_errorResolver.Resolve(result.ErrorCode, result.MessageArgs));
            return; // Keep whatever LastWholeWeekResult already held — do not overwrite with a failed attempt.
        }

        var payload = result.Value!;
        LastWholeWeekResult = payload;
        SkippedDaysWithLocalizedReasons = BuildSkippedDaysWithLocalizedReasons(payload, menuNumber);

        // Supplementary toast; the page's inline summary panel (from LastWholeWeekResult /
        // SkippedDaysWithLocalizedReasons) is the primary feedback, so this is skipped when nothing
        // was actually applied.
        if (payload.AppliedDates.Count > 0)
        {
            _toastService.ShowSuccess(_loc["SubmitSuccessToastMultipleTemplate", payload.AppliedDates.Count]);
        }
    });

    // ---- Private helpers ----

    private async Task SelectEmployeeInternalAsync(EmployeeDto employee)
    {
        SelectedEmployee = employee;
        EmployeeQuery = employee.FullName;
        Suggestions = Array.Empty<EmployeeDto>();
        SimilarNames = Array.Empty<EmployeeDto>();
        ShowRegisterPrompt = false;
        NewEmployeeEmail = string.Empty;

        await ReloadSelectedWeekViewAsync();
        await ReloadTodayViewAsync();
    }

    private async Task ReloadSelectedWeekViewAsync()
    {
        if (SelectedEmployee is null || SelectedWeek is null)
        {
            return;
        }

        var view = await _bookingService.GetWeekViewAsync(SelectedEmployee.Id, SelectedWeek);
        SelectedWeekView = view;
        SeedPendingSelectionsFrom(view);

        // Initialise the whole-week selector to the first available menu number so the button
        // is enabled and labelled correctly on first load (not only after the user changes the
        // combo selection).
        var firstMenuNumber = AvailableMenuNumbersForWeek.Count > 0 ? AvailableMenuNumbersForWeek[0] : (int?)null;
        if (WholeWeekMenuNumber is null || !AvailableMenuNumbersForWeek.Contains(WholeWeekMenuNumber.Value))
        {
            WholeWeekMenuNumber = firstMenuNumber;
        }
    }

    private void SeedPendingSelectionsFrom(WeekViewDto view)
    {
        var seeded = new Dictionary<DateOnly, int?>();
        foreach (var day in view.Days)
        {
            seeded[day.Date] = day.SelectedMenuId;
        }

        PendingSelections = seeded;
    }

    private async Task ReloadTodayViewAsync()
    {
        if (SelectedEmployee is null || CurrentWeek is null)
        {
            return;
        }

        var today = _clock.Today;

        // Reuse the already-loaded week view when it covers today, instead of a second fetch.
        var dayView = SelectedWeekView?.Days.FirstOrDefault(d => d.Date == today);

        if (dayView is null)
        {
            var view = await _bookingService.GetWeekViewAsync(SelectedEmployee.Id, CurrentWeek);
            dayView = view.Days.FirstOrDefault(d => d.Date == today);
        }

        TodayView = dayView;
        RemainingToday = ComputeRemainingToday(dayView);
    }

    private TimeSpan? ComputeRemainingToday(DayViewDto? dayView)
    {
        if (dayView is null || dayView.EditState != DayEditState.Editable)
        {
            return null;
        }

        var cutOff = _appOptions.CurrentValue.BookingCutOffLocalTime;
        var remaining = cutOff - _clock.LocalTimeOfDay;
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    private List<(DateOnly Date, string LocalizedReason)> BuildSkippedDaysWithLocalizedReasons(
        WeekBookingResultDto payload, int menuNumber)
    {
        var list = new List<(DateOnly Date, string LocalizedReason)>();

        // ReasonCode + ReasonArgs are now the authoritative source for the localized reason.
        // Reason remains a developer-facing English string that is still never displayed. The old
        // EditState-based derivation survives only as BuildFallbackSkipReason, used when a
        // SkippedDayDto supplies no ReasonCode (or an unrecognised one).
        foreach (var skipped in payload.SkippedDays)
        {
            string reason;
            switch (skipped.ReasonCode)
            {
                case ErrorCodes.BookingDateInPast:
                    reason = _loc["ApplyMenuSkipReasonPast"];
                    break;

                case ErrorCodes.BookingCutOffPassed:
                    var arg = skipped.ReasonArgs?.Count > 0 ? skipped.ReasonArgs[0] : null;
                    var cutOffFormatted = arg is TimeOnly cutOff
                        ? cutOff.ToString("t", CultureInfo.CurrentCulture)
                        : _appOptions.CurrentValue.BookingCutOffLocalTime.ToString("t", CultureInfo.CurrentCulture);
                    reason = _loc["ApplyMenuSkipReasonCutoffTemplate", cutOffFormatted];
                    break;

                case ErrorCodes.MenuNotFoundForDay:
                    reason = _loc["ApplyMenuSkipReasonNoMenuTemplate", menuNumber];
                    break;

                default:
                    reason = BuildFallbackSkipReason(skipped.Date, menuNumber);
                    break;
            }

            list.Add((skipped.Date, reason));
        }

        return list;
    }

    /// <summary>
    /// Legacy fallback used when a <see cref="SkippedDayDto"/> supplies no <c>ReasonCode</c> (or an
    /// unrecognised one): reconstructs the reason from the freshly reloaded day's EditState,
    /// cross-referenced by date, exactly as this whole method used to work before ReasonCode existed.
    /// </summary>
    private string BuildFallbackSkipReason(DateOnly date, int menuNumber)
    {
        var matchingDay = SelectedWeekView?.Days.FirstOrDefault(d => d.Date == date);

        if (matchingDay is null)
        {
            // Should not happen in practice; fall back to the "no menu" phrasing defensively.
            return _loc["ApplyMenuSkipReasonNoMenuTemplate", menuNumber];
        }

        if (matchingDay.EditState == DayEditState.LockedPast)
        {
            return _loc["ApplyMenuSkipReasonPast"];
        }

        if (matchingDay.EditState == DayEditState.LockedCutOff)
        {
            var cutOffFormatted = _appOptions.CurrentValue.BookingCutOffLocalTime.ToString("t", CultureInfo.CurrentCulture);
            return _loc["ApplyMenuSkipReasonCutoffTemplate", cutOffFormatted];
        }

        // Editable but presumably had no menu with the requested number.
        return _loc["ApplyMenuSkipReasonNoMenuTemplate", menuNumber];
    }

    private void OnBookingChanged(DateOnly changedDate)
    {
        try
        {
            if (SelectedEmployee is null || _isHandlingChangeNotification)
            {
                return;
            }

            var relevant = changedDate == _clock.Today ||
                (SelectedWeek is not null &&
                 changedDate >= SelectedWeek.Monday &&
                 changedDate <= SelectedWeek.Monday.AddDays(6));

            if (!relevant)
            {
                return;
            }

            _isHandlingChangeNotification = true;

            // IBookingChangeNotifier.Changed is a plain synchronous Action<DateOnly> — do not await
            // here. ViewModelComponentBase already marshals PropertyChanged to the UI thread via its
            // own InvokeAsync(StateHasChanged), so no dispatcher call is needed from this ViewModel.
            _ = ReloadAfterNotificationAsync(changedDate);
        }
        catch (Exception)
        {
            // An exception from a notification-driven refresh must never crash the circuit.
        }
    }

    private async Task ReloadAfterNotificationAsync(DateOnly changedDate)
    {
        try
        {
            if (SelectedEmployee is null)
            {
                return;
            }

            await ReloadSelectedWeekViewAsync();

            if (changedDate == _clock.Today)
            {
                await ReloadTodayViewAsync();
            }
        }
        catch (Exception)
        {
            // Swallow — see OnBookingChanged.
        }
        finally
        {
            _isHandlingChangeNotification = false;
        }
    }
}
