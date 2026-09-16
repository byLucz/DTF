using System;

namespace DiscordTelegramFrontier
{
    internal sealed record TelegramRenderedMessage(string Text, string ImageUrl = null, byte[] Png = null)
    {
        public bool IsPhoto => ImageUrl != null || Png != null;

        public bool HasSameContent(TelegramRenderedMessage other)
            => Text == other.Text && ImageUrl == other.ImageUrl && Png.AsSpan().SequenceEqual(other.Png);
    }
}
