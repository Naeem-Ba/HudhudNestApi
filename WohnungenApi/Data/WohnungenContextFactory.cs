using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace WohnungenApi.Data
{
    public class WohnungenContextFactory
        : IDesignTimeDbContextFactory<WohnungenContext>
    {
        public WohnungenContext CreateDbContext(string[] args)
        {
            var basePath = Directory.GetCurrentDirectory();

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .Build();

            var connectionString =
                configuration.GetConnectionString("DefaultConnection");

            var optionsBuilder =
                new DbContextOptionsBuilder<WohnungenContext>();

            optionsBuilder.UseNpgsql(connectionString);

            return new WohnungenContext(optionsBuilder.Options);
        }
    }
}