using HackITSentry.Server.Models;

namespace HackITSentry.Server.Services;

/// <summary>
/// Singleton that holds runtime-configurable settings.
/// Initialized from environment/appsettings, overridable via the Settings API (stored in DB).
/// </summary>
public class RuntimeSettings
{
    // Agent check-in interval
    public int CheckinIntervalMinutes { get; set; } = 30;

    // RustDesk
    public string RustDeskRelayHost { get; set; } = "";
    public string RustDeskPublicKey { get; set; } = "";
    public bool RustDeskAutoInstall { get; set; } = false;
    public string RustDeskDownloadUrl { get; set; } = "";
    public Dictionary<string, string> RustDeskGlobalOptions { get; set; } = new();
    public int RustDeskForceApplyVersion { get; set; } = 0;

    // Email / SMTP
    public string EmailHost { get; set; } = "";
    public int EmailPort { get; set; } = 587;
    public string EmailUsername { get; set; } = "";
    public string EmailPassword { get; set; } = "";
    public string EmailFrom { get; set; } = "sentry@localhost";
    public string EmailTo { get; set; } = "";
    public bool EmailUseSsl { get; set; } = false;

    // Public URL of the agent/outpost server (used for installer links)
    public string AgentServerUrl { get; set; } = "";

    // Agent auto-update
    public bool AutoUpdateAgents { get; set; } = false;

    // Notification defaults
    public bool NotifyDeviceOffline { get; set; } = true;
    public bool NotifyDeviceOnline { get; set; } = false;
    public bool NotifyNewPending { get; set; } = true;
    public bool NotifySoftwareAlert { get; set; } = true;
    public bool NotifyDiskFull { get; set; } = true;
    public int OfflineAlertDelayMinutes { get; set; } = 0;
    public int AvSignatureAgeAlertDays { get; set; } = 7;

    // LDAP / Active Directory
    public bool LdapEnabled { get; set; } = false;
    public string LdapHost { get; set; } = "";
    public int LdapPort { get; set; } = 389;
    // "TCP" | "STARTTLS" | "LDAPS"  (replaces the old LdapUseSsl bool)
    public string LdapTransport { get; set; } = "TCP";
    public string LdapBaseDn { get; set; } = "";
    public string LdapBindDn { get; set; } = "";
    public string LdapBindPassword { get; set; } = "";
    public string LdapUserSearchBase { get; set; } = "";
    public string LdapUserFilter { get; set; } = "(&(objectClass=user)(|(sAMAccountName={0})(userPrincipalName={0})))";
    public string LdapAdminGroup { get; set; } = "";
    public string LdapViewerGroup { get; set; } = "";
    public bool LdapRequireGroup { get; set; } = false;
    public bool LdapIgnoreCertificateErrors { get; set; } = false;
    public bool LdapUseNestedGroups { get; set; } = false;

    public bool IsEmailConfigured =>
        !string.IsNullOrWhiteSpace(EmailHost) && !string.IsNullOrWhiteSpace(EmailTo);

