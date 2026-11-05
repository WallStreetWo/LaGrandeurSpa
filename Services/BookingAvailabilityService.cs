using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Services;

public sealed class BookingAvailabilityService(
    ApplicationDbContext db,
    IConfiguration configuration,
    ISpaClock clock) : IBookingAvailabilityService
{
    private int SlotInterval => Math.Max(5, configuration.GetValue<int?>("Business:SlotIntervalMinutes") ?? 15);

    public IReadOnlyList<DateTime> BuildReservationSlots(DateTime startLocal, DateTime endLocal)
    {
        var slots = new List<DateTime>();
        for (var cursor = startLocal; cursor < endLocal; cursor = cursor.AddMinutes(SlotInterval))
            slots.Add(DateTime.SpecifyKind(cursor, DateTimeKind.Unspecified));
        return slots;
    }

    public async Task<IReadOnlyList<AvailabilitySlot>> GetAvailableSlotsAsync(
        DateTime date,
        SpaService service,
        int? excludeBookingId = null,
        CancellationToken cancellationToken = default)
    {
        date = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);

        if (service.DurationMinutes <= 0)
            return [];

        var hours = await db.BusinessHours.AsNoTracking()
            .FirstOrDefaultAsync(x => x.DayOfWeek == date.DayOfWeek, cancellationToken);

        if (hours is null || hours.IsClosed || hours.CloseTime <= hours.OpenTime)
            return [];

        var open = date.Add(hours.OpenTime);
        var close = date.Add(hours.CloseTime);
        var duration = TimeSpan.FromMinutes(service.DurationMinutes);
        var now = clock.NowLocal;

        var blockedPeriods = await db.BlockedPeriods.AsNoTracking()
            .Where(x => x.StartLocal < close && x.EndLocal > open)
            .Select(x => new { x.StartLocal, x.EndLocal })
            .ToListAsync(cancellationToken);

        var occupiedBookings = await db.Bookings.AsNoTracking()
            .Where(x =>
                x.Status != BookingStatus.Cancelled &&
                x.Status != BookingStatus.Declined &&
                (!excludeBookingId.HasValue || x.Id != excludeBookingId.Value) &&
                x.StartLocal < close &&
                x.EndLocal > open)
            .Select(x => new { x.StartLocal, x.EndLocal })
            .ToListAsync(cancellationToken);

        var result = new List<AvailabilitySlot>();
        for (var start = open; start.Add(duration) <= close; start = start.AddMinutes(SlotInterval))
        {
            var end = start.Add(duration);
            if (start <= now)
                continue;

            if (blockedPeriods.Any(x => x.StartLocal < end && x.EndLocal > start))
                continue;

            if (!occupiedBookings.Any(x => x.StartLocal < end && x.EndLocal > start))
                result.Add(new AvailabilitySlot(start, end));
        }

        return result;
    }

    public async Task<bool> IsAvailableAsync(
        DateTime startLocal,
        DateTime endLocal,
        int? excludeBookingId = null,
        CancellationToken cancellationToken = default)
    {
        startLocal = DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified);
        endLocal = DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified);

        if (endLocal <= startLocal || startLocal <= clock.NowLocal)
            return false;

        if (startLocal.Date != endLocal.AddTicks(-1).Date)
            return false;

        var hours = await db.BusinessHours.AsNoTracking()
            .FirstOrDefaultAsync(x => x.DayOfWeek == startLocal.DayOfWeek, cancellationToken);

        if (hours is null || hours.IsClosed || hours.CloseTime <= hours.OpenTime)
            return false;

        var day = startLocal.Date;
        var open = day.Add(hours.OpenTime);
        var close = day.Add(hours.CloseTime);
        if (startLocal < open || endLocal > close)
            return false;

        var offsetTicks = (startLocal - open).Ticks;
        var intervalTicks = TimeSpan.FromMinutes(SlotInterval).Ticks;
        if (offsetTicks < 0 || offsetTicks % intervalTicks != 0)
            return false;

        if (await db.BlockedPeriods.AsNoTracking().AnyAsync(x =>
                x.StartLocal < endLocal && x.EndLocal > startLocal, cancellationToken))
            return false;

        if (await db.Bookings.AsNoTracking().AnyAsync(x =>
                x.Status != BookingStatus.Cancelled &&
                x.Status != BookingStatus.Declined &&
                (!excludeBookingId.HasValue || x.Id != excludeBookingId.Value) &&
                x.StartLocal < endLocal &&
                x.EndLocal > startLocal,
                cancellationToken))
            return false;

        var slotStarts = BuildReservationSlots(startLocal, endLocal);
        return !await db.BookingSlots.AsNoTracking()
            .AnyAsync(x =>
                (!excludeBookingId.HasValue || x.BookingId != excludeBookingId.Value) &&
                slotStarts.Contains(x.SlotStartLocal),
                cancellationToken);
    }
}
