using LaGrandeurSpa.Web.Models;

namespace LaGrandeurSpa.Web.Services;

public interface INotificationService
{
    Task NotifyAsync(int userId, string title, string message, NotificationType type = NotificationType.Info, string? url = null, int? bookingId = null);
    Task NotifyAdminsAsync(string title, string message, NotificationType type = NotificationType.Info, string? url = null, int? bookingId = null);
    Task MarkReadAsync(int userId, int notificationId);
    Task MarkAllReadAsync(int userId);
}
