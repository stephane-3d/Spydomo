using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static void AddBrightDataService(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpClient<BrightDataService>();

            services.AddScoped<IBrightDataService, BrightDataService>();

            services.AddScoped<BrightDataService>(provider =>
            new BrightDataService(
                provider.GetRequiredService<IHttpClientFactory>(),
                provider.GetRequiredService<ILogger<BrightDataService>>(),
                configuration,
                provider.GetRequiredService<IDbContextFactory<SpydomoContext>>()
            ));
        }
    }
}
