using System.ComponentModel.DataAnnotations;
using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using LaGrandeurSpa.Web.Services;
using LaGrandeurSpa.Web.Services.Emails;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LaGrandeurSpa.Web.Pages.Account;

[EnableRateLimiting("auth")]
public sealed class ResetPasswordModel(
    ApplicationDbContext db,
    IPasswordHasher<AppUser> passwordHasher,
    IPasswordResetTokenService resetTokens,
    IEmailSender email,
    INotificationService notifications,
    IOptions<BusinessSettings> businessOptions,
    ILogger<ResetPasswordModel> logger) : PageModel
{
    private readonly BusinessSettings business = businessOptions.Value;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool TokenIsValid { get; private set; }

    public sealed class InputModel
    {
        [Required]
        public string Token { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required, StringLength(100), DataType(DataType.Password), Compare(nameof(NewPassword))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(string? token)
    {
        Input.Token = token ?? string.Empty;
        TokenIsValid = await IsCurrentTokenValidAsync(Input.Token);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        TokenIsValid = await IsCurrentTokenValidAsync(Input.Token);
        if (!TokenIsValid)
        {
            ModelState.AddModelError(string.Empty, "This password-reset link is invalid, expired, or has already been used.");
            return Page();
        }

        if (!ModelState.IsValid)
            return Page();

        if (!resetTokens.TryReadToken(Input.Token, out var userId, out var fingerprint))
        {
            TokenIsValid = false;
            ModelState.AddModelError(string.Empty, "This password-reset link is invalid or expired.");
            return Page();
        }

        var user = await db.AppUsers.FirstOrDefaultAsync(x => x.Id == userId && x.IsActive);
        if (user is null || !string.Equals(
                resetTokens.CreatePasswordFingerprint(user.PasswordHash),
                fingerprint,
                StringComparison.Ordinal))
        {
            TokenIsValid = false;
            ModelState.AddModelError(string.Empty, "This password-reset link is invalid, expired, or has already been used.");
            return Page();
        }

        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, Input.NewPassword) != PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError("Input.NewPassword", "Choose a new password that is different from your current password.");
            return Page();
        }

        user.PasswordHash = passwordHasher.HashPassword(user, Input.NewPassword);
        await db.SaveChangesAsync();

        try
        {
            await notifications.NotifyAsync(
                user.Id,
                "Password changed",
                "Your La Grandeur Spa account password was changed successfully.",
                NotificationType.Info,
                "/Account/ChangePassword");
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            logger.LogError(ex, "Password changed for user {UserId}, but in-app notification failed.", user.Id);
        }

        if (email.IsEnabled)
        {
            try
            {
                await email.SendAsync(
                    user.Email,
                    "Your La Grandeur Spa password was changed",
                    AccountEmailTemplates.PasswordChanged(user, business.CellPhone),
                    HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Password changed for user {UserId}, but security email failed.", user.Id);
            }
        }

        TempData["StatusMessage"] = "Your password has been changed successfully. You can now sign in with your new password.";
        return RedirectToPage("/Account/Login");
    }

    private async Task<bool> IsCurrentTokenValidAsync(string token)
    {
        if (!resetTokens.TryReadToken(token, out var userId, out var fingerprint))
            return false;

        var user = await db.AppUsers.AsNoTracking()
            .Where(x => x.Id == userId && x.IsActive)
            .Select(x => new { x.PasswordHash })
            .FirstOrDefaultAsync();

        return user is not null && string.Equals(
            resetTokens.CreatePasswordFingerprint(user.PasswordHash),
            fingerprint,
            StringComparison.Ordinal);
    }
}
