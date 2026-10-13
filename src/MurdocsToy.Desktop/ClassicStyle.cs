using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MurdocsToy.Desktop;

/// <summary>Colours and fonts of the Classic interface (measured from a reference screenshot of a classic trading UI).</summary>
static class Cl
{
    public static readonly Color BarTop = Color.FromArgb(0x36, 0x5B, 0x63), BarBottom = Color.FromArgb(0x2A, 0x40, 0x4C), Border = Color.FromArgb(0x28, 0x3B, 0x48);
    public static readonly Color ToolBg = Color.FromArgb(0x54, 0x6C, 0x7B), Light = Color.FromArgb(0xEF, 0xEF, 0xEF);
    public static readonly Color PriceGrey = Color.FromArgb(0xD3, 0xD3, 0xD3), LtpCell = Color.FromArgb(0xF0, 0xF0, 0xF0);
    public static readonly Color BlueStrong = Color.FromArgb(0x27, 0x97, 0xE6), BluePale = Color.FromArgb(0xBE, 0xDD, 0xF4);
    public static readonly Color PinkStrong = Color.FromArgb(0xF9, 0x94, 0xA9), PinkPale = Color.FromArgb(0xF3, 0xDC, 0xE2);
    public static readonly Color GreenBar = Color.FromArgb(0x96, 0xE1, 0x96), GreenBarDark = Color.FromArgb(0x64, 0xC8, 0x64);
    public static readonly Color RedBar = Color.FromArgb(0xFF, 0x96, 0x96), RedBarDark = Color.FromArgb(0xFF, 0x64, 0x64);
    public static readonly Color ScaleTop = Color.FromArgb(0x9A, 0x9A, 0xFF), ScaleBottom = Color.FromArgb(0x84, 0x84, 0xFF);
    public static readonly Color StakeBtn = Color.FromArgb(0x64, 0x64, 0xFF), LiabilityBtn = Color.FromArgb(0xFF, 0x96, 0xFF);
    public static readonly Color BackRow = Color.FromArgb(0x72, 0xBB, 0xEF), LayRow = Color.FromArgb(0xFA, 0xA9, 0xBA);
    public static readonly Color GreenRow = Color.FromArgb(0x98, 0xFB, 0x98), CountRow = Color.FromArgb(0x90, 0xEE, 0x90), Orange = Color.FromArgb(0xFF, 0xA0, 0x7A);
    public static readonly Color TreeSelect = Color.FromArgb(0xEF, 0xCE, 0x86), ClockBlue = Color.FromArgb(0, 0, 0xFF);

    /// <summary>Dark mode applies to the navigator window; the ladders keep their light, Geek's Toy colours.</summary>
    public static bool Dark { get; set; }
    public static Color Panel => Dark ? Color.FromArgb(0x1F, 0x27, 0x2C) : Light;
    public static Color Ink => Dark ? Color.FromArgb(0xE8, 0xEB, 0xEC) : Color.Black;
    public static Color Sel => Dark ? Color.FromArgb(0x3A, 0x57, 0x66) : TreeSelect;
    public static Color Edge => Dark ? Color.FromArgb(0x3A, 0x45, 0x4C) : Color.FromArgb(0xC0, 0xC0, 0xC0);

    public static readonly Font Ui = new("Tahoma", 8.25f), UiBold = new("Tahoma", 8.25f, FontStyle.Bold);
    public static readonly Font Name = new("Tahoma", 9f, FontStyle.Bold), Scale = new("Tahoma", 9.75f);
    public static readonly Font SportHead = new("Tahoma", 12f, FontStyle.Bold), Filter = new("Tahoma", 10f, FontStyle.Bold), Clock = new("Tahoma", 20f, FontStyle.Bold);

    /// <summary>Betfair account currency symbol for the status row.</summary>
    public static string Symbol(string currency) => currency switch { "EUR" => "€", "GBP" => "£", "USD" => "$", "AUD" => "A$", "CAD" => "C$", "SEK" => "kr", "NOK" => "kr", "DKK" => "kr", "CHF" => "CHF", _ => currency };

    /// <summary>Flag for the account's currency (the status row shows it left of the balance).</summary>
    public static string? CurrencyFlag(string currency) => currency switch
    {
        "EUR" => "eu", "GBP" => "gb", "USD" => "us", "AUD" => "au", "CAD" => "ca", "SEK" => "se", "NOK" => "no", "DKK" => "dk", "CHF" => "ch", "HKD" => "hk", "SGD" => "sg", _ => null,
    };

    /// <summary>Decimals shown for a price: 1,68 / 5,3 / 21 (as the price steps get coarser).</summary>
    public static string Price(double p) => p < 4 ? p.ToString("0.00") : p < 20 ? p.ToString("0.0") : p.ToString("0");

