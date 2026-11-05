namespace LaGrandeurSpa.Web.Models;

public sealed class BlockedPeriod
{
    public int Id { get; set; }
    public DateTime StartLocal { get; set; }
    public DateTime EndLocal { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
