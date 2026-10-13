using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>DEPRECATED (kept, not offered; start with --ui=dark|light). The Modern main window: tree explorer on the left, selected market on the right.</summary>
sealed class MainForm : Form
{
    readonly AppHost _host;
    readonly BetfairClient _bf;
    readonly Options _opt = Options.Load();
    readonly bool _smoke, _openLadder, _privacy;

    readonly Button _openBtn = new() { Text = "Open ladder", AutoSize = true };
    readonly Button _optBtn = new() { Text = "Options", AutoSize = true };
    readonly Button _webBtn = new() { Text = "Web", AutoSize = true, Visible = false };
    readonly Label _live = new() { AutoSize = true, Padding = new Padding(8, 5, 0, 0), ForeColor = Color.Gray, Text = "" };
    readonly Label _scoreLbl = new() { Dock = DockStyle.Top, Height = 0, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12f, FontStyle.Bold), BackColor = Color.FromArgb(18, 21, 26), ForeColor = Color.White };
    readonly System.Windows.Forms.Timer _scoreTimer = new() { Interval = 2000 };
    DateTime _flashUntil;

    readonly ComboBox _sports = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    readonly TextBox _search = new() { Width = 160, PlaceholderText = "search matches" };
    readonly CheckBox _inPlay = new() { Text = "In-play", AutoSize = true };
    readonly Button _favBtn = new() { Text = "★ Favorite", AutoSize = true };
    readonly CheckBox _autoOpen = new() { Text = "Auto-open favorites on score change", AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
    string _favSig = "";
    readonly System.Windows.Forms.Timer _volTimer = new() { Interval = 20000 };
    readonly Button _find = new() { Text = "Find", AutoSize = true };
    readonly NumericUpDown _stake = new() { DecimalPlaces = 2, Minimum = 1, Maximum = 10000, Value = 2, Width = 70 };
    readonly Label _funds = new() { AutoSize = true, Padding = new Padding(8, 5, 0, 0) };
    readonly Label _banner = new() { AutoSize = true, Padding = new Padding(8, 5, 0, 0), ForeColor = Color.DarkOrange };
    readonly TreeView _tree = new() { Dock = DockStyle.Fill, ShowLines = true, ShowRootLines = true, HideSelection = false, FullRowSelect = true, ItemHeight = 20 };
    readonly FlagList _flags = new(new Size(20, 15));
    readonly Dictionary<TreeNode, Task> _loads = new();
    readonly DataGridView _runners = Grid();
    readonly DataGridView _ladder = Grid();
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(4, 3, 0, 0) };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    readonly System.Windows.Forms.Timer _keepAlive = new() { Interval = 15 * 60 * 1000 };

    MarketCatalogEntry? _market;
    MarketBook? _book;
    long? _selected;
    bool _busy;
    Font? _boldFont;

    static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window,
        AllowUserToResizeRows = false,
    };

    static readonly Color BackBlue = Color.FromArgb(160, 210, 250), LayPink = Color.FromArgb(250, 190, 205);

    public MainForm(BetfairClient bf, string ui, bool smoke, bool privacy = false, bool openLadder = false, string? search = null)
    {
        if (search != null) _search.Text = search;
        _bf = bf; _smoke = smoke; _privacy = privacy; _openLadder = openLadder;
        _host = new AppHost(bf, _opt, privacy, ui, this);
        Text = "Murdoc's Toy";
        Size = new Size(1280, 800);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        _tree.ImageList = _flags.Images;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 3, 4, 3) };
        top.Controls.AddRange(new Control[]
        {
            new Label { Text = "Sport", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, _sports, _search, _inPlay, _find, _favBtn, _autoOpen,
            _openBtn, _optBtn, _webBtn, _live,
            new Label { Text = "Stake", AutoSize = true, Padding = new Padding(16, 5, 0, 0) }, _stake, _funds, _banner,
        });

        // right side: the selected market's runners above the single-runner ladder
        var centre = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 700 };
        centre.Panel1.Controls.Add(_runners);
        centre.Panel1.Controls.Add(_scoreLbl);
        centre.Panel2.Controls.Add(_ladder);

        var main = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 380 };
        main.Panel1.Controls.Add(_tree);
        main.Panel2.Controls.Add(centre);

        Controls.Add(main); Controls.Add(top); Controls.Add(_status);

        _runners.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _runners.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _runners.Columns.AddRange(
            Col("Runner", 3), Col("Back 3"), Col("Back 2"), Col("Back"), Col("Lay"), Col("Lay 2"), Col("Lay 3"), Col("Last"), Col("Matched"));
        _ladder.Columns.AddRange(Col("Traded"), Col("Back"), Col("Price"), Col("Lay"));

        _host.Status += s => _status.Text = s;
        _host.FavoritesChanged += SyncFavorites;
        _host.AlertReceived += _ => { _flashUntil = DateTime.UtcNow.AddSeconds(20); RefreshScores(); };

        _openBtn.Click += async (_, _) => await OpenLadder();
        _webBtn.Click += (_, _) => { if (_host.Web != null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_host.Web.Url) { UseShellExecute = true }); };
        FormClosed += (_, _) => _host.Dispose();
        _optBtn.Click += (_, _) => { using var f = new OptionsForm(_opt); Theme.Apply(f); if (f.ShowDialog(this) == DialogResult.OK) { _opt.Save(); _host.StartScores(); } };
        _tree.BeforeExpand += (_, e) => { _ = ExpandLoad(e.Node!); };
        _tree.AfterExpand += (_, e) => { _ = AutoExpandSingle(e.Node!); };
        _tree.AfterSelect += async (_, e) => await OnPick(e.Node!);
        _tree.NodeMouseDoubleClick += async (_, e) => { _tree.SelectedNode = e.Node; await OpenLadder(); };
        _sports.SelectedIndexChanged += async (_, _) => { if (_sports.Focused) await LoadTree(); };
        _inPlay.CheckedChanged += async (_, _) => await LoadTree();
        _find.Click += async (_, _) => await LoadTree();
        _autoOpen.Checked = _opt.AutoOpenOnScore;
        _autoOpen.CheckedChanged += (_, _) => { _opt.AutoOpenOnScore = _autoOpen.Checked; _opt.Save(); Say(_autoOpen.Checked ? "Favorites: ladder opens when a score changes" : "Favorites: auto-open off"); };
        _favBtn.Click += (_, _) =>
        {
            if (_tree.SelectedNode is { Tag: CompetitionNode comp } cn) _host.ToggleFavoriteCompetition(comp, string.IsNullOrEmpty(cn.Name) ? SportId : cn.Name);
            else if (SelectedEvent() is { } ev) _host.ToggleFavorite(ev);
            else Say("Select a match or competition first");
        };
        _tree.NodeMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            _tree.SelectedNode = e.Node;
            if (e.Node.Tag is CompetitionNode comp)
            {
                var cmenu = new ContextMenuStrip();
                var sport = string.IsNullOrEmpty(e.Node.Name) ? SportId : e.Node.Name;
                cmenu.Items.Add(_host.IsFavoriteCompetition(comp.Id) ? "Remove competition from favorites" : "Add competition to favorites", null, (_, _) => _host.ToggleFavoriteCompetition(comp, sport));
                Theme.Menu(cmenu);
                cmenu.Show(_tree, e.Location);
                return;
            }
            if (SelectedEvent() is not { } ev) return;
            var menu = new ContextMenuStrip();
            menu.Items.Add(_host.IsFavorite(ev.Id) ? "Remove from favorites" : "Add to favorites", null, (_, _) => _host.ToggleFavorite(ev));
            menu.Items.Add("Open ladder", null, async (_, _) => await OpenLadder());
            Theme.Menu(menu);
            menu.Show(_tree, e.Location);
        };
        _search.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) await LoadTree(); };
        _runners.SelectionChanged += (_, _) => PickRunner();
        _ladder.CellClick += async (_, e) => await LadderClick(e);
        _keepAlive.Tick += async (_, _) => { try { await _bf.KeepAliveAsync(); } catch { /* the next call re-logs in if needed */ } };
        _keepAlive.Start();
        _scoreTimer.Tick += (_, _) => RefreshScores();
        _scoreTimer.Start();
        _volTimer.Tick += async (_, _) => await RefreshVolumes();
        _volTimer.Start();
        _timer.Tick += async (_, _) => await Poll();
        Shown += async (_, _) => await Startup();
        // Splitter positions only make sense once the window has its real size.
        void Layout3()
        {
            if (main.Width < 600) return;
            main.SplitterDistance = Math.Min(380, main.Width / 3);
            centre.SplitterDistance = Math.Max(300, (int)(centre.Width * 0.7));
        }
        Load += (_, _) => Layout3();
        Shown += (_, _) => Layout3();
        Theme.Apply(this);
    }

    static DataGridViewTextBoxColumn Col(string name, float weight = 1) =>
        new() { HeaderText = name, Name = name, FillWeight = weight * 50, SortMode = DataGridViewColumnSortMode.NotSortable };

    void Say(string s) => _status.Text = s;

    async Task Startup()
    {
        try
        {
            _host.Types = await _bf.ListEventTypesAsync();
            _sports.Items.AddRange(_host.Types.Select(t => t.Name).Cast<object>().ToArray());
            _sports.SelectedIndex = Math.Max(0, _host.Types.FindIndex(t => t.Name == "Soccer"));
            _host.StartScores();
            _host.StartMirror();
            _host.StartFavoriteCompetitions();
            if (_host.Web != null) { _webBtn.Visible = true; _webBtn.Text = "Web " + _host.Web.Url.Replace("http://", "").TrimEnd('/'); Theme.Apply(this); }
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

    // ---- tree ---------------------------------------------------------------------------------------------
    static readonly object Placeholder = "ph";
    string SportId => _host.Types[_sports.SelectedIndex].Id;

    TreeNode Node(string text, object? tag, string? flagCode = null)
    {
        var key = _flags.Key(flagCode);
        return new TreeNode(text) { Tag = tag, ImageKey = key, SelectedImageKey = key };
    }

    TreeNode WithPlaceholder(TreeNode n) { n.Nodes.Add(Node("Loading...", Placeholder)); return n; }

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
                foreach (var e in await _bf.ListEventsAsync(SportId, null, text, _inPlay.Checked)) _tree.Nodes.Add(EventTreeNode(e, null));
            else
                foreach (var c in await _bf.ListCompetitionsAsync(SportId, _inPlay.Checked))
                    _tree.Nodes.Add(WithPlaceholder(CompetitionNode_(c, SportId, $"{c.Name} ({c.MarketCount})")));
            SyncFavorites();
            _ = RefreshVolumes();
            await AutoExpandRoots();
            Say(_tree.Nodes.Count <= 1 ? "Nothing found" : $"{_tree.Nodes.Count} {(text != null ? "matches" : "competitions")}");
        }
        catch (Exception ex) { Say(ex.Message); }
    }

    /// <summary>A competition node; Name remembers its sport so favorites load correctly after the sport is switched.</summary>
    TreeNode CompetitionNode_(CompetitionNode c, string sportId, string text)
    {
        var n = Node(text, c, Flags.CodeFor(c.Region, null, c.Name));
        n.Name = sportId;
        return n;
    }

    TreeNode EventTreeNode(EventNode e, string? inheritedFlag)
    {
        var n = WithPlaceholder(Node("", e, Flags.CodeFor(null, e.CountryCode, null) ?? inheritedFlag));
        n.Text = _host.EventLabel(e, null);
        return n;
    }

    async Task RefreshVolumes()
    {
        if (await _host.RefreshVolumes(EventNodes(_tree.Nodes).Select(n => ((EventNode)n.Tag!).Id))) RefreshScores();
    }

    // ---- favorites ----------------------------------------------------------------------------------------
    EventNode? SelectedEvent() => _tree.SelectedNode switch
    {
        { Tag: EventNode e } => e,
        { Tag: MarketCatalogEntry, Parent.Tag: EventNode e } => e,
        _ => null,
    };

    /// <summary>Stars on matches and the Favorites section follow the saved list (also when the web UI changed it).</summary>
    void SyncFavorites()
    {
        _favSig = _host.FavoritesSignature;
        var old = _tree.Nodes.Cast<TreeNode>().FirstOrDefault(x => x.Tag is string t && t == "favorites");
        bool wasOpen = old?.IsExpanded ?? true;
        old?.Remove();
        var root = Node($"★ Favorites ({_opt.Favorites.Count + _opt.FavoriteCompetitions.Count})", "favorites");
        root.NodeFont = new Font(_tree.Font, FontStyle.Bold);
foreach (var fc in _opt.FavoriteCompetitions)
            root.Nodes.Add(WithPlaceholder(CompetitionNode_(new CompetitionNode(fc.CompetitionId, fc.Name, fc.Region, 0), fc.SportId, "★ " + fc.Name)));
        foreach (var f in _opt.Favorites) root.Nodes.Add(EventTreeNode(new EventNode(f.EventId, f.Name, null, null, 0), null));
        if (_opt.Favorites.Count == 0 && _opt.FavoriteCompetitions.Count == 0) root.Nodes.Add(Node("(right-click a match or competition, or select it and press ★ Favorite)", "hint"));
        _tree.Nodes.Insert(0, root);
        if (wasOpen) root.Expand();
        RefreshScores();
    }


    /// <summary>When a level has exactly one real choice, open it, and keep going down while each level has a single choice.</summary>
    async Task AutoExpandSingle(TreeNode n)
    {
        var kids = n.Nodes.Cast<TreeNode>().Where(k => k.Tag != Placeholder).ToList();
        if (kids.Count != 1) return;
        var k = kids[0];
        if (k.Tag is not (CompetitionNode or EventNode)) return;     // markets are leaves
        k.Expand(); await ExpandLoad(k);
        await AutoExpandSingle(k);
    }

    async Task AutoExpandRoots()
    {
        var roots = _tree.Nodes.Cast<TreeNode>().Where(x => x.Tag is CompetitionNode or EventNode).ToList();
        if (roots.Count != 1) return;
        roots[0].Expand(); await ExpandLoad(roots[0]);
        await AutoExpandSingle(roots[0]);
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
            {
                var flag = Flags.CodeFor(c.Region, null, c.Name);
                var sport = string.IsNullOrEmpty(n.Name) ? SportId : n.Name;
                kids.AddRange((await _bf.ListEventsAsync(sport, c.Id, null, _inPlay.Checked)).Select(e => EventTreeNode(e, flag)));
            }
            else if (n.Tag is EventNode e)
                foreach (var m in await _bf.ListEventMarketsAsync(e.Id))
                    kids.Add(Node(m.TotalMatched > 0 ? $"{m.MarketName}   [{Liquidity.Short(m.TotalMatched.Value)}]" : m.MarketName, m));
            n.Nodes.Clear();
            foreach (var k in kids) n.Nodes.Add(k);
            if (n.Tag is CompetitionNode) _ = RefreshVolumes();
            _ = AutoExpandSingle(n);
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
        _host.ShowLadder(m, this);
    }

    IEnumerable<TreeNode> EventNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode n in nodes)
        {
            if (n.Tag is EventNode) yield return n;
            else if (n.Tag is CompetitionNode || n.Tag is string t && t == "favorites") foreach (var c in EventNodes(n.Nodes)) yield return c;
        }
    }

    /// <summary>Game labels in the tree, the banner over the selected market, and the connection indicator.</summary>
    void RefreshScores()
    {
        if (_host.FavoritesSignature != _favSig) SyncFavorites();
        _host.UpdateLiveText();
        _live.Text = _host.LiveText; _live.ForeColor = _host.LiveColour;
        _tree.BeginUpdate();
        foreach (var n in EventNodes(_tree.Nodes))
        {
            var e = (EventNode)n.Tag!;
            var m = _host.Scores.Url == null ? null : _host.Scores.Find(e.Name);
            var text = _host.EventLabel(e, m);
            if (n.Text != text) n.Text = text;
            n.NodeFont = m != null ? (_boldFont ??= new Font(_tree.Font, FontStyle.Bold)) : null;
        }
        _tree.EndUpdate();
        var sel = _market?.Event?.Name == null || _host.Scores.Url == null ? null : _host.Scores.Find(_market.Event.Name);
        if (sel == null) { _scoreLbl.Height = 0; return; }
        bool flash = DateTime.UtcNow < _flashUntil;
        _scoreLbl.Height = 30;
        _scoreLbl.Text = $"{sel.Home}  {sel.HomeScore} - {sel.AwayScore}  {sel.Away}";
        _scoreLbl.BackColor = flash ? Color.Gold : Color.FromArgb(18, 21, 26);
        _scoreLbl.ForeColor = flash ? Color.Black : Color.White;
    }

    // ---- selected market ----------------------------------------------------------------------------------
    async Task OnPick(TreeNode n)
    {
        if (n.Tag is not MarketCatalogEntry m) return;
        MirrorState.Select(m.MarketId);
        _market = m; _book = null; _selected = null; RefreshScores();
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
            if (_smoke && _book != null)
                Smoke($"OK market={_market!.MarketName} runners={_runners.Rows.Count} ladderRows={_ladder.RowCount} delayed={_book.IsMarketDataDelayed}", 0);
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
        var back = Theme.Cell(BackBlue, true); var lay = Theme.Cell(LayPink, true);
        _runners.Rows.Clear();
        foreach (var r in _book.Runners)
        {
            var name = _market.Runners?.FirstOrDefault(x => x.SelectionId == r.SelectionId)?.RunnerName ?? r.SelectionId.ToString();
            var b = r.Ex?.AvailableToBack; var l = r.Ex?.AvailableToLay;
            int i = _runners.Rows.Add(name, Px(At(b, 2)), Px(At(b, 1)), Px(At(b, 0)),
                Px(At(l, 0)), Px(At(l, 1)), Px(At(l, 2)), r.LastPriceTraded?.ToString("0.##"), r.TotalMatched?.ToString("0"));
            var row = _runners.Rows[i]; row.Tag = r.SelectionId; row.Height = 40;
            for (int c = 1; c <= 3; c++) row.Cells[c].Style.BackColor = back;
            for (int c = 4; c <= 6; c++) row.Cells[c].Style.BackColor = lay;
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
        var backCol = Theme.Cell(BackBlue, true); var layCol = Theme.Cell(LayPink, true);
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
            row.Cells[1].Style.BackColor = backCol; row.Cells[3].Style.BackColor = layCol;
            row.Cells[2].Style.Font = new Font(Font, FontStyle.Bold);
            if (Math.Abs(price - (r.LastPriceTraded ?? 0)) < 1e-9) { row.Cells[2].Style.BackColor = Color.Gold; row.Cells[2].Style.ForeColor = Color.Black; }
        }
    }

    async Task LadderClick(DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _market == null || _selected == null || (e.ColumnIndex != 1 && e.ColumnIndex != 3)) return;
        double price = (double)_ladder.Rows[e.RowIndex].Tag!;
        string side = e.ColumnIndex == 1 ? "BACK" : "LAY";
        double size = (double)_stake.Value;
        var runner = _market.Runners?.FirstOrDefault(x => x.SelectionId == _selected)?.RunnerName;
        var q = $"{side} {runner} @ {price:0.##} for {size:0.00}?" + (_book?.IsMarketDataDelayed == true ? "\n\nPrices are DELAYED (delayed app key): the real market may have moved." : "");
        if (MessageBox.Show(q, "Confirm bet", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            var rep = await _bf.PlaceOrderAsync(_market.MarketId, new PlaceInstruction(_selected.Value, side, price, size));
            Say($"Order {rep.Status} {rep.ErrorCode} matched={rep.SizeMatched}");
        }
        catch (BetfairException ex) { Say(ex.Message); MessageBox.Show(ex.Message + "\nA delayed app key cannot place bets.", "Order failed"); }
    }
}
