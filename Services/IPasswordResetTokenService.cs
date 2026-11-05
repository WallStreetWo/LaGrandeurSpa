using LaGrandeurSpa.Web.Models;

namespace LaGrandeurSpa.Web.Services;

public interface IPasswordResetTokenService
{
    int LifetimeMinutes { get; }
    string CreateToken(AppUser user);
    bool TryReadToken(string token, out int userId, out string passwordFingerprint);
    string CreatePasswordFingerprint(string passwordHash);
}
