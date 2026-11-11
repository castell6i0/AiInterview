using Microsoft.AspNetCore.Identity;

namespace AiInterview.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = "";
    public bool EmailConfirmationRequired { get; set; }
    public DateTimeOffset? ConfirmationEmailRequestedAt { get; set; }
}
