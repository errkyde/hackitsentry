using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using HackITSentry.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackITSentry.Server.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize(Roles = "Admin")]
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

    // GET /api/settings  — accessible to all authenticated users
    [HttpGet]
    [Authorize]
    public IActionResult GetSettings()
    {
        return Ok(new
        {
            checkinIntervalMinutes = _runtimeSettings.CheckinIntervalMinutes
        });
    }

    // PUT /api/settings/checkin
    [HttpPut("checkin")]
    public async Task<IActionResult> SaveCheckinSettings([FromBody] CheckinSettingsRequest req)
    {
        var interval = Math.Clamp(req.CheckinIntervalMinutes, 1, 1440);
        _runtimeSettings.CheckinIntervalMinutes = interval;

        var existing = await _db.AppSettings.FindAsync("CheckinIntervalMinutes");
        if (existing != null)
            existing.Value = interval.ToString();
        else
            _db.AppSettings.Add(new AppSetting { Key = "CheckinIntervalMinutes", Value = interval.ToString() });

        await _db.SaveChangesAsync();
        return Ok(new { message = "Check-in-Intervall gespeichert.", checkinIntervalMinutes = interval });
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
            AlertEmailService.BuildHtml(
                "#16a34a", "Test",
                "E-Mail-Konfiguration erfolgreich",
                "<p style='margin:0;font-size:14px;color:#3f3f46;'>Die E-Mail-Einstellungen von HackIT Sentry sind korrekt konfiguriert. Diese Nachricht dient zur Bestätigung.</p>"));

        if (error != null)
            return BadRequest(new { message = $"Fehler: {error}" });

        return Ok(new { message = "Test-E-Mail erfolgreich gesendet." });
    }

    // GET /api/settings/alerts
    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlertSettings()
    {
        var thresholdSetting = await _db.AppSettings.FindAsync("DiskAlertThresholdPercent");
        var threshold = int.TryParse(thresholdSetting?.Value, out var t) ? t : 10;
        return Ok(new { diskAlertThresholdPercent = threshold });
    }

    // GET /api/settings/rustdesk
    [HttpGet("rustdesk")]
    public IActionResult GetRustDeskSettings()
    {
        return Ok(new
        {
            relayHost = _runtimeSettings.RustDeskRelayHost,
            publicKey = _runtimeSettings.RustDeskPublicKey,
            autoInstall = _runtimeSettings.RustDeskAutoInstall,
            downloadUrl = _runtimeSettings.RustDeskDownloadUrl,
        });
    }

    // PUT /api/settings/rustdesk
    [HttpPut("rustdesk")]
    public async Task<IActionResult> SaveRustDeskSettings([FromBody] RustDeskSettingsRequest req)
    {
        _runtimeSettings.RustDeskRelayHost = req.RelayHost ?? "";
        _runtimeSettings.RustDeskPublicKey = req.PublicKey ?? "";
        _runtimeSettings.RustDeskAutoInstall = req.AutoInstall;
        _runtimeSettings.RustDeskDownloadUrl = req.DownloadUrl ?? "";

        foreach (var (key, value) in _runtimeSettings.RustDeskToDbEntries())
        {
            var existing = await _db.AppSettings.FindAsync(key);
            if (existing != null)
                existing.Value = value;
            else
                _db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        await _db.SaveChangesAsync();
        return Ok(new { message = "RustDesk-Einstellungen gespeichert." });
    }

    // PUT /api/settings/alerts
    [HttpPut("alerts")]
    public async Task<IActionResult> SaveAlertSettings([FromBody] AlertSettingsRequest req)
    {
        var threshold = Math.Clamp(req.DiskAlertThresholdPercent, 1, 99);
        var existing = await _db.AppSettings.FindAsync("DiskAlertThresholdPercent");
        if (existing != null)
            existing.Value = threshold.ToString();
        else
            _db.AppSettings.Add(new AppSetting { Key = "DiskAlertThresholdPercent", Value = threshold.ToString() });

        await _db.SaveChangesAsync();
        return Ok(new { message = "Alert-Einstellungen gespeichert." });
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

public record AlertSettingsRequest(int DiskAlertThresholdPercent);
public record CheckinSettingsRequest(int CheckinIntervalMinutes);
public record RustDeskSettingsRequest(string? RelayHost, string? PublicKey, bool AutoInstall, string? DownloadUrl);
