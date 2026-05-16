using HITSight.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace HITSight.Server.Data;

public class PlatformDbContext : DbContext
{
    public PlatformDbContext(DbContextOptions<PlatformDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<SuperAdminUser> SuperAdminUsers { get; set; }
    public DbSet<TenantExtension> TenantExtensions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>()
            .HasIndex(t => t.Slug)
            .IsUnique();

        modelBuilder.Entity<SuperAdminUser>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<TenantExtension>()
            .HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TenantExtension>()
            .HasIndex(e => e.TenantId);
    }
}
