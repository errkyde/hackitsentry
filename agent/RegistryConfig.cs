using Microsoft.Win32;
using System.Runtime.Versioning;

namespace HITSight.Agent;

[SupportedOSPlatform("windows")]
public static class RegistryConfig
{
    private const string Key = @"SOFTWARE\HITSight";

    // Legacy keys from previous branding — read for migration only.
    private static readonly string[] LegacyKeys =
    [
        @"SOFTWARE\HITGuard",
        @"SOFTWARE\HackITSentry",
    ];

    public static string? GetServerUrl() => GetValue("ServerUrl");
    public static string? GetDeployKey() => GetValue("DeployKey");

    private static string? GetValue(string valueName)
    {
        // Try current key first.
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(Key);
            if (k?.GetValue(valueName) is string v && v.Length > 0)
                return v;
        }
        catch { }

        // Migrate from legacy key if present.
        foreach (var legacy in LegacyKeys)
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(legacy);
                if (k?.GetValue(valueName) is string v && v.Length > 0)
                    return v;
            }
            catch { }
        }
        return null;
    }
}
