using HackITSentry.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace HackITSentry.Server.Services;

public class DeviceOfflineAlertService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertEmailService _email;
    private readonly RuntimeSettings _settings;
    private readonly ILogger<DeviceOfflineAlertService> _logger;

    public DeviceOfflineAlertService(
        IServiceScopeFactory scopeFactory,
        AlertEmailService email,
        RuntimeSettings settings,
        ILogger<DeviceOfflineAlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _email = email;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.IsEmailConfigured)
            _logger.LogInformation("Email not configured at startup – device offline alerts disabled until configured.");

        // Brief startup delay so the rest of the app is ready
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_settings.IsEmailConfigured)
                await CheckDevicesAsync();

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task CheckDevicesAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var intervalMinutes = _settings.CheckinIntervalMinutes;
            var globalDelayMinutes = _settings.OfflineAlertDelayMinutes;
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
                var notifyOffline = device.NotificationOverride?.AlertOnOffline ?? _settings.NotifyDeviceOffline;
                var notifyOnline = device.NotificationOverride?.AlertOnOnline ?? _settings.NotifyDeviceOnline;
                var deviceDelay = device.NotificationOverride?.OfflineAlertDelayMinutes ?? globalDelayMinutes;

                var offlineThreshold = DateTime.UtcNow.AddMinutes(-(intervalMinutes + deviceDelay));
                var isOffline = device.LastSeenAt == null || device.LastSeenAt <= offlineThreshold;
                var isOnline = device.LastSeenAt.HasValue && device.LastSeenAt > onlineThreshold;

                if (isOffline && notifyOffline)
                {
                    // Alert if: never alerted, or last alert was > 24h ago
                    if (device.LastOfflineAlertAt == null || device.LastOfflineAlertAt <= alertCooldown)
                    {
                        toAlert.Add((device.Hostname, device.Customer?.Name, device.LastSeenAt, deviceDelay));
                        device.LastOfflineAlertAt = DateTime.UtcNow;
                    }
                }

                // Reset alert state when device recovers so next outage triggers a fresh alert
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
                await _email.SendAsync(
                    $"[HackIT Sentry] {toAlert.Count} Gerät(e) offline",
                    AlertEmailService.BuildHtml(
                        "#dc2626", "Offline-Alert",
                        $"{toAlert.Count} Gerät{(toAlert.Count == 1 ? "" : "e")} nicht mehr erreichbar",
                        AlertEmailService.DeviceRows(rows),
                        $"Schwellwert: kein Check-in innerhalb von {intervalMinutes + effectiveDelay} Minuten{delayNote}"));
                _logger.LogWarning("Offline alert sent: {Count} device(s).", toAlert.Count);
            }

            if (recovered.Count > 0)
            {
                var rows = recovered.Select(d => (
                    d.Hostname,
                    d.Customer != null ? $"Kunde: {d.Customer}" : (string?)null,
                    (string?)"Wieder online"
                ));
                await _email.SendAsync(
                    $"[HackIT Sentry] {recovered.Count} Gerät(e) wieder online",
                    AlertEmailService.BuildHtml(
                        "#16a34a", "Wieder online",
                        $"{recovered.Count} Gerät{(recovered.Count == 1 ? "" : "e")} wieder erreichbar",
                        AlertEmailService.DeviceRows(rows)));
                _logger.LogInformation("Recovery alert sent: {Count} device(s).", recovered.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking device online status for alerts.");
        }
    }
}
