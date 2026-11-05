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

namespace LaGrandeurSpa.Web.Pages.Account;

[EnableRateLimiting("auth")]
public sealed class RegisterModel(
    ApplicationDbContext db,
    IPasswordHasher<AppUser> passwordHasher,
    IEmailSender email,
    Microsoft.Extensions.Options.IOptions<BusinessSettings> businessOptions,
    ILogger<RegisterModel> logger) : PageModel
{
    private readonly BusinessSettings business = businessOptions.Value;
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public sealed class InputModel
    {
        [Required, StringLength(160, MinimumLength = 2)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(220)]
        public string Email { get; set; } = string.Empty;

        [Phone, StringLength(40)]
        public string? MobileNumber { get; set; }

        [Required, StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), StringLength(100), Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return User.IsInRole(nameof(AppUserRole.Admin))
                ? RedirectToPage("/Admin/Dashboard/Index")
                : RedirectToPage("/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var normalizedEmail = Input.Email.Trim().ToLowerInvariant();
        if (await db.AppUsers.AnyAsync(x => x.Email == normalizedEmail))
        {
            ModelState.AddModelError("Input.Email", "An account with this email address already exists.");
            return Page();
        }

        var user = new AppUser
        {
            FullName = Input.FullName.Trim(),
            Email = normalizedEmail,
            MobileNumber = string.IsNullOrWhiteSpace(Input.MobileNumber) ? null : Input.MobileNumber.Trim(),
            Role = AppUserRole.Customer,
            IsActive = true,
            CreatedUtc = DateTime.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, Input.Password);

        db.AppUsers.Add(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqlServerErrorHelper.IsUniqueConstraintViolation(ex))
        {
            // Handles the race where two registration requests for the same email
            // pass the pre-check together; the unique database index remains authoritative.
            db.ChangeTracker.Clear();
            ModelState.AddModelError("Input.Email", "An account with this email address already exists.");
            return Page();
        }

        var welcomeEmailSent = false;
        if (email.IsEnabled)
        {
            try
            {
                var relativeLoginUrl = Url.Page("/Account/Login");
                var baseUrl = !string.IsNullOrWhiteSpace(business.PublicBaseUrl)
                    ? business.PublicBaseUrl.TrimEnd('/')
                    : $"{Request.Scheme}://{Request.Host}";
                var loginUrl = $"{baseUrl}{relativeLoginUrl}";

                await email.SendAsync(
                    user.Email,
                    "Welcome to La Grandeur Spa – your account is ready",
                    AccountEmailTemplates.AccountCreated(user, loginUrl),
                    HttpContext.RequestAborted);
                welcomeEmailSent = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Account {UserId} was created, but the welcome email failed.", user.Id);
            }
        }

        StatusMessage = welcomeEmailSent
            ? "Your account has been created. We’ve also sent you a confirmation email."
            : "Your account has been created. You can sign in and book your treatment.";
        return RedirectToPage("/Account/Login");
    }
}
