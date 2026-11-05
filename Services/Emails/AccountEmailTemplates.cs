using System.Net;
using LaGrandeurSpa.Web.Models;

namespace LaGrandeurSpa.Web.Services.Emails;

public static class AccountEmailTemplates
{
    public static string AccountCreated(AppUser user, string loginUrl) =>
        Wrap(
            "Welcome to La Grandeur Spa 🌿",
            $"""
            <p>Hello {H(user.FullName)},</p>
            <p>Your La Grandeur Spa account has been created successfully.</p>
            <p>You can now browse live availability, make bookings, view invoices and manage your appointments online.</p>
            <p style="margin:28px 0;"><a href="{H(loginUrl)}" style="background:#666b48;color:#fff;text-decoration:none;padding:12px 20px;border-radius:10px;display:inline-block;font-weight:700;">Sign in to your account</a></p>
            <p>If you did not create this account, please contact La Grandeur Spa.</p>
            """);

    public static string PasswordReset(AppUser user, string resetUrl, int lifetimeMinutes) =>
        Wrap(
            "Reset your password",
            $"""
            <p>Hello {H(user.FullName)},</p>
            <p>We received a request to reset the password for your La Grandeur Spa account.</p>
            <p style="margin:28px 0;"><a href="{H(resetUrl)}" style="background:#666b48;color:#fff;text-decoration:none;padding:12px 20px;border-radius:10px;display:inline-block;font-weight:700;">Reset password</a></p>
            <p>This secure link expires in {lifetimeMinutes} minutes and becomes invalid as soon as your password changes.</p>
            <p>If you did not request a password reset, you can safely ignore this email.</p>
            """);

    public static string PasswordChanged(AppUser user, string contactPhone) =>
        Wrap(
            "Your password was changed",
            $"""
            <p>Hello {H(user.FullName)},</p>
            <p>The password for your La Grandeur Spa account was changed successfully.</p>
            <p>If you made this change, no further action is required.</p>
            <p>If this was <strong>not</strong> you, please contact La Grandeur Spa immediately{Phone(contactPhone)}.</p>
            """);

    private static string Phone(string? phone) =>
        string.IsNullOrWhiteSpace(phone)
            ? string.Empty
            : $" on <strong>{H(phone)}</strong>";

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
