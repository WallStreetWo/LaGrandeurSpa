namespace LaGrandeurSpa.Web.Models;

public enum AppUserRole
{
    Customer = 0,
    Admin = 1
}

public sealed class AppUser
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? MobileNumber { get; set; }
    public AppUserRole Role { get; set; } = AppUserRole.Customer;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
