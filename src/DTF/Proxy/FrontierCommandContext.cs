using Discord;
using Discord.Commands;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierCommandContext : ICommandContext
    {
        public IDiscordClient Client { get; }
        public IGuild Guild { get; }
        public IMessageChannel Channel { get; }
        public IUser User { get; }
        public IUserMessage Message { get; }

        public FrontierCommandContext(IDiscordClient client, IGuild guild, IMessageChannel channel, IUser user, IUserMessage message)
        {
            Client = client;
            Guild = guild;
            Channel = channel;
            User = user;
            Message = message;
        }
    }
}
