using HackITSentry.Server.Data;
using HackITSentry.Server.Models;
using HackITSentry.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<LicenseEncryptionService>();
builder.Services.AddSingleton<RuntimeSettings>();
builder.Services.AddSingleton<AlertEmailService>();
builder.Services.AddHostedService<DeviceOfflineAlertService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddHttpContextAccessor();

var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

// Initialize DB, seed admin, load runtime settings
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

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

    if (!db.Users.Any())
    {
        db.Users.Add(new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
            Role = "Admin"
        });
        db.SaveChanges();
        Console.WriteLine("Created default admin user: admin / admin");
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

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
