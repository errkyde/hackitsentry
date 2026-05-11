using HackITSentry.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace HackITSentry.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppSetting> AppSettings { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<DeviceGroup> Groups { get; set; }
    public DbSet<PendingDevice> PendingDevices { get; set; }
    public DbSet<Device> Devices { get; set; }
    public DbSet<DeviceCheckin> DeviceCheckins { get; set; }
    public DbSet<InstalledSoftware> InstalledSoftware { get; set; }
    public DbSet<LicenseInfo> LicenseInfos { get; set; }
    public DbSet<DeviceNote> DeviceNotes { get; set; }
    public DbSet<DeviceCommand> DeviceCommands { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<SoftwareBlacklistEntry> SoftwareBlacklist { get; set; }
    public DbSet<SoftwareAlert> SoftwareAlerts { get; set; }
    public DbSet<AgentVersion> AgentVersions { get; set; }
    public DbSet<InstallToken> InstallTokens { get; set; }
    public DbSet<DeviceNotificationOverride> DeviceNotificationOverrides { get; set; }
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions { get; set; }
    public DbSet<CustomFieldValue> CustomFieldValues { get; set; }
    public DbSet<DeployKey> DeployKeys { get; set; }
    public DbSet<ScriptTemplate> ScriptTemplates { get; set; }
    public DbSet<SoftwarePackage> SoftwarePackages { get; set; }
    public DbSet<DeploymentJob> DeploymentJobs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSetting>()
            .HasKey(s => s.Key);

        modelBuilder.Entity<Device>()
            .HasOne(d => d.Customer)
            .WithMany(c => c.Devices)
            .HasForeignKey(d => d.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Device>()
            .HasOne(d => d.Group)
            .WithMany(g => g.Devices)
            .HasForeignKey(d => d.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<LicenseInfo>()
            .HasOne(l => l.Device)
            .WithOne(d => d.License)
            .HasForeignKey<LicenseInfo>(l => l.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DeviceCheckin>()
            .HasOne(c => c.Device)
            .WithMany(d => d.Checkins)
            .HasForeignKey(c => c.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InstalledSoftware>()
            .HasOne(s => s.Device)
            .WithMany(d => d.Software)
            .HasForeignKey(s => s.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DeviceNote>()
            .HasOne(n => n.Device)
            .WithMany(d => d.Notes)
            .HasForeignKey(n => n.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DeviceCommand>()
            .HasOne(c => c.Device)
            .WithMany(d => d.Commands)
            .HasForeignKey(c => c.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SoftwareAlert>()
            .HasOne(a => a.Device)
            .WithMany(d => d.SoftwareAlerts)
            .HasForeignKey(a => a.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SoftwareAlert>()
            .HasOne(a => a.BlacklistEntry)
            .WithMany()
            .HasForeignKey(a => a.BlacklistEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DeviceNotificationOverride>()
            .HasOne(o => o.Device)
            .WithOne(d => d.NotificationOverride)
            .HasForeignKey<DeviceNotificationOverride>(o => o.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Device>()
            .HasIndex(d => d.AgentApiKey)
            .IsUnique();

        modelBuilder.Entity<PendingDevice>()
            .HasIndex(p => p.RegistrationToken)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<AgentVersion>()
            .HasIndex(v => v.Version)
            .IsUnique();

        modelBuilder.Entity<CustomFieldValue>()
            .HasOne(v => v.Definition)
            .WithMany(d => d.Values)
            .HasForeignKey(v => v.DefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CustomFieldValue>()
            .HasOne(v => v.Device)
            .WithMany()
            .HasForeignKey(v => v.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CustomFieldValue>()
            .HasIndex(v => new { v.DefinitionId, v.DeviceId })
            .IsUnique();

        modelBuilder.Entity<DeployKey>()
            .HasIndex(k => k.Key)
            .IsUnique();

        // Indexes on FK columns used in every device-detail query
        modelBuilder.Entity<DeviceCheckin>()
            .HasIndex(c => c.DeviceId);

        modelBuilder.Entity<InstalledSoftware>()
            .HasIndex(s => s.DeviceId);

        modelBuilder.Entity<DeviceNote>()
            .HasIndex(n => n.DeviceId);

        modelBuilder.Entity<DeviceCommand>()
            .HasIndex(c => new { c.DeviceId, c.Status });

        modelBuilder.Entity<SoftwareAlert>()
            .HasIndex(a => new { a.DeviceId, a.AcknowledgedAt });

        modelBuilder.Entity<AuditLog>()
            .HasIndex(l => l.Timestamp);

        modelBuilder.Entity<InstallToken>()
            .HasIndex(t => t.ExpiresAt);

        modelBuilder.Entity<DeploymentJob>()
            .HasOne(j => j.Package)
            .WithMany()
            .HasForeignKey(j => j.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DeploymentJob>()
            .HasOne(j => j.Device)
            .WithMany()
            .HasForeignKey(j => j.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DeploymentJob>()
            .HasIndex(j => new { j.DeviceId, j.Status });
    }
}
