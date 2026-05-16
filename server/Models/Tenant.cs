namespace HITSight.Server.Models;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string DbName { get; set; } = "";
    public string Plan { get; set; } = "starter";
    public int MaxDevices { get; set; } = 25;
    public bool IsActive { get; set; } = true;
    public string AdminEmail { get; set; } = "";
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? SubscriptionStatus { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }
    public DateTime? ScheduledDeletionAt { get; set; }
    public DateTime? TrialReminderSentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
