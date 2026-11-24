using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace AiInterview.Infrastructure.Identity;

// Emailed 6-digit sign-in code: 2 minutes, single use. Only a hash is kept, in AspNetUserTokens.
// Wrong guesses go through Identity's lockout (TwoFactorSignInAsync counts failures).
public sealed class LoginCodeTokenProvider(TimeProvider clock) : IUserTwoFactorTokenProvider<ApplicationUser>
{
    public const string Name = "LoginCode";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public async Task<string> GenerateAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var expires = clock.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds();
        await manager.SetAuthenticationTokenAsync(user, Name, purpose, $"{expires}:{Hash(user, purpose, code)}");
        return code;
    }

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var parts = (await manager.GetAuthenticationTokenAsync(user, Name, purpose))?.Split(':');
        if (parts is not { Length: 2 } || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out var expires)
            || clock.GetUtcNow().ToUnixTimeSeconds() > expires || token is not { Length: 6 }
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]), Encoding.ASCII.GetBytes(Hash(user, purpose, token))))
            return false;
        await manager.RemoveAuthenticationTokenAsync(user, Name, purpose);
        return true;
    }

    public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<ApplicationUser> manager, ApplicationUser user) =>
        manager.IsEmailConfirmedAsync(user);

    // The security stamp makes a password change invalidate any code already sent.
    private static string Hash(ApplicationUser user, string purpose, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{user.Id}:{user.SecurityStamp}:{purpose}:{code}")));
}
