using LaGrandeurSpa.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<SpaService> SpaServices => Set<SpaService>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingSlot> BookingSlots => Set<BookingSlot>();
    public DbSet<BusinessHour> BusinessHours => Set<BusinessHour>();
    public DbSet<BlockedPeriod> BlockedPeriods => Set<BlockedPeriod>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.Property(x => x.Role).HasConversion<string>();
            entity.Property(x => x.FullName).HasMaxLength(160);
            entity.Property(x => x.Email).HasMaxLength(220);
            entity.Property(x => x.MobileNumber).HasMaxLength(40);
            entity.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<SpaService>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(180);
            entity.Property(x => x.ColorHex).HasMaxLength(16);
            entity.Property(x => x.Price).HasPrecision(10, 2);
            entity.HasIndex(x => new { x.Name, x.DurationMinutes }).IsUnique();
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.ReferenceNumber).HasMaxLength(32);
            entity.Property(x => x.CustomerNote).HasMaxLength(1000);
            entity.Property(x => x.AdminNote).HasMaxLength(1000);
            entity.HasIndex(x => x.ReferenceNumber).IsUnique();
            entity.HasIndex(x => x.StartLocal);
            entity.Property(x => x.InvoiceNumber)
                .HasMaxLength(40);

            entity.Property(x => x.InvoiceServiceName)
                .HasMaxLength(180);

            entity.Property(x => x.InvoiceAmount)
                .HasPrecision(10, 2);

            entity.HasIndex(x => x.InvoiceNumber)
                .IsUnique();

            entity.Property(x => x.RowVersion)
                .IsRowVersion();

            entity.HasOne(x => x.Customer)
                .WithMany(x => x.Bookings)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.SpaService)
                .WithMany(x => x.Bookings)
                .HasForeignKey(x => x.SpaServiceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ActionedBy)
                .WithMany()
                .HasForeignKey(x => x.ActionedById)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<BookingSlot>(entity =>
        {
            entity.HasIndex(x => x.SlotStartLocal).IsUnique();
            entity.HasOne(x => x.Booking)
                .WithMany(x => x.Slots)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BusinessHour>(entity =>
        {
            entity.Property(x => x.DayOfWeek).HasConversion<int>();
            entity.HasIndex(x => x.DayOfWeek).IsUnique();
        });

        modelBuilder.Entity<BlockedPeriod>(entity =>
        {
            entity.Property(x => x.Reason).HasMaxLength(240);
            entity.HasIndex(x => x.StartLocal);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.Property(x => x.Type).HasConversion<string>();
            entity.Property(x => x.Title).HasMaxLength(180);
            entity.Property(x => x.Message).HasMaxLength(1200);
            entity.Property(x => x.Url).HasMaxLength(400);
            entity.HasIndex(x => new { x.AppUserId, x.IsRead });
            entity.HasOne(x => x.AppUser)
                .WithMany(x => x.Notifications)
                .HasForeignKey(x => x.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Booking)
                .WithMany()
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
