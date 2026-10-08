using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using Discord.Commands;
using Discord.WebSocket;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DiscordTelegramFrontier
{
    internal static class FrontierUsers
    {
        private static readonly ConcurrentDictionary<string, User> Known = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConditionalWeakTable<ICommandContext, CommandState> States = new();

        internal sealed record CommandState(Message Message, SocketGuild Guild, FrontierOptions Options);

        public static void Remember(Message message)
        {
            if (message is null)
                return;

            Remember(message.From);
            Remember(message.ReplyToMessage?.From);

            foreach (var entity in message.Entities ?? Array.Empty<MessageEntity>())
                Remember(entity.User);
        }

        private static void Remember(User user)
        {
            if (user is { IsBot: false, Username: { Length: > 0 } username })
                Known[username] = user;
        }

        public static void Attach(ICommandContext context, Message message, SocketGuild guild, FrontierOptions options)
            => States.AddOrUpdate(context, new CommandState(message, guild, options));

        public static bool TryGetState(ICommandContext context, out CommandState state)
            => States.TryGetValue(context, out state);

        public static bool TryFindMention(CommandState state, string input, out User user, out string name)
        {
            user = null;
            name = input.Trim();

            var message = state.Message;
            var text = message.Text ?? string.Empty;

            foreach (var entity in message.Entities ?? Array.Empty<MessageEntity>())
            {
                if (entity.Type is not (MessageEntityType.Mention or MessageEntityType.TextMention))
                    continue;

                if (entity.Offset < 0 || entity.Offset + entity.Length > text.Length)
                    continue;

                if (!string.Equals(text.Substring(entity.Offset, entity.Length), name, StringComparison.Ordinal))
                    continue;

                user = entity.Type == MessageEntityType.TextMention
                    ? entity.User
                    : Known.TryGetValue(name.TrimStart('@'), out var known) ? known : null;

                return true;
            }

            return false;
        }

        public static SocketGuildUser Linked(CommandState state, User user)
        {
            if (state.Guild is null || user is null)
                return null;

            return state.Options.UserToDiscord.TryGetValue(user.Id, out var discordId)
                ? state.Guild.GetUser(discordId)
                : null;
        }

        public static FrontierUser Sender(CommandState state)
        {
            var message = state.Message;

            if (message.From is { } from)
                return Proxy(from, from.Username);

            return message.SenderChat is { } chat
                ? new FrontierUser(chat.Id, chat.Username, chat.Title)
                : null;
        }

        public static FrontierUser Proxy(User user, string name)
        {
            if (user is null)
                return new FrontierUser(null, name, null);

            var display = string.Join(" ", new[] { user.FirstName, user.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
            return new FrontierUser(user.Id, user.Username, string.IsNullOrWhiteSpace(display) ? name : display);
        }
    }
}
