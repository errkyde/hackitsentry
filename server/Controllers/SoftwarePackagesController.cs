using HITSight.Server.Data;
using HITSight.Server.Models;
using HITSight.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/software-packages")]
[Authorize]
public class SoftwarePackagesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public SoftwarePackagesController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // GET /api/software-packages
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var pkgs = await _db.SoftwarePackages
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id, p.Name, p.Version, p.Type, p.InstallCmd,
                p.UninstallCmd, p.Description, p.CreatedBy, p.CreatedAt
            })
            .ToListAsync();

        return Ok(pkgs);
    }

    // POST /api/software-packages
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] PackageRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.InstallCmd))
            return BadRequest(new { message = "Name und InstallCmd sind erforderlich." });

        var pkg = new SoftwarePackage
        {
            Name = req.Name,
            Version = req.Version ?? "",
            Type = req.Type ?? "winget",
            InstallCmd = req.InstallCmd,
            UninstallCmd = req.UninstallCmd,
            Description = req.Description ?? "",
            CreatedBy = User.Identity?.Name ?? "",
        };

        _db.SoftwarePackages.Add(pkg);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("package.create", "SoftwarePackage", pkg.Id.ToString(), pkg.Name);

        return Ok(new { pkg.Id });
    }

    // PUT /api/software-packages/{id}
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] PackageRequest req)
    {
        var pkg = await _db.SoftwarePackages.FindAsync(id);
        if (pkg == null) return NotFound();

        pkg.Name = req.Name ?? pkg.Name;
        pkg.Version = req.Version ?? pkg.Version;
        pkg.Type = req.Type ?? pkg.Type;
        pkg.InstallCmd = req.InstallCmd ?? pkg.InstallCmd;
        pkg.UninstallCmd = req.UninstallCmd;
        pkg.Description = req.Description ?? pkg.Description;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("package.update", "SoftwarePackage", id.ToString(), pkg.Name);

        return Ok();
    }

    // DELETE /api/software-packages/{id}
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var pkg = await _db.SoftwarePackages.FindAsync(id);
        if (pkg == null) return NotFound();

        _db.SoftwarePackages.Remove(pkg);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("package.delete", "SoftwarePackage", id.ToString(), pkg.Name);

        return NoContent();
    }
}

public record PackageRequest(
    string? Name,
    string? Version,
    string? Type,
    string? InstallCmd,
    string? UninstallCmd,
    string? Description
);
