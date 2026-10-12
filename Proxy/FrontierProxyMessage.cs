using Discord;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types.ReplyMarkups;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierProxyMessage : IUserMessage
    {
        private readonly FrontierProxyChannel _channel;
        private string _content;
        private Embed[] _embeds;
        private TelegramRenderedMessage _rendered;
        private MessageComponent _components;
        private InlineKeyboardMarkup _markup;
        private string _signature = "";
        private readonly IUser _author;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _deleted;

        private static long _nextId;

        internal FrontierProxyMessage(FrontierProxyChannel channel, string content, Embed[] embeds, IUser author)
        {
            _channel = channel;
            _content = content ?? "";
            _embeds = embeds;
            _author = author;
            Id = NextId();
        }

        internal static ulong NextId() => (1UL << 63) | (ulong)Interlocked.Increment(ref _nextId);

        internal FrontierProxyChannel ProxyChannel => _channel;
        internal InlineKeyboardMarkup Markup => _markup;

        internal void Bind(int telegramMessageId, TelegramRenderedMessage rendered, MessageComponent components,
            InlineKeyboardMarkup markup)
        {
            TelegramMessageId = telegramMessageId;
            _rendered = rendered.Detach();
            _components = markup == null ? null : components;
            _markup = markup;
            _signature = markup == null ? "" : FrontierButtons.Signature(components);
        }

        public ulong Id { get; }
        public int TelegramMessageId { get; private set; }
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public IMessageChannel Channel => _channel;
        public IUser Author => _author;
        public string Content => _content;
        public string CleanContent => _content;
        public DateTimeOffset Timestamp => CreatedAt;
        public DateTimeOffset? EditedTimestamp { get; private set; }

        public MessageType Type => MessageType.Default;
        public MessageSource Source => MessageSource.Bot;
        public bool IsTTS => false;
        public bool IsPinned => false;
        public bool IsSuppressed => false;
        public bool MentionedEveryone => false;

        public IThreadChannel Thread => null;
        public IReadOnlyCollection<IAttachment> Attachments => Array.Empty<IAttachment>();
        public IReadOnlyCollection<IEmbed> Embeds => Array.AsReadOnly(_embeds);
        public IReadOnlyCollection<ITag> Tags => Array.Empty<ITag>();
        public IReadOnlyCollection<ulong> MentionedChannelIds => Array.Empty<ulong>();
        public IReadOnlyCollection<ulong> MentionedRoleIds => Array.Empty<ulong>();
        public IReadOnlyCollection<ulong> MentionedUserIds => Array.Empty<ulong>();
        public MessageActivity Activity => null;
        public MessageApplication Application => null;
        public MessageReference Reference => null;
        public IReadOnlyDictionary<IEmote, ReactionMetadata> Reactions => new Dictionary<IEmote, ReactionMetadata>();
        public IReadOnlyCollection<IMessageComponent> Components => _components?.Components ?? Array.Empty<IMessageComponent>();
        public IReadOnlyCollection<IStickerItem> Stickers => Array.Empty<IStickerItem>();
        public MessageFlags? Flags => null;

        public MessageResolvedData ResolvedData => null;
        public IUserMessage ReferencedMessage => null;
        public IMessageInteractionMetadata InteractionMetadata => null;
        public IReadOnlyCollection<MessageSnapshot> ForwardedMessages => Array.Empty<MessageSnapshot>();
        public Poll? Poll => null;

        public IMessageInteraction Interaction => null;
        public MessageRoleSubscriptionData RoleSubscriptionData => null;
        public PurchaseNotification PurchaseNotification => default;
        public MessageCallData? CallData => null;

        public async Task ModifyAsync(Action<MessageProperties> func, RequestOptions options = null)
        {
            ArgumentNullException.ThrowIfNull(func);
            var ct = options?.CancelToken ?? default;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_deleted)
                    throw new InvalidOperationException("The Telegram message has been deleted.");

                var properties = new MessageProperties();
                func(properties);
                if (properties.Attachments.IsSpecified || properties.Flags.IsSpecified)
                    throw new NotSupportedException("DTF supports editing Content, Embed, Embeds and Components; attachments and flags are not supported.");
                var content = properties.Content.IsSpecified ? properties.Content.Value ?? "" : _content;
                var embeds = properties.Embed.IsSpecified || properties.Embeds.IsSpecified
                    ? TelegramRenderer.CombineEmbeds(properties.Embed.GetValueOrDefault(), properties.Embeds.GetValueOrDefault())
                    : _embeds;
                var components = properties.Components.IsSpecified ? properties.Components.Value : _components;
                var signature = _channel.ButtonsEnabled ? FrontierButtons.Signature(components) : "";
                if (content == _content && signature == _signature && SameEmbeds(embeds, _embeds))
                    return;
                var markup = signature == _signature ? _markup : _channel.BuildMarkup(this, components);
                var rendered = await _channel.RenderAsync(content, embeds, ct).ConfigureAwait(false);
                var messageId = await _channel.EditAsync(TelegramMessageId, _rendered, rendered, _markup, markup, ct).ConfigureAwait(false);

                TelegramMessageId = messageId;
                _content = content;
                _embeds = embeds;
                _rendered = rendered.Detach();
                _components = markup == null ? null : components;
                _markup = markup;
                _signature = markup == null ? "" : signature;
                EditedTimestamp = DateTimeOffset.UtcNow;
            }
            finally
            {
                _gate.Release();
            }
        }
        private static bool SameEmbeds(Embed[] left, Embed[] right)
        {
            if (left.Length != right.Length)
                return false;
            for (var i = 0; i < left.Length; i++)
                if (!ReferenceEquals(left[i], right[i]))
                    return false;
            return true;
        }

        public Task PinAsync(RequestOptions options = null) => throw new NotSupportedException();
        public Task UnpinAsync(RequestOptions options = null) => throw new NotSupportedException();
        public Task CrosspostAsync(RequestOptions options = null) => throw new NotSupportedException();
        public string Resolve(TagHandling userHandling = TagHandling.Name, TagHandling channelHandling = TagHandling.Name, TagHandling roleHandling = TagHandling.Name, TagHandling everyoneHandling = TagHandling.Ignore, TagHandling emojiHandling = TagHandling.Name) => _content;
        public Task EndPollAsync(RequestOptions options) => throw new NotSupportedException();
        public IAsyncEnumerable<IReadOnlyCollection<IUser>> GetPollAnswerVotersAsync(uint answerId, int? limit = null, ulong? afterId = null, RequestOptions options = null) => throw new NotSupportedException();

        public Task AddReactionAsync(IEmote emote, RequestOptions options = null) => throw new NotSupportedException();
        public Task RemoveReactionAsync(IEmote emote, IUser user, RequestOptions options = null) => throw new NotSupportedException();
        public Task RemoveReactionAsync(IEmote emote, ulong userId, RequestOptions options = null) => throw new NotSupportedException();
        public Task RemoveAllReactionsAsync(RequestOptions options = null) => throw new NotSupportedException();
        public Task RemoveAllReactionsForEmoteAsync(IEmote emote, RequestOptions options = null) => throw new NotSupportedException();
        public IAsyncEnumerable<IReadOnlyCollection<IUser>> GetReactionUsersAsync(IEmote emoji, int limit, RequestOptions options = null, ReactionType type = ReactionType.Normal) => throw new NotSupportedException();

        public async Task DeleteAsync(RequestOptions options = null)
        {
            var ct = options?.CancelToken ?? default;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_deleted) return;
                await _channel.DeleteAsync(TelegramMessageId, ct).ConfigureAwait(false);
                _deleted = true;
                _channel.Forget(Id);
                _channel.Buttons?.Release(Id);
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
