using System;
using System.Linq;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace DiscordTelegramFrontier
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddFrontier(this IServiceCollection services, Action<FrontierOptions> configure = null)
        {
            var opts = new FrontierOptions();
            configure?.Invoke(opts);
            services.AddSingleton(opts);
            AddService(services);
            return services;
        }

        public static IServiceCollection AddFrontier(this IServiceCollection services, Action<IServiceProvider, FrontierOptions> configure)
        {
            services.AddSingleton(provider =>
            {
                var opts = new FrontierOptions();
                configure?.Invoke(provider, opts);
                return opts;
            });
            AddService(services);
            return services;
        }

        public static IServiceCollection AddFrontierModule<T>(this IServiceCollection services, params UpdateType[] updateTypes)
            where T : class, IFrontierModule
        {
            var types = ValidateUpdateTypes(updateTypes);
            services.TryAddScoped<T>();
            services.AddSingleton(new FrontierModuleRegistration(typeof(T), types.Length == 0 ? new[] { UpdateType.Message } : types,
                provider => provider.GetRequiredService<T>()));
            return services;
        }

        public static IServiceCollection AddFrontierMiddleware<T>(this IServiceCollection services, params UpdateType[] updateTypes)
            where T : class, IFrontierMiddleware
        {
            var types = ValidateUpdateTypes(updateTypes);
            services.TryAddScoped<T>();
            services.AddSingleton(new FrontierMiddlewareRegistration(typeof(T), types, provider => provider.GetRequiredService<T>()));
            return services;
        }

        private static UpdateType[] ValidateUpdateTypes(UpdateType[] updateTypes)
        {
            if (updateTypes == null) throw new ArgumentNullException(nameof(updateTypes));
            if (updateTypes.Any(type => type == UpdateType.Unknown || !Enum.IsDefined(type)))
                throw new ArgumentException("Use supported Telegram update types.", nameof(updateTypes));
            return updateTypes.Distinct().ToArray();
        }

        private static void AddService(IServiceCollection services)
            => services.TryAddSingleton(provider => new FrontierService(
                provider.GetService<DiscordSocketClient>(), provider.GetService<CommandService>(), provider,
                provider.GetRequiredService<FrontierOptions>(), provider.GetService<ITelegramBotClient>()));
    }
}
