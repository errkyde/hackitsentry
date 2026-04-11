using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackITSentry.Server.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly AppDbContext _db;

    public CustomersController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var customers = await _db.Customers
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.ContactEmail,
                c.CreatedAt,
                DeviceCount = c.Devices.Count
            })
            .OrderBy(c => c.Name)
            .ToListAsync();

        return Ok(customers);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var customer = await _db.Customers.FindAsync(id);
        if (customer == null)
            return NotFound();
        return Ok(customer);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CustomerRequest request)
    {
        var customer = new Customer
        {
            Name = request.Name,
            ContactEmail = request.ContactEmail
        };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CustomerRequest request)
    {
        var customer = await _db.Customers.FindAsync(id);
        if (customer == null)
            return NotFound();

        customer.Name = request.Name;
        customer.ContactEmail = request.ContactEmail;
        await _db.SaveChangesAsync();
        return Ok(customer);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var customer = await _db.Customers.FindAsync(id);
        if (customer == null)
            return NotFound();

        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record CustomerRequest(string Name, string ContactEmail);
