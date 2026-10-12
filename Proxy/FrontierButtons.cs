using Discord;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Telegram.Bot.Types.ReplyMarkups;

namespace DiscordTelegramFrontier
{
    internal sealed class FrontierButtons : IDisposable
    {
        internal const string Prefix = "dtf:";
        private static readonly TimeSpan MaxSweepInterval = TimeSpan.FromMinutes(5);

        private readonly ConcurrentDictionary<string, Entry> _entries = new();
        private readonly ConcurrentDictionary<ulong, string[]> _byMessage = new();
        private readonly TimeSpan _lifetime;
        private long _next;
        private Timer _sweeper;

        internal sealed class Entry
        {
            public Entry(FrontierProxyMessage message, string customId, ComponentType type, string[] values)
            {
                Message = message;
                CustomId = customId;
                Type = type;
                Values = values;
                Seen = DateTimeOffset.UtcNow;
            }

            public FrontierProxyMessage Message { get; }
            public string CustomId { get; }
            public ComponentType Type { get; }
            public string[] Values { get; }
            public DateTimeOffset Seen { get; set; }
        }

        public FrontierButtons(TimeSpan lifetime)
        {
            _lifetime = lifetime <= TimeSpan.Zero ? TimeSpan.FromHours(1) : lifetime;
        }

        public bool TryGet(string data, out Entry entry)
        {
            if (!_entries.TryGetValue(data, out entry))
                return false;

            if (DateTimeOffset.UtcNow - entry.Seen > _lifetime)
            {
                Release(entry.Message.Id);
                entry = null;
                return false;
            }

            entry.Seen = DateTimeOffset.UtcNow;
            return true;
        }

        public InlineKeyboardMarkup Build(FrontierProxyMessage message, MessageComponent components)
        {
            Release(message.Id);

            if (components?.Components is not { Count: > 0 })
                return null;

            var keys = new List<string>();
            var rows = new List<List<InlineKeyboardButton>>();

            foreach (var row in components.Components.OfType<ActionRowComponent>())
            {
                var line = new List<InlineKeyboardButton>();

                foreach (var component in row.Components)
                {
                    switch (component)
                    {
                        case ButtonComponent { Style: ButtonStyle.Link, Url: { Length: > 0 } url } link:
                            line.Add(InlineKeyboardButton.WithUrl(Label(link.Label, link.Emote), url));
                            break;

                        case ButtonComponent { CustomId: { Length: > 0 } customId } button:
                            line.Add(InlineKeyboardButton.WithCallbackData(Label(button.Label, button.Emote),
                                button.IsDisabled ? Prefix : Register(message, customId, ComponentType.Button, Array.Empty<string>(), keys)));
                            break;

                        case SelectMenuComponent { Type: ComponentType.SelectMenu, CustomId: { Length: > 0 } menuId } menu:
                            if (line.Count > 0)
                            {
                                rows.Add(line);
                                line = new List<InlineKeyboardButton>();
                            }
                            foreach (var option in menu.Options)
                                rows.Add(new List<InlineKeyboardButton>
                                {
                                    InlineKeyboardButton.WithCallbackData(Label(option.Label, option.Emote),
                                        menu.IsDisabled ? Prefix : Register(message, menuId, ComponentType.SelectMenu, new[] { option.Value }, keys))
                                });
                            break;
                    }
                }

                if (line.Count > 0)
                    rows.Add(line);
            }

            if (keys.Count > 0)
            {
                _byMessage[message.Id] = keys.ToArray();
                EnsureSweeper();
            }

            return rows.Count == 0 ? null : new InlineKeyboardMarkup(rows);
        }

        public void Release(ulong messageId)
        {
            if (!_byMessage.TryRemove(messageId, out var keys))
                return;

            foreach (var key in keys)
                _entries.TryRemove(key, out _);
        }

        public static string Signature(MessageComponent components)
        {
            if (components?.Components is not { Count: > 0 })
                return "";

            var sb = new StringBuilder();
            foreach (var row in components.Components.OfType<ActionRowComponent>())
            {
                sb.Append('|');
                foreach (var component in row.Components)
                {
                    switch (component)
                    {
                        case ButtonComponent button:
                            sb.Append("b\u001f").Append(button.CustomId).Append('\u001f').Append(button.Url).Append('\u001f')
                                .Append(Label(button.Label, button.Emote)).Append('\u001f').Append(button.IsDisabled).Append('\u001e');
                            break;
                        case SelectMenuComponent menu:
                            sb.Append("s\u001f").Append(menu.CustomId).Append('\u001f').Append(menu.Type).Append('\u001f').Append(menu.IsDisabled);
                            foreach (var option in menu.Options)
                                sb.Append('\u001f').Append(option.Value).Append('\u001f').Append(Label(option.Label, option.Emote));
                            sb.Append('\u001e');
                            break;
                    }
                }
            }
            return sb.ToString();
        }

        private string Register(FrontierProxyMessage message, string customId, ComponentType type, string[] values, List<string> keys)
        {
            var key = Prefix + Base36(Interlocked.Increment(ref _next));
            _entries[key] = new Entry(message, customId, type, values);
            keys.Add(key);
            return key;
        }

        private void EnsureSweeper()
        {
            if (Volatile.Read(ref _sweeper) != null)
                return;

            var interval = _lifetime < MaxSweepInterval ? _lifetime : MaxSweepInterval;
            var timer = new Timer(_ => Sweep(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (Interlocked.CompareExchange(ref _sweeper, timer, null) != null)
            {
                timer.Dispose();
                return;
            }
            timer.Change(interval, interval);
        }

        public void Dispose() => Interlocked.Exchange(ref _sweeper, null)?.Dispose();

        private void Sweep()
        {
            var cut = DateTimeOffset.UtcNow - _lifetime;
            foreach (var pair in _entries)
                if (pair.Value.Seen < cut)
                    Release(pair.Value.Message.Id);
        }

        private static string Label(string label, IEmote emote)
        {
            var emoji = emote is Emoji unicode ? unicode.Name : null;
            var text = string.Join(" ", new[] { emoji, label }.Where(part => !string.IsNullOrWhiteSpace(part)));
            return text.Length == 0 ? "•" : text;
        }

        private static string Base36(long value)
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            var chars = new Stack<char>();
            do
            {
                chars.Push(digits[(int)(value % 36)]);
                value /= 36;
            }
            while (value > 0);
            return new string(chars.ToArray());
        }
    }
}
