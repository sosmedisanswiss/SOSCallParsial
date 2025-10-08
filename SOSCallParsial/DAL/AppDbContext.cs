using Microsoft.EntityFrameworkCore;
using SOSCallParsial.DAL.Entities;

namespace SOSCallParsial.DAL
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<AlarmLog> AlarmLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AlarmLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Account).IsRequired().HasMaxLength(50);
                entity.Property(e => e.EventCode).HasMaxLength(10);
                entity.Property(e => e.GroupCode).HasMaxLength(10);
                entity.Property(e => e.ZoneCode).HasMaxLength(10);
                entity.Property(e => e.PhoneNumber).HasMaxLength(30);
                entity.Property(e => e.Timestamp).IsRequired();
                entity.Property(e => e.RawMessage).HasMaxLength(1000);
            });
        }
    }

}