    /// <summary>Load defaults from IConfiguration (env vars / appsettings).</summary>
    public void LoadFromConfig(IConfiguration config)
    {
        CheckinIntervalMinutes = config.GetValue<int>("CheckinIntervalMinutes", 30);
        AgentServerUrl = config["OutpostPublicUrl"] ?? "";
        // Seed RustDeskRelayHost from env var (legacy RUSTDESK_PUBLIC_HOST) if not in DB yet
        RustDeskRelayHost = config["RustDeskPublicHost"] ?? "";
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
        if (d.TryGetValue("CheckinIntervalMinutes", out var intervalStr) && int.TryParse(intervalStr, out var interval) && interval > 0) CheckinIntervalMinutes = interval;
        if (d.TryGetValue("RustDesk:RelayHost", out var rdRelay) && !string.IsNullOrEmpty(rdRelay)) RustDeskRelayHost = rdRelay;
        if (d.TryGetValue("RustDesk:PublicKey", out var rdKey)) RustDeskPublicKey = rdKey;
        if (d.TryGetValue("RustDesk:AutoInstall", out var rdAuto) && bool.TryParse(rdAuto, out var autoInstall)) RustDeskAutoInstall = autoInstall;
        if (d.TryGetValue("RustDesk:DownloadUrl", out var rdUrl) && !string.IsNullOrEmpty(rdUrl)) RustDeskDownloadUrl = rdUrl;
        if (d.TryGetValue("RustDesk:GlobalOptions", out var rdOpts) && !string.IsNullOrEmpty(rdOpts))
        {
            try { RustDeskGlobalOptions = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(rdOpts) ?? new(); }
            catch { }
        }
        if (d.TryGetValue("RustDesk:ForceApplyVersion", out var rdFav) && int.TryParse(rdFav, out var fav)) RustDeskForceApplyVersion = fav;
        if (d.TryGetValue("Email:Host", out var host) && !string.IsNullOrEmpty(host)) EmailHost = host;
        if (d.TryGetValue("Email:Port", out var portStr) && int.TryParse(portStr, out var port)) EmailPort = port;
        if (d.TryGetValue("Email:Username", out var user)) EmailUsername = user;
        if (d.TryGetValue("Email:Password", out var pw)) EmailPassword = pw;
        if (d.TryGetValue("Email:From", out var from) && !string.IsNullOrEmpty(from)) EmailFrom = from;
        if (d.TryGetValue("Email:To", out var to)) EmailTo = to;
        if (d.TryGetValue("Email:UseSsl", out var ssl) && bool.TryParse(ssl, out var useSsl)) EmailUseSsl = useSsl;
        if (d.TryGetValue("AgentServerUrl", out var serverUrl) && !string.IsNullOrEmpty(serverUrl)) AgentServerUrl = serverUrl;
        if (d.TryGetValue("Agent:AutoUpdate", out var autoUpd) && bool.TryParse(autoUpd, out var autoUpdate)) AutoUpdateAgents = autoUpdate;
        if (d.TryGetValue("Notify:DeviceOffline", out var nOff) && bool.TryParse(nOff, out var notifyOff)) NotifyDeviceOffline = notifyOff;
        if (d.TryGetValue("Notify:DeviceOnline", out var nOn) && bool.TryParse(nOn, out var notifyOn)) NotifyDeviceOnline = notifyOn;
        if (d.TryGetValue("Notify:NewPending", out var nPend) && bool.TryParse(nPend, out var notifyPend)) NotifyNewPending = notifyPend;
        if (d.TryGetValue("Notify:SoftwareAlert", out var nSw) && bool.TryParse(nSw, out var notifySw)) NotifySoftwareAlert = notifySw;
        if (d.TryGetValue("Notify:DiskFull", out var nDisk) && bool.TryParse(nDisk, out var notifyDisk)) NotifyDiskFull = notifyDisk;
        if (d.TryGetValue("Notify:OfflineAlertDelayMinutes", out var delay) && int.TryParse(delay, out var delayMin) && delayMin >= 0) OfflineAlertDelayMinutes = delayMin;
        if (d.TryGetValue("Notify:AvSignatureAgeAlertDays", out var avAge) && int.TryParse(avAge, out var avDays) && avDays >= 0) AvSignatureAgeAlertDays = avDays;
        if (d.TryGetValue("Ldap:Enabled", out var ldapEnabled) && bool.TryParse(ldapEnabled, out var ldapEn)) LdapEnabled = ldapEn;
        if (d.TryGetValue("Ldap:Host", out var ldapHost) && !string.IsNullOrEmpty(ldapHost)) LdapHost = ldapHost;
        if (d.TryGetValue("Ldap:Port", out var ldapPort) && int.TryParse(ldapPort, out var ldapP)) LdapPort = ldapP;
        if (d.TryGetValue("Ldap:Transport", out var ldapTransport) && !string.IsNullOrEmpty(ldapTransport))
            LdapTransport = ldapTransport;
        else if (d.TryGetValue("Ldap:UseSsl", out var ldapSsl) && bool.TryParse(ldapSsl, out var ldapS) && ldapS)
            LdapTransport = "LDAPS"; // backward-compat: old UseSsl=true → LDAPS
        if (d.TryGetValue("Ldap:BaseDn", out var ldapBase)) LdapBaseDn = ldapBase;
        if (d.TryGetValue("Ldap:BindDn", out var ldapBind)) LdapBindDn = ldapBind;
        if (d.TryGetValue("Ldap:BindPassword", out var ldapBindPw)) LdapBindPassword = ldapBindPw;
        if (d.TryGetValue("Ldap:UserSearchBase", out var ldapUsb)) LdapUserSearchBase = ldapUsb;
        if (d.TryGetValue("Ldap:UserFilter", out var ldapUf) && !string.IsNullOrEmpty(ldapUf)) LdapUserFilter = ldapUf;
        if (d.TryGetValue("Ldap:AdminGroup", out var ldapAg)) LdapAdminGroup = ldapAg;
        if (d.TryGetValue("Ldap:ViewerGroup", out var ldapVg)) LdapViewerGroup = ldapVg;
        if (d.TryGetValue("Ldap:RequireGroup", out var ldapRg) && bool.TryParse(ldapRg, out var ldapR)) LdapRequireGroup = ldapR;
        if (d.TryGetValue("Ldap:IgnoreCertificateErrors", out var ldapIgnoreCert) && bool.TryParse(ldapIgnoreCert, out var ldapIc)) LdapIgnoreCertificateErrors = ldapIc;
        if (d.TryGetValue("Ldap:UseNestedGroups", out var ldapNested) && bool.TryParse(ldapNested, out var ldapNg)) LdapUseNestedGroups = ldapNg;
    }

    public Dictionary<string, string> RustDeskToDbEntries() => new()
    {
        ["RustDesk:RelayHost"] = RustDeskRelayHost,
        ["RustDesk:PublicKey"] = RustDeskPublicKey,
        ["RustDesk:AutoInstall"] = RustDeskAutoInstall.ToString(),
        ["RustDesk:DownloadUrl"] = RustDeskDownloadUrl,
        ["RustDesk:GlobalOptions"] = System.Text.Json.JsonSerializer.Serialize(RustDeskGlobalOptions),
        ["RustDesk:ForceApplyVersion"] = RustDeskForceApplyVersion.ToString(),
    };

    public Dictionary<string, string> ToDbEntries() => new()
    {
        ["CheckinIntervalMinutes"] = CheckinIntervalMinutes.ToString(),
        ["Email:Host"] = EmailHost,
        ["Email:Port"] = EmailPort.ToString(),
        ["Email:Username"] = EmailUsername,
        ["Email:Password"] = EmailPassword,
        ["Email:From"] = EmailFrom,
        ["Email:To"] = EmailTo,
        ["Email:UseSsl"] = EmailUseSsl.ToString(),
    };
}
