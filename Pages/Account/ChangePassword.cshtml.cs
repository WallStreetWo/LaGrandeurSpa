using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using LaGrandeurSpa.Web.Services;
using LaGrandeurSpa.Web.Services.Emails;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace LaGrandeurSpa.Web.Pages.Account;

[EnableRateLimiting("auth")]
public sealed class ChangePasswordModel(
    ApplicationDbContext db,
    IPasswordHasher<AppUser> passwordHasher,
    IEmailSender email,
    INotificationService notifications,
    IOptions<BusinessSettings> businessOptions,
    ILogger<ChangePasswordModel> logger) : PageModel
{
    private readonly BusinessSettings business = businessOptions.Value;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public sealed class InputModel
    {
        [Required, DataType(DataType.Password), StringLength(100)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 8)]
        public string NewPassword { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), StringLength(100), Compare(nameof(NewPassword))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var userId = GetUserId();
        var user = await db.AppUsers.FirstOrDefaultAsync(x => x.Id == userId && x.IsActive);
        if (user is null)
            return Challenge();

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, Input.CurrentPassword);
        if (verification == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError("Input.CurrentPassword", "Your current password is incorrect.");
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

        StatusMessage = "Your password has been changed successfully.";
        ModelState.Clear();
        Input = new InputModel();
        return Page();
    }

    private int GetUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("The authenticated user identifier is invalid.");
}
