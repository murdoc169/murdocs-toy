using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>One runner's price ladder: my orders | back | price | lay | traded volume. Custom drawn for density and speed.</summary>
sealed class LadderControl : Control
{
    const int HeaderH = 70;
    static readonly double[] Cols = { 0.17, 0.22, 0.18, 0.22, 0.21 };   // orders, back, price, lay, volume

    public event Action<double>? BackClicked, LayClicked, CancelClicked;
    public event Action? GreenUpClicked;

    readonly Options _opt;
    string _title = "";
    Runner? _runner;
    List<CurrentOrder> _orders = new();
    Position _pos = new(0, 0);
    double _centre = 2.0;
    bool _manual;
    Rectangle _greenBtn;

    public LadderControl(Options opt)
    {
        _opt = opt;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
    }

    public void SetData(string title, Runner? runner, List<CurrentOrder> myOrders)
    {
        _title = title; _runner = runner; _orders = myOrders;
        _pos = runner == null ? new Position(0, 0) : Position.Of(myOrders, runner.SelectionId);
        if (_opt.AutoCentre && !_manual)
            _centre = runner?.LastPriceTraded ?? runner?.Ex?.AvailableToBack?.FirstOrDefault()?.Price ?? _centre;
        Invalidate();
    }

    int Rows => Math.Max(5, (Height - HeaderH) / _opt.RowHeight);
    IEnumerable<double> Prices() => PriceLadder.Window(_centre, Rows / 2, Rows - Rows / 2 - 1);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _manual = true;
        for (int i = 0; i < 3; i++) _centre = e.Delta > 0 ? PriceLadder.Up(_centre) : PriceLadder.Down(_centre);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Y < HeaderH)
        {
            if (_greenBtn.Contains(e.Location)) GreenUpClicked?.Invoke();
            else { _manual = false; SetData(_title, _runner, _orders); }   // click the header to re-centre
            return;
        }
        int row = (e.Y - HeaderH) / _opt.RowHeight;
        var prices = Prices().ToList();
        if (row < 0 || row >= prices.Count) return;
        double price = prices[row];
        int x = e.X, w = Width;
        double acc = 0; int col = 0;
        for (; col < Cols.Length; col++) { acc += Cols[col] * w; if (x < acc) break; }
        if (col == 0) CancelClicked?.Invoke(price);
        else if (col == 1) BackClicked?.Invoke(price);
        else if (col == 3) LayClicked?.Invoke(price);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.White);
        using var font = new Font("Segoe UI", _opt.FontSize);
        using var bold = new Font("Segoe UI", _opt.FontSize, FontStyle.Bold);
        using var big = new Font("Segoe UI", _opt.FontSize + 2, FontStyle.Bold);
        using var small = new Font("Segoe UI", _opt.FontSize - 1);
        var centreFmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var back = _opt.Back; var lay = _opt.Lay;

        // header
        using (var hb = new SolidBrush(Color.FromArgb(40, 44, 52))) g.FillRectangle(hb, 0, 0, Width, HeaderH);
        g.DrawString(_title, big, Brushes.White, new RectangleF(6, 2, Width - 90, 22), new StringFormat { Trimming = StringTrimming.EllipsisCharacter });
        var r = _runner;
        g.DrawString($"LTP {r?.LastPriceTraded:0.##}   Matched {r?.TotalMatched:#,0}", small, Brushes.Gainsboro, 6, 26);
        bool hasPos = _pos.IfWins != 0 || _pos.IfLoses != 0;
        using (var win = new SolidBrush(_pos.IfWins >= 0 ? Color.LightGreen : Color.Salmon))
            g.DrawString($"Win {(hasPos ? _pos.IfWins.ToString("+0.00;-0.00;0.00") : "-")}", bold, win, 6, 46);
        using (var lose = new SolidBrush(_pos.IfLoses >= 0 ? Color.LightGreen : Color.Salmon))
            g.DrawString($"Lose {(hasPos ? _pos.IfLoses.ToString("+0.00;-0.00;0.00") : "-")}", bold, lose, 90, 46);
        _greenBtn = new Rectangle(Width - 78, 26, 72, 34);
        using (var gb = new SolidBrush(hasPos ? Color.FromArgb(46, 160, 90) : Color.FromArgb(80, 86, 96))) g.FillRectangle(gb, _greenBtn);
        g.DrawString("Green up", bold, Brushes.White, _greenBtn, centreFmt);

        if (r?.Ex == null) return;
        var backs = (r.Ex.AvailableToBack ?? new()).ToDictionary(p => p.Price, p => p.Size);
        var lays = (r.Ex.AvailableToLay ?? new()).ToDictionary(p => p.Price, p => p.Size);
        var traded = (r.Ex.TradedVolume ?? new()).ToDictionary(p => p.Price, p => p.Size);
        double maxVol = traded.Count == 0 ? 1 : traded.Values.Max();
        var open = _orders.Where(o => o.Status == "EXECUTABLE" && o.SizeRemaining > 0 && r != null && o.SelectionId == r.SelectionId).ToList();

        int[] x = new int[Cols.Length + 1];
        for (int i = 0; i < Cols.Length; i++) x[i + 1] = x[i] + (int)(Cols[i] * Width);
        x[^1] = Width;
        using var line = new Pen(Color.FromArgb(215, 215, 215));
        using var backLight = new SolidBrush(back); using var layLight = new SolidBrush(lay);
        using var backPale = new SolidBrush(ControlPaint.Light(back, 0.6f)); using var layPale = new SolidBrush(ControlPaint.Light(lay, 0.6f));
        using var gold = new SolidBrush(Color.Gold); using var grey = new SolidBrush(Color.FromArgb(235, 235, 235));
        using var volBar = new SolidBrush(Color.FromArgb(200, 220, 200));
        double bestBack = r.Ex.AvailableToBack?.FirstOrDefault()?.Price ?? 0, bestLay = r.Ex.AvailableToLay?.FirstOrDefault()?.Price ?? 0;

        int y = HeaderH;
        foreach (var price in Prices())
        {
            var rowRect = new Rectangle(0, y, Width, _opt.RowHeight);
            Rectangle Cell(int c) => new(x[c], y, x[c + 1] - x[c], _opt.RowHeight);
            g.FillRectangle(grey, Cell(2));
            // my open orders at this price
            var mine = open.Where(o => Math.Abs(o.Price - price) < 1e-9).ToList();
            if (mine.Count > 0)
            {
                bool isBack = mine[0].Side == "BACK";
                g.FillRectangle(isBack ? backLight : layLight, Cell(0));
                g.DrawString(mine.Sum(o => o.SizeRemaining).ToString("0.00"), bold, Brushes.Black, Cell(0), centreFmt);
            }
            if (backs.TryGetValue(price, out var bs)) { g.FillRectangle(price == bestBack ? backLight : backPale, Cell(1)); g.DrawString(bs.ToString("#,0"), font, Brushes.Black, Cell(1), centreFmt); }
            if (lays.TryGetValue(price, out var ls)) { g.FillRectangle(price == bestLay ? layLight : layPale, Cell(3)); g.DrawString(ls.ToString("#,0"), font, Brushes.Black, Cell(3), centreFmt); }
            if (traded.TryGetValue(price, out var tv))
            {
                var vc = Cell(4); g.FillRectangle(volBar, vc.X, vc.Y + 1, (int)(vc.Width * (tv / maxVol)), vc.Height - 2);
                g.DrawString(tv.ToString("#,0"), small, Brushes.DimGray, vc, centreFmt);
            }
            if (r.LastPriceTraded is double ltp && Math.Abs(ltp - price) < 1e-9) g.FillRectangle(gold, Cell(2));
            g.DrawString(price.ToString("0.##"), bold, Brushes.Black, Cell(2), centreFmt);
            g.DrawLine(line, 0, y + _opt.RowHeight - 1, Width, y + _opt.RowHeight - 1);
            y += _opt.RowHeight;
        }
        foreach (var cx in x.Skip(1).Take(Cols.Length - 1)) g.DrawLine(line, cx, HeaderH, cx, Height);
    }
}
