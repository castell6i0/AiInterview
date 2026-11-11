using System.ComponentModel.DataAnnotations;

namespace AiInterview.Web.Services;

/// <summary>Per-account storage bounds for the single-instance localhost demo.</summary>
public sealed class StorageLimitsOptions
{
    [Range(1, 1000)] public int MaxCvsPerUser { get; set; } = 10;
    [Range(1, 1000)] public int MaxJdsPerUser { get; set; } = 20;
    [Range(1, 1000)] public int MaxActiveSessionsPerUser { get; set; } = 10;
    [Range(1, 10000)] public int MaxSessionsPerDay { get; set; } = 30;
}

/// <summary>
/// Bounded lock stripes serialize count-and-insert across scoped DbContexts in one process.
/// No user IDs are retained. Multiple app instances require a database/distributed lock.
/// </summary>
public sealed class UserStorageLimiter
{
    internal static UserStorageLimiter Shared { get; } = new();
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 257).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<IDisposable> AcquireAsync(string userId, CancellationToken ct)
    {
        var gate = gates[(uint)StringComparer.Ordinal.GetHashCode(userId) % (uint)gates.Length];
        await gate.WaitAsync(ct);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? owned = gate;
        public void Dispose() => Interlocked.Exchange(ref owned, null)?.Release();
    }
}
