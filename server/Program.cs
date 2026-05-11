using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using HackITSentry.Server.Services;
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
    // Login: max 5 attempts per IP per minute
    options.AddFixedWindowLimiter("login", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    // Agent registration: max 10 per IP per minute
    options.AddFixedWindowLimiter("agent-register", o =>
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

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<LicenseEncryptionService>();
builder.Services.AddSingleton<RuntimeSettings>();
builder.Services.AddSingleton<AlertEmailService>();
builder.Services.AddSingleton<InstallerService>();
builder.Services.AddSingleton<AgentCommandNotifier>();
builder.Services.AddHostedService<DeviceOfflineAlertService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<LdapService>();
builder.Services.AddHttpContextAccessor();

var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = "HackITSentry",
            ValidateAudience = true,
            ValidAudience = "HackITSentry",
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Initialize DB, seed admin, load runtime settings
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // All DDL migrations run in one transaction — much faster than one round-trip each
    using var tx = db.Database.BeginTransaction();

    // Create tables for existing deployments (EnsureCreated won't add new tables)
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "AppSettings" (
            "Key"   text NOT NULL,
            "Value" text NOT NULL,
            CONSTRAINT "PK_AppSettings" PRIMARY KEY ("Key")
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "DeviceNotes" (
            "Id"             uuid NOT NULL DEFAULT gen_random_uuid(),
            "DeviceId"       uuid NOT NULL,
            "Content"        text NOT NULL DEFAULT '',
            "AuthorUsername" text NOT NULL DEFAULT '',
            "CreatedAt"      timestamp with time zone NOT NULL DEFAULT now(),
            CONSTRAINT "PK_DeviceNotes" PRIMARY KEY ("Id"),
            CONSTRAINT "FK_DeviceNotes_Devices" FOREIGN KEY ("DeviceId")
                REFERENCES "Devices" ("Id") ON DELETE CASCADE
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "DeviceCommands" (
            "Id"               uuid NOT NULL DEFAULT gen_random_uuid(),
            "DeviceId"         uuid NOT NULL,
            "CommandType"      integer NOT NULL DEFAULT 0,
            "Parameters"       text,
            "Status"           integer NOT NULL DEFAULT 0,
            "IssuedByUsername" text NOT NULL DEFAULT '',
            "CreatedAt"        timestamp with time zone NOT NULL DEFAULT now(),
            "ExecutedAt"       timestamp with time zone,
            "Result"           text,
            CONSTRAINT "PK_DeviceCommands" PRIMARY KEY ("Id"),
            CONSTRAINT "FK_DeviceCommands_Devices" FOREIGN KEY ("DeviceId")
                REFERENCES "Devices" ("Id") ON DELETE CASCADE
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "AuditLogs" (
            "Id"         uuid NOT NULL DEFAULT gen_random_uuid(),
            "Username"   text NOT NULL DEFAULT '',
            "Action"     text NOT NULL DEFAULT '',
            "EntityType" text NOT NULL DEFAULT '',
            "EntityId"   text,
            "Details"    text,
            "IpAddress"  text,
            "Timestamp"  timestamp with time zone NOT NULL DEFAULT now(),
            CONSTRAINT "PK_AuditLogs" PRIMARY KEY ("Id")
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "SoftwareBlacklist" (
            "Id"              uuid NOT NULL DEFAULT gen_random_uuid(),
            "NamePattern"     text NOT NULL DEFAULT '',
            "Publisher"       text,
            "Reason"          text,
            "AddedByUsername" text NOT NULL DEFAULT '',
            "AddedAt"         timestamp with time zone NOT NULL DEFAULT now(),
            CONSTRAINT "PK_SoftwareBlacklist" PRIMARY KEY ("Id")
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "SoftwareAlerts" (
            "Id"                     uuid NOT NULL DEFAULT gen_random_uuid(),
            "DeviceId"               uuid NOT NULL,
            "BlacklistEntryId"       uuid NOT NULL,
            "SoftwareName"           text NOT NULL DEFAULT '',
            "SoftwareVersion"        text NOT NULL DEFAULT '',
            "DetectedAt"             timestamp with time zone NOT NULL DEFAULT now(),
            "AcknowledgedAt"         timestamp with time zone,
            "AcknowledgedByUsername" text,
            CONSTRAINT "PK_SoftwareAlerts" PRIMARY KEY ("Id"),
            CONSTRAINT "FK_SoftwareAlerts_Devices" FOREIGN KEY ("DeviceId")
                REFERENCES "Devices" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_SoftwareAlerts_Blacklist" FOREIGN KEY ("BlacklistEntryId")
                REFERENCES "SoftwareBlacklist" ("Id") ON DELETE CASCADE
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "AgentVersions" (
            "Id"          uuid NOT NULL DEFAULT gen_random_uuid(),
            "Version"     text NOT NULL DEFAULT '',
            "DownloadUrl" text,
            "Changelog"   text,
            "IsLatest"    boolean NOT NULL DEFAULT false,
            "ReleasedAt"  timestamp with time zone NOT NULL DEFAULT now(),
            CONSTRAINT "PK_AgentVersions" PRIMARY KEY ("Id")
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_AgentVersions_Version"
            ON "AgentVersions" ("Version")
        """);

    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "LicenseInfos"
            ADD COLUMN IF NOT EXISTS "ExpiresAt" timestamp with time zone
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "InstallTokens" (
            "Id"                  uuid NOT NULL DEFAULT gen_random_uuid(),
            "Token"               text NOT NULL,
            "CreatedByUsername"   text NOT NULL DEFAULT '',
            "CreatedAt"           timestamp with time zone NOT NULL DEFAULT now(),
            "ExpiresAt"           timestamp with time zone NOT NULL,
            "Used"                boolean NOT NULL DEFAULT false,
            "UsedAt"              timestamp with time zone,
            CONSTRAINT "PK_InstallTokens" PRIMARY KEY ("Id")
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_InstallTokens_Token"
            ON "InstallTokens" ("Token")
        """);

    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "PendingDevices"
            ADD COLUMN IF NOT EXISTS "InvitedByUsername" text
        """);

    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "PendingDevices"
            ADD COLUMN IF NOT EXISTS "DeployKeyName" text
        """);

    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices"
            ADD COLUMN IF NOT EXISTS "RustDeskId" text NOT NULL DEFAULT ''
        """);

    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices"
            ADD COLUMN IF NOT EXISTS "AgentVersion" text NOT NULL DEFAULT ''
        """);

    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices"
            ADD COLUMN IF NOT EXISTS "LastDiskAlertAt" timestamp with time zone
        """);

    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices"
            ADD COLUMN IF NOT EXISTS "DiskAlertAcknowledgedUsedPct" double precision
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "DeviceNotificationOverrides" (
            "Id"                   uuid NOT NULL DEFAULT gen_random_uuid(),
            "DeviceId"             uuid NOT NULL,
            "AlertOnOffline"       boolean,
            "AlertOnOnline"        boolean,
            "AlertOnSoftwareAlert" boolean,
            "AlertOnDiskFull"      boolean,
            CONSTRAINT "PK_DeviceNotificationOverrides" PRIMARY KEY ("Id"),
            CONSTRAINT "FK_DeviceNotificationOverrides_Devices" FOREIGN KEY ("DeviceId")
                REFERENCES "Devices" ("Id") ON DELETE CASCADE
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_DeviceNotificationOverrides_DeviceId"
            ON "DeviceNotificationOverrides" ("DeviceId")
        """);

    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "DeviceNotificationOverrides"
            ADD COLUMN IF NOT EXISTS "OfflineAlertDelayMinutes" integer NULL
        """);

    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "DeviceNotificationOverrides"
            ADD COLUMN IF NOT EXISTS "SourceGroupId" uuid NULL
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "CustomFieldDefinitions" (
            "Id"        uuid NOT NULL DEFAULT gen_random_uuid(),
            "Name"      text NOT NULL DEFAULT '',
            "SortOrder" integer NOT NULL DEFAULT 0,
            CONSTRAINT "PK_CustomFieldDefinitions" PRIMARY KEY ("Id")
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "CustomFieldValues" (
            "Id"           uuid NOT NULL DEFAULT gen_random_uuid(),
            "DefinitionId" uuid NOT NULL,
            "DeviceId"     uuid NOT NULL,
            "Value"        text NOT NULL DEFAULT '',
            CONSTRAINT "PK_CustomFieldValues" PRIMARY KEY ("Id"),
            CONSTRAINT "FK_CustomFieldValues_Definitions" FOREIGN KEY ("DefinitionId")
                REFERENCES "CustomFieldDefinitions" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_CustomFieldValues_Devices" FOREIGN KEY ("DeviceId")
                REFERENCES "Devices" ("Id") ON DELETE CASCADE
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_CustomFieldValues_DefinitionId_DeviceId"
            ON "CustomFieldValues" ("DefinitionId", "DeviceId")
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "DeployKeys" (
            "Id"                  uuid NOT NULL DEFAULT gen_random_uuid(),
            "Key"                 text NOT NULL DEFAULT '',
            "Name"                text NOT NULL DEFAULT '',
            "CreatedByUsername"   text NOT NULL DEFAULT '',
            "CreatedAt"           timestamp with time zone NOT NULL DEFAULT now(),
            "LastUsedAt"          timestamp with time zone,
            CONSTRAINT "PK_DeployKeys" PRIMARY KEY ("Id")
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_DeployKeys_Key"
            ON "DeployKeys" ("Key")
        """);

    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices"
            ADD COLUMN IF NOT EXISTS "RustDeskOptionsJson" text
        """);

    db.Database.ExecuteSqlRaw(
        """ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "BiosInfoJson" text NOT NULL DEFAULT '{{}}'""");

    db.Database.ExecuteSqlRaw(
        """ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "DefenderStatusJson" text NOT NULL DEFAULT '{{}}'""");

    // Asset Lifecycle
    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "PurchaseDate"   timestamp with time zone
        """);
    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "WarrantyExpiry" timestamp with time zone
        """);
    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "AssetTag"       text NOT NULL DEFAULT ''
        """);
    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "Location"       text NOT NULL DEFAULT ''
        """);
    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "SerialNumber"   text NOT NULL DEFAULT ''
        """);

    // Offline Alert Delay
    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "LastOfflineAlertAt" timestamp with time zone
        """);

    // Scheduled Commands
    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "DeviceCommands" ADD COLUMN IF NOT EXISTS "ScheduledFor" timestamp with time zone
        """);

    // Patch Management
    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "PendingUpdatesCount" integer NOT NULL DEFAULT 0
        """);
    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "LastWindowsUpdateInstalled" timestamp with time zone
        """);

    // Antivirus Alerts
    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "LastAvAlertAt" timestamp with time zone
        """);

    // Software Deployment
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "SoftwarePackages" (
            "Id"           uuid NOT NULL DEFAULT gen_random_uuid(),
            "Name"         text NOT NULL DEFAULT '',
            "Version"      text NOT NULL DEFAULT '',
            "Type"         text NOT NULL DEFAULT 'winget',
            "InstallCmd"   text NOT NULL DEFAULT '',
            "UninstallCmd" text,
            "Description"  text NOT NULL DEFAULT '',
            "CreatedBy"    text NOT NULL DEFAULT '',
            "CreatedAt"    timestamp with time zone NOT NULL DEFAULT now(),
            CONSTRAINT "PK_SoftwarePackages" PRIMARY KEY ("Id")
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "DeploymentJobs" (
            "Id"          uuid NOT NULL DEFAULT gen_random_uuid(),
            "PackageId"   uuid NOT NULL,
            "DeviceId"    uuid NOT NULL,
            "Status"      text NOT NULL DEFAULT 'Queued',
            "Output"      text,
            "CreatedBy"   text NOT NULL DEFAULT '',
            "CreatedAt"   timestamp with time zone NOT NULL DEFAULT now(),
            "ExecutedAt"  timestamp with time zone,
            CONSTRAINT "PK_DeploymentJobs" PRIMARY KEY ("Id"),
            CONSTRAINT "FK_DeploymentJobs_Packages" FOREIGN KEY ("PackageId")
                REFERENCES "SoftwarePackages" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_DeploymentJobs_Devices" FOREIGN KEY ("DeviceId")
                REFERENCES "Devices" ("Id") ON DELETE CASCADE
        )
        """);

    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS "IX_DeploymentJobs_DeviceId_Status"
            ON "DeploymentJobs" ("DeviceId", "Status")
        """);

    // Script Library
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "ScriptTemplates" (
            "Id"          uuid NOT NULL DEFAULT gen_random_uuid(),
            "Name"        text NOT NULL DEFAULT '',
            "Description" text NOT NULL DEFAULT '',
            "Script"      text NOT NULL DEFAULT '',
            "CreatedBy"   text NOT NULL DEFAULT '',
            "CreatedAt"   timestamp with time zone NOT NULL DEFAULT now(),
            CONSTRAINT "PK_ScriptTemplates" PRIMARY KEY ("Id")
        )
        """);

    // LDAP fields on Users table
    db.Database.ExecuteSqlRaw("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsLocal" boolean NOT NULL DEFAULT true""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LdapDn" text""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "DisplayName" text""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Email" text""");

    // Indexes on FK columns used in every device-detail and check-in query
    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS "IX_DeviceCheckins_DeviceId"
            ON "DeviceCheckins" ("DeviceId")
        """);
    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS "IX_InstalledSoftware_DeviceId"
            ON "InstalledSoftware" ("DeviceId")
        """);
    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS "IX_DeviceNotes_DeviceId"
            ON "DeviceNotes" ("DeviceId")
        """);
    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS "IX_DeviceCommands_DeviceId_Status"
            ON "DeviceCommands" ("DeviceId", "Status")
        """);
    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS "IX_SoftwareAlerts_DeviceId_AcknowledgedAt"
            ON "SoftwareAlerts" ("DeviceId", "AcknowledgedAt")
        """);
    db.Database.ExecuteSqlRaw("""
        CREATE INDEX IF NOT EXISTS "IX_AuditLogs_Timestamp"
            ON "AuditLogs" ("Timestamp" DESC)
        """);
    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Devices"
            ADD COLUMN IF NOT EXISTS "EventLogErrorsJson" text NOT NULL DEFAULT '[]'
        """);
    db.Database.ExecuteSqlRaw("""
        ALTER TABLE "Groups"
            ADD COLUMN IF NOT EXISTS "NotificationSettingsJson" text NULL
        """);

    tx.Commit();

    // Seed default software packages — adds missing entries by name, never overwrites existing
    var existingNames = db.SoftwarePackages.Select(p => p.Name).ToHashSet();
    var seedPackages = new[]
    {
        new HackITSentry.Server.Models.SoftwarePackage { Name = "Google Chrome",        Version = "latest", Type = "winget", InstallCmd = "Google.Chrome",                  Description = "Webbrowser von Google",                  CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "Mozilla Firefox",      Version = "latest", Type = "winget", InstallCmd = "Mozilla.Firefox",                Description = "Open-Source Webbrowser",                 CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "7-Zip",                Version = "latest", Type = "winget", InstallCmd = "7zip.7zip",                      Description = "Freie Archivierungssoftware",            CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "VLC Media Player",     Version = "latest", Type = "winget", InstallCmd = "VideoLAN.VLC",                   Description = "Universeller Medienabspieler",           CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "Notepad++",            Version = "latest", Type = "winget", InstallCmd = "Notepad++.Notepad++",            Description = "Erweiterter Texteditor",                 CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "Adobe Acrobat Reader", Version = "latest", Type = "winget", InstallCmd = "Adobe.Acrobat.Reader.64-bit",    Description = "PDF-Betrachter von Adobe",               CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "Microsoft Teams",      Version = "latest", Type = "winget", InstallCmd = "Microsoft.Teams",                Description = "Kommunikationsplattform von Microsoft",  CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "Zoom",                 Version = "latest", Type = "winget", InstallCmd = "Zoom.Zoom",                      Description = "Videokonferenz-Software",                CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "Visual Studio Code",   Version = "latest", Type = "winget", InstallCmd = "Microsoft.VisualStudioCode",     Description = "Code-Editor von Microsoft",              CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "KeePass",              Version = "latest", Type = "winget", InstallCmd = "DominikReichl.KeePass",          Description = "Open-Source Passwortmanager",            CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "Greenshot",            Version = "latest", Type = "winget", InstallCmd = "Greenshot.Greenshot",            Description = "Screenshot-Tool",                        CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "WinRAR",               Version = "latest", Type = "winget", InstallCmd = "RARLab.WinRAR",                  Description = "Archivierungssoftware",                  CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "PuTTY",                Version = "latest", Type = "winget", InstallCmd = "PuTTY.PuTTY",                    Description = "SSH- und Telnet-Client",                 CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "HWiNFO",               Version = "latest", Type = "winget", InstallCmd = "REALiX.HWiNFO",                  Description = "Hardware-Diagnose und Monitoring",       CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "Bitwarden",             Version = "latest", Type = "winget", InstallCmd = "Bitwarden.Bitwarden",             Description = "Open-Source Passwortmanager",            CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "WireGuard",             Version = "latest", Type = "winget", InstallCmd = "WireGuard.WireGuard",             Description = "Moderner, schneller VPN-Client",         CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "OpenVPN",               Version = "latest", Type = "winget", InstallCmd = "OpenVPNTechnologies.OpenVPN",     Description = "Open-Source VPN-Client",                 CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "TeamViewer",            Version = "latest", Type = "winget", InstallCmd = "TeamViewer.TeamViewer",           Description = "Remote-Desktop und Fernwartung",         CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "AnyDesk",               Version = "latest", Type = "winget", InstallCmd = "AnyDeskSoftwareGmbH.AnyDesk",    Description = "Schnelle Remote-Desktop-Software",       CreatedBy = "system" },
        new HackITSentry.Server.Models.SoftwarePackage { Name = "RustDesk",              Version = "latest", Type = "winget", InstallCmd = "RustDesk.RustDesk",              Description = "Open-Source Remote-Desktop (self-hosted)", CreatedBy = "system" },
    };
    var toAdd = seedPackages.Where(p => !existingNames.Contains(p.Name)).ToList();
    if (toAdd.Count > 0)
    {
        db.SoftwarePackages.AddRange(toAdd);
        db.SaveChanges();
    }

    // Bootstrap RuntimeSettings: env/appsettings first, then DB overrides
    var runtimeSettings = app.Services.GetRequiredService<RuntimeSettings>();
    runtimeSettings.LoadFromConfig(app.Configuration);
    var dbSettings = db.AppSettings.ToList();
    runtimeSettings.LoadFromDb(dbSettings);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseResponseCompression();
app.UseRateLimiter();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
