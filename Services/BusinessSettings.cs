namespace LaGrandeurSpa.Web.Services;

public sealed class BusinessSettings
{
    public string Name { get; set; } = "La Grandeur Spa";
    public string Tagline { get; set; } = "Relax. Rejuvenate. Renew.";
    public string TimeZone { get; set; } = "Africa/Johannesburg";
    public int SlotIntervalMinutes { get; set; } = 15;
    public int CancellationCutoffHours { get; set; }
    public string CellPhone { get; set; } = "";
    public string Address { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";
    public string BankName { get; set; } = "";
    public string BankAccountNumber { get; set; } = "";
}
