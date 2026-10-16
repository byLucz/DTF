using Discord;
using Discord.Commands;
using System.Threading;
using System.Threading.Tasks;

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

        public static Task<byte[]> IncomingImageAsync(this ICommandContext context, int maxBytes, CancellationToken cancellationToken = default)
            => context?.Channel is FrontierProxyChannel channel
                ? channel.DownloadImageAsync(maxBytes, cancellationToken)
                : Task.FromResult<byte[]>(null);
    }
}
