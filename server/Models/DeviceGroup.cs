namespace HITSight.Server.Models;

public class DeviceGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Color { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? NotificationSettingsJson { get; set; }
    public string? RustDeskOptionsJson { get; set; }

    public ICollection<Device> Devices { get; set; } = [];
}
