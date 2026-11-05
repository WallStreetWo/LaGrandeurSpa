using System.Security.Claims;
using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using LaGrandeurSpa.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LaGrandeurSpa.Web.Pages.Bookings;

[Authorize]
public sealed class InvoiceModel(
    ApplicationDbContext db,
    ISpaClock clock,
    IOptions<BusinessSettings> businessOptions) : PageModel
{
    private readonly BusinessSettings business = businessOptions.Value;

    public Booking Booking { get; private set; } = default!;
    public DateTime InvoiceIssuedLocal { get; private set; }
    public bool IsAdmin => User.IsInRole(nameof(AppUserRole.Admin));
    public string BusinessName => business.Name;
    public string BusinessTagline => business.Tagline;
    public string BusinessAddress => business.Address;
    public string BusinessPhone => business.CellPhone;
    public string BankName => business.BankName;
    public string BankAccountNumber => business.BankAccountNumber;
    public string MapsUrl
    {
        get
        {
            var query = BusinessAddress.Contains("South Africa", StringComparison.OrdinalIgnoreCase)
                ? BusinessAddress
                : $"{BusinessAddress}, South Africa";
            return $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(query)}";
        }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (id <= 0)
            return NotFound();

        var booking = await db.Bookings.AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.SpaService)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (booking is null)
            return NotFound();

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId))
            return Forbid();

        if (!IsAdmin && booking.CustomerId != currentUserId)
            return Forbid();

        Booking = booking;
        var issuedUtc = booking.InvoiceIssuedUtc == default
            ? booking.CreatedUtc
            : booking.InvoiceIssuedUtc;
        InvoiceIssuedLocal = clock.ToLocal(issuedUtc);

        return Page();
    }
}
