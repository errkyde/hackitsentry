using HITSight.Server.Data;
using HITSight.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;
    private readonly RuntimeSettings _settings;
    private readonly LdapService _ldap;
    private readonly AuditService _audit;

    public AuthController(AppDbContext db, JwtService jwt, RuntimeSettings settings, LdapService ldap, AuditService audit)
    {
        _db = db;
        _jwt = jwt;
        _settings = settings;
        _ldap = ldap;
        _audit = audit;
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

        var user = new HITSight.Server.Models.User
        {
            Username = request.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = "Admin",
            IsLocal = true,
        };
        _db.Users.Add(user);
        _db.SaveChanges();

        var token = _jwt.GenerateToken(user.Id.ToString(), user.Username, user.Role);
        return Ok(new { token, username = user.Username, role = user.Role });
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // 1. Try local account first
        var user = _db.Users.FirstOrDefault(u => u.Username == request.Username && u.IsLocal);
        if (user != null)
        {
            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Unauthorized(new { message = "Ungültige Anmeldedaten" });

            var token = _jwt.GenerateToken(user.Id.ToString(), user.Username, user.Role);
            return Ok(new { token, username = user.Username, role = user.Role });
        }

        // 2. LDAP flow
        if (_settings.LdapEnabled && !string.IsNullOrWhiteSpace(_settings.LdapHost))
        {
            var userDn = await _ldap.FindUserDnAsync(request.Username);
            if (userDn == null)
            {
                await _audit.LogAsync("auth.ldap.failed", "User", null, $"LDAP-Login fehlgeschlagen: Benutzer '{request.Username}' nicht gefunden.");
                return Unauthorized(new { message = "Ungültige Anmeldedaten" });
            }

            if (!await _ldap.TryBindUserAsync(userDn, request.Password))
            {
                await _audit.LogAsync("auth.ldap.failed", "User", null, $"LDAP-Login fehlgeschlagen: Falsches Passwort für '{request.Username}'.");
                return Unauthorized(new { message = "Ungültige Anmeldedaten" });
            }

            var info = await _ldap.GetUserInfoAsync(userDn);
            var role = await _ldap.DeriveRoleAsync(userDn, info?.MemberOf ?? new());
            if (role == null)
            {
                await _audit.LogAsync("auth.ldap.denied", "User", null, $"LDAP-Login verweigert: '{request.Username}' ist in keiner autorisierten Gruppe.");
                return Unauthorized(new { message = "Kein Zugriff. Benutzer ist nicht in einer autorisierten Gruppe." });
            }

            // Upsert LDAP user in DB
            var ldapUser = _db.Users.FirstOrDefault(u => u.Username == request.Username && !u.IsLocal);
            if (ldapUser == null)
            {
                ldapUser = new HITSight.Server.Models.User
                {
                    Username = request.Username,
                    PasswordHash = "",
                    IsLocal = false,
                };
                _db.Users.Add(ldapUser);
            }
            ldapUser.LdapDn = userDn;
            ldapUser.DisplayName = info?.DisplayName;
            ldapUser.Email = info?.Email;
            ldapUser.Role = role;
            _db.SaveChanges();

            await _audit.LogAsync("auth.ldap.success", "User", ldapUser.Id.ToString(), $"LDAP-Login: '{request.Username}' als {role}.");
            var jwtToken = _jwt.GenerateToken(ldapUser.Id.ToString(), ldapUser.Username, ldapUser.Role);
            return Ok(new { token = jwtToken, username = ldapUser.Username, role = ldapUser.Role });
        }

        return Unauthorized(new { message = "Ungültige Anmeldedaten" });
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
