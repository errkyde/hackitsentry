using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using HackITSentry.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackITSentry.Server.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RuntimeSettings _runtimeSettings;

    public DashboardController(AppDbContext db, RuntimeSettings runtimeSettings)
    {
        _db = db;
        _runtimeSettings = runtimeSettings;
    }

    // GET /api/dashboard
    [HttpGet]
    public async Task<IActionResult> GetDashboard()
    {
        var onlineThreshold = DateTime.UtcNow.AddMinutes(-(_runtimeSettings.CheckinIntervalMinutes * 2 + 5));
        var now = DateTime.UtcNow;
        var soonThreshold = now.AddDays(30);

        var totalDevices = await _db.Devices.CountAsync();
        var onlineDevices = await _db.Devices.CountAsync(d => d.LastSeenAt != null && d.LastSeenAt > onlineThreshold);
        var pendingApprovals = await _db.PendingDevices.CountAsync(p => p.Status == PendingDeviceStatus.Pending);
        var totalCustomers = await _db.Customers.CountAsync();
        var totalGroups = await _db.Groups.CountAsync();

        var activeAlerts = await _db.SoftwareAlerts
            .Where(a => a.AcknowledgedAt == null)
            .CountAsync();

        var expiringLicenses = await _db.LicenseInfos
            .Where(l => l.ExpiresAt != null && l.ExpiresAt <= soonThreshold && l.ExpiresAt > now)
            .CountAsync();

        var expiredLicenses = await _db.LicenseInfos
            .Where(l => l.ExpiresAt != null && l.ExpiresAt <= now)
            .CountAsync();

        var recentAlerts = await _db.SoftwareAlerts
            .Where(a => a.AcknowledgedAt == null)
            .Include(a => a.Device)
            .Include(a => a.BlacklistEntry)
            .OrderByDescending(a => a.DetectedAt)
            .Take(5)
            .Select(a => new
            {
                a.Id,
                DeviceHostname = a.Device.Hostname,
                a.DeviceId,
                a.SoftwareName,
                a.SoftwareVersion,
                a.DetectedAt,
                Rule = a.BlacklistEntry.NamePattern
            })
            .ToListAsync();

        var recentAuditLogs = await _db.AuditLogs
            .OrderByDescending(l => l.Timestamp)
            .Take(10)
            .Select(l => new
            {
                l.Id,
                l.Username,
                l.Action,
                l.EntityType,
                l.EntityId,
                l.Timestamp
            })
            .ToListAsync();

        var devicesByGroup = await _db.Groups
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.Color,
                DeviceCount = g.Devices.Count
            })
            .OrderByDescending(g => g.DeviceCount)
            .ToListAsync();

        var devicesByCustomer = await _db.Customers
            .Select(c => new
            {
                c.Id,
                c.Name,
                DeviceCount = c.Devices.Count
            })
            .OrderByDescending(c => c.DeviceCount)
            .ToListAsync();

        var pendingCommands = await _db.DeviceCommands
            .Where(c => c.Status == CommandStatus.Pending || c.Status == CommandStatus.Sent)
            .CountAsync();

        var devicesWithUpdates = await _db.Devices
            .CountAsync(d => d.PendingUpdatesCount > 0);

        return Ok(new
        {
            devices = new
            {
                total = totalDevices,
                online = onlineDevices,
                offline = totalDevices - onlineDevices,
                pending = pendingApprovals
            },
            customers = totalCustomers,
            groups = totalGroups,
            alerts = new
            {
                softwareAlerts = activeAlerts,
                expiringLicenses,
                expiredLicenses,
                pendingCommands,
                devicesWithUpdates
            },
            recentAlerts,
            recentAuditLogs,
            devicesByGroup,
            devicesByCustomer
        });
    }
}
