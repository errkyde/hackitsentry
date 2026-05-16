using HITSight.Server.Data;
using HITSight.Server.Middleware;
using HITSight.Server.Models;
using HITSight.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
builder.Services.AddResponseCompression(opts =>
{
    opts.EnableForHttps = true;
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
});

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.AddFixedWindowLimiter("agent-register", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.AddFixedWindowLimiter("checkout", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.StatusCode = 429;
        await ctx.HttpContext.Response.WriteAsync("Too many requests. Please try again later.", token);
    };
});

// ── Stripe ────────────────────────────────────────────────────────────────────
var stripeKey = builder.Configuration["Stripe:SecretKey"];
if (!string.IsNullOrEmpty(stripeKey))
    Stripe.StripeConfiguration.ApiKey = stripeKey;

// ── Platform DB (optional — only in multi-tenant SaaS mode) ─────────────────
var platformConnStr = builder.Configuration["Platform:ConnectionString"];
if (!string.IsNullOrEmpty(platformConnStr))
{
    builder.Services.AddDbContext<PlatformDbContext>(options =>
        options.UseNpgsql(platformConnStr));

    builder.Services.AddScoped<TenantProvisioningService>();
    builder.Services.AddHostedService<TenantCleanupService>();
    builder.Services.AddHostedService<TrialReminderService>();
}

builder.Services.AddSingleton<PlatformEmailService>();

// ── Tenant resolution ────────────────────────────────────────────────────────
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

// ── Tenant-scoped AppDbContext — connection string from TenantContext ─────────
builder.Services.AddScoped<AppDbContext>(sp =>
{
    var tenantCtx = sp.GetRequiredService<TenantContext>();
    var cs = !string.IsNullOrEmpty(tenantCtx.ConnectionString)
        ? tenantCtx.ConnectionString
        : builder.Configuration.GetConnectionString("DefaultConnection")!;
    var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
    return new AppDbContext(opts);
});

// ── Per-tenant RuntimeSettings & AlertEmailService (Scoped) ──────────────────
builder.Services.AddScoped<RuntimeSettings>();
builder.Services.AddScoped<AlertEmailService>();

// ── Singletons that don't depend on tenant context ───────────────────────────
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<PlatformJwtService>();
builder.Services.AddSingleton<LicenseEncryptionService>();
builder.Services.AddSingleton<InstallerService>();
builder.Services.AddSingleton<AgentCommandNotifier>();
builder.Services.AddHostedService<DeviceOfflineAlertService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<LdapService>();
builder.Services.AddHttpContextAccessor();

var jwtKey = builder.Configuration["Jwt:Key"]!;
var platformJwtKey = builder.Configuration["Platform:JwtKey"];
// Fall back to a random per-process key so the "SuperAdmin" scheme is always registered
// (controllers protected by [Authorize(Policy="SuperAdminFull")] reject unauthenticated calls regardless)
var superAdminKey = string.IsNullOrEmpty(platformJwtKey)
    ? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
    : platformJwtKey;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = "HITSight",
            ValidateAudience = true,
            ValidAudience = "HITSight",
            ClockSkew = TimeSpan.Zero
        };
    })
    .AddJwtBearer("SuperAdmin", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(superAdminKey)),
            ValidateIssuer = true,
            ValidIssuer = "HITSightPlatform",
            ValidateAudience = true,
            ValidAudience = "HITSightPlatform",
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdminFull", policy =>
    {
        policy.AddAuthenticationSchemes("SuperAdmin");
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("phase", "full");
    });
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

var corsPlatformDomain = builder.Configuration["Platform:Domain"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (!string.IsNullOrEmpty(corsPlatformDomain))
        {
            // Platform / SaaS mode: allow any subdomain of the platform domain
            policy.SetIsOriginAllowed(origin =>
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
                return uri.Host.Equals(corsPlatformDomain, StringComparison.OrdinalIgnoreCase)
                    || uri.Host.EndsWith("." + corsPlatformDomain, StringComparison.OrdinalIgnoreCase);
            })
            .AllowAnyHeader()
            .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

// ── Startup: initialize databases ────────────────────────────────────────────
{
    // Always run migrations on the default/legacy DB
    var defaultCs = builder.Configuration.GetConnectionString("DefaultConnection")!;
    await using var startupDb = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(defaultCs).Options);
    await DbMigrator.RunAsync(startupDb);
    await DbMigrator.SeedDefaultPackagesAsync(startupDb);

    // Seed admin user if no users exist
    if (!await startupDb.Users.AnyAsync())
    {
        startupDb.Users.Add(new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
            Role = "Admin"
        });
        await startupDb.SaveChangesAsync();
    }
}

