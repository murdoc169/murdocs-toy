using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>Pop-up trading window for one market: a ladder per runner side by side, one-click betting, stake buttons.</summary>
sealed class LadderForm : Form
{
    readonly BetfairClient _bf;
    readonly MarketCatalogEntry _market;
    readonly Options _opt;
    readonly bool _privacy;
    readonly LiveScoreFeed? _feed;
    readonly Label _score = new() { Dock = DockStyle.Top, Height = 0, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 13f, FontStyle.Bold), BackColor = Color.FromArgb(18, 21, 26), ForeColor = Color.White };
    DateTime _flashUntil;

    readonly FlowLayoutPanel _top = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 4, 4, 0), WrapContents = false };
    readonly TableLayoutPanel _body = new() { Dock = DockStyle.Fill, RowCount = 1 };
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(6, 4, 0, 0), BackColor = Color.FromArgb(40, 44, 52), ForeColor = Color.White };
    readonly NumericUpDown _custom = new() { DecimalPlaces = 2, Minimum = 1, Maximum = 100000, Width = 80 };
    readonly LiquidityPanel _liq = new() { Dock = DockStyle.Bottom, Height = 96 };
    readonly System.Windows.Forms.Timer _timer = new();
    readonly List<(RunnerCatalog Info, LadderControl Ladder)> _ladders = new();

    double _stake;
    bool _busy;
    MarketBook? _book;
    List<CurrentOrder> _orders = new();

    public LadderForm(BetfairClient bf, MarketCatalogEntry market, Options opt, bool privacy, LiveScoreFeed? feed = null)
    {
        _bf = bf; _market = market; _opt = opt; _privacy = privacy; _feed = feed;
        Text = $"{market.Event?.Name} - {market.MarketName}";
        Size = new Size(Math.Max(760, 300 * (market.Runners?.Count ?? 3) + 20), 760);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        Controls.Add(_body); Controls.Add(_top); Controls.Add(_score); Controls.Add(_liq); Controls.Add(_status);
        _stake = opt.DefaultStake;
        BuildLadders();
        BuildTopBar();
        ApplyOptions();

        _timer.Tick += async (_, _) => await Poll();
        Shown += async (_, _) => { await Poll(); _timer.Start(); };
        if (_feed != null) { _feed.Alert += OnAlert; _feed.ScoreChanged += OnScoreChanged; }
        MirrorState.LadderOpened(market.MarketId, Text);
        FormClosed += (_, _) => { if (_feed != null) { _feed.Alert -= OnAlert; _feed.ScoreChanged -= OnScoreChanged; } _timer.Stop(); MirrorState.LadderClosed(market.MarketId); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    void BuildLadders()
    {
        var runners = (_market.Runners ?? new()).OrderBy(r => r.SortPriority ?? 0).ToList();
        _body.ColumnCount = Math.Max(1, runners.Count);
        _body.ColumnStyles.Clear();
        foreach (var r in runners)
        {
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / runners.Count));
            var lc = new LadderControl(_opt) { Dock = DockStyle.Fill, Margin = new Padding(1, 0, 1, 0) };
            lc.BackClicked += async p => await Place("BACK", r, p);
            lc.LayClicked += async p => await Place("LAY", r, p);
            lc.CancelClicked += async p => await CancelAt(r, p);
            lc.GreenUpClicked += async () => await GreenUp(r);
            _body.Controls.Add(lc, _ladders.Count, 0);
            _ladders.Add((r, lc));
        }
    }

    void BuildTopBar()
    {
        _top.Controls.Clear();
        _top.Controls.Add(new Label { Text = "Stake", AutoSize = true, Padding = new Padding(0, 6, 4, 0), Font = new Font(Font, FontStyle.Bold) });
        foreach (var s in _opt.StakePresets)
        {
            var b = new Button { Text = s.ToString("0.##"), Width = 52, Height = 28, Tag = s, FlatStyle = FlatStyle.System };
            b.Click += (_, _) => { _stake = s; _custom.Value = (decimal)s; Mark(); };
            _top.Controls.Add(b);
        }
        _custom.Value = (decimal)Math.Clamp(_stake, 1, 100000);
        _custom.ValueChanged += (_, _) => { _stake = (double)_custom.Value; Mark(); };
        _top.Controls.Add(_custom);
        var cancelAll = new Button { Text = "Cancel all", Width = 80, Height = 28 };
        cancelAll.Click += async (_, _) => await CancelAll();
        var opts = new Button { Text = "Options", Width = 70, Height = 28 };
        opts.Click += (_, _) => { using var f = new OptionsForm(_opt); if (f.ShowDialog(this) == DialogResult.OK) { _opt.Save(); BuildTopBar(); ApplyOptions(); foreach (var l in _ladders) l.Ladder.Invalidate(); } };
        var onTop = new CheckBox { Text = "On top", AutoSize = true, Checked = _opt.AlwaysOnTop, Padding = new Padding(8, 4, 0, 0) };
        onTop.CheckedChanged += (_, _) => TopMost = onTop.Checked;
        _top.Controls.AddRange(new Control[] { cancelAll, opts, onTop });
        Mark();
    }

    void Mark()
    {
        foreach (Control c in _top.Controls)
            if (c is Button { Tag: double s }) { bool on = Math.Abs(s - _stake) < 1e-9; c.BackColor = on ? Color.Gold : SystemColors.Control; ((Button)c).UseVisualStyleBackColor = !on; }
    }

    void ApplyOptions()
    {
        TopMost = _opt.AlwaysOnTop;
        _timer.Interval = Math.Max(250, _opt.RefreshMs);
        Mark();
    }

    void Say(string s) => _status.Text = s;

    async Task Poll()
    {
        if (_busy) return; _busy = true;
        try
        {
            _book = (await _bf.ListMarketBookAsync(new[] { _market.MarketId })).FirstOrDefault();
            if (!_privacy) { try { _orders = await _bf.ListCurrentOrdersAsync(_market.MarketId); } catch (BetfairException) { } }
            if (_book != null) _liq.Update(_book);
            ShowScore();
            foreach (var (info, ladder) in _ladders)
                ladder.SetData(info.RunnerName, _book?.Runners?.FirstOrDefault(r => r.SelectionId == info.SelectionId),
                    _orders.Where(o => o.SelectionId == info.SelectionId).ToList());
            if (_book != null)
                _status.Text = $"{_book.Status}{(_book.InPlay == true ? " IN-PLAY" : "")}   Matched {_book.TotalMatched:#,0}" +
                               (_book.IsMarketDataDelayed ? "   DELAYED DATA (delayed app key)" : "") + (_lastMsg == "" ? "" : "   |   " + _lastMsg);
        }
        catch (Exception ex) { Say(ex.Message); }
        finally { _busy = false; }
    }

    string _lastMsg = "";

    /// <summary>Live score banner for this match (when the relay knows it).</summary>
    void ShowScore()
    {
        var m = _feed?.Find(_market.Event?.Name ?? "");
        if (m == null) { _score.Height = 0; return; }
        _score.Height = 30;
        bool flash = DateTime.UtcNow < _flashUntil;
        _score.Text = $"{m.Home}  {m.HomeScore} - {m.AwayScore}  {m.Away}";
        _score.BackColor = flash ? Color.Gold : Color.FromArgb(18, 21, 26);
        _score.ForeColor = flash ? Color.Black : Color.White;
    }

    void OnScoreChanged(LiveMatch m, int prevHome, int prevAway)
    {
        if (IsDisposed || TeamMatcher.Score(_market.Event?.Name ?? "", m.Home, m.Away) <= 0) return;
        BeginInvoke(() => { _flashUntil = DateTime.UtcNow.AddSeconds(20); _lastMsg = $"Score {prevHome}-{prevAway} -> {m.ScoreText}"; ShowScore(); });
    }

    void OnAlert(LiveAlert a)
    {
        var m = _feed?.Find(_market.Event?.Name ?? "");
        if (m == null || a.Home != m.Home || a.Away != m.Away) return;
        if (IsDisposed) return;
        BeginInvoke(() => { _flashUntil = DateTime.UtcNow.AddSeconds(20); _lastMsg = a.Text; ShowScore(); });
    }

    async Task Place(string side, RunnerCatalog r, double price)
    {
        if (_opt.ConfirmBets && MessageBox.Show($"{side} {r.RunnerName} @ {price:0.##} for {_stake:0.00}?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        await Send(side, r, price, _stake);
    }

    async Task Send(string side, RunnerCatalog r, double price, double size)
    {
        try
        {
            var rep = await _bf.PlaceOrderAsync(_market.MarketId, new PlaceInstruction(r.SelectionId, side, price, size, _opt.Persistence));
            _lastMsg = $"{side} {r.RunnerName} {price:0.##} x{size:0.00}: {rep.Status}{(rep.ErrorCode == null ? "" : " " + rep.ErrorCode)}";
        }
        catch (BetfairException ex) { _lastMsg = $"{side} failed: {ex.Code}" + (ex.Code is "INVALID_APP_KEY" or "PERMISSION_DENIED" ? " (delayed keys cannot bet)" : ""); }
        await Poll();
    }

    async Task CancelAt(RunnerCatalog r, double price)
    {
        foreach (var o in _orders.Where(o => o.SelectionId == r.SelectionId && o.Status == "EXECUTABLE" && Math.Abs(o.Price - price) < 1e-9).ToList())
            try { await _bf.CancelOrderAsync(_market.MarketId, o.BetId); _lastMsg = $"Cancelled {o.Side} {o.Price:0.##}"; }
            catch (BetfairException ex) { _lastMsg = "Cancel failed: " + ex.Code; }
        await Poll();
    }

    async Task CancelAll()
    {
        foreach (var o in _orders.Where(o => o.Status == "EXECUTABLE").ToList())
            try { await _bf.CancelOrderAsync(_market.MarketId, o.BetId); } catch (BetfairException ex) { _lastMsg = "Cancel failed: " + ex.Code; }
        _lastMsg = "Cancelled all open orders";
        await Poll();
    }

    async Task GreenUp(RunnerCatalog r)
    {
        var run = _book?.Runners?.FirstOrDefault(x => x.SelectionId == r.SelectionId);
        var pos = Position.Of(_orders, r.SelectionId);
        if (run?.Ex == null || (pos.IfWins == 0 && pos.IfLoses == 0)) { _lastMsg = "Nothing to green up"; return; }
        bool wantBack = pos.IfLoses > pos.IfWins;
        double price = wantBack ? run.Ex.AvailableToBack?.FirstOrDefault()?.Price ?? 0 : run.Ex.AvailableToLay?.FirstOrDefault()?.Price ?? 0;
        if (price <= 0) { _lastMsg = "No price to green up at"; return; }
        var g = pos.GreenUp(price);
        if (g == null) { _lastMsg = "Already level"; return; }
        if (MessageBox.Show($"{g.Value.Side} {r.RunnerName} @ {price:0.##} for {g.Value.Size:0.00} to lock {pos.LockedProfit(price):+0.00;-0.00}?", "Green up", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        await Send(g.Value.Side, r, price, g.Value.Size);
    }
}
