using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

// ── Placeholders – patched at download time by the server ─────────────────
// Total length must stay constant (server patches by replacing bytes in-place)
const string RawServerUrl   = "HITSIGHT_SERVER_URL:===============================================================================================================================================================================================================================================================================================================================================================================================================================";  // 18 prefix + 430 padding = 448 chars
const string RawInstallTok  = "HITSIGHT_INSTALL_TOK:=============================================================================================================";  // 19 prefix + 109 padding = 128 chars

static string ReadPlaceholder(string raw, string prefix)
{
    if (!raw.StartsWith(prefix)) throw new Exception($"Ungültiger Placeholder: {prefix}");
    return raw.Substring(prefix.Length).Split('\0')[0].TrimEnd('=');
}

// ─────────────────────────────────────────────────────────────────────────

Console.OutputEncoding = Encoding.UTF8;
Console.Title = "HITSight - Agent Setup";

const string ServiceName    = "HITSightAgent";
const string ServiceDisplay = "HITSight Agent";
const string InstallDir     = @"C:\Program Files\HITSight\Agent";

if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
{
    Console.WriteLine("Bitte als Administrator ausfuehren.");
    Console.ReadKey(true);
    return;
}

string serverUrl;
string installToken;

try
{
    serverUrl    = ReadPlaceholder(RawServerUrl,  "HITSIGHT_SERVER_URL:");
    installToken = ReadPlaceholder(RawInstallTok, "HITSIGHT_INSTALL_TOK:");

    if (serverUrl.Length == 0 || serverUrl.StartsWith("="))
        throw new Exception("Installer wurde nicht korrekt konfiguriert (Server-URL fehlt).");
}
catch (Exception ex)
{
    Console.WriteLine($"  FEHLER: {ex.Message}");
    Console.ReadKey(true);
    return;
}

Console.WriteLine();
Console.WriteLine("  HITSight - Agent Setup");
Console.WriteLine("  ============================");
Console.WriteLine($"  Server: {serverUrl}");
Console.WriteLine();

try
{
    // Remove current and all legacy service names so no duplicate services remain.
    foreach (var svcName in new[] { ServiceName, "HITGuardAgent", "HackITSentryAgent", "SentryAgent" })
    {
        if (!ServiceExists(svcName)) continue;
        Console.Write($"  Dienst '{svcName}' entfernen...        ");
        Run("sc", $"stop {svcName}");
        WaitForServiceStopped(svcName, timeoutSeconds: 15);
        Run("sc", $"delete {svcName}");
        Thread.Sleep(500);
        Console.WriteLine("OK");
    }

    // Clear agent state (current + legacy directories) so it registers fresh.
    foreach (var dir in new[] { @"C:\ProgramData\HITSight", @"C:\ProgramData\HITGuard", @"C:\ProgramData\HackITSentry" })
    {
        if (!Directory.Exists(dir)) continue;
        foreach (var f in new[] { "agent-state.json", "agent.key", "server.url" })
        {
            var p = Path.Combine(dir, f);
            if (File.Exists(p)) File.Delete(p);
        }
    }
    var stateDir = @"C:\ProgramData\HITSight";
    Directory.CreateDirectory(stateDir);

    Directory.CreateDirectory(InstallDir);

    Console.Write("  Agent entpacken...            ");
    var agentExe = Path.Combine(InstallDir, "HITSight.Agent.exe");
    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("agent.exe")
        ?? throw new Exception("Eingebettetes Agent-Binary nicht gefunden."))
    using (var file = File.Create(agentExe))
        stream.CopyTo(file);
    Console.WriteLine("OK");

    Console.Write("  Konfiguration schreiben...    ");
    var config = new
    {
        HITSightAgent = new
        {
            ServerUrl = serverUrl,
            InstallToken = installToken,
            CheckinIntervalMinutes = 15
        },
        Logging = new
        {
            LogLevel = new { Default = "Information", Microsoft = "Warning" }
        }
    };
    File.WriteAllText(
        Path.Combine(InstallDir, "appsettings.json"),
        JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("OK");

    Console.Write("  Dienst registrieren...        ");
    Run("sc", $"create {ServiceName} binPath= \"{agentExe}\" start= auto DisplayName= \"{ServiceDisplay}\"");
    Run("sc", $"description {ServiceName} \"HITSight Device Management Agent\"");
    Console.WriteLine("OK");

    Console.Write("  Dienst starten...             ");
    Run("sc", $"start {ServiceName}");
    Console.WriteLine("OK");

    Console.WriteLine();
    Console.WriteLine("  Installation abgeschlossen!");
    Console.WriteLine("  Das Geraet erscheint in Kuerze unter 'Pending' im Dashboard.");
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine($"  FEHLER: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("  Dieses Fenster schliesst sich automatisch in 5 Sekunden...");
Thread.Sleep(5000);

SelfDelete();

// ── Helpers ───────────────────────────────────────────────────────────────

static void WaitForServiceStopped(string name, int timeoutSeconds)
{
    var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
    while (DateTime.UtcNow < deadline)
    {
        using var p = Process.Start(new ProcessStartInfo("sc", $"query {name}")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
        })!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (output.Contains("STOPPED") || p.ExitCode != 0)
            return;
        Thread.Sleep(500);
    }
}

static bool ServiceExists(string name)
{
    using var p = Process.Start(new ProcessStartInfo("sc", $"query {name}")
    {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
    })!;
    p.WaitForExit();
    return p.ExitCode == 0;
}

static void Run(string exe, string args)
{
    using var p = Process.Start(new ProcessStartInfo(exe, args)
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true
    })!;
    p.WaitForExit();
}

static void SelfDelete()
{
    var exe = Environment.ProcessPath!;
    var bat = Path.Combine(Path.GetTempPath(), "hitsight_cleanup.bat");
    File.WriteAllText(bat,
        "@echo off\r\n" +
        "ping -n 3 127.0.0.1 > nul\r\n" +
        $"del /f /q \"{exe}\"\r\n" +
        "del /f /q \"%~f0\"\r\n");
    Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
    {
        WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true, UseShellExecute = true
    });
    Environment.Exit(0);
}
