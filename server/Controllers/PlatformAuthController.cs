using HITSight.Server.Data;
using HITSight.Server.Models;
using HITSight.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OtpNet;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/platform/auth")]
public class PlatformAuthController : ControllerBase
{
    private readonly PlatformJwtService _jwt;
    private readonly PlatformDbContext? _db;
    private readonly ILogger<PlatformAuthController> _logger;

    public PlatformAuthController(
        PlatformJwtService jwt,
        ILogger<PlatformAuthController> logger,
        PlatformDbContext? db = null)
    {
        _jwt = jwt;
        _logger = logger;
        _db = db;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (!_jwt.IsConfigured || _db == null)
            return StatusCode(503, new { message = "Platform mode not enabled" });

        var user = await _db.SuperAdminUsers.FirstOrDefaultAsync(u => u.Username == req.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Ungültige Anmeldedaten" });

        var tempToken = _jwt.GenerateTempToken(user.Username);

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Super admin {Username} logged in", user.Username);

        return Ok(new
        {
            tempToken,
            totpEnabled = user.TotpEnabled,
            totpSetupRequired = !user.TotpEnabled,
        });
    }

    // Returns the TOTP secret + URI to display to the user (called with temp token)
    [Authorize(AuthenticationSchemes = "SuperAdmin")]
    [HttpPost("totp-setup")]
    public async Task<IActionResult> TotpSetup()
    {
        if (_db == null) return StatusCode(503);

        var username = User.FindFirst("sub")?.Value;
        var user = await _db.SuperAdminUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null) return Unauthorized();
        if (user.TotpEnabled) return BadRequest(new { message = "TOTP already enabled" });

        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(secretBytes);

        user.TotpSecret = base32Secret;
        await _db.SaveChangesAsync();

        var encodedUser = Uri.EscapeDataString(username ?? "admin");
        var otpUri = $"otpauth://totp/HITSight:{encodedUser}?secret={base32Secret}&issuer=HITSight";

        return Ok(new { secret = base32Secret, otpAuthUri = otpUri });
    }

    // Confirms TOTP setup with first code — enables TOTP and returns full token
    [Authorize(AuthenticationSchemes = "SuperAdmin")]
    [HttpPost("totp-confirm")]
    public async Task<IActionResult> TotpConfirm([FromBody] CodeRequest req)
    {
        if (_db == null) return StatusCode(503);

        var username = User.FindFirst("sub")?.Value;
        var user = await _db.SuperAdminUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null) return Unauthorized();
        if (user.TotpEnabled) return BadRequest(new { message = "TOTP already enabled" });
        if (string.IsNullOrEmpty(user.TotpSecret))
            return BadRequest(new { message = "Call /totp-setup first" });

        if (!VerifyTotp(user.TotpSecret, req.Code))
            return BadRequest(new { message = "Ungültiger Code" });

        user.TotpEnabled = true;
        await _db.SaveChangesAsync();

        return Ok(new { token = _jwt.GenerateFullToken(user.Username) });
    }

    // Verifies TOTP code after password login — returns full token
    [Authorize(AuthenticationSchemes = "SuperAdmin")]
    [HttpPost("totp-verify")]
    public async Task<IActionResult> TotpVerify([FromBody] CodeRequest req)
    {
        if (_db == null) return StatusCode(503);

        var username = User.FindFirst("sub")?.Value;
        var user = await _db.SuperAdminUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null) return Unauthorized();
        if (!user.TotpEnabled || string.IsNullOrEmpty(user.TotpSecret))
            return BadRequest(new { message = "TOTP not set up" });

        if (!VerifyTotp(user.TotpSecret, req.Code))
            return BadRequest(new { message = "Ungültiger Code" });

        return Ok(new { token = _jwt.GenerateFullToken(user.Username) });
    }

    private static bool VerifyTotp(string base32Secret, string code)
    {
        try
        {
            var bytes = Base32Encoding.ToBytes(base32Secret);
            var totp = new Totp(bytes);
            return totp.VerifyTotp(DateTime.UtcNow, code, out _, VerificationWindow.RfcSpecifiedNetworkDelay);
        }
        catch { return false; }
    }

    public record LoginRequest(string Username, string Password);
    public record CodeRequest(string Code);
}
