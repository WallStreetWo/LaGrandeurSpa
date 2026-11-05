using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Pages.Account;

[EnableRateLimiting("auth")]
public sealed class LoginModel(ApplicationDbContext db, IPasswordHasher<AppUser> passwordHasher) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public string? ErrorMessage { get; set; }

    public sealed class InputModel
    {
        [Required, EmailAddress, StringLength(220)]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), StringLength(100)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
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

        var email = Input.Email.Trim().ToLowerInvariant();
        var user = await db.AppUsers.FirstOrDefaultAsync(x => x.Email == email);

        // Keep the external error deliberately generic so an attacker cannot use
        // the login form to discover which customer email addresses are registered.
        if (user is null || !user.IsActive)
        {
            ErrorMessage = "Incorrect email address or password.";
            return Page();
        }

        var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, Input.Password);
        if (verify == PasswordVerificationResult.Failed)
        {
            ErrorMessage = "Incorrect email address or password.";
            return Page();
        }

        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, Input.Password);
            await db.SaveChangesAsync();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = Input.RememberMe,
                ExpiresUtc = Input.RememberMe
                    ? DateTimeOffset.UtcNow.AddDays(30)
                    : DateTimeOffset.UtcNow.AddHours(4),
                AllowRefresh = true
            });

        return user.Role == AppUserRole.Admin
            ? RedirectToPage("/Admin/Dashboard/Index")
            : RedirectToPage("/Index");
    }
}
