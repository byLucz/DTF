using Discord;
using Discord.Commands;

namespace DiscordTelegramFrontier
{
    public static class FrontierContextExtensions
    {
        public static IUser Sender(this ICommandContext context)
        {
            if (context?.User is { } user)
                return user;

            return context is not null && FrontierUsers.TryGetState(context, out var state)
                ? FrontierUsers.Sender(state)
                : null;
        }
    }
}
