using System;
using System.Collections.Generic;

namespace DiscordTelegramFrontier
{
    internal sealed class EmojiImageCache
    {
        private const int MaxEntries = 256;
        private const int MaxBytes = 16 * 1024 * 1024;
        private readonly object _gate = new();
        private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
        private readonly LinkedList<Entry> _recent = new();
        private long _bytes;

        public bool TryGet(string id, out byte[] bytes)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(id, out var node))
                {
                    if (node.Value.Expires > DateTimeOffset.UtcNow)
                    {
                        _recent.Remove(node);
                        _recent.AddFirst(node);
                        bytes = node.Value.Bytes;
                        return true;
                    }
                    Remove(node);
                }
                bytes = null;
                return false;
            }
        }

        public byte[] Remember(string id, byte[] bytes)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(id, out var previous)) Remove(previous);
                var node = _recent.AddFirst(new Entry(id, bytes, DateTimeOffset.UtcNow.AddMinutes(bytes == null ? 1 : 60)));
                _entries.Add(id, node);
                _bytes += bytes?.Length ?? 0;
                while (_entries.Count > MaxEntries || _bytes > MaxBytes) Remove(_recent.Last);
                return bytes;
            }
        }

        private void Remove(LinkedListNode<Entry> node)
        {
            _entries.Remove(node.Value.Id);
            _recent.Remove(node);
            _bytes -= node.Value.Bytes?.Length ?? 0;
        }

        private sealed record Entry(string Id, byte[] Bytes, DateTimeOffset Expires);
    }
}
