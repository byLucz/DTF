using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierProxyChannel : ISocketMessageChannel
    {
        private readonly ITelegramBotClient _tg;
        private readonly long _chatId;

        public FrontierProxyChannel(ITelegramBotClient tg, long chatId)
        {
            _tg = tg;
            _chatId = chatId;
        }

        private async Task SendCoreAsync(string text, Embed embed)
        {
            var (body, image) = TelegramRenderer.Render(text, embed);
            if (string.IsNullOrWhiteSpace(body) && string.IsNullOrEmpty(image))
                body = "(empty)";

            if (!string.IsNullOrEmpty(image))
                await _tg.SendPhoto(_chatId, InputFile.FromUri(image), caption: Trunc(body, 1024), parseMode: ParseMode.Html);
            else
                await _tg.SendMessage(_chatId, string.IsNullOrEmpty(body) ? "(empty)" : body, parseMode: ParseMode.Html);
        }

        private static string Trunc(string s, int n) => s != null && s.Length > n ? s.Substring(0, n) : s;

        public ulong Id => unchecked((ulong)_chatId);
        public string Name => "telegram";
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public ChannelType ChannelType => Discord.ChannelType.Text;

        public async Task<RestUserMessage> SendMessageAsync(string text = null, bool isTTS = false, Embed embed = null, RequestOptions options = null, AllowedMentions allowedMentions = null, MessageReference messageReference = null, MessageComponent components = null, ISticker[] stickers = null, Embed[] embeds = null, MessageFlags flags = MessageFlags.None, PollProperties poll = null)
        {
            await SendCoreAsync(text, embed);
            return null;
        }

        public Task<RestUserMessage> SendFileAsync(string filePath, string text, bool isTTS = false, Embed embed = null, RequestOptions options = null, bool isSpoiler = false, AllowedMentions allowedMentions = null, MessageReference messageReference = null, MessageComponent components = null, ISticker[] stickers = null, Embed[] embeds = null, MessageFlags flags = MessageFlags.None, PollProperties poll = null)
            => throw new NotSupportedException();

        public Task<RestUserMessage> SendFileAsync(Stream stream, string filename, string text, bool isTTS = false, Embed embed = null, RequestOptions options = null, bool isSpoiler = false, AllowedMentions allowedMentions = null, MessageReference messageReference = null, MessageComponent components = null, ISticker[] stickers = null, Embed[] embeds = null, MessageFlags flags = MessageFlags.None, PollProperties poll = null)
            => throw new NotSupportedException();

        public Task<RestUserMessage> SendFileAsync(FileAttachment attachment, string text, bool isTTS = false, Embed embed = null, RequestOptions options = null, AllowedMentions allowedMentions = null, MessageReference messageReference = null, MessageComponent components = null, ISticker[] stickers = null, Embed[] embeds = null, MessageFlags flags = MessageFlags.None, PollProperties poll = null)
            => throw new NotSupportedException();

        public Task<RestUserMessage> SendFilesAsync(IEnumerable<FileAttachment> attachments, string text, bool isTTS = false, Embed embed = null, RequestOptions options = null, AllowedMentions allowedMentions = null, MessageReference messageReference = null, MessageComponent components = null, ISticker[] stickers = null, Embed[] embeds = null, MessageFlags flags = MessageFlags.None, PollProperties poll = null)
            => throw new NotSupportedException();

        public SocketMessage GetCachedMessage(ulong id) => null;
        public IReadOnlyCollection<SocketMessage> GetCachedMessages(int limit = 100) => Array.Empty<SocketMessage>();
        public IReadOnlyCollection<SocketMessage> GetCachedMessages(ulong fromMessageId, Direction dir, int limit = 100) => Array.Empty<SocketMessage>();
        public IReadOnlyCollection<SocketMessage> GetCachedMessages(IMessage fromMessage, Direction dir, int limit = 100) => Array.Empty<SocketMessage>();
        public IReadOnlyCollection<SocketMessage> CachedMessages => Array.Empty<SocketMessage>();

        public Task<IReadOnlyCollection<RestMessage>> GetPinnedMessagesAsync(RequestOptions options = null)
            => Task.FromResult<IReadOnlyCollection<RestMessage>>(Array.Empty<RestMessage>());

        public Task<IMessage> GetMessageAsync(ulong id, CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => Task.FromResult<IMessage>(null);

        public IAsyncEnumerable<IReadOnlyCollection<IMessage>> GetMessagesAsync(int limit = 100, CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => throw new NotSupportedException();
        public IAsyncEnumerable<IReadOnlyCollection<IMessage>> GetMessagesAsync(ulong fromMessageId, Direction dir, int limit = 100, CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => throw new NotSupportedException();
        public IAsyncEnumerable<IReadOnlyCollection<IMessage>> GetMessagesAsync(IMessage fromMessage, Direction dir, int limit = 100, CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => throw new NotSupportedException();

        public Task DeleteMessageAsync(ulong messageId, RequestOptions options = null) => Task.CompletedTask;
        public Task DeleteMessageAsync(IMessage message, RequestOptions options = null) => Task.CompletedTask;

        public Task<IUserMessage> ModifyMessageAsync(ulong messageId, Action<MessageProperties> func, RequestOptions options = null)
            => throw new NotSupportedException();

        public Task TriggerTypingAsync(RequestOptions options = null) => Task.CompletedTask;
        public IDisposable EnterTypingState(RequestOptions options = null) => new NoopDisposable();

        public IReadOnlyCollection<SocketUser> Users => Array.Empty<SocketUser>();
        public SocketUser GetUser(ulong id) => null;

        public IAsyncEnumerable<IReadOnlyCollection<IUser>> GetUsersAsync(CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => throw new NotSupportedException();
        public Task<IUser> GetUserAsync(ulong id, CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => Task.FromResult<IUser>(null);

        async Task<IUserMessage> IMessageChannel.SendMessageAsync(string text, bool isTTS, Embed embed, RequestOptions options, AllowedMentions allowedMentions, MessageReference messageReference, MessageComponent components, ISticker[] stickers, Embed[] embeds, MessageFlags flags, PollProperties poll)
        {
            await SendCoreAsync(text, embed);
            return new FrontierProxyMessage(this, text, null);
        }

        Task<IUserMessage> IMessageChannel.SendFileAsync(string filePath, string text, bool isTTS, Embed embed, RequestOptions options, bool isSpoiler, AllowedMentions allowedMentions, MessageReference messageReference, MessageComponent components, ISticker[] stickers, Embed[] embeds, MessageFlags flags, PollProperties poll)
            => throw new NotSupportedException();
        Task<IUserMessage> IMessageChannel.SendFileAsync(Stream stream, string filename, string text, bool isTTS, Embed embed, RequestOptions options, bool isSpoiler, AllowedMentions allowedMentions, MessageReference messageReference, MessageComponent components, ISticker[] stickers, Embed[] embeds, MessageFlags flags, PollProperties poll)
            => throw new NotSupportedException();
        Task<IUserMessage> IMessageChannel.SendFileAsync(FileAttachment attachment, string text, bool isTTS, Embed embed, RequestOptions options, AllowedMentions allowedMentions, MessageReference messageReference, MessageComponent components, ISticker[] stickers, Embed[] embeds, MessageFlags flags, PollProperties poll)
            => throw new NotSupportedException();
        Task<IUserMessage> IMessageChannel.SendFilesAsync(IEnumerable<FileAttachment> attachments, string text, bool isTTS, Embed embed, RequestOptions options, AllowedMentions allowedMentions, MessageReference messageReference, MessageComponent components, ISticker[] stickers, Embed[] embeds, MessageFlags flags, PollProperties poll)
            => throw new NotSupportedException();
        Task<IReadOnlyCollection<IMessage>> IMessageChannel.GetPinnedMessagesAsync(RequestOptions options)
            => Task.FromResult<IReadOnlyCollection<IMessage>>(Array.Empty<IMessage>());

        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
