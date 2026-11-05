namespace LaGrandeurSpa.Web.Models;

public sealed class SpaService
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public string ColorHex { get; set; } = "#68704A";
    public bool IncludesHerbalTea { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public string DisplayName => $"{Name} · {DurationMinutes} min · R{Price:0}";
}
