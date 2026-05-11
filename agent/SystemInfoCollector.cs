using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace HackITSentry.Agent;

[SupportedOSPlatform("windows")]
public class SystemInfoCollector
{
    public SystemInfo Collect()
    {
        var (pendingUpdatesCount, lastWindowsUpdateInstalled) = GetWindowsUpdateInfo();
        return new SystemInfo
        {
            Hostname = Environment.MachineName,
            WindowsVersion = GetWindowsVersion(),
            WindowsBuild = GetWindowsBuild(),
            WindowsEdition = GetWindowsEdition(),
            LicenseType = GetLicenseType(),
            CpuModel = GetCpuModel(),
            CpuCores = GetCpuCores(),
            RamTotalGB = GetRamTotalGB(),
            RamUsedGB = GetRamUsedGB(),
            NetworkAdapters = GetNetworkAdapters(),
            DiskDrives = GetDiskDrives(),
            InstalledSoftware = GetInstalledSoftware(),
            RustDeskId = GetRustDeskId(),
            BiosInfoJson = GetBiosInfoJson(),
            DefenderStatusJson = GetDefenderStatusJson(),
            PendingUpdatesCount = pendingUpdatesCount,
            LastWindowsUpdateInstalled = lastWindowsUpdateInstalled
        };
    }

    private static string GetWindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var productName = key?.GetValue("ProductName")?.ToString() ?? "Unknown";
            var displayVersion = key?.GetValue("DisplayVersion")?.ToString() ?? "";

            // ProductName still says "Windows 10" on Windows 11; fix via build number
            if (int.TryParse(key?.GetValue("CurrentBuild")?.ToString(), out var build) && build >= 22000)
                productName = productName.Replace("Windows 10", "Windows 11");

