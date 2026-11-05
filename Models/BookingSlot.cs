namespace LaGrandeurSpa.Web.Models;

public sealed class BookingSlot
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public DateTime SlotStartLocal { get; set; }
}
