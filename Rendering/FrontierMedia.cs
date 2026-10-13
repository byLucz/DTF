using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordTelegramFrontier
{
    internal static class FrontierMedia
    {
        private const int MaxPage = 1024 * 1024;
        private const int MaxCache = 512;

        private static readonly Regex Link = new(
            @"https?://(?:(?:www\.)?tenor\.com/(?:[a-z-]+/)?view/[^\s<>]+|(?:www\.)?giphy\.com/gifs/[^\s<>]+|media\d*\.(?:tenor|giphy)\.com/[^\s<>]+|[^\s<>]+\.(?:gif|mp4|webm)(?:\?[^\s<>]*)?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Meta = new(
            @"<meta[^>]+(?:property|name)=""(?<key>og:video:secure_url|og:video|twitter:player:stream|og:image)""[^>]+content=""(?<url>[^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GiphyId = new(@"giphy\.com/gifs/(?:[^/\s]*-)?(?<id>[A-Za-z0-9]+)/?(?:[?#]|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly ConcurrentDictionary<string, string> Cache = new();

        static FrontierMedia()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; DTF)");
        }

        public static async Task<(string Text, string Media)> ExtractAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (text, null);

            var match = Link.Match(text);
            if (!match.Success)
                return (text, null);

            var media = await ResolveAsync(match.Value, cancellationToken).ConfigureAwait(false);
            if (media == null)
                return (text, null);

            var stripped = (text.Substring(0, match.Index) + text.Substring(match.Index + match.Length)).Trim();
            return (stripped, media);
        }

        private static async Task<string> ResolveAsync(string url, CancellationToken cancellationToken)
        {
            if (IsMedia(url))
                return url;

            if (Cache.TryGetValue(url, out var cached))
                return cached.Length == 0 ? null : cached;

            string media = null;
            if (GiphyId.Match(url) is { Success: true } giphy)
                media = $"https://media.giphy.com/media/{giphy.Groups["id"].Value}/giphy.mp4";
            else
            {
                try
                {
                    using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength is null or <= MaxPage)
                    {
                        var page = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        string fallback = null;
                        foreach (Match meta in Meta.Matches(page))
                        {
                            var candidate = System.Net.WebUtility.HtmlDecode(meta.Groups["url"].Value);
                            if (!IsMedia(candidate)) continue;
                            if (candidate.Contains(".mp4", StringComparison.OrdinalIgnoreCase)) { media = candidate; break; }
                            fallback ??= candidate;
                        }
                        media ??= fallback;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                }
            }

            if (Cache.Count >= MaxCache)
                Cache.Clear();
            Cache[url] = media ?? "";
            return media;
        }

        private static bool IsMedia(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.AbsolutePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                    || uri.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                    || uri.AbsolutePath.EndsWith(".webm", StringComparison.OrdinalIgnoreCase));
    }
}
