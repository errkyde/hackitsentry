using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HackITSentry.Server.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        var users = _db.Users
            .OrderBy(u => u.Username)
            .Select(u => new { u.Id, u.Username, u.Role, u.CreatedAt, u.IsLocal, u.DisplayName, u.Email })
            .ToList();
        return Ok(users);
    }

    [HttpPost]
    public IActionResult Create([FromBody] CreateUserRequest request)
    {
        if (_db.Users.Any(u => u.Username == request.Username))
            return BadRequest(new { message = "Benutzername bereits vergeben" });

        if (request.Password.Length < 6)
            return BadRequest(new { message = "Passwort muss mindestens 6 Zeichen lang sein" });

        var role = request.Role == "Admin" ? "Admin" : "Viewer";

        var user = new User
        {
            Username = request.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = role,
            IsLocal = true,
        };
        _db.Users.Add(user);
        _db.SaveChanges();

        return Ok(new { user.Id, user.Username, user.Role, user.CreatedAt, user.IsLocal });
    }

    [HttpPost("{id:guid}/reset-password")]
    public IActionResult ResetPassword(Guid id, [FromBody] ResetPasswordRequest request)
    {
        var user = _db.Users.Find(id);
        if (user == null)
            return NotFound();

        if (!user.IsLocal)
            return BadRequest(new { message = "Passwort kann für LDAP-Benutzer nicht geändert werden." });

        if (request.NewPassword.Length < 6)
            return BadRequest(new { message = "Passwort muss mindestens 6 Zeichen lang sein" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        _db.SaveChanges();

        return Ok(new { message = "Passwort zurückgesetzt" });
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (currentUserId == id.ToString())
            return BadRequest(new { message = "Du kannst deinen eigenen Account nicht löschen" });

        var user = _db.Users.Find(id);
        if (user == null)
            return NotFound();

        if (_db.Users.Count() <= 1)
            return BadRequest(new { message = "Mindestens ein Benutzer muss vorhanden sein" });

        _db.Users.Remove(user);
        _db.SaveChanges();

        return NoContent();
    }
}

public record CreateUserRequest(string Username, string Password, string? Role = null);
public record ResetPasswordRequest(string NewPassword);
