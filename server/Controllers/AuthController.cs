using HackITSentry.Server.Data;
using HackITSentry.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace HackITSentry.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;

    public AuthController(AppDbContext db, JwtService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var user = _db.Users.FirstOrDefault(u => u.Username == request.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid credentials" });

        var token = _jwt.GenerateToken(user.Id.ToString(), user.Username, user.Role);
        return Ok(new { token, username = user.Username, role = user.Role });
    }
}

public record LoginRequest(string Username, string Password);
