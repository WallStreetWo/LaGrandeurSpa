using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Security.Claims;
using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using LaGrandeurSpa.Web.Services;
using LaGrandeurSpa.Web.Services.Emails;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Pages;

public sealed class IndexModel(
    ApplicationDbContext db,
    IBookingAvailabilityService availability,
    INotificationService notifications,
    IEmailSender email,
    ISpaClock clock,
    ILogger<IndexModel> logger) : PageModel
{
    public AppUser CurrentUser { get; set; } = null!;
    public List<SpaService> Services { get; set; } = [];
    public List<Booking> CalendarBookings { get; set; } = [];
    public List<Booking> SelectedDateBookings { get; set; } = [];
    public List<AvailabilitySlot> SelectedDateSlots { get; set; } = [];
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime SelectedDate { get; set; }
    public int SelectedServiceId { get; set; }
    public DateTime TodayLocal => clock.TodayLocal;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [BindProperty]
    public BookingInput Input { get; set; } = new();

    public sealed class BookingInput
    {
        [Range(1, int.MaxValue, ErrorMessage = "Please choose a treatment.")]
        public int SpaServiceId { get; set; }

        [Required, DataType(DataType.Date)]
        public DateTime Date { get; set; }

        [Required]
        public string StartTime { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? CustomerNote { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(
        int? year,
        int? month,
        DateTime? selectedDate,
        int? serviceId)
    {
        if (User.IsInRole(nameof(AppUserRole.Admin)))
            return RedirectToPage("/Admin/Dashboard/Index");

        await LoadAsync(year, month, selectedDate, serviceId);
        return Page();
    }

    public async Task<JsonResult> OnGetAvailabilityAsync(
        int serviceId,
        DateTime date,
        CancellationToken cancellationToken)
    {
        var service = await db.SpaServices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == serviceId && x.IsActive, cancellationToken);

        if (service is null || date == default)
            return new JsonResult(Array.Empty<object>());

        var slots = await availability.GetAvailableSlotsAsync(date.Date, service, null, cancellationToken);
        return new JsonResult(slots.Select(x => new { value = x.Value, label = x.Label }));
    }

    public async Task<IActionResult> OnPostBookAsync(int? year, int? month)
    {
        if (User.IsInRole(nameof(AppUserRole.Admin)))
            return RedirectToPage("/Admin/Dashboard/Index");

        if (!ModelState.IsValid)
        {
            ErrorMessage = "Please choose a treatment, date and available time slot.";
            return BookingRedirect(year, month, Input.Date, Input.SpaServiceId);
        }

        var service = await db.SpaServices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == Input.SpaServiceId && x.IsActive);

        if (service is null)
        {
            ErrorMessage = "That treatment is no longer available.";
            return BookingRedirect(year, month, Input.Date, Input.SpaServiceId);
        }

        if (!TimeSpan.TryParseExact(
                Input.StartTime,
                @"hh\:mm",
                CultureInfo.InvariantCulture,
                out var startTime) ||
            startTime < TimeSpan.Zero ||
            startTime >= TimeSpan.FromDays(1))
        {
            ErrorMessage = "Please choose a valid time slot.";
            return BookingRedirect(year, month, Input.Date, service.Id);
        }

        var start = DateTime.SpecifyKind(Input.Date.Date.Add(startTime), DateTimeKind.Unspecified);
        var end = start.AddMinutes(service.DurationMinutes);
        var userId = GetUserId();
        Booking? booking = null;

        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            var bookedSuccessfully = await strategy.ExecuteAsync(async () =>
            {
                booking = null;
                db.ChangeTracker.Clear();

                await using var transaction = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    HttpContext.RequestAborted);

                if (!await availability.IsAvailableAsync(start, end, null, HttpContext.RequestAborted))
                {
                    await transaction.RollbackAsync(HttpContext.RequestAborted);
                    return false;
                }

                var referenceNumber =
                    $"LG-{clock.NowLocal:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";

                var createdBooking = new Booking
                {
                    ReferenceNumber = referenceNumber,
                    CustomerId = userId,
                    SpaServiceId = service.Id,
                    StartLocal = start,
                    EndLocal = end,
                    Status = BookingStatus.Pending,
                    CustomerNote = string.IsNullOrWhiteSpace(Input.CustomerNote)
                        ? null
                        : Input.CustomerNote.Trim(),
                    CreatedUtc = DateTime.UtcNow,
                    InvoiceNumber = $"INV-{referenceNumber.Replace("LG-", string.Empty, StringComparison.Ordinal)}",
                    InvoiceServiceName = service.Name,
                    InvoiceDurationMinutes = service.DurationMinutes,
                    InvoiceAmount = service.Price,
                    InvoiceIncludesHerbalTea = service.IncludesHerbalTea,
                    InvoiceIssuedUtc = DateTime.UtcNow
                };

                db.Bookings.Add(createdBooking);
                await db.SaveChangesAsync(HttpContext.RequestAborted);

                foreach (var slotStart in availability.BuildReservationSlots(start, end))
                {
                    db.BookingSlots.Add(new BookingSlot
                    {
                        BookingId = createdBooking.Id,
                        SlotStartLocal = slotStart
                    });
                }

                await db.SaveChangesAsync(HttpContext.RequestAborted);
                await transaction.CommitAsync(HttpContext.RequestAborted);
                booking = createdBooking;
                return true;
            });

            if (!bookedSuccessfully || booking is null)
            {
                ErrorMessage =
                    "Sorry, that time was just taken or is no longer available. Please choose another slot.";
                return BookingRedirect(start.Year, start.Month, start.Date, service.Id);
            }
        }
        catch (DbUpdateException ex) when (SqlServerErrorHelper.IsUniqueConstraintViolation(ex))
        {
            logger.LogInformation(ex, "Booking collision rejected for {StartLocal}.", start);
            ErrorMessage =
                "That slot has just been booked by someone else. Please choose another available time.";
            return BookingRedirect(start.Year, start.Month, start.Date, service.Id);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Database error while creating a booking for {StartLocal}.", start);
            ErrorMessage =
                "We could not save your booking right now. No appointment was created. Please try again.";
            return BookingRedirect(start.Year, start.Month, start.Date, service.Id);
        }

        // The reservation is committed at this point. Notifications/email are best-effort
        // side effects and must never turn a successful booking into a false failure message.
        var customer = await db.AppUsers.AsNoTracking().FirstAsync(x => x.Id == userId);

        await TryNotifyAsync(
            () => notifications.NotifyAsync(
                userId,
                "Booking submitted successfully",
                $"Your {booking.BookedServiceName} is booked for {start:dddd, dd MMMM yyyy} at {start:HH:mm}. La Grandeur Spa will notify you once Kea confirms it.",
                NotificationType.Success,
                "/Bookings/Index",
                booking.Id),
            "customer booking notification",
            booking.Id);

        await TryNotifyAsync(
            () => notifications.NotifyAsync(
                userId,
                "Your invoice is ready",
                $"Invoice {booking.InvoiceNumber} for {booking.BookedServiceName} is ready to view.",
                NotificationType.Info,
                $"/Bookings/Invoice?id={booking.Id}",
                booking.Id),
            "customer invoice notification",
            booking.Id);

        await TryNotifyAsync(
            () => notifications.NotifyAdminsAsync(
                "New booking received",
                $"{customer.FullName} booked {booking.BookedServiceName} for {start:dd MMM yyyy} at {start:HH:mm}.",
                NotificationType.Info,
                "/Admin/Bookings/Index",
                booking.Id),
            "admin booking notification",
            booking.Id);

        if (email.IsEnabled && !string.IsNullOrWhiteSpace(customer.Email))
        {
            try
            {
                var emailBooking = await db.Bookings.AsNoTracking()
                    .Include(x => x.Customer)
                    .Include(x => x.SpaService)
                    .FirstAsync(x => x.Id == booking.Id);

                await email.SendAsync(
                    customer.Email,
                    $"La Grandeur Spa booking received – {booking.ReferenceNumber}",
                    BookingEmailTemplates.BookingReceived(emailBooking),
                    HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Booking {BookingId} succeeded but booking-received email failed.",
                    booking.Id);
            }
        }

        StatusMessage =
            $"Booking successful. Your reference is {booking.ReferenceNumber}. Your invoice is available under My Bookings.";

        return BookingRedirect(start.Year, start.Month, start.Date, service.Id);
    }

    public IEnumerable<Booking> BookingsForDay(DateTime date) =>
        CalendarBookings
            .Where(x => x.StartLocal.Date == date.Date)
            .OrderBy(x => x.StartLocal);

    private async Task LoadAsync(int? year, int? month, DateTime? selectedDate, int? serviceId)
    {
        var userId = GetUserId();
        CurrentUser = await db.AppUsers.AsNoTracking().FirstAsync(x => x.Id == userId);

        Services = await db.SpaServices.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();

        var now = clock.NowLocal;
        Year = year is >= 2020 and <= 2100 ? year.Value : now.Year;
        Month = month is >= 1 and <= 12 ? month.Value : now.Month;

        var monthStart = new DateTime(Year, Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        CalendarBookings = await db.Bookings.AsNoTracking()
            .Include(x => x.SpaService)
            .Where(x =>
                x.StartLocal >= monthStart &&
                x.StartLocal < monthEnd &&
                x.Status != BookingStatus.Cancelled &&
                x.Status != BookingStatus.Declined)
            .OrderBy(x => x.StartLocal)
            .ToListAsync();

        SelectedDate = selectedDate?.Date ??
            (Year == now.Year && Month == now.Month ? now.Date : monthStart);

        if (SelectedDate.Year != Year || SelectedDate.Month != Month)
            SelectedDate = monthStart;

        SelectedServiceId = serviceId.HasValue && Services.Any(x => x.Id == serviceId.Value)
            ? serviceId.Value
            : Services.FirstOrDefault()?.Id ?? 0;

        SelectedDateBookings = CalendarBookings
            .Where(x => x.StartLocal.Date == SelectedDate)
            .ToList();

        var selectedService = Services.FirstOrDefault(x => x.Id == SelectedServiceId);
        if (selectedService is not null)
        {
            SelectedDateSlots = (await availability.GetAvailableSlotsAsync(SelectedDate, selectedService))
                .ToList();
        }
    }

    private async Task TryNotifyAsync(Func<Task> action, string operation, int bookingId)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            logger.LogError(ex, "Booking {BookingId} succeeded but {Operation} failed.", bookingId, operation);
        }
    }

    private IActionResult BookingRedirect(int? year, int? month, DateTime selectedDate, int serviceId)
    {
        var targetDate = selectedDate == default ? clock.TodayLocal : selectedDate.Date;
        var targetYear = year is >= 2020 and <= 2100 ? year.Value : targetDate.Year;
        var targetMonth = month is >= 1 and <= 12 ? month.Value : targetDate.Month;

        return RedirectToPage(new
        {
            year = targetYear,
            month = targetMonth,
            selectedDate = targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            serviceId = serviceId > 0 ? serviceId : (int?)null
        });
    }

    private int GetUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("The authenticated user identifier is invalid.");
}
