using Discord;
using Discord.Rest;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierProxyChannel : ISocketMessageChannel
    {
        private readonly ITelegramBotClient _tg;
        private readonly long _chatId;
        private readonly int? _messageThreadId;
        private readonly string _businessConnectionId;
        private readonly IUser _author;
        private bool _renderAsImage;
        private static readonly TelegramImageRenderer ImageRenderer = new();
        private readonly ConcurrentDictionary<ulong, FrontierProxyMessage> _messages = new();

        public FrontierProxyChannel(ITelegramBotClient tg, long chatId, IUser author = null, bool renderAsImage = false,
            int? messageThreadId = null, string businessConnectionId = null)
        {
            _tg = tg ?? throw new ArgumentNullException(nameof(tg));
            _chatId = chatId;
            _messageThreadId = messageThreadId;
            _businessConnectionId = businessConnectionId;
            _author = author;
            _renderAsImage = renderAsImage;
        }

        internal FrontierButtons Buttons { get; private set; }
        internal bool ButtonsEnabled => Buttons != null;
        internal long ChatId => _chatId;

        internal void EnableButtons(FrontierButtons buttons) => Buttons = buttons;

        internal InlineKeyboardMarkup BuildMarkup(FrontierProxyMessage message, MessageComponent components)
            => Buttons?.Build(message, components);

        private async Task<FrontierProxyMessage> SendCoreAsync(string text, Embed embed, Embed[] embeds, MessageComponent components,
            RequestOptions options)
        {
            var allEmbeds = TelegramRenderer.CombineEmbeds(embed, embeds);
            var ct = options?.CancelToken ?? default;
            var message = new FrontierProxyMessage(this, text, allEmbeds, _author);
            var markup = BuildMarkup(message, components);
            try
            {
                var rendered = await RenderAsync(text, allEmbeds, ct).ConfigureAwait(false);
                var sent = await SendRenderedAsync(rendered, markup, ct).ConfigureAwait(false);
                message.Bind(sent.Id, rendered, components, markup);
            }
            catch
            {
                Buttons?.Release(message.Id);
                throw;
            }
            _messages[message.Id] = message;
            return message;
        }

        internal async Task<TelegramRenderedMessage> RenderAsync(string content, Embed[] embeds, CancellationToken ct)
        {
            if (_renderAsImage)
                return new TelegramRenderedMessage(null, Png: await ImageRenderer.RenderAsync(content, embeds, ct).ConfigureAwait(false));
            var rendered = TelegramRenderer.RenderMessage(content, embeds);
            return new TelegramRenderedMessage(rendered.text, rendered.image);
        }

        private async Task<Message> SendRenderedAsync(TelegramRenderedMessage rendered, InlineKeyboardMarkup markup, CancellationToken ct)
        {
            if (rendered.Png != null)
            {
                using var stream = new MemoryStream(rendered.Png, false);
                return await _tg.SendPhoto(_chatId, InputFile.FromStream(stream, "message.png"), replyMarkup: markup,
                    messageThreadId: _messageThreadId, businessConnectionId: _businessConnectionId, cancellationToken: ct).ConfigureAwait(false);
            }
            if (rendered.ImageUrl == null)
                return await SendTextAsync(rendered.Text, markup, ct).ConfigureAwait(false);

            try
            {
                var media = InputFile.FromUri(rendered.ImageUrl);
                return IsAnimation(rendered.ImageUrl)
                    ? await _tg.SendAnimation(_chatId, media, caption: rendered.Text, parseMode: ParseMode.Html,
                        replyMarkup: markup, messageThreadId: _messageThreadId, businessConnectionId: _businessConnectionId,
                        cancellationToken: ct).ConfigureAwait(false)
                    : await _tg.SendPhoto(_chatId, media, caption: rendered.Text, parseMode: ParseMode.Html,
                        replyMarkup: markup, messageThreadId: _messageThreadId, businessConnectionId: _businessConnectionId,
                        cancellationToken: ct).ConfigureAwait(false);
            }
            catch (ApiRequestException)
            {
                var link = System.Net.WebUtility.HtmlEncode(rendered.ImageUrl);
                var text = string.IsNullOrWhiteSpace(rendered.Text) || rendered.Text == "(empty)"
                    ? link
                    : rendered.Text + "\n" + link;
                return await SendTextAsync(text, markup, ct).ConfigureAwait(false);
            }
        }

        private Task<Message> SendTextAsync(string text, InlineKeyboardMarkup markup, CancellationToken ct)
            => _tg.SendMessage(_chatId, text, parseMode: ParseMode.Html, replyMarkup: markup, messageThreadId: _messageThreadId,
                businessConnectionId: _businessConnectionId, cancellationToken: ct);

        private static bool IsAnimation(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            var path = uri.AbsolutePath;
            return path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);
        }

        internal async Task<int> EditAsync(int messageId, TelegramRenderedMessage previous, TelegramRenderedMessage next,
            InlineKeyboardMarkup previousMarkup, InlineKeyboardMarkup nextMarkup, CancellationToken ct)
        {
            if (previous.HasSameContent(next))
            {
                if (ReferenceEquals(previousMarkup, nextMarkup))
                    return messageId;

                try
                {
                    await _tg.EditMessageReplyMarkup(_chatId, messageId, nextMarkup,
                        businessConnectionId: _businessConnectionId, cancellationToken: ct).ConfigureAwait(false);
                }
                catch (ApiRequestException ex) when (ex.ErrorCode == 400 &&
                    ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                {
                }
                return messageId;
            }

            if (previous.IsPhoto && !next.IsPhoto)
            {
                var replacement = await SendRenderedAsync(next, nextMarkup, ct).ConfigureAwait(false);
                try
                {
                    await DeleteAsync(messageId, ct).ConfigureAwait(false);
                }
                catch (Exception deleteError)
                {
                    try
                    {
                        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await DeleteAsync(replacement.Id, cleanup.Token).ConfigureAwait(false);
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
                if (next.Png != null)
                {
                    using var stream = new MemoryStream(next.Png, false);
                    await _tg.EditMessageMedia(_chatId, messageId,
                        new InputMediaPhoto(InputFile.FromStream(stream, "message.png")), replyMarkup: nextMarkup,
                        businessConnectionId: _businessConnectionId, cancellationToken: ct).ConfigureAwait(false);
                }
                else if (next.ImageUrl == null)
                    await _tg.EditMessageText(_chatId, messageId, next.Text, parseMode: ParseMode.Html, replyMarkup: nextMarkup,
                        businessConnectionId: _businessConnectionId, cancellationToken: ct).ConfigureAwait(false);
                else if (previous.ImageUrl == next.ImageUrl && previous.PngHash == null)
                    await _tg.EditMessageCaption(_chatId, messageId, caption: next.Text, parseMode: ParseMode.Html, replyMarkup: nextMarkup,
                        businessConnectionId: _businessConnectionId, cancellationToken: ct).ConfigureAwait(false);
                else
                    await _tg.EditMessageMedia(_chatId, messageId, new InputMediaPhoto(InputFile.FromUri(next.ImageUrl))
                    {
                        Caption = next.Text,
                        ParseMode = ParseMode.Html
                    }, replyMarkup: nextMarkup, businessConnectionId: _businessConnectionId, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 400 &&
                ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
            {
            }
            return messageId;
        }

        internal Task DeleteAsync(int messageId, CancellationToken ct)
            => _businessConnectionId == null ? _tg.DeleteMessage(_chatId, messageId, cancellationToken: ct)
                : _tg.DeleteBusinessMessages(_businessConnectionId, new[] { messageId }, cancellationToken: ct);

        internal void Forget(ulong id) => _messages.TryRemove(id, out _);
        internal bool UsesClient(ITelegramBotClient client) => ReferenceEquals(_tg, client);
        internal void SetRenderMode(bool renderAsImage) => _renderAsImage = renderAsImage;

        public ulong Id => unchecked((ulong)_chatId);
        public string Name => "telegram";
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public ChannelType ChannelType => Discord.ChannelType.Text;

        public async Task<RestUserMessage> SendMessageAsync(string text = null, bool isTTS = false, Embed embed = null, RequestOptions options = null, AllowedMentions allowedMentions = null, MessageReference messageReference = null, MessageComponent components = null, ISticker[] stickers = null, Embed[] embeds = null, MessageFlags flags = MessageFlags.None, PollProperties poll = null)
        {
            await SendCoreAsync(text, embed, embeds, components, options).ConfigureAwait(false);
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
            return await SendCoreAsync(text, embed, embeds, components, options).ConfigureAwait(false);
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
