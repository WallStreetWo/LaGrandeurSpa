using System.ComponentModel.DataAnnotations;
using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Services;
using LaGrandeurSpa.Web.Services.Emails;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LaGrandeurSpa.Web.Pages.Account;

[EnableRateLimiting("auth")]
public sealed class ForgotPasswordModel(
    ApplicationDbContext db,
    IPasswordResetTokenService resetTokens,
    IEmailSender email,
    IOptions<BusinessSettings> businessOptions,
    ILogger<ForgotPasswordModel> logger) : PageModel
{
    private readonly BusinessSettings business = businessOptions.Value;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool RequestSubmitted { get; private set; }

    public sealed class InputModel
    {
        [Required, EmailAddress, StringLength(220)]
        public string Email { get; set; } = string.Empty;
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Account/ChangePassword");

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var normalizedEmail = Input.Email.Trim().ToLowerInvariant();
        var user = await db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Email == normalizedEmail && x.IsActive);

        // Keep the external response identical whether or not the account exists.
        // This avoids turning the password-recovery form into an email-enumeration tool.
        if (user is not null && email.IsEnabled)
        {
            try
            {
                var token = resetTokens.CreateToken(user);
                var relativeUrl = Url.Page("/Account/ResetPassword", null, new { token });
                var baseUrl = !string.IsNullOrWhiteSpace(business.PublicBaseUrl)
                    ? business.PublicBaseUrl.TrimEnd('/')
                    : $"{Request.Scheme}://{Request.Host}";
                var resetUrl = $"{baseUrl}{relativeUrl}";

                await email.SendAsync(
                    user.Email,
                    "Reset your La Grandeur Spa password",
                    AccountEmailTemplates.PasswordReset(user, resetUrl, resetTokens.LifetimeMinutes),
                    HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Password-reset email failed for user {UserId}.", user.Id);
            }
        }
        else if (user is not null)
        {
            logger.LogWarning(
                "Password reset requested for user {UserId}, but SMTP is disabled or incomplete.",
                user.Id);
        }

        RequestSubmitted = true;
        ModelState.Clear();
        Input = new InputModel();
        return Page();
    }
}
