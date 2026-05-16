using HITSight.Server.Data;
using HITSight.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace HITSight.Server.Services;

/// <summary>
/// Runs all idempotent DDL migrations against a tenant (or default) AppDbContext.
/// Called at startup for every known tenant DB and by TenantProvisioningService for new tenants.
/// </summary>
public static class DbMigrator
{
    public static async Task RunAsync(AppDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        using var tx = await db.Database.BeginTransactionAsync();

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "AppSettings" (
                "Key"   text NOT NULL,
                "Value" text NOT NULL,
                CONSTRAINT "PK_AppSettings" PRIMARY KEY ("Key")
            )
            """);

        await db.Database.ExecuteSqlRawAsync("""
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

        await db.Database.ExecuteSqlRawAsync("""
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

        await db.Database.ExecuteSqlRawAsync("""
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

        await db.Database.ExecuteSqlRawAsync("""
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

        await db.Database.ExecuteSqlRawAsync("""
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

        await db.Database.ExecuteSqlRawAsync("""
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

        await db.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AgentVersions_Version"
                ON "AgentVersions" ("Version")
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "LicenseInfos"
                ADD COLUMN IF NOT EXISTS "ExpiresAt" timestamp with time zone
            """);

        await db.Database.ExecuteSqlRawAsync("""
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

        await db.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_InstallTokens_Token"
                ON "InstallTokens" ("Token")
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "PendingDevices"
                ADD COLUMN IF NOT EXISTS "InvitedByUsername" text
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "PendingDevices"
                ADD COLUMN IF NOT EXISTS "DeployKeyName" text
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices"
                ADD COLUMN IF NOT EXISTS "RustDeskId" text NOT NULL DEFAULT ''
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices"
                ADD COLUMN IF NOT EXISTS "AgentVersion" text NOT NULL DEFAULT ''
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices"
                ADD COLUMN IF NOT EXISTS "LastDiskAlertAt" timestamp with time zone
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices"
                ADD COLUMN IF NOT EXISTS "DiskAlertAcknowledgedUsedPct" double precision
            """);

        await db.Database.ExecuteSqlRawAsync("""
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

        await db.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_DeviceNotificationOverrides_DeviceId"
                ON "DeviceNotificationOverrides" ("DeviceId")
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "DeviceNotificationOverrides"
                ADD COLUMN IF NOT EXISTS "OfflineAlertDelayMinutes" integer NULL
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "DeviceNotificationOverrides"
                ADD COLUMN IF NOT EXISTS "SourceGroupId" uuid NULL
            """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "CustomFieldDefinitions" (
                "Id"        uuid NOT NULL DEFAULT gen_random_uuid(),
                "Name"      text NOT NULL DEFAULT '',
                "SortOrder" integer NOT NULL DEFAULT 0,
                CONSTRAINT "PK_CustomFieldDefinitions" PRIMARY KEY ("Id")
            )
            """);

        await db.Database.ExecuteSqlRawAsync("""
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

        await db.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_CustomFieldValues_DefinitionId_DeviceId"
                ON "CustomFieldValues" ("DefinitionId", "DeviceId")
            """);

        await db.Database.ExecuteSqlRawAsync("""
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

        await db.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_DeployKeys_Key"
                ON "DeployKeys" ("Key")
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices"
                ADD COLUMN IF NOT EXISTS "RustDeskOptionsJson" text
            """);

        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "BiosInfoJson" text NOT NULL DEFAULT '{{}}'""");

        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "DefenderStatusJson" text NOT NULL DEFAULT '{{}}'""");

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "PurchaseDate"   timestamp with time zone
            """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "WarrantyExpiry" timestamp with time zone
            """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "AssetTag"       text NOT NULL DEFAULT ''
            """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "Location"       text NOT NULL DEFAULT ''
            """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "SerialNumber"   text NOT NULL DEFAULT ''
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "LastOfflineAlertAt" timestamp with time zone
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "DeviceCommands" ADD COLUMN IF NOT EXISTS "ScheduledFor" timestamp with time zone
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "PendingUpdatesCount" integer NOT NULL DEFAULT 0
            """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "LastWindowsUpdateInstalled" timestamp with time zone
            """);

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices" ADD COLUMN IF NOT EXISTS "LastAvAlertAt" timestamp with time zone
            """);

        await db.Database.ExecuteSqlRawAsync("""
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

        await db.Database.ExecuteSqlRawAsync("""
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

        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_DeploymentJobs_DeviceId_Status"
                ON "DeploymentJobs" ("DeviceId", "Status")
            """);

        await db.Database.ExecuteSqlRawAsync("""
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

        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsLocal" boolean NOT NULL DEFAULT true""");
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LdapDn" text""");
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "DisplayName" text""");
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Email" text""");

        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_DeviceCheckins_DeviceId"
                ON "DeviceCheckins" ("DeviceId")
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_InstalledSoftware_DeviceId"
                ON "InstalledSoftware" ("DeviceId")
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_DeviceNotes_DeviceId"
                ON "DeviceNotes" ("DeviceId")
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_DeviceCommands_DeviceId_Status"
                ON "DeviceCommands" ("DeviceId", "Status")
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_SoftwareAlerts_DeviceId_AcknowledgedAt"
                ON "SoftwareAlerts" ("DeviceId", "AcknowledgedAt")
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_AuditLogs_Timestamp"
                ON "AuditLogs" ("Timestamp" DESC)
            """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Devices"
                ADD COLUMN IF NOT EXISTS "EventLogErrorsJson" text NOT NULL DEFAULT '[]'
            """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Groups"
                ADD COLUMN IF NOT EXISTS "NotificationSettingsJson" text NULL
            """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Groups"
                ADD COLUMN IF NOT EXISTS "RustDeskOptionsJson" text NULL
            """);

        await tx.CommitAsync();
    }

    public static async Task SeedDefaultPackagesAsync(AppDbContext db)
    {
        var existingNames = db.SoftwarePackages.Select(p => p.Name).ToHashSet();
        var seedPackages = new[]
        {
            new SoftwarePackage { Name = "Google Chrome",        Version = "latest", Type = "winget", InstallCmd = "Google.Chrome",                  Description = "Webbrowser von Google",                    CreatedBy = "system" },
            new SoftwarePackage { Name = "Mozilla Firefox",      Version = "latest", Type = "winget", InstallCmd = "Mozilla.Firefox",                Description = "Open-Source Webbrowser",                   CreatedBy = "system" },
            new SoftwarePackage { Name = "7-Zip",                Version = "latest", Type = "winget", InstallCmd = "7zip.7zip",                      Description = "Freie Archivierungssoftware",              CreatedBy = "system" },
            new SoftwarePackage { Name = "VLC Media Player",     Version = "latest", Type = "winget", InstallCmd = "VideoLAN.VLC",                   Description = "Universeller Medienabspieler",             CreatedBy = "system" },
            new SoftwarePackage { Name = "Notepad++",            Version = "latest", Type = "winget", InstallCmd = "Notepad++.Notepad++",            Description = "Erweiterter Texteditor",                   CreatedBy = "system" },
            new SoftwarePackage { Name = "Adobe Acrobat Reader", Version = "latest", Type = "winget", InstallCmd = "Adobe.Acrobat.Reader.64-bit",    Description = "PDF-Betrachter von Adobe",                 CreatedBy = "system" },
            new SoftwarePackage { Name = "Microsoft Teams",      Version = "latest", Type = "winget", InstallCmd = "Microsoft.Teams",                Description = "Kommunikationsplattform von Microsoft",    CreatedBy = "system" },
            new SoftwarePackage { Name = "Zoom",                 Version = "latest", Type = "winget", InstallCmd = "Zoom.Zoom",                      Description = "Videokonferenz-Software",                  CreatedBy = "system" },
            new SoftwarePackage { Name = "Visual Studio Code",   Version = "latest", Type = "winget", InstallCmd = "Microsoft.VisualStudioCode",     Description = "Code-Editor von Microsoft",                CreatedBy = "system" },
            new SoftwarePackage { Name = "KeePass",              Version = "latest", Type = "winget", InstallCmd = "DominikReichl.KeePass",          Description = "Open-Source Passwortmanager",              CreatedBy = "system" },
            new SoftwarePackage { Name = "Greenshot",            Version = "latest", Type = "winget", InstallCmd = "Greenshot.Greenshot",            Description = "Screenshot-Tool",                          CreatedBy = "system" },
            new SoftwarePackage { Name = "WinRAR",               Version = "latest", Type = "winget", InstallCmd = "RARLab.WinRAR",                  Description = "Archivierungssoftware",                    CreatedBy = "system" },
            new SoftwarePackage { Name = "PuTTY",                Version = "latest", Type = "winget", InstallCmd = "PuTTY.PuTTY",                    Description = "SSH- und Telnet-Client",                   CreatedBy = "system" },
            new SoftwarePackage { Name = "HWiNFO",               Version = "latest", Type = "winget", InstallCmd = "REALiX.HWiNFO",                  Description = "Hardware-Diagnose und Monitoring",         CreatedBy = "system" },
            new SoftwarePackage { Name = "Bitwarden",             Version = "latest", Type = "winget", InstallCmd = "Bitwarden.Bitwarden",             Description = "Open-Source Passwortmanager",              CreatedBy = "system" },
            new SoftwarePackage { Name = "WireGuard",             Version = "latest", Type = "winget", InstallCmd = "WireGuard.WireGuard",             Description = "Moderner, schneller VPN-Client",           CreatedBy = "system" },
            new SoftwarePackage { Name = "OpenVPN",               Version = "latest", Type = "winget", InstallCmd = "OpenVPNTechnologies.OpenVPN",     Description = "Open-Source VPN-Client",                   CreatedBy = "system" },
            new SoftwarePackage { Name = "TeamViewer",            Version = "latest", Type = "winget", InstallCmd = "TeamViewer.TeamViewer",           Description = "Remote-Desktop und Fernwartung",           CreatedBy = "system" },
            new SoftwarePackage { Name = "AnyDesk",               Version = "latest", Type = "winget", InstallCmd = "AnyDeskSoftwareGmbH.AnyDesk",    Description = "Schnelle Remote-Desktop-Software",         CreatedBy = "system" },
            new SoftwarePackage { Name = "RustDesk",              Version = "latest", Type = "winget", InstallCmd = "RustDesk.RustDesk",              Description = "Open-Source Remote-Desktop (self-hosted)", CreatedBy = "system" },
        };
        var toAdd = seedPackages.Where(p => !existingNames.Contains(p.Name)).ToList();
        if (toAdd.Count > 0)
        {
            db.SoftwarePackages.AddRange(toAdd);
            await db.SaveChangesAsync();
        }
    }
}
