using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierProxyMessage : IUserMessage
    {
        private readonly IMessageChannel _channel;
        private readonly string _content;
        private readonly IUser _author;

        public FrontierProxyMessage(IMessageChannel channel, string content, IUser author)
        {
            _channel = channel;
            _content = content ?? "";
            _author = author;
        }

        public ulong Id => 0;
        public DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
        public IMessageChannel Channel => _channel;
        public IUser Author => _author;
        public string Content => _content;
        public string CleanContent => _content;
        public DateTimeOffset Timestamp => CreatedAt;
        public DateTimeOffset? EditedTimestamp => null;

        public MessageType Type => MessageType.Default;
        public MessageSource Source => MessageSource.User;
        public bool IsTTS => false;
        public bool IsPinned => false;
        public bool IsSuppressed => false;
        public bool MentionedEveryone => false;

        public IThreadChannel Thread => null;
        public IReadOnlyCollection<IAttachment> Attachments => Array.Empty<IAttachment>();
        public IReadOnlyCollection<IEmbed> Embeds => Array.Empty<IEmbed>();
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

        public Task ModifyAsync(Action<MessageProperties> func, RequestOptions options = null) => throw new NotSupportedException();
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

        public Task DeleteAsync(RequestOptions options = null) => throw new NotSupportedException();
    }
}
