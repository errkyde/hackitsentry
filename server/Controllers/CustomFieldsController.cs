using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackITSentry.Server.Controllers;

[ApiController]
[Route("api/custom-fields")]
[Authorize]
public class CustomFieldsController : ControllerBase
{
    private readonly AppDbContext _db;

    public CustomFieldsController(AppDbContext db) => _db = db;

    // GET /api/custom-fields
    [HttpGet]
    public async Task<IActionResult> GetDefinitions()
    {
        var defs = await _db.CustomFieldDefinitions
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .Select(d => new { d.Id, d.Name, d.SortOrder })
            .ToListAsync();
        return Ok(defs);
    }

    // POST /api/custom-fields
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateDefinition([FromBody] CustomFieldDefinitionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "Name darf nicht leer sein." });

        var maxOrder = await _db.CustomFieldDefinitions.AnyAsync()
            ? await _db.CustomFieldDefinitions.MaxAsync(d => d.SortOrder)
            : -1;

        var def = new CustomFieldDefinition { Name = req.Name.Trim(), SortOrder = maxOrder + 1 };
        _db.CustomFieldDefinitions.Add(def);
        await _db.SaveChangesAsync();
        return Ok(new { def.Id, def.Name, def.SortOrder });
    }

    // DELETE /api/custom-fields/{id}
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteDefinition(Guid id)
    {
        var def = await _db.CustomFieldDefinitions.FindAsync(id);
        if (def == null) return NotFound();
        _db.CustomFieldDefinitions.Remove(def);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // GET /api/custom-fields/values/{deviceId}
    [HttpGet("values/{deviceId:guid}")]
    public async Task<IActionResult> GetValues(Guid deviceId)
    {
        var defs = await _db.CustomFieldDefinitions
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .ToListAsync();

        var values = await _db.CustomFieldValues
            .Where(v => v.DeviceId == deviceId)
            .ToListAsync();

        var result = defs.Select(d => new
        {
            d.Id,
            d.Name,
            d.SortOrder,
            value = values.FirstOrDefault(v => v.DefinitionId == d.Id)?.Value ?? ""
        });

        return Ok(result);
    }

    // PUT /api/custom-fields/values/{deviceId}
    [HttpPut("values/{deviceId:guid}")]
    public async Task<IActionResult> SaveValues(Guid deviceId, [FromBody] List<CustomFieldValueRequest> req)
    {
        var device = await _db.Devices.FindAsync(deviceId);
        if (device == null) return NotFound();

        foreach (var item in req)
        {
            var existing = await _db.CustomFieldValues
                .FirstOrDefaultAsync(v => v.DeviceId == deviceId && v.DefinitionId == item.DefinitionId);

            if (string.IsNullOrWhiteSpace(item.Value))
            {
                if (existing != null)
                    _db.CustomFieldValues.Remove(existing);
            }
            else if (existing != null)
            {
                existing.Value = item.Value.Trim();
            }
            else
            {
                _db.CustomFieldValues.Add(new CustomFieldValue
                {
                    DeviceId = deviceId,
                    DefinitionId = item.DefinitionId,
                    Value = item.Value.Trim()
                });
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Gespeichert." });
    }
}

public record CustomFieldDefinitionRequest(string Name);
public record CustomFieldValueRequest(Guid DefinitionId, string Value);
