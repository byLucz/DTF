using System.Collections.Generic;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierOptions
    {
        public string TelegramToken { get; set; }

        public Dictionary<long, ulong> ChatToGuild { get; } = new();
        public Dictionary<long, ulong> UserToDiscord { get; } = new();

        public FrontierOptions Chat(long chatId, ulong guildId)
        {
            ChatToGuild[chatId] = guildId;
            return this;
        }

        public FrontierOptions User(long telegramUserId, ulong discordUserId)
        {
            UserToDiscord[telegramUserId] = discordUserId;
            return this;
        }
    }
}
