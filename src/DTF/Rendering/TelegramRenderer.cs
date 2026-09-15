using System.Net;
using System.Text;
using Discord;

namespace DiscordTelegramFrontier
{
    public static class TelegramRenderer
    {
        public static (string text, string image) Render(string message, Embed embed)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(message))
                sb.AppendLine(Enc(message));

            if (embed != null)
            {
                if (!string.IsNullOrWhiteSpace(embed.Author?.Name))
                    sb.AppendLine($"<b>{Enc(embed.Author?.Name)}</b>");

                if (!string.IsNullOrWhiteSpace(embed.Title))
                    sb.AppendLine($"<b>{Enc(embed.Title)}</b>");

                if (!string.IsNullOrWhiteSpace(embed.Description))
                    sb.AppendLine(Enc(embed.Description));

                foreach (var f in embed.Fields)
                    sb.AppendLine($"<b>{Enc(f.Name)}</b>: {Enc(f.Value)}");

                if (!string.IsNullOrWhiteSpace(embed.Footer?.Text))
                    sb.AppendLine($"<i>{Enc(embed.Footer?.Text)}</i>");
            }

            string image = embed?.Image?.Url ?? embed?.Thumbnail?.Url;
            return (sb.ToString().Trim(), image);
        }

        private static string Enc(string s) => WebUtility.HtmlEncode(s ?? "");
    }
}
