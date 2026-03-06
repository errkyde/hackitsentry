namespace HackITSentry.Server.Models;

public class DeviceCheckin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public DateTime CheckedInAt { get; set; } = DateTime.UtcNow;
    public double RamUsedGB { get; set; }
    public string DiskDrivesJson { get; set; } = "[]";
}
