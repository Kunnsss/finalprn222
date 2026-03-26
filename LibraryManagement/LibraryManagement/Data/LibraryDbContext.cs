// Data/LibraryDbContext.cs - FIX TRIGGER CHO SYSTEM ANNOUNCEMENTS
using LibraryManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.Data
{
    public class LibraryDbContext : DbContext
    {
        public LibraryDbContext(DbContextOptions<LibraryDbContext> options) : base(options)
        {
        }

        // Bảng gốc
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Book> Books { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<RentalTransaction> RentalTransactions { get; set; }
        public DbSet<OnlineRentalTransaction> OnlineRentalTransactions { get; set; }
        public DbSet<LateFeeConfig> LateFeeConfigs { get; set; }
        public DbSet<Notification> Notifications { get; set; }

        // Bảng mới 
        public DbSet<BookReservation> BookReservations { get; set; }
        public DbSet<BookReview> BookReviews { get; set; }
        public DbSet<Wishlist> Wishlists { get; set; }
        public DbSet<SupportChat> SupportChats { get; set; }
        public DbSet<ReadingHistory> ReadingHistory { get; set; }
        public DbSet<BookSuggestion> BookSuggestions { get; set; }
        public DbSet<SystemAnnouncement> SystemAnnouncements { get; set; }
        public DbSet<LibraryEvent> LibraryEvents { get; set; }
        public DbSet<EventRegistration> EventRegistrations { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<LateFeeConfig>().ToTable("LateFeeConfig");

            // ========================================
            //  Bảng có trigger - Disable OUTPUT Clause
            // ========================================
            modelBuilder.Entity<BookReview>()
                .ToTable(tb => tb.UseSqlOutputClause(false));

            // Fix trigger cho Event tables
            modelBuilder.Entity<LibraryEvent>()
                .ToTable(tb => tb.UseSqlOutputClause(false));

            modelBuilder.Entity<EventRegistration>()
                .ToTable(tb => tb.UseSqlOutputClause(false));

            // FIX MỚI: SystemAnnouncements (có thể có trigger)
            modelBuilder.Entity<SystemAnnouncement>()
                .ToTable(tb => tb.UseSqlOutputClause(false));

            // Configure relationships and indexes 
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<Book>()
                .HasIndex(b => b.ISBN);

            // Configure indexes cho bảng 
            modelBuilder.Entity<BookReservation>()
                .HasIndex(br => br.UserId);

            modelBuilder.Entity<BookReservation>()
                .HasIndex(br => br.BookId);

            modelBuilder.Entity<BookReservation>()
                .HasIndex(br => br.Status);

            modelBuilder.Entity<BookReview>()
                .HasIndex(br => br.BookId);

            modelBuilder.Entity<BookReview>()
                .HasIndex(br => br.UserId);

            modelBuilder.Entity<Wishlist>()
                .HasIndex(w => w.UserId);

            modelBuilder.Entity<SupportChat>()
                .HasIndex(sc => sc.UserId);

            modelBuilder.Entity<SupportChat>()
                .HasIndex(sc => sc.IsRead);

            modelBuilder.Entity<ReadingHistory>()
                .HasIndex(rh => rh.UserId);

            modelBuilder.Entity<ReadingHistory>()
                .HasIndex(rh => rh.BookId);

            // Indexes cho Event tables
            modelBuilder.Entity<LibraryEvent>()
                .HasIndex(e => e.EventDate);

            modelBuilder.Entity<LibraryEvent>()
                .HasIndex(e => e.IsActive);

            modelBuilder.Entity<EventRegistration>()
                .HasIndex(er => er.EventId);

            modelBuilder.Entity<EventRegistration>()
                .HasIndex(er => er.UserId);

            modelBuilder.Entity<EventRegistration>()
                .HasIndex(er => er.Status);

            // FIX MỚI: Indexes cho SystemAnnouncements
            modelBuilder.Entity<SystemAnnouncement>()
                .HasIndex(a => a.IsActive);

            modelBuilder.Entity<SystemAnnouncement>()
                .HasIndex(a => a.CreatedDate);

            modelBuilder.Entity<SystemAnnouncement>()
                .HasIndex(a => a.ExpiryDate);

            // Configure relationships
            modelBuilder.Entity<BookReservation>()
                .HasOne(br => br.User)
                .WithMany()
                .HasForeignKey(br => br.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<BookReservation>()
                .HasOne(br => br.Book)
                .WithMany()
                .HasForeignKey(br => br.BookId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<BookReview>()
                .HasOne(br => br.Book)
                .WithMany()
                .HasForeignKey(br => br.BookId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<BookReview>()
                .HasOne(br => br.User)
                .WithMany()
                .HasForeignKey(br => br.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Wishlist>()
                .HasOne(w => w.User)
                .WithMany()
                .HasForeignKey(w => w.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Wishlist>()
                .HasOne(w => w.Book)
                .WithMany()
                .HasForeignKey(w => w.BookId)
                .OnDelete(DeleteBehavior.Restrict);

            // Relationships cho Event tables
            modelBuilder.Entity<LibraryEvent>()
                .HasOne(e => e.Creator)
                .WithMany()
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<EventRegistration>()
                .HasOne(er => er.Event)
                .WithMany()
                .HasForeignKey(er => er.EventId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<EventRegistration>()
                .HasOne(er => er.User)
                .WithMany()
                .HasForeignKey(er => er.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // FIX MỚI: Relationships cho SystemAnnouncements
            modelBuilder.Entity<SystemAnnouncement>()
                .HasOne(a => a.Creator)
                .WithMany()
                .HasForeignKey(a => a.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}