using LaGrandeurSpa.Web.Models;

namespace LaGrandeurSpa.Web.Services;

public interface IBookingAvailabilityService
{
    Task<IReadOnlyList<AvailabilitySlot>> GetAvailableSlotsAsync(
        DateTime date,
        SpaService service,
        int? excludeBookingId = null,
        CancellationToken cancellationToken = default);

    Task<bool> IsAvailableAsync(
        DateTime startLocal,
        DateTime endLocal,
        int? excludeBookingId = null,
        CancellationToken cancellationToken = default);

    IReadOnlyList<DateTime> BuildReservationSlots(DateTime startLocal, DateTime endLocal);
}
