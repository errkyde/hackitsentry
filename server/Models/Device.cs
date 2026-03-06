namespace HackITSentry.Server.Models;

public class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AgentApiKey { get; set; } = "";
    public string Hostname { get; set; } = "";
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid? GroupId { get; set; }
    public DeviceGroup? Group { get; set; }
    public string Description { get; set; } = "";
    public DateTime? LastSeenAt { get; set; }
    public string WindowsVersion { get; set; } = "";
    public string WindowsBuild { get; set; } = "";
    public string WindowsEdition { get; set; } = "";
    public string LicenseType { get; set; } = "";
    public string CpuModel { get; set; } = "";
    public int CpuCores { get; set; }
    public double RamTotalGB { get; set; }
    public string NetworkAdaptersJson { get; set; } = "[]";
    public bool LicenseRequested { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<DeviceCheckin> Checkins { get; set; } = [];
    public ICollection<InstalledSoftware> Software { get; set; } = [];
    public LicenseInfo? License { get; set; }
}
