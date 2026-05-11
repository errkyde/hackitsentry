using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using HackITSentry.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace HackITSentry.Server.Controllers;

[ApiController]
[Route("api/agent")]
public class AgentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LicenseEncryptionService _encryption;
    private readonly IConfiguration _config;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly AlertEmailService _email;
    private readonly AgentCommandNotifier _notifier;
    private readonly IMemoryCache _cache;

    public AgentController(AppDbContext db, LicenseEncryptionService encryption, IConfiguration config, RuntimeSettings runtimeSettings, AlertEmailService email, AgentCommandNotifier notifier, IMemoryCache cache)
    {
        _db = db;
        _encryption = encryption;
        _config = config;
        _runtimeSettings = runtimeSettings;
        _email = email;
        _notifier = notifier;
        _cache = cache;
    }

    // POST /api/agent/register
    [HttpPost("register")]
    [EnableRateLimiting("agent-register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var existing = await _db.PendingDevices
            .FirstOrDefaultAsync(p => p.RegistrationToken == request.RegistrationToken);

        if (existing != null)
            return Ok(new { status = existing.Status.ToString(), id = existing.Id });

        string? invitedBy = null;
        string? deployKeyName = null;
        if (!string.IsNullOrEmpty(request.InstallToken))
        {
            var installToken = await _db.InstallTokens
                .FirstOrDefaultAsync(t => t.Token == request.InstallToken && t.ExpiresAt > DateTime.UtcNow);
            if (installToken != null)
            {
                invitedBy = installToken.CreatedByUsername;
            }
            else
            {
                // Check if the token is actually a deploy key (MSI deployment)
                var dk = await _db.DeployKeys.FirstOrDefaultAsync(k => k.Key == request.InstallToken);
                if (dk != null)
                    deployKeyName = dk.Name;
            }
        }

        var pending = new PendingDevice
        {
            RegistrationToken = request.RegistrationToken,
            Hostname = request.Hostname,
            WindowsVersion = request.WindowsVersion,
            CpuModel = request.CpuModel,
            RamTotalGB = request.RamTotalGB,
            InvitedByUsername = invitedBy,
            DeployKeyName = deployKeyName,
        };

        _db.PendingDevices.Add(pending);
        await _db.SaveChangesAsync();

        if (_runtimeSettings.NotifyNewPending && _runtimeSettings.IsEmailConfigured)
        {
            var rows = new[] { (pending.Hostname, $"{pending.WindowsVersion} · {pending.CpuModel} · {pending.RamTotalGB} GB RAM", (string?)"Wartet auf Freigabe") };
            _ = _email.SendAsync(
                "[HackIT Sentry] Neues Gerät wartet auf Genehmigung",
                AlertEmailService.BuildHtml(
                    "#ea580c", "Neues Gerät",
                    "Ein Gerät hat sich registriert und wartet auf Freigabe",
                    AlertEmailService.DeviceRows(rows)));
        }

        return Ok(new { status = "Pending", id = pending.Id });
    }

    // GET /api/agent/register/{token}/status
    [HttpGet("register/{token}/status")]
    public async Task<IActionResult> GetRegistrationStatus(string token)
    {
        var pending = await _db.PendingDevices
            .FirstOrDefaultAsync(p => p.RegistrationToken == token);

        if (pending == null)
            return NotFound();

        if (pending.Status == PendingDeviceStatus.Approved)
        {
            var device = pending.ApprovedDeviceId.HasValue
                ? await _db.Devices.FindAsync(pending.ApprovedDeviceId.Value)
                : null;
            return Ok(new
            {
                status = "Approved",
                apiKey = device?.AgentApiKey
            });
        }

        return Ok(new { status = pending.Status.ToString(), apiKey = (string?)null });
    }

    // POST /api/agent/checkin
    [HttpPost("checkin")]
    public async Task<IActionResult> Checkin([FromBody] CheckinRequest request)
    {
        var device = await GetDeviceByApiKey();
        if (device == null)
            return Unauthorized(new { message = "Invalid API key" });

        var camelCase = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        device.Hostname = request.Hostname.Replace("\0", "");
        device.WindowsVersion = request.WindowsVersion.Replace("\0", "");
        device.WindowsBuild = request.WindowsBuild.Replace("\0", "");
        device.WindowsEdition = request.WindowsEdition.Replace("\0", "");
        device.LicenseType = request.LicenseType.Replace("\0", "");
        device.CpuModel = request.CpuModel.Replace("\0", "");
        device.CpuCores = request.CpuCores;
        device.RamTotalGB = request.RamTotalGB;
        device.NetworkAdaptersJson = JsonSerializer.Serialize(request.NetworkAdapters, camelCase);
        device.LastSeenAt = DateTime.UtcNow;
        device.RustDeskId = request.RustDeskId; // always sync — reflects current agent state
        if (!string.IsNullOrEmpty(request.AgentVersion))
            device.AgentVersion = request.AgentVersion;
        if (!string.IsNullOrEmpty(request.BiosInfoJson) && request.BiosInfoJson != "{}")
            device.BiosInfoJson = request.BiosInfoJson;
        if (!string.IsNullOrEmpty(request.DefenderStatusJson) && request.DefenderStatusJson != "{}")
            device.DefenderStatusJson = request.DefenderStatusJson;
        device.PendingUpdatesCount = request.PendingUpdatesCount;
        if (request.LastWindowsUpdateInstalled.HasValue)
            device.LastWindowsUpdateInstalled = request.LastWindowsUpdateInstalled.Value;
        if (!string.IsNullOrEmpty(request.EventLogErrorsJson) && request.EventLogErrorsJson != "[]")
            device.EventLogErrorsJson = request.EventLogErrorsJson;

        _db.DeviceCheckins.Add(new DeviceCheckin
        {
            DeviceId = device.Id,
            RamUsedGB = request.RamUsedGB,
            DiskDrivesJson = JsonSerializer.Serialize(request.DiskDrives, camelCase)
        });

        // Upsert installed software
        var existingSoftware = _db.InstalledSoftware.Where(s => s.DeviceId == device.Id);
        _db.InstalledSoftware.RemoveRange(existingSoftware);

        foreach (var sw in request.InstalledSoftware)
        {
            _db.InstalledSoftware.Add(new InstalledSoftware
            {
                DeviceId = device.Id,
                Name = sw.Name.Replace("\0", ""),
                Version = sw.Version.Replace("\0", ""),
                Publisher = sw.Publisher.Replace("\0", ""),
                InstallDate = sw.InstallDate.Replace("\0", "")
            });
        }

        await _db.SaveChangesAsync();

        // Check disk space thresholds
        await CheckDiskAlertsAsync(device, request.DiskDrives);

        // Check AV/Defender status
        await CheckAvStatusAsync(device, request.DefenderStatusJson);

        // Check installed software against blacklist
        await CheckBlacklistAsync(device, request.InstalledSoftware);

        // Check for pending commands
        var hasPendingCommands = await _db.DeviceCommands
            .AnyAsync(c => c.DeviceId == device.Id && c.Status == CommandStatus.Pending);

        // Check for latest agent version
        var latestVersion = await _db.AgentVersions
            .Where(v => v.IsLatest)
            .Select(v => new { v.Version, v.DownloadUrl })
            .FirstOrDefaultAsync();

        // Auto-update: queue ForceUpdate only if latest version is strictly newer than what the device runs
        if (_runtimeSettings.AutoUpdateAgents
            && latestVersion != null
            && IsNewerVersion(latestVersion.Version, device.AgentVersion))
        {
            var alreadyQueued = await _db.DeviceCommands.AnyAsync(c =>
                c.DeviceId == device.Id &&
                c.CommandType == CommandType.ForceUpdate &&
                (c.Status == CommandStatus.Pending || c.Status == CommandStatus.Sent));

            if (!alreadyQueued)
            {
                var downloadUrl = $"{Request.Scheme}://{Request.Host}/api/agent/update/download?key={Uri.EscapeDataString(device.AgentApiKey)}";
                _db.DeviceCommands.Add(new DeviceCommand
                {
                    DeviceId = device.Id,
                    CommandType = CommandType.ForceUpdate,
                    Parameters = downloadUrl,
                    IssuedByUsername = "system",
                });
                await _db.SaveChangesAsync();
                hasPendingCommands = true;
                _notifier.NotifyDevice(device.Id);
            }
        }

        // Merge global options with per-device overrides (device wins)
        var mergedOptions = new Dictionary<string, string>(_runtimeSettings.RustDeskGlobalOptions);
        if (!string.IsNullOrEmpty(device.RustDeskOptionsJson))
        {
            try
            {
                var deviceOptions = JsonSerializer.Deserialize<Dictionary<string, string>>(device.RustDeskOptionsJson);
                if (deviceOptions != null)
                    foreach (var (k, v) in deviceOptions)
                        mergedOptions[k] = v;
            }
            catch { }
        }

        return Ok(new
        {
            licenseRequested = device.LicenseRequested,
            hasPendingCommands,
            latestAgentVersion = latestVersion?.Version,
            agentDownloadUrl = latestVersion != null
                ? $"{Request.Scheme}://{Request.Host}/api/agent/update/download?key={Uri.EscapeDataString(device.AgentApiKey)}"
                : null,
            rustDeskRelayServer = _runtimeSettings.RustDeskRelayHost,
            rustDeskPublicKey = _runtimeSettings.RustDeskPublicKey,
            rustDeskAutoInstall = _runtimeSettings.RustDeskAutoInstall,
            rustDeskDownloadUrl = _runtimeSettings.RustDeskDownloadUrl,
            rustDeskDeviceOptions = mergedOptions.Count > 0 ? mergedOptions : null,
            rustDeskForceApplyVersion = _runtimeSettings.RustDeskForceApplyVersion,
            checkinIntervalMinutes = _runtimeSettings.CheckinIntervalMinutes
        });
    }

    // POST /api/agent/request-key
    [HttpPost("request-key")]
    public async Task<IActionResult> SubmitLicenseKey([FromBody] LicenseSubmitRequest request)
    {
        var device = await GetDeviceByApiKey();
        if (device == null)
            return Unauthorized(new { message = "Invalid API key" });

        var license = await _db.LicenseInfos.FirstOrDefaultAsync(l => l.DeviceId == device.Id);
        if (license == null)
        {
            license = new LicenseInfo { DeviceId = device.Id };
            _db.LicenseInfos.Add(license);
        }

        license.WindowsKeyEncrypted = !string.IsNullOrEmpty(request.WindowsKey)
            ? _encryption.Encrypt(request.WindowsKey)
            : null;
        license.LicenseType = request.LicenseType;
        license.OfficeKeyEncrypted = !string.IsNullOrEmpty(request.OfficeKey)
            ? _encryption.Encrypt(request.OfficeKey)
            : null;
        license.OfficeVersion = request.OfficeVersion;
        license.FetchedAt = DateTime.UtcNow;

        device.LicenseRequested = false;

        await _db.SaveChangesAsync();

        return Ok();
    }

    // GET /api/agent/commands/wait  — long poll, blocks up to 29 s
    [HttpGet("commands/wait")]
    public async Task<IActionResult> WaitForCommand(CancellationToken ct)
    {
        var device = await GetDeviceByApiKey();
        if (device == null)
            return Unauthorized(new { message = "Invalid API key" });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(29));
        using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        await _notifier.WaitAsync(device.Id, linked.Token);
        return NoContent(); // 204 — agent fetches commands immediately after
    }

    // GET /api/agent/commands/pending
    [HttpGet("commands/pending")]
    public async Task<IActionResult> GetPendingCommands()
    {
        var device = await GetDeviceByApiKey();
        if (device == null)
            return Unauthorized(new { message = "Invalid API key" });

        var now = DateTime.UtcNow;
        var commands = await _db.DeviceCommands
            .Where(c => c.DeviceId == device.Id && c.Status == CommandStatus.Pending
                     && (c.ScheduledFor == null || c.ScheduledFor <= now))
            .OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                CommandType = c.CommandType.ToString(),
                c.Parameters
            })
            .ToListAsync();

        // Mark as Sent
        var ids = commands.Select(c => c.Id).ToList();
        if (ids.Count > 0)
        {
            await _db.DeviceCommands
                .Where(c => ids.Contains(c.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CommandStatus.Sent));
        }

        return Ok(commands);
    }

    // POST /api/agent/uninstall
    [HttpPost("uninstall")]
    public async Task<IActionResult> Uninstall()
    {
        var device = await GetDeviceByApiKey();
        if (device == null)
            return Unauthorized(new { message = "Invalid API key" });

        _db.Devices.Remove(device);
        await _db.SaveChangesAsync();

        return Ok();
    }

    // POST /api/agent/commands/{id}/result
    [HttpPost("commands/{id:guid}/result")]
    public async Task<IActionResult> ReportCommandResult(Guid id, [FromBody] CommandResultRequest request)
    {
        var device = await GetDeviceByApiKey();
        if (device == null)
            return Unauthorized(new { message = "Invalid API key" });

        var command = await _db.DeviceCommands
            .FirstOrDefaultAsync(c => c.Id == id && c.DeviceId == device.Id);

        if (command == null)
            return NotFound();

        command.Status = request.Success ? CommandStatus.Executed : CommandStatus.Failed;
        command.ExecutedAt = DateTime.UtcNow;
        command.Result = request.Message;

        // Update matching deployment job if this was a DeployPackage command
        if (command.CommandType == CommandType.DeployPackage)
        {
            var job = await _db.DeploymentJobs
                .Where(j => j.DeviceId == device.Id && j.Status == "Queued")
                .OrderBy(j => j.CreatedAt)
                .FirstOrDefaultAsync();
            if (job != null)
            {
                job.Status = request.Success ? "Success" : "Failed";
                job.Output = request.Message;
                job.ExecutedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();

        return Ok();
    }

    private async Task CheckDiskAlertsAsync(Device device, List<DiskDriveDto> drives)
    {
        try
        {
            var thresholdSetting = await _db.AppSettings.FindAsync("DiskAlertThresholdPercent");
            if (!int.TryParse(thresholdSetting?.Value, out var threshold))
                threshold = 10;

            var validDrives = drives.Where(d => d.TotalGB > 0).ToList();
            if (validDrives.Count == 0) return;

            var totalGB = validDrives.Sum(d => d.TotalGB);
            var freeGB  = validDrives.Sum(d => d.FreeGB);
            var freePct = freeGB / totalGB * 100;
            var usedPct = 100 - freePct;

            // Disk healthy again → reset all alert state
            if (freePct >= threshold)
            {
                if (device.LastDiskAlertAt.HasValue || device.DiskAlertAcknowledgedUsedPct.HasValue)
                {
                    device.LastDiskAlertAt = null;
                    device.DiskAlertAcknowledgedUsedPct = null;
                    await _db.SaveChangesAsync();
                }
                return;
            }

            // User acknowledged: only re-alert when 10% more full than at ack time
            if (device.DiskAlertAcknowledgedUsedPct.HasValue)
            {
                if (usedPct < device.DiskAlertAcknowledgedUsedPct.Value + 10) return;
                device.DiskAlertAcknowledgedUsedPct = null; // got significantly worse → require new ack
            }

            // Max one alert per day
            if (device.LastDiskAlertAt.HasValue &&
                (DateTime.UtcNow - device.LastDiskAlertAt.Value).TotalHours < 24)
                return;

            device.LastDiskAlertAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var barColor = freePct < 5 ? "#dc2626" : "#ea580c";
            var bar = $"<div style=\"margin-top:6px;height:6px;border-radius:3px;background:#f4f4f5;\">" +
                      $"<div style=\"width:{usedPct:F0}%;height:6px;border-radius:3px;background:{barColor};\"></div></div>";

            var rows = validDrives.Select(d =>
            {
                var pct = d.FreeGB / d.TotalGB * 100;
                return (
                    $"Laufwerk {d.Drive}",
                    (string?)($"{d.FreeGB:F1} GB frei von {d.TotalGB:F1} GB ({pct:F0}% frei)"),
                    (string?)null
                );
            }).Prepend((
                "Gesamt",
                (string?)($"{freeGB:F1} GB frei von {totalGB:F1} GB ({freePct:F0}% frei){bar}"),
                (string?)null
            ));

            await _email.SendAsync(
                $"[HackIT Sentry] Wenig Speicherplatz auf {device.Hostname}",
                AlertEmailService.BuildHtml(
                    "#ea580c", "Speicherplatz-Warnung",
                    $"Kritisch wenig Speicherplatz auf {device.Hostname}",
                    AlertEmailService.DeviceRows(rows),
                    $"Schwellwert: unter {threshold}% freier Speicher gesamt"));
        }
        catch
        {
            // Don't fail checkin on alert error
        }
    }

    private async Task CheckAvStatusAsync(Device device, string defenderStatusJson)
    {
        try
        {
            if (string.IsNullOrEmpty(defenderStatusJson) || defenderStatusJson == "{}") return;

            // Parse AV status
            using var doc = System.Text.Json.JsonDocument.Parse(defenderStatusJson);
            var root = doc.RootElement;

            bool? rtpEnabled = root.TryGetProperty("realTimeProtectionEnabled", out var rtpEl) && rtpEl.ValueKind != System.Text.Json.JsonValueKind.Null
                ? rtpEl.GetBoolean() : null;
            int? sigAge = root.TryGetProperty("signatureAgeDays", out var ageEl) && ageEl.ValueKind == System.Text.Json.JsonValueKind.Number
                ? ageEl.GetInt32() : null;

            var threshold = _runtimeSettings.AvSignatureAgeAlertDays;
            bool hasIssue = rtpEnabled == false || (sigAge.HasValue && sigAge.Value > threshold);

            if (!hasIssue)
            {
                // AV healthy — reset alert state
                if (device.LastAvAlertAt.HasValue)
                {
                    device.LastAvAlertAt = null;
                    await _db.SaveChangesAsync();
                }
                return;
            }

            if (!_runtimeSettings.IsEmailConfigured) return;

            // Max one alert per 24h
            if (device.LastAvAlertAt.HasValue &&
                (DateTime.UtcNow - device.LastAvAlertAt.Value).TotalHours < 24)
                return;

            device.LastAvAlertAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var issues = new List<(string, string?, string?)>();
            if (rtpEnabled == false)
                issues.Add(("Echtzeitschutz", "Deaktiviert", null));
            if (sigAge.HasValue && sigAge.Value > threshold)
                issues.Add(("Signatur-Alter", $"{sigAge.Value} Tage (Schwellwert: {threshold})", null));

            await _email.SendAsync(
                $"[HackIT Sentry] Antivirus-Problem auf {device.Hostname}",
                AlertEmailService.BuildHtml(
                    "#dc2626", "Antivirus-Alert",
                    $"Sicherheitsproblem erkannt auf {device.Hostname}",
                    AlertEmailService.DeviceRows(issues)));
        }
        catch
        {
            // Don't fail checkin on alert error
        }
    }

    private async Task CheckBlacklistAsync(Device device, List<SoftwareDto> software)
    {
        try
        {
            var blacklist = await _cache.GetOrCreateAsync("blacklist", async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return await _db.SoftwareBlacklist.AsNoTracking().ToListAsync();
            }) ?? [];
            if (blacklist.Count == 0) return;

            // Load all open alerts for this device in one query instead of one per match
            var existingAlertRules = (await _db.SoftwareAlerts
                .Where(a => a.DeviceId == device.Id && a.AcknowledgedAt == null)
                .Select(a => a.BlacklistEntryId)
                .ToListAsync()).ToHashSet();

            foreach (var entry in blacklist)
            {
                var matches = software.Where(s =>
                    s.Name.Contains(entry.NamePattern, StringComparison.OrdinalIgnoreCase) &&
                    (entry.Publisher == null || s.Publisher.Contains(entry.Publisher, StringComparison.OrdinalIgnoreCase)));

                foreach (var match in matches)
                {
                    if (existingAlertRules.Contains(entry.Id)) continue;
                    existingAlertRules.Add(entry.Id); // prevent duplicates within same check-in

                    _db.SoftwareAlerts.Add(new SoftwareAlert
                    {
                        DeviceId = device.Id,
                        BlacklistEntryId = entry.Id,
                        SoftwareName = match.Name,
                        SoftwareVersion = match.Version
                    });

                    var detail =
                        $"<p style='margin:0 0 12px;font-size:14px;color:#3f3f46;'>" +
                        $"Folgende Software wurde auf <strong>{device.Hostname}</strong> gefunden:</p>" +
                        AlertEmailService.DeviceRows([
                            (match.Name, match.Version.Length > 0 ? $"Version: {match.Version}" : null, null),
                            ("Blacklist-Regel", entry.NamePattern, null),
                            ("Grund", entry.Reason ?? "—", null)
                        ]);

                    await _email.SendAsync(
                        $"[HackIT Sentry] Blacklisted Software auf {device.Hostname}",
                        AlertEmailService.BuildHtml(
                            "#dc2626", "Software-Alert",
                            $"Unerlaubte Software erkannt",
                            detail));
                }
            }

            await _db.SaveChangesAsync();
        }
        catch
        {
            // Don't fail checkin on alert error
        }
    }

    // GET /api/agent/update/download?key={apiKey}
    [HttpGet("update/download")]
    public async Task<IActionResult> DownloadUpdate([FromQuery] string? key)
    {
        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault() ?? key;
        if (string.IsNullOrEmpty(apiKey)) return Unauthorized();

        var device = await _db.Devices.FirstOrDefaultAsync(d => d.AgentApiKey == apiKey);
        if (device == null) return Unauthorized();

        var latest = await _db.AgentVersions.Where(v => v.IsLatest).FirstOrDefaultAsync();
        if (latest == null) return NotFound(new { message = "No latest version available." });

        var filePath = Path.Combine(AppContext.BaseDirectory, "downloads", $"HackITSentry-Agent-{latest.Version}.exe");
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { message = "Agent binary not found on server." });

        return PhysicalFile(filePath, "application/octet-stream", $"HackITSentry-Agent-{latest.Version}.exe");
    }

    private async Task<Device?> GetDeviceByApiKey()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var key))
            return null;
        return await _db.Devices.FirstOrDefaultAsync(d => d.AgentApiKey == key.ToString());
    }

    private static bool IsNewerVersion(string? candidate, string? current)
    {
        if (!Version.TryParse(candidate, out var v1)) return false;
        if (!Version.TryParse(current, out var v2)) return true; // current unknown → treat as outdated
        return v1 > v2;
    }
}

