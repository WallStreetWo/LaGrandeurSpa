using System.Net;
using LaGrandeurSpa.Web.Models;

namespace LaGrandeurSpa.Web.Services.Emails;

public static class BookingEmailTemplates
{
    public static string BookingReceived(Booking booking) =>
        Wrap(
            "Booking received 🌿",
            $"""
            <p>Hello {H(booking.Customer.FullName)},</p>
            <p>Thank you for booking with <strong>La Grandeur Spa</strong>.</p>
            <p>We have received your booking and Kea will confirm your appointment shortly.</p>
            {BookingDetails(booking)}
            <p>You will receive another notification once your appointment has been confirmed.</p>
            """);

    public static string BookingConfirmed(Booking booking) =>
        Wrap(
            "Your booking is confirmed 🌿",
            $"""
            <p>Hello {H(booking.Customer.FullName)},</p>
            <p>Your appointment at <strong>La Grandeur Spa</strong> has been confirmed.</p>
            {BookingDetails(booking)}
            <p>We look forward to welcoming you.</p>
            """);

    public static string BookingUpdated(Booking booking, bool requiresConfirmation) =>
        Wrap(
            "Your booking was updated 🌿",
            $"""
            <p>Hello {H(booking.Customer.FullName)},</p>
            <p>Your La Grandeur Spa booking has been updated successfully.</p>
            {BookingDetails(booking)}
            {(requiresConfirmation ? "<p><strong>Your revised appointment is awaiting Kea’s confirmation.</strong> We’ll notify you as soon as it is confirmed.</p>" : "<p>Your current confirmation status remains unchanged.</p>")}
            <p>You can view the current invoice and booking status under <strong>My Bookings</strong>.</p>
            """);

    public static string BookingUpdatedForAdmin(Booking booking, bool requiresConfirmation) =>
        Wrap(
            "A customer updated a booking",
            $"""
            <p>Hello Kea,</p>
            <p><strong>{H(booking.Customer.FullName)}</strong> has updated booking <strong>{H(booking.ReferenceNumber)}</strong>.</p>
            {BookingDetails(booking)}
            {(requiresConfirmation ? "<p><strong>Action required:</strong> the revised appointment has been returned to Pending and needs your confirmation.</p>" : "<p>The booking remains in its current confirmation state; no re-confirmation is required.</p>")}
            <p><strong>Customer:</strong> {H(booking.Customer.FullName)}<br>
            <strong>Email:</strong> {H(booking.Customer.Email)}
            {CustomerMobile(booking)}</p>
            <p>Please review the booking in the La Grandeur Spa admin area.</p>
            """);

    public static string BookingDeclined(Booking booking) =>
        Wrap(
            "Booking update",
            $"""
            <p>Hello {H(booking.Customer.FullName)},</p>
            <p>Unfortunately, La Grandeur Spa could not confirm this booking.</p>
            {BookingDetails(booking)}
            {AdminNote(booking)}
            <p>Please return to the booking calendar to choose another available time.</p>
            """);

    public static string BookingCancelledBySpa(Booking booking) =>
        Wrap(
            "Your appointment was cancelled",
            $"""
            <p>Hello {H(booking.Customer.FullName)},</p>
            <p>La Grandeur Spa has cancelled the appointment below.</p>
            {BookingDetails(booking)}
            {AdminNote(booking)}
            <p>Please contact the spa or book another available time when convenient.</p>
            """);

    public static string BookingCancelledByCustomer(Booking booking) =>
        Wrap(
            "Cancellation confirmed",
            $"""
            <p>Hello {H(booking.Customer.FullName)},</p>
            <p>Your cancellation has been recorded successfully.</p>
            {BookingDetails(booking)}
            <p>The reserved time has been released.</p>
            """);

