using System.Globalization;
using LunchOrganizer.Domain;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;
using LunchOrganizer.Web.Components.Shared.Confirmation;
using LunchOrganizer.Web.Components.Shared.Toasts;
using LunchOrganizer.Web.Localization;
using LunchOrganizer.Web.Resources;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Web.ViewModels;

/// <summary>
/// View model for the admin "Menus &amp; Prices" tab. Registered scoped (one instance per Blazor
/// Server circuit) — mirrors <see cref="BookingViewModel"/>/<see cref="AdminEmployeesViewModel"/>'s
/// idioms exactly, see <see cref="InitializeAsync"/> remarks for why that matters.
///
/// Unlike <see cref="BookingViewModel"/>, week navigation here deliberately does NOT clamp
/// <see cref="SelectedWeek"/> to <see cref="CurrentWeek"/> for past dates: admins are allowed (and
/// expected) to browse past weeks to review/fix historical menus for record-keeping. Delete/add
/// actions on past dates are still correctly blocked by the service/UI rules elsewhere in this file.
///
/// Also owns the Word (.docx) menu import flow: <see cref="OpenImportDialog"/>/
/// <see cref="CloseImportDialog"/> control <see cref="MenuImportDialog"/>'s visibility,
/// <see cref="OnImportFileSelectedAsync"/> buffers the uploaded file, and <see cref="RunImportAsync"/>
/// previews (for the weekday-mismatch confirmation), then commits, the import via
/// <see cref="IMenuImportService"/>.
/// </summary>
public sealed class AdminMenusViewModel : ViewModelBase
{
    private readonly IMenuService _menuService;
    private readonly IPricingService _pricingService;
    private readonly IWeekService _weekService;
    private readonly IBookingChangeNotifier _notifier;
    private readonly IClock _clock;
    private readonly IOptionsMonitor<AppOptions> _appOptions;
    private readonly IStringLocalizer<Admin> _loc;
    private readonly IStringLocalizer<Shared> _sharedLoc;
    private readonly IErrorMessageResolver _errorResolver;
    private readonly IToastService _toastService;
    private readonly IConfirmDialogService _confirmDialogService;
    private readonly IMenuImportService _menuImportService;

    private bool _initialized;

    // Re-entrancy guard for the notifier's synchronous event — same rationale as
    // BookingViewModel.OnBookingChanged: a rapid double-notification while a reload is already in
    // flight is ignored rather than queued.
    private bool _isHandlingChangeNotification;

    private WeekIdentifier? _currentWeek;
    private WeekIdentifier? _selectedWeek;
    private IReadOnlyList<DateOnly> _workingDays = Array.Empty<DateOnly>();
    private IReadOnlyDictionary<DateOnly, IReadOnlyList<MenuDto>> _menusByDate =
        new Dictionary<DateOnly, IReadOnlyList<MenuDto>>();
    private IReadOnlyDictionary<DateOnly, decimal> _pricesByDate = new Dictionary<DateOnly, decimal>();

    private readonly Dictionary<DateOnly, string> _newMenuDrafts = new();

    private bool _isImportDialogOpen;
    private int _importYear;
    private string? _importFileName;
    private string? _importErrorMessage;

    /// <summary>
    /// Buffered copy of the uploaded .docx, seekable and reusable across the preview + commit calls.
    /// See <see cref="OnImportFileSelectedAsync"/> for why buffering is necessary.
    /// </summary>
    private MemoryStream? _importFileContent;

    public AdminMenusViewModel(
        IMenuService menuService,
        IPricingService pricingService,
        IWeekService weekService,
        IBookingChangeNotifier notifier,
        IClock clock,
        IOptionsMonitor<AppOptions> appOptions,
        IStringLocalizer<Admin> loc,
        IStringLocalizer<Shared> sharedLoc,
        IErrorMessageResolver errorResolver,
        IToastService toastService,
        IConfirmDialogService confirmDialogService,
        IMenuImportService menuImportService)
    {
        _menuService = menuService;
        _pricingService = pricingService;
        _weekService = weekService;
        _notifier = notifier;
        _clock = clock;
        _appOptions = appOptions;
        _loc = loc;
        _sharedLoc = sharedLoc;
        _errorResolver = errorResolver;
        _toastService = toastService;
        _confirmDialogService = confirmDialogService;
        _menuImportService = menuImportService;

        _notifier.Changed += OnMenuOrPriceChanged;
    }

    public override void Dispose()
    {
        _notifier.Changed -= OnMenuOrPriceChanged;
        _importFileContent?.Dispose();
        base.Dispose();
    }

