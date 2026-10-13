using System.Text.Json;
using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>
/// The Classic main window: a narrow navigator (balance row, sport header, Today/All filters, country > competition > match > market
/// tree with flags). Double-click a match to open its ladders.
/// </summary>
sealed class ClassicMainForm : Form
{
    enum When { Today, Tomorrow, Next2Days }
    enum Scope { All, InPlay, Favorites }

    readonly AppHost _host;
    readonly BetfairClient _bf;
    readonly Options _opt = Options.Load();
    readonly bool _smoke, _privacy, _openLadder;
    readonly string? _search;

    readonly Panel _info = new() { Dock = DockStyle.Top, Height = 24, BackColor = Cl.Light };
    readonly Panel _sportHead = new() { Dock = DockStyle.Top, Height = 30, BackColor = Cl.Light, Cursor = Cursors.Hand };
    readonly Panel _filters = new() { Dock = DockStyle.Top, Height = 24, BackColor = Cl.Light };
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 18, Font = Cl.Ui, BackColor = Cl.Light, Padding = new Padding(3, 3, 0, 0), AutoEllipsis = true };
    readonly TreeView _tree = new()
    {
        Dock = DockStyle.Fill, ShowLines = true, ShowRootLines = true, HideSelection = false, Font = Cl.Ui, BackColor = Cl.Light,
        BorderStyle = BorderStyle.None, ItemHeight = 18, DrawMode = TreeViewDrawMode.OwnerDrawText, FullRowSelect = false,
    };
    readonly ImageList _images = new() { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
    readonly Dictionary<TreeNode, Task> _loads = new();
    readonly System.Windows.Forms.Timer _scoreTimer = new() { Interval = 2000 }, _volTimer = new() { Interval = 20000 }, _keepAlive = new() { Interval = 15 * 60 * 1000 };

    Dictionary<string, string> _countryNames = new();
    Dictionary<string, string> _iso3 = new();
    When _when = When.Today;
    Scope _scope = Scope.InPlay;     // opens on what is live right now
    string _balance = "", _currencyFlag = "";
    string _favSig = "";
    Font? _bold;

    int SportIndex;
    string SportName => _host.Types.Count > 0 ? _host.Types[SportIndex].Name : "Soccer";
    string SportId => _host.Types[SportIndex].Id;

    public ClassicMainForm(BetfairClient bf, bool smoke, bool privacy, bool openLadder, string? search)
    {
        _bf = bf; _smoke = smoke; _privacy = privacy; _openLadder = openLadder; _search = search;
        _host = new AppHost(bf, _opt, privacy, Ui.Classic, this) { SmokeMode = smoke };
        Text = "Murdoc's Toy";
        Font = Cl.Ui; BackColor = Cl.Light;
        ClientSize = new Size(285, 820); MinimumSize = new Size(260, 420);
        StartPosition = FormStartPosition.Manual; Location = new Point(40, 40);
        Theme.TitleBar(this, Cl.BarTop, Color.White);
        LoadNames();

        var nav = new ClassicSection("Market Navigator", 0) { Dock = DockStyle.Fill };
        nav.Header.Collapsible = false;
        nav.Header.HelpClicked += ShowHelpMenu;
        nav.Body.Controls.Add(_tree); nav.Body.Controls.Add(_filters); nav.Body.Controls.Add(_sportHead);
        nav.Body.Controls.Add(_status);
        Controls.Add(nav); Controls.Add(_info);

        _tree.ImageList = _images;
        _info.Paint += PaintInfo; _sportHead.Paint += PaintSportHead; _filters.Paint += PaintFilters;
        _sportHead.MouseDown += (_, e) => SportMenu(e.Location);
        _filters.MouseDown += FilterClick;
        _tree.DrawNode += DrawNode;
        _tree.BeforeExpand += (_, e) => { _ = ExpandLoad(e.Node!); };
        _tree.NodeMouseDoubleClick += async (_, e) => { if (e.Node.Tag is MarketCatalogEntry || e.Node.Tag is EventNode) { _tree.SelectedNode = e.Node; await OpenLadder(); } };
        _tree.NodeMouseClick += (_, e) => { if (e.Button == MouseButtons.Right) { _tree.SelectedNode = e.Node; NodeMenu(e.Node, e.Location); } };

        _host.Status += s => _status.Text = s;
        _host.FavoritesChanged += SyncFavorites;
        _scoreTimer.Tick += (_, _) => RefreshLabels();
        _volTimer.Tick += async (_, _) => await RefreshVolumes();
        _keepAlive.Tick += async (_, _) => { try { await _bf.KeepAliveAsync(); } catch { } };
        FormClosed += (_, _) => _host.Dispose();
        Shown += async (_, _) => await Startup();
    }

    void LoadNames()
    {
        T Read<T>(string name) where T : new()
        {
            try { using var s = typeof(ClassicMainForm).Assembly.GetManifestResourceStream($"web/flags/{name}.json"); return s == null ? new T() : JsonSerializer.Deserialize<T>(s) ?? new T(); }
            catch { return new T(); }
        }
        _countryNames = Read<Dictionary<string, string>>("names");
        _iso3 = Read<Dictionary<string, string>>("iso3");
    }

    // ---- startup -------------------------------------------------------------------------------------------
    async Task Startup()
    {
        try
        {
            _host.Types = await _bf.ListEventTypesAsync();
            SportIndex = Math.Max(0, _host.Types.FindIndex(t => t.Name == "Soccer"));
            _host.StartScores(); _host.StartMirror(); _host.StartFavoriteCompetitions();
            _scoreTimer.Start(); _volTimer.Start(); _keepAlive.Start();
            _ = LoadAccount();
            await LoadTree();
            if (_smoke || _openLadder) await OpenFirstLadder();
        }
        catch (Exception ex) { _status.Text = ex.Message; if (_smoke) _host.Smoke("FAIL " + ex.Message, 1); }
    }

    async Task LoadAccount()
    {
        try
        {
            var d = await _bf.GetAccountDetailsAsync();
            _currencyFlag = Cl.CurrencyFlag(d.CurrencyCode) ?? "";
            _host.CurrencyCode = d.CurrencyCode;
            if (_privacy) _balance = Cl.Symbol(d.CurrencyCode) + "0,00";
            else
            {
                var f = await _bf.GetAccountFundsAsync();
                _balance = Cl.Symbol(d.CurrencyCode) + f.AvailableToBetBalance.ToString("0.00");
            }
        }
        catch (Exception) { _balance = ""; }
        _info.Invalidate();
    }

    // ---- painted rows ----------------------------------------------------------------------------------------
    void PaintInfo(object? s, PaintEventArgs e)
    {
        var g = e.Graphics;
        using var line = new Pen(Cl.Border); g.DrawRectangle(line, 0, 0, _info.Width - 1, _info.Height - 1);
        int x = 6;
        if (_currencyFlag != "" && Flags.Get(_currencyFlag) != null) { g.DrawImage(Flags.Icon(_currencyFlag, new Size(16, 12)), x, 6); x += 22; }
        TextRenderer.DrawText(g, _balance, Cl.Name, new Point(x, 4), Color.Black);
        var who = _opt.RelayUrl == "" ? "" : "";
        var right = _host.LiveText != "" ? _host.LiveText : "";
        var sz = TextRenderer.MeasureText(right, Cl.UiBold);
        TextRenderer.DrawText(g, right, Cl.UiBold, new Point(_info.Width - sz.Width - 6, 5), _host.LiveColour);
    }

    void PaintSportHead(object? s, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImage(ClIcons.Sport(SportName), 5, 6);
        TextRenderer.DrawText(g, SportName == "Soccer" ? "Soccer Matches" : SportName, Cl.SportHead, new Point(26, 4), Color.Black);
        Arrow(g, _sportHead.Width - 12, 13);
    }

    static void Arrow(Graphics g, int x, int y) => g.FillPolygon(Brushes.Black, new[] { new Point(x - 3, y - 1), new Point(x + 3, y - 1), new Point(x, y + 2) });

    Rectangle _whenRect, _scopeRect;

    void PaintFilters(object? s, PaintEventArgs e)
    {
        var g = e.Graphics; int w = _filters.Width;
        _whenRect = new Rectangle(0, 0, w * 5 / 12, _filters.Height); _scopeRect = new Rectangle(_whenRect.Right, 0, w - _whenRect.Width, _filters.Height);
        using var edge = new Pen(Color.FromArgb(0xC0, 0xC0, 0xC0));
        g.DrawLine(edge, 0, 0, w, 0); g.DrawLine(edge, _scopeRect.X, 0, _scopeRect.X, _filters.Height);
        g.DrawImage(ClIcons.Calendar, 5, 4);
        var whenText = _when switch { When.Today => "Today", When.Tomorrow => "Tomorrow", _ => "2 days" };
        TextRenderer.DrawText(g, whenText, Cl.Filter, new Point(26, 3), _scope == Scope.InPlay ? Color.Gray : Color.Black);   // the date filter does not apply to in-play
        Arrow(g, _whenRect.Right - 10, 12);
        g.DrawImage(ClIcons.Pitch, _scopeRect.X + 6, 4);
        TextRenderer.DrawText(g, _scope switch { Scope.InPlay => "In-play", Scope.Favorites => "Favorites", _ => "All" }, Cl.Filter, new Point(_scopeRect.X + 28, 3), Color.Black);
        Arrow(g, w - 10, 12);
    }

    void FilterClick(object? s, MouseEventArgs e)
    {
        var menu = new ContextMenuStrip();
        if (_whenRect.Contains(e.Location))
        {
            foreach (var (w, label) in new[] { (When.Today, "Today"), (When.Tomorrow, "Tomorrow"), (When.Next2Days, "Next 2 days") })
            {
                var item = new ToolStripMenuItem(label) { Checked = _when == w };
                item.Click += async (_, _) => { _when = w; if (_scope == Scope.InPlay) _scope = Scope.All; _filters.Invalidate(); await LoadTree(); };
                menu.Items.Add(item);
            }
            menu.Show(_filters, new Point(_whenRect.X, _whenRect.Bottom));
        }
        else if (_scopeRect.Contains(e.Location))
        {
            foreach (var (sc, label) in new[] { (Scope.All, "All"), (Scope.InPlay, "In-play"), (Scope.Favorites, "Favorites") })
            {
                var item = new ToolStripMenuItem(label) { Checked = _scope == sc };
                item.Click += async (_, _) => { _scope = sc; _filters.Invalidate(); await LoadTree(); };
                menu.Items.Add(item);
            }
            menu.Show(_filters, new Point(_scopeRect.X, _scopeRect.Bottom));
        }
    }

    void SportMenu(Point at)
    {
        var menu = new ContextMenuStrip();
        for (int i = 0; i < _host.Types.Count; i++)
        {
            int idx = i;
            var item = new ToolStripMenuItem(_host.Types[i].Name) { Checked = idx == SportIndex, Image = ClIcons.Sport(_host.Types[i].Name) };
            item.Click += async (_, _) => { SportIndex = idx; _sportHead.Invalidate(); await LoadTree(); };
            menu.Items.Add(item);
        }
        menu.Show(_sportHead, new Point(0, _sportHead.Height));
    }

    void ShowHelpMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Options...", null, (_, _) => { using var f = new OptionsForm(_opt); if (f.ShowDialog(this) == DialogResult.OK) { _opt.Save(); _host.StartScores(); } });
        if (_host.Web != null) menu.Items.Add($"Open web mirror ({_host.Web.Url})", null, (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_host.Web.Url) { UseShellExecute = true }));
        var auto = new ToolStripMenuItem("Auto-open favorites on score change") { Checked = _opt.AutoOpenOnScore, CheckOnClick = true };
        auto.CheckedChanged += (_, _) => { _opt.AutoOpenOnScore = auto.Checked; _opt.Save(); };
        menu.Items.Add(auto);
        menu.Show(Cursor.Position);
    }

    void NodeMenu(TreeNode n, Point at)
    {
        if (n.Tag is CompetitionNode comp)
        {
            var cm = new ContextMenuStrip();
            var sport = string.IsNullOrEmpty(n.Name) ? SportId : n.Name;
            cm.Items.Add(_host.IsFavoriteCompetition(comp.Id) ? "Remove competition from favorites" : "Add competition to favorites", null, (_, _) => _host.ToggleFavoriteCompetition(comp, sport));
            cm.Show(_tree, at);
            return;
        }
        if (SelectedEvent() is not { } ev) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add(_host.IsFavorite(ev.Id) ? "Remove from favorites" : "Add to favorites", null, (_, _) => _host.ToggleFavorite(ev));
        menu.Items.Add("Open ladder", null, async (_, _) => await OpenLadder());
        menu.Show(_tree, at);
    }

    EventNode? SelectedEvent() => _tree.SelectedNode switch
    {
        { Tag: EventNode e } => e,
        { Tag: MarketCatalogEntry, Parent.Tag: EventNode e } => e,
        _ => null,
    };

    void DrawNode(object? s, DrawTreeNodeEventArgs e)
    {
        if (e.Node == null || e.Bounds.Width <= 0) return;
        bool sel = (e.State & TreeNodeStates.Selected) != 0;
        var font = e.Node.NodeFont ?? _tree.Font;
        // the tree measures bold nodes with the regular font, so size the highlight and text from the real font
        var size = TextRenderer.MeasureText(e.Graphics, e.Node.Text, font, new Size(int.MaxValue, e.Bounds.Height), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        var r = new Rectangle(e.Bounds.X, e.Bounds.Y, size.Width + 4, e.Bounds.Height);
        using (var bg = new SolidBrush(sel ? Cl.TreeSelect : _tree.BackColor)) e.Graphics.FillRectangle(bg, r);
        TextRenderer.DrawText(e.Graphics, e.Node.Text, font, r, Color.Black, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    // ---- images ---------------------------------------------------------------------------------------------
    string Img(string? flagCode, string icon)
    {
        string key = flagCode ?? icon;
        if (_images.Images.ContainsKey(key)) return key;
        if (flagCode != null)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp)) g.DrawImage(Flags.Icon(flagCode, new Size(16, 12)), 0, 2);
            _images.Images.Add(key, bmp);
        }
        else _images.Images.Add(key, icon switch { "ball" => ClIcons.Ball, "pitch" => ClIcons.Pitch, "market" => ClIcons.Market, _ => new Bitmap(16, 16) });
        return key;
    }

    TreeNode Node(string text, object? tag, string? flagCode, string icon, bool bold = false)
    {
        var key = Img(flagCode, icon);
        var n = new TreeNode(text) { Tag = tag, ImageKey = key, SelectedImageKey = key };
        if (bold) n.NodeFont = _bold ??= new Font(_tree.Font, FontStyle.Bold);
        return n;
    }

    TreeNode WithPlaceholder(TreeNode n) { n.Nodes.Add(Node("Loading...", Placeholder, null, "none")); return n; }
    static readonly object Placeholder = "ph";

    // ---- tree -----------------------------------------------------------------------------------------------
    (DateTime? From, DateTime? To) Window()
    {
        var midnight = DateTime.Today;
        return _when switch
        {
            When.Today => (midnight.ToUniversalTime(), midnight.AddDays(1).ToUniversalTime()),
            When.Tomorrow => (midnight.AddDays(1).ToUniversalTime(), midnight.AddDays(2).ToUniversalTime()),
            _ => (null, null),
        };
    }

    /// <summary>Country for a competition: its region, with the UK split into its four nations by the competition name.</summary>
    (string Name, string? Flag) Country(CompetitionNode c)
    {
        var r = (c.Region ?? "").Trim();
        if (r.Equals("International", StringComparison.OrdinalIgnoreCase) || r == "")
            return ("International", Flags.CodeFor("International", null, c.Name));
        var flag = Flags.CodeFor(r, null, c.Name);
        if (flag is "gb-eng") return ("England", flag);
        if (flag is "gb-sct") return ("Scotland", flag);
        if (flag is "gb-wls") return ("Wales", flag);
        if (flag is "gb-nir") return ("Northern Ireland", flag);
        return (_countryNames.TryGetValue(r.ToUpperInvariant(), out var n) ? n : r, flag);
    }

    async Task LoadTree()
    {
        if (_host.Types.Count == 0) return;
        try
        {
            _status.Text = "Loading...";
            _loads.Clear(); _tree.Nodes.Clear();
            if (_scope != Scope.Favorites)
            {
                var (from, to) = Window();
                var comps = await _bf.ListCompetitionsAsync(SportId, _scope == Scope.InPlay, from, to);
                foreach (var grp in comps.GroupBy(c => Country(c).Name).OrderBy(g => g.Key == "International" ? "~" : g.Key))
                {
                    var flag = Country(grp.First()).Flag;
                    var country = Node(grp.Key, "country", flag, "none");
                    foreach (var c in grp.OrderBy(c => c.Name))
                        country.Nodes.Add(WithPlaceholder(CompetitionTreeNode(c, SportId)));
                    _tree.Nodes.Add(country);
                }
            }
            SyncFavorites();
            _ = RefreshVolumes();
            _status.Text = _tree.Nodes.Count <= 1 && _scope != Scope.Favorites ? (_scope == Scope.InPlay ? "Nothing in-play right now" : "Nothing found") : "";
            RefreshLabels();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    TreeNode CompetitionTreeNode(CompetitionNode c, string sportId, bool star = false)
    {
        var n = Node((star ? "★ " : "") + c.Name, c, null, "ball", bold: true);
        n.Name = sportId;                                  // the sport this competition belongs to
        return n;
    }

    TreeNode EventTreeNode(EventNode e, string? flag) =>
        WithPlaceholder(Node(_host.EventLabel(e, null, withTime: true, shortTime: true), e, null, "pitch"));

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
                var (from, to) = Window();
                var sport = string.IsNullOrEmpty(n.Name) ? SportId : n.Name;
                kids.AddRange((await _bf.ListEventsAsync(sport, c.Id, null, _scope == Scope.InPlay, from, to)).Select(e => EventTreeNode(e, null)));
            }
            else if (n.Tag is EventNode e)
                foreach (var m in await _bf.ListEventMarketsAsync(e.Id)) kids.Add(Node(m.MarketName, m, null, "market"));
            n.Nodes.Clear();
            foreach (var k in kids) n.Nodes.Add(k);
            if (n.Tag is CompetitionNode) _ = RefreshVolumes();
            RefreshLabels();
        }
        catch (Exception ex) { _status.Text = ex.Message; n.Nodes.Clear(); WithPlaceholder(n); _loads.Remove(n); }
    }

    IEnumerable<TreeNode> EventNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode n in nodes)
        {
            if (n.Tag is EventNode) yield return n;
            else if (n.Tag is CompetitionNode || n.Tag is string) foreach (var c in EventNodes(n.Nodes)) yield return c;
        }
    }

    void SyncFavorites()
    {
        _favSig = _host.FavoritesSignature;
        var old = _tree.Nodes.Cast<TreeNode>().FirstOrDefault(x => x.Tag is string t && t == "favorites");
        bool wasOpen = old?.IsExpanded ?? true;
        old?.Remove();
        var root = Node($"★ Favorites ({_opt.Favorites.Count + _opt.FavoriteCompetitions.Count})", "favorites", null, "none", bold: true);
        foreach (var fc in _opt.FavoriteCompetitions)
            root.Nodes.Add(WithPlaceholder(CompetitionTreeNode(new CompetitionNode(fc.CompetitionId, fc.Name, fc.Region, 0), fc.SportId, star: true)));
        foreach (var f in _opt.Favorites) root.Nodes.Add(EventTreeNode(new EventNode(f.EventId, f.Name, null, null, 0), null));
        if (_opt.Favorites.Count == 0 && _opt.FavoriteCompetitions.Count == 0) root.Nodes.Add(Node("(right-click a match or competition > Add to favorites)", "hint", null, "none"));
        _tree.Nodes.Insert(0, root);
        if (wasOpen) root.Expand();
        RefreshLabels();
    }

    async Task RefreshVolumes()
    {
        if (await _host.RefreshVolumes(EventNodes(_tree.Nodes).Select(n => ((EventNode)n.Tag!).Id))) RefreshLabels();
    }

    /// <summary>"[time] Home 0 - 1 Away (145 643 €)" on every match in the tree, and the live-scores indicator.</summary>
    void RefreshLabels()
    {
        if (_host.FavoritesSignature != _favSig) { SyncFavorites(); return; }
        _host.UpdateLiveText();
        _info.Invalidate();
        _tree.BeginUpdate();
        foreach (var n in EventNodes(_tree.Nodes))
        {
            var e = (EventNode)n.Tag!;
            var m = _host.Scores.Url == null ? null : _host.Scores.Find(e.Name);
            var text = _host.EventLabel(e, m, withTime: true, shortTime: true);
            if (n.Text != text) n.Text = text;
        }
        _tree.EndUpdate();
    }

    // ---- ladders --------------------------------------------------------------------------------------------
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
        if (m == null) { _status.Text = "Select a match or market first"; return; }
        _host.ShowLadder(m, this);
    }

    /// <summary>Opens the first match found (used by --smoke / --open-ladder).</summary>
    async Task OpenFirstLadder()
    {
        if (_scope == Scope.InPlay && !_tree.Nodes.Cast<TreeNode>().Any(x => x.Tag is string t && t == "country"))
        { _scope = Scope.All; _filters.Invalidate(); await LoadTree(); }     // nothing live right now: fall back to the day's matches
        TreeNode? node = _tree.Nodes.Cast<TreeNode>().FirstOrDefault(x => x.Tag is string t && t == "country");
        if (!string.IsNullOrEmpty(_search))
        {
            var found = await _bf.ListEventsAsync(SportId, null, _search, false);
            var ev = found.FirstOrDefault();
            if (ev != null) { var ms = await _bf.ListEventMarketsAsync(ev.Id); if (ms.Count > 0) { _host.ShowLadder(ms[0], this); return; } }
        }
        if (node == null) return;
        node.Expand();
        var comp = node.Nodes.Cast<TreeNode>().FirstOrDefault(x => x.Tag is CompetitionNode);
        if (comp == null) return;
        comp.Expand(); await ExpandLoad(comp);
        var evNode = comp.Nodes.Cast<TreeNode>().FirstOrDefault(x => x.Tag is EventNode);
        if (evNode == null) return;
        evNode.Expand(); await ExpandLoad(evNode);
        _tree.SelectedNode = evNode;
        await OpenLadder();
    }
}
