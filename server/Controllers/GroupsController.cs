using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackITSentry.Server.Controllers;

[ApiController]
[Route("api/groups")]
[Authorize]
public class GroupsController : ControllerBase
{
    private readonly AppDbContext _db;

    public GroupsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var groups = await _db.Groups
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.Description,
                g.Color,
                g.CreatedAt,
                DeviceCount = g.Devices.Count
            })
            .OrderBy(g => g.Name)
            .ToListAsync();

        return Ok(groups);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var group = await _db.Groups.FindAsync(id);
        if (group == null)
            return NotFound();
        return Ok(group);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] GroupRequest request)
    {
        var group = new DeviceGroup
        {
            Name = request.Name,
            Description = request.Description,
            Color = request.Color
        };
        _db.Groups.Add(group);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = group.Id }, group);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] GroupRequest request)
    {
        var group = await _db.Groups.FindAsync(id);
        if (group == null)
            return NotFound();

        group.Name = request.Name;
        group.Description = request.Description;
        group.Color = request.Color;
        await _db.SaveChangesAsync();
        return Ok(group);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var group = await _db.Groups.FindAsync(id);
        if (group == null)
            return NotFound();

        _db.Groups.Remove(group);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record GroupRequest(string Name, string Description, string? Color);
