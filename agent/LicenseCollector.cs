using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace HackITSentry.Agent;

[SupportedOSPlatform("windows")]
public class LicenseCollector
{
    private readonly ILogger<LicenseCollector> _logger;

    public LicenseCollector(ILogger<LicenseCollector> logger)
    {
        _logger = logger;
    }

    public LicenseData Collect()
    {
        return new LicenseData
        {
            WindowsKey = GetWindowsProductKey(),
            LicenseType = GetLicenseType(),
            OfficeKey = GetOfficeProductKey(),
            OfficeVersion = GetOfficeVersion()
        };
    }

    private string GetWindowsProductKey()
    {
        // Try OA3 key first (embedded in BIOS/UEFI for OEM)
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT OA3xOriginalProductKey FROM SoftwareLicensingService");
            foreach (ManagementObject obj in searcher.Get())
            {
                var key = obj["OA3xOriginalProductKey"]?.ToString();
                if (!string.IsNullOrEmpty(key)) return key;
            }
        }
        catch { }

        // Fallback: decode from registry binary key
        try
        {
            return DecodeProductKeyFromRegistry();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retrieve Windows product key");
            return "";
        }
    }

    private static string DecodeProductKeyFromRegistry()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var digitalProductId = key?.GetValue("DigitalProductId") as byte[];
        if (digitalProductId == null) return "";

        // Decode 25-char product key from DigitalProductId
        const string chars = "BCDFGHJKMPQRTVWXY2346789";
        var keyOffset = 52;
        var isWin8OrLater = (digitalProductId[66] >> 3) & 1;
        digitalProductId[66] = (byte)((digitalProductId[66] & 0xf7) | ((isWin8OrLater & 2) << 2));

        var result = new char[29];
        for (int i = 24; i >= 0; i--)
        {
            int cur = 0;
            for (int j = 14; j >= 0; j--)
            {
                cur = cur * 256 ^ digitalProductId[j + keyOffset];
                digitalProductId[j + keyOffset] = (byte)(cur / 24);
                cur %= 24;
            }
            result[i] = chars[cur];
        }

        // Insert dashes
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 25; i++)
        {
            if (i > 0 && i % 5 == 0) sb.Append('-');
            sb.Append(result[i]);
        }

        return sb.ToString();
    }

    private static string GetLicenseType()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Description FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND ApplicationId = '55c92734-d682-4d71-983e-d6ec3f16059f'");
            foreach (ManagementObject obj in searcher.Get())
            {
                var desc = obj["Description"]?.ToString() ?? "";
                if (desc.Contains("OEM", StringComparison.OrdinalIgnoreCase)) return "OEM";
                if (desc.Contains("Retail", StringComparison.OrdinalIgnoreCase)) return "Retail";
                if (desc.Contains("Volume", StringComparison.OrdinalIgnoreCase)) return "Volume";
            }
        }
        catch { }
        return "Unknown";
    }

    private string GetOfficeProductKey()
    {
        // Office keys are stored in registry under Software\Microsoft\Office\*\Registration
        var officePaths = new[]
        {
            @"SOFTWARE\Microsoft\Office\16.0\Registration",
            @"SOFTWARE\Microsoft\Office\15.0\Registration",
            @"SOFTWARE\WOW6432Node\Microsoft\Office\16.0\Registration",
            @"SOFTWARE\WOW6432Node\Microsoft\Office\15.0\Registration"
        };

        foreach (var path in officePaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key == null) continue;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    var digitalProductId = subKey?.GetValue("DigitalProductId") as byte[];
                    if (digitalProductId == null) continue;

                    // Use same decode algorithm
                    return DecodeProductKeyFromRegistryBytes(digitalProductId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read Office key from {Path}", path);
            }
        }

        return "";
    }

    private static string DecodeProductKeyFromRegistryBytes(byte[] digitalProductId)
    {
        const string chars = "BCDFGHJKMPQRTVWXY2346789";
        var keyOffset = 52;
        var result = new char[29];
        for (int i = 24; i >= 0; i--)
        {
            int cur = 0;
            for (int j = 14; j >= 0; j--)
            {
                cur = cur * 256 ^ digitalProductId[j + keyOffset];
                digitalProductId[j + keyOffset] = (byte)(cur / 24);
                cur %= 24;
            }
            result[i] = chars[cur];
        }
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 25; i++)
        {
            if (i > 0 && i % 5 == 0) sb.Append('-');
            sb.Append(result[i]);
        }
        return sb.ToString();
    }

    private static string GetOfficeVersion()
    {
        var versions = new[]
        {
            (@"SOFTWARE\Microsoft\Office\16.0\Common\InstallRoot", "Office 2016/2019/2021"),
            (@"SOFTWARE\Microsoft\Office\15.0\Common\InstallRoot", "Office 2013"),
            (@"SOFTWARE\WOW6432Node\Microsoft\Office\16.0\Common\InstallRoot", "Office 2016/2019/2021 (32-bit)"),
        };

        foreach (var (path, version) in versions)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key?.GetValue("Path") != null)
                    return version;
            }
            catch { }
        }

        return "";
    }
}

public record LicenseData
{
    public string WindowsKey { get; init; } = "";
    public string LicenseType { get; init; } = "";
    public string OfficeKey { get; init; } = "";
    public string OfficeVersion { get; init; } = "";
}
