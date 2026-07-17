using Microsoft.EntityFrameworkCore;
using Project_OLMS.Models;

namespace Project_OLMS.Data
{
    public class AppDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<AppDbContext> options)
        : Microsoft.EntityFrameworkCore.DbContext(options)
    {
        public DbSet<Auth> Users { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<ExamQuestionnaire> ExamQuestionnaires { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure the Id property as the Auto-Increment Primary Key
            modelBuilder.Entity<Auth>().HasKey(a => a.Id);

            modelBuilder.Entity<Auth>()
                .Property(a => a.Id)
                .ValueGeneratedOnAdd();

            // ── Document entity configuration ────────────────────────────
            modelBuilder.Entity<Document>().HasKey(d => d.Id);

            modelBuilder.Entity<Document>()
                .Property(d => d.Id)
                .ValueGeneratedOnAdd();

            // Optional FK: a document belongs to a user (nullable — no cascade delete)
            modelBuilder.Entity<Document>()
                .HasOne(d => d.UploadedByUser)
                .WithMany()
                .HasForeignKey(d => d.UploadedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Index to speed up "get documents by user" queries
            modelBuilder.Entity<Document>()
                .HasIndex(d => d.UploadedByUserId);

            // ── ExamQuestionnaire entity configuration ───────────────────
            modelBuilder.Entity<ExamQuestionnaire>().HasKey(e => e.Id);

            modelBuilder.Entity<ExamQuestionnaire>()
                .Property(e => e.Id)
                .ValueGeneratedOnAdd();

            // FK: an exam belongs to a document; delete exam when document is deleted
            modelBuilder.Entity<ExamQuestionnaire>()
                .HasOne(e => e.Document)
                .WithMany()
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index to speed up "get exams by document" queries
            modelBuilder.Entity<ExamQuestionnaire>()
                .HasIndex(e => e.DocumentId);

            base.OnModelCreating(modelBuilder);
        }
    }
}