using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierUserTypeReader : TypeReader
    {
        private readonly UserTypeReader<IUser> _discord = new();

        public override async Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
        {
            if (!FrontierUsers.TryGetState(context, out var state))
                return await _discord.ReadAsync(context, input, services).ConfigureAwait(false);

            if (!FrontierUsers.TryFindMention(state, input, out var telegram, out var name))
                return TypeReaderResult.FromError(CommandError.ParseFailed, $"{name} — не юзернейм, укажи пользователя через @упоминание");

            return TypeReaderResult.FromSuccess((IUser)FrontierUsers.Linked(state, telegram) ?? FrontierUsers.Proxy(telegram, name));
        }
    }

    public static class FrontierCommandServiceExtensions
    {
        public static CommandService AddFrontierUserReaders(this CommandService commands)
        {
            ArgumentNullException.ThrowIfNull(commands);
            commands.AddTypeReader<IUser>(new FrontierUserTypeReader(), true);
            return commands;
        }
    }
}
