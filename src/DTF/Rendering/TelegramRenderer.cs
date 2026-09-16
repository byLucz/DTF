using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Discord;

namespace DiscordTelegramFrontier
{
    public static class TelegramRenderer
    {
        public static (string text, string image) Render(string message, Embed embed)
            => RenderMessage(message, embed == null ? Array.Empty<Embed>() : new[] { embed });

        internal static Embed[] CombineEmbeds(Embed embed, IEnumerable<Embed> embeds)
            => (embed == null ? Enumerable.Empty<Embed>() : new[] { embed })
                .Concat(embeds ?? Enumerable.Empty<Embed>()).Where(e => e != null).ToArray();

        internal static (string text, string image) RenderMessage(string message, IReadOnlyList<Embed> embeds)
        {
            var image = embeds.Select(e => e.Image?.Url ?? e.Thumbnail?.Url)
                .FirstOrDefault(url => !string.IsNullOrEmpty(url));
            var remaining = image == null ? 4096 : 1024;
            var sb = new StringBuilder();

            void Append(string value, string tag = null)
            {
                if (string.IsNullOrWhiteSpace(value) || remaining == 0)
                    return;
                if (sb.Length != 0)
                {
                    sb.Append('\n');
                    remaining--;
                }
                if (remaining == 0)
                    return;
                if (value.Length > remaining)
                {
                    var length = remaining - 1;
                    if (length > 0 && char.IsHighSurrogate(value[length - 1]))
                        length--;
                    value = value.Substring(0, length) + "…";
                }
                remaining -= value.Length;
                if (tag != null) sb.Append('<').Append(tag).Append('>');
                sb.Append(WebUtility.HtmlEncode(value));
                if (tag != null) sb.Append("</").Append(tag).Append('>');
            }

            Append(message);
            foreach (var embed in embeds)
            {
                Append(embed.Author?.Name, "b");
                Append(embed.Title, "b");
                Append(embed.Description);
                foreach (var field in embed.Fields)
                    Append(field.Name + ": " + field.Value);
                Append(embed.Footer?.Text, "i");
            }

            return (sb.Length == 0 && image == null ? "(empty)" : sb.ToString(), image);
        }
    }
}
