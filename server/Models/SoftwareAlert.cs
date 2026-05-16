namespace HITSight.Server.Models;

public class SoftwareAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public Guid BlacklistEntryId { get; set; }
    public SoftwareBlacklistEntry BlacklistEntry { get; set; } = null!;
    public string SoftwareName { get; set; } = "";
    public string SoftwareVersion { get; set; } = "";
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedByUsername { get; set; }
}
