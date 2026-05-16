using HITSight.Server.Data;
using HITSight.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/onboarding")]
[Authorize]
public class OnboardingController : ControllerBase
{
    private readonly AppDbContext _db;

    public OnboardingController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetStatus()
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "Platform:OnboardingDone");
        return Ok(new { done = setting?.Value == "true" });
    }

    [HttpPost("complete")]
    public async Task<IActionResult> Complete()
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "Platform:OnboardingDone");
        if (setting == null)
            _db.AppSettings.Add(new AppSetting { Key = "Platform:OnboardingDone", Value = "true" });
        else
            setting.Value = "true";
        await _db.SaveChangesAsync();
        return Ok(new { message = "Onboarding abgeschlossen" });
    }
}
