using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HudhudNestApi.Infrastructure.Persistence;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Must match Program.cs's startup switch (see MigrationCompletenessTests.cs for why):
        // without it, the design-time model differs from the shipped one on every
        // CreatedAt/UpdatedAt/DeletedAt column, and `dotnet ef migrations add` emits a wall of
        // spurious AlterColumn operations that do not reflect any real entity change.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var currentDirectory = Directory.GetCurrentDirectory();

        var apiProjectPath = Path.Combine(currentDirectory, "HudhudNestApi");

        if (!Directory.Exists(apiProjectPath))
        {
            apiProjectPath = Path.GetFullPath(
                Path.Combine(currentDirectory, "..", "HudhudNestApi"));
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.Exists(apiProjectPath)
                ? apiProjectPath
                : currentDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddJsonFile("appsettings.Testing.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? configuration["ConnectionStrings:DefaultConnection"]
            ?? Environment.GetEnvironmentVariable("DATABASE_URL");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString =
                "Host=localhost;Port=5432;Database=HudhudNestApi_DesignTime;Username=postgres;Password=postgres";
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        optionsBuilder.UseNpgsql(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}