using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Discord;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierProxyMessage : IUserMessage
    {
        private readonly FrontierProxyChannel _channel;
        private string _content;
        private Embed[] _embeds;
        private (string text, string image) _rendered;
        private readonly IUser _author;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _deleted;

        internal FrontierProxyMessage(FrontierProxyChannel channel, int telegramMessageId, string content,
            Embed[] embeds, IUser author, (string text, string image) rendered)
        {
            _channel = channel;
            _content = content ?? "";
            _embeds = embeds;
            _author = author;
            _rendered = rendered;
            TelegramMessageId = telegramMessageId;
            Id = (ulong)telegramMessageId;
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
        public IReadOnlyCollection<IMessageComponent> Components => Array.Empty<IMessageComponent>();
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
                if (properties.Attachments.IsSpecified || properties.Components.IsSpecified || properties.Flags.IsSpecified)
                    throw new NotSupportedException("DTF supports editing Content, Embed and Embeds; attachments, components and flags are not supported.");
                var content = properties.Content.IsSpecified ? properties.Content.Value ?? "" : _content;
                var embeds = properties.Embed.IsSpecified || properties.Embeds.IsSpecified
                    ? TelegramRenderer.CombineEmbeds(properties.Embed.GetValueOrDefault(), properties.Embeds.GetValueOrDefault())
                    : _embeds;
                var rendered = TelegramRenderer.RenderMessage(content, embeds);
                var messageId = await _channel.EditAsync(TelegramMessageId, _rendered, rendered, ct).ConfigureAwait(false);

                TelegramMessageId = messageId;
                _content = content;
                _embeds = embeds;
                _rendered = rendered;
                EditedTimestamp = DateTimeOffset.UtcNow;
            }
            finally
            {
                _gate.Release();
            }
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
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