    /// <summary>Compact traded volume: 211, 1K, 1,8K, 14K.</summary>
    public static string Vol(double v) => v >= 1000 ? (v / 1000).ToString(v >= 10000 ? "0" : "0.#") + "K" : v.ToString("0");

    public static void Gradient(Graphics g, Rectangle r, Color top, Color bottom)
    {
        using var b = new LinearGradientBrush(r, top, bottom, LinearGradientMode.Vertical);
        g.FillRectangle(b, r);
    }
}

/// <summary>Small hand-drawn icons for the navigator tree and headers.</summary>
static class ClIcons
{
    static readonly Dictionary<string, Bitmap> Cache = new();

    static Bitmap Make(string key, Action<Graphics> draw)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var b)) return b;
            b = new Bitmap(16, 16);
            using var g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            draw(g);
            return Cache[key] = b;
        }
    }

    public static Bitmap Ball => Make("ball", g =>
    {
        g.FillEllipse(Brushes.White, 1, 1, 13, 13); g.DrawEllipse(Pens.Black, 1, 1, 13, 13);
        g.FillPolygon(Brushes.Black, new PointF[] { new(7.5f, 4.5f), new(10, 6.5f), new(9, 9.5f), new(6, 9.5f), new(5, 6.5f) });
        foreach (var (x, y) in new[] { (7.5f, 1.5f), (13.5f, 7f), (11.5f, 13f), (3.5f, 13f), (1.5f, 7f) }) g.DrawLine(Pens.Black, 7.5f, 7.5f, x, y);
    });

    public static Bitmap Pitch => Make("pitch", g =>
    {
        using var green = new SolidBrush(Color.FromArgb(0x1E, 0xB4, 0x1E));
        g.FillRectangle(green, 1, 2, 14, 12); g.DrawRectangle(Pens.Black, 1, 2, 13, 11);
        g.DrawRectangle(Pens.White, 3, 4, 9, 7); g.DrawLine(Pens.White, 7, 4, 7, 11); g.DrawEllipse(Pens.White, 5, 6, 4, 3);
    });

    public static Bitmap Market => Make("market", g =>
    {
        using var green = new SolidBrush(Color.FromArgb(0x3C, 0xC8, 0x3C));
        g.FillRectangle(green, 1, 1, 14, 14); g.DrawRectangle(Pens.Black, 1, 1, 13, 13);
        using var dark = new SolidBrush(Color.FromArgb(0x10, 0x60, 0x10));
        g.FillRectangle(dark, 4, 4, 3, 8); g.FillRectangle(dark, 9, 4, 3, 8);
    });

    /// <summary>A small chequered flag for race meetings.</summary>
    public static Bitmap Race => Make("race", g =>
    {
        g.DrawLine(Pens.Black, 3, 2, 3, 14);
        for (int r = 0; r < 4; r++) for (int c = 0; c < 5; c++) g.FillRectangle((r + c) % 2 == 0 ? Brushes.Black : Brushes.White, 4 + c * 2, 2 + r * 2, 2, 2);
        g.DrawRectangle(Pens.Black, 4, 2, 10, 8);
    });

    public static Bitmap Calendar => Make("cal", g =>
    {
        g.FillRectangle(Brushes.White, 2, 1, 12, 14); g.DrawRectangle(Pens.Black, 2, 1, 11, 13);
        g.FillRectangle(Brushes.Red, 3, 2, 10, 3);
        using var f = new Font("Tahoma", 6f, FontStyle.Bold);
        g.DrawString(DateTime.Now.Day.ToString(), f, Brushes.Black, 3, 5);
    });

    /// <summary>Any other sport: a coloured ball with its initial.</summary>
    public static Bitmap Sport(string name) => name == "Soccer" ? Ball : Make("sport:" + name, g =>
    {
        int h = Math.Abs(name.GetHashCode());
        using var fill = new SolidBrush(ColorFromHsv(h % 360, 0.55, 0.8));
        g.FillEllipse(fill, 1, 1, 13, 13); g.DrawEllipse(Pens.Black, 1, 1, 13, 13);
        using var f = new Font("Tahoma", 7f, FontStyle.Bold);
        var s = name.Length > 0 ? name[..1] : "?";
        var sz = g.MeasureString(s, f);
        g.DrawString(s, f, Brushes.White, 7.5f - sz.Width / 2, 7.5f - sz.Height / 2);
    });

    static Color ColorFromHsv(double h, double s, double v)
    {
        int i = (int)(h / 60) % 6; double f = h / 60 - Math.Floor(h / 60), p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        (double r, double g, double b) = i switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
        return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }
}

