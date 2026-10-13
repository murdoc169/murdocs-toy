using System.Drawing.Drawing2D;
using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>
/// One runner's Classic ladder: name and stake header, position panel, bar-scale strip, summary row, then the price rows
/// (price | my lay orders | pink lay column | blue back column | my back orders | traded volume | depth bar) and two order boxes.
/// Clicking the blue column backs at that price, the pink column lays at that price.
/// </summary>
sealed class ClassicLadderControl : Control
{
    const int HeaderH = 46, InfoH = 94, ScaleH = 22, SummaryH = 19, RowH = 19, BottomH = 78;
    // fixed pixel widths; the first (blank) column takes whatever is left
    static readonly int[] ColW = { 0, 35, 45, 40, 42, 44, 42, 14 };
    public static readonly int[] Scales = { 10, 20, 25, 50, 75, 100, 125, 150, 175, 200, 250 };

    public event Action<double>? BackAt, LayAt;
    public event Action<CurrentOrder>? CancelOrder;
    public event Action? GreenUp, StakeClicked, Recentre;
    public event Action<int>? ScaleChosen;

    public string RunnerName { get; set; } = "";
    public string StakeText { get; set; } = "";
    public int BarScale { get; set; } = 150;
    public string CurrencySymbol { get; set; } = "€";

    readonly ToolTip _tip = new() { AutoPopDelay = 30000, InitialDelay = 400, ReshowDelay = 100, ShowAlways = true };
    string _lastTip = "";
    public string BackStakeText { get; set; } = "";
    static readonly string NL = Environment.NewLine;

    Runner? _runner;
    List<CurrentOrder> _orders = new();
    Position _pos = new(0, 0);
    double _centre = 2;
    bool _manual;
    Rectangle _stakeBtn, _greenBtn;
    readonly List<(Rectangle Rect, CurrentOrder Order)> _orderLines = new();