// Platform DB: initialize schema and run migrations for all tenant DBs
if (!string.IsNullOrEmpty(platformConnStr))
{
    await using var scope = app.Services.CreateAsyncScope();
    var platformDb = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

    // Ensure Platform DB tables exist
    await platformDb.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "Tenants" (
            "Id"                     uuid NOT NULL DEFAULT gen_random_uuid(),
            "Slug"                   text NOT NULL DEFAULT '',
            "Name"                   text NOT NULL DEFAULT '',
            "DbName"                 text NOT NULL DEFAULT '',
            "Plan"                   text NOT NULL DEFAULT 'starter',
            "MaxDevices"             integer NOT NULL DEFAULT 25,
            "IsActive"               boolean NOT NULL DEFAULT true,
            "AdminEmail"             text NOT NULL DEFAULT '',
            "StripeCustomerId"       text,
            "StripeSubscriptionId"   text,
            "SubscriptionStatus"     text,
            "TrialEndsAt"            timestamp with time zone,
            "CurrentPeriodEndsAt"    timestamp with time zone,
            "DeactivatedAt"          timestamp with time zone,
            "ScheduledDeletionAt"    timestamp with time zone,
            "CreatedAt"              timestamp with time zone NOT NULL DEFAULT now(),
            CONSTRAINT "PK_Tenants" PRIMARY KEY ("Id")
        )
        """);

    await platformDb.Database.ExecuteSqlRawAsync("""
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Tenants_Slug"
            ON "Tenants" ("Slug")
        """);

    // Idempotent column additions for Platform DB
    await platformDb.Database.ExecuteSqlRawAsync("""
        ALTER TABLE "Tenants" ADD COLUMN IF NOT EXISTS "TrialReminderSentAt" timestamp with time zone
        """);

    await platformDb.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "SuperAdminUsers" (
            "Id"           uuid NOT NULL DEFAULT gen_random_uuid(),
            "Username"     text NOT NULL DEFAULT '',
            "PasswordHash" text NOT NULL DEFAULT '',
            "TotpSecret"   text,
            "TotpEnabled"  boolean NOT NULL DEFAULT false,
            "CreatedAt"    timestamp with time zone NOT NULL DEFAULT now(),
            "LastLoginAt"  timestamp with time zone,
            CONSTRAINT "PK_SuperAdminUsers" PRIMARY KEY ("Id")
        )
        """);
    await platformDb.Database.ExecuteSqlRawAsync("""
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_SuperAdminUsers_Username" ON "SuperAdminUsers" ("Username")
        """);

    await platformDb.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "TenantExtensions" (
            "Id"                  uuid NOT NULL DEFAULT gen_random_uuid(),
            "TenantId"            uuid NOT NULL,
            "DaysAdded"           integer NOT NULL DEFAULT 0,
            "Reason"              text,
            "SendToast"           boolean NOT NULL DEFAULT false,
            "SendEmail"           boolean NOT NULL DEFAULT false,
            "CreatedByUsername"   text NOT NULL DEFAULT '',
            "CreatedAt"           timestamp with time zone NOT NULL DEFAULT now(),
            CONSTRAINT "PK_TenantExtensions" PRIMARY KEY ("Id"),
            CONSTRAINT "FK_TenantExtensions_Tenants" FOREIGN KEY ("TenantId")
                REFERENCES "Tenants" ("Id") ON DELETE CASCADE
        )
        """);
    await platformDb.Database.ExecuteSqlRawAsync("""
        CREATE INDEX IF NOT EXISTS "IX_TenantExtensions_TenantId" ON "TenantExtensions" ("TenantId")
        """);

    // Seed default super admin if none exists
    if (!await platformDb.SuperAdminUsers.AnyAsync())
    {
        platformDb.SuperAdminUsers.Add(new HITSight.Server.Models.SuperAdminUser
        {
            Username = "superadmin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("changeme"),
        });
        await platformDb.SaveChangesAsync();
        app.Logger.LogWarning("Created default super admin 'superadmin' with password 'changeme' — change immediately!");
    }

    // Run migrations for all existing tenant DBs
    var tenants = await platformDb.Tenants.Where(t => t.IsActive).AsNoTracking().ToListAsync();
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    foreach (var tenant in tenants)
    {
        try
        {
            var tenantCs = TenantResolutionMiddleware.BuildTenantConnectionString(platformConnStr, tenant.DbName);
            await using var tenantDb = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(tenantCs).Options);
            await DbMigrator.RunAsync(tenantDb);
            await DbMigrator.SeedDefaultPackagesAsync(tenantDb);
        }
        catch (Exception ex)
        {
            startupLogger.LogError(ex, "Failed to run migrations for tenant {Slug}", tenant.Slug);
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseResponseCompression();
app.UseRateLimiter();

// Serve agent binaries as static files
var downloadsPath = Path.Combine(AppContext.BaseDirectory, "downloads");
Directory.CreateDirectory(downloadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(downloadsPath),
    RequestPath = "/downloads",
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream"
});

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseCors();

// Tenant resolution runs BEFORE authentication so the correct DB is set per request
app.UseMiddleware<TenantResolutionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
