using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using LaGrandeurSpa.Web.Services.Emails;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Services;

public sealed class BookingReminderHostedService(
    IServiceScopeFactory scopeFactory,
    ISpaClock clock,
    IConfiguration configuration,
    ILogger<BookingReminderHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Booking reminder processing failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }

    private async Task ProcessRemindersAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var now = clock.NowLocal;
        var dayBeforeTime = ParseTime(
            configuration["EmailReminders:DayBeforeSendAfter"],
            new TimeSpan(9, 0, 0));
        var sameDayTime = ParseTime(
            configuration["EmailReminders:SameDaySendAfter"],
            new TimeSpan(7, 0, 0));

        if (now.TimeOfDay >= dayBeforeTime)
        {
            await SendForDateAsync(
                db,
                email,
                notifications,
                now.Date.AddDays(1),
                "Appointment reminder – tomorrow 🌿",
                "Your La Grandeur Spa appointment is tomorrow",
                dayBefore: true,
                now,
                cancellationToken);
        }

        if (now.TimeOfDay >= sameDayTime)
        {
            await SendForDateAsync(
                db,
                email,
                notifications,
                now.Date,
                "Appointment reminder – today 🌿",
                "Your La Grandeur Spa appointment is today",
                dayBefore: false,
                now,
                cancellationToken);
        }
    }

    private async Task SendForDateAsync(
        ApplicationDbContext db,
        IEmailSender email,
        INotificationService notifications,
        DateTime date,
        string notificationTitle,
        string emailSubject,
        bool dayBefore,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var endDate = date.AddDays(1);

        var bookings = await db.Bookings.AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.SpaService)
            .Where(x =>
                x.Status == BookingStatus.Confirmed &&
                x.StartLocal >= date &&
                x.StartLocal < endDate)
            .OrderBy(x => x.StartLocal)
            .ToListAsync(cancellationToken);

        foreach (var booking in bookings)
        {
            if (booking.StartLocal <= now)
                continue;

            var alreadySent = await db.Notifications.AsNoTracking().AnyAsync(x =>
                x.AppUserId == booking.CustomerId &&
                x.BookingId == booking.Id &&
                x.Title == notificationTitle,
                cancellationToken);

            if (alreadySent)
                continue;

            // The in-app reminder is independent from SMTP. It is also our durable
            // marker, so a successful reminder is not duplicated every 10 minutes.
            try
            {
                await notifications.NotifyAsync(
                    booking.CustomerId,
                    notificationTitle,
                    dayBefore
                        ? $"Reminder: your {booking.BookedServiceName} appointment is tomorrow at {booking.StartLocal:HH:mm}."
                        : $"Reminder: your {booking.BookedServiceName} appointment is today at {booking.StartLocal:HH:mm}.",
                    NotificationType.Info,
                    "/Bookings/Index",
                    booking.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not create reminder notification for booking {BookingId}.", booking.Id);
                continue;
            }

            if (email.IsEnabled && !string.IsNullOrWhiteSpace(booking.Customer.Email))
            {
                try
                {
                    var body = dayBefore
                        ? BookingEmailTemplates.DayBeforeReminder(booking)
                        : BookingEmailTemplates.SameDayReminder(booking);

                    await email.SendAsync(
                        booking.Customer.Email,
                        emailSubject,
                        body,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not send reminder email for booking {BookingId}.", booking.Id);
                }
            }

            logger.LogInformation(
                "{ReminderType} reminder processed for booking {BookingReference}.",
                dayBefore ? "Day-before" : "Same-day",
                booking.ReferenceNumber);
        }
    }

    private static TimeSpan ParseTime(string? value, TimeSpan fallback) =>
        TimeSpan.TryParse(value, out var result) &&
        result >= TimeSpan.Zero &&
        result < TimeSpan.FromDays(1)
            ? result
            : fallback;
}
