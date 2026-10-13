using System.Globalization;
using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>The minimized card and the goal/suspension automation of the Classic ladder window.</summary>
sealed partial class ClassicLadderForm
{
    // ---------------------------------------------------------------- automation state
    // test seam only (not a feature): MT_TEST_NO_SEND=1 makes the automation log instead of calling Betfair
    static readonly bool TestNoSend = Environment.GetEnvironmentVariable("MT_TEST_NO_SEND") == "1";
    bool _armed, _pendingReopen;
    DateTime _lastFire = DateTime.MinValue;
    int _autoBets, _autoFailures;
    string? _prevStatus;
    readonly List<string> _autoLog = new();
    readonly Button _armBtn = new() { Height = 30, Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat, Font = Cl.UiBold };
    readonly ComboBox _autoSide = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 62, Font = Cl.Ui };
    readonly TextBox _autoAmount = new() { Width = 56, TextAlign = HorizontalAlignment.Right, Font = Cl.Name };
    readonly CheckBox _autoLiab = new() { Text = "amount is liability", AutoSize = true, Font = Cl.Ui };
    readonly CheckBox _autoGoal = new() { Text = "Goal", AutoSize = true, Font = Cl.Ui };
    readonly CheckBox _autoSusp = new() { Text = "Suspension (placed when the market reopens)", AutoSize = true, Font = Cl.Ui };
    readonly ComboBox _autoBasis = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Font = Cl.Ui };
    readonly NumericUpDown _autoOffset = new() { Minimum = -20, Maximum = 20, Width = 50, Font = Cl.Ui };
    readonly ToolTip _tip = new() { AutoPopDelay = 30000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };
    static readonly string NL = Environment.NewLine;
    readonly NumericUpDown _autoCool = new() { Minimum = 10, Maximum = 600, Width = 58, Font = Cl.Ui };
    readonly CheckBox _autoDelayed = new() { Text = "Allow with delayed prices", AutoSize = true, Font = Cl.Ui };
    readonly CheckedListBox _autoSel = new() { Dock = DockStyle.Fill, Font = Cl.Ui, CheckOnClick = true, BorderStyle = BorderStyle.None };
    readonly Label _autoLogLbl = new() { Dock = DockStyle.Bottom, Height = 62, Font = Cl.Ui, ForeColor = Color.White, Padding = new Padding(4, 2, 4, 2) };

    // ---------------------------------------------------------------- card state
    ClassicCard? _card;
    readonly Dictionary<long, (double Ltp, string Trend)> _trend = new();
    static int _cardSlots;
    Point? _cardLocation;

    // ================================================================ automation
    ClassicSection BuildAutomation(List<RunnerCatalog> runners)
    {
        int selH = runners.Count * 16 + 6;
        var sec = new ClassicSection("Automation", 380 + selH);
        Panel Row(int h) => new() { Dock = DockStyle.Top, Height = h, BackColor = Cl.ToolBg };
        static Label Lbl(string t, int x, int y) => new() { Text = t, Left = x, Top = y, AutoSize = true, Font = Cl.Ui, ForeColor = Color.White };

        _autoSide.Items.AddRange(new object[] { "Lay", "Back" });
        _autoSide.SelectedItem = Opt.AutoSide == "BACK" ? "Back" : "Lay";
        _autoAmount.Text = Opt.AutoAmount.ToString("0.##", CultureInfo.InvariantCulture);
        _autoLiab.Checked = Opt.AutoLiability; _autoGoal.Checked = Opt.AutoOnGoal; _autoSusp.Checked = Opt.AutoOnSuspend;
        _autoBasis.Items.AddRange(new object[] { "Last traded price", "Best available" });
        _autoBasis.SelectedIndex = Opt.AutoBasis == "BEST" ? 1 : 0;
        _autoOffset.Value = Math.Clamp(Opt.AutoOffsetTicks, -20, 20); _autoCool.Value = Math.Clamp(Opt.AutoCooldownSeconds, 10, 600);
        _autoDelayed.Checked = Opt.AutoAllowDelayed;
        foreach (var r in runners) _autoSel.Items.Add(r.RunnerName, true);

        void Save() { ReadAutoSettings(); UpdateArmButton(); }
        _autoSide.SelectedIndexChanged += (_, _) => Save();
        _autoAmount.Leave += (_, _) => Save();
        foreach (var c in new CheckBox[] { _autoLiab, _autoGoal, _autoSusp, _autoDelayed }) c.CheckedChanged += (_, _) => Save();
        _autoBasis.SelectedIndexChanged += (_, _) => Save(); _autoOffset.ValueChanged += (_, _) => Save(); _autoCool.ValueChanged += (_, _) => Save();
        _armBtn.Click += (_, _) => { if (_armed) Disarm("disarmed"); else Arm(); };

        var logRow = new Panel { Dock = DockStyle.Fill, BackColor = Cl.ToolBg };
        var selWrap = new Panel { Dock = DockStyle.Top, Height = selH, BackColor = Color.White }; selWrap.Controls.Add(_autoSel);

        var r1 = Row(28); r1.Controls.AddRange(new Control[] { Lbl("Bet", 6, 8), _autoSide, _autoAmount }); _autoSide.Left = 36; _autoSide.Top = 4; _autoAmount.Left = 104; _autoAmount.Top = 4;
        r1.Controls.Add(Lbl("on every selection", 168, 8));
        var r1b = Row(24); _autoLiab.Left = 36; _autoLiab.Top = 2; r1b.Controls.Add(_autoLiab);
        var r2 = Row(52); r2.Controls.Add(Lbl("When", 6, 6)); _autoGoal.Left = 56; _autoGoal.Top = 2; _autoSusp.Left = 56; _autoSusp.Top = 26; r2.Controls.AddRange(new Control[] { _autoGoal, _autoSusp });
        var r3 = Row(54);
        r3.Controls.Add(Lbl("Price", 6, 8)); _autoBasis.Left = 40; _autoBasis.Top = 4; r3.Controls.Add(_autoBasis);
        _autoOffset.Left = 176; _autoOffset.Top = 4; r3.Controls.Add(_autoOffset); r3.Controls.Add(Lbl("ticks", 230, 8));
        r3.Controls.Add(Lbl("Cooldown (s)", 6, 32)); _autoCool.Left = 84; _autoCool.Top = 28; r3.Controls.Add(_autoCool);
        var r4 = Row(26); _autoDelayed.Left = 6; _autoDelayed.Top = 4; r4.Controls.Add(_autoDelayed);
        var r5 = Row(18); r5.Controls.Add(Lbl("Selections", 6, 2));

        // docked Top controls stack bottom-up in add order
        logRow.Controls.Add(_autoLogLbl);
        sec.Body.Controls.Add(logRow);
        sec.Body.Controls.Add(selWrap); sec.Body.Controls.Add(r5);
        sec.Body.Controls.Add(r4); sec.Body.Controls.Add(r3); sec.Body.Controls.Add(r2); sec.Body.Controls.Add(r1b); sec.Body.Controls.Add(r1);
        sec.Body.Controls.Add(_armBtn);
        sec.BodyHeight = 30 + 28 + 24 + 52 + 54 + 26 + 18 + selH + 66 + 4;
        AddAutoTips();
        UpdateArmButton();
        return sec;
    }


    /// <summary>Hover explanations for every automation control.</summary>
    void AddAutoTips()
    {
        void T(Control c, params string[] lines) => _tip.SetToolTip(c, string.Join(NL, lines));
        T(_armBtn, "Automation is either OFF or ON. It always starts OFF.", "Turning it ON asks you to confirm and shows the worst case you could lose.", "It only works while this window (or its card) is open.");
        T(_autoSide, "Back or Lay. The same bet is placed on every ticked selection below.");
        T(_autoAmount, "How much to bet on each selection, in your account currency.", "This is the stake, unless 'amount is liability' is ticked.");
        T(_autoLiab, "Lay bets only. Tick it and the amount is the most you can LOSE on each selection;", "the stake is worked out as liability / (price - 1).");
        T(_autoGoal, "Fire when the live-score relay reports a goal (total goals went up).", "A goal taken away by VAR never fires it. Needs a score relay set in Options.");
        T(_autoSusp, "Fire when Betfair suspends this market (it suspends on goals, red cards, penalties).", "Betfair refuses bets while a market is suspended, so the bets go in the moment it reopens.");
        T(_autoBasis, "Where the price starts from.",
          "Last traded price: the price of the most recent matched bet. Right after a goal this is still", "the old, pre-goal price until trading catches up.",
          "Best available: the best price waiting to be taken right now (for a lay, the best lay price;", "for a back, the best back price).");
        T(_autoOffset, "Moves the price along the Betfair price ladder from where it starts, one step (tick) at a time.",
          "+ = higher odds, - = lower odds. Example: 2.00 with +2 ticks = 2.04.",
          "A LAY is more likely to match at higher odds; a BACK is more likely to match at lower odds.", "0 = use the starting price exactly.");
        T(_autoCool, "Minimum seconds between two firings, so one event (say a goal followed by a suspension)", "cannot place a second round of bets.");
        T(_autoDelayed, "With a delayed app key the prices on screen are old, so automated bets could be placed at the wrong price.", "Leave this off and the automation refuses to place bets while prices are delayed.");
        T(_autoSel, "Tick the selections that get a bet.");
        T(_autoLogLbl, "The last things the automation did: turning on or off, triggers, and each bet it placed or why it skipped.");
    }

    static bool TryNum(string s, out double v) => double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    void ReadAutoSettings()
    {
        Opt.AutoSide = _autoSide.SelectedItem?.ToString() == "Back" ? "BACK" : "LAY";
        if (TryNum(_autoAmount.Text, out var a) && a >= 0.01) Opt.AutoAmount = a;
        _autoAmount.Text = Opt.AutoAmount.ToString("0.##", CultureInfo.InvariantCulture);
        Opt.AutoLiability = _autoLiab.Checked; Opt.AutoOnGoal = _autoGoal.Checked; Opt.AutoOnSuspend = _autoSusp.Checked;
        Opt.AutoBasis = _autoBasis.SelectedIndex == 1 ? "BEST" : "LAST"; Opt.AutoOffsetTicks = (int)_autoOffset.Value; Opt.AutoCooldownSeconds = (int)_autoCool.Value;
        Opt.AutoAllowDelayed = _autoDelayed.Checked;
        _autoLiab.Enabled = Opt.AutoSide == "LAY";
        Opt.Save();
    }

    AutoBetSettings AutoSettings() => new(Opt.AutoSide, Opt.AutoAmount, Opt.AutoLiability && Opt.AutoSide == "LAY", Opt.AutoBasis, Opt.AutoOffsetTicks);

    IEnumerable<long> AutoSelections() =>
        _ladders.Where((l, i) => _autoSel.GetItemChecked(i)).Select(l => l.Info.SelectionId);

    void UpdateArmButton()
    {
        _armBtn.Text = _armed ? "Automation ON - click to turn off" : "Automation OFF - click to turn on";
        _armBtn.BackColor = _armed ? Color.Firebrick : Cl.Light;
        _armBtn.ForeColor = _armed ? Color.White : Color.Black;
        _card?.Invalidate();
    }

    void Log(string line)
    {
        _autoLog.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        while (_autoLog.Count > 3) _autoLog.RemoveAt(0);
        _autoLogLbl.Text = string.Join(Environment.NewLine, _autoLog);
        SetMsg(line);
    }

    void Arm()
    {
        ReadAutoSettings();
        if (_book == null) { Log("No prices yet: try again in a moment"); return; }
        if (!Opt.AutoOnGoal && !Opt.AutoOnSuspend) { Log("Tick Goal and/or Suspension first"); return; }
        var plan = AutoBet.Plan(_book, AutoSelections(), AutoSettings());
        if (plan.Count == 0) { Log("Nothing to place: no selection has a price to take"); return; }
        var triggers = string.Join(" or ", new[] { Opt.AutoOnGoal ? "a goal" : null, Opt.AutoOnSuspend ? "a market suspension" : null }.Where(x => x != null));
        var priceHow = (Opt.AutoBasis == "BEST" ? "the best available price" : "the last traded price") + (Opt.AutoOffsetTicks == 0 ? "" : $" {Opt.AutoOffsetTicks:+0;-0} tick(s)");
        var text = $"When {triggers} happens, place {Opt.AutoSide} bets on {plan.Count} selection(s), priced from {priceHow}.\n\n" +
                   string.Join("\n", plan.Select(p => $"  {p.Side} {NameOf(p.SelectionId)} @ {Cl.Price(p.Price)}  size {p.Size:0.00}")) +
                   $"\n\nMost you could lose if every bet goes wrong: {Cl.Symbol(_host.CurrencyCode)}{AutoBet.WorstCase(plan):0.00} per firing." +
                   $"\nCooldown {Opt.AutoCooldownSeconds}s, stops after {Opt.AutoMaxBets} bets.\n\n" +
                   "Real bets will be placed on your account." +
                   (_book.IsMarketDataDelayed ? "\n\nPrices are DELAYED: the bets would be placed at old prices." + (Opt.AutoAllowDelayed ? "" : " (They will be skipped unless you tick 'Allow with delayed prices'.)") : "");
        if (MessageBox.Show(text, "Turn automation on?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _armed = true; _pendingReopen = false; _autoBets = 0; _autoFailures = 0; _lastFire = DateTime.MinValue;
        UpdateArmButton(); Log("Automation ON");
    }

    void Disarm(string why)
    {
        bool was = _armed;
        _armed = false; _pendingReopen = false;
        UpdateArmButton();
        if (was) Log("Automation OFF: " + why);
    }

    string NameOf(long selectionId) => _ladders.FirstOrDefault(l => l.Info.SelectionId == selectionId).Info?.RunnerName ?? selectionId.ToString();

    /// <summary>Called on every poll: watches for the market suspending and reopening.</summary>
    void AutoWatch()
    {
        if (_book == null) return;
        var status = _book.Status;
        if (_armed && Opt.AutoOnSuspend && status == "SUSPENDED" && _prevStatus != "SUSPENDED")
        { _pendingReopen = true; Log("Market suspended: bets go in when it reopens"); }
        if (_armed && _pendingReopen && status == "OPEN") { _pendingReopen = false; _ = FireAuto("market reopened"); }
        _prevStatus = status;
    }

    /// <summary>A score went up: that is a goal (a score going down is a goal taken away and is ignored).</summary>
    void AutoGoal(LiveMatch m, int prevHome, int prevAway)
    {
        if (!_armed || !Opt.AutoOnGoal) return;
        if (m.HomeScore + m.AwayScore <= prevHome + prevAway) return;
        _ = FireAuto($"goal {prevHome}-{prevAway} -> {m.ScoreText}");
    }

    async Task FireAuto(string reason)
    {
        if (!_armed) return;
        if ((DateTime.UtcNow - _lastFire).TotalSeconds < Opt.AutoCooldownSeconds) { Log($"{reason}: skipped (cooldown)"); return; }
        if (_book == null) return;
        if (_book.Status != "OPEN") { _pendingReopen = true; Log($"{reason}: market is {_book.Status}, will place when it reopens"); return; }
        if (_book.IsMarketDataDelayed && !Opt.AutoAllowDelayed) { Log($"{reason}: skipped, prices are delayed"); return; }
        var plan = AutoBet.Plan(_book, AutoSelections(), AutoSettings());
        if (plan.Count == 0) { Log($"{reason}: nothing to place (no prices)"); return; }
        if (_autoBets + plan.Count > Opt.AutoMaxBets) { Disarm($"bet limit ({Opt.AutoMaxBets}) reached"); return; }
        _lastFire = DateTime.UtcNow;
        Log($"{reason}: placing {plan.Count} x {Opt.AutoSide}");
        foreach (var b in plan)
        {
            _autoBets++;
            if (TestNoSend) { Log($"[test] would {b.Side} {NameOf(b.SelectionId)} @ {Cl.Price(b.Price)} size {b.Size:0.00}"); continue; }
            try
            {
                var rep = await _host.Bf.PlaceOrderAsync(_market.MarketId, new PlaceInstruction(b.SelectionId, b.Side, b.Price, b.Size, Opt.Persistence));
                Log($"{b.Side} {NameOf(b.SelectionId)} @ {Cl.Price(b.Price)} x{b.Size:0.00}: {rep.Status}{(rep.ErrorCode == null ? "" : " " + rep.ErrorCode)}");
                _autoFailures = rep.Status == "SUCCESS" ? 0 : _autoFailures + 1;
            }
            catch (BetfairException ex) { _autoFailures++; Log($"{b.Side} {NameOf(b.SelectionId)} failed: {ex.Code}"); }
            if (_autoFailures >= 3) { Disarm("3 bets in a row failed"); break; }
        }
        await Poll();
    }

    // ================================================================ card
    void ShowCard()
    {
        if (_card == null)
        {
            _card = new ClassicCard(Text, _ladders.Count, RestoreFromCard, () => Close());
            var wa = Screen.FromControl(this).WorkingArea;
            int slot = _cardSlots++;
            _card.Location = _cardLocation ?? new Point(wa.Left + 20 + (slot % 3) * 480, wa.Bottom - _card.Height - 20 - (slot / 3) * 40);
        }
        _card.SetData(BuildCardData());
        Hide();
        _card.Show();
    }

    void RestoreFromCard()
    {
        if (_card != null) { _cardLocation = _card.Location; _card.Hide(); }
        Show(); WindowState = FormWindowState.Normal; Activate();
    }

    /// <summary>Open straight into the card (used by the navigator's "Open card only").</summary>
    public void StartAsCard() { Opacity = 0; Show(); ShowCard(); Opacity = 1; }

    /// <summary>Bring this market to the front, whether it is a window or a card.</summary>
    public void Raise()
    {
        if (!Visible) { RestoreFromCard(); return; }
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); BringToFront();
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_SYSCOMMAND = 0x112, SC_MINIMIZE = 0xF020;
        if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt32() & 0xFFF0) == SC_MINIMIZE) { ShowCard(); return; }   // minimize = turn into the card
        base.WndProc(ref m);
    }

    CardData BuildCardData()
    {
        var sym = Cl.Symbol(_host.CurrencyCode);
        var live = _host.Scores.Url == null ? null : _host.Scores.Find(_market.Event?.Name ?? "");
        var rows = new List<CardRunner>();
        foreach (var (info, _) in _ladders)
        {
            var r = _book?.Runners?.FirstOrDefault(x => x.SelectionId == info.SelectionId);
            double? ltp = r?.LastPriceTraded; string trend = ">>";
            if (ltp != null)
            {
                if (_trend.TryGetValue(info.SelectionId, out var t)) { trend = t.Trend; if (Math.Abs(t.Ltp - ltp.Value) > 1e-9) { trend = ltp > t.Ltp ? "▲" : "▼"; _trend[info.SelectionId] = (ltp.Value, trend); } }
                else _trend[info.SelectionId] = (ltp.Value, trend);
            }
            var backs = r?.Ex?.AvailableToBack?.Take(3).ToList() ?? new(); var lays = r?.Ex?.AvailableToLay?.Take(3).ToList() ?? new();
            int gap = 0;
            if (backs.Count > 0 && lays.Count > 0) { double p = backs[0].Price; while (p < lays[0].Price - 1e-9 && gap < 60) { p = PriceLadder.Up(p); gap++; } gap = Math.Max(0, gap - 1); }
            rows.Add(new CardRunner(info.RunnerName, ltp, trend, lays.FirstOrDefault()?.Price, r?.TotalMatched ?? (r?.Ex?.TradedVolume ?? new()).Sum(p => p.Size),
                r == null ? new Position(0, 0) : Position.Of(_orders, r.SelectionId), backs, lays, gap));
        }
        var start = _market.MarketStartTime?.ToLocalTime();
        return new CardData((_armed ? "● AUTO  " : "") + Title(_book).Split("   ")[0], start?.ToString("dd/MM/yyyy  HH:mm") ?? "", _clock.Text, _book?.InPlay == true,
            _book?.TotalMatched is double tm && tm > 0 ? sym + Liquidity.Spaced(tm) : "", sym,
            _book == null ? 0 : Liquidity.BookPct(_book, true), _book == null ? 0 : Liquidity.BookPct(_book, false), _ladders.Count,
            live != null ? $"{live.HomeScore} - {live.AwayScore}" : null, DateTime.UtcNow < _flashUntil, _armed, _book?.Status ?? "", rows);
    }
}
