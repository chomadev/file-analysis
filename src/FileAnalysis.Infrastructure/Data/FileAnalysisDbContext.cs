using FileAnalysis.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FileAnalysis.Infrastructure.Data;

/// <summary>EF Core DbContext for the file analysis database.</summary>
public class FileAnalysisDbContext(DbContextOptions<FileAnalysisDbContext> options) : DbContext(options)
{
    public DbSet<IndexedFile> Files => Set<IndexedFile>();
    public DbSet<FileContent> FileContents => Set<FileContent>();
    public DbSet<FileAnalysisResult> FileAnalyses => Set<FileAnalysisResult>();
    public DbSet<DirectorySurveyEntry> DirectorySurveyEntries => Set<DirectorySurveyEntry>();
    public DbSet<CleanupSuggestion> CleanupSuggestions => Set<CleanupSuggestion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<IndexedFile>(e =>
        {
            e.ToTable("files");
            e.HasKey(f => f.Id);
            e.HasIndex(f => f.Path).IsUnique();
            e.HasIndex(f => f.Extension);
            e.HasIndex(f => f.IndexedAt);
            e.HasIndex(f => f.Md5Hash).HasFilter("\"Md5Hash\" IS NOT NULL");
            e.Property(f => f.Path).IsRequired();
            e.Property(f => f.Name).IsRequired();
            e.HasMany(f => f.Contents)
             .WithOne(c => c.File)
             .HasForeignKey(c => c.FileId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.Analysis).WithOne(a => a.File)
                .HasForeignKey<FileAnalysisResult>(a => a.FileId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FileContent>(e =>
        {
            e.ToTable("file_contents");
            e.HasKey(c => c.Id);
            e.Property(c => c.Content).IsRequired();
        });

        modelBuilder.Entity<FileAnalysisResult>(e =>
        {
            e.ToTable("file_analyses");
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.Category);
            e.Property(a => a.Tags).HasColumnType("text[]");
            e.Property(a => a.Topics).HasColumnType("text[]");
            e.Property(a => a.Embedding)
                .HasConversion(
                    new ValueConverter<float[]?, Vector?>(
                        v => v == null ? null : new Vector(v),
                        v => v == null ? null : v.ToArray()),
                    new ValueComparer<float[]?>(
                        (a1, a2) => (a1 ?? Array.Empty<float>()).SequenceEqual(a2 ?? Array.Empty<float>()),
                        v => v == null ? 0 : v.Aggregate(0, (h, x) => HashCode.Combine(h, x)),
                        v => v == null ? null : v.ToArray()))
                .HasColumnType("vector(768)");
        });

        modelBuilder.Entity<DirectorySurveyEntry>(e =>
        {
            e.ToTable("directory_survey_entries");
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.Path).IsUnique();
            e.HasIndex(s => s.ScanRootPath);
            e.Property(s => s.Path).IsRequired();
            e.Property(s => s.ScanRootPath).IsRequired();
            e.Property(s => s.TopExtensions).HasColumnType("text[]");
            e.Property(s => s.Classification).HasConversion<string>().HasMaxLength(20);
            e.Property(s => s.ClassificationSource).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<CleanupSuggestion>(e =>
        {
            e.ToTable("cleanup_suggestions");
            e.HasKey(s => s.Id);
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(s => s.Reason).HasConversion<string>().HasMaxLength(30);

            e.HasOne(s => s.File).WithOne()
                .HasForeignKey<CleanupSuggestion>(s => s.FileId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(s => s.DuplicateOf).WithMany()
                .HasForeignKey(s => s.DuplicateOfFileId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(s => s.Status);
            e.HasIndex(s => s.DuplicateOfFileId);
        });
    }
}
