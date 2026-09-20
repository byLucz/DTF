using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types.Enums;

namespace DiscordTelegramFrontier
{
    internal sealed class FrontierUpdatePipeline
    {
        private readonly Dictionary<UpdateType, FrontierUpdateDelegate> _routes = new();
        private readonly FrontierUpdateDelegate _fallback;
        public UpdateType[] UpdateTypes { get; }

        public FrontierUpdatePipeline(IServiceProvider services, FrontierUpdateDelegate terminal)
        {
            var modules = services.GetServices<FrontierModuleRegistration>().GroupBy(r => r.ModuleType)
                .Select(group => group.First() with { UpdateTypes = group.SelectMany(r => r.UpdateTypes).Distinct().ToArray() }).ToArray();
            var middleware = services.GetServices<FrontierMiddlewareRegistration>().GroupBy(r => r.MiddlewareType)
                .Select(group => group.First() with
                {
                    UpdateTypes = group.Any(r => r.UpdateTypes.Length == 0) ? Array.Empty<UpdateType>()
                        : group.SelectMany(r => r.UpdateTypes).Distinct().ToArray()
                }).ToArray();
            UpdateTypes = modules.SelectMany(r => r.UpdateTypes).Concat(middleware.SelectMany(r => r.UpdateTypes)).Distinct().ToArray();
            foreach (var type in Enum.GetValues<UpdateType>()) _routes[type] = Build(type);
            _fallback = _routes[UpdateType.Unknown];

            FrontierUpdateDelegate Build(UpdateType type)
            {
                var applicable = modules.Where(r => r.UpdateTypes.Contains(type)).ToArray();
                FrontierUpdateDelegate pipeline = applicable.Length == 0 ? terminal
                    : context => DispatchAsync(applicable, context, terminal);
                for (var i = middleware.Length - 1; i >= 0; i--)
                {
                    var registration = middleware[i];
                    if (registration.UpdateTypes.Length != 0 && !registration.UpdateTypes.Contains(type)) continue;
                    var next = pipeline;
                    pipeline = context => registration.Resolve(context.Services).InvokeAsync(context, next);
                }
                return pipeline;
            }
        }

        public Task<bool> InvokeAsync(FrontierUpdateContext context)
            => (_routes.TryGetValue(context.Update.Type, out var route) ? route : _fallback)(context);

        private static async Task<bool> DispatchAsync(FrontierModuleRegistration[] modules,
            FrontierUpdateContext context, FrontierUpdateDelegate terminal)
        {
            foreach (var registration in modules)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (await registration.Resolve(context.Services).HandleAsync(context).ConfigureAwait(false)) return true;
            }
            return await terminal(context).ConfigureAwait(false);
        }
    }
}
