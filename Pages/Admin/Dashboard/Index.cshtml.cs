using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using LaGrandeurSpa.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Pages.Admin.Dashboard;

public sealed class IndexModel(ApplicationDbContext db, ISpaClock clock) : PageModel
{
    public int PendingCount { get; set; }
    public int TodayCount { get; set; }
    public int ConfirmedThisWeek { get; set; }
    public int CustomerCount { get; set; }
    public List<Booking> TodayBookings { get; set; } = [];
    public List<Booking> MonthBookings { get; set; } = [];
    public int Year { get; set; }
    public int Month { get; set; }
    public string AdminName { get; set; } = "Kea";
    public DateTime TodayLocal => clock.TodayLocal;

    public async Task OnGetAsync(int? year, int? month)
    {
        AdminName = string.IsNullOrWhiteSpace(User.Identity?.Name) ? "Kea" : User.Identity.Name!;

        var now = clock.NowLocal;
        Year = year is >= 2020 and <= 2100 ? year.Value : now.Year;
        Month = month is >= 1 and <= 12 ? month.Value : now.Month;

        var todayStart = now.Date;
        var tomorrow = todayStart.AddDays(1);
        var weekEnd = todayStart.AddDays(7);
        var monthStart = new DateTime(Year, Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        PendingCount = await db.Bookings.CountAsync(x => x.Status == BookingStatus.Pending);
        TodayCount = await db.Bookings.CountAsync(x =>
            x.StartLocal >= todayStart &&
            x.StartLocal < tomorrow &&
            x.Status != BookingStatus.Cancelled &&
            x.Status != BookingStatus.Declined);
        ConfirmedThisWeek = await db.Bookings.CountAsync(x =>
            x.StartLocal >= todayStart &&
            x.StartLocal < weekEnd &&
            x.Status == BookingStatus.Confirmed);
        CustomerCount = await db.AppUsers.CountAsync(x => x.Role == AppUserRole.Customer && x.IsActive);

        TodayBookings = await db.Bookings.AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.SpaService)
            .Where(x =>
                x.StartLocal >= todayStart &&
                x.StartLocal < tomorrow &&
                x.Status != BookingStatus.Cancelled &&
                x.Status != BookingStatus.Declined)
            .OrderBy(x => x.StartLocal)
            .ToListAsync();

        MonthBookings = await db.Bookings.AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.SpaService)
            .Where(x =>
                x.StartLocal >= monthStart &&
                x.StartLocal < monthEnd &&
                x.Status != BookingStatus.Cancelled &&
                x.Status != BookingStatus.Declined)
            .OrderBy(x => x.StartLocal)
            .ToListAsync();
    }

    public IEnumerable<Booking> BookingsForDay(DateTime date) =>
        MonthBookings
            .Where(x => x.StartLocal.Date == date.Date)
            .OrderBy(x => x.StartLocal);
}
