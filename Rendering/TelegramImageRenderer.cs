using System;
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
        private static readonly EmojiImageCache EmojiCache = new();
        private static readonly EmojiImageCache PictureCache = new();
        private const int MaxEmojiBytes = 512 * 1024;
        private const int MaxPictureBytes = 8 * 1024 * 1024;
        private const int MaxPictureSide = 4096;
        private const float MaxThumbnailWidth = 240;
        private static readonly SemaphoreSlim Downloads = new(4, 4);
        private static readonly Regex Words = new(@"\r\n|\r|\n|[^\S\r\n]+|[^\s]+", RegexOptions.Compiled);

        public async Task<byte[]> RenderAsync(string content, IReadOnlyList<Embed> embeds = null, CancellationToken cancellationToken = default)
        {
            embeds ??= Array.Empty<Embed>();
            var blocks = new List<Block>();
            Add(content, 30, SKColors.White);
            foreach (var embed in embeds)
            {
                if (embed == null) continue;
                var accent = embed.Color is { } color ? new SKColor(color.R, color.G, color.B) : new SKColor(91, 160, 238);
                blocks.Add(new Block(null, 0, accent));
                Add(embed.Author?.Name, 23, new SKColor(174, 188, 204), url: embed.Author?.Url, icon: embed.Author?.IconUrl);
                Add(embed.Title, 36, SKColors.White, true, embed.Url);
                Add(embed.Description, 30, new SKColor(231, 237, 244));
                foreach (var row in TelegramRenderer.FieldRows(embed.Fields))
                {
                    if (row.Length == 1)
                    {
                        Add(row[0].Name, 25, SKColors.White, true);
                        Add(row[0].Value, 28, new SKColor(214, 225, 236));
                        continue;
                    }

                    var columns = row.Select(field =>
                    {
                        var column = new List<Block>();
                        Add(field.Name, 25, SKColors.White, true, target: column);
                        Add(field.Value, 28, new SKColor(214, 225, 236), target: column);
                        return column;
                    }).ToArray();
                    blocks.Add(new Block(null, 0, accent, Columns: columns));
                }
                if (DiscordMarkdown.IsLink(embed.Image?.Url)) blocks.Add(new Block(null, 0, accent, Picture: embed.Image.Value.Url));
                else if (DiscordMarkdown.IsLink(embed.Thumbnail?.Url)) blocks.Add(new Block(null, 0, accent, Picture: embed.Thumbnail.Value.Url, Thumbnail: true));
                Add(embed.Footer?.Text, 22, new SKColor(151, 168, 185), icon: embed.Footer?.IconUrl);

            }
            if (!blocks.Any(b => b.Content != null || b.Columns != null)) Add("(empty)", 30, SKColors.White);
            var ids = blocks.SelectMany(b => b.Columns?.SelectMany(column => column) ?? new[] { b })
                .Where(b => b.Content != null).SelectMany(b => b.Content.Runs)
                .Where(run => run.EmojiId != null && !run.Style.HasFlag(TextStyle.Spoiler))
                .Select(run => run.EmojiId).Distinct().Take(100).ToArray();
            var pictureUrls = blocks.Where(b => b.Picture != null).Select(b => b.Picture)
                .Concat(blocks.Where(b => b.Icon != null).Select(b => b.Icon))
                .Distinct().Take(6).ToArray();
            var assets = await Task.WhenAll(ids.Select(id => DownloadAsync(EmojiCache, id,
                $"https://cdn.discordapp.com/emojis/{id}.png?size=96", MaxEmojiBytes, cancellationToken))).ConfigureAwait(false);
            var pictureAssets = await Task.WhenAll(pictureUrls.Select(url => DownloadAsync(PictureCache, url, url,
                MaxPictureBytes, cancellationToken))).ConfigureAwait(false);
            var images = new Dictionary<string, SKBitmap>();
            var pictures = new Dictionary<string, SKBitmap>();
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
                for (var i = 0; i < pictureUrls.Length; i++)
                {
                    if (pictureAssets[i] == null) continue;
                    using var data = SKData.CreateCopy(pictureAssets[i]);
                    using var codec = SKCodec.Create(data);
                    if (codec == null || codec.Info.Width > MaxPictureSide || codec.Info.Height > MaxPictureSide) continue;
                    var bitmap = SKBitmap.Decode(codec);
                    if (bitmap != null) pictures[pictureUrls[i]] = bitmap;
                }
                foreach (var (url, bitmap) in pictures)
                    images[url] = bitmap;
                cancellationToken.ThrowIfCancellationRequested();
                using var recorder = new SKPictureRecorder();
                using var fonts = new RenderFontCache();
                var canvas = recorder.BeginRecording(new SKRect(0, 0, Width, MaxHeight));
                var y = Padding;
                foreach (var block in blocks)
                {
                    if (y > MaxHeight - 120) break;
                    if (block.Picture != null)
                    {
                        y = DrawPicture(canvas, block, pictures, fonts, y, cancellationToken) + 18;
                        continue;
                    }
                    if (block.Columns != null)
                    {
                        y = DrawColumns(canvas, block, images, fonts, y, cancellationToken) + 18;
                        continue;
                    }
                    if (block.Content == null)
                    {
                        if (y > Padding) y += 16;
                        using var rule = new SKPaint { Color = block.Color, IsAntialias = true };
                        canvas.DrawRoundRect(new SKRect(Padding, y, Padding + 56, y + 5), 2, 2, rule);
                        y += 27;
                        continue;
                    }
                    y = DrawBlock(canvas, block, images, fonts, y, cancellationToken) + 18;
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
                foreach (var bitmap in images.Values.Concat(pictures.Values).Distinct()) bitmap.Dispose();
            }

            void Add(string text, float size, SKColor foreground, bool bold = false, string url = null, List<Block> target = null,
                string icon = null)
            {
                if (!DiscordMarkdown.IsLink(icon)) icon = null;
                if (string.IsNullOrWhiteSpace(text)) return;
                foreach (var parsed in DiscordMarkdown.Parse(text))
                {
                    var content = parsed with
                    {
                        Runs = parsed.Runs.Select(run => run with
                        {
                            Url = run.Url ?? (DiscordMarkdown.IsLink(url) ? url : null)
                        }).ToArray()
                    };
                    var scale = parsed.Heading switch { 1 => 1.5f, 2 => 1.3f, 3 => 1.15f, _ => parsed.Small ? 0.8f : 1 };
                    (target ?? blocks).Add(new Block(content, size * scale, parsed.Small ? new SKColor(151, 168, 185) : foreground,
                        bold || parsed.Heading > 0, Icon: icon));
                    icon = null;
                }
            }
        }

        private static float DrawPicture(SKCanvas canvas, Block block, IReadOnlyDictionary<string, SKBitmap> pictures,
            RenderFontCache fonts, float top, CancellationToken cancellationToken)
        {
            if (!pictures.TryGetValue(block.Picture, out var bitmap))
            {
                var link = DiscordMarkdown.Parse(block.Picture).FirstOrDefault();
                return link == null ? top : DrawBlock(canvas, new Block(link, 20, new SKColor(130, 177, 228)), pictures, fonts, top, cancellationToken);
            }

            var maxWidth = block.Thumbnail ? Math.Min(MaxThumbnailWidth, bitmap.Width * 2f) : Width - Padding * 2;
            var maxHeight = MaxHeight - 120 - top;
            var scale = Math.Min(maxWidth / bitmap.Width, maxHeight / bitmap.Height);
            if (scale <= 0) return top;

            var rect = new SKRect(Padding, top, Padding + bitmap.Width * scale, top + bitmap.Height * scale);
            using var image = SKImage.FromBitmap(bitmap);
            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(rect, 12), antialias: true);
            canvas.DrawImage(image, rect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            canvas.Restore();
            return rect.Bottom;
        }

        private static float DrawColumns(SKCanvas canvas, Block block, IReadOnlyDictionary<string, SKBitmap> images,
            RenderFontCache fonts, float top, CancellationToken cancellationToken)
        {
            const float gap = 28;
            var count = block.Columns.Length;
            var width = (Width - Padding * 2 - gap * (count - 1)) / count;
            var bottom = top;
            for (var i = 0; i < count; i++)
            {
                var left = Padding + i * (width + gap);
                var y = top;
                foreach (var cell in block.Columns[i])
                    y = DrawBlock(canvas, cell, images, fonts, y, cancellationToken, left, left + width) + 6;
                bottom = Math.Max(bottom, y - 6);
            }
            return bottom;
        }

        private static float DrawBlock(SKCanvas canvas, Block block, IReadOnlyDictionary<string, SKBitmap> images,
            RenderFontCache fonts, float top, CancellationToken cancellationToken, float start = Padding, float end = Width - Padding)
        {
            var lineHeight = block.Size * 1.65f;
            var left = start + (block.Content.Quote ? 22 : 0);
            var x = left;
            var y = top;
            if (block.Icon != null && images.TryGetValue(block.Icon, out var icon))
            {
                var size = block.Size * 1.3f;
                var rect = new SKRect(x, y + (lineHeight - size) / 2, x + size, y + (lineHeight + size) / 2);
                using var iconImage = SKImage.FromBitmap(icon);
                canvas.Save();
                canvas.ClipRoundRect(new SKRoundRect(rect, size / 2), antialias: true);
                canvas.DrawImage(iconImage, rect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                canvas.Restore();
                x += size + 10;
            }
            for (var runIndex = 0; runIndex < block.Content.Runs.Count; runIndex++)
            {
                var run = block.Content.Runs[runIndex];
                if (y >= MaxHeight - 120) break;
                var code = (run.Style & (TextStyle.Code | TextStyle.Pre)) != 0;
                var bold = !code && (block.Bold || run.Style.HasFlag(TextStyle.Bold));
                var italic = !code && run.Style.HasFlag(TextStyle.Italic);
                var fontResource = fonts.GetFont(code, bold, italic, block.Size);
                var font = fontResource.Font;
                using var paint = new SKPaint
                {
                    Color = !code && run.Url != null ? new SKColor(130, 177, 228) : block.Color,
                    IsAntialias = true
                };
                using var background = new SKPaint { Color = new SKColor(15, 23, 33), IsAntialias = true };
                var spoiler = run.Style.HasFlag(TextStyle.Spoiler);
                if (run.Style.HasFlag(TextStyle.Pre) && x > left) NewLine();
                if (run.EmojiId != null)
                {
                    var size = block.Size * 1.4f;
                    Wrap(size + 4);
                    if (y < MaxHeight - 120)
                    {
                        if (spoiler)
                        {
                            canvas.DrawRect(new SKRect(x, y + 5, x + size + 4, y + lineHeight - 5), background);
                            x += size + 4;
                        }
                        else if (images.TryGetValue(run.EmojiId, out var bitmap))
                        {
                            var scale = Math.Min(size / bitmap.Width, size / bitmap.Height);
                            var width = bitmap.Width * scale;
                            var height = bitmap.Height * scale;
                            canvas.DrawBitmap(bitmap, new SKRect(x + (size - width) / 2, y + (lineHeight - height) / 2,
                                x + (size + width) / 2, y + (lineHeight + height) / 2));
                            Decorate(size + 4, y + lineHeight * 0.75f);
                            x += size + 4;
                        }
                        else DrawWords(":" + run.Text.Split(':')[1] + ":");
                    }
                }
                else DrawWords(run.Text);
                if (run.Style.HasFlag(TextStyle.Pre) && x > left && runIndex + 1 < block.Content.Runs.Count
                    && !block.Content.Runs[runIndex + 1].Text.StartsWith('\n')) NewLine();

                void DrawWords(string text)
                {
                    foreach (Match word in Words.Matches(text.Replace("\t", "    ")))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (y >= MaxHeight - 120) return;
                        if (word.Value is "\n" or "\r" or "\r\n") { NewLine(); continue; }
                        if (!string.IsNullOrWhiteSpace(word.Value)) Wrap(font.MeasureText(word.Value));
                        var elements = StringInfo.GetTextElementEnumerator(word.Value);
                        while (elements.MoveNext())
                        {
                            var element = elements.GetTextElement();
                            var glyph = fonts.GetGlyph(fontResource, element);
                            var glyphFont = glyph.Font.Font;
                            var width = glyph.Width;
                            Wrap(width);
                            if (y >= MaxHeight - 120) return;
                            var baseline = y + (lineHeight - glyph.Font.Metrics.Descent - glyph.Font.Metrics.Ascent) / 2;
                            if (code || spoiler) canvas.DrawRect(new SKRect(x, y + 5, x + width + 0.5f, y + lineHeight - 5), background);
                            if (!spoiler)
                            {
                                if (!glyph.Shaped)
                                    canvas.DrawText(element, x, baseline, SKTextAlign.Left, glyphFont, paint);
                                else
                                    canvas.DrawShapedText(glyph.Font.Shaper, element, x, baseline, SKTextAlign.Left, glyphFont, paint);
                                Decorate(width, baseline);
                            }
                            x += width;
                        }
                    }
                }

                void Decorate(float width, float baseline)
                {
                    if (code) return;
                    paint.StrokeWidth = Math.Max(1.5f, block.Size / 18);
                    if (run.Style.HasFlag(TextStyle.Underline) || run.Url != null)
                        canvas.DrawLine(x, baseline + 3, x + width, baseline + 3, paint);
                    if (run.Style.HasFlag(TextStyle.Strike))
                        canvas.DrawLine(x, baseline - block.Size * 0.3f, x + width, baseline - block.Size * 0.3f, paint);
                }
            }
            if (block.Content.Quote)
            {
                using var rule = new SKPaint { Color = new SKColor(130, 150, 170), IsAntialias = true };
                canvas.DrawRoundRect(new SKRect(Padding, top + 5, Padding + 4, Math.Min(y + lineHeight - 5, MaxHeight - 120)), 2, 2, rule);
            }
            return y + lineHeight;

            void NewLine() { x = left; y += lineHeight; }

            void Wrap(float width)
            {
                if (x > left && x + width > end) NewLine();
            }
        }

        private static async Task<byte[]> DownloadAsync(EmojiImageCache cache, string key, string url, int maxBytes, CancellationToken ct)
        {
            if (cache.TryGet(key, out var cached)) return cached;
            await Downloads.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (cache.TryGet(key, out cached)) return cached;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes) return Remember(null);
                using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var output = new MemoryStream();
                var buffer = new byte[8192];
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
                {
                    if (output.Length + read > maxBytes) return Remember(null);
                    output.Write(buffer, 0, read);
                }
                return Remember(output.ToArray());
            }
            catch (HttpRequestException) { return Remember(null); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Remember(null); }
            finally { Downloads.Release(); }

            byte[] Remember(byte[] bytes) => cache.Remember(key, bytes);
        }

        private sealed record Block(TextBlock Content, float Size, SKColor Color, bool Bold = false,
            string Picture = null, bool Thumbnail = false, List<Block>[] Columns = null, string Icon = null);
    }
}
