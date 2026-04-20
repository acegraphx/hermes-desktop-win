using System.Text.Json.Serialization;

namespace HermesDesktop.Models;

public enum TerminalThemeStyle
{
    System,
    Graphite,
    Evergreen,
    Dusk,
    Paper,
    Custom
}

public struct TerminalThemeColor
{
    public double Red { get; set; }
    public double Green { get; set; }
    public double Blue { get; set; }

    public TerminalThemeColor(double red, double green, double blue)
    {
        Red = Clamp(red);
        Green = Clamp(green);
        Blue = Clamp(blue);
    }

    public static TerminalThemeColor FromHex(int hex) =>
        new(((hex >> 16) & 0xFF) / 255.0, ((hex >> 8) & 0xFF) / 255.0, (hex & 0xFF) / 255.0);

    public string ToHex()
    {
        var r = (int)Math.Round(Red * 255);
        var g = (int)Math.Round(Green * 255);
        var b = (int)Math.Round(Blue * 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static double Clamp(double v) => Math.Clamp(v, 0, 1);
}

public class TerminalThemePreset
{
    public TerminalThemeStyle Style { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public TerminalThemeColor Background { get; set; }
    public TerminalThemeColor Foreground { get; set; }
    public TerminalThemeColor[] AnsiPalette { get; set; } = Array.Empty<TerminalThemeColor>();
}

public class TerminalThemeAppearance
{
    public TerminalThemeStyle Style { get; set; }
    public string Name { get; set; } = string.Empty;
    public TerminalThemeColor Background { get; set; }
    public TerminalThemeColor Foreground { get; set; }
    public TerminalThemeColor[] AnsiPalette { get; set; } = Array.Empty<TerminalThemeColor>();
    public TerminalThemeStyle PaletteStyle { get; set; }
    public bool IsCustom { get; set; }
}

public class TerminalThemePreference
{
    [JsonPropertyName("style")]
    public TerminalThemeStyle Style { get; set; } = TerminalThemeStyle.System;

    [JsonPropertyName("customBackgroundHex")]
    public string? CustomBackgroundHex { get; set; }

    [JsonPropertyName("customForegroundHex")]
    public string? CustomForegroundHex { get; set; }

    [JsonPropertyName("paletteStyle")]
    public TerminalThemeStyle? PaletteStyle { get; set; }

    public static TerminalThemePreference Default => new();

    public static IReadOnlyList<TerminalThemePreset> QuickPresets =>
        new[] { Graphite, Evergreen, Dusk, Paper };

    public TerminalThemeAppearance ResolvedAppearance(bool isDarkMode)
    {
        switch (Style)
        {
            case TerminalThemeStyle.System:
                if (isDarkMode)
                {
                    return new TerminalThemeAppearance
                    {
                        Style = TerminalThemeStyle.System,
                        Name = "System",
                        Background = TerminalThemeColor.FromHex(0x0B0B0B),
                        Foreground = TerminalThemeColor.FromHex(0xEDEDED),
                        AnsiPalette = SystemPalette,
                        PaletteStyle = TerminalThemeStyle.System,
                        IsCustom = false
                    };
                }
                return new TerminalThemeAppearance
                {
                    Style = TerminalThemeStyle.System,
                    Name = "System",
                    Background = TerminalThemeColor.FromHex(0xFFFFFF),
                    Foreground = TerminalThemeColor.FromHex(0x1C1C1C),
                    AnsiPalette = SystemPalette,
                    PaletteStyle = TerminalThemeStyle.System,
                    IsCustom = false
                };

            case TerminalThemeStyle.Custom:
                var basePreset = PresetFor(PaletteStyle ?? TerminalThemeStyle.Graphite) ?? Graphite;
                return new TerminalThemeAppearance
                {
                    Style = TerminalThemeStyle.Custom,
                    Name = "Custom",
                    Background = ParseHex(CustomBackgroundHex) ?? basePreset.Background,
                    Foreground = ParseHex(CustomForegroundHex) ?? basePreset.Foreground,
                    AnsiPalette = basePreset.AnsiPalette,
                    PaletteStyle = basePreset.Style,
                    IsCustom = true
                };

            default:
                var preset = PresetFor(Style) ?? Graphite;
                return new TerminalThemeAppearance
                {
                    Style = preset.Style,
                    Name = preset.Name,
                    Background = preset.Background,
                    Foreground = preset.Foreground,
                    AnsiPalette = preset.AnsiPalette,
                    PaletteStyle = preset.Style,
                    IsCustom = false
                };
        }
    }

    private static TerminalThemeColor? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6) return null;
        if (!int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v)) return null;
        return TerminalThemeColor.FromHex(v);
    }

