using Microsoft.Extensions.Options;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HackITSentry.Agent;

[SupportedOSPlatform("windows")]
public class SentryAgent : BackgroundService
{
    private readonly AgentHttpClient _http;
    private readonly SystemInfoCollector _sysInfo;
    private readonly LicenseCollector _licenseCollector;
    private readonly IOptionsMonitor<AgentConfig> _config;
    private readonly ILogger<SentryAgent> _logger;
    private readonly IConfiguration _fullConfig;

    // File path where we store the registration token and API key
    private readonly string _stateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "HackITSentry", "agent-state.json");

    public SentryAgent(
        AgentHttpClient http,
        SystemInfoCollector sysInfo,
        LicenseCollector licenseCollector,
        IOptionsMonitor<AgentConfig> config,
        ILogger<SentryAgent> logger,
        IConfiguration fullConfig)
    {
        _http = http;
        _sysInfo = sysInfo;
        _licenseCollector = licenseCollector;
        _config = config;
        _logger = logger;
        _fullConfig = fullConfig;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HackIT Sentry Agent starting...");

        // If no API key configured, start registration flow
        var apiKey = _config.CurrentValue.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            apiKey = await RegisterAndWaitForApproval(stoppingToken);
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Could not obtain API key. Will retry on next start.");
                return;
            }
        }

        _logger.LogInformation("Agent registered and approved. Starting check-in loop.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformCheckin(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during check-in");
            }

            var interval = TimeSpan.FromMinutes(_config.CurrentValue.CheckinIntervalMinutes);
            _logger.LogDebug("Next check-in in {Minutes} minutes", interval.TotalMinutes);
            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task<string?> RegisterAndWaitForApproval(CancellationToken stoppingToken)
    {
        var state = LoadState();
        if (state == null)
        {
            // Generate a unique registration token
            var token = Guid.NewGuid().ToString("N");
            var sysInfo = _sysInfo.Collect();

            _logger.LogInformation("Sending registration request for {Hostname}...", sysInfo.Hostname);

            var response = await _http.RegisterAsync(new
            {
                registrationToken = token,
                hostname = sysInfo.Hostname,
                windowsVersion = sysInfo.WindowsVersion,
                cpuModel = sysInfo.CpuModel,
                ramTotalGB = sysInfo.RamTotalGB
            });

            if (response == null)
            {
                _logger.LogError("Registration failed - could not reach server");
                return null;
            }

            state = new AgentState { RegistrationToken = token };
            SaveState(state);
            _logger.LogInformation("Registration request sent. Waiting for admin approval...");
        }
        else
        {
            _logger.LogInformation("Resuming pending registration, token: {Token}", state.RegistrationToken);
        }

        // Poll for approval
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            var status = await _http.GetRegistrationStatusAsync(state.RegistrationToken);
            if (status == null) continue;

            _logger.LogDebug("Registration status: {Status}", status.Status);

            if (status.Status == "Approved" && !string.IsNullOrEmpty(status.ApiKey))
            {
                _logger.LogInformation("Device approved! API key received.");
                state.ApiKey = status.ApiKey;
                SaveState(state);

                // Write API key to appsettings so it persists across restarts
                PersistApiKey(status.ApiKey);
                return status.ApiKey;
            }

            if (status.Status == "Rejected")
            {
                _logger.LogWarning("Registration was rejected by admin.");
                return null;
            }
        }

        return null;
    }

    private async Task PerformCheckin(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Performing check-in...");
        var info = _sysInfo.Collect();

        var payload = new
        {
            hostname = info.Hostname,
            windowsVersion = info.WindowsVersion,
            windowsBuild = info.WindowsBuild,
            windowsEdition = info.WindowsEdition,
            licenseType = info.LicenseType,
            cpuModel = info.CpuModel,
            cpuCores = info.CpuCores,
            ramTotalGB = info.RamTotalGB,
            ramUsedGB = info.RamUsedGB,
            networkAdapters = info.NetworkAdapters.Select(n => new
            {
                name = n.Name,
                ipAddress = n.IpAddress,
                macAddress = n.MacAddress
            }),
            diskDrives = info.DiskDrives.Select(d => new
            {
                drive = d.Drive,
                totalGB = d.TotalGB,
                freeGB = d.FreeGB
            }),
            installedSoftware = info.InstalledSoftware.Select(s => new
            {
                name = s.Name,
                version = s.Version,
                publisher = s.Publisher,
                installDate = s.InstallDate
            })
        };

        var response = await _http.CheckinAsync(payload);
        if (response == null)
        {
            _logger.LogWarning("Check-in failed");
            return;
        }

        _logger.LogDebug("Check-in successful. LicenseRequested: {LicenseRequested}", response.LicenseRequested);

        if (response.LicenseRequested)
        {
            _logger.LogInformation("License key requested - collecting keys...");
            var licenseData = _licenseCollector.Collect();
            await _http.SubmitLicenseKeyAsync(new
            {
                windowsKey = licenseData.WindowsKey,
                licenseType = licenseData.LicenseType,
                officeKey = licenseData.OfficeKey,
                officeVersion = licenseData.OfficeVersion
            });
            _logger.LogInformation("License keys submitted.");
        }
    }

    private void PersistApiKey(string apiKey)
    {
        // Update the appsettings.json file with the new API key
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        try
        {
            var json = File.Exists(configPath) ? File.ReadAllText(configPath) : "{}";
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.Deserialize<Dictionary<string, JsonElement>>() ?? [];

            var sentryAgent = new Dictionary<string, object>();
            if (root.TryGetValue("SentryAgent", out var existing))
            {
                var existingDict = existing.Deserialize<Dictionary<string, object>>() ?? [];
                foreach (var kv in existingDict) sentryAgent[kv.Key] = kv.Value;
            }
            sentryAgent["ApiKey"] = apiKey;
            root["SentryAgent"] = JsonSerializer.SerializeToElement(sentryAgent);

            File.WriteAllText(configPath, JsonSerializer.Serialize(root,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist API key to config file");
        }
    }

    private AgentState? LoadState()
    {
        try
        {
            if (File.Exists(_stateFile))
            {
                var json = File.ReadAllText(_stateFile);
                return JsonSerializer.Deserialize<AgentState>(json);
            }
        }
        catch { }
        return null;
    }

    private void SaveState(AgentState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(state));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save agent state");
        }
    }
}

public class AgentState
{
    public string RegistrationToken { get; set; } = "";
    public string? ApiKey { get; set; }
}
