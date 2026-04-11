namespace HackITSentry.Server.Models;

public class DeviceNotificationOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;

    // null = use global default
    public bool? AlertOnOffline { get; set; }
    public bool? AlertOnOnline { get; set; }
    public bool? AlertOnSoftwareAlert { get; set; }
    public bool? AlertOnDiskFull { get; set; }
}
