using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Services;

public sealed class NotificationService(ApplicationDbContext db) : INotificationService
{
    public async Task NotifyAsync(
        int userId,
        string title,
        string message,
        NotificationType type = NotificationType.Info,
        string? url = null,
        int? bookingId = null)
    {
        if (userId <= 0)
            return;

        title = Truncate((title ?? string.Empty).Trim(), 180);
        message = Truncate((message ?? string.Empty).Trim(), 1200);
        url = string.IsNullOrWhiteSpace(url) ? null : Truncate(url.Trim(), 400);

        if (title.Length == 0 || message.Length == 0)
            return;

        var since = DateTime.UtcNow.AddMinutes(-1);
        var duplicate = await db.Notifications.AsNoTracking().AnyAsync(x =>
            x.AppUserId == userId &&
            x.Title == title &&
            x.Message == message &&
            x.BookingId == bookingId &&
            x.CreatedUtc >= since);

        if (duplicate)
            return;

        db.Notifications.Add(new Notification
        {
            AppUserId = userId,
            Title = title,
            Message = message,
            Type = type,
            Url = url,
            BookingId = bookingId,
            CreatedUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    public async Task NotifyAdminsAsync(
        string title,
        string message,
        NotificationType type = NotificationType.Info,
        string? url = null,
        int? bookingId = null)
    {
        var adminIds = await db.AppUsers.AsNoTracking()
            .Where(x => x.IsActive && x.Role == AppUserRole.Admin)
            .Select(x => x.Id)
            .ToListAsync();

        foreach (var adminId in adminIds)
            await NotifyAsync(adminId, title, message, type, url, bookingId);
    }

    public async Task MarkReadAsync(int userId, int notificationId)
    {
        if (userId <= 0 || notificationId <= 0)
            return;

        await db.Notifications
            .Where(x => x.Id == notificationId && x.AppUserId == userId && !x.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsRead, true));
    }

    public async Task MarkAllReadAsync(int userId)
    {
        if (userId <= 0)
            return;

        await db.Notifications
            .Where(x => x.AppUserId == userId && !x.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsRead, true));
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
