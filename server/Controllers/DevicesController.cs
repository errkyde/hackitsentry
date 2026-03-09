using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using HackITSentry.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HackITSentry.Server.Controllers;

[ApiController]
[Route("api/devices")]
[Authorize]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LicenseEncryptionService _encryption;
    private readonly IConfiguration _config;
    private readonly AuditService _audit;

    public DevicesController(AppDbContext db, LicenseEncryptionService encryption, IConfiguration config, AuditService audit)
    {
        _db = db;
        _encryption = encryption;
        _config = config;
        _audit = audit;
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
        [FromQuery] double? maxRam)
    {
        var onlineThreshold = DateTime.UtcNow.AddMinutes(-(_config.GetValue<int>("CheckinIntervalMinutes", 30) * 2 + 5));

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

        var devices = await query.OrderBy(d => d.Hostname).ToListAsync();

        return Ok(devices.Select(d => MapToListDto(d, onlineThreshold)));
    }

    // GET /api/devices/stats
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var onlineThreshold = DateTime.UtcNow.AddMinutes(-(_config.GetValue<int>("CheckinIntervalMinutes", 30) * 2 + 5));
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
            p.Status
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
        var onlineThreshold = DateTime.UtcNow.AddMinutes(-(_config.GetValue<int>("CheckinIntervalMinutes", 30) * 2 + 5));

        var device = await _db.Devices
            .Include(d => d.Customer)
            .Include(d => d.Group)
            .Include(d => d.Checkins.OrderByDescending(c => c.CheckedInAt).Take(50))
            .FirstOrDefaultAsync(d => d.Id == id);

        if (device == null)
            return NotFound();

        return Ok(MapToDetailDto(device, onlineThreshold));
    }

    // PATCH /api/devices/{id}
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> PatchDevice(Guid id, [FromBody] PatchDeviceRequest request)
    {
        var device = await _db.Devices.FindAsync(id);
        if (device == null)
            return NotFound();

        if (request.Description != null)
            device.Description = request.Description;
        device.CustomerId = request.CustomerId;
        device.GroupId = request.GroupId;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("device.update", "Device", id.ToString(), $"description={request.Description}");

        return Ok();
    }

    // DELETE /api/devices/{id}
    [HttpDelete("{id:guid}")]
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
    public async Task<IActionResult> BulkUpdate([FromBody] BulkUpdateRequest request)
    {
        if (request.DeviceIds == null || request.DeviceIds.Count == 0)
            return BadRequest(new { message = "No device IDs provided." });

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
    public async Task<IActionResult> BulkDelete([FromBody] BulkDeleteRequest request)
    {
        if (request.DeviceIds == null || request.DeviceIds.Count == 0)
            return BadRequest(new { message = "No device IDs provided." });

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
    public async Task<IActionResult> ApprovePending(Guid id, [FromBody] ApproveRequest request)
    {
        var pending = await _db.PendingDevices.FindAsync(id);
        if (pending == null)
            return NotFound();
        if (pending.Status != PendingDeviceStatus.Pending)
            return BadRequest(new { message = "Request is not pending" });

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

        device.LicenseRequested = true;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("license.request", "Device", id.ToString());

        return Ok(new { message = "License key request queued. Waiting for agent check-in." });
    }

    // GET /api/devices/{id}/license
    [HttpGet("{id:guid}/license")]
    public async Task<IActionResult> GetLicense(Guid id)
    {
        var license = await _db.LicenseInfos.FirstOrDefaultAsync(l => l.DeviceId == id);
        if (license == null)
            return NotFound(new { message = "No license info available" });

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
                c.ExecutedAt,
                c.Result
            })
            .ToListAsync();

        return Ok(commands);
    }

    // POST /api/devices/{id}/commands
    [HttpPost("{id:guid}/commands")]
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
            Status = CommandStatus.Pending
        };

        _db.DeviceCommands.Add(command);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("command.issue", "Device", id.ToString(),
            $"type={commandType}, params={request.Parameters}");

        return Ok(new { command.Id });
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
        d.NetworkAdaptersJson,
        d.CreatedAt,
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
}

public record PatchDeviceRequest(string? Description, Guid? CustomerId, Guid? GroupId);
public record ApproveRequest(Guid? CustomerId, Guid? GroupId);
public record BulkUpdateRequest(List<Guid> DeviceIds, Guid? SetCustomerId, Guid? SetGroupId);
public record BulkDeleteRequest(List<Guid> DeviceIds);
public record AddNoteRequest(string Content);
public record IssueCommandRequest(string CommandType, string? Parameters);
public record LicenseExpiryRequest(DateTime? ExpiresAt);
