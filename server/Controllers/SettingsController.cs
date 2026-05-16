using HITSight.Server.Data;
using HITSight.Server.Models;
using HITSight.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize(Roles = "Admin")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly AlertEmailService _email;
    private readonly IConfiguration _config;
    private readonly AuditService _audit;

    public SettingsController(AppDbContext db, RuntimeSettings runtimeSettings, AlertEmailService email, IConfiguration config, AuditService audit)
    {
        _db = db;
        _runtimeSettings = runtimeSettings;
        _email = email;
        _config = config;
        _audit = audit;
    }

    // GET /api/settings  — accessible to all authenticated users
    [HttpGet]
    [Authorize]
    public IActionResult GetSettings()
    {
        return Ok(new
        {
            checkinIntervalMinutes = _runtimeSettings.CheckinIntervalMinutes,
            agentServerUrl = _runtimeSettings.AgentServerUrl,
        });
    }

    // PUT /api/settings/server-url
    [HttpPut("server-url")]
    public async Task<IActionResult> SaveServerUrl([FromBody] ServerUrlRequest req)
    {
        _runtimeSettings.AgentServerUrl = req.AgentServerUrl?.TrimEnd('/') ?? "";

        var existing = await _db.AppSettings.FindAsync("AgentServerUrl");
        if (existing != null)
            existing.Value = _runtimeSettings.AgentServerUrl;
        else
            _db.AppSettings.Add(new AppSetting { Key = "AgentServerUrl", Value = _runtimeSettings.AgentServerUrl });

        await _db.SaveChangesAsync();
        return Ok(new { message = "Server-URL gespeichert.", agentServerUrl = _runtimeSettings.AgentServerUrl });
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
        _runtimeSettings.EmailFrom = req.From ?? "hitsight@localhost";
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
            "[HITSight] Test-E-Mail",
            AlertEmailService.BuildHtml(
                "#16a34a", "Test",
                "E-Mail-Konfiguration erfolgreich",
                "<p style='margin:0;font-size:14px;color:#3f3f46;'>Die E-Mail-Einstellungen von HITSight sind korrekt konfiguriert. Diese Nachricht dient zur Bestätigung.</p>"));

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

    // GET /api/settings/agent
    [HttpGet("agent")]
    public IActionResult GetAgentSettings()
    {
        return Ok(new { autoUpdate = _runtimeSettings.AutoUpdateAgents });
    }

    // PUT /api/settings/agent
    [HttpPut("agent")]
    public async Task<IActionResult> SaveAgentSettings([FromBody] AgentSettingsRequest req)
    {
        _runtimeSettings.AutoUpdateAgents = req.AutoUpdate;
        var existing = await _db.AppSettings.FindAsync("Agent:AutoUpdate");
        if (existing != null)
            existing.Value = req.AutoUpdate.ToString();
        else
            _db.AppSettings.Add(new AppSetting { Key = "Agent:AutoUpdate", Value = req.AutoUpdate.ToString() });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Agent-Einstellungen gespeichert." });
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
            globalOptions = _runtimeSettings.RustDeskGlobalOptions,
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
        _runtimeSettings.RustDeskGlobalOptions = req.GlobalOptions ?? new();

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

    // POST /api/settings/rustdesk/force-apply
    [HttpPost("rustdesk/force-apply")]
    public async Task<IActionResult> ForceApplyRustDesk()
    {
        _runtimeSettings.RustDeskForceApplyVersion++;
        var key = "RustDesk:ForceApplyVersion";
        var value = _runtimeSettings.RustDeskForceApplyVersion.ToString();
        var existing = await _db.AppSettings.FindAsync(key);
        if (existing != null) existing.Value = value;
        else _db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ForceApplyRustDesk", "Settings", null, $"ForceApplyVersion={_runtimeSettings.RustDeskForceApplyVersion}");
        return Ok(new { message = "Alle Agents werden beim nächsten Check-in neu konfiguriert.", version = _runtimeSettings.RustDeskForceApplyVersion });
    }

    // DELETE /api/settings/rustdesk/device-overrides
    [HttpDelete("rustdesk/device-overrides")]
    public async Task<IActionResult> ClearDeviceRustDeskOverrides()
    {
        await _db.Database.ExecuteSqlRawAsync("""UPDATE "Devices" SET "RustDeskOptionsJson" = NULL""");
        await _audit.LogAsync("ClearDeviceRustDeskOverrides", "Settings", null, "Alle gerätespezifischen RustDesk-Overrides gelöscht.");
        return Ok(new { message = "Alle gerätespezifischen RustDesk-Overrides wurden gelöscht." });
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

    // GET /api/settings/ldap
    [HttpGet("ldap")]
    public IActionResult GetLdap()
    {
        return Ok(new
        {
            enabled = _runtimeSettings.LdapEnabled,
            host = _runtimeSettings.LdapHost,
            port = _runtimeSettings.LdapPort,
            transport = _runtimeSettings.LdapTransport,
            ignoreCertificateErrors = _runtimeSettings.LdapIgnoreCertificateErrors,
            baseDn = _runtimeSettings.LdapBaseDn,
            bindDn = _runtimeSettings.LdapBindDn,
            hasBindPassword = !string.IsNullOrEmpty(_runtimeSettings.LdapBindPassword),
            userSearchBase = _runtimeSettings.LdapUserSearchBase,
            userFilter = _runtimeSettings.LdapUserFilter,
            adminGroup = _runtimeSettings.LdapAdminGroup,
            viewerGroup = _runtimeSettings.LdapViewerGroup,
            requireGroup = _runtimeSettings.LdapRequireGroup,
            useNestedGroups = _runtimeSettings.LdapUseNestedGroups,
            hasCaCertificate = !string.IsNullOrEmpty(_runtimeSettings.LdapCaCertificate),
        });
    }

    // PUT /api/settings/ldap
    [HttpPut("ldap")]
    public async Task<IActionResult> SaveLdap([FromBody] LdapSettingsRequest req)
    {
        _runtimeSettings.LdapEnabled = req.Enabled;
        _runtimeSettings.LdapHost = req.Host?.Trim() ?? "";
        _runtimeSettings.LdapPort = req.Port > 0 ? req.Port : 389;
        _runtimeSettings.LdapTransport = req.Transport is "TCP" or "STARTTLS" or "LDAPS" ? req.Transport : "TCP";
        _runtimeSettings.LdapIgnoreCertificateErrors = req.IgnoreCertificateErrors;
        _runtimeSettings.LdapBaseDn = req.BaseDn?.Trim() ?? "";
        _runtimeSettings.LdapBindDn = req.BindDn?.Trim() ?? "";
        if (!string.IsNullOrEmpty(req.BindPassword))
            _runtimeSettings.LdapBindPassword = req.BindPassword;
        _runtimeSettings.LdapUserSearchBase = req.UserSearchBase?.Trim() ?? "";
        _runtimeSettings.LdapUserFilter = string.IsNullOrWhiteSpace(req.UserFilter)
            ? "(&(objectClass=user)(|(sAMAccountName={0})(userPrincipalName={0})))"
            : req.UserFilter.Trim();
        _runtimeSettings.LdapAdminGroup = req.AdminGroup?.Trim() ?? "";
        _runtimeSettings.LdapViewerGroup = req.ViewerGroup?.Trim() ?? "";
        _runtimeSettings.LdapRequireGroup = req.RequireGroup;
        _runtimeSettings.LdapUseNestedGroups = req.UseNestedGroups;

        var entries = new Dictionary<string, string>
        {
            ["Ldap:Enabled"] = _runtimeSettings.LdapEnabled.ToString(),
            ["Ldap:Host"] = _runtimeSettings.LdapHost,
            ["Ldap:Port"] = _runtimeSettings.LdapPort.ToString(),
            ["Ldap:Transport"] = _runtimeSettings.LdapTransport,
            ["Ldap:IgnoreCertificateErrors"] = _runtimeSettings.LdapIgnoreCertificateErrors.ToString(),
            ["Ldap:BaseDn"] = _runtimeSettings.LdapBaseDn,
            ["Ldap:BindDn"] = _runtimeSettings.LdapBindDn,
            ["Ldap:BindPassword"] = _runtimeSettings.LdapBindPassword,
            ["Ldap:UserSearchBase"] = _runtimeSettings.LdapUserSearchBase,
            ["Ldap:UserFilter"] = _runtimeSettings.LdapUserFilter,
            ["Ldap:AdminGroup"] = _runtimeSettings.LdapAdminGroup,
            ["Ldap:ViewerGroup"] = _runtimeSettings.LdapViewerGroup,
            ["Ldap:RequireGroup"] = _runtimeSettings.LdapRequireGroup.ToString(),
            ["Ldap:UseNestedGroups"] = _runtimeSettings.LdapUseNestedGroups.ToString(),
            ["Ldap:CaCertificate"] = _runtimeSettings.LdapCaCertificate,
        };

        foreach (var (key, value) in entries)
        {
            var s = await _db.AppSettings.FindAsync(key);
            if (s != null) s.Value = value;
            else _db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync("settings.ldap.save", "Settings", null, $"LDAP {(req.Enabled ? "aktiviert" : "deaktiviert")}");
        return Ok(new { message = "LDAP-Einstellungen gespeichert." });
    }

    // POST /api/settings/ldap/test
    [HttpPost("ldap/test")]
    public async Task<IActionResult> TestLdap([FromServices] LdapService ldap)
    {
        var error = await ldap.TestConnectionAsync();
        if (error == null)
            return Ok(new { message = "Verbindung erfolgreich." });
        return BadRequest(new { message = $"Verbindung fehlgeschlagen: {error}" });
    }

    // POST /api/settings/ldap/ca-certificate
    [HttpPost("ldap/ca-certificate")]
    public async Task<IActionResult> UploadCaCertificate([FromBody] CaCertificateRequest req)
    {
        var pem = req.Pem?.Trim() ?? "";
        if (string.IsNullOrEmpty(pem))
            return BadRequest(new { message = "Kein Zertifikat angegeben." });

        // Validate the PEM is actually a valid X.509 certificate
        try
        {
            var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                System.Text.Encoding.UTF8.GetBytes(pem));
            var subject = cert.Subject;
        }
        catch
        {
            return BadRequest(new { message = "Ungültiges Zertifikat. Bitte PEM-Format verwenden (-----BEGIN CERTIFICATE-----)." });
        }

        _runtimeSettings.LdapCaCertificate = pem;
        var s = await _db.AppSettings.FindAsync("Ldap:CaCertificate");
        if (s != null) s.Value = pem;
        else _db.AppSettings.Add(new AppSetting { Key = "Ldap:CaCertificate", Value = pem });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("settings.ldap.ca-cert.upload", "Settings", null, "CA-Zertifikat hochgeladen");
        return Ok(new { message = "CA-Zertifikat gespeichert." });
    }

    // DELETE /api/settings/ldap/ca-certificate
    [HttpDelete("ldap/ca-certificate")]
    public async Task<IActionResult> DeleteCaCertificate()
    {
        _runtimeSettings.LdapCaCertificate = "";
        var s = await _db.AppSettings.FindAsync("Ldap:CaCertificate");
        if (s != null) { _db.AppSettings.Remove(s); await _db.SaveChangesAsync(); }
        await _audit.LogAsync("settings.ldap.ca-cert.delete", "Settings", null, "CA-Zertifikat entfernt");
        return Ok(new { message = "CA-Zertifikat entfernt." });
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
public record LdapSettingsRequest(
    bool Enabled, string? Host, int Port, string Transport, bool IgnoreCertificateErrors,
    string? BaseDn, string? BindDn, string? BindPassword,
    string? UserSearchBase, string? UserFilter,
    string? AdminGroup, string? ViewerGroup, bool RequireGroup, bool UseNestedGroups);
public record CheckinSettingsRequest(int CheckinIntervalMinutes);
public record RustDeskSettingsRequest(string? RelayHost, string? PublicKey, bool AutoInstall, string? DownloadUrl, Dictionary<string, string>? GlobalOptions = null);
public record AgentSettingsRequest(bool AutoUpdate);
public record ServerUrlRequest(string? AgentServerUrl);
public record CaCertificateRequest(string? Pem);
