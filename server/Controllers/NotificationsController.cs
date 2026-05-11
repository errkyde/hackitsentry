using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using HackITSentry.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackITSentry.Server.Controllers;

[ApiController]
[Route("api/settings/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RuntimeSettings _settings;

    public NotificationsController(AppDbContext db, RuntimeSettings settings)
    {
        _db = db;
        _settings = settings;
    }

    // GET /api/settings/notifications
    [HttpGet]
    public IActionResult GetDefaults()
    {
        return Ok(new
        {
            deviceOffline = _settings.NotifyDeviceOffline,
            deviceOnline = _settings.NotifyDeviceOnline,
            newPending = _settings.NotifyNewPending,
            softwareAlert = _settings.NotifySoftwareAlert,
            diskFull = _settings.NotifyDiskFull,
            offlineAlertDelayMinutes = _settings.OfflineAlertDelayMinutes,
            avSignatureAgeAlertDays = _settings.AvSignatureAgeAlertDays,
        });
    }

    // PUT /api/settings/notifications
    [HttpPut]
    public async Task<IActionResult> SaveDefaults([FromBody] NotificationDefaultsRequest req)
    {
        _settings.NotifyDeviceOffline = req.DeviceOffline;
        _settings.NotifyDeviceOnline = req.DeviceOnline;
        _settings.NotifyNewPending = req.NewPending;
        _settings.NotifySoftwareAlert = req.SoftwareAlert;
        _settings.NotifyDiskFull = req.DiskFull;
        _settings.OfflineAlertDelayMinutes = Math.Max(0, req.OfflineAlertDelayMinutes);
        _settings.AvSignatureAgeAlertDays = Math.Max(0, req.AvSignatureAgeAlertDays);

        var entries = new Dictionary<string, string>
        {
            ["Notify:DeviceOffline"] = req.DeviceOffline.ToString(),
            ["Notify:DeviceOnline"] = req.DeviceOnline.ToString(),
            ["Notify:NewPending"] = req.NewPending.ToString(),
            ["Notify:SoftwareAlert"] = req.SoftwareAlert.ToString(),
            ["Notify:DiskFull"] = req.DiskFull.ToString(),
            ["Notify:OfflineAlertDelayMinutes"] = _settings.OfflineAlertDelayMinutes.ToString(),
            ["Notify:AvSignatureAgeAlertDays"] = _settings.AvSignatureAgeAlertDays.ToString(),
        };

        foreach (var (key, value) in entries)
        {
            var existing = await _db.AppSettings.FindAsync(key);
            if (existing != null)
                existing.Value = value;
            else
                _db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Benachrichtigungseinstellungen gespeichert." });
    }

    // GET /api/settings/notifications/devices
    [HttpGet("devices")]
    public async Task<IActionResult> GetDeviceOverrides()
    {
        var overrides = await _db.DeviceNotificationOverrides
            .Include(o => o.Device)
                .ThenInclude(d => d.Customer)
            .OrderBy(o => o.Device.Hostname)
            .ToListAsync();

        return Ok(overrides.Select(o => new
        {
            o.Id,
            device = new
            {
                o.Device.Id,
                o.Device.Hostname,
                o.Device.Description,
                customer = o.Device.Customer != null ? new { o.Device.Customer.Id, o.Device.Customer.Name } : null,
            },
            o.AlertOnOffline,
            o.AlertOnOnline,
            o.AlertOnSoftwareAlert,
            o.AlertOnDiskFull,
            o.OfflineAlertDelayMinutes,
        }));
    }

    // GET /api/settings/notifications/devices/{deviceId}
    [HttpGet("devices/{deviceId:guid}")]
    public async Task<IActionResult> GetDeviceOverride(Guid deviceId)
    {
        var o = await _db.DeviceNotificationOverrides
            .FirstOrDefaultAsync(o => o.DeviceId == deviceId);

        if (o == null) return NotFound();

        return Ok(new
        {
            o.AlertOnOffline,
            o.AlertOnOnline,
            o.AlertOnSoftwareAlert,
            o.AlertOnDiskFull,
            o.OfflineAlertDelayMinutes,
            o.SourceGroupId,
        });
    }

    // POST /api/settings/notifications/devices
    [HttpPost("devices")]
    public async Task<IActionResult> UpsertDeviceOverride([FromBody] DeviceOverrideRequest req)
    {
        var device = await _db.Devices.FindAsync(req.DeviceId);
        if (device == null)
            return NotFound(new { message = "Gerät nicht gefunden." });

        var existing = await _db.DeviceNotificationOverrides
            .FirstOrDefaultAsync(o => o.DeviceId == req.DeviceId);

        if (existing != null)
        {
            existing.AlertOnOffline = req.AlertOnOffline;
            existing.AlertOnOnline = req.AlertOnOnline;
            existing.AlertOnSoftwareAlert = req.AlertOnSoftwareAlert;
            existing.AlertOnDiskFull = req.AlertOnDiskFull;
            existing.OfflineAlertDelayMinutes = req.OfflineAlertDelayMinutes;
            existing.SourceGroupId = null; // manual override detaches from group
        }
        else
        {
            _db.DeviceNotificationOverrides.Add(new DeviceNotificationOverride
            {
                DeviceId = req.DeviceId,
                AlertOnOffline = req.AlertOnOffline,
                AlertOnOnline = req.AlertOnOnline,
                AlertOnSoftwareAlert = req.AlertOnSoftwareAlert,
                AlertOnDiskFull = req.AlertOnDiskFull,
                OfflineAlertDelayMinutes = req.OfflineAlertDelayMinutes,
                SourceGroupId = null,
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Gerätespezifische Einstellung gespeichert." });
    }

    // DELETE /api/settings/notifications/devices/{deviceId}
    [HttpDelete("devices/{deviceId:guid}")]
    public async Task<IActionResult> DeleteDeviceOverride(Guid deviceId)
    {
        var existing = await _db.DeviceNotificationOverrides
            .FirstOrDefaultAsync(o => o.DeviceId == deviceId);

        if (existing == null)
            return NotFound();

        _db.DeviceNotificationOverrides.Remove(existing);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record NotificationDefaultsRequest(
    bool DeviceOffline,
    bool DeviceOnline,
    bool NewPending,
    bool SoftwareAlert,
    bool DiskFull,
    int OfflineAlertDelayMinutes = 0,
    int AvSignatureAgeAlertDays = 7
);

public record DeviceOverrideRequest(
    Guid DeviceId,
    bool? AlertOnOffline,
    bool? AlertOnOnline,
    bool? AlertOnSoftwareAlert,
    bool? AlertOnDiskFull,
    int? OfflineAlertDelayMinutes = null
);
