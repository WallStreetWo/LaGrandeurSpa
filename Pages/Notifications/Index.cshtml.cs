using System.Security.Claims;
using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using LaGrandeurSpa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Pages.Notifications;

public sealed class IndexModel(
    ApplicationDbContext db,
    INotificationService notifications,
    ISpaClock clock) : PageModel
{
    public List<Notification> Items { get; set; } = [];

    public async Task OnGetAsync()
    {
        var userId = GetUserId();
        Items = await db.Notifications.AsNoTracking()
            .Where(x => x.AppUserId == userId)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(100)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostReadAsync(int id)
    {
        var userId = GetUserId();
        var item = await db.Notifications.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.AppUserId == userId);

        if (item is null)
            return RedirectToPage();

        await notifications.MarkReadAsync(userId, id);
        return !string.IsNullOrWhiteSpace(item.Url) && Url.IsLocalUrl(item.Url)
            ? LocalRedirect(item.Url)
            : RedirectToPage();
    }

    public async Task<IActionResult> OnPostReadAllAsync()
    {
        await notifications.MarkAllReadAsync(GetUserId());
        return RedirectToPage();
    }

    public DateTime CreatedLocal(DateTime createdUtc) => clock.ToLocal(createdUtc);

    private int GetUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("The authenticated user identifier is invalid.");
}
