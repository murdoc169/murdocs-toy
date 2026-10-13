using System.Globalization;
using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>
/// Classic trading window for one market: a ladder per runner side by side, and the slate tools panel on the right
/// (clock and book percentages, back/lay staking, bet persistence, market information, ladder selector).
/// </summary>
sealed partial class ClassicLadderForm : Form
{
    readonly AppHost _host;
    readonly MarketCatalogEntry _market;
    Options Opt => _host.Opt;

    readonly List<(RunnerCatalog Info, ClassicLadderControl Ladder)> _ladders = new();
    readonly TableLayoutPanel _grid = new() { Dock = DockStyle.Fill, RowCount = 1, BackColor = Cl.Border, Padding = new Padding(0), Margin = new Padding(0) };
    readonly Panel _tools = new() { Dock = DockStyle.Right, Width = 300, BackColor = Cl.ToolBg, AutoScroll = true };
    readonly System.Windows.Forms.Timer _timer = new();

    // tools
    readonly Panel _infoRow = new() { Dock = DockStyle.Top, Height = 24, BackColor = Cl.Light };
    readonly ClassicClock _clock = new() { Text = "--:--:--" };
    readonly Label _backPct = new() { Dock = DockStyle.Fill, BackColor = Cl.BackRow, Font = Cl.Name, TextAlign = ContentAlignment.MiddleCenter };
    readonly Label _layPct = new() { Dock = DockStyle.Fill, BackColor = Cl.LayRow, Font = Cl.Name, TextAlign = ContentAlignment.MiddleCenter };
    readonly Label _mid = new() { Dock = DockStyle.Fill, BackColor = Color.White, Font = Cl.Name, ForeColor = Color.FromArgb(0, 0x80, 0), TextAlign = ContentAlignment.MiddleCenter };
    readonly Label _openCount = new() { Dock = DockStyle.Fill, BackColor = Cl.CountRow, Font = Cl.UiBold, TextAlign = ContentAlignment.MiddleCenter };
    readonly Label _matchedCount = new() { Dock = DockStyle.Fill, BackColor = Cl.CountRow, Font = Cl.UiBold, TextAlign = ContentAlignment.MiddleCenter };
    readonly TextBox _backBox = new() { Font = Cl.Name, TextAlign = HorizontalAlignment.Right, Width = 62 };
    readonly TextBox _layBox = new() { Font = Cl.Name, TextAlign = HorizontalAlignment.Right, Width = 62 };
    readonly Button _layMode = new() { Width = 20, Height = 20, FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 9f, FontStyle.Bold), ForeColor = Color.White };
    readonly ComboBox _persist = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Font = Cl.Ui };
    readonly Label _msg = new() { Dock = DockStyle.Fill, Font = Cl.Ui, ForeColor = Color.White, Padding = new Padding(6, 4, 6, 4) };
    readonly Label _market1 = new() { Dock = DockStyle.Fill, Font = Cl.Ui, Padding = new Padding(6, 4, 6, 4), BackColor = Cl.Light };
    readonly CheckedListBox _selector = new() { Dock = DockStyle.Fill, Font = Cl.Ui, CheckOnClick = true, BorderStyle = BorderStyle.None };

    MarketBook? _book;
    List<CurrentOrder> _orders = new();
    bool _busy;
    string _balance = "";
    DateTime _flashUntil;
    string _lastMsg = "";

    public ClassicLadderForm(AppHost host, MarketCatalogEntry market)
    {
        _host = host; _market = market;
        Font = Cl.Ui; BackColor = Cl.Border;
        Text = Title(null);
        var runners = (market.Runners ?? new()).OrderBy(r => r.SortPriority ?? 0).ToList();
        int n = Math.Max(1, runners.Count);
        int shown = Math.Min(n, 4), each = n > 3 ? 270 : 340;      // a big field (a race) opens with its four favourites, so the window does not need to be wide
        ClientSize = new Size(Math.Min(Screen.PrimaryScreen!.WorkingArea.Width - 40, shown * each + 300), Math.Min(Screen.PrimaryScreen!.WorkingArea.Height - 80, 940));
        MinimumSize = new Size(Math.Min(900, 120 * shown + 300), 520);
        StartPosition = FormStartPosition.CenterParent;
        Theme.TitleBar(this, Cl.BarTop, Color.White);
        TopMost = Opt.AlwaysOnTop;

        _grid.ColumnCount = n;
        foreach (var r in runners)
        {
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / n));
            var lc = new ClassicLadderControl { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 2, 0), RunnerName = r.RunnerName, BarScale = Opt.ClassicScale };
            lc.BackAt += async p => await Place("BACK", r, p);
            lc.LayAt += async p => await Place("LAY", r, p);
            lc.CancelOrder += async o => await Cancel(o);
            lc.GreenUp += async () => await GreenUp(r);
            lc.StakeClicked += () => StakeMenu(lc);
            lc.ScaleChosen += s => { Opt.ClassicScale = s; Opt.Save(); foreach (var l in _ladders) { l.Ladder.BarScale = s; l.Ladder.Invalidate(); } };
            lc.Recentre += () => _ = Poll();
            _grid.Controls.Add(lc, _ladders.Count, 0);
            _ladders.Add((r, lc));
        }
        BuildTools(runners);
        Controls.Add(_grid); Controls.Add(_tools);
        RefreshStakeText();

        _timer.Interval = Math.Max(500, Opt.RefreshMs);
        _timer.Tick += async (_, _) => await Poll();
        Shown += async (_, _) => { await LoadBalance(); await Poll(); _timer.Start(); };
        _host.Scores.ScoreChanged += OnScoreChanged;
        MirrorState.LadderOpened(market.MarketId, Text);
        FormClosed += (_, _) => { Disarm("window closed"); _card?.Dispose(); _timer.Stop(); _host.Scores.ScoreChanged -= OnScoreChanged; MirrorState.LadderClosed(market.MarketId); };
    }

    string Title(MarketBook? b)
    {
        var sym = Cl.Symbol(_host.CurrencyCode);
        var vol = b?.TotalMatched is double m && m > 0 ? $"   {sym}{Liquidity.Spaced(m)}" : "";
        return $"{_market.Event?.Name} - {_market.MarketName}{vol}";
    }

    // ---- tools panel --------------------------------------------------------------------------------------
    void BuildTools(List<RunnerCatalog> runners)
    {
        _infoRow.Paint += (_, e) =>
        {
            var g = e.Graphics;
            using var line = new Pen(Cl.Border); g.DrawRectangle(line, 0, 0, _infoRow.Width - 1, _infoRow.Height - 1);
            int x = 6;
            var f = Cl.CurrencyFlag(_host.CurrencyCode);
            if (f != null && Flags.Get(f) != null) { g.DrawImage(Flags.Icon(f, new Size(16, 12)), x, 6); x += 22; }
            TextRenderer.DrawText(g, _balance, Cl.Name, new Point(x, 4), Color.Black);
            if (_book?.IsMarketDataDelayed == true) TextRenderer.DrawText(g, "DELAYED", Cl.UiBold, new Point(_infoRow.Width - 62, 5), Color.DarkOrange);
        };

        var status = new ClassicSection("Market & API Status", 98) { };
        var pctRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 28, ColumnCount = 3, Margin = new Padding(0), Padding = new Padding(0) };
        pctRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36)); pctRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28)); pctRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        pctRow.Controls.Add(_backPct, 0, 0); pctRow.Controls.Add(_mid, 1, 0); pctRow.Controls.Add(_layPct, 2, 0);
        var cntRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 24, ColumnCount = 2, Margin = new Padding(0), Padding = new Padding(0) };
        cntRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); cntRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cntRow.Controls.Add(_openCount, 0, 0); cntRow.Controls.Add(_matchedCount, 1, 0);
        status.Body.Controls.Add(cntRow); status.Body.Controls.Add(pctRow); status.Body.Controls.Add(_clock);

        // staking
        var staking = new ClassicSection("Staking & Tools", 226);
        Control Row(Color colour, string label, Control box, Control? btn1, Control? btn2)
        {
            var p = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = colour };
            p.Controls.Add(new Label { Text = label, Left = 6, Top = 8, AutoSize = true, Font = Cl.Ui });
            box.Left = 100; box.Top = 4; p.Controls.Add(box);
            if (btn1 != null) { btn1.Left = 168; btn1.Top = 4; p.Controls.Add(btn1); }
            if (btn2 != null) { btn2.Left = 192; btn2.Top = 4; p.Controls.Add(btn2); }
            return p;
        }
        var backS = new Button { Text = "S", Width = 20, Height = 20, FlatStyle = FlatStyle.Flat, BackColor = Cl.StakeBtn, ForeColor = Color.White, Font = new Font("Tahoma", 9f, FontStyle.Bold) };
        backS.FlatAppearance.BorderSize = 0;
        backS.Click += (_, _) => PresetMenu(backS, v => { Opt.ClassicBackStake = v; Opt.Save(); RefreshStakeText(); });
        var backReset = ResetButton(() => { Opt.ClassicBackStake = Opt.DefaultStake; Opt.Save(); RefreshStakeText(); });
        _layMode.FlatAppearance.BorderSize = 0;
        _layMode.Click += (_, _) => { Opt.ClassicLayLiability = !Opt.ClassicLayLiability; Opt.Save(); RefreshStakeText(); };
        var layReset = ResetButton(() => { Opt.ClassicLayStake = Opt.DefaultStake * 5; Opt.Save(); RefreshStakeText(); });
        _backBox.Leave += (_, _) => { if (TryParse(_backBox.Text, out var v) && v >= 1) { Opt.ClassicBackStake = v; Opt.Save(); } RefreshStakeText(); };
        _layBox.Leave += (_, _) => { if (TryParse(_layBox.Text, out var v) && v >= 1) { Opt.ClassicLayStake = v; Opt.Save(); } RefreshStakeText(); };
        _backBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _grid.Focus(); } };
        _layBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _grid.Focus(); } };
        _persist.Items.AddRange(new object[] { "Cancel at in-play (Lapse)", "Keep in-play (Persist)", "Take SP (Market on close)" });
        _persist.SelectedIndex = Opt.Persistence switch { "PERSIST" => 1, "MARKET_ON_CLOSE" => 2, _ => 0 };
        _persist.SelectedIndexChanged += (_, _) => { Opt.Persistence = _persist.SelectedIndex switch { 1 => "PERSIST", 2 => "MARKET_ON_CLOSE", _ => "LAPSE" }; Opt.Save(); };

        var orange = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Cl.Orange };
        var greenAll = new Button { Text = "Green up all runners", Left = 8, Top = 8, Width = 262, Height = 26, Font = Cl.UiBold, BackColor = Cl.GreenRow, FlatStyle = FlatStyle.Flat };
        var cancelAll = new Button { Text = "Cancel all open bets", Left = 8, Top = 40, Width = 262, Height = 26, Font = Cl.UiBold, BackColor = Color.White, FlatStyle = FlatStyle.Flat };
        greenAll.Click += async (_, _) => { foreach (var (info, _) in _ladders) await GreenUp(info, quiet: true); };
        cancelAll.Click += async (_, _) => await CancelAll();
        orange.Controls.Add(new Label { Text = "Blue column backs, pink column lays.", Left = 8, Top = 72, AutoSize = true, Font = Cl.Ui });
        orange.Controls.Add(cancelAll); orange.Controls.Add(greenAll);
        var msgPanel = new Panel { Dock = DockStyle.Fill, BackColor = Cl.ToolBg }; msgPanel.Controls.Add(_msg);
        staking.Body.Controls.Add(msgPanel);
        staking.Body.Controls.Add(orange);
        staking.Body.Controls.Add(Row(Cl.GreenRow, "Unmatched bets", _persist, null, null));
        staking.Body.Controls.Add(Row(Cl.LayRow, "Lay Staking", _layBox, _layMode, layReset));
        staking.Body.Controls.Add(Row(Cl.BackRow, "Back Staking", _backBox, backS, backReset));
        _persist.Left = 100; _persist.Width = 168;

        var info = new ClassicSection("Market Information", 120) { Collapsed = true };
        info.Body.Controls.Add(_market1);
        var sel = new ClassicSection($"Ladder Selector ({runners.Count})", Math.Max(40, runners.Count * 16 + 6));
        foreach (var r in runners) _selector.Items.Add(r.RunnerName, true);
        _selector.ItemCheck += (_, e) => BeginInvoke(() => ApplyLadderSelection());
        sel.Body.Controls.Add(_selector);

        void Tip(Control c, params string[] lines) => _tip.SetToolTip(c, string.Join(Environment.NewLine, lines));
        Tip(_backBox, "Back stake: the amount a click in the blue column bets.");
        Tip(backS, "S = stake. Pick a back stake from your presets.");
        Tip(backReset, "Reset the back stake to your default stake (Options).");
        Tip(_layBox, "Lay amount: what a click in the pink column bets.", "Its meaning depends on the button to the right: S = stake, L = liability.");
        Tip(_layMode, "S = the lay amount is the stake.", "L = the lay amount is your liability (the most you can lose); the stake is worked out from the price.", "Click to switch.");
        Tip(layReset, "Reset the lay amount.");
        Tip(_persist, "What happens to a bet that is not matched when the game goes in-play:", "Lapse = cancelled. Persist = stays in the market. Take SP = matched at the starting price.");
        Tip(greenAll, "Places the hedge bet on every selection that has matched bets, locking the same profit either way.");
        Tip(cancelAll, "Cancels every unmatched bet you have on this market.");
        Tip(_backPct, "Back book: if you backed every selection at the best back prices, this is the percentage you would pay in. Under 100% is normal.");
        Tip(_layPct, "Lay book: the same at the best lay prices. Over 100% is normal.");
        Tip(_mid, "The live score when a score relay is connected (flashes gold on a goal), otherwise 0.");
        Tip(_openCount, "Your unmatched bets on this market.");
        Tip(_matchedCount, "Your bets that have been at least partly matched.");
        Tip(_clock, "Time until the market starts (blue), or time since it started (red) once in-play.",
            "Click the little screen on the left to show or hide the live video panel (a small browser on betfair.com).", "Shift-click it to open the market in your normal browser instead.");
        _clock.IconClicked += () => { if ((ModifierKeys & Keys.Shift) != 0) OpenOnBetfair(); else _ = ToggleTv(); };
        Tip(_selector, "Tick or untick a selection to show or hide its ladder.");

        // sections stack top to bottom (docked Top: add in reverse)
        _tools.Controls.Add(BuildAutomation(runners)); _tools.Controls.Add(sel); _tools.Controls.Add(info); _tools.Controls.Add(staking); _tools.Controls.Add(status); _tools.Controls.Add(BuildTv()); _tools.Controls.Add(_infoRow);
    }

    /// <summary>Opens the market's page on betfair.com. Live video, where Betfair offers it for the account, plays on that page.</summary>
    void OpenOnBetfair()
    {
        var url = BetfairLinks.MarketPage(_market.MarketId, _market.EventType?.Name);
        try
        {
            // test seam only: MT_TEST_NO_BROWSER=1 reports the address instead of launching the browser
            if (Environment.GetEnvironmentVariable("MT_TEST_NO_BROWSER") != "1") System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            SetMsg("Opened the market on betfair.com: " + url);
        }
        catch (Exception ex) { SetMsg("Could not open the browser: " + ex.Message); }
    }

    Button ResetButton(Action onClick)
    {
        var b = new Button { Text = "↻", Width = 20, Height = 20, FlatStyle = FlatStyle.Flat, BackColor = Cl.Light, Font = new Font("Tahoma", 9f, FontStyle.Bold), Padding = new Padding(0) };
        b.FlatAppearance.BorderColor = Color.Gray; b.Click += (_, _) => onClick();
        return b;
    }

    static bool TryParse(string s, out double v) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    static string Num(double v) => v.ToString("0.##");

    void RefreshStakeText()
    {
        _backBox.Text = Num(Opt.ClassicBackStake); _layBox.Text = Num(Opt.ClassicLayStake);
        _layMode.Text = Opt.ClassicLayLiability ? "L" : "S";
        _layMode.BackColor = Opt.ClassicLayLiability ? Cl.LiabilityBtn : Cl.StakeBtn;
        foreach (var (_, l) in _ladders) { l.StakeText = Num(Opt.ClassicBackStake); l.BackStakeText = Num(Opt.ClassicBackStake); l.Invalidate(); }
    }

    void PresetMenu(Control at, Action<double> choose)
    {
        var menu = new ContextMenuStrip();
        foreach (var p in Opt.StakePresets) { var v = p; menu.Items.Add(Num(v), null, (_, _) => choose(v)); }
        menu.Show(at, new Point(0, at.Height));
    }

    void StakeMenu(ClassicLadderControl at)
    {
        var menu = new ContextMenuStrip();
        foreach (var p in Opt.StakePresets) { var v = p; menu.Items.Add($"Back stake {Num(v)}", null, (_, _) => { Opt.ClassicBackStake = v; Opt.Save(); RefreshStakeText(); }); }
        menu.Show(Cursor.Position);
    }

    bool _trimmed;

    /// <summary>A race can have 20 runners: start with the four shortest prices and let the user tick more in the Ladder Selector.</summary>
    void ShowFavouritesOnly()
    {
        if (_trimmed || _ladders.Count <= 4 || _book?.Runners == null) return;
        var order = _book.Runners.Where(r => r.Status == "ACTIVE")
            .OrderBy(r => r.LastPriceTraded ?? r.Ex?.AvailableToBack?.FirstOrDefault()?.Price ?? 1000).Take(4).Select(r => r.SelectionId).ToHashSet();
        if (order.Count == 0) return;
        _trimmed = true;
        for (int i = 0; i < _ladders.Count; i++) _selector.SetItemChecked(i, order.Contains(_ladders[i].Info.SelectionId));
        ApplyLadderSelection();
        SetMsg($"Showing the 4 favourites of {_ladders.Count} runners. Tick more in the Ladder Selector.");
    }

    void ApplyLadderSelection()
    {
        for (int i = 0; i < _ladders.Count; i++) _ladders[i].Ladder.Visible = _selector.GetItemChecked(i);
        var visible = _ladders.Count(l => l.Ladder.Visible);
        if (visible == 0) return;
        _grid.ColumnStyles.Clear();
        for (int i = 0; i < _ladders.Count; i++) _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, _ladders[i].Ladder.Visible ? 100f / visible : 0));
    }

    // ---- data ---------------------------------------------------------------------------------------------
    async Task LoadBalance()
    {
        try
        {
            if (_host.Privacy) { _balance = Cl.Symbol(_host.CurrencyCode) + "0,00"; _infoRow.Invalidate(); return; }
            var f = await _host.Bf.GetAccountFundsAsync();
            _balance = Cl.Symbol(_host.CurrencyCode) + f.AvailableToBetBalance.ToString("0.00");
        }
        catch (Exception) { _balance = ""; }
        _infoRow.Invalidate();
    }

    async Task Poll()
    {
        if (_busy) return; _busy = true;
        try
        {
            _book = (await _host.Bf.ListMarketBookAsync(new[] { _market.MarketId })).FirstOrDefault();
            if (!_host.Privacy) { try { _orders = await _host.Bf.ListCurrentOrdersAsync(_market.MarketId); } catch (BetfairException) { } }
            foreach (var (info, ladder) in _ladders)
                ladder.SetData(_book?.Runners?.FirstOrDefault(r => r.SelectionId == info.SelectionId),
                    _orders.Where(o => o.SelectionId == info.SelectionId).ToList(), Opt.AutoCentre);
            ShowFavouritesOnly();
            UpdateTools();
            AutoWatch();
            if (_card != null && _card.Visible) _card.SetData(BuildCardData());
            if (_host.SmokeMode && _book != null)
                _host.Smoke($"OK classic market={_market.MarketName} runners={_ladders.Count} delayed={_book.IsMarketDataDelayed}", 0);
        }
        catch (Exception ex) { SetMsg(ex.Message); }
        finally { _busy = false; }
    }

    void UpdateTools()
    {
        Text = Title(_book);
        foreach (var (_, l) in _ladders) l.CurrencySymbol = Cl.Symbol(_host.CurrencyCode);
        if (_book == null) return;
        double bp = Liquidity.BookPct(_book, true), lp = Liquidity.BookPct(_book, false);
        _backPct.Text = bp > 0 ? bp.ToString("0.0") + "%" : "-"; _layPct.Text = lp > 0 ? lp.ToString("0.0") + "%" : "-";
        var live = _host.Scores.Url == null ? null : _host.Scores.Find(_market.Event?.Name ?? "");
        bool flash = DateTime.UtcNow < _flashUntil;
        _mid.Text = live != null ? $"{live.HomeScore} - {live.AwayScore}" : (_orders.Count(o => o.Status == "EXECUTION_COMPLETE") > 0 ? "✔" : "0");
        _mid.BackColor = flash ? Color.Gold : Color.White;
        _openCount.Text = $"{_orders.Count(o => o.Status == "EXECUTABLE")} open";
        _matchedCount.Text = $"{_orders.Count(o => o.SizeMatched > 0)} matched";

        var start = _market.MarketStartTime?.ToUniversalTime();
        if (start != null)
        {
            var d = start.Value - DateTime.UtcNow;
            bool inPlay = _book.InPlay == true;
            var span = inPlay ? DateTime.UtcNow - start.Value : d;
            if (span < TimeSpan.Zero) span = span.Negate();
            _clock.Text = $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"; _clock.Invalidate();
            _clock.Digits = inPlay ? Color.Firebrick : Cl.ClockBlue; _clock.Online = true; _clock.Invalidate();
        }
        _market1.Text = $"Market: {_market.MarketName}\nMatch: {_market.Event?.Name}\nStatus: {_book.Status}{(_book.InPlay == true ? " (in-play)" : "")}\n" +
                        $"Start: {_market.MarketStartTime?.ToLocalTime():dd/MM HH:mm}\nMatched: {Cl.Symbol(_host.CurrencyCode)}{Liquidity.Spaced(_book.TotalMatched ?? 0)}\nId: {_market.MarketId}";
        _infoRow.Invalidate();
    }

    void SetMsg(string s) { _lastMsg = s; _msg.Text = s; }

    void OnScoreChanged(LiveMatch m, int ph, int pa)
    {
        if (IsDisposed || TeamMatcher.Score(_market.Event?.Name ?? "", m.Home, m.Away) <= 0) return;
        BeginInvoke(() => { _flashUntil = DateTime.UtcNow.AddSeconds(20); SetMsg($"Score {ph}-{pa} -> {m.ScoreText}"); UpdateTools(); AutoGoal(m, ph, pa); });
    }

    // ---- betting ------------------------------------------------------------------------------------------
    double StakeSize(string side, double price)
    {
        if (side == "BACK") return Opt.ClassicBackStake;
        if (!Opt.ClassicLayLiability) return Opt.ClassicLayStake;
        return Math.Max(1, Math.Round(Opt.ClassicLayStake / Math.Max(0.01, price - 1), 2));    // liability -> stake
    }


    /// <summary>With a delayed app key the prices on screen can be minutes old, so every bet is confirmed, whatever the one-click setting.</summary>
    static bool Confirm(bool delayed, bool askAlways, string question)
    {
        if (!delayed && !askAlways) return true;
        var text = delayed ? question + "\n\nPrices are DELAYED (delayed app key): the real market may have moved. Place this bet anyway?" : question;
        return MessageBox.Show(text, delayed ? "Delayed prices" : "Confirm", MessageBoxButtons.YesNo, delayed ? MessageBoxIcon.Warning : MessageBoxIcon.Question) == DialogResult.Yes;
    }

    async Task Place(string side, RunnerCatalog r, double price)
    {
        var size = StakeSize(side, price);
        if (!Confirm(_book?.IsMarketDataDelayed == true, Opt.ConfirmBets, $"{side} {r.RunnerName} @ {Cl.Price(price)} for {size:0.00}?")) return;
        await Send(side, r, price, size);
    }

    async Task Send(string side, RunnerCatalog r, double price, double size)
    {
        try
        {
            var rep = await _host.Bf.PlaceOrderAsync(_market.MarketId, new PlaceInstruction(r.SelectionId, side, price, size, Opt.Persistence));
            SetMsg($"{side} {r.RunnerName} {Cl.Price(price)} x{size:0.00}: {rep.Status}{(rep.ErrorCode == null ? "" : " " + rep.ErrorCode)}");
        }
        catch (BetfairException ex) { SetMsg($"{side} failed: {ex.Code}" + (ex.Code is "INVALID_APP_KEY" or "PERMISSION_DENIED" ? " (delayed keys cannot bet)" : "")); }
        await Poll();
    }

    async Task Cancel(CurrentOrder o)
    {
        try { await _host.Bf.CancelOrderAsync(_market.MarketId, o.BetId); SetMsg($"Cancelled {o.Side} {Cl.Price(o.Price)}"); }
        catch (BetfairException ex) { SetMsg("Cancel failed: " + ex.Code); }
        await Poll();
    }

    async Task CancelAll()
    {
        foreach (var o in _orders.Where(o => o.Status == "EXECUTABLE").ToList())
            try { await _host.Bf.CancelOrderAsync(_market.MarketId, o.BetId); } catch (BetfairException ex) { SetMsg("Cancel failed: " + ex.Code); return; }
        SetMsg("Cancelled all open bets");
        await Poll();
    }

    async Task GreenUp(RunnerCatalog r, bool quiet = false)
    {
        var run = _book?.Runners?.FirstOrDefault(x => x.SelectionId == r.SelectionId);
        var pos = Position.Of(_orders, r.SelectionId);
        if (run?.Ex == null || (pos.IfWins == 0 && pos.IfLoses == 0)) { if (!quiet) SetMsg("Nothing to green up"); return; }
        bool wantBack = pos.IfLoses > pos.IfWins;
        double price = wantBack ? run.Ex.AvailableToBack?.FirstOrDefault()?.Price ?? 0 : run.Ex.AvailableToLay?.FirstOrDefault()?.Price ?? 0;
        if (price <= 0) { if (!quiet) SetMsg("No price to green up at"); return; }
        var g = pos.GreenUp(price);
        if (g == null) { if (!quiet) SetMsg("Already level"); return; }
        if (MessageBox.Show($"{g.Value.Side} {r.RunnerName} @ {Cl.Price(price)} for {g.Value.Size:0.00} to lock {pos.LockedProfit(price):+0.00;-0.00}?", "Green up", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        await Send(g.Value.Side, r, price, g.Value.Size);
    }
}
