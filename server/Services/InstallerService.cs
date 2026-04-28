using System.Text;

namespace HackITSentry.Server.Services;

/// <summary>
/// Singleton that locates the installer binary and caches placeholder offsets
/// (scanned once at startup, not on every request).
/// </summary>
public sealed class InstallerService
{
    public string  ExePath    { get; }
    public bool    IsAvailable { get; private set; }
    public long    FileSize    { get; private set; }

    public string  MsiPath       { get; }
    public bool    IsMsiAvailable { get; private set; }
    public long    MsiFileSize    { get; private set; }

    public long ServerUrlValueOffset  { get; private set; }
    public int  ServerUrlSlotBytes    { get; private set; }
    public long InstallTokenValueOffset { get; private set; }
    public int  InstallTokenSlotBytes   { get; private set; }

    private readonly ILogger<InstallerService> _logger;

    public InstallerService(ILogger<InstallerService> logger)
    {
        _logger = logger;
        ExePath = Path.Combine(AppContext.BaseDirectory, "installer", "HackITSentry-Setup.exe");
        MsiPath = Path.Combine(AppContext.BaseDirectory, "installer", "HackITSentry-Setup.msi");

        if (File.Exists(MsiPath))
        {
            MsiFileSize = new FileInfo(MsiPath).Length;
            IsMsiAvailable = true;
            _logger.LogInformation("MSI installer ready at {Path}", MsiPath);
        }

        if (!File.Exists(ExePath))
        {
            _logger.LogWarning("Installer binary not found at {Path}", ExePath);
            return;
        }

        FileSize = new FileInfo(ExePath).Length;

        try
        {
            ScanOffsets();
            IsAvailable = true;
            _logger.LogInformation(
                "Installer ready – ServerUrl@{A} InstallToken@{B}",
                ServerUrlValueOffset, InstallTokenValueOffset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan installer placeholders");
        }
    }

    /// <summary>Creates a read-only stream that streams the installer with patches applied on-the-fly.</summary>
    public Stream CreatePatchedStream(string serverUrl, string installToken)
    {
        var patches = new[]
        {
            new Patch(ServerUrlValueOffset,    Encoding.Unicode.GetBytes(serverUrl),    ServerUrlSlotBytes),
            new Patch(InstallTokenValueOffset, Encoding.Unicode.GetBytes(installToken), InstallTokenSlotBytes),
        };
        return new PatchedFileStream(ExePath, patches);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void ScanOffsets()
    {
        // Both placeholders are within the first ~10 MB; scan 15 MB to be safe.
        int scanBytes = (int)Math.Min(15L * 1024 * 1024, FileSize);
        var buf = new byte[scanBytes];

        using var f = File.OpenRead(ExePath);
        int total = 0;
        while (total < scanBytes)
        {
            int n = f.Read(buf, total, scanBytes - total);
            if (n == 0) break;
            total += n;
        }

        var serverPrefix = Encoding.Unicode.GetBytes("HACKIT_SERVER_URL:");
        var tokenPrefix  = Encoding.Unicode.GetBytes("HACKIT_INSTALL_TOK:");

        long serverIdx = FindPattern(buf, total, serverPrefix);
        long tokenIdx  = FindPattern(buf, total, tokenPrefix);

        if (serverIdx < 0) throw new InvalidOperationException("HACKIT_SERVER_URL: placeholder not found in binary");
        if (tokenIdx  < 0) throw new InvalidOperationException("HACKIT_INSTALL_TOK: placeholder not found in binary");

        // Value area starts right after the prefix
        ServerUrlValueOffset    = serverIdx + serverPrefix.Length;
        ServerUrlSlotBytes      = 415 * 2;   // 433 total chars – 18 prefix chars, * 2 bytes/char

        InstallTokenValueOffset = tokenIdx  + tokenPrefix.Length;
        InstallTokenSlotBytes   = 109 * 2;   // 128 total chars – 19 prefix chars, * 2 bytes/char
    }

    private static long FindPattern(byte[] data, int length, byte[] pattern)
    {
        for (int i = 0; i <= length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
                if (data[i + j] != pattern[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    public sealed record Patch(long Offset, byte[] Value, int SlotBytes);
}
