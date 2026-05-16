using HITSight.Server.Data;
using HITSight.Server.Models;
using HITSight.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/agent-versions")]
[Authorize(Roles = "Admin")]
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

    // GET /api/agent-versions/changelog-suggestions  (admin)
    // Returns recent git commits as changelog suggestions
    [HttpGet("changelog-suggestions")]
    [Authorize]
    public IActionResult GetChangelogSuggestions()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git",
                "log --pretty=format:%s -30")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return Ok(new { lines = Array.Empty<string>() });
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(3000);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();
            return Ok(new { lines });
        }
        catch
        {
            return Ok(new { lines = Array.Empty<string>() });
        }
    }

    // PATCH /api/agent-versions/{id}/changelog  (admin)
    [HttpPatch("{id:guid}/changelog")]
    [Authorize]
    public async Task<IActionResult> UpdateChangelog(Guid id, [FromBody] ChangelogRequest request)
    {
        var version = await _db.AgentVersions.FindAsync(id);
        if (version == null) return NotFound();
        version.Changelog = request.Changelog;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("agent-version.changelog", "AgentVersion", id.ToString(), version.Version);
        return Ok();
    }

    // POST /api/agent-versions/publish  (admin)
    // Copies the pre-built agent exe from /app/agent/ into /app/downloads/
    // and registers it as the new latest version in the DB.
    [HttpPost("publish")]
    [Authorize]
    public async Task<IActionResult> Publish([FromServices] IConfiguration config, [FromBody] PublishRequest? request = null)
    {
        var agentExe = Path.Combine(AppContext.BaseDirectory, "agent", "HITSight.Agent.exe");
        var versionFile = Path.Combine(AppContext.BaseDirectory, "agent", "agent-version.txt");

        if (!System.IO.File.Exists(agentExe))
            return StatusCode(503, new { message = "Agent binary not found in server image. Rebuild the server container." });

        var version = System.IO.File.Exists(versionFile)
            ? (await System.IO.File.ReadAllTextAsync(versionFile)).Trim()
            : "1.0.0";

        var downloadsDir = Path.Combine(AppContext.BaseDirectory, "downloads");
        Directory.CreateDirectory(downloadsDir);

        var outFileName = $"HITSight-Agent-{version}.exe";
        var outPath = Path.Combine(downloadsDir, outFileName);
        System.IO.File.Copy(agentExe, outPath, overwrite: true);

        // Build public download URL from OutpostPublicUrl or request origin
        var baseUrl = config["OutpostPublicUrl"]?.TrimEnd('/')
            ?? $"{Request.Scheme}://{Request.Host}";
        var downloadUrl = $"{baseUrl}/downloads/{outFileName}";

        // Mark all existing as non-latest, upsert this version
        var existing = await _db.AgentVersions.FirstOrDefaultAsync(v => v.Version == version);
        var allLatest = await _db.AgentVersions.Where(v => v.IsLatest).ToListAsync();
        foreach (var v in allLatest) v.IsLatest = false;

        if (existing != null)
        {
            existing.DownloadUrl = downloadUrl;
            existing.IsLatest = true;
            existing.ReleasedAt = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(request?.Changelog))
                existing.Changelog = request.Changelog;
        }
        else
        {
            _db.AgentVersions.Add(new AgentVersion
            {
                Version = version,
                DownloadUrl = downloadUrl,
                Changelog = request?.Changelog,
                IsLatest = true,
                ReleasedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync("agent-version.publish", "AgentVersion", version, downloadUrl);

        return Ok(new { version, downloadUrl });
    }
}

public record AgentVersionRequest(string Version, string? DownloadUrl, string? Changelog, bool IsLatest);
public record ChangelogRequest(string? Changelog);
public record PublishRequest(string? Changelog);
