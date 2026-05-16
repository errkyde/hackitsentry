using HITSight.Server.Data;
using HITSight.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/tenant-info")]
[Authorize]
public class TenantInfoController(AppDbContext db, ITenantContext tenantCtx) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var deviceCount = await db.Devices.CountAsync();

        return Ok(new
        {
            plan = tenantCtx.Plan,
            maxDevices = tenantCtx.MaxDevices == int.MaxValue ? (int?)null : tenantCtx.MaxDevices,
            deviceCount,
            subscriptionStatus = tenantCtx.SubscriptionStatus,
            trialEndsAt = tenantCtx.TrialEndsAt,
            currentPeriodEndsAt = tenantCtx.CurrentPeriodEndsAt,
        });
    }
}
