using System.ComponentModel.DataAnnotations;
using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using LaGrandeurSpa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Pages.Admin.Availability;

public sealed class IndexModel(ApplicationDbContext db, ISpaClock clock) : PageModel
{
    public List<BusinessHour> Hours { get; set; } = [];
    public List<BlockedPeriod> BlockedPeriods { get; set; } = [];
    public DateTime NowLocal => clock.NowLocal;

    [BindProperty]
    public BlockInput Block { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public sealed class BlockInput
    {
        [Required]
        public DateTime StartLocal { get; set; }

        [Required]
        public DateTime EndLocal { get; set; }

        [Required, StringLength(240, MinimumLength = 2)]
        public string Reason { get; set; } = string.Empty;
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostSaveHoursAsync(
        int id,
        string openTime,
        string closeTime,
        bool isClosed)
    {
        var row = await db.BusinessHours.FirstOrDefaultAsync(x => x.Id == id);
        if (row is null)
        {
            ErrorMessage = "That business-hours row no longer exists.";
            return RedirectToPage();
        }

        if (!TimeSpan.TryParse(openTime, out var open) ||
            !TimeSpan.TryParse(closeTime, out var close) ||
            open < TimeSpan.Zero || open >= TimeSpan.FromDays(1) ||
            close <= TimeSpan.Zero || close >= TimeSpan.FromDays(1) ||
            (!isClosed && close <= open))
        {
            ErrorMessage = "Please enter valid opening and closing times.";
            return RedirectToPage();
        }

        var futureBookings = await db.Bookings.AsNoTracking()
            .Where(x =>
                (x.Status == BookingStatus.Pending || x.Status == BookingStatus.Confirmed) &&
                x.EndLocal > clock.NowLocal)
            .Select(x => new { x.ReferenceNumber, x.StartLocal, x.EndLocal })
            .ToListAsync();

        var conflict = futureBookings
            .Where(x => x.StartLocal.DayOfWeek == row.DayOfWeek)
            .OrderBy(x => x.StartLocal)
            .FirstOrDefault(x =>
                isClosed ||
                x.StartLocal.TimeOfDay < open ||
                x.EndLocal.TimeOfDay > close);

        if (conflict is not null)
        {
            ErrorMessage =
                $"Hours were not changed because booking {conflict.ReferenceNumber} on {conflict.StartLocal:dd MMM yyyy} " +
                $"({conflict.StartLocal:HH:mm}–{conflict.EndLocal:HH:mm}) would fall outside the new hours.";
            return RedirectToPage();
        }

        row.OpenTime = open;
        row.CloseTime = close;
        row.IsClosed = isClosed;
        await db.SaveChangesAsync();

        StatusMessage = $"{row.DayOfWeek} hours updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddBlockAsync()
    {
        var start = DateTime.SpecifyKind(Block.StartLocal, DateTimeKind.Unspecified);
        var end = DateTime.SpecifyKind(Block.EndLocal, DateTimeKind.Unspecified);

        if (!ModelState.IsValid || end <= start)
        {
            ErrorMessage = "Please enter a valid blocked period with an end time after the start time.";
            return RedirectToPage();
        }

        if (end <= clock.NowLocal)
        {
            ErrorMessage = "The blocked period must end in the future.";
            return RedirectToPage();
        }

        var conflictingBooking = await db.Bookings.AsNoTracking()
            .Where(x =>
                (x.Status == BookingStatus.Pending || x.Status == BookingStatus.Confirmed) &&
                x.StartLocal < end &&
                x.EndLocal > start)
            .OrderBy(x => x.StartLocal)
            .Select(x => new { x.ReferenceNumber, x.StartLocal, x.EndLocal })
            .FirstOrDefaultAsync();

        if (conflictingBooking is not null)
        {
            ErrorMessage =
                $"That period overlaps active booking {conflictingBooking.ReferenceNumber} on " +
                $"{conflictingBooking.StartLocal:dd MMM yyyy} at {conflictingBooking.StartLocal:HH:mm}. " +
                "Cancel or reschedule the booking before blocking its time.";
            return RedirectToPage();
        }

        var overlappingBlock = await db.BlockedPeriods.AsNoTracking().AnyAsync(x =>
            x.StartLocal < end && x.EndLocal > start);

        if (overlappingBlock)
        {
            ErrorMessage = "That time is already covered by another blocked period.";
            return RedirectToPage();
        }

        db.BlockedPeriods.Add(new BlockedPeriod
        {
            StartLocal = start,
            EndLocal = end,
            Reason = Block.Reason.Trim(),
            CreatedUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        StatusMessage = "Unavailable time added. Customers will no longer be able to book inside that period.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteBlockAsync(int id)
    {
        var row = await db.BlockedPeriods.FirstOrDefaultAsync(x => x.Id == id);
        if (row is not null)
        {
            db.BlockedPeriods.Remove(row);
            await db.SaveChangesAsync();
            StatusMessage = "Blocked period removed.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Hours = await db.BusinessHours.AsNoTracking()
            .OrderBy(x => (int)x.DayOfWeek)
            .ToListAsync();

        var cutoff = clock.NowLocal.AddDays(-1);
        BlockedPeriods = await db.BlockedPeriods.AsNoTracking()
            .Where(x => x.EndLocal >= cutoff)
            .OrderBy(x => x.StartLocal)
            .Take(100)
            .ToListAsync();
    }
}
