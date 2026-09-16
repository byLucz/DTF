using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Exceptions;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierProxyChannel : ISocketMessageChannel
    {
        private readonly ITelegramBotClient _tg;
        private readonly long _chatId;
        private readonly IUser _author;
        private readonly ConcurrentDictionary<ulong, FrontierProxyMessage> _messages = new();

        public FrontierProxyChannel(ITelegramBotClient tg, long chatId, IUser author = null)
        {
            _tg = tg ?? throw new ArgumentNullException(nameof(tg));
            _chatId = chatId;
            _author = author;
        }

        private async Task<FrontierProxyMessage> SendCoreAsync(string text, Embed embed, Embed[] embeds, RequestOptions options)
        {
            var allEmbeds = TelegramRenderer.CombineEmbeds(embed, embeds);
            var rendered = TelegramRenderer.RenderMessage(text, allEmbeds);
            var sent = await SendRenderedAsync(rendered, options?.CancelToken ?? default).ConfigureAwait(false);
            var message = new FrontierProxyMessage(this, sent.Id, text, allEmbeds, _author, rendered);
            _messages[message.Id] = message;
            return message;
        }

        private Task<Message> SendRenderedAsync((string text, string image) rendered, CancellationToken ct)
            => rendered.image == null
                ? _tg.SendMessage(_chatId, rendered.text, parseMode: ParseMode.Html, cancellationToken: ct)
                : _tg.SendPhoto(_chatId, InputFile.FromUri(rendered.image), caption: rendered.text,
                    parseMode: ParseMode.Html, cancellationToken: ct);

        internal async Task<int> EditAsync(int messageId, (string text, string image) previous,
            (string text, string image) next, CancellationToken ct)
        {
            if (previous == next)
                return messageId;

            if (previous.image != null && next.image == null)
            {
                var replacement = await SendRenderedAsync(next, ct).ConfigureAwait(false);
                try
                {
                    await _tg.DeleteMessage(_chatId, messageId, cancellationToken: ct).ConfigureAwait(false);
                }
                catch (Exception deleteError)
                {
                    try
                    {
                        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await _tg.DeleteMessage(_chatId, replacement.Id, cancellationToken: cleanup.Token).ConfigureAwait(false);
                    }
                    catch (Exception cleanupError)
                    {
                        throw new AggregateException("Could not replace the Telegram photo or remove the replacement.", deleteError, cleanupError);
                    }
                    throw;
                }
                return replacement.Id;
            }

            try
            {
                if (next.image == null)
                    await _tg.EditMessageText(_chatId, messageId, next.text, parseMode: ParseMode.Html,
                        cancellationToken: ct).ConfigureAwait(false);
                else if (previous.image == next.image)
                    await _tg.EditMessageCaption(_chatId, messageId, caption: next.text, parseMode: ParseMode.Html,
                        cancellationToken: ct).ConfigureAwait(false);
                else
                    await _tg.EditMessageMedia(_chatId, messageId, new InputMediaPhoto(InputFile.FromUri(next.image))
                    {
                        Caption = next.text,
                        ParseMode = ParseMode.Html
                    }, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 400 &&
                ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
            {
            }
            return messageId;
        }

        internal Task DeleteAsync(int messageId, CancellationToken ct)
            => _tg.DeleteMessage(_chatId, messageId, cancellationToken: ct);

        internal void Forget(ulong id) => _messages.TryRemove(id, out _);
        internal bool UsesClient(ITelegramBotClient client) => ReferenceEquals(_tg, client);

        public ulong Id => unchecked((ulong)_chatId);
        public string Name => "telegram";
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public ChannelType ChannelType => Discord.ChannelType.Text;

        public async Task<RestUserMessage> SendMessageAsync(string text = null, bool isTTS = false, Embed embed = null, RequestOptions options = null, AllowedMentions allowedMentions = null, MessageReference messageReference = null, MessageComponent components = null, ISticker[] stickers = null, Embed[] embeds = null, MessageFlags flags = MessageFlags.None, PollProperties poll = null)
        {
            await SendCoreAsync(text, embed, embeds, options).ConfigureAwait(false);
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
            => Task.FromResult<IMessage>(_messages.TryGetValue(id, out var message) ? message : null);

        public IAsyncEnumerable<IReadOnlyCollection<IMessage>> GetMessagesAsync(int limit = 100, CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => throw new NotSupportedException();
        public IAsyncEnumerable<IReadOnlyCollection<IMessage>> GetMessagesAsync(ulong fromMessageId, Direction dir, int limit = 100, CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => throw new NotSupportedException();
        public IAsyncEnumerable<IReadOnlyCollection<IMessage>> GetMessagesAsync(IMessage fromMessage, Direction dir, int limit = 100, CacheMode mode = CacheMode.AllowDownload, RequestOptions options = null)
            => throw new NotSupportedException();

        public Task DeleteMessageAsync(ulong messageId, RequestOptions options = null)
            => GetSentMessage(messageId).DeleteAsync(options);
        public Task DeleteMessageAsync(IMessage message, RequestOptions options = null)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (message.Channel.Id != Id)
                throw new ArgumentException("The message belongs to another channel.", nameof(message));
            return DeleteMessageAsync(message.Id, options);
        }

        public async Task<IUserMessage> ModifyMessageAsync(ulong messageId, Action<MessageProperties> func, RequestOptions options = null)
        {
            var message = GetSentMessage(messageId);
            await message.ModifyAsync(func, options).ConfigureAwait(false);
            return message;
        }

        private FrontierProxyMessage GetSentMessage(ulong id)
            => _messages.TryGetValue(id, out var message) ? message
                : throw new KeyNotFoundException("This channel instance has no sent message with that ID.");

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
            return await SendCoreAsync(text, embed, embeds, options).ConfigureAwait(false);
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
