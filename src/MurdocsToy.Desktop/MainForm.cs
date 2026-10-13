using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

sealed class MainForm : Form
{
    readonly BetfairClient _bf;
    readonly bool _smoke;

    readonly ComboBox _sports = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    readonly TextBox _search = new() { Width = 160, PlaceholderText = "search markets" };
    readonly CheckBox _inPlay = new() { Text = "In-play", AutoSize = true };
    readonly Button _find = new() { Text = "Find", AutoSize = true };
    readonly NumericUpDown _stake = new() { DecimalPlaces = 2, Minimum = 1, Maximum = 10000, Value = 2, Width = 70 };
    readonly Label _funds = new() { AutoSize = true, Padding = new Padding(8, 5, 0, 0) };
    readonly Label _banner = new() { AutoSize = true, Padding = new Padding(8, 5, 0, 0), ForeColor = Color.DarkOrange };
    readonly ListBox _markets = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly DataGridView _runners = Grid();
    readonly DataGridView _ladder = Grid();
    readonly DataGridView _orders = Grid();
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(4, 3, 0, 0) };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };

    List<EventType> _types = new();
    List<MarketCatalogEntry> _catalog = new();
    MarketCatalogEntry? _market;
    MarketBook? _book;
    long? _selected;
    bool _busy;

    static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window,
        AllowUserToResizeRows = false,
    };

    static readonly Color BackBlue = Color.FromArgb(160, 210, 250), LayPink = Color.FromArgb(250, 190, 205);

    public MainForm(BetfairClient bf, bool smoke)
    {
        _bf = bf; _smoke = smoke;
        Text = "Murdoc's Toy";
        Size = new Size(1200, 780);
        StartPosition = FormStartPosition.CenterScreen;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4, 3, 4, 0) };
        top.Controls.AddRange(new Control[]
        {
            new Label { Text = "Sport", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, _sports, _search, _inPlay, _find,
            new Label { Text = "Stake", AutoSize = true, Padding = new Padding(16, 5, 0, 0) }, _stake, _funds, _banner,
        });

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 200 };
        var centre = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 700 };
        centre.Panel1.Controls.Add(_runners);
        centre.Panel2.Controls.Add(_ladder);
        var lower = new GroupBox { Text = "Current orders (double-click to cancel)", Dock = DockStyle.Fill };
        lower.Controls.Add(_orders);
        right.Panel1.Controls.Add(centre);
        right.Panel2.Controls.Add(lower);

        var main = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 320 };
        main.Panel1.Controls.Add(_markets);
        main.Panel2.Controls.Add(right);

        Controls.Add(main); Controls.Add(top); Controls.Add(_status);

        _runners.Columns.AddRange(
            Col("Runner", 3), Col("Back 3"), Col("Back 2"), Col("Back"), Col("Lay"), Col("Lay 2"), Col("Lay 3"), Col("Last"), Col("Matched"));
        _ladder.Columns.AddRange(Col("Traded"), Col("Back"), Col("Price"), Col("Lay"));
        _orders.Columns.AddRange(Col("Market", 2), Col("Runner", 2), Col("Side"), Col("Price"), Col("Size"), Col("Matched"), Col("Remaining"), Col("Status"), Col("Bet id", 2));

        _find.Click += async (_, _) => await FindMarkets();
        _search.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) await FindMarkets(); };
        _markets.SelectedIndexChanged += async (_, _) => await OpenMarket();
        _runners.SelectionChanged += (_, _) => PickRunner();
        _ladder.CellClick += async (_, e) => await LadderClick(e);
        _orders.CellDoubleClick += async (_, e) => await CancelOrder(e.RowIndex);
        _timer.Tick += async (_, _) => await Poll();
        Shown += async (_, _) => await Startup();
    }

    static DataGridViewTextBoxColumn Col(string name, float weight = 1) =>
        new() { HeaderText = name, Name = name, FillWeight = weight * 50, SortMode = DataGridViewColumnSortMode.NotSortable };

    void Say(string s) => _status.Text = s;

    async Task Startup()
    {
        try
        {
            _types = await _bf.ListEventTypesAsync();
            _sports.Items.AddRange(_types.Select(t => t.Name).Cast<object>().ToArray());
            _sports.SelectedIndex = Math.Max(0, _types.FindIndex(t => t.Name == "Soccer"));
            await FindMarkets();
            await RefreshFunds();
            _timer.Start();
        }
        catch (Exception ex) { Say(ex.Message); if (_smoke) Smoke("FAIL " + ex.Message, 1); }
    }

    async Task RefreshFunds()
    {
        try
        {
            var f = await _bf.GetAccountFundsAsync();
            _funds.Text = $"Balance {f.AvailableToBetBalance:0.00}  Exposure {f.Exposure:0.00}";
        }
        catch (BetfairException ex) { _funds.Text = "Funds: " + ex.Code; }
    }

    async Task FindMarkets()
    {
        if (_sports.SelectedIndex < 0) return;
        try
        {
            Say("Loading markets...");
            _catalog = await _bf.ListMarketCatalogueAsync(_types[_sports.SelectedIndex].Id,
                string.IsNullOrWhiteSpace(_search.Text) ? null : _search.Text, _inPlay.Checked, null, 100);
            _markets.Items.Clear();
            foreach (var m in _catalog)
                _markets.Items.Add($"{m.MarketStartTime?.ToLocalTime():dd/MM HH:mm}  {m.Event?.Name} - {m.MarketName}");
            Say($"{_catalog.Count} markets");
            if (_smoke && _catalog.Count > 0) _markets.SelectedIndex = 0;
        }
        catch (Exception ex) { Say(ex.Message); }
    }

    async Task OpenMarket()
    {
        if (_markets.SelectedIndex < 0) return;
        _market = _catalog[_markets.SelectedIndex];
        _book = null; _selected = null;
        await Poll();
    }

    async Task Poll()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (_market != null)
            {
                _book = (await _bf.ListMarketBookAsync(new[] { _market.MarketId })).FirstOrDefault();
                DrawRunners();
                DrawLadder();
                _banner.Text = _book?.IsMarketDataDelayed == true ? "DELAYED DATA (delayed app key)" : "";
            }
            await DrawOrders();
            if (_smoke && _book != null)
                Smoke($"OK market={_market!.MarketName} runners={_runners.Rowcount()} ladderRows={_ladder.RowCount} delayed={_book.IsMarketDataDelayed}", 0);
        }
        catch (Exception ex) { Say(ex.Message); }
        finally { _busy = false; }
    }

    void Smoke(string msg, int code)
    {
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke.txt"), msg);
        Environment.Exit(code);
    }

    static PriceSize? At(List<PriceSize>? l, int i) => l != null && l.Count > i ? l[i] : null;
    static string Px(PriceSize? p) => p == null ? "" : $"{p.Price:0.##}\n{p.Size:0}";

    void DrawRunners()
    {
        if (_market == null || _book?.Runners == null) return;
        var sel = _selected;
        _runners.Rows.Clear();
        foreach (var r in _book.Runners)
        {
            var name = _market.Runners?.FirstOrDefault(x => x.SelectionId == r.SelectionId)?.RunnerName ?? r.SelectionId.ToString();
            var back = r.Ex?.AvailableToBack; var lay = r.Ex?.AvailableToLay;
            int i = _runners.Rows.Add(name, Px(At(back, 2)), Px(At(back, 1)), Px(At(back, 0)),
                Px(At(lay, 0)), Px(At(lay, 1)), Px(At(lay, 2)), r.LastPriceTraded?.ToString("0.##"), r.TotalMatched?.ToString("0"));
            var row = _runners.Rows[i]; row.Tag = r.SelectionId; row.Height = 40;
            for (int c = 1; c <= 3; c++) row.Cells[c].Style.BackColor = BackBlue;
            for (int c = 4; c <= 6; c++) row.Cells[c].Style.BackColor = LayPink;
            if (r.Status != "ACTIVE") row.DefaultCellStyle.ForeColor = Color.Gray;
        }
        if (sel != null)
            foreach (DataGridViewRow row in _runners.Rows)
                if ((long)row.Tag! == sel) { _runners.CurrentCell = row.Cells[0]; break; }
        if (_selected == null && _runners.Rows.Count > 0)
        {
            _runners.CurrentCell = _runners.Rows[0].Cells[0];
            _selected = (long)_runners.Rows[0].Tag!;
        }
    }

    void PickRunner()
    {
        if (_runners.CurrentRow?.Tag is long id && id != _selected) { _selected = id; DrawLadder(); }
    }

    void DrawLadder()
    {
        _ladder.Rows.Clear();
        var r = _book?.Runners?.FirstOrDefault(x => x.SelectionId == _selected);
        if (r == null) return;
        double centre = r.LastPriceTraded ?? r.Ex?.AvailableToBack?.FirstOrDefault()?.Price ?? 2.0;
        var back = (r.Ex?.AvailableToBack ?? new()).ToDictionary(p => p.Price, p => p.Size);
        var lay = (r.Ex?.AvailableToLay ?? new()).ToDictionary(p => p.Price, p => p.Size);
        var traded = (r.Ex?.TradedVolume ?? new()).ToDictionary(p => p.Price, p => p.Size);
        foreach (var price in PriceLadder.Window(centre, 12, 12))
        {
            int i = _ladder.Rows.Add(traded.TryGetValue(price, out var t) ? t.ToString("0") : "",
                back.TryGetValue(price, out var b) ? b.ToString("0.00") : "", price.ToString("0.##"),
                lay.TryGetValue(price, out var l) ? l.ToString("0.00") : "");
            var row = _ladder.Rows[i]; row.Tag = price;
            row.Cells[1].Style.BackColor = BackBlue; row.Cells[3].Style.BackColor = LayPink;
            row.Cells[2].Style.Font = new Font(Font, FontStyle.Bold);
            if (Math.Abs(price - (r.LastPriceTraded ?? 0)) < 1e-9) row.Cells[2].Style.BackColor = Color.Gold;
        }
    }

    async Task LadderClick(DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _market == null || _selected == null || (e.ColumnIndex != 1 && e.ColumnIndex != 3)) return;
        double price = (double)_ladder.Rows[e.RowIndex].Tag!;
        string side = e.ColumnIndex == 1 ? "BACK" : "LAY";
        double size = (double)_stake.Value;
        var runner = _market.Runners?.FirstOrDefault(x => x.SelectionId == _selected)?.RunnerName;
        if (MessageBox.Show($"{side} {runner} @ {price:0.##} for {size:0.00}?", "Confirm bet", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            var rep = await _bf.PlaceOrderAsync(_market.MarketId, new PlaceInstruction(_selected.Value, side, price, size));
            Say($"Order {rep.Status} {rep.ErrorCode} matched={rep.SizeMatched}");
            await DrawOrders();
        }
        catch (BetfairException ex) { Say(ex.Message); MessageBox.Show(ex.Message + "\nA delayed app key cannot place bets.", "Order failed"); }
    }

    List<CurrentOrder> _current = new();

    async Task DrawOrders()
    {
        try { _current = await _bf.ListCurrentOrdersAsync(); }
        catch (BetfairException) { return; }
        _orders.Rows.Clear();
        foreach (var o in _current)
        {
            var mk = _catalog.FirstOrDefault(c => c.MarketId == o.MarketId);
            _orders.Rows.Add(mk?.MarketName ?? o.MarketId,
                mk?.Runners?.FirstOrDefault(r => r.SelectionId == o.SelectionId)?.RunnerName ?? o.SelectionId.ToString(),
                o.Side, o.Price.ToString("0.##"), o.Size.ToString("0.00"), o.SizeMatched.ToString("0.00"),
                o.SizeRemaining.ToString("0.00"), o.Status, o.BetId);
        }
    }

    async Task CancelOrder(int row)
    {
        if (row < 0 || row >= _current.Count) return;
        var o = _current[row];
        if (o.Status != "EXECUTABLE") return;
        try { await _bf.CancelOrderAsync(o.MarketId, o.BetId); await DrawOrders(); }
        catch (BetfairException ex) { Say(ex.Message); }
    }
}

static class GridExt { public static int Rowcount(this DataGridView g) => g.Rows.Count; }
