using Discord;
using Discord.Interactions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierComponentData : IComponentInteractionData
    {
        internal FrontierComponentData(string customId, ComponentType type, IReadOnlyCollection<string> values)
        {
            CustomId = customId;
            Type = type;
            Values = values ?? Array.Empty<string>();
        }

        public string CustomId { get; }
        public ComponentType Type { get; }
        public IReadOnlyCollection<string> Values { get; }
        public IReadOnlyCollection<IChannel> Channels => Array.Empty<IChannel>();
        public IReadOnlyCollection<IUser> Users => Array.Empty<IUser>();
        public IReadOnlyCollection<IRole> Roles => Array.Empty<IRole>();
        public IReadOnlyCollection<IGuildUser> Members => Array.Empty<IGuildUser>();
        public string Value => null;
        public bool? BoolValue => null;
    }

    public sealed class FrontierComponentInteraction : IComponentInteraction
    {
        private readonly FrontierProxyMessage _message;
        private readonly Func<string, bool, Task> _answer;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private FrontierProxyMessage _response;
        private bool _answered;

        internal FrontierComponentInteraction(FrontierProxyMessage message, FrontierComponentData data, IUser user,
            IGuild guild, ulong applicationId, string locale, Func<string, bool, Task> answer)
        {
            _message = message;
            _answer = answer;
            Data = data;
            User = user;
            GuildId = guild?.Id;
            ApplicationId = applicationId;
            UserLocale = locale;
            Id = FrontierProxyMessage.NextId();
        }

        public FrontierComponentData Data { get; }
        IComponentInteractionData IComponentInteraction.Data => Data;
        IDiscordInteractionData IDiscordInteraction.Data => Data;
        public IUserMessage Message => _message;

        public ulong Id { get; }
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public InteractionType Type => InteractionType.MessageComponent;
        public string Token => "";
        public int Version => 1;
        public bool HasResponded { get; private set; }
        public IUser User { get; }
        public string UserLocale { get; }
        public string GuildLocale => null;
        public bool IsDMInteraction => GuildId is null;
        public ulong? ChannelId => _message.Channel.Id;
        public ulong? GuildId { get; }
        public ulong ApplicationId { get; }
        public IReadOnlyCollection<IEntitlement> Entitlements => Array.Empty<IEntitlement>();
        public IReadOnlyDictionary<ApplicationIntegrationType, ulong> IntegrationOwners { get; } = new Dictionary<ApplicationIntegrationType, ulong>();
        public InteractionContextType? ContextType => null;
        public GuildPermissions Permissions => GuildPermissions.None;
        public ulong AttachmentSizeLimit => 0;

        internal async Task AnswerAsync(string text = null, bool alert = false)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_answered) return;
                _answered = true;
                await _answer(text, alert).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DeferAsync(bool ephemeral = false, RequestOptions options = null)
        {
            EnsureNotResponded();
            await AnswerAsync().ConfigureAwait(false);
            HasResponded = true;
        }

        public Task DeferLoadingAsync(bool ephemeral = false, RequestOptions options = null)
            => DeferAsync(ephemeral, options);

        public async Task RespondAsync(string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false,
            AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null,
            RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None)
        {
            EnsureNotResponded();
            await AnswerAsync().ConfigureAwait(false);
            _response = await SendAsync(text, embeds, embed, components, options).ConfigureAwait(false);
            HasResponded = true;
        }

        public async Task UpdateAsync(Action<MessageProperties> func, RequestOptions options = null)
        {
            EnsureNotResponded();
            await AnswerAsync().ConfigureAwait(false);
            await _message.ModifyAsync(func, options).ConfigureAwait(false);
            HasResponded = true;
        }

        public async Task<IUserMessage> FollowupAsync(string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false,
            AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null,
            RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None)
        {
            await AnswerAsync().ConfigureAwait(false);
            return await SendAsync(text, embeds, embed, components, options).ConfigureAwait(false);
        }

        public Task<IUserMessage> GetOriginalResponseAsync(RequestOptions options = null)
            => Task.FromResult<IUserMessage>(_response ?? _message);

        public async Task<IUserMessage> ModifyOriginalResponseAsync(Action<MessageProperties> func, RequestOptions options = null)
        {
            var target = _response ?? _message;
            await target.ModifyAsync(func, options).ConfigureAwait(false);
            return target;
        }

        public Task DeleteOriginalResponseAsync(RequestOptions options = null)
            => (_response ?? _message).DeleteAsync(options);

        public Task RespondWithModalAsync(Modal modal, RequestOptions options = null)
            => throw new NotSupportedException("DTF does not support modals in Telegram.");

        public Task RespondWithPremiumRequiredAsync(RequestOptions options = null)
            => throw new NotSupportedException();

        public Task RespondWithFileAsync(Stream fileStream, string fileName, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None)
            => throw new NotSupportedException();
        public Task RespondWithFileAsync(string filePath, string fileName = null, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None)
            => throw new NotSupportedException();
        public Task RespondWithFileAsync(FileAttachment attachment, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None)
            => throw new NotSupportedException();
        public Task RespondWithFilesAsync(IEnumerable<FileAttachment> attachments, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None)
            => throw new NotSupportedException();
        public Task<IUserMessage> FollowupWithFileAsync(Stream fileStream, string fileName, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None)
            => throw new NotSupportedException();
        public Task<IUserMessage> FollowupWithFileAsync(string filePath, string fileName = null, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None)
            => throw new NotSupportedException();
        public Task<IUserMessage> FollowupWithFileAsync(FileAttachment attachment, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None)
            => throw new NotSupportedException();
        public Task<IUserMessage> FollowupWithFilesAsync(IEnumerable<FileAttachment> attachments, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None)
            => throw new NotSupportedException();

        private async Task<FrontierProxyMessage> SendAsync(string text, Embed[] embeds, Embed embed, MessageComponent components, RequestOptions options)
            => (FrontierProxyMessage)await ((IMessageChannel)_message.Channel)
                .SendMessageAsync(text, embed: embed, options: options, components: components, embeds: embeds).ConfigureAwait(false);

        private void EnsureNotResponded()
        {
            if (HasResponded)
                throw new InvalidOperationException("Cannot respond twice to the same interaction.");
        }
    }

    public sealed class FrontierInteractionContext : IInteractionContext, IRouteMatchContainer
    {
        private int _finished;

        internal FrontierInteractionContext(IDiscordClient client, IGuild guild, IMessageChannel channel, IUser user,
            FrontierComponentInteraction interaction)
        {
            Client = client;
            Guild = guild;
            Channel = channel;
            User = user;
            Interaction = interaction;
        }

        public IDiscordClient Client { get; }
        public IGuild Guild { get; }
        public IMessageChannel Channel { get; }
        public IUser User { get; }
        public FrontierComponentInteraction Interaction { get; }
        IDiscordInteraction IInteractionContext.Interaction => Interaction;
        public IReadOnlyCollection<IRouteSegmentMatch> SegmentMatches { get; private set; } = Array.Empty<IRouteSegmentMatch>();

        public void SetSegmentMatches(IEnumerable<IRouteSegmentMatch> segmentMatches)
            => SegmentMatches = segmentMatches?.ToArray() ?? Array.Empty<IRouteSegmentMatch>();

        IEnumerable<IRouteSegmentMatch> IRouteMatchContainer.SegmentMatches => SegmentMatches;

        internal bool TryFinish() => Interlocked.Exchange(ref _finished, 1) == 0;
    }
}
