using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using LaGrandeurSpa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Pages.Admin.Services;

public sealed class IndexModel(ApplicationDbContext db, IConfiguration configuration) : PageModel
{
    private static readonly Regex HexColorRegex =
        new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public int SlotIntervalMinutes =>
        Math.Max(5, configuration.GetValue<int?>("Business:SlotIntervalMinutes") ?? 15);

    public int MinimumServiceDuration => Math.Max(15, SlotIntervalMinutes);

    public List<SpaService> Items { get; set; } = [];

    [BindProperty]
    public NewServiceInput NewService { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public sealed class NewServiceInput
    {
        [Required, StringLength(180, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Range(15, 480)]
        public int DurationMinutes { get; set; } = 60;

        [Range(typeof(decimal), "0", "100000")]
        public decimal Price { get; set; }

        [Required, StringLength(7)]
        public string ColorHex { get; set; } = "#68704A";

        public bool IncludesHerbalTea { get; set; }
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostUpdateAsync(
        int id,
        string name,
        int durationMinutes,
        decimal price,
        string colorHex,
        bool isActive,
        bool includesHerbalTea)
    {
        var item = await db.SpaServices.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null)
        {
            ErrorMessage = "That service no longer exists.";
            return RedirectToPage();
        }

        var validationError = ValidateService(name, durationMinutes, price, colorHex);
        if (validationError is not null)
        {
            ErrorMessage = validationError;
            return RedirectToPage();
        }

        item.Name = name.Trim();
        item.DurationMinutes = durationMinutes;
        item.Price = price;
        item.ColorHex = NormalizeColor(colorHex);
        item.IsActive = isActive;
        item.IncludesHerbalTea = includesHerbalTea;

        try
        {
            await db.SaveChangesAsync();
            StatusMessage = "Service updated. Existing bookings keep their original booked price and treatment details.";
        }
        catch (DbUpdateException ex) when (SqlServerErrorHelper.IsUniqueConstraintViolation(ex))
        {
            ErrorMessage = "A service with that name and duration already exists.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        var validationError = ValidateService(
            NewService.Name,
            NewService.DurationMinutes,
            NewService.Price,
            NewService.ColorHex);

        if (!ModelState.IsValid || validationError is not null)
        {
            ErrorMessage = validationError ?? "Please complete the new service details.";
            return RedirectToPage();
        }

        var nextSort = (await db.SpaServices.MaxAsync(x => (int?)x.SortOrder) ?? 0) + 10;
        db.SpaServices.Add(new SpaService
        {
            Name = NewService.Name.Trim(),
            DurationMinutes = NewService.DurationMinutes,
            Price = NewService.Price,
            ColorHex = NormalizeColor(NewService.ColorHex),
            IncludesHerbalTea = NewService.IncludesHerbalTea,
            IsActive = true,
            SortOrder = nextSort
        });

        try
        {
            await db.SaveChangesAsync();
            StatusMessage = "New treatment added.";
        }
        catch (DbUpdateException ex) when (SqlServerErrorHelper.IsUniqueConstraintViolation(ex))
        {
            ErrorMessage = "A service with that name and duration already exists.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync() =>
        Items = await db.SpaServices.AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();

    private string? ValidateService(string? name, int durationMinutes, decimal price, string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length is < 2 or > 180)
            return "Please enter a service name between 2 and 180 characters.";

        if (durationMinutes is < 15 or > 480)
            return "Service duration must be between 15 and 480 minutes.";

        if (durationMinutes % SlotIntervalMinutes != 0)
            return $"Service duration must be a multiple of the {SlotIntervalMinutes}-minute booking interval.";

        if (price is < 0 or > 100000)
            return "Service price must be between R0 and R100,000.";

        if (string.IsNullOrWhiteSpace(colorHex))
            return "Please choose a calendar colour.";

        var normalizedColor = NormalizeColor(colorHex);
        if (!HexColorRegex.IsMatch(normalizedColor))
            return "Please choose a valid six-digit calendar colour.";

        return null;
    }

    private static string NormalizeColor(string? value)
    {
        var color = string.IsNullOrWhiteSpace(value) ? "#68704A" : value.Trim();
        if (!color.StartsWith('#'))
            color = "#" + color;
        return color.ToUpperInvariant();
    }
}
