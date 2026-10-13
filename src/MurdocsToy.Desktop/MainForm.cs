using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

sealed class MainForm : Form
{
    readonly BetfairClient _bf;
    readonly bool _smoke;
    readonly Options _opt = Options.Load();
    readonly bool _openLadder;
    readonly Button _openBtn = new() { Text = "Open ladder", AutoSize = true };
    readonly Button _optBtn = new() { Text = "Options", AutoSize = true };
    readonly Button _webBtn = new() { Text = "Web", AutoSize = true, Visible = false };
    WebMirror? _web;
    readonly LiveScoreFeed _scores = new();
    readonly Label _live = new() { AutoSize = true, Padding = new Padding(8, 5, 0, 0), ForeColor = Color.Gray, Text = "" };
    readonly Label _scoreLbl = new() { Dock = DockStyle.Top, Height = 0, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12f, FontStyle.Bold), BackColor = Color.FromArgb(18, 21, 26), ForeColor = Color.White };
    readonly System.Windows.Forms.Timer _scoreTimer = new() { Interval = 2000 };
    DateTime _flashUntil;
    readonly bool _privacy; // --privacy hides balance and orders (for screenshots and screen sharing)

    readonly ComboBox _sports = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    readonly TextBox _search = new() { Width = 160, PlaceholderText = "search matches" };
    readonly CheckBox _inPlay = new() { Text = "In-play", AutoSize = true };
    readonly Button _favBtn = new() { Text = "\u2605 Favorite", AutoSize = true };
    readonly CheckBox _autoOpen = new() { Text = "Auto-open favorites on score change", AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
    readonly Dictionary<string, LadderForm> _ladderWindows = new();
    readonly Dictionary<string, MarketCatalogEntry> _matchOdds = new();
    string _favSig = "";
    readonly Dictionary<string, double> _volumes = new();
    readonly System.Windows.Forms.Timer _volTimer = new() { Interval = 20000 };
    bool _volBusy;
    readonly Button _find = new() { Text = "Find", AutoSize = true };
    readonly NumericUpDown _stake = new() { DecimalPlaces = 2, Minimum = 1, Maximum = 10000, Value = 2, Width = 70 };
    readonly Label _funds = new() { AutoSize = true, Padding = new Padding(8, 5, 0, 0) };
    readonly Label _banner = new() { AutoSize = true, Padding = new Padding(8, 5, 0, 0), ForeColor = Color.DarkOrange };
    readonly TreeView _tree = new() { Dock = DockStyle.Fill, ShowLines = true, ShowRootLines = true, HideSelection = false, FullRowSelect = true, ItemHeight = 20 };
    readonly Dictionary<TreeNode, Task> _loads = new();
    readonly LiquidityPanel _liq = new() { Dock = DockStyle.Bottom };
    readonly DataGridView _runners = Grid();
    readonly DataGridView _ladder = Grid();
    readonly DataGridView _orders = Grid();
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(4, 3, 0, 0) };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    readonly System.Windows.Forms.Timer _keepAlive = new() { Interval = 15 * 60 * 1000 };

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

    public MainForm(BetfairClient bf, bool smoke, bool privacy = false, bool openLadder = false, string? search = null)
    {
        if (search != null) _search.Text = search;
        _bf = bf; _smoke = smoke; _privacy = privacy; _openLadder = openLadder;
        Text = "Murdoc's Toy";
        Size = new Size(1280, 800);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 3, 4, 3) };
        top.Controls.AddRange(new Control[]
        {
            new Label { Text = "Sport", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, _sports, _search, _inPlay, _find, _favBtn, _autoOpen,
            _openBtn, _optBtn, _webBtn, _live,
            new Label { Text = "Stake", AutoSize = true, Padding = new Padding(16, 5, 0, 0) }, _stake, _funds, _banner,
        });

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 200 };
        var centre = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 700 };
        centre.Panel1.Controls.Add(_runners);
        centre.Panel1.Controls.Add(_liq);
        centre.Panel1.Controls.Add(_scoreLbl);
        centre.Panel2.Controls.Add(_ladder);
        var lower = new GroupBox { Text = "Current orders (double-click to cancel)", Dock = DockStyle.Fill };
        lower.Controls.Add(_orders);
        right.Panel1.Controls.Add(centre);
        right.Panel2.Controls.Add(lower);

        var main = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 320 };
        main.Panel1.Controls.Add(_tree);
        main.Panel2.Controls.Add(right);

        Controls.Add(main); Controls.Add(top); Controls.Add(_status);

        _runners.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _runners.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _runners.Columns.AddRange(
            Col("Runner", 3), Col("Back 3"), Col("Back 2"), Col("Back"), Col("Lay"), Col("Lay 2"), Col("Lay 3"), Col("Last"), Col("Matched"));
        _ladder.Columns.AddRange(Col("Traded"), Col("Back"), Col("Price"), Col("Lay"));
        _orders.Columns.AddRange(Col("Market", 2), Col("Runner", 2), Col("Side"), Col("Price"), Col("Size"), Col("Matched"), Col("Remaining"), Col("Status"), Col("Bet id", 2));

        _openBtn.Click += async (_, _) => await OpenLadder();
        _webBtn.Click += (_, _) => { if (_web != null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_web.Url) { UseShellExecute = true }); };
        FormClosed += (_, _) => _web?.Dispose();
        _optBtn.Click += (_, _) => { using var f = new OptionsForm(_opt); if (f.ShowDialog(this) == DialogResult.OK) { _opt.Save(); StartScores(); } };
        _tree.BeforeExpand += (_, e) => { _ = ExpandLoad(e.Node!); };
        _tree.AfterSelect += async (_, e) => await OnPick(e.Node!);
        _tree.NodeMouseDoubleClick += async (_, e) => { _tree.SelectedNode = e.Node; await OpenLadder(); };
        _sports.SelectedIndexChanged += async (_, _) => { if (_sports.Focused) await LoadTree(); };
        _inPlay.CheckedChanged += async (_, _) => await LoadTree();
        _find.Click += async (_, _) => await LoadTree();
        _autoOpen.Checked = _opt.AutoOpenOnScore;
        _autoOpen.CheckedChanged += (_, _) => { _opt.AutoOpenOnScore = _autoOpen.Checked; _opt.Save(); Say(_autoOpen.Checked ? "Favorites: ladder opens when a score changes" : "Favorites: auto-open off"); };
        _favBtn.Click += (_, _) => { if (SelectedEvent() is { } ev) ToggleFavorite(ev); else Say("Select a match first"); };
        _tree.NodeMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            _tree.SelectedNode = e.Node;
            if (SelectedEvent() is not { } ev) return;
            var menu = new ContextMenuStrip();
            menu.Items.Add(IsFavorite(ev.Id) ? "Remove from favorites" : "Add to favorites", null, (_, _) => ToggleFavorite(ev));
            menu.Items.Add("Open ladder", null, async (_, _) => await OpenLadder());
            menu.Show(_tree, e.Location);
        };
        _scores.ScoreChanged += (m, ph, pa) => { if (!IsDisposed && IsHandleCreated) BeginInvoke(() => OnScoreChanged(m, ph, pa)); };
        _search.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) await LoadTree(); };
        _runners.SelectionChanged += (_, _) => PickRunner();
        _ladder.CellClick += async (_, e) => await LadderClick(e);
        _orders.CellDoubleClick += async (_, e) => await CancelOrder(e.RowIndex);
        _keepAlive.Tick += async (_, _) => { try { await _bf.KeepAliveAsync(); } catch { /* the next call re-logs in if needed */ } };
        _keepAlive.Start();
        _scoreTimer.Tick += (_, _) => RefreshScores();
        _volTimer.Tick += async (_, _) => await RefreshVolumes();
        _volTimer.Start();
        _scoreTimer.Start();
        _scores.Alert += a => { if (!IsDisposed && IsHandleCreated) BeginInvoke(() => { _flashUntil = DateTime.UtcNow.AddSeconds(20); Say(a.Text); RefreshScores(); }); };
        FormClosed += (_, _) => _scores.Dispose();
        _timer.Tick += async (_, _) => await Poll();
        Shown += async (_, _) => await Startup();
        // Splitter positions only make sense once the window has its real size.
        void Layout3()
        {
            if (main.Width < 600 || right.Height < 300) return;
            main.SplitterDistance = Math.Min(380, main.Width / 3);
            centre.SplitterDistance = Math.Max(300, (int)(centre.Width * 0.68));
            right.SplitterDistance = (int)(right.Height * 0.68);
        }
        Load += (_, _) => Layout3();
        Shown += (_, _) => Layout3();
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
            StartScores();
            StartMirror();
            await LoadTree();
            await AutoSelect();
            await RefreshFunds();
            _timer.Start();
            if (_openLadder) await OpenLadder();
        }
        catch (Exception ex) { Say(ex.Message); if (_smoke) Smoke("FAIL " + ex.Message, 1); }
    }

    async Task RefreshFunds()
    {
        if (_privacy) return;
        try
        {
            var f = await _bf.GetAccountFundsAsync();
            _funds.Text = $"Balance {f.AvailableToBetBalance:0.00}  Exposure {f.Exposure:0.00}";
        }
        catch (BetfairException ex) { _funds.Text = "Funds: " + ex.Code; }
    }

    static readonly object Placeholder = "ph";
    string SportId => _types[_sports.SelectedIndex].Id;

    static TreeNode WithPlaceholder(TreeNode n) { n.Nodes.Add(new TreeNode("Loading...") { Tag = Placeholder }); return n; }

    /// <summary>Root of the tree: competitions of the chosen sport, or matching matches when a search text is given.</summary>
    async Task LoadTree()
    {
        if (_sports.SelectedIndex < 0) return;
        try
        {
            Say("Loading...");
            _loads.Clear(); _tree.Nodes.Clear(); _market = null;
            var text = string.IsNullOrWhiteSpace(_search.Text) ? null : _search.Text.Trim();
            if (text != null)
                foreach (var e in await _bf.ListEventsAsync(SportId, null, text, _inPlay.Checked)) _tree.Nodes.Add(EventTreeNode(e));
            else
                foreach (var c in await _bf.ListCompetitionsAsync(SportId, _inPlay.Checked))
                    _tree.Nodes.Add(WithPlaceholder(new TreeNode($"{c.Name} ({c.MarketCount})") { Tag = c }));
            SyncFavorites();
            _ = RefreshVolumes();
            Say(_tree.Nodes.Count <= 1 ? "Nothing found" : $"{_tree.Nodes.Count} {(text != null ? "matches" : "competitions")}");
        }
        catch (Exception ex) { Say(ex.Message); }
    }

    TreeNode EventTreeNode(EventNode e)
    {
        var n = WithPlaceholder(new TreeNode { Tag = e });
        n.Text = EventLabel(e, null);
        return n;
    }

    /// <summary>"[star] [dd/MM HH:mm] Porto 0 - 1 Benfica (145643€)": live score between the teams, match volume after.</summary>
    string EventLabel(EventNode e, LiveMatch? live)
    {
        var parts = e.Name.Split(" v ", 2, StringSplitOptions.TrimEntries);
        string game = live != null && parts.Length == 2 ? $"{parts[0]} {live.HomeScore} - {live.AwayScore} {parts[1]}"
                    : live != null ? $"{e.Name} [{live.ScoreText}]" : e.Name;
        string when = live == null && e.OpenDate != null ? e.OpenDate.Value.ToLocalTime().ToString("dd/MM HH:mm  ") : "";
        string vol = _volumes.TryGetValue(e.Id, out var v) && v > 0 ? $" ({(long)v}€)" : "";
        return (IsFavorite(e.Id) ? "★ " : "") + when + game + vol;
    }

    /// <summary>Matched volume of every match currently in the tree, fetched in a few batched calls.</summary>
    async Task RefreshVolumes()
    {
        if (_volBusy) return;
        _volBusy = true;
        try
        {
            var ids = EventNodes(_tree.Nodes).Select(n => ((EventNode)n.Tag!).Id).Distinct().ToList();
            if (ids.Count == 0) return;
            foreach (var kv in await _bf.GetEventVolumesAsync(ids)) _volumes[kv.Key] = kv.Value;
            RefreshScores();
        }
        catch (Exception) { /* volumes are decoration: try again next tick */ }
        finally { _volBusy = false; }
    }

    // ---- favorites -------------------------------------------------------------------------------------------
    bool IsFavorite(string eventId) => _opt.Favorites.Any(f => f.EventId == eventId);

    EventNode? SelectedEvent() => _tree.SelectedNode switch
    {
        { Tag: EventNode e } => e,
        { Tag: MarketCatalogEntry, Parent.Tag: EventNode e } => e,
        _ => null,
    };

    void ToggleFavorite(EventNode ev)
    {
        var list = _opt.Favorites.ToList();            // replaced as a whole: the web mirror may read it from another thread
        if (list.RemoveAll(f => f.EventId == ev.Id) == 0) list.Add(new FavoriteEvent(ev.Id, ev.Name));
        _opt.Favorites = list; _opt.Save();
        Say(IsFavorite(ev.Id) ? $"Added to favorites: {ev.Name}" : $"Removed from favorites: {ev.Name}");
        SyncFavorites();
    }

    /// <summary>Stars on matches and the Favorites section follow the saved list (also when the web UI changed it).</summary>
    void SyncFavorites()
    {
        _favSig = string.Join(",", _opt.Favorites.Select(f => f.EventId));
        var old = _tree.Nodes.Cast<TreeNode>().FirstOrDefault(x => x.Tag is string t && t == "favorites");
        bool wasOpen = old?.IsExpanded ?? true;
        old?.Remove();
        var root = new TreeNode($"\u2605 Favorites ({_opt.Favorites.Count})") { Tag = "favorites", NodeFont = new Font(_tree.Font, FontStyle.Bold) };
        foreach (var f in _opt.Favorites) root.Nodes.Add(EventTreeNode(new EventNode(f.EventId, f.Name, null, null, 0)));
        if (_opt.Favorites.Count == 0) root.Nodes.Add(new TreeNode("(right-click a match, or select it and press \u2605 Favorite)") { ForeColor = Color.Gray, Tag = "hint" });
        _tree.Nodes.Insert(0, root);
        if (wasOpen) root.Expand();
        RefreshScores();
    }

    /// <summary>A score changed on the relay: if it is a favorite and auto-open is on, show that match's ladder.</summary>
    void OnScoreChanged(LiveMatch m, int prevHome, int prevAway)
    {
        if (!_opt.AutoOpenOnScore) return;
        var fav = _opt.Favorites.FirstOrDefault(f => TeamMatcher.Score(f.Name, m.Home, m.Away) > 0);
        if (fav != null) _ = OpenFavoriteLadder(fav, m, prevHome, prevAway);
    }

    async Task OpenFavoriteLadder(FavoriteEvent fav, LiveMatch m, int prevHome, int prevAway)
    {
        try
        {
            if (!_matchOdds.TryGetValue(fav.EventId, out var market))
            {
                market = (await _bf.ListEventMarketsAsync(fav.EventId)).FirstOrDefault();
                if (market == null) return;
                _matchOdds[fav.EventId] = market;
            }
            ShowLadder(market);
            Say($"Score change {m.Home} {prevHome}-{prevAway} -> {m.ScoreText} {m.Away}: ladder opened");
        }
        catch (Exception ex) { Say(ex.Message); }
    }

    /// <summary>Opens the ladder window for a market, or raises it if it is already open.</summary>
    void ShowLadder(MarketCatalogEntry market)
    {
        if (_ladderWindows.TryGetValue(market.MarketId, out var open) && !open.IsDisposed)
        {
            if (open.WindowState == FormWindowState.Minimized) open.WindowState = FormWindowState.Normal;
            open.Activate(); open.BringToFront();
            return;
        }
        var f = new LadderForm(_bf, market, _opt, _privacy, _scores);
        _ladderWindows[market.MarketId] = f;
        f.FormClosed += (_, _) => _ladderWindows.Remove(market.MarketId);
        f.Show(this);
    }

    /// <summary>Loads a node's children once (events of a competition, markets of a match).</summary>
    Task ExpandLoad(TreeNode n)
    {
        if (_loads.TryGetValue(n, out var t)) return t;
        if (n.Nodes.Count != 1 || n.Nodes[0].Tag != Placeholder) return Task.CompletedTask;
        return _loads[n] = Fill(n);
    }

    async Task Fill(TreeNode n)
    {
        try
        {
            var kids = new List<TreeNode>();
            if (n.Tag is CompetitionNode c)
                kids.AddRange((await _bf.ListEventsAsync(SportId, c.Id, null, _inPlay.Checked)).Select(EventTreeNode));
            else if (n.Tag is EventNode e)
                foreach (var m in await _bf.ListEventMarketsAsync(e.Id))
                {
                    if (!_catalog.Any(x => x.MarketId == m.MarketId)) _catalog.Add(m);
                    kids.Add(new TreeNode(m.TotalMatched > 0 ? $"{m.MarketName}   [{Liquidity.Short(m.TotalMatched.Value)}]" : m.MarketName) { Tag = m });
                }
            n.Nodes.Clear();
            foreach (var k in kids) n.Nodes.Add(k);
            if (n.Tag is CompetitionNode) _ = RefreshVolumes();
        }
        catch (Exception ex) { Say(ex.Message); n.Nodes.Clear(); WithPlaceholder(n); _loads.Remove(n); }
    }

    /// <summary>Opens the busiest competition's first match and selects its Match Odds market.</summary>
    async Task AutoSelect()
    {
        var node = _tree.Nodes.Cast<TreeNode>().Where(x => x.Tag is CompetitionNode or EventNode).OrderByDescending(x => (x.Tag as CompetitionNode)?.MarketCount ?? 0).FirstOrDefault();
        if (node == null) return;
        if (node.Tag is CompetitionNode) { node.Expand(); await ExpandLoad(node); node = node.Nodes.Cast<TreeNode>().FirstOrDefault(x => x.Tag is EventNode); }
        if (node?.Tag is not EventNode) return;
        node.Expand(); await ExpandLoad(node);
        _tree.SelectedNode = node.Nodes.Cast<TreeNode>().FirstOrDefault(x => (x.Tag as MarketCatalogEntry)?.MarketName == "Match Odds") ?? node.FirstNode;
    }

    async Task<MarketCatalogEntry?> ResolveMarket(TreeNode? n)
    {
        if (n?.Tag is MarketCatalogEntry m) return m;
        if (n?.Tag is EventNode)
        {
            await ExpandLoad(n);
            return n.Nodes.Cast<TreeNode>().Select(x => x.Tag as MarketCatalogEntry).FirstOrDefault(x => x != null);
        }
        return null;
    }

    async Task OpenLadder()
    {
        var m = await ResolveMarket(_tree.SelectedNode);
        if (m == null) { Say("Select a match or market first"); return; }
        ShowLadder(m);
    }

    void StartMirror()
    {
        if (!_opt.MirrorToWeb) return;
        _web = new WebMirror(_bf, _opt, _privacy, _scores);
        if (_web.Start()) { _webBtn.Visible = true; _webBtn.Text = "Web " + _web.Url.Replace("http://", "").TrimEnd('/'); }
        else { _web.Dispose(); _web = null; Say("Could not start the web mirror (ports 8080-8089 busy)"); }
    }

    /// <summary>Connects to the live-score relay named in Options (MT_RELAY_URL overrides it); empty means off.</summary>
    void StartScores()
    {
        var url = Environment.GetEnvironmentVariable("MT_RELAY_URL");
        if (string.IsNullOrWhiteSpace(url)) url = _opt.RelayUrl;
        _scores.Stop();
        if (string.IsNullOrWhiteSpace(url)) { _live.Text = ""; return; }
        _scores.Start(url.Trim());
        _live.Text = "Live scores: connecting...";
    }

    IEnumerable<TreeNode> EventNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode n in nodes)
        {
            if (n.Tag is EventNode) yield return n;
            else if (n.Tag is CompetitionNode || n.Tag is string t && t == "favorites") foreach (var c in EventNodes(n.Nodes)) yield return c;
        }
    }

    /// <summary>Score badges on matches in the tree, the banner over the selected market, and the connection indicator.</summary>
    void RefreshScores()
    {
        if (string.Join(",", _opt.Favorites.Select(f => f.EventId)) != _favSig) SyncFavorites();
        if (_scores.Url != null)
        {
        _live.Text = _scores.Connected ? $"Live scores: {_scores.Matches.Count} matches" : "Live scores: offline (retrying)";
        _live.ForeColor = _scores.Connected ? Color.SeaGreen : Color.Firebrick;
        }
        _tree.BeginUpdate();
        foreach (var n in EventNodes(_tree.Nodes))
        {
            var e = (EventNode)n.Tag!;
            var m = _scores.Url == null ? null : _scores.Find(e.Name);
            var text = EventLabel(e, m);
            if (n.Text != text) n.Text = text;
            n.NodeFont = m != null ? (_boldFont ??= new Font(_tree.Font, FontStyle.Bold)) : null;
        }
        _tree.EndUpdate();
        var sel = _market?.Event?.Name == null || _scores.Url == null ? null : _scores.Find(_market.Event.Name);
        if (sel == null) { _scoreLbl.Height = 0; return; }
        bool flash = DateTime.UtcNow < _flashUntil;
        _scoreLbl.Height = 30;
        _scoreLbl.Text = $"{sel.Home}  {sel.HomeScore} - {sel.AwayScore}  {sel.Away}";
        _scoreLbl.BackColor = flash ? Color.Gold : Color.FromArgb(18, 21, 26);
        _scoreLbl.ForeColor = flash ? Color.Black : Color.White;
    }

    Font? _boldFont;

    async Task OnPick(TreeNode n)
    {
        if (n.Tag is not MarketCatalogEntry m) return;
        MirrorState.Select(m.MarketId);
        _market = m; _book = null; _selected = null; _liq.Reset(); RefreshScores();
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
                if (_book != null) _liq.Update(_book);
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
        if (_privacy) return;
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
