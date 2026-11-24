using AiInterview.Application.Interfaces;
using AiInterview.Domain.Entities;
using AiInterview.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AiInterview.Web.Services;

// Students confirm each login with an emailed code. Without SMTP or a confirmed address the step is
// skipped, otherwise those students could never sign in.
public sealed class StudentSignInManager(UserManager<ApplicationUser> users, IHttpContextAccessor accessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claims, IOptions<IdentityOptions> options,
    ILogger<SignInManager<ApplicationUser>> logger, IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation, IAccountEmailSender mail)
    : SignInManager<ApplicationUser>(users, accessor, claims, options, logger, schemes, confirmation)
{
    public override async Task<bool> IsTwoFactorEnabledAsync(ApplicationUser user) =>
        mail.IsConfigured && await UserManager.IsEmailConfirmedAsync(user) && await UserManager.IsInRoleAsync(user, Roles.Student);
}
