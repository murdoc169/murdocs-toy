using System.Drawing.Drawing2D;
using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

public record CardRunner(string Name, double? Ltp, string Trend, double? BestLay, double Matched, Position Pos,
                         List<PriceSize> Backs, List<PriceSize> Lays, int Gap);

public record CardData(string Title, string DateTimeText, string ClockText, bool ClockLive, string MatchedText, string Symbol,
                       double BackPct, double LayPct, int Selections, string? Score, bool Flash, bool Armed, string Status,
                       List<CardRunner> Runners);

/// <summary>The small card shown while a Classic ladder is minimized: a compact view of the market's prices.</summary>
sealed class ClassicCard : ChromeForm
{
    readonly CardView _view = new() { Dock = DockStyle.Fill };
    readonly Action _restore, _closeLadder;

    public ClassicCard(string title, int runners, Action restore, Action closeLadder)
    {
        _restore = restore; _closeLadder = closeLadder;
        Text = title; Font = Cl.Ui; BackColor = Cl.Border;
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(520, CardView.HeaderH + CardView.BarH + runners * (CardView.BandH + CardView.CellsH) + 2);
        Theme.TitleBar(this, Cl.BarTop, Color.White);
        Controls.Add(_view);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Restore ladder", null, (_, _) => _restore());
        menu.Items.Add("Close ladder", null, (_, _) => _closeLadder());
        _view.ContextMenuStrip = menu;
        _view.DoubleClick += (_, _) => _restore();
        _view.OpenClicked += () => _restore();
        new ToolTip { InitialDelay = 300 }.SetToolTip(_view, "Click Open ladder, or double-click the card, to bring the full ladder back." + Environment.NewLine + "Right-click for more.");
        _view.Cursor = Cursors.Hand;
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; _closeLadder(); } };
    }

    public void SetData(CardData d) { Text = d.Title; _view.Data = d; _view.Invalidate(); }
}

sealed class CardView : Control
{
    public const int HeaderH = 28, BarH = 20, BandH = 19, CellsH = 46;
    public CardData? Data { get; set; }
    public event Action? OpenClicked;
    Rectangle _openBtn;
    static readonly Color Grey = Color.FromArgb(0xDF, 0xE4, 0xE7), BandTop = Color.FromArgb(0x5C, 0x74, 0x83), BandBottom = Color.FromArgb(0x2F, 0x55, 0x60);
    static readonly Font Big = new("Tahoma", 11f, FontStyle.Bold), Mid = new("Tahoma", 9.75f, FontStyle.Bold), Clock = new("Tahoma", 13f, FontStyle.Bold);

    public CardView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseDown(MouseEventArgs e) { if (_openBtn.Contains(e.Location)) OpenClicked?.Invoke(); }

    static void T(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags align = TextFormatFlags.Left) =>
        TextRenderer.DrawText(g, s, f, r, c, align | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Cl.Light);
        var d = Data;
        if (d == null) { T(g, "waiting for data...", Cl.Ui, new Rectangle(8, 6, Width, 20), Color.Gray); return; }
        int W = Width, y = 0;

        // header: date and time, clock, live score, matched, and the button that brings the full ladder back
        g.FillRectangle(new SolidBrush(Cl.Light), 0, 0, W, HeaderH);
        T(g, d.DateTimeText, Mid, new Rectangle(8, 0, 142, HeaderH), Color.Black);
        T(g, d.ClockText, Clock, new Rectangle(148, 0, 100, HeaderH), d.ClockLive ? Color.Firebrick : Cl.ClockBlue, TextFormatFlags.HorizontalCenter);
        if (d.Score != null)
        {
            var box = new Rectangle(252, 4, 66, HeaderH - 8);
            using (var b = new SolidBrush(d.Flash ? Color.Gold : Color.FromArgb(18, 21, 26))) g.FillRectangle(b, box);
            T(g, d.Score, Cl.UiBold, box, d.Flash ? Color.Black : Color.White, TextFormatFlags.HorizontalCenter);
        }
        T(g, d.MatchedText, Cl.Ui, new Rectangle(322, 0, 94, HeaderH), Color.Black, TextFormatFlags.Right);
        _openBtn = new Rectangle(W - 100, 3, 94, HeaderH - 6);
        using (var b = new SolidBrush(Cl.StakeBtn)) g.FillRectangle(b, _openBtn);
        T(g, "Open ladder ▸", Cl.UiBold, _openBtn, Color.White, TextFormatFlags.HorizontalCenter);
        y = HeaderH;

