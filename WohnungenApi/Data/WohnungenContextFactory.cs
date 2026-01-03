using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace WohnungenApi.Data
{
    public class WohnungenContextFactory : IDesignTimeDbContextFactory<WohnungenContext>
    {
        public WohnungenContext CreateDbContext(string[] args)
        {
            // إعداد قراءة ملف appsettings.json لكي يعرف البرنامج نص الاتصال أثناء الـ Migration
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            var optionsBuilder = new DbContextOptionsBuilder<WohnungenContext>();

            // جلب نص الاتصال من الملف
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            // تأكد من استخدام UseNpgsql بدلاً من UseSqlServer
            optionsBuilder.UseNpgsql(connectionString);

            return new WohnungenContext(optionsBuilder.Options);
        }
    }
}