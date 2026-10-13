using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>Liquidity tracker for one market: matched volume and rate, money available to back/lay, book percentages and history.</summary>
sealed class LiquidityPanel : Control
{
    LiquidityTracker _t = new();

    public LiquidityPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = 96;
    }

    public void Reset() { _t = new LiquidityTracker(); Invalidate(); }
    public void Update(MarketBook book) { _t.Add(book, DateTime.UtcNow); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.FromArgb(27, 32, 40));
        using var title = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var big = new Font("Segoe UI", 12f, FontStyle.Bold);
        using var small = new Font("Segoe UI", 8.5f);
        var s = _t.Last;
        g.DrawString("LIQUIDITY", title, Brushes.Gray, 8, 4);
        if (s == null) { g.DrawString("waiting for data...", small, Brushes.Gray, 8, 24); return; }

        g.DrawString($"Matched {Liquidity.Short(s.Matched)}", big, Brushes.White, 8, 18);
        using (var rate = new SolidBrush(s.MatchedPerMin > 0 ? Color.LightGreen : Color.Gray))
            g.DrawString($"{(s.MatchedPerMin > 0 ? "+" : "")}{Liquidity.Short(s.MatchedPerMin)}/min", small, rate, 10, 42);
        g.DrawString($"Available   Back {Liquidity.Short(s.AvailBack)}   Lay {Liquidity.Short(s.AvailLay)}", small, Brushes.Gainsboro, 8, 60);
        g.DrawString($"Book   Back {(s.BackBookPct > 0 ? s.BackBookPct.ToString("0.0") + "%" : "-")}   Lay {(s.LayBookPct > 0 ? s.LayBookPct.ToString("0.0") + "%" : "-")}", small, Brushes.Gainsboro, 8, 76);

        int x0 = 230, w = Width - x0 - 10;
        if (w < 60) return;
        Spark(g, new Rectangle(x0, 20, w, 30), _t.Samples.Select(x => x.Matched).ToList(), Color.FromArgb(90, 169, 230), "matched");
        Spark(g, new Rectangle(x0, 60, w, 30), _t.Samples.Select(x => x.AvailBack + x.AvailLay).ToList(), Color.FromArgb(76, 195, 138), "available");
    }

    static void Spark(Graphics g, Rectangle r, List<double> v, Color c, string label)
    {
        using var font = new Font("Segoe UI", 7.5f);
        g.DrawString(label, font, Brushes.Gray, r.X, r.Y - 14);
        using var frame = new Pen(Color.FromArgb(55, 62, 74));
        g.DrawRectangle(frame, r);
        if (v.Count < 2) return;
        double min = v.Min(), max = v.Max(), span = Math.Max(max - min, 1e-9);
        var pts = v.Select((y, i) => new PointF(r.X + (float)i / (v.Count - 1) * r.Width,
            max - min < 1e-9 ? r.Y + r.Height / 2f : r.Bottom - (float)((y - min) / span) * (r.Height - 2) - 1)).ToArray();
        using var pen = new Pen(c, 1.6f);
        g.DrawLines(pen, pts);
    }
}
