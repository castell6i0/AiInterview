using Microsoft.AspNetCore.Identity;

namespace AiInterview.Infrastructure.Identity;

// Accounts that existed before email verification keep their prior access. New self-registrations
// must verify ownership. Never mark historical email addresses confirmed during migration.
public sealed class AccountEmailConfirmation : IUserConfirmation<ApplicationUser>
{
    public Task<bool> IsConfirmedAsync(UserManager<ApplicationUser> manager, ApplicationUser user) =>
        Task.FromResult(!user.EmailConfirmationRequired || user.EmailConfirmed);
}