    private static TerminalThemePreset? PresetFor(TerminalThemeStyle s) =>
        QuickPresets.FirstOrDefault(p => p.Style == s);

    public static TerminalThemePreset Graphite { get; } = new()
    {
        Style = TerminalThemeStyle.Graphite,
        Name = "Graphite",
        Summary = "Neutral dark theme with high contrast and quiet ANSI accents.",
        Background = TerminalThemeColor.FromHex(0x12161D),
        Foreground = TerminalThemeColor.FromHex(0xE7ECF3),
        AnsiPalette = Palette(new[]
        {
            0x1F2430, 0xC7746E, 0x88B976, 0xD6B97A,
            0x78A6D8, 0xB18AD0, 0x6EC5C8, 0xCFD6E3,
            0x596273, 0xE08D86, 0x9FD58A, 0xE4CA91,
            0x93B8E4, 0xC7A3E1, 0x8BD9DA, 0xF4F7FB
        })
    };

    public static TerminalThemePreset Evergreen { get; } = new()
    {
        Style = TerminalThemeStyle.Evergreen,
        Name = "Evergreen",
        Summary = "Deep forest backdrop with calm greens and warm highlights.",
        Background = TerminalThemeColor.FromHex(0x0F1714),
        Foreground = TerminalThemeColor.FromHex(0xDBE8E1),
        AnsiPalette = Palette(new[]
        {
            0x16211D, 0xC97973, 0x73B181, 0xD5B66A,
            0x6D98C4, 0xAA86BF, 0x63BEB0, 0xC6D5CE,
            0x4F635B, 0xE49790, 0x8ED09D, 0xE9CB88,
            0x8CB4D6, 0xC39BD3, 0x7FD6C8, 0xEFF7F3
        })
    };

    public static TerminalThemePreset Dusk { get; } = new()
    {
        Style = TerminalThemeStyle.Dusk,
        Name = "Dusk",
        Summary = "Cool navy tones that stay readable for long SSH sessions.",
        Background = TerminalThemeColor.FromHex(0x101726),
        Foreground = TerminalThemeColor.FromHex(0xDDE7F7),
        AnsiPalette = Palette(new[]
        {
            0x1A2235, 0xD06E79, 0x86B97B, 0xD5BA79,
            0x7AA2D8, 0xB390D2, 0x70C0D0, 0xCCD7EA,
            0x55627E, 0xE48A95, 0xA1D191, 0xE6CD90,
            0x97B9E8, 0xC9A5E3, 0x89D9E4, 0xF4F8FD
        })
    };

    public static TerminalThemePreset Paper { get; } = new()
    {
        Style = TerminalThemeStyle.Paper,
        Name = "Paper",
        Summary = "Light, editorial theme for daytime work and quiet rooms.",
        Background = TerminalThemeColor.FromHex(0xF5F1E8),
        Foreground = TerminalThemeColor.FromHex(0x2F3743),
        AnsiPalette = Palette(new[]
        {
            0x3C4657, 0xB44A56, 0x4E8B67, 0xA77720,
            0x416EA9, 0x8758A6, 0x2E8B92, 0xD9D2C4,
            0x6D7482, 0xCD6571, 0x66A07C, 0xBF9147,
            0x5C86BE, 0xA072BD, 0x53A6AD, 0xFFFDF8
        })
    };

    private static readonly TerminalThemeColor[] SystemPalette = Palette(new[]
    {
        0x000000, 0xC23621, 0x25BC24, 0xADAD27,
        0x492EE1, 0xD338D3, 0x33BBC8, 0xCBCCCD,
        0x818383, 0xFC391F, 0x31E722, 0xEAEC23,
        0x5833FF, 0xF935F8, 0x14F0F0, 0xE9EBEB
    });

    private static TerminalThemeColor[] Palette(int[] hex) =>
        hex.Select(TerminalThemeColor.FromHex).ToArray();
}
