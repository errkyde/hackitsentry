using HITSight.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace HITSight.Server.Services;

/// <summary>
/// Runs daily. Sends a reminder email to tenants whose trial ends in ≤ 3 days.
/// Tracks sent reminders via TrialReminderSentAt to prevent duplicates.
/// </summary>
public class TrialReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlatformEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<TrialReminderService> _logger;

    public TrialReminderService(
        IServiceScopeFactory scopeFactory,
        PlatformEmailService email,
        IConfiguration config,
        ILogger<TrialReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _email = email;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunAsync();
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task RunAsync()
    {
        if (!_email.IsConfigured) return;

        var platformConnStr = _config["Platform:ConnectionString"];
        if (string.IsNullOrEmpty(platformConnStr)) return;

        using var scope = _scopeFactory.CreateScope();
        var platformDb = scope.ServiceProvider.GetService<PlatformDbContext>();
        if (platformDb == null) return;

        var now = DateTime.UtcNow;
        var reminderWindow = now.AddDays(3);

        List<Models.Tenant> dueTenants;
        try
        {
            dueTenants = await platformDb.Tenants
                .Where(t =>
                    t.IsActive &&
                    t.Plan != "free" &&
                    t.SubscriptionStatus == "trialing" &&
                    t.TrialEndsAt.HasValue &&
                    t.TrialEndsAt <= reminderWindow &&
                    t.TrialEndsAt > now &&
                    t.TrialReminderSentAt == null)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query tenants for trial reminders");
            return;
        }

        foreach (var tenant in dueTenants)
        {
            try
            {
                await SendReminderAsync(tenant);
                tenant.TrialReminderSentAt = now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send trial reminder for tenant {Slug}", tenant.Slug);
            }
        }

        if (dueTenants.Count > 0)
            await platformDb.SaveChangesAsync();
    }

    private async Task SendReminderAsync(Models.Tenant tenant)
    {
        if (string.IsNullOrEmpty(tenant.AdminEmail)) return;

        var platformDomain = _config["Platform:Domain"] ?? "localhost";
        var supportEmail = _config["Platform:SupportEmail"] ?? "";
        var supportUrl = _config["Platform:SupportUrl"] ?? "";
        var daysLeft = (int)Math.Ceiling((tenant.TrialEndsAt!.Value - DateTime.UtcNow).TotalDays);
        var loginUrl = $"https://{tenant.Slug}.{platformDomain}/login";

        var supportLine = string.IsNullOrEmpty(supportEmail) ? "" :
            $"<p style='margin:16px 0 0;font-size:13px;color:#71717a;'>Fragen? " +
            (string.IsNullOrEmpty(supportUrl) ? "" : $"<a href='{supportUrl}' style='color:#2563eb;'>Ticket erstellen</a> &middot; ") +
            $"<a href='mailto:{supportEmail}' style='color:#2563eb;'>{supportEmail}</a></p>";

        var planNames = new Dictionary<string, string>
        {
            ["starter"] = "Starter (25 Geräte)",
            ["pro"] = "Pro (100 Geräte)",
            ["enterprise"] = "Enterprise (unbegrenzt)",
        };
        var planName = planNames.TryGetValue(tenant.Plan, out var n) ? n : tenant.Plan;

        var body = AlertEmailService.BuildHtml(
            "#ea580c", "Testphase läuft ab",
            $"Ihre Testphase endet in {daysLeft} Tag{(daysLeft == 1 ? "" : "en")}",
            $"""
            <p style="margin:0 0 16px;font-size:14px;color:#3f3f46;line-height:1.6;">
              Die 14-tägige Testphase für <strong>{tenant.Name}</strong> endet am
              <strong>{tenant.TrialEndsAt:dd.MM.yyyy}</strong>.
            </p>
            <p style="margin:0 0 16px;font-size:14px;color:#3f3f46;line-height:1.6;">
              Ab dann wird Ihr gewähltes Paket <strong>{planName}</strong> automatisch abgerechnet.
              Falls Sie kündigen möchten, tun Sie dies bitte vor dem Ablaufdatum in Ihrem Stripe-Kundenportal.
            </p>
            <a href="{loginUrl}"
               style="display:inline-block;padding:12px 28px;background:#18181b;color:#fff;text-decoration:none;border-radius:6px;font-size:14px;font-weight:600;margin-bottom:4px;">
              Zum Dashboard
            </a>
            {supportLine}
            """,
            $"Testphase endet am {tenant.TrialEndsAt:dd.MM.yyyy}");

        await _email.SendAsync(
            tenant.AdminEmail,
            $"HITSight – Ihre Testphase endet in {daysLeft} Tag{(daysLeft == 1 ? "" : "en")}",
            body);

        _logger.LogInformation("Trial reminder sent to {Email} (tenant {Slug}, {Days} days left)",
            tenant.AdminEmail, tenant.Slug, daysLeft);
    }
}
