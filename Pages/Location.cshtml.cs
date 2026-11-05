using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using LaGrandeurSpa.Web.Services;

namespace LaGrandeurSpa.Web.Pages;

public sealed class LocationModel(IOptions<BusinessSettings> businessOptions) : PageModel
{
    private readonly BusinessSettings business = businessOptions.Value;

    public string BusinessName => business.Name;
    public string Address => business.Address;
    public string CellPhone => business.CellPhone;
    private string MapAddress => Address.Contains("South Africa", StringComparison.OrdinalIgnoreCase)
        ? Address
        : $"{Address}, South Africa";
    public string MapEmbedUrl =>
        $"https://www.google.com/maps?q={Uri.EscapeDataString(MapAddress)}&output=embed";
    public string MapsSearchUrl =>
        $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(MapAddress)}";
    public string MapsDirectionsUrl =>
        $"https://www.google.com/maps/dir/?api=1&destination={Uri.EscapeDataString(MapAddress)}&travelmode=driving";
    public string MapDestination => MapAddress;
    public string TelephoneUrl =>
        $"tel:{new string((CellPhone ?? string.Empty).Where(char.IsDigit).ToArray())}";

    public void OnGet()
    {
    }
}
