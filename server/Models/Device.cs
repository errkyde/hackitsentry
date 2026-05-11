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
    public string RustDeskId { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastDiskAlertAt { get; set; }
    public double? DiskAlertAcknowledgedUsedPct { get; set; }

    public string? RustDeskOptionsJson { get; set; }
    public string BiosInfoJson { get; set; } = "{}";
    public string DefenderStatusJson { get; set; } = "{}";

    // Patch Management
    public int PendingUpdatesCount { get; set; }
    public DateTime? LastWindowsUpdateInstalled { get; set; }

    // Antivirus Alerts
    public DateTime? LastAvAlertAt { get; set; }

    public DateTime? LastOfflineAlertAt { get; set; }

    // Event Log Errors (last 24h, updated on checkin)
    public string EventLogErrorsJson { get; set; } = "[]";

    // Asset Lifecycle
    public DateTime? PurchaseDate { get; set; }
    public DateTime? WarrantyExpiry { get; set; }
    public string AssetTag { get; set; } = "";
    public string Location { get; set; } = "";
    public string SerialNumber { get; set; } = "";

    public ICollection<DeviceCheckin> Checkins { get; set; } = [];
    public ICollection<InstalledSoftware> Software { get; set; } = [];
    public LicenseInfo? License { get; set; }
    public ICollection<DeviceNote> Notes { get; set; } = [];
    public ICollection<DeviceCommand> Commands { get; set; } = [];
    public ICollection<SoftwareAlert> SoftwareAlerts { get; set; } = [];
    public DeviceNotificationOverride? NotificationOverride { get; set; }
}
