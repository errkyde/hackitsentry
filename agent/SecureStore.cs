using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace HackITSentry.Agent;

/// <summary>
/// Stores sensitive agent data (ApiKey, ServerUrl) encrypted on disk using Windows DPAPI.
/// Encryption is scoped to the local machine — the data cannot be decrypted on any other machine.
/// The plaintext never touches appsettings.json.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SecureStore
{
    private static readonly string StoreDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "HackITSentry");

    private static string KeyFile    => Path.Combine(StoreDir, "agent.key");
    private static string UrlFile    => Path.Combine(StoreDir, "server.url");

    // ── Public API ────────────────────────────────────────────────────────────

    public static void SaveApiKey(string apiKey)
    {
        Directory.CreateDirectory(StoreDir);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(apiKey),
            optionalEntropy: null,
            scope: DataProtectionScope.LocalMachine);
        File.WriteAllBytes(KeyFile, encrypted);
    }

    public static string? LoadApiKey()
    {
        if (!File.Exists(KeyFile)) return null;
        try
        {
            var encrypted = File.ReadAllBytes(KeyFile);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveServerUrl(string url)
    {
        Directory.CreateDirectory(StoreDir);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(url),
            optionalEntropy: null,
            scope: DataProtectionScope.LocalMachine);
        File.WriteAllBytes(UrlFile, encrypted);
    }

    public static string? LoadServerUrl()
    {
        if (!File.Exists(UrlFile)) return null;
        try
        {
            var encrypted = File.ReadAllBytes(UrlFile);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    public static void Delete()
    {
        try { if (File.Exists(KeyFile)) File.Delete(KeyFile); } catch { }
        try { if (File.Exists(UrlFile)) File.Delete(UrlFile); } catch { }
    }
}
