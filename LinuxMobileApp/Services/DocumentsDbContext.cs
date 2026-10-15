using Microsoft.EntityFrameworkCore;

namespace LinuxMobileApp.Services;

public sealed class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options) : DbContext(options)
{
    public DbSet<DocumentRecord> Documents => Set<DocumentRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var document = modelBuilder.Entity<DocumentRecord>();
        document.HasKey(item => item.Id);
        document.Property(item => item.FileName).HasMaxLength(255).IsRequired();
        document.Property(item => item.Extension).HasMaxLength(10).IsRequired();
        document.Property(item => item.ContentType).HasMaxLength(100).IsRequired();
        document.Property(item => item.AccessTokenHash).HasMaxLength(64).IsRequired();
        document.HasIndex(item => new { item.Id, item.AccessTokenHash }).IsUnique();
    }
}
