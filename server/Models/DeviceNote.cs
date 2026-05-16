namespace HITSight.Server.Models;

public class DeviceNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public string Content { get; set; } = "";
    public string AuthorUsername { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
