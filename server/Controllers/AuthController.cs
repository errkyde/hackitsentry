using HackITSentry.Server.Data;
using HackITSentry.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

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

    [HttpGet("setup-required")]
    public IActionResult SetupRequired()
    {
        return Ok(new { required = !_db.Users.Any() });
    }

    [HttpPost("setup")]
    public IActionResult Setup([FromBody] SetupRequest request)
    {
        if (_db.Users.Any())
            return BadRequest(new { message = "Setup bereits abgeschlossen" });

        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length < 3)
            return BadRequest(new { message = "Benutzername muss mindestens 3 Zeichen lang sein" });

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            return BadRequest(new { message = "Passwort muss mindestens 6 Zeichen lang sein" });

        var user = new HackITSentry.Server.Models.User
        {
            Username = request.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = "Admin"
        };
        _db.Users.Add(user);
        _db.SaveChanges();

        var token = _jwt.GenerateToken(user.Id.ToString(), user.Username, user.Role);
        return Ok(new { token, username = user.Username, role = user.Role });
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var user = _db.Users.FirstOrDefault(u => u.Username == request.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { message = "Ungültige Anmeldedaten" });

        var token = _jwt.GenerateToken(user.Id.ToString(), user.Username, user.Role);
        return Ok(new { token, username = user.Username, role = user.Role });
    }

    [HttpPost("change-password")]
    [Authorize]
    public IActionResult ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = _db.Users.Find(Guid.Parse(userId!));
        if (user == null)
            return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "Aktuelles Passwort ist falsch" });

        if (request.NewPassword.Length < 6)
            return BadRequest(new { message = "Neues Passwort muss mindestens 6 Zeichen lang sein" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        _db.SaveChanges();

        return Ok(new { message = "Passwort erfolgreich geändert" });
    }
}

public record LoginRequest(string Username, string Password);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record SetupRequest(string Username, string Password);
