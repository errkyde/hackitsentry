using HITSight.Server.Data;
using HITSight.Server.Models;
using HITSight.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/deployment")]
[Authorize]
public class DeploymentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly AgentCommandNotifier _notifier;

    public DeploymentController(AppDbContext db, AuditService audit, AgentCommandNotifier notifier)
    {
        _db = db;
        _audit = audit;
        _notifier = notifier;
    }

    // POST /api/deployment/deploy
    [HttpPost("deploy")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Deploy([FromBody] DeployRequest req)
    {
        if (req.DeviceIds == null || req.DeviceIds.Count == 0)
            return BadRequest(new { message = "Keine Geräte angegeben." });

        var pkg = await _db.SoftwarePackages.FindAsync(req.PackageId);
        if (pkg == null)
            return NotFound(new { message = "Paket nicht gefunden." });

        var validDeviceIds = await _db.Devices
            .Where(d => req.DeviceIds.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync();

        var username = User.Identity?.Name ?? "";
        var camelCase = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var paramsJson = JsonSerializer.Serialize(new { type = pkg.Type, installCmd = pkg.InstallCmd }, camelCase);

        var jobs = new List<DeploymentJob>();
        foreach (var deviceId in validDeviceIds)
        {
            var job = new DeploymentJob
            {
                PackageId = req.PackageId,
                DeviceId = deviceId,
                CreatedBy = username,
                Status = "Queued",
            };
            _db.DeploymentJobs.Add(job);
            jobs.Add(job);

            _db.DeviceCommands.Add(new DeviceCommand
            {
                DeviceId = deviceId,
                CommandType = CommandType.DeployPackage,
                Parameters = paramsJson,
                IssuedByUsername = username,
                Status = CommandStatus.Pending,
            });
        }

        await _db.SaveChangesAsync();

        foreach (var deviceId in validDeviceIds)
            _notifier.NotifyDevice(deviceId);

        await _audit.LogAsync("deployment.deploy", "SoftwarePackage", req.PackageId.ToString(),
            $"package={pkg.Name}, devices={validDeviceIds.Count}");

        return Ok(new { queued = validDeviceIds.Count, jobIds = jobs.Select(j => j.Id) });
    }

    // GET /api/deployment/jobs?deviceId={id}
    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs([FromQuery] Guid? deviceId, [FromQuery] Guid? packageId)
    {
        var query = _db.DeploymentJobs
            .Include(j => j.Package)
            .Include(j => j.Device)
            .AsQueryable();

        if (deviceId.HasValue)
            query = query.Where(j => j.DeviceId == deviceId.Value);

        if (packageId.HasValue)
            query = query.Where(j => j.PackageId == packageId.Value);

        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Take(200)
            .Select(j => new
            {
                j.Id,
                j.Status,
                j.Output,
                j.CreatedBy,
                j.CreatedAt,
                j.ExecutedAt,
                Package = new { j.Package.Id, j.Package.Name, j.Package.Version, j.Package.Type },
                Device = new { j.Device.Id, j.Device.Hostname },
            })
            .ToListAsync();

        return Ok(jobs);
    }
}

public record DeployRequest(Guid PackageId, List<Guid> DeviceIds);
