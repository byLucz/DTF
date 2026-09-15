using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierProxyChannel : IMessageChannel
    {
        private readonly ITelegramBotClient _tg;
        private readonly long _chatId;

        public FrontierProxyChannel(ITelegramBotClient tg, long chatId)
        {
            _tg = tg;
            _chatId = chatId;
        }

        public async Task<IUserMessage> SendMessageAsync(string text = null, bool isTTS = false, Embed embed = null,
            RequestOptions options = null, AllowedMentions allowedMentions = null, MessageReference messageReference = null,
            MessageComponent components = null, ISticker[] stickers = null, Embed[] embeds = null,
            MessageFlags flags = MessageFlags.None, PollProperties poll = null)
        {
            var (body, image) = TelegramRenderer.Render(text, embed);
            if (string.IsNullOrWhiteSpace(body) && string.IsNullOrEmpty(image))
                body = "(empty)";

            if (!string.IsNullOrEmpty(image))
                await _tg.SendPhoto(_chatId, InputFile.FromUri(image), caption: Trunc(body, 1024), parseMode: ParseMode.Html);
            else
                await _tg.SendMessage(_chatId, string.IsNullOrEmpty(body) ? "(empty)" : body, parseMode: ParseMode.Html);

            return new FrontierProxyMessage(this, body, null);
        }

        private static string Trunc(string s, int n) => s != null && s.Length > n ? s.Substring(0, n) : s;

        public ulong Id => unchecked((ulong)_chatId);
        public string Name => "telegram";
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public ChannelType ChannelType => Discord.ChannelType.Text;

        public Task<IUserMessage> SendFileAsync(string filePath, string text, bool isTTS = false, Embed embed = null, RequestOptions options = null, bool isSpoiler = false, AllowedMentions allowedMentions = null, MessageReference messageReference = null, MessageComponent components = null, ISticker[] stickers = null, Embed[] embeds = null, MessageFlags flags = MessageFlags.None, PollProperties poll = null)
            => throw new NotSupportedException();

        public Task<IUserMessage> SendFileAsync(Stream stream, string filename, string text, bool isTTS = false, Embed embed = null, RequestOptions options = null, bool isSpoiler = false, AllowedMentions allowedMentions = null, MessageReference messageReference = null, MessageComponent components = null, ISticker[] stickers = null, Embed[] embeds = null, MessageFlags flags = MessageFlags.None, PollProperties poll = null)
            => throw new NotSupportedException();

        public Task<IUserMessage> SendFileAsync(FileAttachment attachment, string text, bool isTTS = false, Embed embed = null, RequestOptions options = null, AllowedMentions allowedMentions = null, MessageReference messageReference = null, MessageComponent components = null, ISticker[] stickers = null, Embed[] embeds = null, MessageFlags flags = MessageFlags.None, PollProperties poll = null)
            => throw new NotSupportedException();

        public Task<IUserMessage> SendFilesAsync(IEnumerable<FileAttachment> attachments, string text, bool isTTS = false, Embed embed = null, RequestOptions options = null, AllowedMentions allowedMentions = null, MessageReference messageReference = null, MessageComponent components = null, ISticker[] stickers = null, Embed[] embeds = null, MessageFlags flags = MessageFlags.None, PollProperties poll = null)
            => throw new NotSupportedException();

        public Task<IMessage> GetMessageAsync(ulong id, CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => throw new NotSupportedException();

        public IAsyncEnumerable<IReadOnlyCollection<IMessage>> GetMessagesAsync(int limit = 100, CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => throw new NotSupportedException();

        public IAsyncEnumerable<IReadOnlyCollection<IMessage>> GetMessagesAsync(ulong fromMessageId, Direction dir, int limit = 100, CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => throw new NotSupportedException();

        public IAsyncEnumerable<IReadOnlyCollection<IMessage>> GetMessagesAsync(IMessage fromMessage, Direction dir, int limit = 100, CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => throw new NotSupportedException();

        public Task<IReadOnlyCollection<IMessage>> GetPinnedMessagesAsync(RequestOptions options = null)
            => throw new NotSupportedException();

        public Task DeleteMessageAsync(ulong messageId, RequestOptions options = null) => throw new NotSupportedException();
        public Task DeleteMessageAsync(IMessage message, RequestOptions options = null) => throw new NotSupportedException();

        public Task<IUserMessage> ModifyMessageAsync(ulong messageId, Action<MessageProperties> func, RequestOptions options = null)
            => throw new NotSupportedException();

        public Task TriggerTypingAsync(RequestOptions options = null) => Task.CompletedTask;
        public IDisposable EnterTypingState(RequestOptions options = null) => new NoopDisposable();

        public IAsyncEnumerable<IReadOnlyCollection<IUser>> GetUsersAsync(CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => throw new NotSupportedException();

        public Task<IUser> GetUserAsync(ulong id, CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => throw new NotSupportedException();

        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
