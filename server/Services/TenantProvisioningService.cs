using HITSight.Server.Data;
using HITSight.Server.Middleware;
using HITSight.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using System.Text;
using System.Text.RegularExpressions;

namespace HITSight.Server.Services;

public class TenantProvisioningService
{
    private readonly PlatformDbContext _platformDb;
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly PlatformEmailService _email;
    private readonly ILogger<TenantProvisioningService> _logger;

    public TenantProvisioningService(
        PlatformDbContext platformDb,
        IConfiguration config,
        IMemoryCache cache,
        PlatformEmailService email,
        ILogger<TenantProvisioningService> logger)
    {
        _platformDb = platformDb;
        _config = config;
        _cache = cache;
        _email = email;
        _logger = logger;
    }

    public record ProvisionResult(
        string Slug,
        string LoginUrl,
        string AdminUsername,
        string AdminPassword,
        string DeployKeyToken,
        string MsiInstallUrl
    );

    public async Task<ProvisionResult> ProvisionAsync(
        string companyName,
        string adminEmail,
        string plan,
        int? maxDevices = null,
        int trialDays = 14,
        string? stripeCustomerId = null,
        string? stripeSubscriptionId = null,
        string? subscriptionStatus = null)
    {
        var slug = await GenerateUniqueSlugAsync(companyName);
        var dbName = $"hitsight_{slug.Replace("-", "_")}";
        var platformDomain = _config["Platform:Domain"] ?? "localhost";
        var platformConnStr = _config["Platform:ConnectionString"]!;

        int deviceLimit = maxDevices ?? plan switch
        {
            "pro" => 100,
            "enterprise" => int.MaxValue,
            _ => 25 // starter
        };

        // Create PostgreSQL database
        await CreateDatabaseAsync(platformConnStr, dbName);

        AppDbContext? tenantDb = null;
        try
        {
            var tenantCs = HITSight.Server.Middleware.TenantResolutionMiddleware.BuildTenantConnectionString(platformConnStr, dbName);
            var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(tenantCs).Options;
            tenantDb = new AppDbContext(opts);

            await DbMigrator.RunAsync(tenantDb);
            await DbMigrator.SeedDefaultPackagesAsync(tenantDb);

            // Create admin user
            var (adminUsername, adminPassword) = await CreateAdminUserAsync(tenantDb, adminEmail);

            // Create deploy key
            var deployKeyToken = await CreateDeployKeyAsync(tenantDb);

            // Save Tenant record in Platform DB
            var tenant = new Tenant
            {
                Slug = slug,
                Name = companyName,
                DbName = dbName,
                Plan = plan,
                MaxDevices = deviceLimit,
                IsActive = true,
                AdminEmail = adminEmail,
                StripeCustomerId = stripeCustomerId,
                StripeSubscriptionId = stripeSubscriptionId,
                SubscriptionStatus = subscriptionStatus ?? (trialDays > 0 ? "trialing" : "active"),
                TrialEndsAt = trialDays > 0 ? DateTime.UtcNow.AddDays(trialDays) : null,
            };
            _platformDb.Tenants.Add(tenant);
            await _platformDb.SaveChangesAsync();

            var loginUrl = $"https://{slug}.{platformDomain}/login";
            var msiInstallUrl = $"https://{slug}.{platformDomain}/install/deploy/download";

            _logger.LogInformation("Tenant {Slug} provisioned successfully (DB: {DbName})", slug, dbName);

            var result = new ProvisionResult(slug, loginUrl, adminUsername, adminPassword, deployKeyToken, msiInstallUrl);

            // Send welcome email (best-effort — provisioning is complete regardless)
            _ = SendWelcomeEmailAsync(result, tenant);

            return result;
        }
        catch
        {
            // Roll back: drop the DB if provisioning failed after creation
            tenantDb?.Dispose();
            try { await DropDatabaseAsync(platformConnStr, dbName); } catch { }
            throw;
        }
        finally
        {
            tenantDb?.Dispose();
        }
    }