    public static string DayBeforeReminder(Booking booking) =>
        Wrap(
            "Your appointment is tomorrow 🌿",
            $"""
            <p>Hello {H(booking.Customer.FullName)},</p>
            <p>This is a friendly reminder that your La Grandeur Spa appointment is <strong>tomorrow</strong>.</p>
            {BookingDetails(booking)}
            <p>We look forward to helping you relax, rejuvenate and renew.</p>
            """);

    public static string SameDayReminder(Booking booking) =>
        Wrap(
            "Your appointment is today 🌿",
            $"""
            <p>Hello {H(booking.Customer.FullName)},</p>
            <p>We're looking forward to seeing you at La Grandeur Spa <strong>today</strong>.</p>
            {BookingDetails(booking)}
            <p>See you soon 🌿</p>
            """);

    private static string BookingDetails(Booking booking)
    {
        var tea = booking.BookedIncludesHerbalTea
            ? """
              <tr>
                  <td style="padding:8px 0;color:#777;">Special</td>
                  <td style="padding:8px 0;"><strong>Complimentary herbal tea included</strong></td>
              </tr>
              """
            : string.Empty;

        return $"""
        <table style="width:100%;border-collapse:collapse;margin:24px 0;">
            <tr>
                <td style="padding:8px 0;color:#777;">Treatment</td>
                <td style="padding:8px 0;"><strong>{H(booking.BookedServiceName)}</strong></td>
            </tr>
            <tr>
                <td style="padding:8px 0;color:#777;">Date</td>
                <td style="padding:8px 0;"><strong>{booking.StartLocal:dddd, dd MMMM yyyy}</strong></td>
            </tr>
            <tr>
                <td style="padding:8px 0;color:#777;">Time</td>
                <td style="padding:8px 0;"><strong>{booking.StartLocal:HH:mm} – {booking.EndLocal:HH:mm}</strong></td>
            </tr>
            <tr>
                <td style="padding:8px 0;color:#777;">Duration</td>
                <td style="padding:8px 0;"><strong>{booking.BookedDurationMinutes} minutes</strong></td>
            </tr>
            <tr>
                <td style="padding:8px 0;color:#777;">Price</td>
                <td style="padding:8px 0;"><strong>R{booking.BookedAmount:0.00}</strong></td>
            </tr>
            <tr>
                <td style="padding:8px 0;color:#777;">Reference</td>
                <td style="padding:8px 0;"><strong>{H(booking.ReferenceNumber)}</strong></td>
            </tr>
            {tea}
        </table>
        """;
    }

    private static string CustomerMobile(Booking booking) =>
        string.IsNullOrWhiteSpace(booking.Customer.MobileNumber)
            ? string.Empty
            : $"<br><strong>Mobile:</strong> {H(booking.Customer.MobileNumber)}";

    private static string AdminNote(Booking booking) =>
        string.IsNullOrWhiteSpace(booking.AdminNote)
            ? string.Empty
            : $"<p><strong>Note from La Grandeur Spa:</strong> {H(booking.AdminNote)}</p>";

    private static string Wrap(string heading, string content) =>
        $"""
        <!DOCTYPE html>
        <html>
        <body style="margin:0;background:#faf1ea;font-family:Arial,sans-serif;color:#4f5038;">
            <div style="max-width:620px;margin:0 auto;padding:30px 18px;">
                <div style="background:#fffdf9;border:1px solid #dfd2c1;border-radius:18px;padding:32px;">
                    <div style="text-align:center;margin-bottom:28px;">
                        <div style="font-family:Georgia,serif;font-size:30px;color:#646445;">La Grandeur</div>
                        <div style="letter-spacing:7px;font-size:12px;color:#b39a72;">S P A</div>
                    </div>
                    <h2 style="font-family:Georgia,serif;color:#646445;">{heading}</h2>
                    {content}
                    <p style="margin-top:30px;color:#777;">Relax. Rejuvenate. Renew.</p>
                </div>
            </div>
        </body>
        </html>
        """;

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
