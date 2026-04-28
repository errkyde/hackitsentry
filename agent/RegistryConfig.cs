using Microsoft.Win32;
using System.Runtime.Versioning;

namespace HackITSentry.Agent;

[SupportedOSPlatform("windows")]
public static class RegistryConfig
{
    private const string Key = @"SOFTWARE\HackIT Sentry";

    public static string? GetServerUrl()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(Key);
            return k?.GetValue("ServerUrl") as string is { Length: > 0 } v ? v : null;
        }
        catch { return null; }
    }

    public static string? GetDeployKey()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(Key);
            return k?.GetValue("DeployKey") as string is { Length: > 0 } v ? v : null;
        }
        catch { return null; }
    }
}
