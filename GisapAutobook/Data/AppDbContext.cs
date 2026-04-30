using GisapAutobook.Models;
using Microsoft.EntityFrameworkCore;

namespace GisapAutobook.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<BookingLog> BookingLogs => Set<BookingLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Schedule>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).IsRequired().HasMaxLength(200);
            e.Property(s => s.ResourceId).IsRequired().HasMaxLength(50);
            // Store TimeSpan as total minutes (long) for SQLite compatibility
            e.Property(s => s.TriggerTime)
             .HasConversion(
                 v => (long)v.TotalMinutes,
                 v => TimeSpan.FromMinutes(v));
        });

        modelBuilder.Entity<BookingLog>(e =>
        {
            e.HasKey(l => l.Id);
            e.HasOne(l => l.Schedule)
             .WithMany(s => s.BookingLogs)
             .HasForeignKey(l => l.ScheduleId)
             .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
