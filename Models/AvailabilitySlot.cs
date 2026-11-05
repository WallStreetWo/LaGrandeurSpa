namespace LaGrandeurSpa.Web.Models;

public sealed record AvailabilitySlot(DateTime StartLocal, DateTime EndLocal)
{
    public string Label => $"{StartLocal:HH:mm} – {EndLocal:HH:mm}";
    public string Value => StartLocal.ToString("HH:mm");
}
