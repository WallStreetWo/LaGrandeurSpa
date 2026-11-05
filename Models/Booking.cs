using System.ComponentModel.DataAnnotations.Schema;

namespace LaGrandeurSpa.Web.Models;

public enum BookingStatus
{
    Pending = 0,
    Confirmed = 1,
    Completed = 2,
    Cancelled = 3,
    Declined = 4
}

public sealed class Booking
{
    public int Id { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public AppUser Customer { get; set; } = null!;
    public int SpaServiceId { get; set; }
    public SpaService SpaService { get; set; } = null!;
    public DateTime StartLocal { get; set; }
    public DateTime EndLocal { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public string? CustomerNote { get; set; }
    public string? AdminNote { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedUtc { get; set; }
    public DateTime? CancelledUtc { get; set; }

    // These fields are the booking snapshot used by invoices/history.
    // Normal service edits never rewrite old bookings; an explicit customer booking update
    // intentionally refreshes the snapshot to the newly selected treatment.
    public string InvoiceNumber { get; set; } = string.Empty;
    public string InvoiceServiceName { get; set; } = string.Empty;
    public int InvoiceDurationMinutes { get; set; }
    public decimal InvoiceAmount { get; set; }
    public bool InvoiceIncludesHerbalTea { get; set; }
    public DateTime InvoiceIssuedUtc { get; set; }

    // SQL Server rowversion prevents two simultaneous booking actions from silently
    // overwriting each other (for example, confirm vs customer cancellation).
    public byte[] RowVersion { get; set; } = [];

    public int? ActionedById { get; set; }
    public AppUser? ActionedBy { get; set; }
    public ICollection<BookingSlot> Slots { get; set; } = new List<BookingSlot>();

    [NotMapped]
    public bool HasBookingSnapshot =>
        InvoiceIssuedUtc != default ||
        !string.IsNullOrWhiteSpace(InvoiceServiceName) ||
        InvoiceDurationMinutes > 0;

    [NotMapped]
    public string BookedServiceName =>
        !string.IsNullOrWhiteSpace(InvoiceServiceName)
            ? InvoiceServiceName
            : SpaService?.Name ?? "Treatment";

    [NotMapped]
    public int BookedDurationMinutes =>
        InvoiceDurationMinutes > 0
            ? InvoiceDurationMinutes
            : SpaService?.DurationMinutes ?? Math.Max(0, (int)(EndLocal - StartLocal).TotalMinutes);

    [NotMapped]
    public decimal BookedAmount =>
        HasBookingSnapshot
            ? InvoiceAmount
            : SpaService?.Price ?? 0m;

    [NotMapped]
    public bool BookedIncludesHerbalTea =>
        HasBookingSnapshot
            ? InvoiceIncludesHerbalTea
            : SpaService?.IncludesHerbalTea ?? false;
}
