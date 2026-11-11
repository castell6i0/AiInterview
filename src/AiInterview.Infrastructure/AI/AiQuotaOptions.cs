using System.ComponentModel.DataAnnotations;

namespace AiInterview.Infrastructure.AI;

/// <summary>Reloadable policy for shared credentials. Quota periods use UTC.</summary>
public sealed class AiQuotaOptions
{
    public bool SystemKeyEnabled { get; set; } = true;
    public bool RequireConfirmedEmail { get; set; } = true;
    [Range(1, 100000)] public int SystemRequestsPerDay { get; set; } = 100;
    // Zero disables the optional monthly cap.
    [Range(0, 1000000)] public int SystemRequestsPerMonth { get; set; } = 2000;
    [Range(1, 20)] public int SystemMaxConcurrentRequests { get; set; } = 2;
}
