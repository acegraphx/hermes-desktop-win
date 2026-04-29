using System.Windows;
using System.Windows.Media;

namespace HermesDesktop.Helpers;

public static class MonospaceFonts
{
    private static IReadOnlyList<string>? _cached;

    public static IReadOnlyList<string> Installed => _cached ??= Enumerate();

    public const string Default = "Consolas";

    private static IReadOnlyList<string> Enumerate()
    {
        var result = new List<string>();
        foreach (var family in Fonts.SystemFontFamilies)
        {
            try
            {
                if (!IsMonospace(family)) continue;
                var name = family.Source;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!result.Contains(name, StringComparer.OrdinalIgnoreCase))
                    result.Add(name);
            }
            catch
            {
                // Skip fonts that fail to load — Windows occasionally has corrupt entries.
            }
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static bool IsMonospace(FontFamily family)
    {
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        if (!typeface.TryGetGlyphTypeface(out var glyph)) return false;

        // A font is monospace if reference glyphs all share an advance width.
        var i = AdvanceFor(glyph, 'i');
        var m = AdvanceFor(glyph, 'M');
        var w = AdvanceFor(glyph, 'W');
        if (i is null || m is null || w is null) return false;
        return Math.Abs(i.Value - m.Value) < 0.001 && Math.Abs(m.Value - w.Value) < 0.001;
    }

    private static double? AdvanceFor(GlyphTypeface glyph, char c) =>
        glyph.CharacterToGlyphMap.TryGetValue(c, out var idx)
            ? glyph.AdvanceWidths[idx]
            : null;
}
