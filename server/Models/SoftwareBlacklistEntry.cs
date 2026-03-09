namespace HackITSentry.Server.Models;

public class SoftwareBlacklistEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string NamePattern { get; set; } = "";
    public string? Publisher { get; set; }
    public string? Reason { get; set; }
    public string AddedByUsername { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
