using System;
using Telegram.Bot.Types.Enums;

namespace DiscordTelegramFrontier
{
    internal sealed record FrontierModuleRegistration(Type ModuleType, UpdateType[] UpdateTypes,
        Func<IServiceProvider, IFrontierModule> Resolve);

    internal sealed record FrontierMiddlewareRegistration(Type MiddlewareType, UpdateType[] UpdateTypes,
        Func<IServiceProvider, IFrontierMiddleware> Resolve);
}
