using System;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordTelegramFrontier
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddFrontier(this IServiceCollection services, Action<FrontierOptions> configure)
        {
            var opts = new FrontierOptions();
            configure?.Invoke(opts);
            services.AddSingleton(opts);
            services.AddSingleton<FrontierService>();
            return services;
        }
    }
}
