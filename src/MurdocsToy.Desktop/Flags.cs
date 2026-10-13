using System.Drawing.Drawing2D;
using System.Reflection;
using System.Text.Json;

namespace MurdocsToy.Desktop;

/// <summary>
/// Country flags for competitions and matches. Betfair's API gives country codes only (competition region as ISO alpha-3,
/// event country as alpha-2); the flag pictures are the MIT-licensed flag-icons set, embedded in the program.
/// </summary>
static class Flags
{
    static readonly Assembly Asm = typeof(Flags).Assembly;
    static Dictionary<string, string>? _iso3;
    static readonly Dictionary<string, Image?> Cache = new();

    static Dictionary<string, string> Iso3
    {
        get
        {
            if (_iso3 != null) return _iso3;
            try
            {
                using var s = Asm.GetManifestResourceStream("web/flags/iso3.json");
                _iso3 = s == null ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new();
            }
            catch { _iso3 = new(); }
            return _iso3;
        }
    }

    /// <summary>Flag file code for a competition or event, or null when there is no sensible flag.</summary>
    public static string? CodeFor(string? region, string? countryCode, string? name)
    {
        string? code = null;
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            code = countryCode.Trim().ToLowerInvariant();
            if (code == "uk") code = "gb";
        }
        else if (!string.IsNullOrWhiteSpace(region))
        {
            var r = region.Trim();
            if (r.Equals("International", StringComparison.OrdinalIgnoreCase))
                return name != null && (name.Contains("UEFA", StringComparison.OrdinalIgnoreCase) || name.Contains("European", StringComparison.OrdinalIgnoreCase)) ? "eu" : "un";
            if (Iso3.TryGetValue(r.ToUpperInvariant(), out var c)) code = c;
        }
        if (code == "gb" && name != null)    // the UK plays as four nations
        {
            if (name.StartsWith("English", StringComparison.OrdinalIgnoreCase)) code = "gb-eng";
            else if (name.StartsWith("Scottish", StringComparison.OrdinalIgnoreCase)) code = "gb-sct";
            else if (name.StartsWith("Welsh", StringComparison.OrdinalIgnoreCase)) code = "gb-wls";
            else if (name.StartsWith("Northern Irish", StringComparison.OrdinalIgnoreCase)) code = "gb-nir";
        }
        return code != null && Exists(code) ? code : null;
    }

    static bool Exists(string code) => Asm.GetManifestResourceInfo($"web/flags/{code}.png") != null;

    public static Image? Get(string? code)
    {
        if (code == null) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(code, out var img)) return img;
            using var s = Asm.GetManifestResourceStream($"web/flags/{code}.png");
            return Cache[code] = s == null ? null : new Bitmap(s);
        }
    }

    /// <summary>A flag scaled to a small icon, with a thin outline so white flags stay visible on light backgrounds.</summary>
    public static Bitmap Icon(string code, Size size)
    {
        var src = Get(code);
        var bmp = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        if (src != null) g.DrawImage(src, 0, 0, size.Width - 1, size.Height - 1);
        using var pen = new Pen(Color.FromArgb(110, 110, 110));
        g.DrawRectangle(pen, 0, 0, size.Width - 2, size.Height - 2);
        return bmp;
    }
}

/// <summary>Image list for tree views: add flags on demand, get back the key to put on a node.</summary>
sealed class FlagList
{
    public const string None = "none";
    public ImageList Images { get; }
    readonly Size _size;

    public FlagList(Size size)
    {
        _size = size;
        Images = new ImageList { ImageSize = size, ColorDepth = ColorDepth.Depth32Bit };
        Images.Images.Add(None, new Bitmap(size.Width, size.Height));     // blank, keeps text aligned
    }

    public string Key(string? code)
    {
        if (code == null) return None;
        if (!Images.Images.ContainsKey(code)) Images.Images.Add(code, Flags.Icon(code, _size));
        return code;
    }
}
