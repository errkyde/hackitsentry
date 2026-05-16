using HITSight.Server.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/login-message")]
[Authorize]
public class LoginMessageController : ControllerBase
{
    private readonly AppDbContext _db;

    public LoginMessageController(AppDbContext db) => _db = db;

    // Returns the pending login message (if any) and clears it — show-once behavior
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "Platform:LoginMessage");
        if (setting == null) return Ok(new { message = (string?)null });

        var message = setting.Value;
        _db.AppSettings.Remove(setting);
        await _db.SaveChangesAsync();

        return Ok(new { message });
    }
}
