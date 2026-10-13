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
        _info.Paint += PaintInfo;
        _info.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) InfoMenu(e.Location); else if (e.Button == MouseButtons.Left && _rulesBadge.Contains(e.Location)) OpenSettings("Automation rules"); };
        _host.Rules.Changed += () => { if (!IsDisposed && IsHandleCreated) BeginInvoke(() => _info.Invalidate()); };
        new ToolTip { InitialDelay = 400, AutoPopDelay = 20000 }.SetToolTip(_info, "Right-click for settings and automation rules."); _sportHead.Paint += PaintSportHead; _filters.Paint += PaintFilters;
        _sportHead.MouseDown += (_, e) => SportMenu(e.Location);
        _filters.MouseDown += FilterClick;
        _tree.DrawNode += DrawNode;
        _tree.BeforeExpand += (_, e) => { _ = ExpandLoad(e.Node!); };
        _tree.AfterExpand += (_, e) => { _ = AutoExpandSingle(e.Node!); };
        _tree.NodeMouseDoubleClick += async (_, e) => { if (e.Node.Tag is MarketCatalogEntry || e.Node.Tag is EventNode) { _tree.SelectedNode = e.Node; await OpenLadder(); } };
        _tree.NodeMouseClick += async (_, e) =>
        {
            if (e.Button == MouseButtons.Right) { _tree.SelectedNode = e.Node; NodeMenu(e.Node, e.Location); return; }
            // one click on a match (or one of its markets) opens its ladder; the +/- expander only expands
            if (e.Button == MouseButtons.Left && e.Node.Tag is EventNode or MarketCatalogEntry
                && _tree.HitTest(e.Location).Location is TreeViewHitTestLocations.Label or TreeViewHitTestLocations.Image)
            {
                _tree.SelectedNode = e.Node;
                await OpenLadder();
            }
        };

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
            SportIndex = Math.Max(0, _host.Types.FindIndex(t => t.Name == (Environment.GetEnvironmentVariable("MT_TEST_SPORT") is { Length: > 0 } sp ? sp : "Soccer")));   // MT_TEST_SPORT: test seam
            _host.StartScores(); _host.StartMirror(); _host.StartFavoriteCompetitions();
            _scoreTimer.Start(); _volTimer.Start(); _keepAlive.Start();
            _ = LoadAccount();
            await LoadTree();
            // test seams only (not features): MT_TEST_ARM_RULES=1 turns the rules ON without the dialog; MT_OPEN_SETTINGS=<tab> opens Settings
            if (Environment.GetEnvironmentVariable("MT_TEST_ARM_RULES") == "1") _host.Rules.Arm();
            if (_smoke || _openLadder) await OpenFirstLadder();
            if (Environment.GetEnvironmentVariable("MT_OPEN_SETTINGS") is { Length: > 0 } tab) BeginInvoke(() => OpenSettings(tab));
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
        _rulesBadge = Rectangle.Empty;
        if (_host.Rules.Armed)
        {
            _rulesBadge = new Rectangle(_info.Width / 2 - 70, 3, 76, 18);
            g.FillRectangle(Brushes.Firebrick, _rulesBadge);
            TextRenderer.DrawText(g, "RULES ON", Cl.UiBold, _rulesBadge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
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

    Rectangle _rulesBadge;

    void OpenSettings(string tab = "General")
    {
        using var f = new SettingsForm(_host, tab);
        f.ShowDialog(this);
        _info.Invalidate();
    }

    /// <summary>Right-click on the balance bar.</summary>
    void InfoMenu(Point at)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
        menu.Items.Add(_host.Rules.Armed ? "Automation rules... (ON)" : "Automation rules...", null, (_, _) => OpenSettings("Automation rules"));
        menu.Items.Add(new ToolStripSeparator());
        if (_host.Web != null) menu.Items.Add($"Open web mirror ({_host.Web.Url})", null, (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_host.Web.Url) { UseShellExecute = true }));
        menu.Items.Add("Forget saved login", null, (_, _) => { LoginStore.Forget(); _status.Text = "Saved password removed."; });
        menu.Show(_info, at);
    }

    void ShowHelpMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
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
        menu.Items.Add(_host.IsFavorite(ev.Id) ? "Remove from favorites" : "Add to favorites", null, (_, _) => _host.ToggleFavorite(ev, SportId));
        menu.Items.Add("Open ladder", null, async (_, _) => await OpenLadder());
        menu.Items.Add("Open card only", null, async (_, _) => await OpenLadder(cardOnly: true));
        menu.Show(_tree, at);
    }

    EventNode? SelectedEvent() => _tree.SelectedNode switch
    {
        { Tag: EventNode e } => e,
        { Tag: MarketCatalogEntry, Parent.Tag: EventNode e } => e,
        { Tag: MarketCatalogEntry, Parent.Tag: MarketCatalogEntry, Parent.Parent.Tag: EventNode e } => e,
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
        else _images.Images.Add(key, icon switch { "ball" => ClIcons.Ball, "pitch" => ClIcons.Pitch, "race" => ClIcons.Race, "market" => ClIcons.Market, _ => new Bitmap(16, 16) });
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
            bool fellBack = false;
            if (_scope != Scope.Favorites)
            {
                var (from, to) = Window();
                bool built = await BuildTree(_scope == Scope.InPlay, from, to);
                if (!built && _scope == Scope.InPlay)       // nothing live right now: show the day's schedule instead
                {
                    _scope = Scope.All; _filters.Invalidate();
                    (from, to) = Window();
                    fellBack = built = await BuildTree(false, from, to);
                }
            }
            SyncFavorites();
            _ = RefreshVolumes();
            await AutoExpandRoots();
            _status.Text = fellBack ? "Nothing in-play right now: showing the schedule" : _tree.Nodes.Count <= 1 && _scope != Scope.Favorites ? "Nothing found" : "";
            RefreshLabels();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    /// <summary>
    /// Country > competition for team sports. Racing has no competitions (races sit under meeting events with a country), so
    /// there it is country > meeting > race. Returns false when there is nothing to show.
    /// </summary>
    async Task<bool> BuildTree(bool inPlay, DateTime? from, DateTime? to)
    {
        var comps = await _bf.ListCompetitionsAsync(SportId, inPlay, from, to);
        if (comps.Count > 0)
        {
            foreach (var grp in comps.GroupBy(c => Country(c).Name).OrderBy(g => g.Key == "International" ? "~" : g.Key))
            {
                var flag = Country(grp.First()).Flag;
                var country = Node(grp.Key, "country", flag, "none");
                foreach (var c in grp.OrderBy(c => c.Name)) country.Nodes.Add(WithPlaceholder(CompetitionTreeNode(c, SportId)));
                _tree.Nodes.Add(country);
            }
            return true;
        }
        var meetings = await _bf.ListEventsAsync(SportId, null, null, inPlay, from, to);
        if (meetings.Count == 0) return false;
        var alpha3 = _iso3.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);       // alpha-2 -> alpha-3
        foreach (var grp in meetings.GroupBy(e => (e.CountryCode ?? "").ToLowerInvariant()).OrderBy(g => g.Key == "" ? "~" : g.Key))
        {
            var name = grp.Key != "" && alpha3.TryGetValue(grp.Key, out var a3) && _countryNames.TryGetValue(a3, out var cn) ? cn : (grp.Key == "" ? "Other" : grp.Key.ToUpperInvariant());
            var country = Node(name, "country", Flags.CodeFor(null, grp.Key == "" ? null : grp.Key, null), "none");
            foreach (var e in grp.OrderBy(e => e.OpenDate).ThenBy(e => e.Name)) country.Nodes.Add(EventTreeNode(e, null, "race", plain: true));
            _tree.Nodes.Add(country);
        }
        return true;
    }

    TreeNode CompetitionTreeNode(CompetitionNode c, string sportId, bool star = false)
    {
        var n = Node((star ? "★ " : "") + c.Name, c, null, "ball", bold: true);
        n.Name = sportId;                                  // the sport this competition belongs to
        return n;
    }

    TreeNode EventTreeNode(EventNode e, string? flag, string icon = "pitch", bool plain = false) =>
        WithPlaceholder(Node(plain ? e.Name : _host.EventLabel(e, null, withTime: true, shortTime: true), e, null, icon));


    /// <summary>When a level has exactly one real choice, open it, and keep going down while each level has a single choice.</summary>
    async Task AutoExpandSingle(TreeNode n)
    {
        var kids = n.Nodes.Cast<TreeNode>().Where(k => k.Tag != Placeholder).ToList();
        if (kids.Count != 1) return;
        var k = kids[0];
        if (k.Tag is not (CompetitionNode or EventNode) && !(k.Tag is string t && t == "country")) return;     // markets are leaves
        k.Expand(); await ExpandLoad(k);
        await AutoExpandSingle(k);
    }

    async Task AutoExpandRoots()
    {
        var roots = _tree.Nodes.Cast<TreeNode>().Where(x => x.Tag is CompetitionNode or EventNode || x.Tag is string t && t == "country").ToList();
        if (roots.Count != 1) return;
        roots[0].Expand(); await ExpandLoad(roots[0]);
        await AutoExpandSingle(roots[0]);
    }

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
            {
                var markets = await _bf.ListEventMarketsAsync(e.Id);
                bool racing = markets.Any(m => m.EventType?.Name is "Horse Racing" or "Greyhound Racing");
                if (!racing) foreach (var m in markets) kids.Add(Node(m.MarketName, m, null, "market"));
                else
                    // one node per race (its time and name); the race's other markets (To Be Placed, Each Way...) sit inside it
                    foreach (var race in markets.Where(m => m.MarketStartTime != null).GroupBy(m => m.MarketStartTime).OrderBy(g => g.Key))
                    {
                        var main = race.FirstOrDefault(m => !RaceSideMarket(m.MarketName)) ?? race.First();
                        var node = Node($"{race.Key!.Value.ToLocalTime():HH:mm}  {main.MarketName}", main, null, "race");
                        foreach (var side in race.Where(m => m != main).OrderBy(m => m.MarketName)) node.Nodes.Add(Node(side.MarketName, side, null, "market"));
                        kids.Add(node);
                    }
            }
            n.Nodes.Clear();
            foreach (var k in kids) n.Nodes.Add(k);
            if (n.Tag is CompetitionNode) _ = RefreshVolumes();
            RefreshLabels();
            _ = AutoExpandSingle(n);
        }
        catch (Exception ex) { _status.Text = ex.Message; n.Nodes.Clear(); WithPlaceholder(n); _loads.Remove(n); }
    }

    /// <summary>The markets that sit beside a race's main (win) market.</summary>
    static bool RaceSideMarket(string name) =>
        name.StartsWith("To Be Placed", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Each Way", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Match Bet", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Without", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Forecast", StringComparison.OrdinalIgnoreCase) || name.Contains("Place", StringComparison.OrdinalIgnoreCase) && !name.Contains(' ') ;

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
        var comps = _opt.FavoriteCompetitions.Where(c => c.SportId == SportId).ToList();      // favourites belong to the sport shown
        var events = _opt.Favorites.Where(f => (f.SportId ?? "1") == SportId).ToList();
        var root = Node($"★ Favorites ({events.Count + comps.Count})", "favorites", null, "none", bold: true);
        foreach (var fc in comps)
            root.Nodes.Add(WithPlaceholder(CompetitionTreeNode(new CompetitionNode(fc.CompetitionId, fc.Name, fc.Region, 0), fc.SportId, star: true)));
        foreach (var f in events) root.Nodes.Add(EventTreeNode(new EventNode(f.EventId, f.Name, null, null, 0), null));
        if (events.Count == 0 && comps.Count == 0) root.Nodes.Add(Node("(right-click a match or competition > Add to favorites)", "hint", null, "none"));
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
            if (n.ImageKey == "race") continue;          // a race meeting is just its name
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

    async Task OpenLadder(bool cardOnly = false)
    {
        var m = await ResolveMarket(_tree.SelectedNode);
        if (m == null) { _status.Text = "Select a match or market first"; return; }
        _host.ShowLadder(m, this, cardOnly);
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
        TreeNode? evNode;
        if (comp != null) { comp.Expand(); await ExpandLoad(comp); evNode = comp.Nodes.Cast<TreeNode>().FirstOrDefault(x => x.Tag is EventNode); }
        else evNode = node.Nodes.Cast<TreeNode>().FirstOrDefault(x => x.Tag is EventNode);      // racing: meetings sit directly under the country
        if (evNode == null) return;
        evNode.Expand(); await ExpandLoad(evNode);
        _tree.SelectedNode = evNode;
        await OpenLadder();
    }
}
