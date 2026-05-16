using System.Text.Json;
using HITSight.Server.Data;
using HITSight.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/groups")]
[Authorize]
public class GroupsController : ControllerBase
{
    private readonly AppDbContext _db;

    public GroupsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var groups = await _db.Groups
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.Description,
                g.Color,
                g.CreatedAt,
                g.NotificationSettingsJson,
                g.RustDeskOptionsJson,
                DeviceCount = g.Devices.Count
            })
            .OrderBy(g => g.Name)
            .ToListAsync();

        return Ok(groups);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var group = await _db.Groups.FindAsync(id);
        if (group == null)
            return NotFound();
        return Ok(group);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] GroupRequest request)
    {
        var group = new DeviceGroup
        {
            Name = request.Name,
            Description = request.Description,
            Color = request.Color
        };
        _db.Groups.Add(group);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = group.Id }, group);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] GroupRequest request)
    {
        var group = await _db.Groups.FindAsync(id);
        if (group == null)
            return NotFound();

        group.Name = request.Name;
        group.Description = request.Description;
        group.Color = request.Color;
        await _db.SaveChangesAsync();
        return Ok(group);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var group = await _db.Groups.FindAsync(id);
        if (group == null)
            return NotFound();

        _db.Groups.Remove(group);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/groups/{id}/sync-rustdesk
    // Applies RustDesk options to all devices in the group.
    // Pass null options to clear per-device overrides.
    [HttpPost("{id:guid}/sync-rustdesk")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SyncRustDesk(Guid id, [FromBody] SyncRustDeskRequest req)
    {
        var devices = await _db.Devices.Where(d => d.GroupId == id).ToListAsync();
        if (devices.Count == 0)
            return Ok(new { updated = 0 });

        var json = req.Options != null && req.Options.Count > 0
            ? JsonSerializer.Serialize(req.Options)
            : null;

        foreach (var device in devices)
            device.RustDeskOptionsJson = json;

        var group = await _db.Groups.FindAsync(id);
        if (group != null) group.RustDeskOptionsJson = json;

        await _db.SaveChangesAsync();
        return Ok(new { updated = devices.Count });
    }

    // POST /api/groups/{id}/sync-notifications
    // Upserts DeviceNotificationOverride for all devices in the group.
    [HttpPost("{id:guid}/sync-notifications")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SyncNotifications(Guid id, [FromBody] GroupNotificationRequest req)
    {
        var deviceIds = await _db.Devices
            .Where(d => d.GroupId == id)
            .Select(d => d.Id)
            .ToListAsync();

        if (deviceIds.Count == 0)
            return Ok(new { updated = 0 });

        var existingOverrides = await _db.DeviceNotificationOverrides
            .Where(o => deviceIds.Contains(o.DeviceId))
            .ToListAsync();

        var existingByDevice = existingOverrides.ToDictionary(o => o.DeviceId);

        int updated = 0;
        foreach (var deviceId in deviceIds)
        {
            if (existingByDevice.TryGetValue(deviceId, out var existing))
            {
                // Skip devices with custom overrides (SourceGroupId == null means manually set)
                if (existing.SourceGroupId == null)
                    continue;

                existing.AlertOnOffline = req.AlertOnOffline;
                existing.AlertOnOnline = req.AlertOnOnline;
                existing.AlertOnSoftwareAlert = req.AlertOnSoftwareAlert;
                existing.AlertOnDiskFull = req.AlertOnDiskFull;
                existing.OfflineAlertDelayMinutes = req.OfflineAlertDelayMinutes;
                existing.SourceGroupId = id;
            }
            else
            {
                _db.DeviceNotificationOverrides.Add(new DeviceNotificationOverride
                {
                    DeviceId = deviceId,
                    AlertOnOffline = req.AlertOnOffline,
                    AlertOnOnline = req.AlertOnOnline,
                    AlertOnSoftwareAlert = req.AlertOnSoftwareAlert,
                    AlertOnDiskFull = req.AlertOnDiskFull,
                    OfflineAlertDelayMinutes = req.OfflineAlertDelayMinutes,
                    SourceGroupId = id,
                });
            }
            updated++;
        }

        // Persist settings on the group so they can be displayed in the overview
        var group = await _db.Groups.FindAsync(id);
        if (group != null)
            group.NotificationSettingsJson = System.Text.Json.JsonSerializer.Serialize(req);

        await _db.SaveChangesAsync();
        return Ok(new { updated });
    }

    // DELETE /api/groups/{id}/sync-notifications
    // Removes all DeviceNotificationOverrides for devices in the group (reset to global defaults).
    [HttpDelete("{id:guid}/sync-notifications")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ClearNotifications(Guid id)
    {
        var deviceIds = await _db.Devices
            .Where(d => d.GroupId == id)
            .Select(d => d.Id)
            .ToListAsync();

        var overrides = await _db.DeviceNotificationOverrides
            .Where(o => deviceIds.Contains(o.DeviceId))
            .ToListAsync();

        _db.DeviceNotificationOverrides.RemoveRange(overrides);
        await _db.SaveChangesAsync();
        return Ok(new { removed = overrides.Count });
    }
}

public record GroupRequest(string Name, string Description, string? Color);
public record SyncRustDeskRequest(Dictionary<string, string>? Options);
public record GroupNotificationRequest(bool? AlertOnOffline, bool? AlertOnOnline, bool? AlertOnSoftwareAlert, bool? AlertOnDiskFull, int? OfflineAlertDelayMinutes = null);