    // ---- Public properties ----

    /// <summary>This week ("today"'s week). Fetched once, during the first <see cref="InitializeAsync"/> call.</summary>
    public WeekIdentifier? CurrentWeek
    {
        get => _currentWeek;
        private set => SetProperty(ref _currentWeek, value);
    }

    /// <summary>The week currently shown in the panel. Admins may navigate this into the past.</summary>
    public WeekIdentifier? SelectedWeek
    {
        get => _selectedWeek;
        private set => SetProperty(ref _selectedWeek, value);
    }

    /// <summary><see cref="SelectedWeek"/>'s working days.</summary>
    public IReadOnlyList<DateOnly> WorkingDays
    {
        get => _workingDays;
        private set => SetProperty(ref _workingDays, value);
    }

    /// <summary>
    /// <see cref="SelectedWeek"/>'s menus, grouped by date. A day with no menus simply has no key —
    /// callers should use <see cref="GetMenusFor"/> rather than indexing this directly.
    /// </summary>
    public IReadOnlyDictionary<DateOnly, IReadOnlyList<MenuDto>> MenusByDate
    {
        get => _menusByDate;
        private set => SetProperty(ref _menusByDate, value);
    }

    /// <summary>
    /// <see cref="SelectedWeek"/>'s explicit price rows, keyed by date. A day with no explicit price
    /// row simply has no key — callers should use <see cref="GetPriceFor"/> rather than indexing this
    /// directly.
    /// </summary>
    public IReadOnlyDictionary<DateOnly, decimal> PricesByDate
    {
        get => _pricesByDate;
        private set => SetProperty(ref _pricesByDate, value);
    }

    /// <summary>Whether the "Import menus" modal is currently shown.</summary>
    public bool IsImportDialogOpen
    {
        get => _isImportDialogOpen;
        private set => SetProperty(ref _isImportDialogOpen, value);
    }

    /// <summary>The year the admin has selected the uploaded document to refer to.</summary>
    public int ImportYear
    {
        get => _importYear;
        private set => SetProperty(ref _importYear, value);
    }

    /// <summary>Display name of the currently selected import file, or null if none is selected.</summary>
    public string? ImportFileName
    {
        get => _importFileName;
        private set => SetProperty(ref _importFileName, value);
    }

    /// <summary>
    /// Localized failure text kept on screen inside the dialog (a toast alone is too transient for a
    /// message that lists conflicting dates).
    /// </summary>
    public string? ImportErrorMessage
    {
        get => _importErrorMessage;
        private set => SetProperty(ref _importErrorMessage, value);
    }

    /// <summary>Selectable years in the import dialog: current year minus 1 through plus 2, computed on demand.</summary>
    public IReadOnlyList<int> ImportYearChoices
    {
        get
        {
            var currentYear = _clock.Today.Year;
            return new[] { currentYear - 1, currentYear, currentYear + 1, currentYear + 2 };
        }
    }

    /// <summary>Whether a file is buffered and no guarded action is currently in flight.</summary>
    public bool CanRunImport => _importFileContent is not null && !IsBusy;

    public IReadOnlyList<MenuDto> GetMenusFor(DateOnly date) =>
        MenusByDate.TryGetValue(date, out var list) ? list : Array.Empty<MenuDto>();

    /// <summary>Falls back to <see cref="AppOptions.DefaultLunchPrice"/> when a day has no explicit price row yet.</summary>
    public decimal GetPriceFor(DateOnly date) =>
        PricesByDate.TryGetValue(date, out var p) ? p : _appOptions.CurrentValue.DefaultLunchPrice;

    /// <summary>
    /// Per-day "new menu description" draft buffer. Lives here (not as component-local state) so a
    /// language-switch remount never silently discards an admin's unsaved typing (plan §6.6).
    /// </summary>
    public string GetNewMenuDraft(DateOnly date) =>
        _newMenuDrafts.TryGetValue(date, out var v) ? v : string.Empty;

    public void SetNewMenuDraft(DateOnly date, string value)
    {
        _newMenuDrafts[date] = value;

        // Piggyback the change notification on MenusByDate — an already-existing public property —
        // so ViewModelComponentBase re-renders and the component's @bind:get picks up the fresh value.
        OnPropertyChanged(nameof(MenusByDate));
    }

    // ---- Public methods ----

