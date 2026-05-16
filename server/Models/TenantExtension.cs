namespace HITSight.Server.Models;

public class TenantExtension
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int DaysAdded { get; set; }
    public string? Reason { get; set; }
    public bool SendToast { get; set; }
    public bool SendEmail { get; set; }
    public string CreatedByUsername { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Tenant Tenant { get; set; } = null!;
}
