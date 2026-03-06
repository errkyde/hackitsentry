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

    public AgentController(AppDbContext db, LicenseEncryptionService encryption, IConfiguration config)
    {
        _db = db;
        _encryption = encryption;
        _config = config;
    }

    // POST /api/agent/register
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        // Idempotent: if same token already exists, return current status
        var existing = await _db.PendingDevices
            .FirstOrDefaultAsync(p => p.RegistrationToken == request.RegistrationToken);

        if (existing != null)
            return Ok(new { status = existing.Status.ToString(), id = existing.Id });

        var pending = new PendingDevice
        {
            RegistrationToken = request.RegistrationToken,
            Hostname = request.Hostname,
            WindowsVersion = request.WindowsVersion,
            CpuModel = request.CpuModel,
            RamTotalGB = request.RamTotalGB
        };

        _db.PendingDevices.Add(pending);
        await _db.SaveChangesAsync();

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

        // Update device info
        device.Hostname = request.Hostname;
        device.WindowsVersion = request.WindowsVersion;
        device.WindowsBuild = request.WindowsBuild;
        device.WindowsEdition = request.WindowsEdition;
        device.LicenseType = request.LicenseType;
        device.CpuModel = request.CpuModel;
        device.CpuCores = request.CpuCores;
        device.RamTotalGB = request.RamTotalGB;
        device.NetworkAdaptersJson = JsonSerializer.Serialize(request.NetworkAdapters);
        device.LastSeenAt = DateTime.UtcNow;

        // Record checkin history
        _db.DeviceCheckins.Add(new DeviceCheckin
        {
            DeviceId = device.Id,
            RamUsedGB = request.RamUsedGB,
            DiskDrivesJson = JsonSerializer.Serialize(request.DiskDrives)
        });

        // Upsert installed software
        var existing = _db.InstalledSoftware.Where(s => s.DeviceId == device.Id);
        _db.InstalledSoftware.RemoveRange(existing);

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

        return Ok(new { licenseRequested = device.LicenseRequested });
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
    double RamTotalGB
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
    List<SoftwareDto> InstalledSoftware
);

public record NetworkAdapterDto(string Name, string IpAddress, string MacAddress);
public record DiskDriveDto(string Drive, double TotalGB, double FreeGB);
public record SoftwareDto(string Name, string Version, string Publisher, string InstallDate);

public record LicenseSubmitRequest(
    string WindowsKey,
    string LicenseType,
    string OfficeKey,
    string OfficeVersion
);
