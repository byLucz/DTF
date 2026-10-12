using System.IO.Hashing;

namespace DiscordTelegramFrontier
{
    internal sealed record TelegramRenderedMessage(string Text, string ImageUrl = null, byte[] Png = null)
    {
        public ulong? PngHash { get; init; } = Png == null ? null : XxHash3.HashToUInt64(Png);

        public bool IsPhoto => ImageUrl != null || PngHash != null;

        public bool HasSameContent(TelegramRenderedMessage other)
            => Text == other.Text && ImageUrl == other.ImageUrl && PngHash == other.PngHash;

        public TelegramRenderedMessage Detach() => Png == null ? this : this with { Png = null };
    }
}
