using HITSight.Server.Data;
using HITSight.Server.Middleware;
using HITSight.Server.Models;
using HITSight.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/platform/admin")]
[Authorize(Policy = "SuperAdminFull")]
public class PlatformAdminController : ControllerBase
{
    private readonly PlatformDbContext? _db;
    private readonly TenantProvisioningService? _provisioning;
    private readonly PlatformEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<PlatformAdminController> _logger;

    public PlatformAdminController(
        PlatformEmailService email,
        IConfiguration config,
        ILogger<PlatformAdminController> logger,
        PlatformDbContext? db = null,
        TenantProvisioningService? provisioning = null)
    {
        _email = email;
        _config = config;
        _logger = logger;
        _db = db;
        _provisioning = provisioning;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        if (_db == null) return StatusCode(503);
        var tenants = await _db.Tenants.AsNoTracking().ToListAsync();
        return Ok(new
        {
            total = tenants.Count,
            active = tenants.Count(t => t.IsActive),
            trialing = tenants.Count(t => t.SubscriptionStatus == "trialing"),
            free = tenants.Count(t => t.Plan == "free"),
            scheduledDeletion = tenants.Count(t => t.ScheduledDeletionAt.HasValue),
        });
    }

    [HttpGet("tenants")]
    public async Task<IActionResult> ListTenants(
        [FromQuery] string? search,
        [FromQuery] string? plan,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        if (_db == null) return StatusCode(503);
        var query = _db.Tenants.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(search))
            query = query.Where(t => t.Name.Contains(search) || t.Slug.Contains(search) || t.AdminEmail.Contains(search));
        if (!string.IsNullOrEmpty(plan))
            query = query.Where(t => t.Plan == plan);
        if (status == "active") query = query.Where(t => t.IsActive);
        else if (status == "inactive") query = query.Where(t => !t.IsActive);
        else if (status == "trialing") query = query.Where(t => t.SubscriptionStatus == "trialing");
        else if (status == "deletion") query = query.Where(t => t.ScheduledDeletionAt.HasValue);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id, t.Slug, t.Name, t.Plan, t.MaxDevices, t.IsActive,
                t.AdminEmail, t.SubscriptionStatus, t.TrialEndsAt,
                t.CurrentPeriodEndsAt, t.ScheduledDeletionAt, t.CreatedAt,
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("tenants/{id:guid}")]
    public async Task<IActionResult> GetTenant(Guid id)
    {
        if (_db == null) return StatusCode(503);
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant == null) return NotFound();

        var extensions = await _db.TenantExtensions
            .AsNoTracking()
            .Where(e => e.TenantId == id)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new { e.Id, e.DaysAdded, e.Reason, e.SendToast, e.SendEmail, e.CreatedByUsername, e.CreatedAt })
            .ToListAsync();

        int? deviceCount = null;
        try
        {
            await using var tenantDb = OpenTenantDb(tenant);
            deviceCount = await tenantDb.Devices.CountAsync();
        }
        catch { }

        return Ok(new
        {
            tenant.Id, tenant.Slug, tenant.Name, tenant.Plan, tenant.MaxDevices,
            tenant.IsActive, tenant.AdminEmail, tenant.SubscriptionStatus,
            tenant.TrialEndsAt, tenant.CurrentPeriodEndsAt, tenant.DeactivatedAt,
            tenant.ScheduledDeletionAt, tenant.CreatedAt, tenant.StripeCustomerId,
            tenant.StripeSubscriptionId, tenant.TrialReminderSentAt,
            deviceCount,
            extensions,
        });
    }

    [HttpPost("tenants")]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest req)
    {
        if (_provisioning == null || _db == null) return StatusCode(503);

        if (string.IsNullOrWhiteSpace(req.CompanyName) || string.IsNullOrWhiteSpace(req.AdminEmail))
            return BadRequest(new { message = "Name und E-Mail sind Pflichtfelder" });

        var validPlans = new[] { "starter", "pro", "enterprise", "free" };
        if (!validPlans.Contains(req.Plan))
            return BadRequest(new { message = "Ungültiger Plan" });

        try
        {
            var result = await _provisioning.ProvisionAsync(
                req.CompanyName,
                req.AdminEmail,
                req.Plan,
                maxDevices: req.MaxDevices,
                trialDays: req.Plan == "free" ? 0 : (req.TrialDays ?? 14),
                subscriptionStatus: req.Plan == "free" ? "free" : (req.TrialDays == 0 ? "active" : "trialing"));

            return Ok(new
            {
                result.Slug,
                result.LoginUrl,
                result.AdminUsername,
                result.AdminPassword,
                result.DeployKeyToken,
                result.MsiInstallUrl,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision tenant {Name}", req.CompanyName);
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPatch("tenants/{id:guid}")]
    public async Task<IActionResult> UpdateTenant(Guid id, [FromBody] UpdateTenantRequest req)
    {
        if (_db == null) return StatusCode(503);
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant == null) return NotFound();

        if (!string.IsNullOrEmpty(req.Plan))
        {
            tenant.Plan = req.Plan;
            tenant.MaxDevices = req.MaxDevices ?? req.Plan switch
            {
                "pro" => 100,
                "enterprise" => int.MaxValue,
                _ => 25
            };
        }
        else if (req.MaxDevices.HasValue)
        {
            tenant.MaxDevices = req.MaxDevices.Value;
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Aktualisiert" });
    }

    [HttpPost("tenants/{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        if (_db == null) return StatusCode(503);
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant == null) return NotFound();

        tenant.IsActive = false;
        tenant.DeactivatedAt = DateTime.UtcNow;
        tenant.ScheduledDeletionAt = DateTime.UtcNow.AddDays(30);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Deaktiviert. Löschung in 30 Tagen." });
    }

    [HttpPost("tenants/{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        if (_db == null) return StatusCode(503);
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant == null) return NotFound();

        tenant.IsActive = true;
        tenant.DeactivatedAt = null;
        tenant.ScheduledDeletionAt = null;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Aktiviert" });
    }

    [HttpPost("tenants/{id:guid}/cancel-deletion")]
    public async Task<IActionResult> CancelDeletion(Guid id)
    {
        if (_db == null) return StatusCode(503);
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant == null) return NotFound();

        tenant.ScheduledDeletionAt = null;
        tenant.IsActive = true;
        tenant.DeactivatedAt = null;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Löschung abgebrochen" });
    }

    [HttpDelete("tenants/{id:guid}")]
    public async Task<IActionResult> DeleteTenant(Guid id)
    {
        if (_provisioning == null || _db == null) return StatusCode(503);
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant == null) return NotFound();

        try
        {
            await _provisioning.DropTenantAsync(tenant.Slug);
            return Ok(new { message = "Tenant gelöscht" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete tenant {Slug}", tenant.Slug);
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("tenants/{id:guid}/extend")]
    public async Task<IActionResult> Extend(Guid id, [FromBody] ExtendRequest req)
    {
        if (_db == null) return StatusCode(503);
        if (req.DaysAdded <= 0) return BadRequest(new { message = "DaysAdded muss > 0 sein" });

        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant == null) return NotFound();

        // Compute new end date
        var baseDate = tenant.SubscriptionStatus == "trialing" && tenant.TrialEndsAt.HasValue
            ? tenant.TrialEndsAt.Value
            : (tenant.CurrentPeriodEndsAt ?? DateTime.UtcNow);

        var from = baseDate < DateTime.UtcNow ? DateTime.UtcNow : baseDate;
        var newEnd = from.AddDays(req.DaysAdded);

        if (tenant.SubscriptionStatus == "trialing" && tenant.TrialEndsAt.HasValue)
            tenant.TrialEndsAt = newEnd;
        else
            tenant.CurrentPeriodEndsAt = newEnd;

        // Optional plan / device limit change
        if (!string.IsNullOrEmpty(req.Plan) && req.Plan != tenant.Plan)
        {
            tenant.Plan = req.Plan;
            tenant.MaxDevices = req.MaxDevices ?? req.Plan switch
            {
                "pro" => 100,
                "enterprise" => int.MaxValue,
                _ => 25
            };
        }
        else if (req.MaxDevices.HasValue)
        {
            tenant.MaxDevices = req.MaxDevices.Value;
        }

        // Reactivate if previously deactivated
        if (!tenant.IsActive)
        {
            tenant.IsActive = true;
            tenant.DeactivatedAt = null;
            tenant.ScheduledDeletionAt = null;
        }

        var adminUsername = User.FindFirst("sub")?.Value ?? "superadmin";
        _db.TenantExtensions.Add(new TenantExtension
        {
            TenantId = tenant.Id,
            DaysAdded = req.DaysAdded,
            Reason = req.Reason,
            SendToast = req.SendToast,
            SendEmail = req.SendEmail,
            CreatedByUsername = adminUsername,
        });

        await _db.SaveChangesAsync();

        // Write login toast message to tenant DB
        if (req.SendToast && !string.IsNullOrEmpty(req.Reason))
        {
            try
            {
                await using var tenantDb = OpenTenantDb(tenant);
                var existing = await tenantDb.AppSettings.FindAsync("Platform:LoginMessage");
                if (existing == null)
                    tenantDb.AppSettings.Add(new AppSetting { Key = "Platform:LoginMessage", Value = req.Reason });
                else
                    existing.Value = req.Reason;
                await tenantDb.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write login message to tenant {Slug}", tenant.Slug);
            }
        }

        // Send email notification
        if (req.SendEmail && !string.IsNullOrEmpty(tenant.AdminEmail) && _email.IsConfigured)
        {
            _ = SendExtensionEmailAsync(tenant, req.DaysAdded, req.Reason, newEnd);
        }

        return Ok(new { message = $"{req.DaysAdded} Tage gutgeschrieben", newEndDate = newEnd });
    }

    [HttpGet("tenants/{id:guid}/extensions")]
    public async Task<IActionResult> GetExtensions(Guid id)
    {
        if (_db == null) return StatusCode(503);
        var extensions = await _db.TenantExtensions
            .AsNoTracking()
            .Where(e => e.TenantId == id)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new { e.Id, e.DaysAdded, e.Reason, e.SendToast, e.SendEmail, e.CreatedByUsername, e.CreatedAt })
            .ToListAsync();
        return Ok(extensions);
    }

    [HttpGet("super-admins")]
    public async Task<IActionResult> ListSuperAdmins()
    {
        if (_db == null) return StatusCode(503);
        var admins = await _db.SuperAdminUsers
            .AsNoTracking()
            .Select(u => new { u.Id, u.Username, u.TotpEnabled, u.CreatedAt, u.LastLoginAt })
            .ToListAsync();
        return Ok(admins);
    }

    [HttpPost("super-admins")]
    public async Task<IActionResult> CreateSuperAdmin([FromBody] CreateSuperAdminRequest req)
    {
        if (_db == null) return StatusCode(503);
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "Username und Passwort sind Pflichtfelder" });

        if (await _db.SuperAdminUsers.AnyAsync(u => u.Username == req.Username))
            return Conflict(new { message = "Username bereits vergeben" });

        _db.SuperAdminUsers.Add(new SuperAdminUser
        {
            Username = req.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
        });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Super-Admin erstellt" });
    }

    [HttpDelete("super-admins/{id:guid}")]
    public async Task<IActionResult> DeleteSuperAdmin(Guid id)
    {
        if (_db == null) return StatusCode(503);
        var admin = await _db.SuperAdminUsers.FindAsync(id);
        if (admin == null) return NotFound();

        if (await _db.SuperAdminUsers.CountAsync() <= 1)
            return BadRequest(new { message = "Letzter Super-Admin kann nicht gelöscht werden" });

        _db.SuperAdminUsers.Remove(admin);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Gelöscht" });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private AppDbContext OpenTenantDb(Tenant tenant)
    {
        var platformConnStr = _config["Platform:ConnectionString"]!;
        var cs = TenantResolutionMiddleware.BuildTenantConnectionString(platformConnStr, tenant.DbName);
        return new AppDbContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options);
    }

    private async Task SendExtensionEmailAsync(Tenant tenant, int days, string? reason, DateTime newEnd)
    {
        try
        {
            var platformDomain = _config["Platform:Domain"] ?? "localhost";
            var loginUrl = $"https://{tenant.Slug}.{platformDomain}/login";
            var reasonHtml = string.IsNullOrEmpty(reason) ? "" :
                $"<p style='margin:12px 0;font-size:14px;color:#3f3f46;line-height:1.6;'><em>{System.Web.HttpUtility.HtmlEncode(reason)}</em></p>";

            var body = AlertEmailService.BuildHtml(
                "#16a34a", "Gutschrift",
                $"Ihr Konto wurde um {days} Tag{(days == 1 ? "" : "e")} verlängert",
                $"""
                <p style="margin:0 0 12px;font-size:14px;color:#3f3f46;line-height:1.6;">
                  Ihr HITSight Konto (<strong>{tenant.Name}</strong>) wurde verlängert.
                </p>
                {reasonHtml}
                <p style="margin:12px 0;font-size:14px;color:#3f3f46;">
                  Neues Laufzeitende: <strong>{newEnd:dd.MM.yyyy}</strong>
                </p>
                <a href="{loginUrl}"
                   style="display:inline-block;padding:12px 28px;background:#18181b;color:#fff;text-decoration:none;border-radius:6px;font-size:14px;font-weight:600;">
                  Zum Dashboard
                </a>
                """,
                $"Verlängert bis {newEnd:dd.MM.yyyy}");

            await _email.SendAsync(
                tenant.AdminEmail,
                $"HITSight – Ihr Konto wurde um {days} Tag{(days == 1 ? "" : "e")} verlängert",
                body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send extension email to {Email}", tenant.AdminEmail);
        }
    }

    public record CreateTenantRequest(
        string CompanyName,
        string AdminEmail,
        string Plan,
        int? MaxDevices = null,
        int? TrialDays = null);

    public record UpdateTenantRequest(string? Plan = null, int? MaxDevices = null);

    public record ExtendRequest(
        int DaysAdded,
        string? Reason = null,
        bool SendToast = false,
        bool SendEmail = false,
        string? Plan = null,
        int? MaxDevices = null);

    public record CreateSuperAdminRequest(string Username, string Password);
}
