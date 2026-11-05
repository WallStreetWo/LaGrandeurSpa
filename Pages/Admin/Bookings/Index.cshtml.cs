using System.Security.Claims;
using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using LaGrandeurSpa.Web.Services;
using LaGrandeurSpa.Web.Services.Emails;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Pages.Admin.Bookings;

public sealed class IndexModel(
    ApplicationDbContext db,
    INotificationService notifications,
    IEmailSender email,
    ISpaClock clock,
    ILogger<IndexModel> logger) : PageModel
{
    public List<Booking> Items { get; set; } = [];
    public string StatusFilter { get; set; } = "Active";
    public DateTime? DateFilter { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(string? status, DateTime? date)
    {
        StatusFilter = string.IsNullOrWhiteSpace(status) ? "Active" : status;
        DateFilter = date?.Date;

        var query = db.Bookings.AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.SpaService)
            .AsQueryable();

        if (DateFilter.HasValue)
        {
            var nextDay = DateFilter.Value.AddDays(1);
            query = query.Where(x => x.StartLocal >= DateFilter.Value && x.StartLocal < nextDay);
        }

        query = StatusFilter switch
        {
            "Pending" => query.Where(x => x.Status == BookingStatus.Pending),
            "Confirmed" => query.Where(x => x.Status == BookingStatus.Confirmed),
            "Completed" => query.Where(x => x.Status == BookingStatus.Completed),
            "Cancelled" => query.Where(x => x.Status == BookingStatus.Cancelled),
            "Declined" => query.Where(x => x.Status == BookingStatus.Declined),
            "All" => query,
            _ => query.Where(x => x.Status == BookingStatus.Pending || x.Status == BookingStatus.Confirmed)
        };

        var showSoonestFirst = StatusFilter is "Active" or "Pending" or "Confirmed";
        Items = showSoonestFirst
            ? await query
                .OrderBy(x => x.StartLocal)
                .ThenBy(x => x.Customer.FullName)
                .Take(300)
                .ToListAsync()
            : await query
                .OrderByDescending(x => x.StartLocal)
                .ThenBy(x => x.Customer.FullName)
                .Take(300)
                .ToListAsync();
    }

    public async Task<IActionResult> OnPostConfirmAsync(int id, string? adminNote)
    {
        var booking = await LoadBookingAsync(id);
        if (booking is null)
            return RedirectToPage();

        if (booking.Status != BookingStatus.Pending)
        {
            ErrorMessage = "Only pending bookings can be confirmed.";
            return RedirectToPage();
        }

        if (booking.StartLocal <= clock.NowLocal)
        {
            ErrorMessage = "This appointment has already started or passed and can no longer be confirmed.";
            return RedirectToPage();
        }

        booking.Status = BookingStatus.Confirmed;
        booking.ConfirmedUtc = DateTime.UtcNow;
        booking.ActionedById = GetUserId();
        booking.AdminNote = CleanNote(adminNote);
        if (!await TrySaveBookingChangeAsync(booking.Id, "confirm"))
            return RedirectToPage();

        await TryNotifyAsync(
            () => notifications.NotifyAsync(
                booking.CustomerId,
                "Booking confirmed",
                $"La Grandeur Spa has confirmed your {booking.BookedServiceName} for {booking.StartLocal:dddd, dd MMMM yyyy} at {booking.StartLocal:HH:mm}.",
                NotificationType.Success,
                "/Bookings/Index",
                booking.Id),
            booking.Id,
            "confirmation notification");

        await TrySendEmailAsync(
            booking,
            "Your La Grandeur Spa booking is confirmed 🌿",
            BookingEmailTemplates.BookingConfirmed(booking),
            "confirmation");

        StatusMessage = $"{booking.Customer.FullName}'s booking has been confirmed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeclineAsync(int id, string? adminNote)
    {
        var booking = await LoadBookingAsync(id, includeSlots: true);
        if (booking is null)
            return RedirectToPage();

        if (booking.Status != BookingStatus.Pending)
        {
            ErrorMessage = "Only pending bookings can be declined. Confirmed bookings should be cancelled instead.";
            return RedirectToPage();
        }

        booking.Status = BookingStatus.Declined;
        booking.ActionedById = GetUserId();
        booking.AdminNote = CleanNote(adminNote);
        db.BookingSlots.RemoveRange(booking.Slots);
        if (!await TrySaveBookingChangeAsync(booking.Id, "decline"))
            return RedirectToPage();

        var reason = string.IsNullOrWhiteSpace(booking.AdminNote)
            ? string.Empty
            : $" Note: {booking.AdminNote}";

        await TryNotifyAsync(
            () => notifications.NotifyAsync(
                booking.CustomerId,
                "Booking could not be confirmed",
                $"Your {booking.BookedServiceName} for {booking.StartLocal:dd MMM yyyy} at {booking.StartLocal:HH:mm} was not confirmed.{reason}",
                NotificationType.Warning,
                "/Bookings/Index",
                booking.Id),
            booking.Id,
            "decline notification");

        await TrySendEmailAsync(
            booking,
            $"La Grandeur Spa booking update – {booking.ReferenceNumber}",
            BookingEmailTemplates.BookingDeclined(booking),
            "decline");

        StatusMessage = "Booking declined and the time slot has been released.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCompleteAsync(int id)
    {
        var booking = await LoadBookingAsync(id);
        if (booking is null)
            return RedirectToPage();

        if (booking.Status != BookingStatus.Confirmed)
        {
            ErrorMessage = "Only confirmed bookings can be marked as completed.";
            return RedirectToPage();
        }

        if (booking.EndLocal > clock.NowLocal)
        {
            ErrorMessage = "This appointment has not finished yet and cannot be marked as completed.";
            return RedirectToPage();
        }

        booking.Status = BookingStatus.Completed;
        booking.ActionedById = GetUserId();
        if (!await TrySaveBookingChangeAsync(booking.Id, "complete"))
            return RedirectToPage();

        await TryNotifyAsync(
            () => notifications.NotifyAsync(
                booking.CustomerId,
                "Thank you for visiting La Grandeur Spa",
                $"Your {booking.BookedServiceName} appointment has been marked as completed. We hope you left feeling relaxed, rejuvenated and renewed.",
                NotificationType.Success,
                "/Bookings/Index",
                booking.Id),
            booking.Id,
            "completion notification");

        StatusMessage = "Booking marked as completed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id, string? adminNote)
    {
        var booking = await LoadBookingAsync(id, includeSlots: true);
        if (booking is null)
            return RedirectToPage();

        if (booking.Status != BookingStatus.Confirmed)
        {
            ErrorMessage = "Only confirmed bookings can be cancelled. Pending requests should be declined instead.";
            return RedirectToPage();
        }

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledUtc = DateTime.UtcNow;
        booking.ActionedById = GetUserId();
        booking.AdminNote = CleanNote(adminNote);
        db.BookingSlots.RemoveRange(booking.Slots);
        if (!await TrySaveBookingChangeAsync(booking.Id, "cancel"))
            return RedirectToPage();

        var reason = string.IsNullOrWhiteSpace(booking.AdminNote)
            ? string.Empty
            : $" Note: {booking.AdminNote}";

        await TryNotifyAsync(
            () => notifications.NotifyAsync(
                booking.CustomerId,
                "Booking cancelled by La Grandeur Spa",
                $"Your {booking.BookedServiceName} appointment for {booking.StartLocal:dd MMM yyyy} at {booking.StartLocal:HH:mm} has been cancelled.{reason}",
                NotificationType.Warning,
                "/Bookings/Index",
                booking.Id),
            booking.Id,
            "spa cancellation notification");

        await TrySendEmailAsync(
            booking,
            $"La Grandeur Spa appointment cancelled – {booking.ReferenceNumber}",
            BookingEmailTemplates.BookingCancelledBySpa(booking),
            "cancellation");

        StatusMessage = "Booking cancelled and the time slot has been released.";
        return RedirectToPage();
    }

    private async Task<Booking?> LoadBookingAsync(int id, bool includeSlots = false)
    {
        IQueryable<Booking> query = db.Bookings
            .Include(x => x.Customer)
            .Include(x => x.SpaService);

        if (includeSlots)
            query = query.Include(x => x.Slots);

        return await query.FirstOrDefaultAsync(x => x.Id == id);
    }

    public bool IsPast(Booking booking) => booking.EndLocal <= clock.NowLocal;

    private async Task<bool> TrySaveBookingChangeAsync(int bookingId, string operation)
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            db.ChangeTracker.Clear();
            logger.LogInformation(ex, "Booking {BookingId} changed while admin tried to {Operation} it.", bookingId, operation);
            ErrorMessage = "This booking was changed by another action at the same time. Its latest status has been preserved; please review it and try again if needed.";
            return false;
        }
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
            logger.LogError(ex, "Booking {BookingId}: {Operation} failed after the status change was saved.", bookingId, operation);
        }
    }

    private async Task TrySendEmailAsync(Booking booking, string subject, string body, string operation)
    {
        if (!email.IsEnabled || string.IsNullOrWhiteSpace(booking.Customer.Email))
            return;

        try
        {
            await email.SendAsync(
                booking.Customer.Email,
                subject,
                body,
                HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Booking {BookingId}: {Operation} email failed.", booking.Id, operation);
        }
    }

    private int GetUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("The authenticated user identifier is invalid.");

    private static string? CleanNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return null;

        var clean = note.Trim();
        return clean.Length <= 1000 ? clean : clean[..1000];
    }
}
