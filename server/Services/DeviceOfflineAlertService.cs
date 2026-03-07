using HackITSentry.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace HackITSentry.Server.Services;

public class DeviceOfflineAlertService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<DeviceOfflineAlertService> _logger;

    // Devices known to be offline (survives between check cycles, reset on restart)
    private readonly HashSet<Guid> _knownOffline = [];

    public DeviceOfflineAlertService(
        IServiceScopeFactory scopeFactory,
        AlertEmailService email,
        IConfiguration config,
        ILogger<DeviceOfflineAlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _email = email;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_email.IsConfigured)
        {
            _logger.LogInformation("Email not configured – device offline alerts disabled.");
            return;
        }

        // Wait a bit after startup so the DB is ready
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        // Seed initial offline set without sending alerts (devices may already be offline at startup)
        await SeedInitialStateAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalMinutes = _config.GetValue<int>("CheckinIntervalMinutes", 30);
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
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

            _logger.LogInformation("Device alert service started. {Count} devices already offline (no alerts sent).", offlineIds.Count);
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

            var devices = await db.Devices
                .Include(d => d.Customer)
                .Select(d => new { d.Id, d.Hostname, d.LastSeenAt, CustomerName = d.Customer != null ? d.Customer.Name : null })
                .ToListAsync();

            var newlyOffline = new List<(string Hostname, string? Customer, DateTime? LastSeen)>();
            var recovered = new List<(string Hostname, string? Customer)>();

            foreach (var device in devices)
            {
                var isOffline = device.LastSeenAt == null || device.LastSeenAt <= threshold;

                if (isOffline && _knownOffline.Add(device.Id))
                    newlyOffline.Add((device.Hostname, device.CustomerName, device.LastSeenAt));

                if (!isOffline && _knownOffline.Remove(device.Id))
                    recovered.Add((device.Hostname, device.CustomerName));
            }

            if (newlyOffline.Count > 0)
            {
                var lines = newlyOffline.Select(d =>
                    $"  • {d.Hostname}" +
                    (d.Customer != null ? $" ({d.Customer})" : "") +
                    (d.LastSeen.HasValue ? $" – last seen {d.LastSeen:yyyy-MM-dd HH:mm} UTC" : " – never seen"));

                await _email.SendAsync(
                    $"[HackIT Sentry] {newlyOffline.Count} device(s) went offline",
                    $"The following device(s) have gone offline:\n\n{string.Join("\n", lines)}\n\n" +
                    $"Checked at: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC\n" +
                    $"Online threshold: last check-in within {_config.GetValue<int>("CheckinIntervalMinutes", 30) * 2 + 5} minutes");

                _logger.LogWarning("Alert sent: {Count} device(s) went offline.", newlyOffline.Count);
            }

            if (recovered.Count > 0)
            {
                var lines = recovered.Select(d =>
                    $"  • {d.Hostname}" + (d.Customer != null ? $" ({d.Customer})" : ""));

                await _email.SendAsync(
                    $"[HackIT Sentry] {recovered.Count} device(s) recovered",
                    $"The following device(s) are back online:\n\n{string.Join("\n", lines)}\n\n" +
                    $"Recovered at: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");

                _logger.LogInformation("Recovery alert sent: {Count} device(s) back online.", recovered.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking device online status for alerts.");
        }
    }

    private DateTime GetOnlineThreshold() =>
        DateTime.UtcNow.AddMinutes(-(_config.GetValue<int>("CheckinIntervalMinutes", 30) * 2 + 5));
}
