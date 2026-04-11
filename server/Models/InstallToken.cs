namespace HackITSentry.Server.Models;

public class InstallToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Token { get; set; } = "";
    public string CreatedByUsername { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; } = false;
    public DateTime? UsedAt { get; set; }
}
