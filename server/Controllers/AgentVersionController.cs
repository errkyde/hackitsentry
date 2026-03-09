using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using HackITSentry.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackITSentry.Server.Controllers;

[ApiController]
[Route("api/agent-versions")]
public class AgentVersionController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public AgentVersionController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // GET /api/agent-versions/latest  (no auth, used by agent)
    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest()
    {
        var latest = await _db.AgentVersions
            .Where(v => v.IsLatest)
            .FirstOrDefaultAsync();

        if (latest == null)
            return Ok(new { version = (string?)null });

        return Ok(new
        {
            latest.Id,
            latest.Version,
            latest.DownloadUrl,
            latest.Changelog,
            latest.ReleasedAt
        });
    }

    // GET /api/agent-versions  (admin)
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetAll()
    {
        var versions = await _db.AgentVersions
            .OrderByDescending(v => v.ReleasedAt)
            .ToListAsync();

        return Ok(versions.Select(v => new
        {
            v.Id,
            v.Version,
            v.DownloadUrl,
            v.Changelog,
            v.IsLatest,
            v.ReleasedAt
        }));
    }

    // POST /api/agent-versions  (admin)
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] AgentVersionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Version))
            return BadRequest(new { message = "Version is required." });

        // If this is set as latest, unset all others
        if (request.IsLatest)
        {
            var currentLatest = await _db.AgentVersions.Where(v => v.IsLatest).ToListAsync();
            foreach (var v in currentLatest)
                v.IsLatest = false;
        }

        var version = new AgentVersion
        {
            Version = request.Version,
            DownloadUrl = request.DownloadUrl,
            Changelog = request.Changelog,
            IsLatest = request.IsLatest,
            ReleasedAt = DateTime.UtcNow
        };

        _db.AgentVersions.Add(version);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("agent-version.create", "AgentVersion", version.Id.ToString(), request.Version);

        return Ok(new { version.Id });
    }

    // PATCH /api/agent-versions/{id}/set-latest  (admin)
    [HttpPatch("{id:guid}/set-latest")]
    [Authorize]
    public async Task<IActionResult> SetLatest(Guid id)
    {
        var target = await _db.AgentVersions.FindAsync(id);
        if (target == null) return NotFound();

        var currentLatest = await _db.AgentVersions.Where(v => v.IsLatest).ToListAsync();
        foreach (var v in currentLatest)
            v.IsLatest = false;

        target.IsLatest = true;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("agent-version.set-latest", "AgentVersion", id.ToString(), target.Version);

        return Ok();
    }

    // DELETE /api/agent-versions/{id}  (admin)
    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id)
    {
        var version = await _db.AgentVersions.FindAsync(id);
        if (version == null) return NotFound();

        _db.AgentVersions.Remove(version);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}

public record AgentVersionRequest(string Version, string? DownloadUrl, string? Changelog, bool IsLatest);
