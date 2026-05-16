using HITSight.Server.Data;
using HITSight.Server.Models;
using HITSight.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/devices")]
[Authorize]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LicenseEncryptionService _encryption;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly AuditService _audit;
    private readonly AgentCommandNotifier _notifier;
    private readonly ITenantContext _tenantCtx;

    public DevicesController(AppDbContext db, LicenseEncryptionService encryption, RuntimeSettings runtimeSettings, AuditService audit, AgentCommandNotifier notifier, ITenantContext tenantCtx)
    {
        _db = db;
        _encryption = encryption;
        _runtimeSettings = runtimeSettings;
        _audit = audit;
        _notifier = notifier;
        _tenantCtx = tenantCtx;
    }

    // GET /api/devices
    [HttpGet]
    public async Task<IActionResult> GetDevices(
        [FromQuery] string? search,
        [FromQuery] Guid? groupId,
        [FromQuery] Guid? customerId,
        [FromQuery] string? status,
        [FromQuery] string? os,
        [FromQuery] double? minRam,
        [FromQuery] double? maxRam,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var onlineThreshold = DateTime.UtcNow.AddMinutes(-(_runtimeSettings.CheckinIntervalMinutes * 2 + 5));

        var query = _db.Devices
            .Include(d => d.Customer)
            .Include(d => d.Group)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(d => d.Hostname.Contains(search) || d.Description.Contains(search));

        if (groupId.HasValue)
            query = query.Where(d => d.GroupId == groupId);

        if (customerId.HasValue)
            query = query.Where(d => d.CustomerId == customerId);

        if (!string.IsNullOrWhiteSpace(os))
            query = query.Where(d => d.WindowsVersion.Contains(os));

        if (minRam.HasValue)
            query = query.Where(d => d.RamTotalGB >= minRam);

        if (maxRam.HasValue)
            query = query.Where(d => d.RamTotalGB <= maxRam);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var isOnline = status.Equals("online", StringComparison.OrdinalIgnoreCase);
            if (isOnline)
                query = query.Where(d => d.LastSeenAt != null && d.LastSeenAt > onlineThreshold);
            else
                query = query.Where(d => d.LastSeenAt == null || d.LastSeenAt <= onlineThreshold);
        }

        var total = await query.CountAsync();
        var devices = await query
            .OrderBy(d => d.Hostname)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            total,
            page,
            pageSize,
            items = devices.Select(d => MapToListDto(d, onlineThreshold))
        });
    }

    // GET /api/devices/stats
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var onlineThreshold = DateTime.UtcNow.AddMinutes(-(_runtimeSettings.CheckinIntervalMinutes * 2 + 5));
        var total = await _db.Devices.CountAsync();
        var online = await _db.Devices.CountAsync(d => d.LastSeenAt != null && d.LastSeenAt > onlineThreshold);
        var pending = await _db.PendingDevices.CountAsync(p => p.Status == PendingDeviceStatus.Pending);
        return Ok(new { total, online, offline = total - online, pending });
    }

    // GET /api/devices/pending
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var pending = await _db.PendingDevices
            .Where(p => p.Status == PendingDeviceStatus.Pending)
            .OrderByDescending(p => p.RequestedAt)
            .ToListAsync();

        return Ok(pending.Select(p => new
        {
            p.Id,
            p.Hostname,
            p.WindowsVersion,
            p.CpuModel,
            p.RamTotalGB,
            p.RequestedAt,
            p.Status,
            p.InvitedByUsername,
            p.DeployKeyName,
        }));
    }

    // GET /api/devices/pending/count
    [HttpGet("pending/count")]
    public async Task<IActionResult> GetPendingCount()
    {
        var count = await _db.PendingDevices.CountAsync(p => p.Status == PendingDeviceStatus.Pending);
        return Ok(new { count });
    }

    // GET /api/devices/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDevice(Guid id)
    {
        var onlineThreshold = DateTime.UtcNow.AddMinutes(-(_runtimeSettings.CheckinIntervalMinutes * 2 + 5));

        var device = await _db.Devices
            .Include(d => d.Customer)
            .Include(d => d.Group)
            .Include(d => d.Checkins.OrderByDescending(c => c.CheckedInAt).Take(50))
            .FirstOrDefaultAsync(d => d.Id == id);

        if (device == null)
            return NotFound();

        return Ok(MapToDetailDto(device, onlineThreshold));
    }

    // GET /api/devices/{id}/history
    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> GetHistory(Guid id, [FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 90);
        var since = DateTime.UtcNow.AddDays(-days);
        var thresholdMinutes = _runtimeSettings.CheckinIntervalMinutes * 2 + 5;

        var raw = await _db.DeviceCheckins
            .Where(c => c.DeviceId == id && c.CheckedInAt >= since)
            .OrderBy(c => c.CheckedInAt)
            .Select(c => new { c.CheckedInAt, c.RamUsedGB })
            .Take(5000)
            .ToListAsync();

        // Downsample to hourly buckets for ranges > 7 days to keep response small
        var checkins = days > 7
            ? raw
                .GroupBy(c => new DateTime(c.CheckedInAt.Year, c.CheckedInAt.Month, c.CheckedInAt.Day, c.CheckedInAt.Hour, 0, 0, DateTimeKind.Utc))
                .Select(g => new { checkedInAt = g.Key, ramUsedGB = Math.Round(g.Average(c => c.RamUsedGB), 2) })
                .OrderBy(c => c.checkedInAt)
                .ToList<object>()
            : raw.ToList<object>();

        var downtimes = new List<object>();
        for (int i = 1; i < raw.Count; i++)
        {
            var gap = raw[i].CheckedInAt - raw[i - 1].CheckedInAt;
            if (gap.TotalMinutes > thresholdMinutes)
            {
                downtimes.Add(new
                {
                    from = raw[i - 1].CheckedInAt,
                    to = raw[i].CheckedInAt,
                    durationMinutes = (int)gap.TotalMinutes
                });
            }
        }

        return Ok(new { checkins, downtimes, thresholdMinutes });
    }

    // PATCH /api/devices/{id}
    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PatchDevice(Guid id, [FromBody] PatchDeviceRequest request)
    {
        var device = await _db.Devices.FindAsync(id);
        if (device == null)
            return NotFound();

        if (request.Description != null)
            device.Description = request.Description;
        device.CustomerId = request.CustomerId;
        device.GroupId = request.GroupId;
        if (request.RustDeskId != null)
            device.RustDeskId = request.RustDeskId;
        if (request.AssetTag != null)
            device.AssetTag = request.AssetTag;
        if (request.Location != null)
            device.Location = request.Location;
        if (request.SerialNumber != null)
            device.SerialNumber = request.SerialNumber;
        if (request.PurchaseDate.HasValue)
            device.PurchaseDate = request.PurchaseDate;
        if (request.WarrantyExpiry.HasValue)
            device.WarrantyExpiry = request.WarrantyExpiry;
        // Allow explicit null to clear dates
        if (request.ClearPurchaseDate) device.PurchaseDate = null;
        if (request.ClearWarrantyExpiry) device.WarrantyExpiry = null;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("device.update", "Device", id.ToString(), $"description={request.Description}");

        return Ok();
    }

    // DELETE /api/devices/{id}
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteDevice(Guid id)
    {
        var device = await _db.Devices.FindAsync(id);
        if (device == null)
            return NotFound();

        await _audit.LogAsync("device.delete", "Device", id.ToString(), device.Hostname);

        _db.Devices.Remove(device);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // PATCH /api/devices/bulk
    [HttpPatch("bulk")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> BulkUpdate([FromBody] BulkUpdateRequest request)
    {
        if (request.DeviceIds == null || request.DeviceIds.Count == 0)
            return BadRequest(new { message = "No device IDs provided." });
        if (request.DeviceIds.Count > 500)
            return BadRequest(new { message = "Cannot update more than 500 devices at once." });

        var devices = await _db.Devices
            .Where(d => request.DeviceIds.Contains(d.Id))
            .ToListAsync();

        foreach (var device in devices)
        {
            if (request.SetCustomerId.HasValue)
                device.CustomerId = request.SetCustomerId == Guid.Empty ? null : request.SetCustomerId;
            if (request.SetGroupId.HasValue)
                device.GroupId = request.SetGroupId == Guid.Empty ? null : request.SetGroupId;
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync("device.bulk-update", "Device", null,
            $"devices={request.DeviceIds.Count}, customerId={request.SetCustomerId}, groupId={request.SetGroupId}");

        return Ok(new { updated = devices.Count });
    }

    // DELETE /api/devices/bulk
    [HttpDelete("bulk")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> BulkDelete([FromBody] BulkDeleteRequest request)
    {
        if (request.DeviceIds == null || request.DeviceIds.Count == 0)
            return BadRequest(new { message = "No device IDs provided." });
        if (request.DeviceIds.Count > 500)
            return BadRequest(new { message = "Cannot delete more than 500 devices at once." });

        var devices = await _db.Devices
            .Where(d => request.DeviceIds.Contains(d.Id))
            .ToListAsync();

        await _audit.LogAsync("device.bulk-delete", "Device", null, $"devices={devices.Count}");

        _db.Devices.RemoveRange(devices);
        await _db.SaveChangesAsync();

        return Ok(new { deleted = devices.Count });
    }

    // POST /api/devices/pending/{id}/approve
    [HttpPost("pending/{id:guid}/approve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ApprovePending(Guid id, [FromBody] ApproveRequest request)
    {
        var pending = await _db.PendingDevices.FindAsync(id);
        if (pending == null)
            return NotFound();
        if (pending.Status != PendingDeviceStatus.Pending)
            return BadRequest(new { message = "Request is not pending" });

        // Enforce device limit
        if (_tenantCtx.MaxDevices != int.MaxValue)
        {
            var activeCount = await _db.Devices.CountAsync();
            if (activeCount >= _tenantCtx.MaxDevices)
                return StatusCode(429, new { error = "device_limit_reached", limit = _tenantCtx.MaxDevices });
        }

        var apiKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        var device = new Device
        {
            AgentApiKey = apiKey,
            Hostname = pending.Hostname,
            WindowsVersion = pending.WindowsVersion,
            CpuModel = pending.CpuModel,
            RamTotalGB = pending.RamTotalGB,
            CustomerId = request.CustomerId,
            GroupId = request.GroupId
        };

        _db.Devices.Add(device);
        pending.Status = PendingDeviceStatus.Approved;
        pending.ApprovedDeviceId = device.Id;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("device.approve", "Device", device.Id.ToString(), pending.Hostname);

        return Ok(new { deviceId = device.Id });
    }

    // POST /api/devices/pending/{id}/reject
    [HttpPost("pending/{id:guid}/reject")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RejectPending(Guid id)
    {
        var pending = await _db.PendingDevices.FindAsync(id);
        if (pending == null)
            return NotFound();
        if (pending.Status != PendingDeviceStatus.Pending)
            return BadRequest(new { message = "Request is not pending" });

        pending.Status = PendingDeviceStatus.Rejected;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("device.reject", "PendingDevice", id.ToString(), pending.Hostname);

        return Ok();
    }

    // GET /api/devices/{id}/software
    [HttpGet("{id:guid}/software")]
    public async Task<IActionResult> GetSoftware(Guid id)
    {
        var software = await _db.InstalledSoftware
            .Where(s => s.DeviceId == id)
            .OrderBy(s => s.Name)
            .ToListAsync();

        return Ok(software.Select(s => new
        {
            s.Id,
            s.Name,
            s.Version,
            s.Publisher,
            s.InstallDate,
            s.UpdatedAt
        }));
    }

    // POST /api/devices/{id}/request-license
    [HttpPost("{id:guid}/request-license")]
    public async Task<IActionResult> RequestLicense(Guid id)
    {
        var device = await _db.Devices.FindAsync(id);
        if (device == null)
            return NotFound();

        var command = new DeviceCommand
        {
            DeviceId = id,
            CommandType = CommandType.CollectLicense,
            IssuedByUsername = User.Identity?.Name ?? "",
            Status = CommandStatus.Pending
        };
        _db.DeviceCommands.Add(command);
        await _db.SaveChangesAsync();

        _notifier.NotifyDevice(id);

        await _audit.LogAsync("license.request", "Device", id.ToString());

        return Ok(new { message = "License key request sent to agent." });
    }

    // GET /api/devices/{id}/license
    [HttpGet("{id:guid}/license")]
    public async Task<IActionResult> GetLicense(Guid id)
    {
        var license = await _db.LicenseInfos.FirstOrDefaultAsync(l => l.DeviceId == id);
        if (license == null)
            return Ok((object?)null);

        await _audit.LogAsync("license.view", "Device", id.ToString());

        return Ok(new
        {
            license.Id,
            windowsKey = license.WindowsKeyEncrypted != null
                ? _encryption.Decrypt(license.WindowsKeyEncrypted)
                : null,
            license.LicenseType,
            officeKey = license.OfficeKeyEncrypted != null
                ? _encryption.Decrypt(license.OfficeKeyEncrypted)
                : null,
            license.OfficeVersion,
            license.FetchedAt,
            license.ExpiresAt
        });
    }

    // PATCH /api/devices/{id}/license/expiry
    [HttpPatch("{id:guid}/license/expiry")]
    public async Task<IActionResult> SetLicenseExpiry(Guid id, [FromBody] LicenseExpiryRequest request)
    {
        var license = await _db.LicenseInfos.FirstOrDefaultAsync(l => l.DeviceId == id);
        if (license == null)
            return NotFound(new { message = "No license info available" });

        license.ExpiresAt = request.ExpiresAt;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("license.expiry-set", "Device", id.ToString(), request.ExpiresAt?.ToString("O"));

        return Ok();
    }

    // --- Notes ---

    // GET /api/devices/{id}/notes
    [HttpGet("{id:guid}/notes")]
    public async Task<IActionResult> GetNotes(Guid id)
    {
        var notes = await _db.DeviceNotes
            .Where(n => n.DeviceId == id)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new { n.Id, n.Content, n.AuthorUsername, n.CreatedAt })
            .ToListAsync();

        return Ok(notes);
    }

    // POST /api/devices/{id}/notes
    [HttpPost("{id:guid}/notes")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AddNote(Guid id, [FromBody] AddNoteRequest request)
    {
        if (!await _db.Devices.AnyAsync(d => d.Id == id))
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { message = "Content is required." });

        var note = new DeviceNote
        {
            DeviceId = id,
            Content = request.Content,
            AuthorUsername = User.Identity?.Name ?? ""
        };

        _db.DeviceNotes.Add(note);
        await _db.SaveChangesAsync();

        return Ok(new { note.Id, note.Content, note.AuthorUsername, note.CreatedAt });
    }

    // DELETE /api/devices/{id}/notes/{noteId}
    [HttpDelete("{id:guid}/notes/{noteId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteNote(Guid id, Guid noteId)
    {
        var note = await _db.DeviceNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.DeviceId == id);
        if (note == null)
            return NotFound();

        _db.DeviceNotes.Remove(note);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // --- Commands ---

    // GET /api/devices/{id}/commands
    [HttpGet("{id:guid}/commands")]
    public async Task<IActionResult> GetCommands(Guid id)
    {
        var commands = await _db.DeviceCommands
            .Where(c => c.DeviceId == id)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                CommandType = c.CommandType.ToString(),
                Status = c.Status.ToString(),
                c.Parameters,
                c.IssuedByUsername,
                c.CreatedAt,
                c.ScheduledFor,
                c.ExecutedAt,
                c.Result
            })
            .ToListAsync();

        return Ok(commands);
    }

    // POST /api/devices/{id}/commands
    [HttpPost("{id:guid}/commands")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> IssueCommand(Guid id, [FromBody] IssueCommandRequest request)
    {
        if (!await _db.Devices.AnyAsync(d => d.Id == id))
            return NotFound();

        if (!Enum.TryParse<CommandType>(request.CommandType, true, out var commandType))
            return BadRequest(new { message = $"Unknown command type: {request.CommandType}" });

        var command = new DeviceCommand
        {
            DeviceId = id,
            CommandType = commandType,
            Parameters = request.Parameters,
            IssuedByUsername = User.Identity?.Name ?? "",
            Status = CommandStatus.Pending,
            ScheduledFor = request.ScheduledFor?.ToUniversalTime()
        };

        _db.DeviceCommands.Add(command);
        await _db.SaveChangesAsync();

        _notifier.NotifyDevice(id);

        await _audit.LogAsync("command.issue", "Device", id.ToString(),
            $"type={commandType}, params={request.Parameters}");

        return Ok(new { command.Id });
    }

    // POST /api/devices/bulk-command
    [HttpPost("bulk-command")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> BulkCommand([FromBody] BulkCommandRequest request)
    {
        if (request.DeviceIds == null || request.DeviceIds.Count == 0)
            return BadRequest(new { message = "No device IDs provided." });
        if (request.DeviceIds.Count > 500)
            return BadRequest(new { message = "Cannot send commands to more than 500 devices at once." });
        if (!Enum.TryParse<CommandType>(request.CommandType, true, out var commandType))
            return BadRequest(new { message = $"Unknown command type: {request.CommandType}" });

        var validIds = await _db.Devices
            .Where(d => request.DeviceIds.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync();

        var username = User.Identity?.Name ?? "";
        foreach (var deviceId in validIds)
        {
            _db.DeviceCommands.Add(new DeviceCommand
            {
                DeviceId = deviceId,
                CommandType = commandType,
                Parameters = request.Parameters,
                IssuedByUsername = username,
                Status = CommandStatus.Pending,
                ScheduledFor = request.ScheduledFor?.ToUniversalTime()
            });
        }

        await _db.SaveChangesAsync();

        foreach (var deviceId in validIds)
            _notifier.NotifyDevice(deviceId);

        await _audit.LogAsync("command.bulk", "Device", null,
            $"type={commandType}, devices={validIds.Count}, params={request.Parameters}");

        return Ok(new { queued = validIds.Count });
    }

    private static object MapToListDto(Device d, DateTime onlineThreshold) => new
    {
        d.Id,
        d.Hostname,
        d.Description,
        d.WindowsVersion,
        d.WindowsBuild,
        d.WindowsEdition,
        d.CpuModel,
        d.CpuCores,
        d.RamTotalGB,
        d.LastSeenAt,
        d.LicenseType,
        d.RustDeskId,
        d.DefenderStatusJson,
        d.PendingUpdatesCount,
        d.LastWindowsUpdateInstalled,
        IsOnline = d.LastSeenAt.HasValue && d.LastSeenAt > onlineThreshold,
        Customer = d.Customer == null ? null : new { d.Customer.Id, d.Customer.Name },
        Group = d.Group == null ? null : new { d.Group.Id, d.Group.Name, d.Group.Color }
    };

    private static object MapToDetailDto(Device d, DateTime onlineThreshold) => new
    {
        d.Id,
        d.Hostname,
        d.Description,
        d.WindowsVersion,
        d.WindowsBuild,
        d.WindowsEdition,
        d.CpuModel,
        d.CpuCores,
        d.RamTotalGB,
        d.LastSeenAt,
        d.LicenseType,
        d.LicenseRequested,
        d.RustDeskId,
        d.AgentVersion,
        d.NetworkAdaptersJson,
        d.CreatedAt,
        d.LastDiskAlertAt,
        d.DiskAlertAcknowledgedUsedPct,
        d.RustDeskOptionsJson,
        d.BiosInfoJson,
        d.DefenderStatusJson,
        d.AssetTag,
        d.Location,
        d.SerialNumber,
        d.PurchaseDate,
        d.WarrantyExpiry,
        d.PendingUpdatesCount,
        d.LastWindowsUpdateInstalled,
        d.EventLogErrorsJson,
        IsOnline = d.LastSeenAt.HasValue && d.LastSeenAt > onlineThreshold,
        Customer = d.Customer == null ? null : new { d.Customer.Id, d.Customer.Name },
        Group = d.Group == null ? null : new { d.Group.Id, d.Group.Name, d.Group.Color },
        RecentCheckins = d.Checkins.OrderByDescending(c => c.CheckedInAt).Take(10).Select(c => new
        {
            c.CheckedInAt,
            c.RamUsedGB,
            c.DiskDrivesJson
        })
    };

    // PATCH /api/devices/{id}/rustdesk-options
    [HttpPatch("{id:guid}/rustdesk-options")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PatchRustDeskOptions(Guid id, [FromBody] RustDeskOptionsRequest request)
    {
        var device = await _db.Devices.FindAsync(id);
        if (device == null)
            return NotFound();

        device.RustDeskOptionsJson = request.Options != null && request.Options.Count > 0
            ? JsonSerializer.Serialize(request.Options)
            : null;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("device.rustdesk-options", "Device", id.ToString());

        return Ok();
    }

    // POST /api/devices/{id}/acknowledge-disk-alert
    [HttpPost("{id:guid}/acknowledge-disk-alert")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AcknowledgeDiskAlert(Guid id)
    {
        var device = await _db.Devices
            .Include(d => d.Checkins.OrderByDescending(c => c.CheckedInAt).Take(1))
            .FirstOrDefaultAsync(d => d.Id == id);

        if (device == null) return NotFound();

        var latestCheckin = device.Checkins.FirstOrDefault();
        double usedPct = 0;
        if (latestCheckin != null)
        {
            var camelCase = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var driveOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var drives = JsonSerializer.Deserialize<List<DiskDriveAck>>(latestCheckin.DiskDrivesJson, driveOptions) ?? [];
            var validDrives = drives.Where(d => d.TotalGB > 0).ToList();
            if (validDrives.Count > 0)
            {
                var total = validDrives.Sum(d => d.TotalGB);
                var free  = validDrives.Sum(d => d.FreeGB);
                usedPct = (total - free) / total * 100;
            }
        }

        device.DiskAlertAcknowledgedUsedPct = usedPct;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("disk-alert.acknowledge", "Device", id.ToString(),
            $"acknowledgedAtUsedPct={usedPct:F1}");

        return Ok(new { acknowledgedAtUsedPct = usedPct });
    }
}

file record DiskDriveAck(string Drive, double TotalGB, double FreeGB);

public record PatchDeviceRequest(
    string? Description,
    Guid? CustomerId,
    Guid? GroupId,
    string? RustDeskId = null,
    string? AssetTag = null,
    string? Location = null,
    string? SerialNumber = null,
    DateTime? PurchaseDate = null,
    DateTime? WarrantyExpiry = null,
    bool ClearPurchaseDate = false,
    bool ClearWarrantyExpiry = false
);
public record ApproveRequest(Guid? CustomerId, Guid? GroupId);
public record BulkUpdateRequest(List<Guid> DeviceIds, Guid? SetCustomerId, Guid? SetGroupId);
public record BulkDeleteRequest(List<Guid> DeviceIds);
public record AddNoteRequest(string Content);
public record IssueCommandRequest(string CommandType, string? Parameters, DateTime? ScheduledFor = null);
public record BulkCommandRequest(List<Guid> DeviceIds, string CommandType, string? Parameters, DateTime? ScheduledFor = null);
public record LicenseExpiryRequest(DateTime? ExpiresAt);
public record RustDeskOptionsRequest(Dictionary<string, string>? Options);
