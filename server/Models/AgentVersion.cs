namespace HITSight.Server.Models;

public class AgentVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Version { get; set; } = "";
    public string? DownloadUrl { get; set; }
    public string? Changelog { get; set; }
    public bool IsLatest { get; set; }
    public DateTime ReleasedAt { get; set; } = DateTime.UtcNow;
}
