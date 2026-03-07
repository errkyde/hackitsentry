using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using HackITSentry.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackITSentry.Server.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly AlertEmailService _email;
    private readonly IConfiguration _config;

    public SettingsController(AppDbContext db, RuntimeSettings runtimeSettings, AlertEmailService email, IConfiguration config)
    {
        _db = db;
        _runtimeSettings = runtimeSettings;
        _email = email;
        _config = config;
    }

    // GET /api/settings  (kept for agent compatibility)
    [HttpGet]
    public IActionResult GetSettings()
    {
        return Ok(new
        {
            checkinIntervalMinutes = _config.GetValue<int>("CheckinIntervalMinutes", 30)
        });
    }

    // GET /api/settings/email
    [HttpGet("email")]
    public IActionResult GetEmailSettings()
    {
        return Ok(new
        {
            host = _runtimeSettings.EmailHost,
            port = _runtimeSettings.EmailPort,
            username = _runtimeSettings.EmailUsername,
            hasPassword = !string.IsNullOrEmpty(_runtimeSettings.EmailPassword),
            from = _runtimeSettings.EmailFrom,
            to = _runtimeSettings.EmailTo,
            useSsl = _runtimeSettings.EmailUseSsl,
            isConfigured = _runtimeSettings.IsEmailConfigured,
        });
    }

    // PUT /api/settings/email
    [HttpPut("email")]
    public async Task<IActionResult> SaveEmailSettings([FromBody] EmailSettingsRequest req)
    {
        // Update runtime settings
        _runtimeSettings.EmailHost = req.Host ?? "";
        _runtimeSettings.EmailPort = req.Port > 0 ? req.Port : 587;
        _runtimeSettings.EmailUsername = req.Username ?? "";
        // Keep existing password if new one is empty (don't overwrite with blank)
        if (!string.IsNullOrEmpty(req.Password))
            _runtimeSettings.EmailPassword = req.Password;
        _runtimeSettings.EmailFrom = req.From ?? "sentry@localhost";
        _runtimeSettings.EmailTo = req.To ?? "";
        _runtimeSettings.EmailUseSsl = req.UseSsl;

        // Persist to DB
        foreach (var (key, value) in _runtimeSettings.ToDbEntries())
        {
            var existing = await _db.AppSettings.FindAsync(key);
            if (existing != null)
                existing.Value = value;
            else
                _db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        await _db.SaveChangesAsync();

        return Ok(new { message = "E-Mail-Einstellungen gespeichert." });
    }

    // POST /api/settings/email/test
    [HttpPost("email/test")]
    public async Task<IActionResult> TestEmail()
    {
        if (!_runtimeSettings.IsEmailConfigured)
            return BadRequest(new { message = "E-Mail ist nicht konfiguriert." });

        var error = await _email.SendAsync(
            "[HackIT Sentry] Test-E-Mail",
            $"Dies ist eine Test-E-Mail von HackIT Sentry.\n\nSent at: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");

        if (error != null)
            return BadRequest(new { message = $"Fehler: {error}" });

        return Ok(new { message = "Test-E-Mail erfolgreich gesendet." });
    }
}

public record EmailSettingsRequest(
    string? Host,
    int Port,
    string? Username,
    string? Password,
    string? From,
    string? To,
    bool UseSsl);
