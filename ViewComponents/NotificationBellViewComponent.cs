using System.Security.Claims;
using LaGrandeurSpa.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.ViewComponents;

public sealed class NotificationBellViewComponent(ApplicationDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var unreadCount = 0;
        var idClaim = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(idClaim, out var userId))
        {
            unreadCount = await db.Notifications.AsNoTracking()
                .CountAsync(x => x.AppUserId == userId && !x.IsRead);
        }
        return View(unreadCount);
    }
}
