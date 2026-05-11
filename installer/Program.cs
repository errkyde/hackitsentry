using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

// ── Placeholders – patched at download time by the server ─────────────────
// Total length must stay constant (server patches by replacing bytes in-place)
const string RawServerUrl   = "HACKIT_SERVER_URL:===============================================================================================================================================================================================================================================================================================================================================================================================================================";  // 18 prefix + 430 padding = 448 chars
const string RawInstallTok  = "HACKIT_INSTALL_TOK:=============================================================================================================";  // 19 prefix + 109 padding = 128 chars

static string ReadPlaceholder(string raw, string prefix)
{
    if (!raw.StartsWith(prefix)) throw new Exception($"Ungültiger Placeholder: {prefix}");
    return raw.Substring(prefix.Length).Split('\0')[0].TrimEnd('=');
}

// ─────────────────────────────────────────────────────────────────────────

Console.OutputEncoding = Encoding.UTF8;
Console.Title = "HackIT Sentry - Agent Setup";

const string ServiceName    = "HackITSentryAgent";
const string ServiceDisplay = "HackIT Sentry Agent";
const string InstallDir     = @"C:\Program Files\HackITSentry\Agent";

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
    serverUrl    = ReadPlaceholder(RawServerUrl,  "HACKIT_SERVER_URL:");
    installToken = ReadPlaceholder(RawInstallTok, "HACKIT_INSTALL_TOK:");

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
Console.WriteLine("  HackIT Sentry - Agent Setup");
Console.WriteLine("  ============================");
Console.WriteLine($"  Server: {serverUrl}");
Console.WriteLine();

try
{
    if (ServiceExists(ServiceName))
    {
        Console.Write("  Bestehenden Dienst entfernen... ");
        Run("sc", $"stop {ServiceName}");
        WaitForServiceStopped(ServiceName, timeoutSeconds: 15);
        Run("sc", $"delete {ServiceName}");
        Thread.Sleep(500);
        Console.WriteLine("OK");
    }

    // Clear old agent state so it registers fresh (removes stale tokens / API keys)
    var stateDir = @"C:\ProgramData\HackITSentry";
    Directory.CreateDirectory(stateDir);
    foreach (var f in new[] { "agent-state.json", "agent.key", "server.url" })
    {
        var p = Path.Combine(stateDir, f);
        if (File.Exists(p)) { File.Delete(p); }
    }

    Directory.CreateDirectory(InstallDir);

    Console.Write("  Agent entpacken...            ");
    var agentExe = Path.Combine(InstallDir, "HackITSentry.Agent.exe");
    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("agent.exe")
        ?? throw new Exception("Eingebettetes Agent-Binary nicht gefunden."))
    using (var file = File.Create(agentExe))
        stream.CopyTo(file);
    Console.WriteLine("OK");

    Console.Write("  Konfiguration schreiben...    ");
    var config = new
    {
        SentryAgent = new
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
    Run("sc", $"description {ServiceName} \"HackIT Sentry Device Management Agent\"");
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
    var bat = Path.Combine(Path.GetTempPath(), "hackit_cleanup.bat");
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
