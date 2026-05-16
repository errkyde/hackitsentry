using HITSight.Server.Data;
using HITSight.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/script-templates")]
[Authorize]
public class ScriptTemplatesController : ControllerBase
{
    private readonly AppDbContext _db;

    public ScriptTemplatesController(AppDbContext db) => _db = db;

    // GET /api/script-templates
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var templates = await _db.ScriptTemplates
            .OrderBy(t => t.Name)
            .Select(t => new { t.Id, t.Name, t.Description, t.CreatedBy, t.CreatedAt })
            .ToListAsync();
        return Ok(templates);
    }

    // GET /api/script-templates/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var t = await _db.ScriptTemplates.FindAsync(id);
        return t == null ? NotFound() : Ok(t);
    }

    // POST /api/script-templates
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] ScriptTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Name ist erforderlich." });
        if (string.IsNullOrWhiteSpace(request.Script))
            return BadRequest(new { message = "Script ist erforderlich." });

        var template = new ScriptTemplate
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? "",
            Script = request.Script,
            CreatedBy = User.Identity?.Name ?? ""
        };

        _db.ScriptTemplates.Add(template);
        await _db.SaveChangesAsync();

        return Ok(new { template.Id });
    }

    // PUT /api/script-templates/{id}
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ScriptTemplateRequest request)
    {
        var template = await _db.ScriptTemplates.FindAsync(id);
        if (template == null) return NotFound();

        template.Name = request.Name.Trim();
        template.Description = request.Description?.Trim() ?? "";
        template.Script = request.Script;

        await _db.SaveChangesAsync();
        return Ok();
    }

    // DELETE /api/script-templates/{id}
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var template = await _db.ScriptTemplates.FindAsync(id);
        if (template == null) return NotFound();

        _db.ScriptTemplates.Remove(template);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record ScriptTemplateRequest(string Name, string? Description, string Script);