            return $"{productName} {displayVersion}".Trim();
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    private static string GetWindowsBuild()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var currentBuild = key?.GetValue("CurrentBuild")?.ToString() ?? "";
            var ubr = key?.GetValue("UBR")?.ToString() ?? "";
            return string.IsNullOrEmpty(ubr) ? currentBuild : $"{currentBuild}.{ubr}";
        }
        catch { return ""; }
    }

    private static string GetWindowsEdition()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("EditionID")?.ToString() ?? "";
        }
        catch { return ""; }
    }

    private static string GetLicenseType()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT LicenseStatus, Description FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND ApplicationId = '55c92734-d682-4d71-983e-d6ec3f16059f'");
            foreach (ManagementObject obj in searcher.Get())
            {
                var description = obj["Description"]?.ToString() ?? "";
                if (description.Contains("OEM", StringComparison.OrdinalIgnoreCase))
                    return "OEM";
                if (description.Contains("Retail", StringComparison.OrdinalIgnoreCase))
                    return "Retail";
                if (description.Contains("Volume", StringComparison.OrdinalIgnoreCase))
                    return "Volume";
            }
        }
        catch { }
        return "Unknown";
    }

    private static string GetCpuModel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
                return obj["Name"]?.ToString()?.Trim() ?? "Unknown";
        }
        catch { }
        return "Unknown";
    }

    private static int GetCpuCores()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
                return Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
        }
        catch { }
        return Environment.ProcessorCount;
    }

    private static double GetRamTotalGB()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
                return Math.Round(Convert.ToDouble(obj["TotalVisibleMemorySize"]) / 1024 / 1024, 2);
        }
        catch { }
        return 0;
    }

    private static double GetRamUsedGB()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                var total = Convert.ToDouble(obj["TotalVisibleMemorySize"]);
                var free = Convert.ToDouble(obj["FreePhysicalMemory"]);
                return Math.Round((total - free) / 1024 / 1024, 2);
            }
        }
        catch { }
        return 0;
    }

    private static List<NetworkAdapterInfo> GetNetworkAdapters()
    {
        var result = new List<NetworkAdapterInfo>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var props = ni.GetIPProperties();

                var ipv4Info = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                var ipv4 = ipv4Info?.Address.ToString() ?? "";
                var subnetMask = ipv4Info?.IPv4Mask.ToString() ?? "";

                var ipv6 = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6
                             && !a.Address.IsIPv6LinkLocal)
                    .Select(a => a.Address.ToString())
                    .FirstOrDefault() ?? "";

                var gateway = props.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                    ?.Address.ToString() ?? "";

                var dns = props.DnsAddresses
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.ToString())
                    .ToList();

                var mac = string.Join(":", ni.GetPhysicalAddress().GetAddressBytes()
                    .Select(b => b.ToString("X2")));

                var speedMbps = ni.Speed > 0 ? ni.Speed / 1_000_000 : 0;

                var adapterType = ni.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => "WLAN",
                    NetworkInterfaceType.Ethernet => "Ethernet",
                    NetworkInterfaceType.GigabitEthernet => "Gigabit Ethernet",
                    NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => "Fast Ethernet",
                    _ => ni.NetworkInterfaceType.ToString()
                };

                result.Add(new NetworkAdapterInfo(ni.Name, ipv4, mac, ipv6, subnetMask, gateway, dns, speedMbps, adapterType));
            }
        }
        catch { }
        return result;
    }

    private static List<DiskDriveInfo> GetDiskDrives()
    {
        var result = new List<DiskDriveInfo>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                result.Add(new DiskDriveInfo(
                    drive.Name,
                    Math.Round((double)drive.TotalSize / 1024 / 1024 / 1024, 2),
                    Math.Round((double)drive.TotalFreeSpace / 1024 / 1024 / 1024, 2)
                ));
            }
        }
        catch { }
        return result;
    }

    private static List<SoftwareInfo> GetInstalledSoftware()
    {
        var result = new List<SoftwareInfo>();
        var paths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (var path in paths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key == null) continue;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    var name = subKey.GetValue("DisplayName")?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    result.Add(new SoftwareInfo(
                        name,
                        subKey.GetValue("DisplayVersion")?.ToString() ?? "",
                        subKey.GetValue("Publisher")?.ToString() ?? "",
                        subKey.GetValue("InstallDate")?.ToString() ?? ""
                    ));
                }
            }
            catch { }
        }

        return result.DistinctBy(s => s.Name).OrderBy(s => s.Name).ToList();
    }

    private static (int pendingCount, DateTime? lastInstalled) GetWindowsUpdateInfo()
    {
        int pendingCount = 0;
        DateTime? lastInstalled = null;

        try
        {
            // Count pending updates via COM (Microsoft.Update.Session)
            var updateSessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (updateSessionType != null)
            {
                dynamic session = Activator.CreateInstance(updateSessionType)!;
                dynamic searcher = session.CreateUpdateSearcher();
                dynamic result = searcher.Search("IsInstalled=0 AND IsHidden=0 AND Type='Software'");
                pendingCount = result.Updates.Count;
            }
        }
        catch { }

        try
        {
            // Last successful update from Registry
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install");
            var raw = key?.GetValue("LastSuccessTime")?.ToString() ?? "";
            if (!string.IsNullOrEmpty(raw) && DateTime.TryParse(raw, out var dt))
                lastInstalled = dt.ToUniversalTime();
        }
        catch { }

        return (pendingCount, lastInstalled);
    }

    private static string GetDefenderStatusJson()
    {
        try
        {
            // Installed AV products from SecurityCenter2
            var avProducts = new List<object>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var name = obj["displayName"]?.ToString() ?? "";
                    var state = Convert.ToUInt32(obj["productState"] ?? 0);
                    // productState encoding: bits 12-15 = monitoring status, bits 4-7 = definition status
                    var enabled = ((state >> 12) & 0xf) == 1;
                    var upToDate = ((state >> 4) & 0xf) == 0;
                    avProducts.Add(new { name, enabled, upToDate });
                }
            }
            catch { }

            // Defender-specific details via WMI (available on Win8+)
            bool? rtpEnabled = null;
            int? signatureAgeDays = null;
            string? lastScanTime = null;
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Defender",
                    "SELECT AMRunningMode, RealTimeProtectionEnabled, AntivirusSignatureAge, LastQuickScanEndTime FROM MSFT_MpComputerStatus");
                foreach (ManagementObject obj in searcher.Get())
                {
                    rtpEnabled = (bool?)obj["RealTimeProtectionEnabled"];
                    signatureAgeDays = obj["AntivirusSignatureAge"] != null
                        ? Convert.ToInt32(obj["AntivirusSignatureAge"]) : (int?)null;
                    var scanTime = obj["LastQuickScanEndTime"]?.ToString() ?? "";
                    // CIM_DATETIME: "20230401120000.000000+000"
                    if (scanTime.Length >= 14)
                        lastScanTime = $"{scanTime[..4]}-{scanTime[4..6]}-{scanTime[6..8]}T{scanTime[8..10]}:{scanTime[10..12]}:{scanTime[12..14]}Z";
                    break;
                }
            }
            catch { }

            return JsonSerializer.Serialize(new
            {
                avProducts,
                realTimeProtectionEnabled = rtpEnabled,
                signatureAgeDays,
                lastScanTime
            });
        }
        catch { return "{}"; }
    }

    private static string GetBiosInfoJson()
    {
        try
        {
            string biosVersion = "", biosDate = "", biosManufacturer = "";
            using (var searcher = new ManagementObjectSearcher(
                "SELECT SMBIOSBIOSVersion, ReleaseDate, Manufacturer FROM Win32_BIOS"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    biosVersion = obj["SMBIOSBIOSVersion"]?.ToString() ?? "";
                    biosDate = obj["ReleaseDate"]?.ToString() ?? "";
                    // ReleaseDate is CIM_DATETIME: "20230401000000.000000+000" → "2023-04-01"
                    if (biosDate.Length >= 8)
                        biosDate = $"{biosDate[..4]}-{biosDate[4..6]}-{biosDate[6..8]}";
                    biosManufacturer = obj["Manufacturer"]?.ToString() ?? "";
                    break;
                }
            }

            string boardManufacturer = "", boardProduct = "", boardSerial = "";
            using (var searcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    boardManufacturer = obj["Manufacturer"]?.ToString() ?? "";
                    boardProduct = obj["Product"]?.ToString() ?? "";
                    boardSerial = obj["SerialNumber"]?.ToString() ?? "";
                    break;
                }
            }

            return JsonSerializer.Serialize(new
            {
                biosVersion,
                biosDate,
                biosManufacturer,
                boardManufacturer,
                boardProduct,
                boardSerial
            });
        }
        catch { return "{}"; }
    }

    public static string GetRustDeskId()
    {
        // Primary: ask RustDesk directly via CLI — works regardless of profile/service mode
        var rustDeskExePaths = new[]
        {
            @"C:\Program Files\RustDesk\RustDesk.exe",
            @"C:\Program Files (x86)\RustDesk\RustDesk.exe",
        };
        foreach (var exe in rustDeskExePaths)
        {
            if (!File.Exists(exe)) continue;
            try
            {
                var psi = new ProcessStartInfo(exe, "--get-id")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) continue;
                // ReadToEnd on a background task with timeout to avoid hanging
                var readTask = Task.Run(() => proc.StandardOutput.ReadToEnd());
                if (readTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    proc.WaitForExit(1000);
                    if (!proc.HasExited) proc.Kill();
                    var id = readTask.Result.Trim();
                    if (!string.IsNullOrWhiteSpace(id)) return id;
                }
                else
                {
                    if (!proc.HasExited) proc.Kill();
                }
            }
            catch { }
        }

        // Fallback: parse TOML config files across all known service/user profile paths
        var baseProfiles = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustDesk", "config"),
            @"C:\Windows\ServiceProfiles\LocalService\AppData\Roaming\RustDesk\config",
            @"C:\Windows\ServiceProfiles\NetworkService\AppData\Roaming\RustDesk\config",
            @"C:\Windows\system32\config\systemprofile\AppData\Roaming\RustDesk\config",
        };

        foreach (var dir in baseProfiles)
        {
            foreach (var fileName in new[] { "RustDesk.toml", "RustDesk2.toml" })
            {
                var path = Path.Combine(dir, fileName);
                try
                {
                    if (!File.Exists(path)) continue;
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var trimmed = line.Trim();
                        // Match "id = ..." but not "enc_id" (encrypted ID, not the display ID)
                        if (!trimmed.StartsWith("id ") && !trimmed.StartsWith("id=")) continue;
                        var parts = trimmed.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            var id = parts[1].Trim().Trim('"', '\'');
                            if (!string.IsNullOrEmpty(id)) return id;
                        }
                    }
                }
                catch { }
            }
        }
        return "";
    }
}