    public ClassicLadderControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = Cl.Ui; Cursor = Cursors.Default;
    }

    public void SetData(Runner? runner, List<CurrentOrder> myOrders, bool autoCentre)
    {
        _runner = runner; _orders = myOrders;
        _pos = runner == null ? new Position(0, 0) : Position.Of(myOrders, runner.SelectionId);
        if (autoCentre && !_manual) _centre = runner?.LastPriceTraded ?? runner?.Ex?.AvailableToBack?.FirstOrDefault()?.Price ?? _centre;
        Invalidate();
    }

    int[] Cols()
    {
        var w = (int[])ColW.Clone();
        w[0] = Math.Max(20, Width - ColW.Skip(1).Sum() - 4);
        return w;
    }

    int RowsTop => HeaderH + InfoH + ScaleH + SummaryH;
    int Rows => Math.Max(5, (Height - RowsTop - BottomH) / RowH);
    IEnumerable<double> Prices() => PriceLadder.Window(_centre, Rows / 2, Rows - Rows / 2 - 1);

    // ---- input -----------------------------------------------------------------------------------------------
    /// <summary>What the pointer is over, in plain words.</summary>
    string HoverText(Point p)
    {
        if (_stakeBtn.Contains(p)) return "Pick the back stake from your preset stakes." + NL + "Change the presets in Options.";
        if (p.Y < HeaderH)
            return p.X > Width - 70 ? "Your back stake: the amount a click in the blue column bets." + NL + "Change it in Staking & Tools on the right, or press S for presets."
                                    : "The selection (runner) this ladder shows.";
        if (_greenBtn.Contains(p)) return "Green up: places the one bet that locks in the same profit whether this selection wins or loses." + NL + "Needs matched bets on it first; asks you to confirm.";
        if (p.Y < HeaderH + InfoH) return "Your result on this selection from your matched bets:" + NL + "profit if it wins, profit or loss if it loses. Back and Lay are the best prices right now.";
        int stripY = HeaderH + InfoH;
        if (p.Y < stripY + ScaleH) return "Bar scale. The coloured bars in the pink and blue columns grow with the money waiting at each price;" + NL + "this number sets how much money a full-length bar stands for. Click a number to change it.";
        if (p.Y < RowsTop)
        {
            var w0 = Cols(); int x1 = w0[0], x2 = x1 + w0[1], x4 = w0[0] + w0[1] + w0[2] + w0[3], x5 = x4 + w0[4], x6 = x5 + w0[5];
            if (p.X >= x1 && p.X < x2) return "Last traded price: the price of the most recent matched bet on this selection.";
            if (p.X >= x4 && p.X < x5) return "Weight of money: money waiting in the blue column divided by money waiting in the pink column." + NL + "Above 1 means more people are waiting to back than to lay.";
            if (p.X >= x6) return "Total matched on this selection.";
            return "Click here to re-centre the ladder on the last traded price.";
        }
        var prices = Prices().ToList(); int row = (p.Y - RowsTop) / RowH;
        if (row < 0 || row >= prices.Count) return p.Y > RowsTop + prices.Count * RowH ? "Your open bets on this selection. Click one to cancel it." : "";
        var w = Cols(); int acc = 0, col = 0;
        for (; col < w.Length; col++) { acc += w[col]; if (p.X < acc) break; }
        string px = Cl.Price(prices[row]);
        return col switch
        {
            1 => $"Price {px}. Prices move one tick at a time along the Betfair ladder.",
            2 => $"Your open LAY bets at {px} (stake shown). Click to cancel them.",
            3 => $"Pink: money waiting to be laid at {px}. Click to LAY here at your lay stake.",
            4 => $"Blue: money waiting to be backed at {px}. Click to BACK here at your back stake" + (BackStakeText != "" ? $" ({BackStakeText})." : "."),
            5 => $"Your open BACK bets at {px} (stake shown). Click to cancel them.",
            6 => $"Money matched at {px}.",
            7 => "Depth: green = money waiting to back (blue side), red = money waiting to lay (pink side), split by amount.",
            _ => "",
        };
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var text = HoverText(e.Location);
        if (text != _lastTip) { _lastTip = text; _tip.SetToolTip(this, text == "" ? null : text); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _manual = true;
        for (int i = 0; i < 3; i++) _centre = e.Delta > 0 ? PriceLadder.Up(_centre) : PriceLadder.Down(_centre);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_stakeBtn.Contains(e.Location)) { StakeClicked?.Invoke(); return; }
        if (_greenBtn.Contains(e.Location)) { GreenUp?.Invoke(); return; }
        foreach (var (rect, order) in _orderLines) if (rect.Contains(e.Location)) { CancelOrder?.Invoke(order); return; }

        int stripY = HeaderH + InfoH;
        if (e.Y >= stripY && e.Y < stripY + ScaleH)
        {
            int idx = Math.Clamp((e.X - 3) * Scales.Length / Math.Max(1, Width - 20), 0, Scales.Length - 1);
            BarScale = Scales[idx]; ScaleChosen?.Invoke(BarScale); Invalidate(); return;
        }
        if (e.Y >= stripY + ScaleH && e.Y < RowsTop) { _manual = false; Recentre?.Invoke(); return; }   // summary row: re-centre

        int row = (e.Y - RowsTop) / RowH;
        var prices = Prices().ToList();
        if (e.Y < RowsTop || row < 0 || row >= prices.Count) return;
        double price = prices[row];
        var w = Cols(); int x = e.X, acc = 0, col = 0;
        for (; col < w.Length; col++) { acc += w[col]; if (x < acc) break; }
        if (col == 2 || col == 5)                                              // my orders: cancel those at this price
        {
            foreach (var o in _orders.Where(o => o.Status == "EXECUTABLE" && o.SelectionId == _runner?.SelectionId && Math.Abs(o.Price - price) < 1e-9 && (col == 5) == (o.Side == "BACK")))
                CancelOrder?.Invoke(o);
        }
        else if (col == 4) BackAt?.Invoke(price);
        else if (col == 3) LayAt?.Invoke(price);
    }

    // ---- painting --------------------------------------------------------------------------------------------
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Cl.LBg);
        g.SmoothingMode = SmoothingMode.None;
        int W = Width;
        var w = Cols();
        var x = new int[w.Length + 1]; for (int i = 0; i < w.Length; i++) x[i + 1] = x[i] + w[i];
        using var black = new Pen(Cl.Grid);

        PaintHeader(g, W);
        PaintInfo(g, W);
        PaintScale(g, W);
        var r = _runner;

        // summary row
        int sy = HeaderH + InfoH + ScaleH;
        using (var b = new SolidBrush(Cl.Panel)) g.FillRectangle(b, x[0], sy, w[0], SummaryH);
        double? ltp = r?.LastPriceTraded;
        using (var b = new SolidBrush(Cl.LtpCell)) g.FillRectangle(b, x[1], sy, w[1], SummaryH);
        using (var b = new SolidBrush(Cl.PinkPale)) g.FillRectangle(b, x[3], sy, w[3], SummaryH);
        using (var b = new SolidBrush(Cl.BlueStrong)) g.FillRectangle(b, x[4], sy, w[4], SummaryH);
        using (var b = new SolidBrush(Cl.PriceGrey)) g.FillRectangle(b, x[6], sy, w[6], SummaryH);
        double totBack = (r?.Ex?.AvailableToBack ?? new()).Sum(p => p.Size), totLay = (r?.Ex?.AvailableToLay ?? new()).Sum(p => p.Size);
        Cell(g, ltp?.ToString("0.00") ?? "", Cl.UiBold, new Rectangle(x[1], sy, w[1], SummaryH), Color.Black, centre: true);
        Cell(g, totBack > 0 && totLay > 0 ? (totLay / totBack).ToString("0.0") : "", Cl.Ui, new Rectangle(x[4], sy, w[4], SummaryH), Color.FromArgb(0x10, 0x30, 0x60), centre: true);
        Cell(g, (r?.TotalMatched is double tm && tm > 0 ? tm : (r?.Ex?.TradedVolume ?? new()).Sum(p => p.Size)) is double vol && vol > 0 ? Cl.Vol(vol) : "", Cl.Ui, new Rectangle(x[6], sy, w[6], SummaryH), Color.Black, centre: true);
        GridRow(g, black, x, sy, SummaryH, W);
        g.DrawLine(black, 0, sy + SummaryH, W, sy + SummaryH); g.DrawLine(black, 0, sy + SummaryH - 1, W, sy + SummaryH - 1);

        // price rows
        int ry0 = RowsTop;
        var lay = (r?.Ex?.AvailableToBack ?? new()).ToDictionary(p => p.Price, p => p.Size);    // pink: waiting lay offers
        var back = (r?.Ex?.AvailableToLay ?? new()).ToDictionary(p => p.Price, p => p.Size);    // blue: waiting back offers
        var traded = (r?.Ex?.TradedVolume ?? new()).ToDictionary(p => p.Price, p => p.Size);
        double bestBack = r?.Ex?.AvailableToBack?.FirstOrDefault()?.Price ?? ltp ?? 0;           // pink zone: at or below
        double bestLay = r?.Ex?.AvailableToLay?.FirstOrDefault()?.Price ?? (ltp.HasValue ? PriceLadder.Up(ltp.Value) : 1000);   // blue zone: at or above
        using var greyBr = new SolidBrush(Cl.PriceGrey); using var ltpBr = new SolidBrush(Cl.LtpCell);
        using var bluePale = new SolidBrush(Cl.BluePale); using var blueStrong = new SolidBrush(Cl.BlueStrong);
        using var pinkPale = new SolidBrush(Cl.PinkPale); using var pinkStrong = new SolidBrush(Cl.PinkStrong);
        _orderLines.Clear();
        var mine = _orders.Where(o => o.Status == "EXECUTABLE" && o.SizeRemaining > 0 && o.SelectionId == r?.SelectionId).ToList();
        var prices = Prices().ToList();
        double fullAmount = BarScale * 20.0;
        for (int i = 0; i < prices.Count; i++)
        {
            double p = prices[i]; int y = ry0 + i * RowH;
            bool isLtp = ltp.HasValue && Math.Abs(ltp.Value - p) < 1e-9;
            g.FillRectangle(isLtp ? ltpBr : greyBr, x[1], y, w[1], RowH);
            if (p <= bestBack + 1e-9 && bestBack > 0) g.FillRectangle(pinkPale, x[3], y, w[3], RowH);
            if (p >= bestLay - 1e-9) g.FillRectangle(bluePale, x[4], y, w[4], RowH);
            if (lay.TryGetValue(p, out var ls))
            {
                int bw = Bar(ls, w[3], fullAmount);
                g.FillRectangle(pinkStrong, x[3] + w[3] - bw, y, bw, RowH);
                Cell(g, ls.ToString("0"), Cl.Ui, new Rectangle(x[3], y, w[3] - 2, RowH), Color.FromArgb(0x40, 0x30, 0x50), right: true);
            }
            if (back.TryGetValue(p, out var bs))
            {
                int bw = Bar(bs, w[4], fullAmount);
                g.FillRectangle(blueStrong, x[4], y, bw, RowH);
                Cell(g, bs.ToString("0"), Cl.Ui, new Rectangle(x[4] + 2, y, w[4] - 2, RowH), Color.FromArgb(0x20, 0x30, 0x58));
            }
            // my open orders at this price
            foreach (var o in mine.Where(o => Math.Abs(o.Price - p) < 1e-9))
            {
                bool isBack = o.Side == "BACK"; int col = isBack ? 5 : 2;
                using var ob = new SolidBrush(isBack ? Cl.BlueStrong : Cl.PinkStrong);
                g.FillRectangle(ob, x[col], y, w[col], RowH);
                Cell(g, o.SizeRemaining.ToString("0.00"), Cl.UiBold, new Rectangle(x[col], y, w[col], RowH), isBack ? Color.White : Color.Black, centre: true);
            }
            if (traded.TryGetValue(p, out var tv) && tv > 0) Cell(g, Cl.Vol(tv), Cl.Ui, new Rectangle(x[6], y, w[6] - 3, RowH), Color.Black, right: true);
            Cell(g, Cl.Price(p), Cl.UiBold, new Rectangle(x[1], y, w[1], RowH), Color.Black, centre: true);
            GridRow(g, black, x, y, RowH, W);
            if (isLtp)   // last-traded marker: blue over red on the right edge of the blank column
            {
                g.FillRectangle(Brushes.Blue, x[1] - 3, y + 1, 3, RowH / 2); g.FillRectangle(Brushes.Red, x[1] - 3, y + 1 + RowH / 2, 3, RowH / 2 - 1);
            }
        }
        g.DrawLine(black, 0, ry0 + prices.Count * RowH, W, ry0 + prices.Count * RowH);
        PaintDepthBar(g, x[7], w[7], ry0, prices.Count * RowH, totBack, totLay);
        PaintBottom(g, W, ry0 + prices.Count * RowH, mine);
    }

    static int Bar(double amount, int cellW, double full)
    {
        double f = Math.Min(1, Math.Log10(1 + amount) / Math.Log10(1 + full));
        return (int)Math.Round(cellW * (0.25 + 0.55 * f));
    }

    static void Cell(Graphics g, string text, Font font, Rectangle r, Color colour, bool centre = false, bool right = false)
    {
        if (text == "") return;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | (centre ? TextFormatFlags.HorizontalCenter : right ? TextFormatFlags.Right : TextFormatFlags.Left);
        TextRenderer.DrawText(g, text, font, r, Cl.Adapt(colour), flags);
    }

    /// <summary>Black grid lines under a row and between its columns.</summary>
    void GridRow(Graphics g, Pen black, int[] x, int y, int h, int width)
    {
        g.DrawLine(black, 0, y + h - 1, width, y + h - 1);
        for (int i = 1; i < x.Length - 1; i++) g.DrawLine(black, x[i], y, x[i], y + h);
    }

    void PaintHeader(Graphics g, int W)
    {
        TextRenderer.DrawText(g, RunnerName, Cl.Name, new Rectangle(6, 5, W - 90, 20), Cl.Ink, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        var box = new Rectangle(W - 66, 3, 43, 21);
        using (var b = new SolidBrush(Cl.LtpCell)) g.FillRectangle(b, box);
        ControlPaint.DrawBorder3D(g, box, Border3DStyle.SunkenInner);
        Cell(g, StakeText, Cl.Name, new Rectangle(box.X, box.Y, box.Width - 4, box.Height), Color.Black, right: true);
        _stakeBtn = new Rectangle(W - 22, 3, 19, 21);
        using (var b = new SolidBrush(Cl.StakeBtn)) g.FillRectangle(b, _stakeBtn);
        Cell(g, "S", new Font("Tahoma", 10f, FontStyle.Bold), _stakeBtn, Color.White, centre: true);
    }

    void PaintInfo(Graphics g, int W)
    {
        var rect = new Rectangle(0, HeaderH - 2, W, InfoH);
        using (var b = new LinearGradientBrush(rect, Cl.Dark ? Color.FromArgb(0x1B, 0x22, 0x26) : Color.White, Cl.Dark ? Color.FromArgb(0x20, 0x28, 0x2D) : Color.FromArgb(0xEE, 0xEE, 0xEE), LinearGradientMode.Vertical)) g.FillRectangle(b, rect);
        g.DrawLine(Cl.GridPen, 0, rect.Top, W, rect.Top); g.DrawLine(Cl.GridPen, 0, rect.Bottom - 1, W, rect.Bottom - 1);
        var r = _runner;
        bool has = _pos.IfWins != 0 || _pos.IfLoses != 0;
        string sign(double v) => (v >= 0 ? "+" : "") + v.ToString("0.00");
        TextRenderer.DrawText(g, "If it wins", Cl.Ui, new Point(8, rect.Top + 8), Cl.Dim);
        TextRenderer.DrawText(g, has ? sign(_pos.IfWins) : "-", Cl.UiBold, new Point(70, rect.Top + 8), _pos.IfWins >= 0 ? Color.FromArgb(0, 0x80, 0) : Color.Firebrick);
        TextRenderer.DrawText(g, "If it loses", Cl.Ui, new Point(8, rect.Top + 24), Cl.Dim);
        TextRenderer.DrawText(g, has ? sign(_pos.IfLoses) : "-", Cl.UiBold, new Point(70, rect.Top + 24), _pos.IfLoses >= 0 ? Color.FromArgb(0, 0x80, 0) : Color.Firebrick);
        double bb = r?.Ex?.AvailableToBack?.FirstOrDefault()?.Price ?? 0, bl = r?.Ex?.AvailableToLay?.FirstOrDefault()?.Price ?? 0;
        TextRenderer.DrawText(g, $"Back {(bb > 0 ? Cl.Price(bb) : "-")}    Lay {(bl > 0 ? Cl.Price(bl) : "-")}", Cl.Ui, new Point(8, rect.Top + 48), Cl.Ink);
        if (r?.Status is string st && st != "ACTIVE") TextRenderer.DrawText(g, st, Cl.UiBold, new Point(8, rect.Top + 66), Color.Firebrick);
        _greenBtn = new Rectangle(W - 82, rect.Bottom - 30, 74, 22);
        using (var b = new SolidBrush(has ? Color.FromArgb(0x90, 0xEE, 0x90) : Cl.Panel)) g.FillRectangle(b, _greenBtn);
        g.DrawRectangle(Cl.GridPen, _greenBtn);
        TextRenderer.DrawText(g, "Green up", Cl.UiBold, _greenBtn, has ? Cl.Ink : Color.Gray, TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    void PaintScale(Graphics g, int W)
    {
        var rect = new Rectangle(0, HeaderH + InfoH, W, ScaleH);
        Cl.Gradient(g, rect, Cl.ScaleTop, Cl.ScaleBottom);
        int n = Scales.Length, span = W - 20;
        using var sep = new Pen(Color.FromArgb(0x60, 0x60, 0xC8));
        for (int i = 0; i < n; i++)
        {
            var cell = new Rectangle(3 + i * span / n, rect.Top, span / n, ScaleH);
            if (Scales[i] == BarScale) { using var sel = new SolidBrush(Color.FromArgb(0x6A, 0x6A, 0xF0)); g.FillRectangle(sel, cell); }
            Cell(g, Scales[i].ToString(), Cl.Scale, cell, Color.FromArgb(0x1A, 0x1A, 0x66), centre: true);
            if (i > 0) g.DrawLine(sep, cell.X, rect.Top + 3, cell.X, rect.Bottom - 4);
        }
        g.DrawLine(Cl.GridPen, 0, rect.Top, W, rect.Top);
    }

    /// <summary>Green (waiting backs, upper) over red (waiting lays, lower), split in proportion to the money on each side.</summary>
    void PaintDepthBar(Graphics g, int x, int width, int top, int height, double totBack, double totLay)
    {
        if (height <= 0) return;
        double blue = (_runner?.Ex?.AvailableToLay ?? new()).Sum(p => p.Size), pink = totBack;
        double f = blue + pink <= 0 ? 0.5 : blue / (blue + pink);
        int split = top + (int)(height * f);
        using var gb = new SolidBrush(Cl.GreenBar); using var gd = new SolidBrush(Cl.GreenBarDark);
        using var rb = new SolidBrush(Cl.RedBar); using var rd = new SolidBrush(Cl.RedBarDark);
        int band = Math.Min(12, Math.Max(4, height / 25));
        g.FillRectangle(gb, x, top, width, Math.Max(0, split - top - band));
        g.FillRectangle(gd, x, Math.Max(top, split - band), width, Math.Min(band, split - top));
        g.FillRectangle(rd, x, split, width, Math.Min(band, top + height - split));
        g.FillRectangle(rb, x, split + band, width, Math.Max(0, top + height - split - band));
        g.DrawRectangle(Cl.GridPen, x, top, width - 1, height - 1);
    }

    void PaintBottom(Graphics g, int W, int y, List<CurrentOrder> mine)
    {
        int h = Math.Max(30, Height - y - 3), half = W / 2;
        foreach (var (idx, side) in new[] { (0, "BACK"), (1, "LAY") })
        {
            var box = new Rectangle(idx * half + 2, y + 4, half - 4, h - 4);
            using (var wb = new SolidBrush(Cl.LBg)) g.FillRectangle(wb, box);
            ControlPaint.DrawBorder3D(g, box, Border3DStyle.Sunken);
            int ly = box.Y + 4;
            foreach (var o in mine.Where(o => o.Side == side).OrderByDescending(o => o.Price))
            {
                if (ly + 14 > box.Bottom) break;
                var line = new Rectangle(box.X + 3, ly, box.Width - 6, 14);
                Cell(g, $"{o.SizeRemaining:0.00} @ {Cl.Price(o.Price)}", Cl.Ui, line, side == "BACK" ? Color.FromArgb(0x10, 0x60, 0xB0) : Color.FromArgb(0xB0, 0x30, 0x60));
                _orderLines.Add((line, o)); ly += 14;
            }
        }
    }
}
