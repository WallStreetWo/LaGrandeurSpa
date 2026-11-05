using System.Security.Claims;
using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using LaGrandeurSpa.Web.Services;
using LaGrandeurSpa.Web.Services.Emails;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Pages.Bookings;

public sealed class IndexModel(
    ApplicationDbContext db,
    INotificationService notifications,
    IEmailSender email,
    IConfiguration configuration,
    ISpaClock clock,
    ILogger<IndexModel> logger) : PageModel
{
    public List<Booking> Upcoming { get; set; } = [];
    public List<Booking> History { get; set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        var userId = GetUserId();
        var all = await db.Bookings.AsNoTracking()
            .Include(x => x.SpaService)
            .Where(x => x.CustomerId == userId)
            .OrderByDescending(x => x.StartLocal)
            .ToListAsync();

        Upcoming = all
            .Where(x =>
                x.StartLocal >= clock.NowLocal &&
                x.Status is BookingStatus.Pending or BookingStatus.Confirmed)
            .OrderBy(x => x.StartLocal)
            .ToList();

        var upcomingIds = Upcoming.Select(x => x.Id).ToHashSet();
        History = all
            .Where(x => !upcomingIds.Contains(x.Id))
            .OrderByDescending(x => x.StartLocal)
            .ToList();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        var userId = GetUserId();
        var booking = await db.Bookings
            .Include(x => x.Customer)
            .Include(x => x.SpaService)
            .Include(x => x.Slots)
            .FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == userId);

        if (booking is null)
            return RedirectToPage();

        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
        {
            ErrorMessage = "This booking can no longer be cancelled.";
            return RedirectToPage();
        }

        var cutoffHours = Math.Clamp(
            configuration.GetValue<int?>("Business:CancellationCutoffHours") ?? 0,
            0,
            168);

        if (booking.StartLocal <= clock.NowLocal.AddHours(cutoffHours))
        {
            ErrorMessage = cutoffHours > 0
                ? $"Online cancellation closes {cutoffHours} hour(s) before your appointment. Please contact La Grandeur Spa directly."
                : "This appointment has already started and can no longer be cancelled online.";
            return RedirectToPage();
        }

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledUtc = DateTime.UtcNow;
        db.BookingSlots.RemoveRange(booking.Slots);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            db.ChangeTracker.Clear();
            logger.LogInformation(ex, "Booking {BookingId} changed while the customer was cancelling it.", booking.Id);
            ErrorMessage = "This booking was changed at the same time. Please review its latest status and try again if needed.";
            return RedirectToPage();
        }

        await TryNotifyAsync(
            () => notifications.NotifyAsync(
                userId,
                "Booking cancelled",
                $"Your {booking.BookedServiceName} on {booking.StartLocal:dd MMM yyyy} at {booking.StartLocal:HH:mm} has been cancelled successfully.",
                NotificationType.Warning,
                "/Bookings/Index",
                booking.Id),
            booking.Id,
            "customer cancellation notification");

        await TryNotifyAsync(
            () => notifications.NotifyAdminsAsync(
                "Customer cancelled a booking",
                $"{booking.Customer.FullName} cancelled {booking.BookedServiceName} for {booking.StartLocal:dd MMM yyyy} at {booking.StartLocal:HH:mm}. The time slot is available again.",
                NotificationType.Warning,
                "/Admin/Bookings/Index",
                booking.Id),
            booking.Id,
            "admin cancellation notification");

        if (email.IsEnabled && !string.IsNullOrWhiteSpace(booking.Customer.Email))
        {
            try
            {
                await email.SendAsync(
                    booking.Customer.Email,
                    $"La Grandeur Spa cancellation confirmed – {booking.ReferenceNumber}",
                    BookingEmailTemplates.BookingCancelledByCustomer(booking),
                    HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Booking {BookingId} was cancelled but cancellation email failed.", booking.Id);
            }
        }

        StatusMessage = "Your booking has been cancelled and the time slot has been released.";
        return RedirectToPage();
    }

    private async Task TryNotifyAsync(Func<Task> action, int bookingId, string operation)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            logger.LogError(ex, "Booking {BookingId}: {Operation} failed after the booking state was saved.", bookingId, operation);
        }
    }

    private int GetUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("The authenticated user identifier is invalid.");
}