/// <summary>Dark gradient title bar of a section with the small ? - x buttons.</summary>
sealed class ClassicHeader : Control
{
    public string Caption { get; set; } = "";
    public bool Collapsed { get; set; }
    public bool Collapsible { get; set; } = true;
    public bool Closable { get; set; } = true;
    public event Action? ToggleClicked, HelpClicked, CloseClicked;
    Rectangle _toggle, _help, _close;

    public ClassicHeader()
    {
        Height = 18; Dock = DockStyle.Top; DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Cl.Gradient(g, new Rectangle(0, 0, Width, Height), Cl.BarTop, Cl.BarBottom);
        using var line = new Pen(Cl.Border); g.DrawRectangle(line, 0, 0, Width - 1, Height - 1);
        int x = 3;
        _toggle = _help = _close = Rectangle.Empty;
        if (Collapsible) { _toggle = Box(g, x, Collapsed ? "+" : "−"); x += 14; }
        _help = Box(g, x, "?"); x += 15;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        TextRenderer.DrawText(g, Caption, Cl.UiBold, new Point(x + 1, 2), Color.White, TextFormatFlags.NoPrefix);
        if (Closable) _close = Box(g, Width - 15, "×");
    }

    Rectangle Box(Graphics g, int x, string glyph)
    {
        var r = new Rectangle(x, 3, 11, 11);
        g.FillRectangle(Brushes.White, r); g.DrawRectangle(Pens.Black, r);
        TextRenderer.DrawText(g, glyph, new Font("Tahoma", 7.5f, FontStyle.Bold), r, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        return r;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_toggle.Contains(e.Location)) ToggleClicked?.Invoke();
        else if (_help.Contains(e.Location)) HelpClicked?.Invoke();
        else if (_close.Contains(e.Location)) CloseClicked?.Invoke();
    }
}

/// <summary>A titled, collapsible panel.</summary>
sealed class ClassicSection : Panel
{
    public ClassicHeader Header { get; } = new();
    public Panel Body { get; } = new() { Dock = DockStyle.Fill, BackColor = Cl.Light };
    int _bodyHeight;

    public ClassicSection(string title, int bodyHeight, bool closable = false)
    {
        _bodyHeight = bodyHeight;
        Header.Caption = title; Header.Closable = closable;
        Dock = DockStyle.Top; Height = bodyHeight + Header.Height;
        Controls.Add(Body); Controls.Add(Header);
        Header.ToggleClicked += () => Collapsed = !Collapsed;
    }

    public bool Collapsed
    {
        get => Header.Collapsed;
        set { Header.Collapsed = value; Body.Visible = !value; Height = value ? Header.Height : _bodyHeight + Header.Height; Header.Invalidate(); }
    }

    public int BodyHeight { get => _bodyHeight; set { _bodyHeight = value; if (!Collapsed) Height = value + Header.Height; } }
}


/// <summary>The status-panel clock: a small screen icon, big blue digits, and a status light.</summary>
sealed class ClassicClock : Control
{
    public bool Online { get; set; }
    public event Action? IconClicked;
    readonly Rectangle _icon = new(2, 6, 26, 30);
    public Color Digits { get; set; } = Cl.ClockBlue;

    public ClassicClock()
    {
        DoubleBuffered = true; Dock = DockStyle.Top; Height = 42; BackColor = Cl.Light;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    bool _hover;
    protected override void OnMouseMove(MouseEventArgs e) { bool h = _icon.Contains(e.Location); if (h != _hover) { _hover = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { if (_hover) { _hover = false; Cursor = Cursors.Default; Invalidate(); } }
    protected override void OnMouseDown(MouseEventArgs e) { if (_icon.Contains(e.Location)) IconClicked?.Invoke(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Cl.Light); g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_hover) { using var h = new SolidBrush(Color.FromArgb(215, 226, 232)); g.FillRectangle(h, _icon); g.DrawRectangle(Pens.SteelBlue, _icon); }
        g.FillRectangle(Brushes.Turquoise, 8, 10, 16, 12); g.DrawRectangle(Pens.Black, 8, 10, 15, 11); g.FillRectangle(Brushes.Black, 13, 23, 6, 2);
        using (var tri = new SolidBrush(Color.FromArgb(0xD0, 0x20, 0x20))) g.FillPolygon(tri, new Point[] { new(13, 12), new(13, 20), new(19, 16) });   // play
        using var light = new SolidBrush(Online ? Color.LimeGreen : Color.Gray);
        g.FillEllipse(light, Width - 24, 12, 16, 16); g.DrawEllipse(Pens.Black, Width - 24, 12, 15, 15);
        TextRenderer.DrawText(g, Text, Cl.Clock, new Rectangle(28, 0, Width - 56, Height), Digits,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}
