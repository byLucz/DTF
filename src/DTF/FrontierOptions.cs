using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierOptions
    {
        public string TelegramToken { get; set; }
        public bool EnableDiscordCommands { get; set; } = true;
        public ulong DefaultGuildId { get; set; }
        public Action<Exception> ErrorHandler { get; set; }
        public Func<ITelegramBotClient, Update, string, CancellationToken, Task<bool>> UpdateHandler { get; set; }
        public Func<FrontierUpdateContext, Exception, Task> UpdateErrorHandler { get; set; }
        public UpdateType[] AllowedUpdates { get; set; } = new[] { UpdateType.Message };

        public Dictionary<long, ulong> ChatToGuild { get; } = new();
        public Dictionary<long, ulong> UserToDiscord { get; } = new();

        public FrontierOptions DefaultGuild(ulong guildId)
        {
            DefaultGuildId = guildId;
            return this;
        }

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
