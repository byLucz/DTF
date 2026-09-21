using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace DiscordTelegramFrontier
{
    internal sealed class RenderFontCache : IDisposable
    {
        private readonly Dictionary<(bool Code, bool Bold, bool Italic), SKTypeface> _typefaces = new();
        private readonly Dictionary<int, SKTypeface> _fallbacks = new();
        private readonly Dictionary<(SKTypeface Typeface, float Size), RenderFont> _fonts = new();

        public RenderFont GetFont(bool code, bool bold, bool italic, float size)
        {
            var key = (code, bold, italic);
            if (!_typefaces.TryGetValue(key, out var typeface))
            {
                var style = bold ? italic ? SKFontStyle.BoldItalic : SKFontStyle.Bold
                    : italic ? SKFontStyle.Italic : SKFontStyle.Normal;
                typeface = SKTypeface.FromFamilyName(code ? "Courier New" : "Arial", style);
                _typefaces.Add(key, typeface);
            }
            return GetFont(typeface, size);
        }

        public RenderGlyph GetGlyph(RenderFont source, string text)
        {
            if (source.Glyphs.TryGetValue(text, out var glyph)) return glyph;
            var font = source;
            var codePoint = char.ConvertToUtf32(text, 0);
            if (!source.Font.ContainsGlyphs(text))
            {
                if (!_fallbacks.TryGetValue(codePoint, out var fallback))
                {
                    fallback = SKFontManager.Default.MatchCharacter(codePoint);
                    _fallbacks.Add(codePoint, fallback);
                }
                if (fallback != null) font = GetFont(fallback, source.Font.Size);
            }
            var shaped = text.Length > (codePoint > 0xFFFF ? 2 : 1);
            var width = shaped ? font.Shaper.Shape(text, font.Font).Width : font.Font.MeasureText(text);
            glyph = new RenderGlyph(font, width, shaped);
            source.Glyphs.Add(text, glyph);
            return glyph;
        }

        private RenderFont GetFont(SKTypeface typeface, float size)
        {
            var key = (typeface, size);
            if (_fonts.TryGetValue(key, out var font)) return font;
            font = new RenderFont(typeface, size);
            _fonts.Add(key, font);
            return font;
        }

        public void Dispose()
        {
            foreach (var font in _fonts.Values) font.Dispose();
            foreach (var typeface in _typefaces.Values.Concat(_fallbacks.Values).Distinct()) typeface?.Dispose();
        }
    }

    internal sealed class RenderFont : IDisposable
    {
        private SKShaper _shaper;
        public SKFont Font { get; }
        public SKFontMetrics Metrics { get; }
        public SKShaper Shaper => _shaper ??= new SKShaper(Font.Typeface);
        public Dictionary<string, RenderGlyph> Glyphs { get; } = new(StringComparer.Ordinal);

        public RenderFont(SKTypeface typeface, float size)
        {
            Font = new SKFont(typeface, size);
            Metrics = Font.Metrics;
        }

        public void Dispose()
        {
            _shaper?.Dispose();
            Font.Dispose();
        }
    }

    internal readonly record struct RenderGlyph(RenderFont Font, float Width, bool Shaped);
}
