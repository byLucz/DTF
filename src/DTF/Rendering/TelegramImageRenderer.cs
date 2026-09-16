using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace DiscordTelegramFrontier
{
    public sealed class TelegramImageRenderer
    {
        private const int Width = 1000;
        private const int MaxHeight = 6000;
        private const float Padding = 40;
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly ConcurrentDictionary<string, CachedEmoji> EmojiCache = new();
        private static readonly SemaphoreSlim Downloads = new(4, 4);
        private static readonly Regex EmojiPattern = new(@"<a?:(?<name>[A-Za-z0-9_]+):(?<id>[0-9]+)>", RegexOptions.Compiled);
        private static readonly Regex Words = new(@"\r\n|\r|\n|[^\S\r\n]+|[^\s]+", RegexOptions.Compiled);

        public async Task<byte[]> RenderAsync(string content, IReadOnlyList<Embed> embeds = null, CancellationToken cancellationToken = default)
        {
            embeds ??= Array.Empty<Embed>();
            var blocks = new List<Block>();
            if (!string.IsNullOrWhiteSpace(content)) blocks.Add(new Block(content, 30, SKColors.White));
            foreach (var embed in embeds)
            {
                if (embed == null) continue;
                var accent = embed.Color is { } color ? new SKColor(color.R, color.G, color.B) : new SKColor(91, 160, 238);
                blocks.Add(new Block(null, 0, accent));
                Add(embed.Author?.Name, 23, new SKColor(174, 188, 204));
                Add(embed.Title, 36, SKColors.White, true);
                Add(embed.Description, 30, new SKColor(231, 237, 244));
                foreach (var field in embed.Fields)
                {
                    Add(field.Name, 25, SKColors.White, true);
                    Add(field.Value, 28, new SKColor(214, 225, 236));
                }
                if (embed.Image?.Url is { } imageUrl) Add(imageUrl, 20, new SKColor(130, 177, 228));
                else if (embed.Thumbnail?.Url is { } thumbnailUrl) Add(thumbnailUrl, 20, new SKColor(130, 177, 228));
                Add(embed.Footer?.Text, 22, new SKColor(151, 168, 185));

                void Add(string text, float size, SKColor foreground, bool bold = false)
                {
                    if (!string.IsNullOrWhiteSpace(text)) blocks.Add(new Block(text, size, foreground, bold));
                }
            }
            if (!blocks.Any(b => b.Text != null)) blocks.Add(new Block("(empty)", 30, SKColors.White));
            var ids = blocks.Where(b => b.Text != null).SelectMany(b => EmojiPattern.Matches(b.Text).Cast<Match>())
                .Select(m => m.Groups["id"].Value).Distinct().Take(100).ToArray();
            var assets = await Task.WhenAll(ids.Select(id => DownloadEmojiAsync(id, cancellationToken))).ConfigureAwait(false);
            var images = new Dictionary<string, SKBitmap>();
            try
            {
                for (var i = 0; i < ids.Length; i++)
                {
                    if (assets[i] == null) continue;
                    using var data = SKData.CreateCopy(assets[i]);
                    using var codec = SKCodec.Create(data);
                    if (codec == null || codec.Info.Width > 1024 || codec.Info.Height > 1024) continue;
                    var bitmap = SKBitmap.Decode(codec);
                    if (bitmap != null) images[ids[i]] = bitmap;
                }
                cancellationToken.ThrowIfCancellationRequested();
                using var recorder = new SKPictureRecorder();
                var canvas = recorder.BeginRecording(new SKRect(0, 0, Width, MaxHeight));
                var y = Padding;
                foreach (var block in blocks)
                {
                    if (y > MaxHeight - 120) break;
                    if (block.Text == null)
                    {
                        if (y > Padding) y += 16;
                        using var rule = new SKPaint { Color = block.Color, IsAntialias = true };
                        canvas.DrawRoundRect(new SKRect(Padding, y, Padding + 56, y + 5), 2, 2, rule);
                        y += 27;
                        continue;
                    }
                    y = DrawBlock(canvas, block, images, y) + 18;
                }
                if (y > MaxHeight - 120)
                {
                    using var font = new SKFont(SKTypeface.Default, 24);
                    using var paint = new SKPaint { Color = new SKColor(151, 168, 185), IsAntialias = true };
                    canvas.DrawText("…", Padding, MaxHeight - 40, SKTextAlign.Left, font, paint);
                    y = MaxHeight - Padding;
                }
                using var picture = recorder.EndRecording();
                var height = Math.Clamp((int)Math.Ceiling(y + Padding - 18), 180, MaxHeight);
                using var surface = SKSurface.Create(new SKImageInfo(Width, height));
                surface.Canvas.Clear(new SKColor(24, 35, 48));
                surface.Canvas.DrawPicture(picture);
                using var snapshot = surface.Snapshot();
                using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
                return png.ToArray();
            }
            finally
            {
                foreach (var bitmap in images.Values) bitmap.Dispose();
            }
        }

        private static float DrawBlock(SKCanvas canvas, Block block, IReadOnlyDictionary<string, SKBitmap> images, float top)
        {
            using var typeface = SKTypeface.FromFamilyName("Arial", block.Bold ? SKFontStyle.Bold : SKFontStyle.Normal);
            using var font = new SKFont(typeface, block.Size);
            using var paint = new SKPaint { Color = block.Color, IsAntialias = true };
            var lineHeight = block.Size * 1.65f;
            var x = Padding;
            var y = top;
            var position = 0;
            foreach (Match emoji in EmojiPattern.Matches(block.Text))
            {
                DrawWords(block.Text.Substring(position, emoji.Index - position));
                if (images.TryGetValue(emoji.Groups["id"].Value, out var bitmap))
                {
                    var size = block.Size * 1.4f;
                    Wrap(size + 4);
                    if (y < MaxHeight - 120)
                    {
                        var scale = Math.Min(size / bitmap.Width, size / bitmap.Height);
                        var width = bitmap.Width * scale;
                        var height = bitmap.Height * scale;
                        canvas.DrawBitmap(bitmap, new SKRect(x + (size - width) / 2, y + (lineHeight - height) / 2,
                            x + (size + width) / 2, y + (lineHeight + height) / 2));
                        x += size + 4;
                    }
                }
                else DrawWords(":" + emoji.Groups["name"].Value + ":");
                position = emoji.Index + emoji.Length;
            }
            DrawWords(block.Text.Substring(position));
            return y + lineHeight;

            void Wrap(float width)
            {
                if (x > Padding && x + width > Width - Padding)
                {
                    x = Padding;
                    y += lineHeight;
                }
            }

            void DrawWords(string text)
            {
                foreach (Match word in Words.Matches(text))
                {
                    if (y >= MaxHeight - 120) return;
                    if (word.Value is "\n" or "\r" or "\r\n")
                    {
                        x = Padding;
                        y += lineHeight;
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(word.Value))
                    {
                        if (x > Padding) x += font.MeasureText(" ");
                        continue;
                    }
                    Wrap(font.MeasureText(word.Value));
                    var elements = StringInfo.GetTextElementEnumerator(word.Value);
                    while (elements.MoveNext())
                    {
                        var element = elements.GetTextElement();
                        var codePoint = char.ConvertToUtf32(element, 0);
                        using var fallback = font.ContainsGlyphs(element) ? null : SKFontManager.Default.MatchCharacter(codePoint);
                        using var glyphFont = new SKFont(fallback ?? typeface, block.Size);
                        var needsShaping = element.Length > (codePoint > 0xFFFF ? 2 : 1);
                        using var shaper = needsShaping ? new SKShaper(glyphFont.Typeface) : null;
                        var width = shaper == null ? glyphFont.MeasureText(element) : shaper.Shape(element, glyphFont).Width;
                        Wrap(width);
                        if (y >= MaxHeight - 120) return;
                        var baseline = y + (lineHeight - glyphFont.Metrics.Descent - glyphFont.Metrics.Ascent) / 2;
                        if (shaper == null)
                            canvas.DrawText(element, x, baseline, SKTextAlign.Left, glyphFont, paint);
                        else
                            canvas.DrawShapedText(shaper, element, x, baseline, SKTextAlign.Left, glyphFont, paint);
                        x += width;
                    }
                }
            }
        }

        private static async Task<byte[]> DownloadEmojiAsync(string id, CancellationToken ct)
        {
            if (EmojiCache.TryGetValue(id, out var cached) && cached.Expires > DateTimeOffset.UtcNow) return cached.Bytes;
            await Downloads.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (EmojiCache.TryGetValue(id, out cached) && cached.Expires > DateTimeOffset.UtcNow) return cached.Bytes;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await Http.GetAsync($"https://cdn.discordapp.com/emojis/{id}.png?size=96", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 512 * 1024) return Remember(null);
                using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var output = new MemoryStream();
                var buffer = new byte[8192];
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
                {
                    if (output.Length + read > 512 * 1024) return Remember(null);
                    output.Write(buffer, 0, read);
                }
                return Remember(output.ToArray());
            }
            catch (HttpRequestException) { return Remember(null); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Remember(null); }
            finally { Downloads.Release(); }

            byte[] Remember(byte[] bytes)
            {
                if (EmojiCache.Count >= 256) EmojiCache.Clear();
                EmojiCache[id] = new CachedEmoji(bytes, DateTimeOffset.UtcNow.AddMinutes(bytes == null ? 1 : 60));
                return bytes;
            }
        }

        private sealed record CachedEmoji(byte[] Bytes, DateTimeOffset Expires);
        private sealed record Block(string Text, float Size, SKColor Color, bool Bold = false);
    }
}
