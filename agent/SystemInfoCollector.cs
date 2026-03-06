using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace HackITSentry.Agent;

[SupportedOSPlatform("windows")]
public class SystemInfoCollector
{
    public SystemInfo Collect()
    {
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
            InstalledSoftware = GetInstalledSoftware()
        };
    }

    private static string GetWindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var productName = key?.GetValue("ProductName")?.ToString() ?? "Unknown";
            var displayVersion = key?.GetValue("DisplayVersion")?.ToString() ?? "";
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

                var ipv4 = ni.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    ?.Address.ToString() ?? "";

                var mac = string.Join(":", ni.GetPhysicalAddress().GetAddressBytes()
                    .Select(b => b.ToString("X2")));

                result.Add(new NetworkAdapterInfo(ni.Name, ipv4, mac));
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
}

public record NetworkAdapterInfo(string Name, string IpAddress, string MacAddress);
public record DiskDriveInfo(string Drive, double TotalGB, double FreeGB);
public record SoftwareInfo(string Name, string Version, string Publisher, string InstallDate);
