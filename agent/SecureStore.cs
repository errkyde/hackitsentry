using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace HITSight.Agent;

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
        "HITSight");

    // Legacy store directories from previous branding — used only for one-time migration.
    private static readonly string[] LegacyDirs =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HITGuard"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HackITSentry"),
    ];

    private static string KeyFile => Path.Combine(StoreDir, "agent.key");
    private static string UrlFile => Path.Combine(StoreDir, "server.url");

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
        if (File.Exists(KeyFile))
            return TryDecrypt(KeyFile);

        // Migrate from legacy directory on first run after rename.
        foreach (var dir in LegacyDirs)
        {
            var legacy = Path.Combine(dir, "agent.key");
            if (!File.Exists(legacy)) continue;
            var value = TryDecrypt(legacy);
            if (value != null) { SaveApiKey(value); return value; }
        }
        return null;
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
        if (File.Exists(UrlFile))
            return TryDecrypt(UrlFile);

        // Migrate from legacy directory on first run after rename.
        foreach (var dir in LegacyDirs)
        {
            var legacy = Path.Combine(dir, "server.url");
            if (!File.Exists(legacy)) continue;
            var value = TryDecrypt(legacy);
            if (value != null) { SaveServerUrl(value); return value; }
        }
        return null;
    }

    private static string? TryDecrypt(string path)
    {
        try
        {
            var encrypted = File.ReadAllBytes(path);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return null; }
    }

    public static void Delete()
    {
        try { if (File.Exists(KeyFile)) File.Delete(KeyFile); } catch { }
        try { if (File.Exists(UrlFile)) File.Delete(UrlFile); } catch { }
    }
}
