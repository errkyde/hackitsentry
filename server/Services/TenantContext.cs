namespace HITSight.Server.Services;

public interface ITenantContext
{
    Guid TenantId { get; }
    string Slug { get; }
    string ConnectionString { get; }
    string Plan { get; }
    int MaxDevices { get; }
    bool IsActive { get; }
    string? SubscriptionStatus { get; }
    DateTime? TrialEndsAt { get; }
    DateTime? CurrentPeriodEndsAt { get; }
}

/// <summary>
/// Mutable, Scoped implementation. TenantResolutionMiddleware populates it per request.
/// Background services create a scope and set properties directly.
/// </summary>
public class TenantContext : ITenantContext
{
    public Guid TenantId { get; set; }
    public string Slug { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string Plan { get; set; } = "starter";
    public int MaxDevices { get; set; } = int.MaxValue;
    public bool IsActive { get; set; } = true;
    public string? SubscriptionStatus { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
}
