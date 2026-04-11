using HackITSentry.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace HackITSentry.Server.Services;

public class DeviceOfflineAlertService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertEmailService _email;
    private readonly RuntimeSettings _settings;
    private readonly ILogger<DeviceOfflineAlertService> _logger;

    private readonly HashSet<Guid> _knownOffline = [];

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

        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        await SeedInitialStateAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalMinutes = _settings.CheckinIntervalMinutes;
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);

            if (_settings.IsEmailConfigured)
                await CheckDevicesAsync();
        }
    }

    private async Task SeedInitialStateAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var threshold = GetOnlineThreshold();

            var offlineIds = await db.Devices
                .Where(d => d.LastSeenAt == null || d.LastSeenAt <= threshold)
                .Select(d => d.Id)
                .ToListAsync();

            foreach (var id in offlineIds)
                _knownOffline.Add(id);

            _logger.LogInformation("Device alert service started. {Count} device(s) already offline.", offlineIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding initial offline device state.");
        }
    }

    private async Task CheckDevicesAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var threshold = GetOnlineThreshold();
            var intervalMinutes = _settings.CheckinIntervalMinutes;

            var devices = await db.Devices
                .Include(d => d.Customer)
                .Include(d => d.NotificationOverride)
                .Select(d => new
                {
                    d.Id,
                    d.Hostname,
                    d.LastSeenAt,
                    CustomerName = d.Customer != null ? d.Customer.Name : null,
                    Override = d.NotificationOverride
                })
                .ToListAsync();

            var newlyOffline = new List<(string Hostname, string? Customer, DateTime? LastSeen)>();
            var recovered = new List<(string Hostname, string? Customer)>();

            foreach (var device in devices)
            {
                var isOffline = device.LastSeenAt == null || device.LastSeenAt <= threshold;
                var notifyOffline = device.Override?.AlertOnOffline ?? _settings.NotifyDeviceOffline;
                var notifyOnline = device.Override?.AlertOnOnline ?? _settings.NotifyDeviceOnline;

                if (isOffline && _knownOffline.Add(device.Id))
                {
                    if (notifyOffline)
                        newlyOffline.Add((device.Hostname, device.CustomerName, device.LastSeenAt));
                }
                if (!isOffline && _knownOffline.Remove(device.Id))
                {
                    if (notifyOnline)
                        recovered.Add((device.Hostname, device.CustomerName));
                }
            }

            if (newlyOffline.Count > 0)
            {
                var rows = newlyOffline.Select(d => (
                    d.Hostname,
                    d.Customer != null ? $"Kunde: {d.Customer}" : (string?)null,
                    d.LastSeen.HasValue ? $"Zuletzt gesehen: {d.LastSeen:dd.MM.yyyy HH:mm} UTC" : "Noch nie gesehen"
                ));

                await _email.SendAsync(
                    $"[HackIT Sentry] {newlyOffline.Count} Gerät(e) offline",
                    AlertEmailService.BuildHtml(
                        "#dc2626", "Offline-Alert",
                        $"{newlyOffline.Count} Gerät{(newlyOffline.Count == 1 ? "" : "e")} nicht mehr erreichbar",
                        AlertEmailService.DeviceRows(rows),
                        $"Schwellwert: kein Check-in innerhalb von {intervalMinutes * 2 + 5} Minuten"));

                _logger.LogWarning("Offline alert sent: {Count} device(s).", newlyOffline.Count);
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

    private DateTime GetOnlineThreshold() =>
        DateTime.UtcNow.AddMinutes(-(_settings.CheckinIntervalMinutes * 2 + 5));
}
