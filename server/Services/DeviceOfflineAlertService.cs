using HITSight.Server.Data;
using HITSight.Server.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HITSight.Server.Services;

public class DeviceOfflineAlertService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<DeviceOfflineAlertService> _logger;

    public DeviceOfflineAlertService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<DeviceOfflineAlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckAllTenantsAsync();
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task CheckAllTenantsAsync()
    {
        var platformConnStr = _config["Platform:ConnectionString"];

        if (string.IsNullOrEmpty(platformConnStr))
        {
            // Single-tenant mode
            var defaultCs = _config.GetConnectionString("DefaultConnection")!;
            await CheckTenantAsync(defaultCs);
            return;
        }

        // Multi-tenant: iterate all active tenants
        List<(string DbName, string Slug)> tenants;
        try
        {
            var opts = new DbContextOptionsBuilder<PlatformDbContext>()
                .UseNpgsql(platformConnStr)
                .Options;
            await using var platformDb = new PlatformDbContext(opts);
            tenants = await platformDb.Tenants
                .Where(t => t.IsActive)
                .Select(t => new { t.DbName, t.Slug })
                .AsNoTracking()
                .ToListAsync()
                .ContinueWith(r => r.Result.Select(t => (t.DbName, t.Slug)).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tenants for offline alert check");
            return;
        }

        foreach (var (dbName, slug) in tenants)
        {
            try
            {
                var cs = TenantResolutionMiddleware.BuildTenantConnectionString(platformConnStr, dbName);
                await CheckTenantAsync(cs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Offline alert check failed for tenant {Slug}", slug);
            }
        }
    }

    private async Task CheckTenantAsync(string connectionString)
    {
        using var scope = _scopeFactory.CreateScope();

        // Populate TenantContext so AppDbContext and RuntimeSettings resolve correctly
        var tenantCtx = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantCtx.ConnectionString = connectionString;
        tenantCtx.IsActive = true;
        tenantCtx.MaxDevices = int.MaxValue;

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<RuntimeSettings>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var email = AlertEmailService.FromSettings(settings, loggerFactory.CreateLogger<AlertEmailService>());

        if (!settings.IsEmailConfigured) return;

        try
        {
            await CheckDevicesAsync(db, settings, email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking device online status for alerts.");
        }
    }

    private static async Task CheckDevicesAsync(AppDbContext db, RuntimeSettings settings, AlertEmailService email)
    {
        var intervalMinutes = settings.CheckinIntervalMinutes;
        var globalDelayMinutes = settings.OfflineAlertDelayMinutes;
        var onlineThreshold = DateTime.UtcNow.AddMinutes(-(intervalMinutes * 2 + 5));
        var alertCooldown = DateTime.UtcNow.AddHours(-24);

        var devices = await db.Devices
            .Include(d => d.Customer)
            .Include(d => d.NotificationOverride)
            .ToListAsync();

        var toAlert = new List<(string Hostname, string? Customer, DateTime? LastSeen, int DelayMinutes)>();
        var recovered = new List<(string Hostname, string? Customer)>();

        foreach (var device in devices)
        {
            var notifyOffline = device.NotificationOverride?.AlertOnOffline ?? settings.NotifyDeviceOffline;
            var notifyOnline = device.NotificationOverride?.AlertOnOnline ?? settings.NotifyDeviceOnline;
            var deviceDelay = device.NotificationOverride?.OfflineAlertDelayMinutes ?? globalDelayMinutes;

            var offlineThreshold = DateTime.UtcNow.AddMinutes(-(intervalMinutes + deviceDelay));
            var isOffline = device.LastSeenAt == null || device.LastSeenAt <= offlineThreshold;
            var isOnline = device.LastSeenAt.HasValue && device.LastSeenAt > onlineThreshold;

            if (isOffline && notifyOffline)
            {
                if (device.LastOfflineAlertAt == null || device.LastOfflineAlertAt <= alertCooldown)
                {
                    toAlert.Add((device.Hostname, device.Customer?.Name, device.LastSeenAt, deviceDelay));
                    device.LastOfflineAlertAt = DateTime.UtcNow;
                }
            }

            if (isOnline && device.LastOfflineAlertAt != null)
            {
                device.LastOfflineAlertAt = null;
                if (notifyOnline)
                    recovered.Add((device.Hostname, device.Customer?.Name));
            }
        }

        await db.SaveChangesAsync();

        if (toAlert.Count > 0)
        {
            var rows = toAlert.Select(d => (
                d.Hostname,
                d.Customer != null ? $"Kunde: {d.Customer}" : (string?)null,
                d.LastSeen.HasValue ? $"Zuletzt gesehen: {d.LastSeen:dd.MM.yyyy HH:mm} UTC" : "Noch nie gesehen"
            ));
            var effectiveDelay = toAlert.Select(d => d.DelayMinutes).Distinct().Count() == 1
                ? toAlert[0].DelayMinutes : globalDelayMinutes;
            var delayNote = effectiveDelay > 0 ? $" (Verzögerung: {effectiveDelay} Min.)" : "";
            await email.SendAsync(
                $"[HITSight] {toAlert.Count} Gerät(e) offline",
                AlertEmailService.BuildHtml(
                    "#dc2626", "Offline-Alert",
                    $"{toAlert.Count} Gerät{(toAlert.Count == 1 ? "" : "e")} nicht mehr erreichbar",
                    AlertEmailService.DeviceRows(rows),
                    $"Schwellwert: kein Check-in innerhalb von {intervalMinutes + effectiveDelay} Minuten{delayNote}"));
        }

        if (recovered.Count > 0)
        {
            var rows = recovered.Select(d => (
                d.Hostname,
                d.Customer != null ? $"Kunde: {d.Customer}" : (string?)null,
                (string?)"Wieder online"
            ));
            await email.SendAsync(
                $"[HITSight] {recovered.Count} Gerät(e) wieder online",
                AlertEmailService.BuildHtml(
                    "#16a34a", "Wieder online",
                    $"{recovered.Count} Gerät{(recovered.Count == 1 ? "" : "e")} wieder erreichbar",
                    AlertEmailService.DeviceRows(rows)));
        }
    }
}
