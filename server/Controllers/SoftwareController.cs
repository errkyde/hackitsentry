using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using HackITSentry.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackITSentry.Server.Controllers;

[ApiController]
[Route("api/software")]
[Authorize]
public class SoftwareController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public SoftwareController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // GET /api/software?name=&publisher=&customerId=&groupId=
    [HttpGet]
    public async Task<IActionResult> GetInventory(
        [FromQuery] string? name,
        [FromQuery] string? publisher,
        [FromQuery] Guid? customerId,
        [FromQuery] Guid? groupId)
    {
        var query = _db.InstalledSoftware
            .Include(s => s.Device)
            .ThenInclude(d => d.Customer)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(s => s.Name.Contains(name));

        if (!string.IsNullOrWhiteSpace(publisher))
            query = query.Where(s => s.Publisher.Contains(publisher));

        if (customerId.HasValue)
            query = query.Where(s => s.Device.CustomerId == customerId);

        if (groupId.HasValue)
            query = query.Where(s => s.Device.GroupId == groupId);

        var results = await query
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Device.Hostname)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Version,
                s.Publisher,
                s.InstallDate,
                Device = new { s.Device.Id, s.Device.Hostname },
                Customer = s.Device.Customer == null ? null : new { s.Device.Customer.Id, s.Device.Customer.Name }
            })
            .ToListAsync();

        return Ok(results);
    }

    // GET /api/software/summary - grouped by software name
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] string? name)
    {
        var query = _db.InstalledSoftware.AsQueryable();

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(s => s.Name.Contains(name));

        var summary = await query
            .GroupBy(s => new { s.Name, s.Publisher })
            .Select(g => new
            {
                g.Key.Name,
                g.Key.Publisher,
                DeviceCount = g.Select(s => s.DeviceId).Distinct().Count(),
                Versions = g.Select(s => s.Version).Distinct().OrderBy(v => v).ToList()
            })
            .OrderByDescending(g => g.DeviceCount)
            .ThenBy(g => g.Name)
            .ToListAsync();

        return Ok(summary);
    }

    // --- Blacklist ---

    // GET /api/software/blacklist
    [HttpGet("blacklist")]
    public async Task<IActionResult> GetBlacklist()
    {
        var entries = await _db.SoftwareBlacklist
            .OrderBy(e => e.NamePattern)
            .ToListAsync();

        return Ok(entries.Select(e => new
        {
            e.Id,
            e.NamePattern,
            e.Publisher,
            e.Reason,
            e.AddedByUsername,
            e.AddedAt
        }));
    }

    // POST /api/software/blacklist
    [HttpPost("blacklist")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AddBlacklist([FromBody] BlacklistRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NamePattern))
            return BadRequest(new { message = "NamePattern is required." });

        var entry = new SoftwareBlacklistEntry
        {
            NamePattern = request.NamePattern,
            Publisher = request.Publisher,
            Reason = request.Reason,
            AddedByUsername = User.Identity?.Name ?? ""
        };

        _db.SoftwareBlacklist.Add(entry);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("blacklist.add", "SoftwareBlacklist", entry.Id.ToString(), request.NamePattern);

        return Ok(new { entry.Id });
    }

    // DELETE /api/software/blacklist/{id}
    [HttpDelete("blacklist/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteBlacklist(Guid id)
    {
        var entry = await _db.SoftwareBlacklist.FindAsync(id);
        if (entry == null) return NotFound();

        _db.SoftwareBlacklist.Remove(entry);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("blacklist.remove", "SoftwareBlacklist", id.ToString(), entry.NamePattern);

        return NoContent();
    }

    // --- Alerts ---

    // GET /api/software/alerts?acknowledged=false
    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts([FromQuery] bool? acknowledged)
    {
        var query = _db.SoftwareAlerts
            .Include(a => a.Device)
            .ThenInclude(d => d.Customer)
            .Include(a => a.BlacklistEntry)
            .AsQueryable();

        if (acknowledged.HasValue)
        {
            if (acknowledged.Value)
                query = query.Where(a => a.AcknowledgedAt != null);
            else
                query = query.Where(a => a.AcknowledgedAt == null);
        }

        var alerts = await query
            .OrderByDescending(a => a.DetectedAt)
            .Select(a => new
            {
                a.Id,
                a.SoftwareName,
                a.SoftwareVersion,
                a.DetectedAt,
                a.AcknowledgedAt,
                a.AcknowledgedByUsername,
                Device = new { a.Device.Id, a.Device.Hostname },
                Customer = a.Device.Customer == null ? null : new { a.Device.Customer.Id, a.Device.Customer.Name },
                Rule = new { a.BlacklistEntry.Id, a.BlacklistEntry.NamePattern, a.BlacklistEntry.Reason }
            })
            .ToListAsync();

        return Ok(alerts);
    }

    // POST /api/software/alerts/{id}/acknowledge
    [HttpPost("alerts/{id:guid}/acknowledge")]
    public async Task<IActionResult> AcknowledgeAlert(Guid id)
    {
        var alert = await _db.SoftwareAlerts.FindAsync(id);
        if (alert == null) return NotFound();

        alert.AcknowledgedAt = DateTime.UtcNow;
        alert.AcknowledgedByUsername = User.Identity?.Name ?? "";
        await _db.SaveChangesAsync();

        return Ok();
    }

    // POST /api/software/alerts/acknowledge-all
    [HttpPost("alerts/acknowledge-all")]
    public async Task<IActionResult> AcknowledgeAll()
    {
        var unacknowledged = await _db.SoftwareAlerts
            .Where(a => a.AcknowledgedAt == null)
            .ToListAsync();

        var username = User.Identity?.Name ?? "";
        var now = DateTime.UtcNow;

        foreach (var alert in unacknowledged)
        {
            alert.AcknowledgedAt = now;
            alert.AcknowledgedByUsername = username;
        }

        await _db.SaveChangesAsync();

        return Ok(new { acknowledged = unacknowledged.Count });
    }
}

public record BlacklistRequest(string NamePattern, string? Publisher, string? Reason);
