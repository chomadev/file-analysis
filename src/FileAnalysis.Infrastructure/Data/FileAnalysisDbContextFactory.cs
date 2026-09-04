using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FileAnalysis.Infrastructure.Data;

/// <summary>Design-time factory for running EF Core migrations.</summary>
public class FileAnalysisDbContextFactory : IDesignTimeDbContextFactory<FileAnalysisDbContext>
{
    public FileAnalysisDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var connectionString = config.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=fileanalysis;Username=fa;Password=fa_dev_password";

        var optionsBuilder = new DbContextOptionsBuilder<FileAnalysisDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.UseVector());

        return new FileAnalysisDbContext(optionsBuilder.Options);
    }
}