public record SystemInfo
{
    public string Hostname { get; init; } = "";
    public string WindowsVersion { get; init; } = "";
    public string WindowsBuild { get; init; } = "";
    public string WindowsEdition { get; init; } = "";
    public string LicenseType { get; init; } = "";
    public string CpuModel { get; init; } = "";
    public int CpuCores { get; init; }
    public double RamTotalGB { get; init; }
    public double RamUsedGB { get; init; }
    public List<NetworkAdapterInfo> NetworkAdapters { get; init; } = [];
    public List<DiskDriveInfo> DiskDrives { get; init; } = [];
    public List<SoftwareInfo> InstalledSoftware { get; init; } = [];
    public string RustDeskId { get; init; } = "";
    public string BiosInfoJson { get; init; } = "{}";
    public string DefenderStatusJson { get; init; } = "{}";
    public int PendingUpdatesCount { get; init; }
    public DateTime? LastWindowsUpdateInstalled { get; init; }
}

public record NetworkAdapterInfo(
    string Name,
    string IpAddress,
    string MacAddress,
    string Ipv6Address,
    string SubnetMask,
    string Gateway,
    List<string> DnsServers,
    long SpeedMbps,
    string AdapterType);
public record DiskDriveInfo(string Drive, double TotalGB, double FreeGB);
public record SoftwareInfo(string Name, string Version, string Publisher, string InstallDate);
