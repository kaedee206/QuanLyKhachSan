using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Data
{
    public class SunHotelDbContext : DbContext
    {
        public SunHotelDbContext(DbContextOptions<SunHotelDbContext> options) : base(options) { }

        public DbSet<RoomType> RoomTypes => Set<RoomType>();
        public DbSet<Room> Rooms => Set<Room>();
        public DbSet<User> Users => Set<User>();
        public DbSet<Booking> Bookings => Set<Booking>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<Ticket> Tickets => Set<Ticket>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<EmailQueue> EmailQueues => Set<EmailQueue>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── RoomType ──────────────────────────────────────────────
            modelBuilder.Entity<RoomType>(entity =>
            {
                entity.HasIndex(e => e.Name);
            });

            // ── Room ─────────────────────────────────────────────────
            modelBuilder.Entity<Room>(entity =>
            {
                entity.HasIndex(e => e.RoomNumber).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.RoomTypeId);

                entity.HasOne(e => e.RoomType)
                      .WithMany(rt => rt.Rooms)
                      .HasForeignKey(e => e.RoomTypeId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ── User ─────────────────────────────────────────────────
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();

                // SQL Server: enum maps to int by default, explicit for clarity
                entity.Property(e => e.Role).HasConversion<int>();
            });

            // ── Booking ───────────────────────────────────────────────
            modelBuilder.Entity<Booking>(entity =>
            {
                entity.HasIndex(e => e.BookingCode).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => new { e.CheckInDate, e.CheckOutDate });
                entity.HasIndex(e => e.RoomId);

                entity.Property(e => e.Status).HasConversion<int>();

                entity.HasOne(e => e.Room)
                      .WithMany(r => r.Bookings)
                      .HasForeignKey(e => e.RoomId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.RoomType)
                      .WithMany(rt => rt.Bookings)
                      .HasForeignKey(e => e.RoomTypeId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ── Service ───────────────────────────────────────────────
            modelBuilder.Entity<Service>(entity =>
            {
                entity.Property(e => e.ServiceType).HasConversion<int>();

                entity.HasOne(e => e.Booking)
                      .WithMany(b => b.Services)
                      .HasForeignKey(e => e.BookingId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ── Invoice ───────────────────────────────────────────────
            modelBuilder.Entity<Invoice>(entity =>
            {
                entity.HasIndex(e => e.BookingId).IsUnique();
                entity.HasIndex(e => e.InvoiceNumber).IsUnique();
                entity.HasIndex(e => e.PaymentStatus);

                entity.Property(e => e.PaymentMethod).HasConversion<int>();
                entity.Property(e => e.PaymentStatus).HasConversion<int>();

                entity.HasOne(e => e.Booking)
                      .WithOne(b => b.Invoice)
                      .HasForeignKey<Invoice>(e => e.BookingId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Creator)
                      .WithMany(u => u.Invoices)
                      .HasForeignKey(e => e.CreatedById)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ── Ticket ────────────────────────────────────────────────
            modelBuilder.Entity<Ticket>(entity =>
            {
                entity.HasIndex(e => e.TicketNumber).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.RoomId);
                entity.HasIndex(e => e.Type);

                entity.Property(e => e.Type).HasConversion<int>();
                entity.Property(e => e.Priority).HasConversion<int>();
                entity.Property(e => e.Status).HasConversion<int>();

                entity.HasOne(e => e.Room)
                      .WithMany(r => r.Tickets)
                      .HasForeignKey(e => e.RoomId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Reporter)
                      .WithMany(u => u.ReportedTickets)
                      .HasForeignKey(e => e.ReportedById)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Assignee)
                      .WithMany(u => u.AssignedTickets)
                      .HasForeignKey(e => e.AssignedToId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // ── AuditLog ──────────────────────────────────────────────
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasIndex(e => new { e.EntityType, e.EntityId });
                entity.HasIndex(e => e.CreatedAt);

                entity.HasOne(e => e.User)
                      .WithMany(u => u.AuditLogs)
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // ── EmailQueue ────────────────────────────────────────────
            modelBuilder.Entity<EmailQueue>(entity =>
            {
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.CreatedAt);

                entity.Property(e => e.Status).HasConversion<int>();
            });
        }

        public override int SaveChanges()
        {
            UpdateTimestamps();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            UpdateTimestamps();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void UpdateTimestamps()
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Modified);

            foreach (var entry in entries)
            {
                var updatedAtProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "UpdatedAt");
                if (updatedAtProp != null)
                {
                    updatedAtProp.CurrentValue = DateTime.UtcNow;
                }
            }
        }
    }
}