using HITSight.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace HITSight.Server.Services;

/// <summary>
/// Runs daily. Drops databases and removes records for tenants whose ScheduledDeletionAt has passed.
/// Free tenants (plan = "free") have ScheduledDeletionAt = null and are never cleaned up.
/// </summary>
public class TenantCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<TenantCleanupService> _logger;

    public TenantCleanupService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<TenantCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for app fully started, then run once per day
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCleanupAsync();
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task RunCleanupAsync()
    {
        var platformConnStr = _config["Platform:ConnectionString"];
        if (string.IsNullOrEmpty(platformConnStr)) return;

        using var scope = _scopeFactory.CreateScope();
        var platformDb = scope.ServiceProvider.GetService<PlatformDbContext>();
        if (platformDb == null) return;

        List<Models.Tenant> expired;
        try
        {
            expired = await platformDb.Tenants
                .Where(t => t.ScheduledDeletionAt.HasValue && t.ScheduledDeletionAt <= DateTime.UtcNow)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query expired tenants");
            return;
        }

        if (expired.Count == 0) return;

        _logger.LogInformation("TenantCleanup: {Count} tenant(s) scheduled for deletion", expired.Count);

        var provisioning = scope.ServiceProvider.GetService<TenantProvisioningService>();
        if (provisioning == null)
        {
            _logger.LogError("TenantProvisioningService not available — skipping cleanup");
            return;
        }

        foreach (var tenant in expired)
        {
            try
            {
                _logger.LogInformation(
                    "Deleting tenant {Slug} (DB: {DbName}), scheduled at {Date}",
                    tenant.Slug, tenant.DbName, tenant.ScheduledDeletionAt);

                await provisioning.DropTenantAsync(tenant.Slug);

                _logger.LogInformation("Tenant {Slug} deleted successfully", tenant.Slug);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete tenant {Slug}", tenant.Slug);
            }
        }
    }
}
