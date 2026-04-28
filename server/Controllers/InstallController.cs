using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using HackITSentry.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HackITSentry.Server.Controllers;

[ApiController]
public class InstallController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly AlertEmailService _email;
    private readonly InstallerService _installer;
    private readonly AuditService _audit;

    public InstallController(AppDbContext db, IConfiguration config, RuntimeSettings runtimeSettings, AlertEmailService email, InstallerService installer, AuditService audit)
    {
        _db = db;
        _config = config;
        _runtimeSettings = runtimeSettings;
        _email = email;
        _installer = installer;
        _audit = audit;
    }

    // GET /install/{token}  — Landing page
    [HttpGet("/install/{token}")]
    public async Task<IActionResult> LandingPage(string token)
    {
        var installToken = await _db.InstallTokens
            .FirstOrDefaultAsync(t => t.Token == token);

        if (installToken == null)
            return Content(HtmlStatus("Link ungültig", "Dieser Link existiert nicht.", "❌"), "text/html");

        if (installToken.ExpiresAt < DateTime.UtcNow)
            return Content(HtmlStatus("Link abgelaufen", $"Dieser Link war gültig bis {installToken.ExpiresAt:dd.MM.yyyy HH:mm} Uhr.", "⏱️"), "text/html");

        if (installToken.Used && installToken.UsedAt.HasValue &&
            installToken.UsedAt.Value.AddSeconds(120) < DateTime.UtcNow)
        {
            _db.InstallTokens.Remove(installToken);
            await _db.SaveChangesAsync();
            return Content(HtmlStatus(
                "Link bereits verwendet",
                $"Dieser Installationslink wurde bereits am {installToken.UsedAt:dd.MM.yyyy} um {installToken.UsedAt:HH:mm} Uhr verwendet.",
                "✅"), "text/html");
        }

        if (!_installer.IsAvailable)
            return Content(HtmlStatus("Installer nicht verfügbar", "Der Installer ist derzeit nicht verfügbar. Bitte kontaktiere deinen Administrator.", "⚠️"), "text/html");

        return Content(HtmlDownloadPage(token, installToken.ExpiresAt), "text/html");
    }

    // GET /install/{token}/download  — Actual file delivery
    [HttpGet("/install/{token}/download")]
    public async Task<IActionResult> Download(string token)
    {
        var installToken = await _db.InstallTokens
            .FirstOrDefaultAsync(t => t.Token == token);

        if (installToken == null || installToken.ExpiresAt < DateTime.UtcNow)
            return Content(HtmlStatus("Link ungültig oder abgelaufen", "Bitte fordere einen neuen Installationslink an.", "❌"), "text/html");

        // After first use, allow re-downloads for 120 seconds (for download managers), then delete
        if (installToken.Used && installToken.UsedAt.HasValue &&
            installToken.UsedAt.Value.AddSeconds(120) < DateTime.UtcNow)
        {
            _db.InstallTokens.Remove(installToken);
            await _db.SaveChangesAsync();
            return Content(HtmlStatus(
                "Link bereits verwendet",
                $"Dieser Installationslink wurde bereits am {installToken.UsedAt:dd.MM.yyyy} um {installToken.UsedAt:HH:mm} Uhr verwendet.",
                "✅"), "text/html");
        }

        if (!_installer.IsAvailable)
            return StatusCode(503, "Installer nicht verfügbar.");

        var outpostUrl = !string.IsNullOrEmpty(_runtimeSettings.AgentServerUrl)
            ? _runtimeSettings.AgentServerUrl
            : (_config["OutpostPublicUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}");

        // Track first use + audit log
        if (!installToken.Used)
        {
            installToken.Used = true;
            installToken.UsedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("INSTALLER_DOWNLOADED", "InstallToken", installToken.Id.ToString(),
                $"Token {installToken.Token} heruntergeladen");
        }

        Response.ContentLength = _installer.FileSize;
        var stream = _installer.CreatePatchedStream(outpostUrl, token);
        return File(stream, "application/octet-stream", "HackITSentry-Setup.exe");
    }

    // ── Token management (auth required) ─────────────────────────────────

    [HttpGet("/api/install-tokens")]
    [Authorize]
    public async Task<IActionResult> List()
    {
        var tokens = await _db.InstallTokens
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.Token,
                t.CreatedByUsername,
                t.CreatedAt,
                t.ExpiresAt,
                t.Used,
                t.UsedAt,
                Expired = t.ExpiresAt < DateTime.UtcNow
            })
            .ToListAsync();

        return Ok(tokens);
    }

    [HttpPost("/api/install-tokens")]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateTokenRequest request)
    {
        var username = User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";
        var token = Guid.NewGuid().ToString("N")[..16];
        var expiryHours = request.ExpiryHours > 0 ? request.ExpiryHours : 24;

        var installToken = new InstallToken
        {
            Token = token,
            CreatedByUsername = username,
            ExpiresAt = DateTime.UtcNow.AddHours(expiryHours)
        };

        _db.InstallTokens.Add(installToken);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            installToken.Id,
            installToken.Token,
            installToken.CreatedByUsername,
            installToken.CreatedAt,
            installToken.ExpiresAt,
        });
    }

    [HttpPost("/api/install-tokens/{id}/send-email")]
    [Authorize]
    public async Task<IActionResult> SendEmail(Guid id, [FromBody] SendEmailRequest request)
    {
        var token = await _db.InstallTokens.FindAsync(id);
        if (token == null || token.Used || token.ExpiresAt < DateTime.UtcNow)
            return BadRequest(new { message = "Token ungültig oder abgelaufen." });

        var outpostUrl = !string.IsNullOrEmpty(_runtimeSettings.AgentServerUrl)
            ? _runtimeSettings.AgentServerUrl
            : (_config["OutpostPublicUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}");
        var downloadUrl = $"{outpostUrl}/install/{token.Token}";
        var senderName = User.FindFirstValue(ClaimTypes.Name) ?? "HackIT Sentry";

        var body = AlertEmailService.BuildHtml(
            "#2563eb", "Einladung",
            "HackIT Sentry Agent installieren",
            $"""
            <p style="margin:0 0 20px;font-size:14px;color:#3f3f46;line-height:1.6;">
              <strong style="color:#18181b;">{senderName}</strong> hat dich eingeladen,
              den HackIT Sentry Monitoring-Agent auf diesem Gerät zu installieren.
            </p>

            <a href="{downloadUrl}"
               style="display:inline-block;padding:12px 24px;background:#18181b;color:#ffffff;text-decoration:none;border-radius:6px;font-size:14px;font-weight:600;margin-bottom:20px;">
              Installer herunterladen
            </a>

            <div style="border:1px solid #e4e4e7;border-radius:6px;padding:14px 16px;margin-bottom:8px;font-size:13px;color:#71717a;">
              <div style="margin-bottom:6px;">
                <span style="color:#a1a1aa;">Link gültig bis</span>
                <span style="float:right;color:#18181b;font-weight:500;">{token.ExpiresAt:dd.MM.yyyy HH:mm} Uhr</span>
              </div>
            </div>

            <p style="margin:16px 0 0;font-size:12px;color:#a1a1aa;line-height:1.6;">
              Die heruntergeladene Datei als <strong>Administrator</strong> ausführen &ndash;
              die Installation und Einrichtung erfolgt vollautomatisch.
            </p>
            """);

        var error = await _email.SendToAsync(request.Email, "HackIT Sentry – Agent installieren", body);
        if (error != null)
            return BadRequest(new { message = error });

        return Ok(new { message = "E-Mail gesendet." });
    }

    [HttpDelete("/api/install-tokens/{id}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id)
    {
        var token = await _db.InstallTokens.FindAsync(id);
        if (token == null) return NotFound();
        _db.InstallTokens.Remove(token);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── HTML helpers ──────────────────────────────────────────────────────

    private static string HtmlStatus(string title, string message, string icon) => $$"""
        <!DOCTYPE html>
        <html lang="de">
        <head>
          <meta charset="UTF-8">
          <meta name="viewport" content="width=device-width, initial-scale=1.0">
          <title>HackIT Sentry – {{title}}</title>
          <style>
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #0f0f0f; color: #e5e5e5; display: flex; align-items: center; justify-content: center; min-height: 100vh; padding: 1rem; }
            .card { background: #1a1a1a; border: 1px solid #2a2a2a; border-radius: 12px; padding: 2.5rem; max-width: 420px; width: 100%; text-align: center; }
            .icon { font-size: 3rem; margin-bottom: 1rem; }
            .brand { font-size: 0.75rem; color: #666; margin-bottom: 1.5rem; letter-spacing: 0.05em; text-transform: uppercase; }
            h1 { font-size: 1.25rem; font-weight: 600; margin-bottom: 0.75rem; color: #fff; }
            p { font-size: 0.875rem; color: #999; line-height: 1.6; }
          </style>
        </head>
        <body>
          <div class="card">
            <div class="icon">{{icon}}</div>
            <div class="brand">HackIT Sentry</div>
            <h1>{{title}}</h1>
            <p>{{message}}</p>
          </div>
        </body>
        </html>
        """;

    private static string HtmlDownloadPage(string token, DateTime expiresAt) => $$"""
        <!DOCTYPE html>
        <html lang="de">
        <head>
          <meta charset="UTF-8">
          <meta name="viewport" content="width=device-width, initial-scale=1.0">
          <title>HackIT Sentry – Agent installieren</title>
          <style>
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #0f0f0f; color: #e5e5e5; display: flex; align-items: center; justify-content: center; min-height: 100vh; padding: 1rem; }
            .card { background: #1a1a1a; border: 1px solid #2a2a2a; border-radius: 12px; padding: 2.5rem; max-width: 440px; width: 100%; text-align: center; }
            .logo { font-size: 2.5rem; margin-bottom: 0.75rem; }
            .brand { font-size: 0.75rem; color: #666; margin-bottom: 1.5rem; letter-spacing: 0.05em; text-transform: uppercase; }
            h1 { font-size: 1.25rem; font-weight: 600; margin-bottom: 0.5rem; color: #fff; }
            .sub { font-size: 0.875rem; color: #999; margin-bottom: 2rem; line-height: 1.6; }
            .btn { display: inline-block; padding: 14px 32px; background: #2563eb; color: #fff; text-decoration: none; border-radius: 8px; font-size: 1rem; font-weight: 600; transition: background 0.15s; cursor: pointer; border: none; }
            .btn:hover { background: #1d4ed8; }
            .meta { margin-top: 1.5rem; font-size: 0.75rem; color: #555; }
            .steps { margin-top: 1.75rem; text-align: left; border-top: 1px solid #2a2a2a; padding-top: 1.5rem; }
            .step { display: flex; gap: 0.75rem; align-items: flex-start; margin-bottom: 0.75rem; font-size: 0.8125rem; color: #888; }
            .step-num { background: #2a2a2a; color: #aaa; border-radius: 50%; width: 1.4rem; height: 1.4rem; display: flex; align-items: center; justify-content: center; flex-shrink: 0; font-size: 0.7rem; font-weight: 600; margin-top: 0.05rem; }
          </style>
        </head>
        <body>
          <div class="card">
            <div class="logo">🛡️</div>
            <div class="brand">HackIT Sentry</div>
            <h1>Agent installieren</h1>
            <p class="sub">Lade den Monitoring-Agent herunter und führe ihn als Administrator aus.</p>
            <a class="btn" href="/install/{{token}}/download">⬇ Installer herunterladen</a>
            <p class="meta">Link gültig bis {{expiresAt:dd.MM.yyyy HH:mm}} Uhr</p>
            <div class="steps">
              <div class="step"><div class="step-num">1</div><span>Installer herunterladen</span></div>
              <div class="step"><div class="step-num">2</div><span>Als <strong style="color:#ccc;">Administrator</strong> ausführen</span></div>
              <div class="step"><div class="step-num">3</div><span>Installation läuft vollautomatisch ab</span></div>
            </div>
          </div>
        </body>
        </html>
        """;
}

public record CreateTokenRequest(int ExpiryHours);
public record SendEmailRequest(string Email);
