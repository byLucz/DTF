using Discord;
using Discord.WebSocket;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DiscordTelegramFrontier
{
    internal static class FrontierMentions
    {
        private static readonly Regex Token = new(@"<@!?(?<user>\d+)>|<@&(?<role>\d+)>|<#(?<channel>\d+)>|<t:(?<time>-?\d+)(?::(?<style>[tTdDfFR]))?>",
            RegexOptions.Compiled);
        private static readonly Regex Markdown = new(@"([\\*_~|`<>\[\]()#])", RegexOptions.Compiled);

        public static Func<string, string> Resolver(DiscordSocketClient client, SocketGuild guild, FrontierOptions options)
            => text => Resolve(text, client, guild, options);

        public static string Resolve(string text, DiscordSocketClient client, SocketGuild guild, FrontierOptions options)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0)
                return text;

            return Token.Replace(text, match =>
            {
                if (match.Groups["user"].Success && ulong.TryParse(match.Groups["user"].Value, out var userId))
                    return "@" + Escape(UserName(userId, client, guild, options));

                if (match.Groups["role"].Success && ulong.TryParse(match.Groups["role"].Value, out var roleId))
                    return "@" + Escape(guild?.GetRole(roleId)?.Name ?? "роль");

                if (match.Groups["channel"].Success && ulong.TryParse(match.Groups["channel"].Value, out var channelId))
                    return "#" + Escape((client?.GetChannel(channelId) as IChannel)?.Name ?? "канал");

                if (match.Groups["time"].Success && long.TryParse(match.Groups["time"].Value, out var unix))
                    return Time(DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime(), match.Groups["style"].Value);

                return match.Value;
            });
        }

        public static Embed Rewrite(Embed embed, Func<string, string> resolve)
        {
            var builder = embed.ToEmbedBuilder();
            if (builder.Author != null) builder.Author.Name = resolve(builder.Author.Name);
            builder.Title = resolve(builder.Title);
            builder.Description = resolve(builder.Description);
            foreach (var field in builder.Fields)
            {
                field.Name = resolve(field.Name);
                field.Value = resolve(field.Value?.ToString());
            }
            if (builder.Footer != null) builder.Footer.Text = resolve(builder.Footer.Text);
            return builder.Build();
        }

        private static string UserName(ulong userId, DiscordSocketClient client, SocketGuild guild, FrontierOptions options)
        {
            var telegramId = options?.UserToDiscord.FirstOrDefault(pair => pair.Value == userId).Key ?? 0;
            if (telegramId != 0 && FrontierUsers.UsernameOf(telegramId) is { } username)
                return username;

            var member = guild?.GetUser(userId);
            var user = (IUser)member ?? client?.GetUser(userId);
            return member?.DisplayName ?? user?.GlobalName ?? user?.Username ?? "пользователь";
        }

        private static string Time(DateTimeOffset at, string style) => style switch
        {
            "t" => at.ToString("HH:mm", CultureInfo.InvariantCulture),
            "T" => at.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            "d" => at.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            "D" => at.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU")),
            _ => at.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)
        };

        private static string Escape(string value) => Markdown.Replace(value ?? "", @"\$1");
    }
}
