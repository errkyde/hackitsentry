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

    // Used to cancel the inter-checkin delay for immediate re-checkin
    private CancellationTokenSource _forceCheckinCts = new();

    // Runtime override for check-in interval (received from server, overrides appsettings)
    private int? _checkinIntervalOverride;

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

        CleanupLegacyState();

        var apiKey = SecureStore.LoadApiKey();

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

        // Separate lightweight loop: poll for commands every 30 seconds
        _ = Task.Run(() => CommandPollLoopAsync(stoppingToken), stoppingToken);

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

            // If the API key was cleared by a 401 response, stop here.
            // Windows service recovery will restart us and the agent will re-register.
            if (string.IsNullOrEmpty(SecureStore.LoadApiKey()))
            {
                _logger.LogWarning("API key was invalidated by the server. Stopping for re-registration on restart...");
                DeleteStateFile();
                return;
            }

            var interval = TimeSpan.FromMinutes(_checkinIntervalOverride ?? _config.CurrentValue.CheckinIntervalMinutes);
            _logger.LogDebug("Next check-in in {Minutes} minutes", interval.TotalMinutes);

            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _forceCheckinCts.Token);
                await Task.Delay(interval, linked.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // ForceCheckin was requested — reset CTS and continue immediately
                _forceCheckinCts = new CancellationTokenSource();
                _logger.LogInformation("Force check-in triggered — skipping delay.");
            }
        }
    }

    private async Task CommandPollLoopAsync(CancellationToken stoppingToken)
    {
        // Brief initial delay so the first full check-in completes first
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Check for commands queued while we were reconnecting (handles missed notifications)
            try { await ProcessPendingCommandsAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Error processing commands"); }

            if (stoppingToken.IsCancellationRequested) break;

            // Block until the server signals a new command (or 29s timeout)
            await _http.WaitForCommandAsync(stoppingToken).ConfigureAwait(false);

            if (stoppingToken.IsCancellationRequested) break;

            // Execute whatever was signalled
            try { await ProcessPendingCommandsAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Error processing commands after long-poll"); }
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
                ramTotalGB = sysInfo.RamTotalGB,
                installToken = _config.CurrentValue.InstallToken
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

        // Check immediately on (re)start — no initial delay
        while (!stoppingToken.IsCancellationRequested)
        {
            var status = await _http.GetRegistrationStatusAsync(state.RegistrationToken);

            if (status != null)
            {
                _logger.LogDebug("Registration status: {Status}", status.Status);

                if (status.Status == "Approved" && !string.IsNullOrEmpty(status.ApiKey))
                {
                    _logger.LogInformation("Device approved! API key received.");
                    PersistApiKey(status.ApiKey);
                    return status.ApiKey;
                }

                if (status.Status == "Rejected")
                {
                    _logger.LogWarning("Registration was rejected by admin. Uninstalling agent...");
                    _ = Task.Run(() => UninstallSelf());
                    return null;
                }

                if (status.Status == "NotFound")
                {
                    _logger.LogWarning("Registration token no longer exists on server. Clearing state and re-registering...");
                    DeleteStateFile();
                    return null;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        return null;
    }

    private async Task PerformCheckin(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Performing check-in...");
        var info = _sysInfo.Collect();
        _logger.LogInformation("RustDesk ID found on this PC: '{Id}'", string.IsNullOrEmpty(info.RustDeskId) ? "(leer)" : info.RustDeskId);
        var state = LoadState() ?? new AgentState();

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
                macAddress = n.MacAddress,
                ipv6Address = n.Ipv6Address,
                subnetMask = n.SubnetMask,
                gateway = n.Gateway,
                dnsServers = n.DnsServers,
                speedMbps = n.SpeedMbps,
                adapterType = n.AdapterType
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
            }),
            rustDeskId = info.RustDeskId,
            agentVersion = CurrentVersion
        };

        var response = await _http.CheckinAsync(payload);
        if (response == null)
        {
            _logger.LogWarning("Check-in failed");
            return;
        }

        _logger.LogInformation("Check-in successful. LicenseRequested={LicReq}, HasCommands={HasCmds}, RustDeskAutoInstall={RdAuto}, RustDeskRelay={RdRelay}, RustDeskDownloadUrl={RdUrl}",
            response.LicenseRequested, response.HasPendingCommands,
            response.RustDeskAutoInstall, response.RustDeskRelayServer ?? "(leer)", response.RustDeskDownloadUrl ?? "(leer)");

        if (response.CheckinIntervalMinutes.HasValue && response.CheckinIntervalMinutes.Value > 0)
            _checkinIntervalOverride = response.CheckinIntervalMinutes.Value;

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

        // Auto-install RustDesk if requested and not yet present (don't start service yet)
        bool rustDeskServiceNeeded = false;
        _logger.LogInformation("RustDesk check: AutoInstall={Auto}, Installed={Installed}, DownloadUrl={Url}",
            response.RustDeskAutoInstall, IsRustDeskInstalled(), response.RustDeskDownloadUrl ?? "(leer)");
        if (response.RustDeskAutoInstall && !IsRustDeskInstalled())
        {
            _logger.LogInformation("RustDesk not found — starting silent installation...");
            await InstallRustDeskAsync(response.RustDeskDownloadUrl, stoppingToken);
            rustDeskServiceNeeded = true; // will start after config is written
        }

        // Configure RustDesk relay + public key BEFORE starting/restarting the service
        // Version is included in the key so any agent update triggers a fresh reconfiguration
        if (!string.IsNullOrEmpty(response.RustDeskRelayServer) && IsRustDeskInstalled())
        {
            var configKey = $"{response.RustDeskRelayServer}|{response.RustDeskPublicKey}|v{CurrentVersion}";
            if (state.RustDeskConfiguredFor != configKey)
            {
                ConfigureRustDesk(response.RustDeskRelayServer, response.RustDeskPublicKey ?? "");
                state.RustDeskConfiguredFor = configKey;
                SaveState(state);
                rustDeskServiceNeeded = true; // restart so RustDesk picks up new config
                _logger.LogInformation("RustDesk relay/key configured.");
            }
        }
        else if (!string.IsNullOrEmpty(response.RustDeskRelayServer) && !IsRustDeskInstalled())
        {
            _logger.LogDebug("RustDesk not installed yet — will retry at next check-in.");
        }

        // Start or restart RustDesk service now that config is written
        if (rustDeskServiceNeeded)
        {
            try
            {
                Process.Start(new ProcessStartInfo("sc", "stop RustDesk")
                    { CreateNoWindow = true, UseShellExecute = false });
                await Task.Delay(1500, stoppingToken);
                Process.Start(new ProcessStartInfo("sc", "start RustDesk")
                    { CreateNoWindow = true, UseShellExecute = false });
                _logger.LogInformation("RustDesk service started/restarted.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not start/restart RustDesk service (non-fatal).");
            }
        }

        // Check for self-update
        if (!string.IsNullOrEmpty(response.LatestAgentVersion) &&
            !string.IsNullOrEmpty(response.AgentDownloadUrl) &&
            response.LatestAgentVersion != CurrentVersion)
        {
            _logger.LogInformation("New agent version available: {New} (current: {Current})",
                response.LatestAgentVersion, CurrentVersion);
            await TryAutoUpdateAsync(response.AgentDownloadUrl, response.LatestAgentVersion, stoppingToken);
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
                {
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
                }

                case "ForceCheckin":
                    _logger.LogInformation("Force check-in requested.");
                    _forceCheckinCts.Cancel();
                    return (true, "Check-in triggered");

                case "CollectLicense":
                {
                    _logger.LogInformation("License key collection requested.");
                    var licenseData = _licenseCollector.Collect();
                    await _http.SubmitLicenseKeyAsync(new
                    {
                        windowsKey = licenseData.WindowsKey,
                        licenseType = licenseData.LicenseType,
                        officeKey = licenseData.OfficeKey,
                        officeVersion = licenseData.OfficeVersion
                    });
                    return (true, "License keys collected and submitted.");
                }

                case "Uninstall":
                    _ = Task.Run(() => UninstallSelf());
                    return (true, "Uninstall initiated");

                case "ForceUpdate":
                {
                    if (string.IsNullOrWhiteSpace(cmd.Parameters))
                        return (false, "No download URL provided");

                    _logger.LogInformation("Force update requested — URL: {Url}", cmd.Parameters);
                    _ = Task.Run(() => TryAutoUpdateAsync(cmd.Parameters.Trim(), "forced"));
                    return (true, "Update download started, service will restart shortly.");
                }

                case "InitRustDesk":
                {
                    _logger.LogInformation("RustDesk initialisation requested via command.");
                    var state = LoadState() ?? new AgentState();
                    // Clear configured-for so the next check-in reconfigures relay + key
                    state.RustDeskConfiguredFor = "";
                    SaveState(state);
                    // Trigger immediate check-in (handles install + configure)
                    _forceCheckinCts.Cancel();
                    return (true, "RustDesk initialisation triggered — will install/configure at next check-in.");
                }

                case "UpdateServerUrl":
                {
                    if (string.IsNullOrWhiteSpace(cmd.Parameters))
                        return (false, "No URL provided");

                    var newUrl = cmd.Parameters.Trim();
                    _logger.LogInformation("Updating server URL to: {Url}", newUrl);

                    // Persist encrypted + update plaintext config as fallback
                    SecureStore.SaveServerUrl(newUrl);
                    UpdateServerUrlInConfig(newUrl);

                    // Restart service via a delayed batch script (same pattern as uninstall)
                    _ = Task.Run(() => RestartService());
                    return (true, $"Server URL updated to {newUrl}. Restarting...");
                }

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

    private async void UninstallSelf()
    {
        try
        {
            _logger.LogInformation("Uninstalling HackIT Sentry Agent...");

            // Notify server to delete device record
            await _http.UninstallAsync();
            SecureStore.Delete();

            // Small delay so the command result can be reported first
            await Task.Delay(3000);

            var installDir = AppContext.BaseDirectory;
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "HackITSentry");

            // Build self-deleting batch script
            var bat = Path.Combine(Path.GetTempPath(), "hackit_uninstall.bat");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "ping -n 4 127.0.0.1 > nul\r\n" +
                "sc stop HackITSentryAgent > nul 2>&1\r\n" +
                "ping -n 3 127.0.0.1 > nul\r\n" +
                "sc delete HackITSentryAgent > nul 2>&1\r\n" +
                $"rd /s /q \"{installDir}\" > nul 2>&1\r\n" +
                $"rd /s /q \"{dataDir}\" > nul 2>&1\r\n" +
                "del /f /q \"%~f0\"\r\n");

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Uninstall failed");
        }
    }

    private async Task TryAutoUpdateAsync(string downloadUrl, string newVersion, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Downloading agent update v{Version} from {Url}...", newVersion, downloadUrl);

            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromMinutes(10);

            var updateDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "HackITSentry", "updates");
            Directory.CreateDirectory(updateDir);

            var newExe = Path.Combine(updateDir, $"HackITSentry.Agent-{newVersion}.exe");

            // Stream to file — avoids loading 68 MB into RAM and is more resilient
            // to proxy/timeout issues that can cut GetByteArrayAsync mid-transfer.
            using (var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                using var src = await response.Content.ReadAsStreamAsync(ct);
                using var dst = new FileStream(newExe, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await src.CopyToAsync(dst, ct);
            }

            // Batch: wait → stop service → replace exe → start service → cleanup
            var installPath = Process.GetCurrentProcess().MainModule?.FileName
                ?? Path.Combine(AppContext.BaseDirectory, "HackITSentry.Agent.exe");
            var bat = Path.Combine(Path.GetTempPath(), "hackit_update.bat");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "ping -n 5 127.0.0.1 > nul\r\n" +
                "sc stop HackITSentryAgent > nul 2>&1\r\n" +
                "ping -n 3 127.0.0.1 > nul\r\n" +
                $"copy /y \"{newExe}\" \"{installPath}\" > nul\r\n" +
                "sc start HackITSentryAgent > nul 2>&1\r\n" +
                $"del /f /q \"{newExe}\"\r\n" +
                "del /f /q \"%~f0\"\r\n");

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });

            _logger.LogInformation("Update installer launched, exiting for replacement...");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-update failed");
        }
    }

    private void PersistApiKey(string apiKey)
    {
        try
        {
            SecureStore.SaveApiKey(apiKey);
            _logger.LogInformation("API key saved to encrypted store.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save API key to encrypted store");
        }
    }

    private void CleanupLegacyState()
    {
        // If there's a stale registration token but no API key, clear it so we re-register fresh.
        // This happens when the server DB was reset or the token expired/was deleted.
        if (File.Exists(_stateFile) && string.IsNullOrEmpty(SecureStore.LoadApiKey()))
        {
            try
            {
                var state = LoadState();
                // Only clear if the state file is older than 1 hour (not a just-created registration)
                if (state != null && new FileInfo(_stateFile).LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1))
                {
                    _logger.LogWarning("Stale registration state detected (no API key, >1h old). Clearing for fresh registration.");
                    DeleteStateFile();
                }
            }
            catch { }
        }

        // Remove files from the old PowerShell installer path ("HackIT Sentry" with space)
        var legacyDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HackIT Sentry");
        if (Directory.Exists(legacyDataDir))
        {
            try
            {
                Directory.Delete(legacyDataDir, recursive: true);
                _logger.LogInformation("Removed legacy data directory: {Dir}", legacyDataDir);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not remove legacy directory {Dir}", legacyDataDir);
            }
        }
    }

    private void DeleteStateFile()
    {
        try { if (File.Exists(_stateFile)) File.Delete(_stateFile); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete state file"); }
    }

    private void UpdateServerUrlInConfig(string newUrl)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        try
        {
            if (!File.Exists(configPath)) return;
            var json = File.ReadAllText(configPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.Deserialize<Dictionary<string, JsonElement>>() ?? [];

            if (!root.TryGetValue("SentryAgent", out var section)) return;
            var sentryAgent = section.Deserialize<Dictionary<string, object>>() ?? [];
            sentryAgent["ServerUrl"] = newUrl;
            root["SentryAgent"] = JsonSerializer.SerializeToElement(sentryAgent);
            File.WriteAllText(configPath, JsonSerializer.Serialize(root,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update ServerUrl in config file");
        }
    }

    private void RestartService()
    {
        try
        {
            var bat = Path.Combine(Path.GetTempPath(), "hackit_restart.bat");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "ping -n 4 127.0.0.1 > nul\r\n" +
                "sc stop HackITSentryAgent > nul 2>&1\r\n" +
                "ping -n 3 127.0.0.1 > nul\r\n" +
                "sc start HackITSentryAgent > nul 2>&1\r\n" +
                "del /f /q \"%~f0\"\r\n");

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service restart failed");
        }
    }

    private static readonly string[] RustDeskTomlPaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustDesk", "config", "RustDesk.toml"),
        @"C:\Windows\ServiceProfiles\LocalService\AppData\Roaming\RustDesk\config\RustDesk.toml",
        @"C:\Windows\ServiceProfiles\NetworkService\AppData\Roaming\RustDesk\config\RustDesk.toml",
        @"C:\Windows\system32\config\systemprofile\AppData\Roaming\RustDesk\config\RustDesk.toml",
    ];

    // RustDesk 1.2+ uses RustDesk2.toml with an [options] section for server settings
    private static readonly string[] RustDesk2TomlPaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustDesk", "config", "RustDesk2.toml"),
        @"C:\Windows\ServiceProfiles\LocalService\AppData\Roaming\RustDesk\config\RustDesk2.toml",
        @"C:\Windows\ServiceProfiles\NetworkService\AppData\Roaming\RustDesk\config\RustDesk2.toml",
        @"C:\Windows\system32\config\systemprofile\AppData\Roaming\RustDesk\config\RustDesk2.toml",
    ];

    private static bool IsRustDeskInstalled()
    {
        var exePaths = new[]
        {
            @"C:\Program Files\RustDesk\RustDesk.exe",
            @"C:\Program Files (x86)\RustDesk\RustDesk.exe",
        };
        return exePaths.Any(File.Exists);
    }

    private async Task<string?> ResolveRustDeskDownloadUrlAsync(string? configuredUrl)
    {
        if (!string.IsNullOrWhiteSpace(configuredUrl))
            return configuredUrl;

        try
        {
            _logger.LogInformation("No download URL configured — fetching latest RustDesk release from GitHub...");
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("HackITSentry-Agent/1.0");
            http.Timeout = TimeSpan.FromSeconds(15);

            var json = await http.GetStringAsync(
                "https://api.github.com/repos/rustdesk/rustdesk/releases/latest");

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var assets = doc.RootElement.GetProperty("assets");

            // Prefer x86_64 installer; skip portable and sciter variants
            string? fallback = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var url  = asset.GetProperty("browser_download_url").GetString() ?? "";

                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("portable", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("sciter",   StringComparison.OrdinalIgnoreCase)) continue;

                if (name.Contains("x86_64", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Found RustDesk installer: {Name}", name);
                    return url;
                }
                fallback ??= url; // keep first .exe as fallback
            }

            if (fallback != null)
                _logger.LogInformation("Using fallback RustDesk installer.");

            return fallback;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve RustDesk download URL from GitHub");
            return null;
        }
    }

    private async Task InstallRustDeskAsync(string? configuredUrl, CancellationToken ct = default)
    {
        var downloadUrl = await ResolveRustDeskDownloadUrlAsync(configuredUrl);
        if (string.IsNullOrEmpty(downloadUrl))
        {
            _logger.LogWarning("RustDesk install skipped — no download URL available.");
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), "rustdesk-installer.exe");
        try
        {
            _logger.LogInformation("Downloading RustDesk from {Url}", downloadUrl);
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromMinutes(10);
            using (var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                using var src = await response.Content.ReadAsStreamAsync(ct);
                using var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await src.CopyToAsync(dst, ct);
            }

            _logger.LogInformation("Running RustDesk silent installer...");
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = "--silent-install",
                UseShellExecute = true,
            });
            if (proc != null) await proc.WaitForExitAsync(ct);
            _logger.LogInformation("RustDesk installation finished (exit code: {Code})", proc?.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RustDesk auto-install failed");
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private void ConfigureRustDesk(string host, string publicKey)
    {
        bool configured = false;

        // ── RustDesk.toml (flat format, backwards compat) ──────────────
        foreach (var path in RustDeskTomlPaths)
        {
            try
            {
                var lines = File.Exists(path)
                    ? File.ReadAllLines(path).ToList()
                    : new List<string>();
                if (!File.Exists(path)) Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                SetRootField(lines, "relay-server", host);
                SetRootField(lines, "rendezvous_server", host);
                if (!string.IsNullOrEmpty(publicKey)) SetRootField(lines, "key", publicKey);

                File.WriteAllLines(path, lines);
                _logger.LogInformation("RustDesk.toml written at {Path}", path);
                configured = true;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not write RustDesk.toml at {Path}", path); }
        }

        // ── RustDesk2.toml ([options] section, RustDesk 1.2+) ──────────
        foreach (var path in RustDesk2TomlPaths)
        {
            try
            {
                var lines = File.Exists(path)
                    ? File.ReadAllLines(path).ToList()
                    : new List<string>();
                if (!File.Exists(path)) Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                // Root-level rendezvous_server (legacy field still read by some versions)
                SetRootField(lines, "rendezvous_server", host);

                // [options] section: custom-rendezvous-server, relay-server, key
                SetSectionField(lines, "options", "custom-rendezvous-server", host);
                SetSectionField(lines, "options", "relay-server", host);
                if (!string.IsNullOrEmpty(publicKey))
                    SetSectionField(lines, "options", "key", publicKey);

                File.WriteAllLines(path, lines);
                _logger.LogInformation("RustDesk2.toml written at {Path}", path);
                configured = true;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not write RustDesk2.toml at {Path}", path); }
        }

        if (!configured)
            _logger.LogWarning("RustDesk configuration failed — no writable TOML path found.");
    }

    /// <summary>Sets or adds a key=value at the root level (before any [section]).</summary>
    private static void SetRootField(List<string> lines, string key, string value)
    {
        // Find root-level line (not inside a section)
        bool inSection = false;
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[')) { inSection = true; continue; }
            if (inSection) continue;
            if (t.StartsWith(key) && (t.Length == key.Length || t[key.Length] is ' ' or '='))
            {
                lines[i] = $"{key} = '{value}'";
                return;
            }
        }
        // Not found at root — insert before first [section] or at end
        int insertAt = lines.FindIndex(l => l.Trim().StartsWith('['));
        var newLine = $"{key} = '{value}'";
        if (insertAt >= 0) lines.Insert(insertAt, newLine);
        else lines.Add(newLine);
    }

    /// <summary>Sets or adds a key=value inside a named [section].</summary>
    private static void SetSectionField(List<string> lines, string section, string key, string value)
    {
        var sectionHeader = $"[{section}]";
        int sectionIdx = lines.FindIndex(l => l.Trim().Equals(sectionHeader, StringComparison.OrdinalIgnoreCase));

        if (sectionIdx < 0)
        {
            // Section doesn't exist — append it with the key
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add(sectionHeader);
            lines.Add($"{key} = '{value}'");
            return;
        }

        // Find end of this section (next [section] or EOF)
        int sectionEnd = lines.Count;
        for (int i = sectionIdx + 1; i < lines.Count; i++)
        {
            if (lines[i].Trim().StartsWith('[')) { sectionEnd = i; break; }
        }

        // Look for existing key within section
        for (int i = sectionIdx + 1; i < sectionEnd; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith(key) && (t.Length == key.Length || t[key.Length] is ' ' or '='))
            {
                lines[i] = $"{key} = '{value}'";
                return;
            }
        }

        // Key not found in section — insert after section header
        lines.Insert(sectionIdx + 1, $"{key} = '{value}'");
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
    /// <summary>Stores "relay|key" that was last written to RustDesk.toml, so we reconfigure when settings change.</summary>
    public string RustDeskConfiguredFor { get; set; } = "";
    // ApiKey is NOT stored here — it lives in the DPAPI-encrypted SecureStore
}
