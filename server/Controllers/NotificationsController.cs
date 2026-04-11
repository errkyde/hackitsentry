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

        var entries = new Dictionary<string, string>
        {
            ["Notify:DeviceOffline"] = req.DeviceOffline.ToString(),
            ["Notify:DeviceOnline"] = req.DeviceOnline.ToString(),
            ["Notify:NewPending"] = req.NewPending.ToString(),
            ["Notify:SoftwareAlert"] = req.SoftwareAlert.ToString(),
            ["Notify:DiskFull"] = req.DiskFull.ToString(),
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
        }));
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
    bool DiskFull
);

public record DeviceOverrideRequest(
    Guid DeviceId,
    bool? AlertOnOffline,
    bool? AlertOnOnline,
    bool? AlertOnSoftwareAlert,
    bool? AlertOnDiskFull
);
