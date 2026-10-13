using System.Globalization;
using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>
/// Settings for the software (right-click the balance bar): general, betting, scores and favorites, web, account, and the
/// automation rules (a whole tab). Edits are kept until OK; the rules master switch is the one thing that acts straight away.
/// </summary>
sealed class SettingsForm : Form
{
    readonly AppHost _host;
    readonly Options _o;
    readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Font = Cl.Ui };
    readonly ToolTip _tip = new() { AutoPopDelay = 30000, InitialDelay = 300, ReshowDelay = 100, ShowAlways = true };
    static readonly string NL = Environment.NewLine;

    // general / betting
    readonly NumericUpDown _refresh = new() { Minimum = 500, Maximum = 10000, Increment = 250, Width = 80 };
    readonly CheckBox _onTop = new() { Text = "Keep ladder windows on top", AutoSize = true };
    readonly CheckBox _centre = new() { Text = "Centre each ladder on its last traded price", AutoSize = true };
    readonly TextBox _stakes = new() { Width = 220 };
    readonly NumericUpDown _defaultStake = new() { DecimalPlaces = 2, Minimum = 1, Maximum = 100000, Width = 80 };
    readonly NumericUpDown _backStake = new() { DecimalPlaces = 2, Minimum = 1, Maximum = 100000, Width = 80 };
    readonly NumericUpDown _layAmount = new() { DecimalPlaces = 2, Minimum = 1, Maximum = 100000, Width = 80 };
    readonly CheckBox _layLiab = new() { Text = "lay amount is liability", AutoSize = true };
    readonly ComboBox _persist = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    readonly CheckBox _confirm = new() { Text = "Ask before every bet", AutoSize = true };
    // scores and favorites / web
    readonly TextBox _relay = new() { Width = 300 };
    readonly CheckBox _autoOpen = new() { Text = "Open a ladder when a favorite's score changes", AutoSize = true };
    readonly ListBox _favComps = new() { Width = 300, Height = 150 }, _favMatches = new() { Width = 300, Height = 150 };
    readonly CheckBox _mirror = new() { Text = "Mirror to the web (http://localhost:8080)", AutoSize = true };
    // rules
    readonly List<AutomationRule> _rules;
    readonly CheckedListBox _ruleList = new() { Width = 230, Height = 250, CheckOnClick = true, Font = Cl.Ui };
    readonly TextBox _rName = new() { Width = 300 }, _rMarket = new() { Width = 300 }, _rSelections = new() { Width = 300 };
    readonly ComboBox _rScope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    readonly ComboBox _rSide = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 62 };
    readonly TextBox _rAmount = new() { Width = 56, TextAlign = HorizontalAlignment.Right };
    readonly CheckBox _rLiab = new() { Text = "liability", AutoSize = true };
    readonly ComboBox _rBasis = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    readonly NumericUpDown _rOffset = new() { Minimum = -20, Maximum = 20, Width = 52 };
    readonly NumericUpDown _rCool = new() { Minimum = 5, Maximum = 600, Width = 58 };
    readonly NumericUpDown _rMax = new() { Minimum = 1, Maximum = 20, Width = 50 };
    readonly Label _rDesc = new() { AutoSize = false, Width = 460, Height = 34, ForeColor = Color.FromArgb(0x20, 0x40, 0x60) };
    readonly CheckBox _rulesDelayed = new() { Text = "Allow with delayed prices", AutoSize = true };
    readonly NumericUpDown _rulesMax = new() { Minimum = 1, Maximum = 500, Width = 56 };
    readonly Button _master = new() { Height = 34, Width = 300, FlatStyle = FlatStyle.Flat, Font = Cl.UiBold };
    readonly ListBox _log = new() { Width = 700, Height = 92, Font = new Font("Tahoma", 8f) };
    bool _loading;

    public SettingsForm(AppHost host, string startTab = "General")
    {
        _host = host; _o = host.Opt;
        _rules = _o.Rules.Select(r => r.Copy()).ToList();
        Text = "Settings"; Font = Cl.Ui; BackColor = Cl.Light;
        ClientSize = new Size(800, 640); MinimumSize = new Size(800, 640);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        Theme.TitleBar(this, Cl.BarTop, Color.White);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 6), BackColor = Cl.Light };
        var ok = new Button { Text = "OK", Width = 88, Height = 28, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", Width = 88, Height = 28, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        Controls.Add(_tabs); Controls.Add(buttons);
        AcceptButton = ok; CancelButton = cancel;
        ok.Click += (_, _) => { if (Apply()) { DialogResult = DialogResult.OK; Close(); } };

        BuildGeneral(); BuildBetting(); BuildScores(); BuildWeb(); BuildAccount(); BuildRules();
        LoadValues();
        _tabs.SelectedTab = _tabs.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Text == startTab) ?? _tabs.TabPages[0];
        _host.Rules.Changed += OnRulesChanged;
        FormClosed += (_, _) => _host.Rules.Changed -= OnRulesChanged;
    }

    // ---- small layout helpers ------------------------------------------------------------------------------
    TabPage Page(string title) { var p = new TabPage(title) { BackColor = Cl.Light, Padding = new Padding(12), AutoScroll = true }; _tabs.TabPages.Add(p); return p; }

    int Row(TabPage p, int y, string label, Control c, string tip, int x = 220)
    {
        if (label != "") p.Controls.Add(new Label { Text = label, Left = 14, Top = y + 4, AutoSize = true });
        c.Left = x; c.Top = y; p.Controls.Add(c);
        _tip.SetToolTip(c, tip);
        return y + Math.Max(28, c.Height + 6);
    }

    void Tip(Control c, params string[] lines) => _tip.SetToolTip(c, string.Join(NL, lines));
    static bool Num(string s, out double v) => double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    // ---- tabs ----------------------------------------------------------------------------------------------
    void BuildGeneral()
    {
        var p = Page("General"); int y = 14;
        y = Row(p, y, "Refresh every (ms)", _refresh, "How often the ladders ask Betfair for fresh prices. Lower = faster but more requests (500 to 10000).");
        y = Row(p, y, "", _onTop, "Ladder windows stay above every other window.");
        y = Row(p, y, "", _centre, "Each ladder keeps scrolling so the last traded price stays in the middle. Turn off to scroll yourself with the mouse wheel.");
    }

    void BuildBetting()
    {
        var p = Page("Betting"); int y = 14;
        y = Row(p, y, "Preset stakes", _stakes, "The stakes offered by the S buttons, separated by commas. Minimum 1.");
        y = Row(p, y, "Default stake", _defaultStake, "What the reset button on the staking rows goes back to.");
        y = Row(p, y, "Back stake", _backStake, "The amount a click in the blue column bets.");
        y = Row(p, y, "Lay amount", _layAmount, "The amount a click in the pink column bets. See the box below for what it means.");
        y = Row(p, y, "", _layLiab, "Tick: the lay amount is your liability (the most you can lose) and the stake is worked out from the price. Untick: it is the stake.");
        y = Row(p, y, "Unmatched bets", _persist, "What happens to a bet that is not matched when the game goes in-play: cancelled (lapse), kept (persist) or matched at the starting price.");
        y = Row(p, y, "", _confirm, "Ask before every bet. With a delayed app key you are asked every time regardless, because the prices on screen are old.");
        _persist.Items.AddRange(new object[] { "Cancel at in-play (Lapse)", "Keep in-play (Persist)", "Take SP (Market on close)" });
    }

    void BuildScores()
    {
        var p = Page("Scores & favorites"); int y = 14;
        y = Row(p, y, "Live-score relay (ws://...)", _relay, "Address of the WebSocket relay that reports scores and goals. Empty = no live scores. Applied when you press OK.");
        y = Row(p, y, "", _autoOpen, "When the score of a starred match, or of any match in a starred competition, changes, its ladder opens (or comes to the front).");
        p.Controls.Add(new Label { Text = "Favorite competitions", Left = 14, Top = y + 8, AutoSize = true, Font = Cl.UiBold });
        p.Controls.Add(new Label { Text = "Favorite matches", Left = 400, Top = y + 8, AutoSize = true, Font = Cl.UiBold });
        _favComps.Left = 14; _favComps.Top = y + 28; _favMatches.Left = 400; _favMatches.Top = y + 28;
        p.Controls.Add(_favComps); p.Controls.Add(_favMatches);
        var rc = new Button { Text = "Remove selected", Left = 14, Top = y + 184, Width = 130 }; var rm = new Button { Text = "Remove selected", Left = 400, Top = y + 184, Width = 130 };
        rc.Click += (_, _) => { if (_favComps.SelectedIndex >= 0) _favComps.Items.RemoveAt(_favComps.SelectedIndex); };
        rm.Click += (_, _) => { if (_favMatches.SelectedIndex >= 0) _favMatches.Items.RemoveAt(_favMatches.SelectedIndex); };
        p.Controls.Add(rc); p.Controls.Add(rm);
        Tip(_favComps, "Competitions you starred. Their matches count as favorites. Remove one here, then press OK.");
        Tip(_favMatches, "Matches you starred. Remove one here, then press OK.");
    }

    void BuildWeb()
    {
        var p = Page("Web"); int y = 14;
        y = Row(p, y, "", _mirror, "Serve the web page from this app, using its Betfair login, so a browser shows the same markets. Takes effect the next time the app starts.");
        p.Controls.Add(new Label { Left = 14, Top = y + 4, AutoSize = true, Text = "The page only listens on this computer and follows the market you select here." });
    }

    void BuildAccount()
    {
        var p = Page("Account"); int y = 14;
        p.Controls.Add(new Label { Left = 14, Top = y + 4, AutoSize = true, Text = $"Account currency: {_host.CurrencyCode}" }); y += 34;
        var forget = new Button { Text = "Forget saved login", Width = 200, Height = 28 };
        forget.Click += (_, _) => { LoginStore.Forget(); MessageBox.Show("The saved password is removed. You will be asked to log in next time.", "Settings"); };
        y = Row(p, y, "", forget, "Removes the password this app saved (it is stored encrypted for your Windows user). Your username and app key stay.");
        var folder = new Button { Text = "Open settings folder", Width = 200, Height = 28 };
        folder.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Profile.Dir) { UseShellExecute = true });
        Row(p, y, "", folder, "Opens the folder where this app keeps its settings files.");
    }

    // ---- automation rules ------------------------------------------------------------------------------------
    void BuildRules()
    {
        var p = Page("Automation rules");
        p.Controls.Add(new Label { Text = "Rules (tick = enabled)", Left = 14, Top = 8, AutoSize = true, Font = Cl.UiBold });
        _ruleList.Left = 14; _ruleList.Top = 28; p.Controls.Add(_ruleList);
        var add = new Button { Text = "Add", Left = 14, Top = 284, Width = 70 }; var dup = new Button { Text = "Duplicate", Left = 88, Top = 284, Width = 74 }; var del = new Button { Text = "Delete", Left = 166, Top = 284, Width = 78 };
        p.Controls.AddRange(new Control[] { add, dup, del });
        Tip(_ruleList, "Your rules. A ticked rule is enabled. Rules only act while the master switch below is ON.");
        add.Click += (_, _) => { _rules.Add(new AutomationRule()); RefreshList(_rules.Count - 1); };
        dup.Click += (_, _) => { if (Sel() is { } r) { _rules.Add(r.Clone()); RefreshList(_rules.Count - 1); } };
        del.Click += (_, _) => { if (Sel() is { } r) { _rules.Remove(r); RefreshList(Math.Min(_ruleList.SelectedIndex, _rules.Count - 1)); } };
        _ruleList.SelectedIndexChanged += (_, _) => LoadEditor();
        _ruleList.ItemCheck += (_, e) => { if (!_loading && e.Index >= 0 && e.Index < _rules.Count) _rules[e.Index].Enabled = e.NewValue == CheckState.Checked; };

        int x = 262, y = 8;
        void L(string t, int yy) => p.Controls.Add(new Label { Text = t, Left = x, Top = yy + 4, AutoSize = true });
        void At(Control c, int xx, int yy, params string[] tip) { c.Left = xx; c.Top = yy; p.Controls.Add(c); if (tip.Length > 0) Tip(c, tip); }
        L("Name", y); At(_rName, x + 130, y, "A name for this rule, shown in the list and in the log."); y += 28;
        L("Market name contains", y); At(_rMarket, x + 130, y, "The rule bets in markets of the match whose name contains this text, e.g. Correct Score, Match Odds, Over/Under 2.5.", "At most 3 matching markets are used."); y += 28;
        L("Matches", y); At(_rScope, x + 130, y, "Which matches the rule watches.", "Favorites: the matches and competitions you starred (safest).", "Open ladders: matches whose ladder window is open.", "Any match: every match the score relay reports (careful)."); y += 28;
        L("Bet", y); At(_rSide, x + 130, y, "Back or Lay."); At(_rAmount, x + 198, y, "Amount on each selection: the stake, or your liability if 'liability' is ticked (lay only)."); At(_rLiab, x + 262, y + 2, "Lay only: the amount is the most you can lose on each selection."); y += 28;
        L("On selections", y); At(_rSelections, x + 130, y,
            "Which selections get the bet, separated by ;  (or * for every selection).",
            "Tokens follow the game:",
            "{score} = the score now, e.g. 1 - 0.   {before} = the score before the goal.",
            "{home+1} = the score if the home team scores again.   {away+1} = if the away team does.",
            "Example for Correct Score: {score}  lays the score that just happened.", "Names are matched ignoring case and spacing (1-0 = 1 - 0)."); y += 28;
        var tokens = new FlowLayoutPanel { Left = x + 130, Top = y, Width = 470, Height = 28, WrapContents = false };
        foreach (var tok in new[] { "{score}", "{before}", "{home+1}", "{away+1}", "*" })
        {
            var b = new Button { Text = tok, AutoSize = true, Height = 22, Margin = new Padding(0, 0, 4, 0) };
            b.Click += (_, _) => { _rSelections.Text = _rSelections.Text.Trim() == "" ? tok : _rSelections.Text.TrimEnd().TrimEnd(';') + "; " + tok; };
            tokens.Controls.Add(b); Tip(b, "Add " + tok + " to the selections list");
        }
        p.Controls.Add(tokens); L("Insert", y); y += 30;
        L("Price", y); At(_rBasis, x + 130, y,
            "Where the price starts from.", "Last traded price: the most recent matched price (right after a goal this is still the old price).", "Best available: the best price waiting to be taken now.");
        At(_rOffset, x + 268, y, "Ticks to move the price along the Betfair ladder: + higher odds, - lower odds.", "A lay is likelier to match at higher odds, a back at lower odds. 0 = exactly the starting price.");
        p.Controls.Add(new Label { Text = "ticks", Left = x + 324, Top = y + 4, AutoSize = true }); y += 28;
        L("Cooldown (s)", y); At(_rCool, x + 130, y, "Minimum seconds between two firings of this rule in the same match."); _rMax.Left = x + 330; _rMax.Top = y; p.Controls.Add(_rMax); Tip(_rMax, "This rule places at most this many bets in one match."); p.Controls.Add(new Label { Text = "Max bets per match", Left = x + 214, Top = y + 4, AutoSize = true }); y += 36;
        _rDesc.Left = x; _rDesc.Top = y; p.Controls.Add(_rDesc);
        foreach (var c in new Control[] { _rName, _rMarket, _rSelections, _rAmount }) c.TextChanged += (_, _) => ReadEditor();
        foreach (var c in new ComboBox[] { _rScope, _rSide, _rBasis }) c.SelectedIndexChanged += (_, _) => ReadEditor();
        _rLiab.CheckedChanged += (_, _) => ReadEditor();
        foreach (var c in new NumericUpDown[] { _rOffset, _rCool, _rMax }) c.ValueChanged += (_, _) => ReadEditor();
        _rScope.Items.AddRange(new object[] { "Favorite matches and competitions", "Matches with an open ladder", "Any match with a live score (careful)" });
        _rSide.Items.AddRange(new object[] { "Lay", "Back" });
        _rBasis.Items.AddRange(new object[] { "Last traded price", "Best available" });

        // master switch and global safety
        int by = 330;
        p.Controls.Add(new Label { Left = 14, Top = by, AutoSize = true, Font = Cl.UiBold, Text = "Master switch" });
        _master.Left = 14; _master.Top = by + 22; p.Controls.Add(_master);
        _master.Click += (_, _) => ToggleMaster();
        Tip(_master, "Rules do nothing until this is ON, and it is always OFF when the app starts.", "Turning it ON applies your rule edits and asks you to confirm.", "While ON, a goal in a watched match places real bets.");
        At(_rulesDelayed, 330, by + 28, "With a delayed app key the prices on screen are old, so rule bets could land at the wrong price.", "Off = rules refuse to place bets while prices are delayed.");
        p.Controls.Add(new Label { Text = "Stop after", Left = 520, Top = by + 32, AutoSize = true });
        At(_rulesMax, 582, by + 28, "Safety limit: after this many bets in one session the rules switch themselves OFF.");
        p.Controls.Add(new Label { Text = "bets", Left = 644, Top = by + 32, AutoSize = true });
        p.Controls.Add(new Label { Left = 14, Top = by + 68, AutoSize = true, Font = Cl.UiBold, Text = "Log" });
        _log.Left = 14; _log.Top = by + 86; p.Controls.Add(_log);
        Tip(_log, "What the rules did: goals seen, bets placed (or why a bet was skipped).");
    }

    AutomationRule? Sel() => _ruleList.SelectedIndex >= 0 && _ruleList.SelectedIndex < _rules.Count ? _rules[_ruleList.SelectedIndex] : null;

    void RefreshList(int select)
    {
        _loading = true;
        _ruleList.Items.Clear();
        for (int i = 0; i < _rules.Count; i++) _ruleList.Items.Add(_rules[i].Name, _rules[i].Enabled);
        if (_rules.Count > 0) _ruleList.SelectedIndex = Math.Clamp(select, 0, _rules.Count - 1);
        _loading = false; LoadEditor();
    }

    void LoadEditor()
    {
        _loading = true;
        var r = Sel();
        foreach (var c in new Control[] { _rName, _rMarket, _rScope, _rSide, _rAmount, _rLiab, _rSelections, _rBasis, _rOffset, _rCool, _rMax }) c.Enabled = r != null;
        if (r != null)
        {
            _rName.Text = r.Name; _rMarket.Text = r.MarketContains; _rSelections.Text = r.Selections;
            _rScope.SelectedIndex = r.Scope switch { "OPEN" => 1, "ANY" => 2, _ => 0 };
            _rSide.SelectedIndex = r.Side == "BACK" ? 1 : 0; _rAmount.Text = r.Amount.ToString("0.##", CultureInfo.InvariantCulture);
            _rLiab.Checked = r.AmountIsLiability; _rLiab.Enabled = r.Side == "LAY";
            _rBasis.SelectedIndex = r.Basis == "BEST" ? 1 : 0; _rOffset.Value = Math.Clamp(r.OffsetTicks, -20, 20);
            _rCool.Value = Math.Clamp(r.CooldownSeconds, 5, 600); _rMax.Value = Math.Clamp(r.MaxBetsPerMatch, 1, 20);
            _rDesc.Text = RuleLogic.Describe(r);
        }
        else _rDesc.Text = "No rule selected. Press Add to create one.";
        _loading = false;
    }

    void ReadEditor()
    {
        if (_loading || Sel() is not { } r) return;
        r.Name = _rName.Text; r.MarketContains = _rMarket.Text; r.Selections = _rSelections.Text;
        r.Scope = _rScope.SelectedIndex switch { 1 => "OPEN", 2 => "ANY", _ => "FAVORITES" };
        r.Side = _rSide.SelectedIndex == 1 ? "BACK" : "LAY";
        if (Num(_rAmount.Text, out var a) && a >= 0.01) r.Amount = a;
        r.AmountIsLiability = _rLiab.Checked; _rLiab.Enabled = r.Side == "LAY";
        r.Basis = _rBasis.SelectedIndex == 1 ? "BEST" : "LAST"; r.OffsetTicks = (int)_rOffset.Value;
        r.CooldownSeconds = (int)_rCool.Value; r.MaxBetsPerMatch = (int)_rMax.Value;
        _rDesc.Text = RuleLogic.Describe(r);
        _loading = true; int i = _ruleList.SelectedIndex; if (i >= 0 && (string)_ruleList.Items[i] != r.Name) _ruleList.Items[i] = r.Name; _loading = false;
    }

    // ---- master switch ---------------------------------------------------------------------------------------
    void ToggleMaster()
    {
        if (_host.Rules.Armed) { _host.Rules.Disarm("turned off"); UpdateMaster(); return; }
        if (!Apply()) return;
        var on = _o.Rules.Where(r => r.Enabled).ToList();
        if (on.Count == 0) { MessageBox.Show("Tick at least one rule first.", "Automation rules"); return; }
        var text = "While ON, a goal in a watched match places these bets:" + NL + NL + string.Join(NL, on.Select(r => "  " + r.Name + ": " + RuleLogic.Describe(r))) + NL + NL +
                   $"At most {_o.Rules.Max(r => r.MaxBetsPerMatch)} bets per match per rule, and the rules switch themselves OFF after {_o.RulesMaxBets} bets or 3 failures in a row." + NL +
                   (_o.RulesAllowDelayed ? "Prices may be DELAYED: bets can be placed at old prices." : "Bets are skipped while prices are delayed.") + NL + NL + "Real bets will be placed on your account.";
        if (MessageBox.Show(text, "Turn the rules ON?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _host.Rules.Arm(); UpdateMaster();
    }

    void UpdateMaster()
    {
        bool on = _host.Rules.Armed;
        _master.Text = on ? "RULES ON - click to turn off" : "Rules OFF - click to turn on";
        _master.BackColor = on ? Color.Firebrick : Color.White; _master.ForeColor = on ? Color.White : Color.Black;
    }

    void OnRulesChanged() { if (!IsDisposed && IsHandleCreated) BeginInvoke(() => { UpdateMaster(); _log.Items.Clear(); foreach (var l in _host.Rules.Log.TakeLast(60)) _log.Items.Add(l); _log.TopIndex = Math.Max(0, _log.Items.Count - 1); }); }

    // ---- load / apply ------------------------------------------------------------------------------------------
    void LoadValues()
    {
        _refresh.Value = Math.Clamp(_o.RefreshMs, 500, 10000); _onTop.Checked = _o.AlwaysOnTop; _centre.Checked = _o.AutoCentre;
        _stakes.Text = string.Join(", ", _o.StakePresets.Select(s => s.ToString("0.##", CultureInfo.InvariantCulture)));
        _defaultStake.Value = (decimal)Math.Max(1, _o.DefaultStake); _backStake.Value = (decimal)Math.Max(1, _o.ClassicBackStake); _layAmount.Value = (decimal)Math.Max(1, _o.ClassicLayStake);
        _layLiab.Checked = _o.ClassicLayLiability; _confirm.Checked = _o.ConfirmBets;
        _persist.SelectedIndex = _o.Persistence switch { "PERSIST" => 1, "MARKET_ON_CLOSE" => 2, _ => 0 };
        _relay.Text = _o.RelayUrl; _autoOpen.Checked = _o.AutoOpenOnScore; _mirror.Checked = _o.MirrorToWeb;
        foreach (var c in _o.FavoriteCompetitions) _favComps.Items.Add(new FavItem(c.CompetitionId, c.Name));
        foreach (var f in _o.Favorites) _favMatches.Items.Add(new FavItem(f.EventId, f.Name));
        _rulesDelayed.Checked = _o.RulesAllowDelayed; _rulesMax.Value = Math.Clamp(_o.RulesMaxBets, 1, 500);
        RefreshList(0); UpdateMaster(); OnRulesChanged();
    }

    record FavItem(string Id, string Name) { public override string ToString() => Name; }

    bool Apply()
    {
        var stakes = new List<double>();
        foreach (var part in _stakes.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Num(part, out var v) || v < 1) { _tabs.SelectedIndex = 1; MessageBox.Show($"'{part}' is not a valid stake (minimum 1).", "Settings"); return false; }
            stakes.Add(v);
        }
        if (stakes.Count == 0) { _tabs.SelectedIndex = 1; MessageBox.Show("Enter at least one preset stake.", "Settings"); return false; }
        var relay = _relay.Text.Trim();
        if (relay != "" && !(Uri.TryCreate(relay, UriKind.Absolute, out var ru) && ru.Scheme is "ws" or "wss")) { _tabs.SelectedIndex = 2; MessageBox.Show("The relay address must look like ws://host:port", "Settings"); return false; }

        bool relayChanged = relay != _o.RelayUrl;
        _o.RefreshMs = (int)_refresh.Value; _o.AlwaysOnTop = _onTop.Checked; _o.AutoCentre = _centre.Checked;
        _o.StakePresets = stakes.Distinct().OrderBy(s => s).ToList(); _o.DefaultStake = (double)_defaultStake.Value;
        _o.ClassicBackStake = (double)_backStake.Value; _o.ClassicLayStake = (double)_layAmount.Value; _o.ClassicLayLiability = _layLiab.Checked;
        _o.Persistence = _persist.SelectedIndex switch { 1 => "PERSIST", 2 => "MARKET_ON_CLOSE", _ => "LAPSE" }; _o.ConfirmBets = _confirm.Checked;
        _o.RelayUrl = relay; _o.AutoOpenOnScore = _autoOpen.Checked; _o.MirrorToWeb = _mirror.Checked;
        var keepComps = _favComps.Items.Cast<FavItem>().Select(i => i.Id).ToHashSet(); var keepMatches = _favMatches.Items.Cast<FavItem>().Select(i => i.Id).ToHashSet();
        bool favChanged = keepComps.Count != _o.FavoriteCompetitions.Count || keepMatches.Count != _o.Favorites.Count;
        _o.FavoriteCompetitions = _o.FavoriteCompetitions.Where(c => keepComps.Contains(c.CompetitionId)).ToList();
        _o.Favorites = _o.Favorites.Where(f => keepMatches.Contains(f.EventId)).ToList();
        _o.Rules = _rules.Select(r => r.Copy()).ToList();
        _o.RulesAllowDelayed = _rulesDelayed.Checked; _o.RulesMaxBets = (int)_rulesMax.Value;
        _o.Save();
        if (relayChanged) _host.StartScores();
        if (favChanged) _host.RaiseFavoritesChanged();
        return true;
    }
}
