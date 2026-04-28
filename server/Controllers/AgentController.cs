using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using HackITSentry.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    public AgentController(AppDbContext db, LicenseEncryptionService encryption, IConfiguration config, RuntimeSettings runtimeSettings, AlertEmailService email, AgentCommandNotifier notifier)
    {
        _db = db;
        _encryption = encryption;
        _config = config;
        _runtimeSettings = runtimeSettings;
        _email = email;
        _notifier = notifier;
    }

    // POST /api/agent/register
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var existing = await _db.PendingDevices
            .FirstOrDefaultAsync(p => p.RegistrationToken == request.RegistrationToken);

        if (existing != null)
            return Ok(new { status = existing.Status.ToString(), id = existing.Id });

        string? invitedBy = null;
        if (!string.IsNullOrEmpty(request.InstallToken))
        {
            var installToken = await _db.InstallTokens
                .FirstOrDefaultAsync(t => t.Token == request.InstallToken && t.ExpiresAt > DateTime.UtcNow);
            invitedBy = installToken?.CreatedByUsername;
        }

        var pending = new PendingDevice
        {
            RegistrationToken = request.RegistrationToken,
            Hostname = request.Hostname,
            WindowsVersion = request.WindowsVersion,
            CpuModel = request.CpuModel,
            RamTotalGB = request.RamTotalGB,
            InvitedByUsername = invitedBy
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

        device.Hostname = request.Hostname;
        device.WindowsVersion = request.WindowsVersion;
        device.WindowsBuild = request.WindowsBuild;
        device.WindowsEdition = request.WindowsEdition;
        device.LicenseType = request.LicenseType;
        device.CpuModel = request.CpuModel;
        device.CpuCores = request.CpuCores;
        device.RamTotalGB = request.RamTotalGB;
        device.NetworkAdaptersJson = JsonSerializer.Serialize(request.NetworkAdapters, camelCase);
        device.LastSeenAt = DateTime.UtcNow;
        device.RustDeskId = request.RustDeskId; // always sync — reflects current agent state
        if (!string.IsNullOrEmpty(request.AgentVersion))
            device.AgentVersion = request.AgentVersion;

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
                Name = sw.Name,
                Version = sw.Version,
                Publisher = sw.Publisher,
                InstallDate = sw.InstallDate
            });
        }

        await _db.SaveChangesAsync();

        // Check disk space thresholds
        await CheckDiskAlertsAsync(device, request.DiskDrives);

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

        // Auto-update: queue ForceUpdate if enabled and a newer version exists
        if (_runtimeSettings.AutoUpdateAgents
            && latestVersion != null
            && !string.IsNullOrEmpty(device.AgentVersion)
            && latestVersion.Version != device.AgentVersion)
        {
            var alreadyQueued = await _db.DeviceCommands.AnyAsync(c =>
                c.DeviceId == device.Id &&
                c.CommandType == CommandType.ForceUpdate &&
                (c.Status == CommandStatus.Pending || c.Status == CommandStatus.Sent));

            if (!alreadyQueued)
            {
                _db.DeviceCommands.Add(new DeviceCommand
                {
                    DeviceId = device.Id,
                    CommandType = CommandType.ForceUpdate,
                    IssuedByUsername = "system",
                });
                await _db.SaveChangesAsync();
                hasPendingCommands = true;
                _notifier.NotifyDevice(device.Id);
            }
        }

        return Ok(new
        {
            licenseRequested = device.LicenseRequested,
            hasPendingCommands,
            latestAgentVersion = latestVersion?.Version,
            agentDownloadUrl = latestVersion?.DownloadUrl,
            rustDeskRelayServer = _runtimeSettings.RustDeskRelayHost,
            rustDeskPublicKey = _runtimeSettings.RustDeskPublicKey,
            rustDeskAutoInstall = _runtimeSettings.RustDeskAutoInstall,
            rustDeskDownloadUrl = _runtimeSettings.RustDeskDownloadUrl,
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

        var commands = await _db.DeviceCommands
            .Where(c => c.DeviceId == device.Id && c.Status == CommandStatus.Pending)
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

        await _db.SaveChangesAsync();

        return Ok();
    }

    private async Task CheckDiskAlertsAsync(Device device, List<DiskDriveDto> drives)
    {
        try
        {
            var thresholdSetting = await _db.AppSettings.FindAsync("DiskAlertThresholdPercent");
            if (!int.TryParse(thresholdSetting?.Value, out var threshold))
                threshold = 10; // default 10%

            var criticalDrives = drives
                .Where(d => d.TotalGB > 0 && (d.FreeGB / d.TotalGB * 100) < threshold)
                .ToList();

            if (criticalDrives.Count > 0)
            {
                var rows = criticalDrives.Select(d =>
                {
                    var pct = d.FreeGB / d.TotalGB * 100;
                    var barColor = pct < 5 ? "#dc2626" : "#ea580c";
                    var bar = $"<div style=\"margin-top:6px;height:6px;border-radius:3px;background:#f4f4f5;\">" +
                              $"<div style=\"width:{100 - pct:F0}%;height:6px;border-radius:3px;background:{barColor};\"></div></div>";
                    return (
                        $"Laufwerk {d.Drive}",
                        (string?)($"{d.FreeGB:F1} GB frei von {d.TotalGB:F1} GB ({pct:F0}% frei){bar}"),
                        (string?)null
                    );
                });

                await _email.SendAsync(
                    $"[HackIT Sentry] Wenig Speicherplatz auf {device.Hostname}",
                    AlertEmailService.BuildHtml(
                        "#ea580c", "Speicherplatz-Warnung",
                        $"Kritisch wenig Speicherplatz auf {device.Hostname}",
                        AlertEmailService.DeviceRows(rows),
                        $"Schwellwert: unter {threshold}% freier Speicher"));
            }
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
            var blacklist = await _db.SoftwareBlacklist.ToListAsync();
            if (blacklist.Count == 0) return;

            foreach (var entry in blacklist)
            {
                var matches = software.Where(s =>
                    s.Name.Contains(entry.NamePattern, StringComparison.OrdinalIgnoreCase) &&
                    (entry.Publisher == null || s.Publisher.Contains(entry.Publisher, StringComparison.OrdinalIgnoreCase)));

                foreach (var match in matches)
                {
                    // Only create alert if not already open for this device+entry combo
                    var exists = await _db.SoftwareAlerts.AnyAsync(a =>
                        a.DeviceId == device.Id &&
                        a.BlacklistEntryId == entry.Id &&
                        a.AcknowledgedAt == null);

                    if (!exists)
                    {
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
            }

            await _db.SaveChangesAsync();
        }
        catch
        {
            // Don't fail checkin on alert error
        }
    }

    private async Task<Device?> GetDeviceByApiKey()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var key))
            return null;
        return await _db.Devices.FirstOrDefaultAsync(d => d.AgentApiKey == key.ToString());
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
    string AgentVersion = ""
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
