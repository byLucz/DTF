using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierUpdateContext
    {
        public ITelegramBotClient Bot { get; }
        public Update Update { get; }
        public string BotUsername { get; }
        public IServiceProvider Services { get; }
        public CancellationToken CancellationToken { get; }
        private Dictionary<object, object> _items;
        public IDictionary<object, object> Items => _items ??= new Dictionary<object, object>();
        public Message Message => Update.Message ?? Update.EditedMessage ?? Update.ChannelPost ?? Update.EditedChannelPost
            ?? Update.BusinessMessage ?? Update.EditedBusinessMessage ?? Update.CallbackQuery?.Message;
        public CallbackQuery CallbackQuery => Update.CallbackQuery;
        public string Command { get; }
        public string Arguments { get; } = "";
        public bool AddressedToAnotherBot { get; }

        public FrontierUpdateContext(ITelegramBotClient bot, Update update, string botUsername,
            IServiceProvider services, CancellationToken cancellationToken = default)
        {
            Bot = bot ?? throw new ArgumentNullException(nameof(bot));
            Update = update ?? throw new ArgumentNullException(nameof(update));
            BotUsername = botUsername;
            Services = services ?? throw new ArgumentNullException(nameof(services));
            CancellationToken = cancellationToken;
            var text = (update.Message ?? update.ChannelPost)?.Text?.TrimStart();
            if (string.IsNullOrEmpty(text) || !text.StartsWith('/')) return;
            var end = 1;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
            var command = text.Substring(1, end - 1);
            var mention = command.IndexOf('@');
            if (mention >= 0)
            {
                AddressedToAnotherBot = !string.Equals(command.Substring(mention + 1), botUsername, StringComparison.OrdinalIgnoreCase);
                command = command.Substring(0, mention);
            }
            if (AddressedToAnotherBot || command.Length == 0) return;
            Command = command.ToLowerInvariant();
            Arguments = text.Substring(end).TrimStart();
        }

        public Task<Message> ReplyAsync(string text, ParseMode parseMode = ParseMode.None, ReplyMarkup replyMarkup = null)
        {
            var message = Message ?? throw new InvalidOperationException("This update has no message to reply to.");
            return Bot.SendMessage(message.Chat.Id, text, parseMode: parseMode, replyMarkup: replyMarkup,
                replyParameters: new ReplyParameters { MessageId = message.Id }, businessConnectionId: message.BusinessConnectionId,
                messageThreadId: message.MessageThreadId, cancellationToken: CancellationToken);
        }

        public Task AnswerCallbackAsync(string text = null, bool showAlert = false)
        {
            var callback = CallbackQuery ?? throw new InvalidOperationException("This update has no callback query.");
            return Bot.AnswerCallbackQuery(callback.Id, text: text, showAlert: showAlert, cancellationToken: CancellationToken);
        }

        public IMessageChannel CreateReplyChannel(bool renderAsImage = false)
        {
            var message = Message ?? throw new InvalidOperationException("This update has no chat to send to.");
            return new FrontierProxyChannel(Bot, message.Chat.Id, renderAsImage: renderAsImage,
                messageThreadId: message.MessageThreadId, businessConnectionId: message.BusinessConnectionId);
        }
    }
}
