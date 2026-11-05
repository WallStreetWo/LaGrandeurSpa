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

namespace LaGrandeurSpa.Web.Pages.Bookings;

public sealed class EditModel(
    ApplicationDbContext db,
    IBookingAvailabilityService availability,
    INotificationService notifications,
    IEmailSender email,
    IConfiguration configuration,
    ISpaClock clock,
    ILogger<EditModel> logger) : PageModel
{
    public Booking Booking { get; private set; } = default!;
    public List<SpaService> Services { get; private set; } = [];
    public DateTime TodayLocal => clock.TodayLocal;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? ErrorMessage { get; set; }

    public sealed class InputModel
    {
        [Range(1, int.MaxValue)]
        public int Id { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Please choose a treatment.")]
        public int SpaServiceId { get; set; }

        [Required, DataType(DataType.Date)]
        public DateTime Date { get; set; }

        [Required]
        public string StartTime { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? CustomerNote { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadBookingAsync(id))
            return NotFound();

        if (!CanModify(Booking, out var reason))
        {
            ErrorMessage = reason;
            return RedirectToPage("/Bookings/Index");
        }

        await LoadServicesAsync();
        Input = new InputModel
        {
            Id = Booking.Id,
            SpaServiceId = Booking.SpaServiceId,
            Date = Booking.StartLocal.Date,
            StartTime = Booking.StartLocal.ToString("HH:mm", CultureInfo.InvariantCulture),
            CustomerNote = Booking.CustomerNote
        };

        return Page();
    }

    public async Task<JsonResult> OnGetAvailabilityAsync(
        int id,
        int serviceId,
        DateTime date,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var booking = await db.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == userId, cancellationToken);

        if (booking is null || booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
            return new JsonResult(Array.Empty<object>());

        var service = await db.SpaServices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == serviceId && (x.IsActive || x.Id == booking.SpaServiceId), cancellationToken);

        if (service is null || date == default)
            return new JsonResult(Array.Empty<object>());

        var slots = await availability.GetAvailableSlotsAsync(date.Date, service, booking.Id, cancellationToken);
        return new JsonResult(slots.Select(x => new { value = x.Value, label = x.Label }));
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            if (!await LoadBookingAsync(Input.Id))
                return NotFound();
            await LoadServicesAsync();
            return Page();
        }

        var userId = GetUserId();
        var original = await db.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == Input.Id && x.CustomerId == userId);

        if (original is null)
            return NotFound();

        if (!CanModify(original, out var modifyReason))
        {
            ErrorMessage = modifyReason;
            return RedirectToPage("/Bookings/Index");
        }

        var service = await db.SpaServices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == Input.SpaServiceId && (x.IsActive || x.Id == original.SpaServiceId));

        if (service is null)
        {
            ModelState.AddModelError("Input.SpaServiceId", "That treatment is no longer available.");
            await LoadBookingAsync(Input.Id);
            await LoadServicesAsync();
            return Page();
        }

        if (!TimeSpan.TryParseExact(
                Input.StartTime,
                @"hh\:mm",
                CultureInfo.InvariantCulture,
                out var startTime) ||
            startTime < TimeSpan.Zero ||
            startTime >= TimeSpan.FromDays(1))
        {
            ModelState.AddModelError("Input.StartTime", "Please choose a valid available time.");
            await LoadBookingAsync(Input.Id);
            await LoadServicesAsync();
            return Page();
        }

        var newStart = DateTime.SpecifyKind(Input.Date.Date.Add(startTime), DateTimeKind.Unspecified);
        var newEnd = newStart.AddMinutes(service.DurationMinutes);
        var normalizedNote = string.IsNullOrWhiteSpace(Input.CustomerNote) ? null : Input.CustomerNote.Trim();
        var bookingDetailsChanged =
            original.SpaServiceId != service.Id ||
            original.StartLocal != newStart ||
            original.EndLocal != newEnd;
        var noteChanged = !string.Equals(original.CustomerNote, normalizedNote, StringComparison.Ordinal);

        if (!bookingDetailsChanged && !noteChanged)
        {
            ErrorMessage = "There are no changes to save.";
            return RedirectToPage("/Bookings/Index");
        }

        Booking? updatedBooking = null;
        var returnedToPending = false;

        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            var outcome = await strategy.ExecuteAsync(async () =>
            {
                var resetToPending = false;
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    HttpContext.RequestAborted);

                var booking = await db.Bookings
                    .Include(x => x.Slots)
                    .Include(x => x.Customer)
                    .FirstOrDefaultAsync(
                        x => x.Id == Input.Id && x.CustomerId == userId,
                        HttpContext.RequestAborted);

                if (booking is null || !CanModify(booking, out _))
                {
                    await transaction.RollbackAsync(HttpContext.RequestAborted);
                    return (Updated: false, ReturnedToPending: false);
                }

                if (bookingDetailsChanged && !await availability.IsAvailableAsync(
                        newStart,
                        newEnd,
                        booking.Id,
                        HttpContext.RequestAborted))
                {
                    await transaction.RollbackAsync(HttpContext.RequestAborted);
                    return (Updated: false, ReturnedToPending: false);
                }

                if (bookingDetailsChanged)
                {
                    db.BookingSlots.RemoveRange(booking.Slots);

                    booking.SpaServiceId = service.Id;
                    booking.StartLocal = newStart;
                    booking.EndLocal = newEnd;
                    booking.InvoiceServiceName = service.Name;
                    booking.InvoiceDurationMinutes = service.DurationMinutes;
                    booking.InvoiceAmount = service.Price;
                    booking.InvoiceIncludesHerbalTea = service.IncludesHerbalTea;
                    booking.InvoiceIssuedUtc = DateTime.UtcNow;

                    if (booking.Status == BookingStatus.Confirmed)
                    {
                        booking.Status = BookingStatus.Pending;
                        booking.ConfirmedUtc = null;
                        booking.ActionedById = null;
                        booking.AdminNote = null;
                        resetToPending = true;
                    }

                    await db.SaveChangesAsync(HttpContext.RequestAborted);

                    foreach (var slotStart in availability.BuildReservationSlots(newStart, newEnd))
                    {
                        db.BookingSlots.Add(new BookingSlot
                        {
                            BookingId = booking.Id,
                            SlotStartLocal = slotStart
                        });
                    }
                }

                booking.CustomerNote = normalizedNote;
                await db.SaveChangesAsync(HttpContext.RequestAborted);
                await transaction.CommitAsync(HttpContext.RequestAborted);
                return (Updated: true, ReturnedToPending: resetToPending);
            });

            if (!outcome.Updated)
            {
                ErrorMessage = "That booking changed or the new time is no longer available. Please review the latest booking and choose another slot.";
                return RedirectToPage("/Bookings/Index");
            }

            returnedToPending = outcome.ReturnedToPending;
            updatedBooking = await db.Bookings.AsNoTracking()
                .Include(x => x.Customer)
                .Include(x => x.SpaService)
                .FirstAsync(x => x.Id == Input.Id);
        }
        catch (DbUpdateException ex) when (SqlServerErrorHelper.IsUniqueConstraintViolation(ex))
        {
            logger.LogInformation(ex, "Booking update collision rejected for booking {BookingId}.", Input.Id);
            ErrorMessage = "That new time has just been booked by someone else. Please choose another available slot.";
            return RedirectToPage("/Bookings/Index");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogInformation(ex, "Booking {BookingId} changed concurrently during customer update.", Input.Id);
            ErrorMessage = "This booking was changed at the same time. Please review its latest status before trying again.";
            return RedirectToPage("/Bookings/Index");
        }

        if (updatedBooking is null)
            return RedirectToPage("/Bookings/Index");

        var customerMessage = returnedToPending
            ? $"Your booking was updated to {updatedBooking.BookedServiceName} on {updatedBooking.StartLocal:dd MMM yyyy} at {updatedBooking.StartLocal:HH:mm}. Kea will confirm the updated appointment shortly."
            : $"Your booking was updated successfully for {updatedBooking.StartLocal:dd MMM yyyy} at {updatedBooking.StartLocal:HH:mm}.";

        await TrySideEffectAsync(
            () => notifications.NotifyAsync(
                userId,
                "Booking updated",
                customerMessage,
                NotificationType.Success,
                "/Bookings/Index",
                updatedBooking.Id),
            updatedBooking.Id,
            "customer booking-update notification");

        await TrySideEffectAsync(
            () => notifications.NotifyAdminsAsync(
                "Customer updated a booking",
                $"{updatedBooking.Customer.FullName} updated {updatedBooking.ReferenceNumber} to {updatedBooking.BookedServiceName} on {updatedBooking.StartLocal:dd MMM yyyy} at {updatedBooking.StartLocal:HH:mm}." +
                (returnedToPending ? " The booking requires confirmation again." : string.Empty),
                NotificationType.Info,
                "/Admin/Bookings/Index",
                updatedBooking.Id),
            updatedBooking.Id,
            "admin booking-update notification");

        if (email.IsEnabled)
        {
            if (!string.IsNullOrWhiteSpace(updatedBooking.Customer.Email))
            {
                await TrySideEffectAsync(
                    () => email.SendAsync(
                        updatedBooking.Customer.Email,
                        $"La Grandeur Spa booking updated – {updatedBooking.ReferenceNumber}",
                        BookingEmailTemplates.BookingUpdated(updatedBooking, returnedToPending),
                        HttpContext.RequestAborted),
                    updatedBooking.Id,
                    "customer booking-update email");
            }

            var adminEmails = await db.AppUsers.AsNoTracking()
                .Where(x => x.IsActive && x.Role == AppUserRole.Admin && x.Email != string.Empty)
                .Select(x => x.Email)
                .Distinct()
                .ToListAsync(HttpContext.RequestAborted);

            foreach (var adminEmail in adminEmails)
            {
                await TrySideEffectAsync(
                    () => email.SendAsync(
                        adminEmail,
                        $"Customer updated booking {updatedBooking.ReferenceNumber}",
                        BookingEmailTemplates.BookingUpdatedForAdmin(updatedBooking, returnedToPending),
                        HttpContext.RequestAborted),
                    updatedBooking.Id,
                    $"admin booking-update email to {adminEmail}");
            }
        }

        TempData["StatusMessage"] = returnedToPending
            ? "Your booking has been updated and sent to Kea for confirmation again."
            : "Your booking has been updated successfully.";
        return RedirectToPage("/Bookings/Index");
    }

    private async Task<bool> LoadBookingAsync(int id)
    {
        var userId = GetUserId();
        var booking = await db.Bookings.AsNoTracking()
            .Include(x => x.SpaService)
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == userId);

        if (booking is null)
            return false;

        Booking = booking;
        return true;
    }

    private async Task LoadServicesAsync()
    {
        Services = await db.SpaServices.AsNoTracking()
            .Where(x => x.IsActive || x.Id == Booking.SpaServiceId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();
    }

    private bool CanModify(Booking booking, out string reason)
    {
        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
        {
            reason = "This booking can no longer be updated online.";
            return false;
        }

        var cutoffHours = Math.Clamp(
            configuration.GetValue<int?>("Business:CancellationCutoffHours") ?? 0,
            0,
            168);

        if (booking.StartLocal <= clock.NowLocal.AddHours(cutoffHours))
        {
            reason = cutoffHours > 0
                ? $"Online booking changes close {cutoffHours} hour(s) before your appointment. Please contact La Grandeur Spa directly."
                : "This appointment has already started and can no longer be updated online.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private async Task TrySideEffectAsync(Func<Task> action, int bookingId, string operation)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            logger.LogError(ex, "Booking {BookingId}: {Operation} failed after update was saved.", bookingId, operation);
        }
    }

    private int GetUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("The authenticated user identifier is invalid.");
}
