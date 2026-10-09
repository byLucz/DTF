using Discord;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierUser : IUser
    {
        public FrontierUser(long? telegramId, string username, string displayName)
        {
            TelegramId = telegramId;
            Username = string.IsNullOrWhiteSpace(username) ? null : username.TrimStart('@');
            GlobalName = string.IsNullOrWhiteSpace(displayName) ? Username : displayName;
        }

        public long? TelegramId { get; }

        public ulong Id => TelegramId is long id && id > 0 ? (ulong)id : 0;

        public string Username { get; }

        public string GlobalName { get; }

        public string Mention => Username is null ? GlobalName : "@" + Username;

        public DateTimeOffset CreatedAt => DateTimeOffset.MinValue;
        public UserStatus Status => UserStatus.Offline;
        public IReadOnlyCollection<ClientType> ActiveClients => Array.Empty<ClientType>();
        public IReadOnlyCollection<IActivity> Activities => Array.Empty<IActivity>();
        public string AvatarId => null;
        public string Discriminator => "0000";
        public ushort DiscriminatorValue => 0;
        public bool IsBot => false;
        public bool IsWebhook => false;
        public UserProperties? PublicFlags => null;
        public string AvatarDecorationHash => null;
        public ulong? AvatarDecorationSkuId => null;
        public PrimaryGuild? PrimaryGuild => null;

        public string GetAvatarUrl(ImageFormat format = ImageFormat.Auto, ushort size = 128) => null;
        public string GetDefaultAvatarUrl() => null;
        public string GetDisplayAvatarUrl(ImageFormat format = ImageFormat.Auto, ushort size = 128) => null;
        public string GetAvatarDecorationUrl() => null;

        public Task<IDMChannel> CreateDMChannelAsync(RequestOptions options = null)
            => throw new NotSupportedException("Telegram user has no Discord DM channel");

        public override string ToString() => GlobalName;
    }
}
