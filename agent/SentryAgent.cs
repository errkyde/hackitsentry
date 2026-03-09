using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;

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

    private readonly string _stateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "HackITSentry", "agent-state.json");

    // Current agent version (set at build time via AssemblyInfo or hardcoded here)
    private static readonly string CurrentVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

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
        _logger.LogInformation("HackIT Sentry Agent starting (v{Version})...", CurrentVersion);

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

        _logger.LogDebug("Check-in successful. LicenseRequested={LicReq}, HasCommands={HasCmds}",
            response.LicenseRequested, response.HasPendingCommands);

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

        if (response.HasPendingCommands)
        {
            await ProcessPendingCommandsAsync();
        }

        // Check for self-update
        if (!string.IsNullOrEmpty(response.LatestAgentVersion) &&
            !string.IsNullOrEmpty(response.AgentDownloadUrl) &&
            response.LatestAgentVersion != CurrentVersion)
        {
            _logger.LogInformation("New agent version available: {New} (current: {Current})",
                response.LatestAgentVersion, CurrentVersion);
            await TryAutoUpdateAsync(response.AgentDownloadUrl, response.LatestAgentVersion);
        }
    }

    private async Task ProcessPendingCommandsAsync()
    {
        var commands = await _http.GetPendingCommandsAsync();
        if (commands == null || commands.Count == 0) return;

        _logger.LogInformation("Processing {Count} pending command(s).", commands.Count);

        foreach (var cmd in commands)
        {
            _logger.LogInformation("Executing command: {Type} (Id={Id})", cmd.CommandType, cmd.Id);
            (bool success, string? message) = await ExecuteCommandAsync(cmd);
            await _http.ReportCommandResultAsync(cmd.Id, success, message);
        }
    }

    private async Task<(bool success, string? message)> ExecuteCommandAsync(PendingCommandDto cmd)
    {
        try
        {
            switch (cmd.CommandType)
            {
                case "Restart":
                    _logger.LogInformation("Initiating system restart...");
                    Process.Start("shutdown", "/r /t 10 /c \"HackIT Sentry: Remote restart\"");
                    return (true, "Restart initiated (10s delay)");

                case "Shutdown":
                    _logger.LogInformation("Initiating system shutdown...");
                    Process.Start("shutdown", "/s /t 10 /c \"HackIT Sentry: Remote shutdown\"");
                    return (true, "Shutdown initiated (10s delay)");

                case "RunScript":
                    if (string.IsNullOrWhiteSpace(cmd.Parameters))
                        return (false, "No script content provided");

                    var tempFile = Path.Combine(Path.GetTempPath(), $"sentry_cmd_{Guid.NewGuid():N}.ps1");
                    await File.WriteAllTextAsync(tempFile, cmd.Parameters);

                    var psi = new ProcessStartInfo("powershell.exe",
                        $"-NonInteractive -ExecutionPolicy Bypass -File \"{tempFile}\"")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc == null) return (false, "Failed to start process");

                    var output = await proc.StandardOutput.ReadToEndAsync();
                    var error = await proc.StandardError.ReadToEndAsync();
                    await proc.WaitForExitAsync();

                    File.Delete(tempFile);

                    var result = (output + error).Trim();
                    return (proc.ExitCode == 0, result.Length > 500 ? result[..500] : result);

                default:
                    return (false, $"Unknown command type: {cmd.CommandType}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command execution failed: {Type}", cmd.CommandType);
            return (false, ex.Message);
        }
    }

    private async Task TryAutoUpdateAsync(string downloadUrl, string newVersion)
    {
        try
        {
            _logger.LogInformation("Downloading agent update from {Url}...", downloadUrl);

            var data = await _http.DownloadFileAsync(downloadUrl);
            if (data == null)
            {
                _logger.LogWarning("Failed to download update.");
                return;
            }

            var updateDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "HackITSentry", "updates");
            Directory.CreateDirectory(updateDir);

            var installerPath = Path.Combine(updateDir, $"SentryAgent-{newVersion}.msi");
            await File.WriteAllBytesAsync(installerPath, data);

            _logger.LogInformation("Starting installer: {Path}", installerPath);
            Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{installerPath}\" /quiet /norestart")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-update failed");
        }
    }

    private void PersistApiKey(string apiKey)
    {
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
                return JsonSerializer.Deserialize<AgentState>(File.ReadAllText(_stateFile));
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
