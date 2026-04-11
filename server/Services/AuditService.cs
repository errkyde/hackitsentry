using HackITSentry.Server.Data;
using HackITSentry.Server.Models;

namespace HackITSentry.Server.Services;

public class AuditService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContext;

    public AuditService(AppDbContext db, IHttpContextAccessor httpContext)
    {
        _db = db;
        _httpContext = httpContext;
    }

    public async Task LogAsync(string action, string entityType, string? entityId = null, string? details = null)
    {
        var username = _httpContext.HttpContext?.User?.Identity?.Name ?? "system";
        var ip = _httpContext.HttpContext?.Connection?.RemoteIpAddress?.ToString();

        _db.AuditLogs.Add(new AuditLog
        {
            Username = username,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            IpAddress = ip
        });

        await _db.SaveChangesAsync();
    }
}
