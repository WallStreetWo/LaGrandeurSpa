using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LaGrandeurSpa.Web.Models;
using Microsoft.AspNetCore.DataProtection;

namespace LaGrandeurSpa.Web.Services;

public sealed class PasswordResetTokenService : IPasswordResetTokenService
{
    private readonly ITimeLimitedDataProtector protector;

    public PasswordResetTokenService(IDataProtectionProvider dataProtectionProvider, IConfiguration configuration)
    {
        protector = dataProtectionProvider
            .CreateProtector("LaGrandeurSpa.PasswordReset.v1")
            .ToTimeLimitedDataProtector();

        LifetimeMinutes = Math.Clamp(
            configuration.GetValue<int?>("Security:PasswordResetMinutes") ?? 45,
            10,
            180);
    }

    public int LifetimeMinutes { get; }

    public string CreateToken(AppUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var payload = JsonSerializer.Serialize(new PasswordResetPayload(
            user.Id,
            CreatePasswordFingerprint(user.PasswordHash)));

        return protector.Protect(payload, TimeSpan.FromMinutes(LifetimeMinutes));
    }

    public bool TryReadToken(string token, out int userId, out string passwordFingerprint)
    {
        userId = 0;
        passwordFingerprint = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var payloadJson = protector.Unprotect(token);
            var payload = JsonSerializer.Deserialize<PasswordResetPayload>(payloadJson);
            if (payload is null || payload.UserId <= 0 || string.IsNullOrWhiteSpace(payload.PasswordFingerprint))
                return false;

            userId = payload.UserId;
            passwordFingerprint = payload.PasswordFingerprint;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public string CreatePasswordFingerprint(string passwordHash)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash ?? string.Empty));
        return Convert.ToHexString(bytes);
    }

    private sealed record PasswordResetPayload(int UserId, string PasswordFingerprint);
}
