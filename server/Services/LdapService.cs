using Novell.Directory.Ldap;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Text;

namespace HITSight.Server.Services;

public class LdapUserInfo
{
    public string Dn { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public List<string> MemberOf { get; set; } = new();
}

public class LdapService
{
    private readonly RuntimeSettings _settings;
    private readonly ILogger<LdapService> _logger;

    public LdapService(RuntimeSettings settings, ILogger<LdapService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    // Finds the DN for a given username using the service account.
    public async Task<string?> FindUserDnAsync(string username)
    {
        try
        {
            using var conn = await ConnectAndBindServiceAccountAsync();
            var searchBase = string.IsNullOrWhiteSpace(_settings.LdapUserSearchBase)
                ? _settings.LdapBaseDn
                : _settings.LdapUserSearchBase;
            var filter = string.Format(_settings.LdapUserFilter, EscapeLdap(username));

            var results = await conn.SearchAsync(searchBase, LdapConnection.ScopeSub, filter, new[] { "dn" }, false);
            if (await results.HasMoreAsync())
            {
                var entry = await results.NextAsync();
                return entry.Dn;
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LDAP FindUserDn failed for {Username}", username);
            return null;
        }
    }

    // Validates user credentials by attempting a bind with their DN + password.
    public async Task<bool> TryBindUserAsync(string userDn, string password)
    {
        if (string.IsNullOrWhiteSpace(password)) return false;
        try
        {
            using var conn = await ConnectAsync();
            await conn.BindAsync(userDn, password);
            return conn.Bound;
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LDAP TryBindUser failed for DN {Dn}", userDn);
            return false;
        }
    }

    // Fetches display name, email, and group memberships for a user DN.
    public async Task<LdapUserInfo?> GetUserInfoAsync(string userDn)
    {
        try
        {
            using var conn = await ConnectAndBindServiceAccountAsync();
            var attrs = new[] { "displayName", "cn", "mail", "memberOf" };
            var entry = await conn.ReadAsync(userDn, attrs);
            if (entry == null) return null;

            var displayName = entry.GetStringValueOrDefault("displayName", "")
                .NullIfEmpty() ?? entry.GetStringValueOrDefault("cn", "");
            var email = entry.GetStringValueOrDefault("mail", "");
            var memberOf = GetMultiValue(entry, "memberOf");

            return new LdapUserInfo { Dn = userDn, DisplayName = displayName, Email = email, MemberOf = memberOf };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LDAP GetUserInfo failed for DN {Dn}", userDn);
            return null;
        }
    }

    // Tests connectivity with the service account. Returns null on success, error message on failure.
    public async Task<string?> TestConnectionAsync()
    {
        try
        {
            using var conn = await ConnectAndBindServiceAccountAsync();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // Derives role from group memberships (direct memberOf only).
    public string? DeriveRole(List<string> memberOf)
    {
        if (!string.IsNullOrWhiteSpace(_settings.LdapAdminGroup) && GroupMatches(memberOf, _settings.LdapAdminGroup))
            return "Admin";
        if (!string.IsNullOrWhiteSpace(_settings.LdapViewerGroup) && GroupMatches(memberOf, _settings.LdapViewerGroup))
            return "Viewer";
        if (_settings.LdapRequireGroup) return null;
        return "Viewer";
    }

    // Derives role with optional nested group support via AD LDAP_MATCHING_RULE_IN_CHAIN OID.
    public async Task<string?> DeriveRoleAsync(string userDn, List<string> memberOf)
    {
        if (_settings.LdapUseNestedGroups)
        {
            try
            {
                using var conn = await ConnectAndBindServiceAccountAsync();
                if (!string.IsNullOrWhiteSpace(_settings.LdapAdminGroup) && await IsUserInGroupAsync(conn, userDn, _settings.LdapAdminGroup))
                    return "Admin";
                if (!string.IsNullOrWhiteSpace(_settings.LdapViewerGroup) && await IsUserInGroupAsync(conn, userDn, _settings.LdapViewerGroup))
                    return "Viewer";
                if (_settings.LdapRequireGroup) return null;
                return "Viewer";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nested group check failed, falling back to memberOf");
            }
        }
        return DeriveRole(memberOf);
    }

    // Uses AD's LDAP_MATCHING_RULE_IN_CHAIN OID to check transitive group membership.
    private async Task<bool> IsUserInGroupAsync(LdapConnection conn, string userDn, string group)
    {
        var searchBase = _settings.LdapBaseDn;
        // Support both full DN ("CN=...") and plain CN ("Admins")
        var groupCondition = group.Contains('=')
            ? $"(distinguishedName={EscapeLdap(group)})"
            : $"(cn={EscapeLdap(group)})";
        var filter = $"(&(objectClass=group){groupCondition}(member:1.2.840.113556.1.4.1941:={EscapeLdap(userDn)}))";
        var results = await conn.SearchAsync(searchBase, LdapConnection.ScopeSub, filter, new[] { "dn" }, false);
        return await results.HasMoreAsync();
    }

    private async Task<LdapConnection> ConnectAsync()
    {
        var conn = new LdapConnection();
        var transport = _settings.LdapTransport; // "TCP" | "STARTTLS" | "LDAPS"

        if (transport != "TCP")
        {
            if (!string.IsNullOrEmpty(_settings.LdapCaCertificate))
            {
                // Validate server cert against the uploaded CA certificate
                X509Certificate2 caCert;
                try
                {
                    caCert = new X509Certificate2(Encoding.UTF8.GetBytes(_settings.LdapCaCertificate));
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Das gespeicherte CA-Zertifikat ist ungültig.", ex);
                }

                conn.UserDefinedServerCertValidationDelegate += (_, serverCert, chain, sslPolicyErrors) =>
                {
                    if (sslPolicyErrors == SslPolicyErrors.None) return true;
                    // Only tolerate chain errors (unknown root) — not name mismatches
                    if ((sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0) return false;
                    chain!.ChainPolicy.ExtraStore.Add(caCert);
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    return chain.Build(new X509Certificate2(serverCert!));
                };
            }
            else if (_settings.LdapIgnoreCertificateErrors)
            {
                _logger.LogWarning("LDAP: Zertifikatsfehler werden ignoriert (kein CA-Zertifikat hinterlegt).");
                conn.UserDefinedServerCertValidationDelegate += (_, _, _, _) => true;
            }
        }

        if (transport == "LDAPS")
            conn.SecureSocketLayer = true;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await conn.ConnectAsync(_settings.LdapHost, _settings.LdapPort).WaitAsync(cts.Token);
            if (transport == "STARTTLS")
                await conn.StartTlsAsync().WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            conn.Dispose();
            throw new TimeoutException($"LDAP-Verbindung zu {_settings.LdapHost}:{_settings.LdapPort} Timeout (10 s).");
        }
        return conn;
    }

    private async Task<LdapConnection> ConnectAndBindServiceAccountAsync()
    {
        var conn = await ConnectAsync();
        if (!string.IsNullOrWhiteSpace(_settings.LdapBindDn))
            await conn.BindAsync(_settings.LdapBindDn, _settings.LdapBindPassword);
        // anonymous bind: skip BindAsync — sending ("","") can confuse some servers
        return conn;
    }

    private static bool GroupMatches(List<string> memberOf, string group)
    {
        return memberOf.Any(m =>
            m.Equals(group, StringComparison.OrdinalIgnoreCase) ||
            m.StartsWith($"CN={group},", StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> GetMultiValue(LdapEntry entry, string attr)
    {
        try
        {
            var attrSet = entry.GetAttributeSet();
            if (!attrSet.ContainsKey(attr)) return new();
            var a = attrSet[attr];
            return a.StringValueArray?.ToList() ?? new();
        }
        catch { return new(); }
    }

    // Escapes special LDAP filter characters per RFC 4515.
    private static string EscapeLdap(string input) =>
        input.Replace("\\", "\\5c").Replace("*", "\\2a")
             .Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");
}

file static class StringExtensions
{
    public static string? NullIfEmpty(this string? s) => string.IsNullOrEmpty(s) ? null : s;
}
