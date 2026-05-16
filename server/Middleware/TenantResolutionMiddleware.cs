using HITSight.Server.Data;
using HITSight.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HITSight.Server.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        IConfiguration config,
        ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _config = config;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, TenantContext tenantCtx)
    {
        var platformConnStr = _config["Platform:ConnectionString"];

        // Single-tenant mode (no Platform DB): use DefaultConnection
        if (string.IsNullOrEmpty(platformConnStr))
        {
            tenantCtx.ConnectionString = _config.GetConnectionString("DefaultConnection")!;
            tenantCtx.IsActive = true;
            tenantCtx.MaxDevices = int.MaxValue;
            await _next(context);
            return;
        }

        var host = context.Request.Host.Host;
        var platformDomain = _config["Platform:Domain"] ?? "";
        var adminSubdomain = _config["Platform:AdminSubdomain"] ?? "admin";

        // Skip tenant resolution for admin subdomain (handled by platform auth)
        if (!string.IsNullOrEmpty(platformDomain) && host == $"{adminSubdomain}.{platformDomain}")
        {
            await _next(context);
            return;
        }

        // Skip tenant resolution for the root domain (landing page)
        if (!string.IsNullOrEmpty(platformDomain) && (host == platformDomain || host == $"www.{platformDomain}"))
        {
            await _next(context);
            return;
        }

        // Extract slug from subdomain
        var slug = ExtractSlug(host, platformDomain);
        if (string.IsNullOrEmpty(slug))
        {
            context.Response.StatusCode = 404;
            return;
        }

        var tenant = await _cache.GetOrCreateAsync($"tenant:{slug}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            try
            {
                var opts = new DbContextOptionsBuilder<PlatformDbContext>()
                    .UseNpgsql(platformConnStr)
                    .Options;
                await using var platformDb = new PlatformDbContext(opts);
                return await platformDb.Tenants.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Slug == slug);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve tenant for slug {Slug}", slug);
                return null;
            }
        });

        // Identical 404 for unknown AND inactive tenants (prevents timing-based enumeration)
        if (tenant == null || !tenant.IsActive)
        {
            context.Response.StatusCode = 404;
            return;
        }

        tenantCtx.TenantId = tenant.Id;
        tenantCtx.Slug = tenant.Slug;
        tenantCtx.ConnectionString = BuildTenantConnectionString(platformConnStr, tenant.DbName);
        tenantCtx.Plan = tenant.Plan;
        tenantCtx.MaxDevices = tenant.MaxDevices;
        tenantCtx.IsActive = tenant.IsActive;
        tenantCtx.SubscriptionStatus = tenant.SubscriptionStatus;
        tenantCtx.TrialEndsAt = tenant.TrialEndsAt;
        tenantCtx.CurrentPeriodEndsAt = tenant.CurrentPeriodEndsAt;

        await _next(context);
    }

    private static string ExtractSlug(string host, string platformDomain)
    {
        if (string.IsNullOrEmpty(platformDomain))
            return "";

        if (!host.EndsWith($".{platformDomain}", StringComparison.OrdinalIgnoreCase))
            return "";

        var slug = host[..^(platformDomain.Length + 1)];
        return slug.Contains('.') ? "" : slug; // no nested subdomains
    }

    public static string BuildTenantConnectionString(string platformConnStr, string dbName)
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(platformConnStr)
        {
            Database = dbName
        };
        return builder.ConnectionString;
    }
}