    public async Task DropTenantAsync(string slug)
    {
        var tenant = await _platformDb.Tenants.FirstOrDefaultAsync(t => t.Slug == slug)
            ?? throw new InvalidOperationException($"Tenant '{slug}' not found");

        var platformConnStr = _config["Platform:ConnectionString"]!;
        await DropDatabaseAsync(platformConnStr, tenant.DbName);

        _platformDb.Tenants.Remove(tenant);
        await _platformDb.SaveChangesAsync();

        _cache.Remove($"tenant:{slug}");
        _logger.LogInformation("Tenant {Slug} and database {DbName} deleted", slug, tenant.DbName);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<string> GenerateUniqueSlugAsync(string companyName)
    {
        var baseSlug = SlugifyName(companyName);
        var candidate = baseSlug;
        var suffix = 2;

        while (await _platformDb.Tenants.AnyAsync(t => t.Slug == candidate))
            candidate = $"{baseSlug}-{suffix++}";

        return candidate;
    }

    public static string SlugifyName(string companyName) => Slugify(companyName);

    private static string Slugify(string input)
    {
        var s = input.ToLowerInvariant();
        // German umlauts
        s = s.Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
        // Replace non-alphanumeric with dash
        s = Regex.Replace(s, @"[^a-z0-9]+", "-");
        // Trim leading/trailing dashes, collapse multiple dashes
        s = s.Trim('-');
        s = Regex.Replace(s, @"-{2,}", "-");
        return string.IsNullOrEmpty(s) ? "tenant" : s;
    }

    private static async Task CreateDatabaseAsync(string platformConnStr, string dbName)
    {
        var adminCs = GetAdminConnectionString(platformConnStr);
        await using var conn = new NpgsqlConnection(adminCs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // Parameterized queries don't work for DDL; dbName is sanitized (slug → only alphanumeric + underscore)
        cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string platformConnStr, string dbName)
    {
        var adminCs = GetAdminConnectionString(platformConnStr);
        await using var conn = new NpgsqlConnection(adminCs);
        await conn.OpenAsync();
        // Terminate existing connections before dropping
        await using var terminateCmd = conn.CreateCommand();
        terminateCmd.CommandText = $"""
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = '{dbName}' AND pid <> pg_backend_pid()
            """;
        await terminateCmd.ExecuteNonQueryAsync();
        await using var dropCmd = conn.CreateCommand();
        dropCmd.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\"";
        await dropCmd.ExecuteNonQueryAsync();
    }

    private static string GetAdminConnectionString(string platformConnStr)
    {
        var builder = new NpgsqlConnectionStringBuilder(platformConnStr)
        {
            Database = "postgres"
        };
        return builder.ConnectionString;
    }

    private static async Task<(string Username, string Password)> CreateAdminUserAsync(AppDbContext db, string email)
    {
        const string username = "admin";
        var password = GenerateRandomPassword(16);
        var hash = BCrypt.Net.BCrypt.HashPassword(password);

        db.Users.Add(new User
        {
            Username = username,
            PasswordHash = hash,
            Role = "Admin",
            Email = email,
            IsLocal = true,
        });
        await db.SaveChangesAsync();
        return (username, password);
    }

    private static async Task<string> CreateDeployKeyAsync(AppDbContext db)
    {
        var key = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8];
        db.DeployKeys.Add(new DeployKey
        {
            Key = key,
            Name = "Standard-Deploy-Key",
            CreatedByUsername = "system",
        });
        await db.SaveChangesAsync();
        return key;
    }

    private static string GenerateRandomPassword(int length)
    {
        const string chars = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$";
        var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var bytes = new byte[length];
        rng.GetBytes(bytes);
        var sb = new StringBuilder(length);
        foreach (var b in bytes)
            sb.Append(chars[b % chars.Length]);
        return sb.ToString();
    }

    private async Task SendWelcomeEmailAsync(ProvisionResult result, Tenant tenant)
    {
        if (!_email.IsConfigured || string.IsNullOrEmpty(tenant.AdminEmail)) return;

        try
        {
            var supportEmail = _config["Platform:SupportEmail"] ?? "";
            var supportUrl = _config["Platform:SupportUrl"] ?? "";
            var platformDomain = _config["Platform:Domain"] ?? "localhost";

            var trialSection = tenant.Plan == "free"
                ? """
                  <tr style="border-top:1px solid #e4e4e7;">
                    <td style="padding:11px 16px;font-size:13px;color:#71717a;">Lizenz</td>
                    <td style="padding:11px 16px;font-size:13px;color:#18181b;font-weight:600;">Dauerhaft kostenlos &amp; unbegrenzt</td>
                  </tr>
                  """
                : $"""
                  <tr style="border-top:1px solid #e4e4e7;">
                    <td style="padding:11px 16px;font-size:13px;color:#71717a;">Testphase bis</td>
                    <td style="padding:11px 16px;font-size:13px;color:#18181b;font-weight:600;">{tenant.TrialEndsAt:dd.MM.yyyy} – danach automatische Abbuchung</td>
                  </tr>
                  """;

            var supportSection = string.IsNullOrEmpty(supportEmail) ? "" : $"""
                <p style="margin:24px 0 0;font-size:13px;color:#71717a;line-height:1.6;">
                  <strong style="color:#18181b;">Support:</strong>
                  {(string.IsNullOrEmpty(supportUrl) ? "" : $"<a href='{supportUrl}' style='color:#2563eb;'>Ticket erstellen</a> &middot; ")}
                  <a href="mailto:{supportEmail}" style="color:#2563eb;">{supportEmail}</a>
                </p>
                """;

            var body = AlertEmailService.BuildHtml(
                "#16a34a", "Willkommen",
                $"Willkommen bei HITSight, {tenant.Name}!",
                $"""
                <p style="margin:0 0 20px;font-size:14px;color:#3f3f46;line-height:1.6;">
                  Ihre Instanz ist bereit. Melden Sie sich mit den folgenden Zugangsdaten an:
                </p>

                <table style="width:100%;border-collapse:collapse;border:1px solid #e4e4e7;border-radius:6px;overflow:hidden;font-size:14px;margin-bottom:20px;">
                  <tr>
                    <td style="padding:11px 16px;font-size:13px;color:#71717a;width:130px;">Login-URL</td>
                    <td style="padding:11px 16px;">
                      <a href="{result.LoginUrl}" style="color:#2563eb;font-weight:600;">{result.LoginUrl}</a>
                    </td>
                  </tr>
                  <tr style="border-top:1px solid #e4e4e7;">
                    <td style="padding:11px 16px;font-size:13px;color:#71717a;">Benutzername</td>
                    <td style="padding:11px 16px;font-size:13px;font-family:monospace;font-weight:600;color:#18181b;">{result.AdminUsername}</td>
                  </tr>
                  <tr style="border-top:1px solid #e4e4e7;">
                    <td style="padding:11px 16px;font-size:13px;color:#71717a;">Passwort</td>
                    <td style="padding:11px 16px;font-size:13px;font-family:monospace;font-weight:600;color:#dc2626;">{result.AdminPassword}</td>
                  </tr>
                  {trialSection}
                </table>

                <p style="margin:0 0 12px;font-size:14px;font-weight:600;color:#18181b;">Agent auf Windows-Geräten installieren</p>
                <p style="margin:0 0 12px;font-size:13px;color:#71717a;line-height:1.6;">
                  Führen Sie dieses PowerShell-Skript als Administrator auf dem Zielgerät aus:
                </p>
                <div style="background:#18181b;border-radius:6px;padding:14px 16px;margin-bottom:8px;">
                  <code style="font-size:12px;color:#a3e635;font-family:monospace;word-break:break-all;">
                    $wc=[Net.WebClient]::new();$wc.Headers.Add('X-Deploy-Key','{result.DeployKeyToken}');iex $wc.DownloadString('{result.MsiInstallUrl}')
                  </code>
                </div>
                <p style="margin:0 0 24px;font-size:12px;color:#a1a1aa;">
                  Alternativ: Nach dem Login finden Sie alle Installations-Optionen unter <strong>Einstellungen → Installation</strong>.
                </p>

                <a href="{result.LoginUrl}"
                   style="display:inline-block;padding:12px 28px;background:#18181b;color:#ffffff;text-decoration:none;border-radius:6px;font-size:14px;font-weight:600;">
                  Jetzt anmelden →
                </a>

                {supportSection}
                """,
                "Bitte ändern Sie Ihr Passwort nach dem ersten Login.");

            await _email.SendAsync(
                tenant.AdminEmail,
                "Willkommen bei HITSight — Ihre Zugangsdaten",
                body);

            _logger.LogInformation("Welcome email sent to {Email} for tenant {Slug}", tenant.AdminEmail, tenant.Slug);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome email for tenant {Slug}", tenant.Slug);
        }
    }
}

