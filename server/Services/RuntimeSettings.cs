using HackITSentry.Server.Models;

namespace HackITSentry.Server.Services;

/// <summary>
/// Singleton that holds runtime-configurable settings.
/// Initialized from environment/appsettings, overridable via the Settings API (stored in DB).
/// </summary>
public class RuntimeSettings
{
    // Email / SMTP
    public string EmailHost { get; set; } = "";
    public int EmailPort { get; set; } = 587;
    public string EmailUsername { get; set; } = "";
    public string EmailPassword { get; set; } = "";
    public string EmailFrom { get; set; } = "sentry@localhost";
    public string EmailTo { get; set; } = "";
    public bool EmailUseSsl { get; set; } = false;

    public bool IsEmailConfigured =>
        !string.IsNullOrWhiteSpace(EmailHost) && !string.IsNullOrWhiteSpace(EmailTo);

    /// <summary>Load defaults from IConfiguration (env vars / appsettings).</summary>
    public void LoadFromConfig(IConfiguration config)
    {
        EmailHost = config["Email:Host"] ?? "";
        EmailPort = config.GetValue<int>("Email:Port", 587);
        EmailUsername = config["Email:Username"] ?? "";
        EmailPassword = config["Email:Password"] ?? "";
        EmailFrom = config["Email:From"] ?? "sentry@localhost";
        EmailTo = config["Email:To"] ?? "";
        EmailUseSsl = config.GetValue<bool>("Email:UseSsl", false);
    }

    /// <summary>Override with values from the DB (non-empty values win over config defaults).</summary>
    public void LoadFromDb(IEnumerable<AppSetting> dbSettings)
    {
        var d = dbSettings.ToDictionary(s => s.Key, s => s.Value);
        if (d.TryGetValue("Email:Host", out var host) && !string.IsNullOrEmpty(host)) EmailHost = host;
        if (d.TryGetValue("Email:Port", out var portStr) && int.TryParse(portStr, out var port)) EmailPort = port;
        if (d.TryGetValue("Email:Username", out var user)) EmailUsername = user;
        if (d.TryGetValue("Email:Password", out var pw)) EmailPassword = pw;
        if (d.TryGetValue("Email:From", out var from) && !string.IsNullOrEmpty(from)) EmailFrom = from;
        if (d.TryGetValue("Email:To", out var to)) EmailTo = to;
        if (d.TryGetValue("Email:UseSsl", out var ssl) && bool.TryParse(ssl, out var useSsl)) EmailUseSsl = useSsl;
    }

    public Dictionary<string, string> ToDbEntries() => new()
    {
        ["Email:Host"] = EmailHost,
        ["Email:Port"] = EmailPort.ToString(),
        ["Email:Username"] = EmailUsername,
        ["Email:Password"] = EmailPassword,
        ["Email:From"] = EmailFrom,
        ["Email:To"] = EmailTo,
        ["Email:UseSsl"] = EmailUseSsl.ToString(),
    };
}