    /// <summary>
    /// Idempotent: on a page remount (e.g. a language switch, which remounts the whole component
    /// tree but reuses this same scoped ViewModel instance) this returns immediately without
    /// touching <see cref="SelectedWeek"/>/<see cref="MenusByDate"/>/etc. Only the very first call
    /// does real async work.
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
        CurrentWeek = await _weekService.GetCurrentWeekAsync();
        SelectedWeek ??= CurrentWeek;

        await ReloadWeekDataAsync();

        _initialized = true;
    }

    public Task GoToThisWeekAsync() => RunGuardedAsync(async () =>
    {
        if (CurrentWeek is null)
        {
            return;
        }

        SelectedWeek = CurrentWeek;
        await ReloadWeekDataAsync();
    });

    public Task GoToNextWeekAsync() => RunGuardedAsync(async () =>
    {
        var baseWeek = SelectedWeek ?? CurrentWeek;
        if (baseWeek is null)
        {
            return;
        }

        SelectedWeek = await _weekService.GetNextWeekAsync(baseWeek);
        await ReloadWeekDataAsync();
    });

    /// <summary>
    /// Unlike <see cref="BookingViewModel.GoToWeekAsync"/>, this deliberately does NOT clamp
    /// <see cref="SelectedWeek"/> to <see cref="CurrentWeek"/> when the resolved week is earlier —
    /// admins are allowed to navigate into the past here.
    /// </summary>
    public Task GoToWeekAsync(DateOnly anyDateInWeek) => RunGuardedAsync(() => GoToWeekCoreAsync(anyDateInWeek));

    public Task AddMenuAsync(DateOnly date) => RunGuardedAsync(async () =>
    {
        var description = GetNewMenuDraft(date);
        var result = await _menuService.AddMenuAsync(date, string.IsNullOrWhiteSpace(description) ? null : description.Trim());

        await ReloadWeekDataAsync();

        if (!result.IsSuccess)
        {
            _toastService.ShowError(_errorResolver.Resolve(result.ErrorCode, result.MessageArgs));
            return;
        }

        // Clear the draft only on success, so a failed add (e.g. MaxMenusPerDayReached) leaves the
        // typed description in place for the admin to see/retry.
        SetNewMenuDraft(date, string.Empty);
    });

    public Task UpdateMenuDescriptionAsync(int menuId, string? description) => RunGuardedAsync(async () =>
    {
        var result = await _menuService.UpdateDescriptionAsync(menuId, string.IsNullOrWhiteSpace(description) ? null : description!.Trim());

        await ReloadWeekDataAsync();

        if (!result.IsSuccess)
        {
            _toastService.ShowError(_errorResolver.Resolve(result.ErrorCode, result.MessageArgs));
        }
    });

    /// <summary>
    /// Implements CONTRACT GAP #3's delete-guard precedence: past-date check first, then
    /// booking-count check, then the actual deletable case. The first two branches are defensive
    /// only — MenuCard should never offer a delete button in those states — so they safely no-op
    /// without calling the service or showing any dialog.
    /// </summary>
    public Task DeleteMenuAsync(MenuDto menu) => RunGuardedAsync(async () =>
    {
        if (IsMenuDateInPast(menu))
        {
            // Should be unreachable via the UI (MenuCard hides delete for past dates); no-op defensively.
            return;
        }

        if (menu.BookingCount > 0)
        {
            // Should be unreachable via the UI (MenuCard hides delete when bookings exist); no-op defensively.
            return;
        }

        var confirmed = await _confirmDialogService.ConfirmAsync(
            _loc["MenusDeleteConfirmNoBookings"],
            confirmLabel: _sharedLoc["ButtonDelete"],
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        var result = await _menuService.DeleteAsync(menu.Id);

        await ReloadWeekDataAsync();

        if (!result.IsSuccess)
        {
            // A race could still make the server reject it even though the client-side check passed
            // (e.g. a booking landed in the meantime) — handle that failure path gracefully.
            _toastService.ShowError(_errorResolver.Resolve(result.ErrorCode, result.MessageArgs));
        }

        // No dedicated "menu deleted" success toast key exists — the menu disappearing from the
        // reloaded list is sufficient confirmation (same reasoning accepted for the Employees panel).
    });

    public Task CopyDescriptionToWeekAsync(int menuId) => RunGuardedAsync(async () =>
    {
        var result = await _menuService.CopyDescriptionToWeekAsync(menuId);

        await ReloadWeekDataAsync();

        if (result.IsSuccess)
        {
            _toastService.ShowSuccess(_loc["MenusCopyDescriptionToast"]);
        }
        else
        {
            _toastService.ShowError(_errorResolver.Resolve(result.ErrorCode, result.MessageArgs));
        }
    });

    public Task SetPriceAsync(DateOnly date, decimal price) => RunGuardedAsync(async () =>
    {
        var result = await _pricingService.SetPriceAsync(date, price);

        await ReloadWeekDataAsync();

        if (!result.IsSuccess)
        {
            _toastService.ShowError(_errorResolver.Resolve(result.ErrorCode, result.MessageArgs));
        }

        // No success toast needed — the price re-rendering with its new formatted value in place is
        // the confirmation.
    });

    public Task ApplyPriceToWeekAsync(DateOnly date) => RunGuardedAsync(async () =>
    {
        if (SelectedWeek is null)
        {
            return;
        }

        var price = GetPriceFor(date);
        var result = await _pricingService.SetPriceForWeekAsync(SelectedWeek.Monday, price);

        await ReloadWeekDataAsync();

        if (result.IsSuccess)
        {
            _toastService.ShowSuccess(_loc["MenusApplyPriceToast"]);
        }
        else
        {
            _toastService.ShowError(_errorResolver.Resolve(result.ErrorCode, result.MessageArgs));
        }
    });

    /// <summary>
    /// Computed from the same <see cref="IClock.Today"/> read as <see cref="IsMenuDateInPast"/> so
    /// the two can never disagree.
    /// </summary>
    public bool CanDeleteMenu(MenuDto menu) => menu.BookingCount == 0 && menu.MenuDate >= _clock.Today;

    public bool IsMenuDateInPast(MenuDto menu) => menu.MenuDate < _clock.Today;

    public int GetMaxMenusPerDay() => _appOptions.CurrentValue.MaxMenusPerDay;

    // ---- Menu import ----

    /// <summary>Opens the import dialog with fresh state (any previous file selection/error is discarded).</summary>
    public void OpenImportDialog()
    {
        ImportFileName = null;
        ImportErrorMessage = null;
        _importFileContent?.Dispose();
        _importFileContent = null;
        ImportYear = _clock.Today.Year;
        IsImportDialogOpen = true;
    }

    /// <summary>Closes the import dialog and clears its state. Safe to call more than once.</summary>
    public void CloseImportDialog()
    {
        ImportFileName = null;
        ImportErrorMessage = null;
        _importFileContent?.Dispose();
        _importFileContent = null;
        IsImportDialogOpen = false;
    }

    public void SetImportYear(int year) => ImportYear = year;

    /// <summary>
    /// Buffers the selected .docx into a seekable <see cref="MemoryStream"/>. Deliberately NOT
    /// wrapped in <see cref="RunGuardedAsync"/>: file selection must stay responsive even while a
    /// previous guarded action (e.g. a week reload) is still finishing, so this method guards itself
    /// with its own try/catch instead of sharing the ViewModel-wide semaphore.
    /// </summary>
    public async Task OnImportFileSelectedAsync(IBrowserFile? file)
    {
        try
        {
            if (file is null)
            {
                _importFileContent?.Dispose();
                _importFileContent = null;
                ImportFileName = null;
                ImportErrorMessage = null;
                return;
            }

            if (!string.Equals(Path.GetExtension(file.Name), ".docx", StringComparison.OrdinalIgnoreCase))
            {
                _importFileContent?.Dispose();
                _importFileContent = null;
                ImportErrorMessage = _loc["MenusImportInvalidExtension"];
                return;
            }

            // Cheap client-side rejection before reading anything; the service re-checks the same
            // limit once the stream is actually parsed.
            if (file.Size > BusinessRules.MaxMenuImportBytes)
            {
                ImportErrorMessage = _errorResolver.Resolve(ErrorCodes.MenuImportFileTooLarge, [BusinessRules.MaxMenuImportMegabytes]);
                return;
            }

            _importFileContent?.Dispose();

            // IBrowserFile's stream is forward-only and single-use, but the import needs a seekable
            // stream and reads it twice (preview, then commit) — buffering into a MemoryStream means
            // the browser still uploads the file only once.
            await using var source = file.OpenReadStream(BusinessRules.MaxMenuImportBytes);
            var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            buffer.Position = 0;
            _importFileContent = buffer;

            ImportFileName = file.Name;
            ImportErrorMessage = null;
        }
        catch (IOException)
        {
            // OpenReadStream/CopyToAsync throws IOException when the actual stream turns out to
            // exceed maxAllowedSize — surfaces the same too-large message as the upfront check.
            ImportErrorMessage = _errorResolver.Resolve(ErrorCodes.MenuImportFileTooLarge, [BusinessRules.MaxMenuImportMegabytes]);
        }
        catch (Exception)
        {
            ImportErrorMessage = _errorResolver.Resolve(ErrorCodes.MenuImportFailed, null);
        }
    }

    public Task RunImportAsync() => RunGuardedAsync(async () =>
    {
        if (_importFileContent is null)
        {
            return;
        }

        ImportErrorMessage = null;

        _importFileContent.Position = 0;
        var preview = await _menuImportService.PreviewAsync(_importFileContent, ImportYear);

        if (!preview.IsSuccess)
        {
            var message = _errorResolver.Resolve(preview.ErrorCode, preview.MessageArgs);
            ImportErrorMessage = message;
            _toastService.ShowError(message);
            return;
        }

        if (preview.Value!.WeekdayMismatchExamples.Count > 0)
        {
            // Wrong-year guard from plan §2.6: this is deliberately a confirmation rather than a hard
            // error, because a document's weekday labels may legitimately have been typed for a
            // different year than the one selected.
            var confirmed = await _confirmDialogService.ConfirmAsync(
                _loc["MenusImportWeekdayMismatchConfirm", ImportYear, string.Join("; ", preview.Value.WeekdayMismatchExamples)],
                confirmLabel: _sharedLoc["ButtonConfirm"]);

            if (!confirmed)
            {
                return;
            }
        }

        _importFileContent.Position = 0;
        var result = await _menuImportService.ImportAsync(_importFileContent, ImportYear);

        if (!result.IsSuccess)
        {
            var message = _errorResolver.Resolve(result.ErrorCode, result.MessageArgs);
            ImportErrorMessage = message;
            _toastService.ShowError(message);
            return;
        }

        var r = result.Value!;
        var successText = (string)_loc["MenusImportSuccessTemplate", r.MenusImported, r.DaysImported,
            r.FirstDate.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture),
            r.LastDate.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture)];

        if (r.SkippedNonWorkingDayCount > 0)
        {
            successText += " " + (string)_loc["MenusImportSkippedDaysNote", r.SkippedNonWorkingDayCount];
        }

        _toastService.ShowSuccess(successText);

        // GoToWeekAsync is itself wrapped in RunGuardedAsync, and RunGuardedAsync's semaphore is not
        // re-entrant — calling it from here (already inside this method's own RunGuardedAsync) would
        // be silently dropped. GoToWeekCoreAsync is the extracted body so both paths can call it
        // directly. Do not re-inline it.
        await GoToWeekCoreAsync(r.FirstDate);

        CloseImportDialog();
    });

    // ---- Private helpers ----

    /// <summary>
    /// Extracted from <see cref="GoToWeekAsync"/> so <see cref="RunImportAsync"/> — already running
    /// inside its own <see cref="ViewModelBase.RunGuardedAsync"/> — can navigate to the imported
    /// week's data without going through GoToWeekAsync's own (non-reentrant) guard.
    /// </summary>
    private async Task GoToWeekCoreAsync(DateOnly anyDateInWeek)
    {
        SelectedWeek = await _weekService.GetWeekIdentifierAsync(anyDateInWeek);
        await ReloadWeekDataAsync();
    }

    private async Task ReloadWeekDataAsync()
    {
        if (SelectedWeek is null)
        {
            return;
        }

        WorkingDays = await _weekService.GetWorkingDaysAsync(SelectedWeek.Monday);

        var menus = await _menuService.GetForWeekAsync(SelectedWeek.Monday);
        MenusByDate = menus.GroupBy(m => m.MenuDate).ToDictionary(g => g.Key, g => (IReadOnlyList<MenuDto>)g.ToList());

        var prices = await _pricingService.GetPricesForWeekAsync(SelectedWeek.Monday);
        PricesByDate = prices.ToDictionary(p => p.PriceDate, p => p.Price);
    }

    private void OnMenuOrPriceChanged(DateOnly changedDate)
    {
        try
        {
            if (!_initialized || _isHandlingChangeNotification || SelectedWeek is null)
            {
                return;
            }

            var relevant = changedDate >= SelectedWeek.Monday && changedDate <= SelectedWeek.Monday.AddDays(6);
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
            await ReloadWeekDataAsync();
        }
        catch (Exception)
        {
            // Swallow — see OnMenuOrPriceChanged.
        }
        finally
        {
            _isHandlingChangeNotification = false;
        }
    }
}
