namespace HackITSentry.Server.Models;

public enum PendingDeviceStatus { Pending, Approved, Rejected }

public class PendingDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RegistrationToken { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string WindowsVersion { get; set; } = "";
    public string CpuModel { get; set; } = "";
    public double RamTotalGB { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public PendingDeviceStatus Status { get; set; } = PendingDeviceStatus.Pending;
}
