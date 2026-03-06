namespace HackITSentry.Server.Models;

public class LicenseInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public string? WindowsKeyEncrypted { get; set; }
    public string LicenseType { get; set; } = "";
    public string? OfficeKeyEncrypted { get; set; }
    public string OfficeVersion { get; set; } = "";
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
