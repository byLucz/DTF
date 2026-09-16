using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DiscordTelegramFrontier
{
    [Flags]
    internal enum TextStyle
    {
        None = 0,
        Bold = 1,
        Italic = 2,
        Underline = 4,
        Strike = 8,
        Spoiler = 16,
        Code = 32,
        Pre = 64
    }

    internal sealed record TextRun(string Text, TextStyle Style = TextStyle.None, string Url = null,
        string EmojiId = null, string Language = null);

    internal sealed record TextBlock(IReadOnlyList<TextRun> Runs, bool Quote = false, int Heading = 0, bool Small = false);

    internal static class DiscordMarkdown
    {
        private static readonly Regex Emoji = new(@"\G<a?:[A-Za-z0-9_]+:(?<id>[0-9]+)>", RegexOptions.Compiled);
        private static readonly Regex Url = new(@"\Ghttps?://[^\s<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ListItem = new(@"^(?<indent> *)(?:[-*] |(?<number>[0-9]+)\. )", RegexOptions.Compiled);
        private static readonly Regex Language = new(@"^[A-Za-z0-9_+#.-]+$", RegexOptions.Compiled);
        private static readonly (string Marker, TextStyle Style)[] Delimiters =
        {
            ("***", TextStyle.Bold | TextStyle.Italic), ("___", TextStyle.Underline | TextStyle.Italic),
            ("**", TextStyle.Bold), ("__", TextStyle.Underline), ("~~", TextStyle.Strike),
            ("||", TextStyle.Spoiler), ("*", TextStyle.Italic), ("_", TextStyle.Italic)
        };

        internal static IReadOnlyList<TextBlock> Parse(string text)
        {
            var blocks = new List<TextBlock>();
            ParseBlocks((text ?? "").Replace("\r\n", "\n").Replace('\r', '\n'), blocks, false, 0);
            return blocks;
        }

        private static void ParseBlocks(string text, List<TextBlock> blocks, bool quote, int depth)
        {
            var lines = text.Split('\n');
            var paragraph = new List<string>();
            var codeTicks = 0;
            var spoiler = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var literal = codeTicks > 0 || spoiler;
                if (!literal && depth < 8 && (line.StartsWith("> ") || line == ">" || line.StartsWith(">>> ") || line == ">>>"))
                {
                    Flush();
                    var quoted = new List<string>();
                    if (line.StartsWith(">>>"))
                    {
                        quoted.Add(line.Length == 3 ? "" : line.Substring(4));
                        quoted.AddRange(lines.Skip(i + 1));
                        i = lines.Length;
                    }
                    else
                    {
                        do
                        {
                            quoted.Add(lines[i].Length == 1 ? "" : lines[i].Substring(2));
                            i++;
                        } while (i < lines.Length && (lines[i].StartsWith("> ") || lines[i] == ">"));
                        i--;
                    }
                    ParseBlocks(string.Join("\n", quoted), blocks, true, depth + 1);
                    continue;
                }
                var heading = !literal ? line.TakeWhile(c => c == '#').Count() : 0;
                var small = !literal && line.StartsWith("-# ");
                if (small || heading is >= 1 and <= 3 && line.Length > heading && line[heading] == ' ')
                {
                    Flush();
                    blocks.Add(new TextBlock(ParseInline(line.Substring(small ? 3 : heading + 1)), quote, small ? 0 : heading, small));
                    continue;
                }
                var list = !literal ? ListItem.Match(line) : Match.Empty;
                if (list.Success)
                {
                    Flush();
                    var prefix = list.Groups["indent"].Value + (list.Groups["number"].Success ? list.Groups["number"].Value + ". " : "• ");
                    blocks.Add(new TextBlock(new[] { new TextRun(prefix) }.Concat(ParseInline(line.Substring(list.Length))).ToArray(), quote));
                    continue;
                }
                paragraph.Add(line);
                for (var p = 0; p < line.Length; p++)
                {
                    if (line[p] == '\\' && codeTicks == 0) { p++; continue; }
                    if (line[p] == '`')
                    {
                        var count = 1;
                        while (p + count < line.Length && line[p + count] == '`') count++;
                        if (codeTicks == 0) codeTicks = count;
                        else if (codeTicks == count) codeTicks = 0;
                        p += count - 1;
                    }
                    else if (codeTicks == 0 && line.AsSpan(p).StartsWith("||", StringComparison.Ordinal)) { spoiler = !spoiler; p++; }
                }
            }
            Flush();

            void Flush()
            {
                if (paragraph.Count == 0) return;
                blocks.Add(new TextBlock(ParseInline(string.Join("\n", paragraph)), quote));
                paragraph.Clear();
            }
        }

        private static IReadOnlyList<TextRun> ParseInline(string text)
        {
            var position = 0;
            var budget = new ParseBudget(text.Length);
            ReadInline(text, ref position, null, TextStyle.None, null, 0, budget, out var runs);
            return runs;
        }

        private static bool ReadInline(string text, ref int position, string closing, TextStyle style,
            string url, int depth, ParseBudget budget, out List<TextRun> runs)
        {
            var result = new List<TextRun>();
            runs = result;
            var plain = new StringBuilder();
            while (position < text.Length)
            {
                if (--budget.Remaining < 0)
                {
                    plain.Append(text.AsSpan(position));
                    position = text.Length;
                    break;
                }
                if (closing != null && text.AsSpan(position).StartsWith(closing, StringComparison.Ordinal)
                    && (closing != "_" || position + 1 == text.Length || !char.IsLetterOrDigit(text[position + 1])))
                {
                    position += closing.Length;
                    Flush();
                    return true;
                }
                var c = text[position];
                if (c == '\\' && position + 1 < text.Length && char.IsAscii(text[position + 1])
                    && !char.IsLetterOrDigit(text[position + 1]) && !char.IsWhiteSpace(text[position + 1]))
                {
                    plain.Append(text[position + 1]);
                    position += 2;
                    continue;
                }
                if (c == '`')
                {
                    var count = 1;
                    while (position + count < text.Length && text[position + count] == '`') count++;
                    var end = text.IndexOf(new string('`', count), position + count, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        Flush();
                        var code = text.Substring(position + count, end - position - count);
                        string language = null;
                        if (count >= 3)
                        {
                            var newline = code.IndexOf('\n');
                            if (newline >= 0 && (newline == 0 || Language.IsMatch(code.Substring(0, newline))))
                            {
                                language = newline == 0 ? null : code.Substring(0, newline);
                                code = code.Substring(newline + 1);
                            }
                            if (code.EndsWith('\n')) code = code.Substring(0, code.Length - 1);
                        }
                        result.Add(new TextRun(code, (count >= 3 ? TextStyle.Pre : TextStyle.Code) | (style & TextStyle.Spoiler), Language: language));
                        position = end + count;
                        continue;
                    }
                    plain.Append('`', count);
                    position += count;
                    continue;
                }
                if (c == '<')
                {
                    var emoji = Emoji.Match(text, position);
                    if (emoji.Success)
                    {
                        Flush();
                        result.Add(new TextRun(emoji.Value, style, url, emoji.Groups["id"].Value));
                        position += emoji.Length;
                        continue;
                    }
                    var end = text.IndexOf('>', position + 1);
                    if (end >= 0 && IsLink(text.Substring(position + 1, end - position - 1)))
                    {
                        Flush();
                        var link = text.Substring(position + 1, end - position - 1);
                        result.Add(new TextRun(link, style, url ?? link));
                        position = end + 1;
                        continue;
                    }
                }
                if (c == '[' && url == null && depth < 24 && TryLink(text, position, out var label, out var target, out var after))
                {
                    Flush();
                    var start = 0;
                    ReadInline(label, ref start, null, style, target, depth + 1, budget, out var linked);
                    result.AddRange(linked);
                    position = after;
                    continue;
                }
                var automatic = c is 'h' or 'H' ? Url.Match(text, position) : Match.Empty;
                if (automatic.Success)
                {
                    var link = automatic.Value.TrimEnd('.', ',', '!', '?', ':', ';');
                    var marker = closing == null ? -1 : link.IndexOf(closing, StringComparison.Ordinal);
                    if (marker >= 0) link = link.Substring(0, marker);
                    while (link.EndsWith(')') && link.Count(x => x == ')') > link.Count(x => x == '(')) link = link.Substring(0, link.Length - 1);
                    if (IsLink(link))
                    {
                        Flush();
                        result.Add(new TextRun(link, style, url ?? link));
                        position += link.Length;
                        continue;
                    }
                }
                var matched = false;
                if (depth < 24 && c is '*' or '_' or '~' or '|')
                {
                    foreach (var delimiter in Delimiters)
                    {
                        if (!text.AsSpan(position).StartsWith(delimiter.Marker, StringComparison.Ordinal)) continue;
                        var start = position + delimiter.Marker.Length;
                        if (start == text.Length || delimiter.Marker.Length == 1 && char.IsWhiteSpace(text[start])) continue;
                        if (delimiter.Marker == "_" && position > 0 && char.IsLetterOrDigit(text[position - 1])) continue;
                        if (text.IndexOf(delimiter.Marker, start, StringComparison.Ordinal) < 0) continue;
                        if (ReadInline(text, ref start, delimiter.Marker, style | delimiter.Style, url, depth + 1, budget, out var nested))
                        {
                            Flush();
                            result.AddRange(nested);
                            position = start;
                            matched = true;
                            break;
                        }
                    }
                }
                if (matched) continue;
                plain.Append(c);
                position++;
            }
            Flush();
            return closing == null;

            void Flush()
            {
                if (plain.Length == 0) return;
                result.Add(new TextRun(plain.ToString(), style, url));
                plain.Clear();
            }
        }

        internal static bool IsLink(string value)
            => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "tg" or "mailto";

        private static bool TryLink(string text, int start, out string label, out string target, out int after)
        {
            label = target = null;
            after = start;
            var close = FindClosing(text, start, '[', ']');
            if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(') return false;
            var end = FindClosing(text, close + 1, '(', ')');
            if (end < 0) return false;
            var destination = text.Substring(close + 2, end - close - 2).Trim();
            if (destination.StartsWith('<'))
            {
                var angle = destination.IndexOf('>');
                if (angle < 0) return false;
                destination = destination.Substring(1, angle - 1);
            }
            else
            {
                var title = destination.IndexOf(" \"", StringComparison.Ordinal);
                if (title >= 0) destination = destination.Substring(0, title);
            }
            destination = Regex.Replace(destination, @"\\([\[\]()\\])", "$1");
            if (!IsLink(destination)) return false;
            label = text.Substring(start + 1, close - start - 1);
            target = destination;
            after = end + 1;
            return true;
        }

        private static int FindClosing(string text, int start, char open, char close)
        {
            var depth = 0;
            for (var i = start; i < text.Length; i++)
            {
                if (text[i] == '\\') { i++; continue; }
                if (text[i] == open) depth++;
                if (text[i] == close && --depth == 0) return i;
            }
            return -1;
        }

        private sealed class ParseBudget(int length)
        {
            internal long Remaining = (long)length * 32 + 1024;
        }
    }
}