public record RegisterRequest(
    string RegistrationToken,
    string Hostname,
    string WindowsVersion,
    string CpuModel,
    double RamTotalGB,
    string? InstallToken = null
);

public record CheckinRequest(
    string Hostname,
    string WindowsVersion,
    string WindowsBuild,
    string WindowsEdition,
    string LicenseType,
    string CpuModel,
    int CpuCores,
    double RamTotalGB,
    double RamUsedGB,
    List<NetworkAdapterDto> NetworkAdapters,
    List<DiskDriveDto> DiskDrives,
    List<SoftwareDto> InstalledSoftware,
    string RustDeskId = "",
    string AgentVersion = "",
    string BiosInfoJson = "{}",
    string DefenderStatusJson = "{}",
    int PendingUpdatesCount = 0,
    DateTime? LastWindowsUpdateInstalled = null,
    string EventLogErrorsJson = "[]"
);

public record NetworkAdapterDto(
    string Name,
    string IpAddress,
    string MacAddress,
    string Ipv6Address = "",
    string SubnetMask = "",
    string Gateway = "",
    List<string>? DnsServers = null,
    long SpeedMbps = 0,
    string AdapterType = "");
public record DiskDriveDto(string Drive, double TotalGB, double FreeGB);
public record SoftwareDto(string Name, string Version, string Publisher, string InstallDate);

public record LicenseSubmitRequest(
    string WindowsKey,
    string LicenseType,
    string OfficeKey,
    string OfficeVersion
);

public record CommandResultRequest(bool Success, string? Message);
