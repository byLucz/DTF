using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Discord.Commands;
using Discord.WebSocket;

namespace DiscordTelegramFrontier
{
    internal static class DiscordContextFactory
    {
        private static readonly Lazy<FieldInfo[]> Fields = new(() => new[] { "Client", "Guild", "Channel", "User", "Message" }
            .Select(name => typeof(SocketCommandContext).GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(SocketCommandContext).FullName, name)).ToArray());

        public static void Validate() => _ = Fields.Value;

        public static SocketCommandContext Create(DiscordSocketClient client, SocketGuild guild,
            FrontierProxyChannel channel, SocketGuildUser user)
        {
            var fields = Fields.Value;
            var context = (SocketCommandContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketCommandContext));
            fields[0].SetValue(context, client);
            fields[1].SetValue(context, guild);
            fields[2].SetValue(context, channel);
            fields[3].SetValue(context, user);
            fields[4].SetValue(context, null);
            return context;
        }
    }
}
