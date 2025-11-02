using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore;

namespace WohnungenApi.Data
{
    public class WohnungenContextFactory : IDesignTimeDbContextFactory<WohnungenContext>
    {
        public WohnungenContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<WohnungenContext>();
            optionsBuilder.UseSqlServer(
                "Server=DESKTOP-EKKIH4K\\SQLEXPRESS;Database=ImmobilienDB;Trusted_Connection=True;TrustServerCertificate=True;"
            );

            return new WohnungenContext(optionsBuilder.Options);
        }
    }
}
