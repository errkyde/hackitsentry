namespace HITSight.Server.Models;

public class SoftwarePackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Type { get; set; } = "winget"; // "winget" | "script" | "exe"
    public string InstallCmd { get; set; } = "";
    public string? UninstallCmd { get; set; }
    public string Description { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class DeploymentJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    public SoftwarePackage Package { get; set; } = null!;
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public string Status { get; set; } = "Queued"; // Queued/Running/Success/Failed
    public string? Output { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExecutedAt { get; set; }
}
