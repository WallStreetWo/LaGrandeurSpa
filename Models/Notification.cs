namespace LaGrandeurSpa.Web.Models;

public enum NotificationType
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Danger = 3
}

public sealed class Notification
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUser AppUser { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Url { get; set; }
    public NotificationType Type { get; set; } = NotificationType.Info;
    public bool IsRead { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }
}
