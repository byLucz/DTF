using Discord;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

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
            var blocks = new List<TextBlock>();
            void Append(string value, TextStyle style = TextStyle.None, string url = null)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                blocks.AddRange(DiscordMarkdown.Parse(value).Select(block => block with
                {
                    Runs = block.Runs.Select(run => run with
                    {
                        Style = run.Style | style,
                        Url = run.Url ?? (DiscordMarkdown.IsLink(url) ? url : null)
                    }).ToArray()
                }));
            }

            Append(message);
            foreach (var embed in embeds)
            {
                Append(embed.Author?.Name, TextStyle.Bold, embed.Author?.Url);
                Append(embed.Title, TextStyle.Bold, embed.Url);
                Append(embed.Description);
                foreach (var row in FieldRows(embed.Fields))
                {
                    if (row.Length > 1 && row.All(IsShort))
                    {
                        Append(string.Join(" / ", row.Select(field => $"**{field.Name.Trim()}** {field.Value.Trim()}")));
                        continue;
                    }
                    foreach (var field in row)
                    {
                        if (field.Inline && IsShort(field))
                        {
                            Append($"**{field.Name.Trim()}** {field.Value.Trim()}");
                            continue;
                        }
                        Append(field.Name, TextStyle.Bold);
                        Append(field.Value);
                    }
                }
                Append(embed.Footer?.Text, TextStyle.Italic);
            }

            var html = RenderHtml(blocks, image == null ? 4096 : 1024);
            return (html.Length == 0 && image == null ? "(empty)" : html, image);
        }

        internal static IEnumerable<EmbedField[]> FieldRows(IEnumerable<EmbedField> fields)
        {
            var row = new List<EmbedField>();
            foreach (var field in fields)
            {
                if (!field.Inline || row.Count == 3)
                {
                    if (row.Count > 0) yield return row.ToArray();
                    row.Clear();
                }
                if (!field.Inline)
                {
                    yield return new[] { field };
                    continue;
                }
                row.Add(field);
            }
            if (row.Count > 0) yield return row.ToArray();
        }

        private static bool IsShort(EmbedField field)
            => !string.IsNullOrWhiteSpace(field.Value) && !field.Value.Contains('\n') && field.Value.Length <= 48
                && !field.Name.Contains('\n') && !field.Value.Contains("```");

        private static string RenderHtml(IReadOnlyList<TextBlock> blocks, int limit)
        {
            var length = blocks.Sum(block => block.Runs.Sum(run => run.Text.Length)) + Math.Max(0, blocks.Count - 1);
            var truncated = length > limit;
            var remaining = truncated ? limit - 1 : limit;
            var html = new StringBuilder();
            for (var i = 0; i < blocks.Count && remaining > 0; i++)
            {
                if (i > 0) { html.Append('\n'); remaining--; }
                var block = blocks[i];
                var quoting = false;
                foreach (var run in block.Runs)
                {
                    if (run.EmojiId != null) continue;
                    var text = Take(run.Text, remaining);
                    if (text.Length == 0)
                    {
                        if (run.Text.Length != 0) { remaining = 0; break; }
                        continue;
                    }
                    remaining -= text.Length;
                    var style = run.Style | (block.Heading > 0 ? TextStyle.Bold : TextStyle.None)
                        | (block.Small ? TextStyle.Italic : TextStyle.None);
                    var code = (style & (TextStyle.Code | TextStyle.Pre)) != 0 && !style.HasFlag(TextStyle.Spoiler);
                    var quote = block.Quote && !code && run.Url == null;
                    if (quoting && !quote) html.Append("</blockquote>");
                    if (!quoting && quote) html.Append("<blockquote>");
                    quoting = quote;
                    var encoded = WebUtility.HtmlEncode(text);
                    if (code)
                    {
                        if (style.HasFlag(TextStyle.Pre))
                        {
                            encoded = run.Language == null ? "<pre>" + encoded + "</pre>"
                                : "<pre><code class=\"language-" + WebUtility.HtmlEncode(run.Language) + "\">" + encoded + "</code></pre>";
                        }
                        else encoded = "<code>" + encoded + "</code>";
                    }
                    else
                    {
                        if (style.HasFlag(TextStyle.Bold)) encoded = "<b>" + encoded + "</b>";
                        if (style.HasFlag(TextStyle.Italic)) encoded = "<i>" + encoded + "</i>";
                        if (style.HasFlag(TextStyle.Underline)) encoded = "<u>" + encoded + "</u>";
                        if (style.HasFlag(TextStyle.Strike)) encoded = "<s>" + encoded + "</s>";
                        if (style.HasFlag(TextStyle.Spoiler)) encoded = "<tg-spoiler>" + encoded + "</tg-spoiler>";
                        if (run.Url != null) encoded = "<a href=\"" + WebUtility.HtmlEncode(run.Url) + "\">" + encoded + "</a>";
                    }
                    html.Append(encoded);
                    if (text.Length < run.Text.Length) { remaining = 0; break; }
                }
                if (quoting) html.Append("</blockquote>");
            }
            if (truncated) html.Append('…');
            return html.ToString();
        }

        private static string Take(string text, int limit)
        {
            if (text.Length <= limit) return text;
            var length = 0;
            var elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                var next = elements.ElementIndex + elements.GetTextElement().Length;
                if (next > limit) break;
                length = next;
            }
            return text.Substring(0, length);
        }
    }
}
