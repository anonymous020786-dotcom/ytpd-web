using Microsoft.EntityFrameworkCore;
using YtpdWeb.Api.Models;

namespace YtpdWeb.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<DownloadJob> Jobs => Set<DownloadJob>();
    public DbSet<DownloadJobItem> JobItems => Set<DownloadJobItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DownloadJob>()
            .HasMany(j => j.Items)
            .WithOne(i => i.Job)
            .HasForeignKey(i => i.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DownloadJob>().Property(j => j.Format).HasConversion<string>();
        modelBuilder.Entity<DownloadJobItem>().Property(i => i.Status).HasConversion<string>();
    }
}
