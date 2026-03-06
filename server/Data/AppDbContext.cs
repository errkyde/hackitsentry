using HackITSentry.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace HackITSentry.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<DeviceGroup> Groups { get; set; }
    public DbSet<PendingDevice> PendingDevices { get; set; }
    public DbSet<Device> Devices { get; set; }
    public DbSet<DeviceCheckin> DeviceCheckins { get; set; }
    public DbSet<InstalledSoftware> InstalledSoftware { get; set; }
    public DbSet<LicenseInfo> LicenseInfos { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

        modelBuilder.Entity<Device>()
            .HasIndex(d => d.AgentApiKey)
            .IsUnique();

        modelBuilder.Entity<PendingDevice>()
            .HasIndex(p => p.RegistrationToken)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();
    }
}
