using SphereBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace SphereBackend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Board> Boards { get; set; }
        public DbSet<Column> Columns { get; set; }
        public DbSet<Card> Cards { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<UserSession> UserSessions { get; set; }
        public DbSet<BoardMember> BoardMembers { get; set; }

        private static string NormalizeForStorage(string value)
        {
            return value?.Trim() ?? string.Empty;
        }

        private static string NormalizeForLookup(string value)
        {
            return NormalizeForStorage(value).ToLowerInvariant();
        }

        private void NormalizeTrackedUsers()
        {
            foreach (var entry in ChangeTracker.Entries<User>())
            {
                if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
                    continue;

                entry.Entity.Username = NormalizeForStorage(entry.Entity.Username);
                entry.Entity.Email = NormalizeForStorage(entry.Entity.Email);
                entry.Entity.NormalizedUsername = NormalizeForLookup(entry.Entity.Username);
                entry.Entity.NormalizedEmail = NormalizeForLookup(entry.Entity.Email);
            }
        }

        public override int SaveChanges()
        {
            NormalizeTrackedUsers();
            return base.SaveChanges();
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            NormalizeTrackedUsers();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            NormalizeTrackedUsers();
            return base.SaveChangesAsync(cancellationToken);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            NormalizeTrackedUsers();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<BoardMember>()
                .HasKey(bm => new { bm.UserId, bm.BoardId });

            modelBuilder.Entity<BoardMember>()
                .HasOne(bm => bm.User)
                .WithMany(u => u.SharedBoards)
                .HasForeignKey(bm => bm.UserId);

            modelBuilder.Entity<BoardMember>()
                .HasOne(bm => bm.Board)
                .WithMany(b => b.Members)
                .HasForeignKey(bm => bm.BoardId);

            modelBuilder.Entity<BoardMember>()
                .HasIndex(bm => new { bm.UserId, bm.Order })
                .HasDatabaseName("IX_BoardMembers_UserId_Order")
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.NormalizedUsername)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.NormalizedEmail)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => new { u.IsDemo, u.CreatedAt })
                .HasDatabaseName("IX_Users_IsDemo_CreatedAt");

            modelBuilder.Entity<UserSession>()
                .HasIndex(s => s.RefreshTokenHash)
                .IsUnique();

            modelBuilder.Entity<Column>()
                .HasIndex(c => new { c.BoardId, c.Order })
                .HasDatabaseName("IX_Columns_BoardId_Order_Active")
                .HasFilter("\"ArchivedAt\" IS NULL")
                .IsUnique();

            modelBuilder.Entity<Card>()
                .HasIndex(c => new { c.ColumnId, c.Order })
                .HasDatabaseName("IX_Cards_ColumnId_Order_Active")
                .HasFilter("\"ArchivedAt\" IS NULL AND \"ColumnId\" IS NOT NULL")
                .IsUnique();
        }
    }
}
