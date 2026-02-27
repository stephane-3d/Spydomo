using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Spydomo.Models
{
    public class SpydomoContextFactory : IDesignTimeDbContextFactory<SpydomoContext>
    {
        public SpydomoContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<SpydomoContext>();

            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

            // Load configuration manually (EF doesn't have access to Program.cs)
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory()) // Ensures correct path
                .AddJsonFile($"appsettings.{env}.json", optional: true)
                .Build();

            var connectionString = configuration.GetConnectionString("SpydomoDB");
            optionsBuilder.UseSqlServer(connectionString);

            return new SpydomoContext(optionsBuilder.Options);
        }
    }
}