        // back / lay percentages
        int half = W / 2 - 4;
        using (var b = new SolidBrush(Cl.BackRow)) g.FillRectangle(b, 0, y, half, BarH);
        using (var b = new SolidBrush(Cl.LayRow)) g.FillRectangle(b, half + 8, y, W - half - 8, BarH);
        T(g, $"Selections: {d.Selections} ({d.Selections})", Cl.Ui, new Rectangle(4, y, 110, BarH), Color.Black);
        T(g, d.BackPct > 0 ? d.BackPct.ToString("0.0") + "%" : "-", Cl.Ui, new Rectangle(110, y, 60, BarH), Color.Black, TextFormatFlags.HorizontalCenter);
        T(g, "Back", Cl.UiBold, new Rectangle(half - 60, y, 56, BarH), Color.Black, TextFormatFlags.Right);
        T(g, "Lay", Cl.UiBold, new Rectangle(half + 14, y, 40, BarH), Color.Black);
        T(g, d.LayPct > 0 ? d.LayPct.ToString("0.0") + "%" : "-", Cl.Ui, new Rectangle(half + 70, y, 60, BarH), Color.Black, TextFormatFlags.HorizontalCenter);
        T(g, "Winners: (1)", Cl.Ui, new Rectangle(W - 100, y, 96, BarH), Color.Black, TextFormatFlags.Right);
        y += BarH;

        foreach (var r in d.Runners)
        {
            Cl.Gradient(g, new Rectangle(0, y, W, BandH), BandTop, BandBottom);
            T(g, r.Name, Cl.UiBold, new Rectangle(6, y, 170, BandH), Color.White);
            T(g, (r.Ltp?.ToString("0.0#") ?? "-") + " " + r.Trend, Cl.UiBold, new Rectangle(176, y, 90, BandH), Color.White, TextFormatFlags.HorizontalCenter);
            T(g, r.BestLay?.ToString("0.0#") ?? "", Cl.Ui, new Rectangle(268, y, 60, BandH), Color.Gainsboro, TextFormatFlags.HorizontalCenter);
            bool hasPos = r.Pos.IfWins != 0 || r.Pos.IfLoses != 0;
            if (hasPos) T(g, d.Symbol + r.Pos.IfWins.ToString("+0.##;-0.##"), Cl.UiBold, new Rectangle(330, y, 60, BandH), r.Pos.IfWins >= 0 ? Color.LightGreen : Color.Salmon, TextFormatFlags.HorizontalCenter);
            T(g, d.Symbol + Liquidity.Spaced(r.Matched), Cl.Ui, new Rectangle(W - 78, y, 74, BandH), Color.White, TextFormatFlags.Right);
            y += BandH;

            int leftW = 76, cellW = (W - leftW - 24 - 76) / 6;
            using (var b = new SolidBrush(Grey)) { g.FillRectangle(b, 0, y, leftW, CellsH); g.FillRectangle(b, W - 76, y, 76, CellsH); g.FillRectangle(b, leftW + 3 * cellW, y, 24, CellsH); }
            T(g, r.Gap > 0 ? r.Gap.ToString() : "", Cl.Ui, new Rectangle(leftW + 3 * cellW, y, 24, CellsH), Color.White, TextFormatFlags.HorizontalCenter);
            for (int i = 0; i < 3; i++)
            {
                // back cells run from the third-best price on the left to the best next to the centre; lay cells run best first
                var back = r.Backs.Count > 2 - i ? r.Backs[2 - i] : null;
                var lay = r.Lays.Count > i ? r.Lays[i] : null;
                Cell(g, new Rectangle(leftW + i * cellW, y, cellW, CellsH), back, Cl.BackRow, d.Symbol);
                Cell(g, new Rectangle(leftW + 3 * cellW + 24 + i * cellW, y, cellW, CellsH), lay, Cl.LayRow, d.Symbol);
            }
            y += CellsH;
        }
    }

    static void Cell(Graphics g, Rectangle r, PriceSize? p, Color colour, string symbol)
    {
        if (p == null) { using var e = new SolidBrush(Color.FromArgb(0xDF, 0xE4, 0xE7)); g.FillRectangle(e, r); return; }
        using (var b = new SolidBrush(colour)) g.FillRectangle(b, r);
        using var edge = new Pen(Color.FromArgb(0xDF, 0xE4, 0xE7)); g.DrawRectangle(edge, r.X, r.Y, r.Width - 1, r.Height - 1);
        T(g, p.Price.ToString("0.0#"), Big, new Rectangle(r.X, r.Y + 2, r.Width, 22), Color.Black, TextFormatFlags.HorizontalCenter);
        T(g, symbol + Math.Round(p.Size).ToString("0"), Cl.Ui, new Rectangle(r.X, r.Y + 24, r.Width, 18), Color.Black, TextFormatFlags.HorizontalCenter);
    }
}
