using LaGrandeurSpa.Web.Data;
using LaGrandeurSpa.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Services;

public sealed class DatabaseSeeder(
    ApplicationDbContext db,
    IPasswordHasher<AppUser> passwordHasher,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    public async Task SeedAsync()
    {
        if (!await db.SpaServices.AnyAsync())
        {
            db.SpaServices.AddRange(
                new SpaService { Name = "Back, Neck & Shoulder Massage", DurationMinutes = 30, Price = 300, ColorHex = "#7B8060", SortOrder = 10 },
                new SpaService { Name = "Back, Neck & Shoulder Massage", DurationMinutes = 45, Price = 450, ColorHex = "#8A8E6A", SortOrder = 20 },
                new SpaService { Name = "Swedish Full Body Massage", DurationMinutes = 60, Price = 550, ColorHex = "#B39A72", SortOrder = 30 },
                new SpaService { Name = "Swedish Full Body Massage", DurationMinutes = 90, Price = 750, ColorHex = "#C4A77D", IncludesHerbalTea = true, SortOrder = 40 },
                new SpaService { Name = "Deep Tissue Massage", DurationMinutes = 60, Price = 650, ColorHex = "#5F6545", SortOrder = 50 },
                new SpaService { Name = "Deep Tissue Massage", DurationMinutes = 90, Price = 850, ColorHex = "#4F553A", IncludesHerbalTea = true, SortOrder = 60 },
                new SpaService { Name = "Sports Massage", DurationMinutes = 60, Price = 650, ColorHex = "#8C7858", SortOrder = 70 },
                new SpaService { Name = "Sports Massage", DurationMinutes = 90, Price = 850, ColorHex = "#766548", IncludesHerbalTea = true, SortOrder = 80 },
                new SpaService { Name = "Foot Reflexology", DurationMinutes = 30, Price = 350, ColorHex = "#A99577", SortOrder = 90 }
            );
        }

        var existingDays = await db.BusinessHours
            .Select(x => x.DayOfWeek)
            .ToListAsync();

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            if (existingDays.Contains(day))
                continue;

            db.BusinessHours.Add(new BusinessHour
            {
                DayOfWeek = day,
                OpenTime = new TimeSpan(9, 0, 0),
                CloseTime = new TimeSpan(17, 0, 0),
                IsClosed = day == DayOfWeek.Sunday
            });
        }

        var hasActiveAdmin = await db.AppUsers.AnyAsync(x => x.Role == AppUserRole.Admin && x.IsActive);
        if (!hasActiveAdmin)
        {
            var adminEmail = (configuration["SeedAdmin:Email"] ?? "kea@lagrandeur.local").Trim().ToLowerInvariant();
            var adminPassword = configuration["SeedAdmin:Password"] ?? "ChangeMe!2026";

            if (!environment.IsDevelopment() &&
                (adminEmail.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(adminPassword, "ChangeMe!2026", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "No administrator account exists and the production SeedAdmin settings still contain development placeholders. " +
                    "Set SeedAdmin__Email and SeedAdmin__Password securely before first production launch.");
            }

            var existingUser = await db.AppUsers.SingleOrDefaultAsync(x => x.Email == adminEmail);
            if (existingUser is not null)
            {
                // If the configured seed account already exists as a customer, promote it rather than
                // creating a duplicate email or leaving the application without an administrator.
                existingUser.Role = AppUserRole.Admin;
                existingUser.IsActive = true;
                existingUser.PasswordHash = passwordHasher.HashPassword(existingUser, adminPassword);
            }
            else
            {
                var admin = new AppUser
                {
                    FullName = (configuration["SeedAdmin:FullName"] ?? "Kea").Trim(),
                    Email = adminEmail,
                    Role = AppUserRole.Admin,
                    IsActive = true,
                    CreatedUtc = DateTime.UtcNow
                };
                admin.PasswordHash = passwordHasher.HashPassword(admin, adminPassword);
                db.AppUsers.Add(admin);
            }
        }

        await db.SaveChangesAsync();
    }
}
