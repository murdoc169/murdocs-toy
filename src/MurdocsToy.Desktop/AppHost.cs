using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>
/// Everything the app does that is not specific to one window: live scores, favorites, match volumes, the web mirror and the
/// registry of open ladders. The Modern and Classic main windows are two views over the same host.
/// </summary>
sealed class AppHost : IDisposable
{
    public BetfairClient Bf { get; }
    public Options Opt { get; }
    public bool Privacy { get; }
    public string UiMode { get; }
    public LiveScoreFeed Scores { get; } = new();
    public RuleEngine Rules { get; }
    public WebMirror? Web { get; private set; }
    public List<EventType> Types { get; set; } = new();
    public Dictionary<string, double> Volumes { get; } = new();
    public string CurrencyCode { get; set; } = "EUR";
    public bool SmokeMode { get; set; }
    public string LiveText { get; private set; } = "";
    public Color LiveColour { get; private set; } = Color.Gray;

    /// <summary>A message for the window's status line.</summary>
    public event Action<string>? Status;
    public event Action? FavoritesChanged;
    /// <summary>A goal/VAR alert from the relay (already on the UI thread).</summary>
    public event Action<LiveAlert>? AlertReceived;

    readonly Control _ui;                                     // the main window, used to hop to the UI thread
    readonly Dictionary<string, Form> _ladders = new();
    readonly Dictionary<string, MarketCatalogEntry> _matchOdds = new();
    bool _volBusy;

    public AppHost(BetfairClient bf, Options opt, bool privacy, string uiMode, Control ui)
    {
        Bf = bf; Opt = opt; Privacy = privacy; UiMode = uiMode; _ui = ui;
        Rules = new RuleEngine(this);
        Scores.ScoreChanged += (m, ph, pa) => OnUi(() => { Rules.OnScoreChanged(m, ph, pa); OnScoreChanged(m, ph, pa); });
        Scores.Alert += a => OnUi(() => { Status?.Invoke(a.Text); AlertReceived?.Invoke(a); });
    }

    void OnUi(Action a) { if (!_ui.IsDisposed && _ui.IsHandleCreated) _ui.BeginInvoke(a); }
    public void Say(string s) => Status?.Invoke(s);

    // ---- live scores --------------------------------------------------------------------------------------
    /// <summary>Connects to the relay named in Options (MT_RELAY_URL overrides it); empty means off.</summary>
    public void StartScores()
    {
        var url = Environment.GetEnvironmentVariable("MT_RELAY_URL");
        if (string.IsNullOrWhiteSpace(url)) url = Opt.RelayUrl;
        Scores.Stop();
        if (string.IsNullOrWhiteSpace(url)) { LiveText = ""; return; }
        Scores.Start(url.Trim());
        LiveText = "Live scores: connecting..."; LiveColour = Color.Gray;
    }

    public void UpdateLiveText()
    {
        if (Scores.Url == null) return;
        LiveText = Scores.Connected ? $"Live scores: {Scores.Matches.Count} matches" : "Live scores: offline (retrying)";
        LiveColour = Scores.Connected ? Color.SeaGreen : Color.Firebrick;
    }

    // ---- web mirror ---------------------------------------------------------------------------------------
    public void StartMirror()
    {
        if (!Opt.MirrorToWeb) return;
        Web = new WebMirror(Bf, Opt, Privacy, Scores);
        if (!Web.Start()) { Web.Dispose(); Web = null; Say("Could not start the web mirror (ports 8080-8089 busy)"); }
    }

    // ---- favorites ----------------------------------------------------------------------------------------
    public bool IsFavorite(string eventId) => Opt.Favorites.Any(f => f.EventId == eventId);
    public string FavoritesSignature => string.Join(",", Opt.Favorites.Select(f => f.EventId)) + "|" + string.Join(",", Opt.FavoriteCompetitions.Select(f => f.CompetitionId));

    public void RaiseFavoritesChanged() => FavoritesChanged?.Invoke();

    public bool IsFavoriteCompetition(string competitionId) => Opt.FavoriteCompetitions.Any(f => f.CompetitionId == competitionId);

    public void ToggleFavoriteCompetition(CompetitionNode c, string sportId)
    {
        var list = Opt.FavoriteCompetitions.ToList();
        if (list.RemoveAll(f => f.CompetitionId == c.Id) == 0) list.Add(new FavoriteCompetition(c.Id, c.Name, sportId, c.Region));
        Opt.FavoriteCompetitions = list; Opt.Save();
        Say(IsFavoriteCompetition(c.Id) ? $"Added competition to favorites: {c.Name}" : $"Removed competition from favorites: {c.Name}");
        _ = RefreshCompetitionEvents();
        FavoritesChanged?.Invoke();
    }

    // matches of the favorite competitions, so a goal in any of them can open a ladder
    List<EventNode> _compEvents = new();
    readonly Dictionary<string, MarketCatalogEntry> _open = new();
    /// <summary>Matches in the starred competitions (refreshed every few minutes).</summary>
    public IReadOnlyList<EventNode> CompetitionEvents => _compEvents;
    /// <summary>Matches that currently have a ladder window open.</summary>
    public IEnumerable<EventNode> OpenEvents => _open.Values.Where(m => m.Event != null).Select(m => new EventNode(m.Event!.Id, m.Event.Name, null, null, 0)).ToList();
    readonly System.Windows.Forms.Timer _compTimer = new() { Interval = 3 * 60 * 1000 };

    public async Task RefreshCompetitionEvents()
    {
        try
        {
            var all = new List<EventNode>();
            foreach (var c in Opt.FavoriteCompetitions.ToList())
                all.AddRange(await Bf.ListEventsAsync(c.SportId, c.CompetitionId));
            _compEvents = all;
        }
        catch (Exception) { /* try again on the next tick */ }
    }

    public void StartFavoriteCompetitions() { _ = RefreshCompetitionEvents(); _compTimer.Tick += async (_, _) => await RefreshCompetitionEvents(); _compTimer.Start(); }

    public void ToggleFavorite(EventNode ev, string? sportId = null)
    {
        var list = Opt.Favorites.ToList();               // replaced as a whole: the web mirror may read it from another thread
        if (list.RemoveAll(f => f.EventId == ev.Id) == 0) list.Add(new FavoriteEvent(ev.Id, ev.Name, sportId));
        Opt.Favorites = list; Opt.Save();
        Say(IsFavorite(ev.Id) ? $"Added to favorites: {ev.Name}" : $"Removed from favorites: {ev.Name}");
        FavoritesChanged?.Invoke();
    }

    // ---- labels and volumes -------------------------------------------------------------------------------
    /// <summary>"[star] [dd/MM HH:mm] Porto 0 - 1 Benfica (145 643 €)": live score between the teams, match volume after.</summary>
    public string EventLabel(EventNode e, LiveMatch? live, bool withTime = true, bool shortTime = false)
    {
        var parts = e.Name.Split(" v ", 2, StringSplitOptions.TrimEntries);
        string game = live != null && parts.Length == 2 ? $"{parts[0]} {live.HomeScore} - {live.AwayScore} {parts[1]}"
                    : live != null ? $"{e.Name} [{live.ScoreText}]" : e.Name;
        string when = "";
        if (withTime && live == null && e.OpenDate != null)
        {
            var local = e.OpenDate.Value.ToLocalTime();
            when = local.ToString(shortTime && local.Date == DateTime.Today ? "HH:mm  " : "dd/MM HH:mm  ");
        }
        string vol = Volumes.TryGetValue(e.Id, out var v) && v > 0 ? $" ({Liquidity.Spaced(v)} €)" : "";
        return (IsFavorite(e.Id) ? "★ " : "") + when + game + vol;
    }

    /// <summary>Matched volume of the given matches, fetched in a few batched calls.</summary>
    public async Task<bool> RefreshVolumes(IEnumerable<string> eventIds)
    {
        if (_volBusy) return false;
        _volBusy = true;
        try
        {
            var ids = eventIds.Distinct().ToList();
            if (ids.Count == 0) return false;
            foreach (var kv in await Bf.GetEventVolumesAsync(ids)) Volumes[kv.Key] = kv.Value;
            return true;
        }
        catch (Exception) { return false; }               // volumes are decoration: try again next tick
        finally { _volBusy = false; }
    }

    // ---- ladders ------------------------------------------------------------------------------------------
    Form CreateLadder(MarketCatalogEntry market) => UiMode == Ui.Classic
        ? new ClassicLadderForm(this, market)
        : new LadderForm(Bf, market, Opt, Privacy, Scores);

    /// <summary>Opens the ladder window for a market, or raises it if it is already open.</summary>
    public void ShowLadder(MarketCatalogEntry market, Form owner, bool cardOnly = false)
    {
        if (_ladders.TryGetValue(market.MarketId, out var open) && !open.IsDisposed)
        {
            if (open is ClassicLadderForm cl) { cl.Raise(); return; }
            if (open.WindowState == FormWindowState.Minimized) open.WindowState = FormWindowState.Normal;
            open.Activate(); open.BringToFront();
            return;
        }
        var f = CreateLadder(market);
        _ladders[market.MarketId] = f; _open[market.MarketId] = market;
        f.FormClosed += (_, _) => { _ladders.Remove(market.MarketId); _open.Remove(market.MarketId); };
        f.Show(owner);
        if (cardOnly && f is ClassicLadderForm c) c.StartAsCard();
    }

    /// <summary>A score changed on the relay: if it is a favorite and auto-open is on, show that match's ladder.</summary>
    void OnScoreChanged(LiveMatch m, int prevHome, int prevAway)
    {
        if (!Opt.AutoOpenOnScore) return;
        var fav = Opt.Favorites.FirstOrDefault(f => TeamMatcher.Score(f.Name, m.Home, m.Away) > 0);
        if (fav != null) { _ = OpenFavoriteLadder(fav, m, prevHome, prevAway); return; }
        var ev = _compEvents.FirstOrDefault(e => TeamMatcher.Score(e.Name, m.Home, m.Away) > 0);
        if (ev != null) _ = OpenFavoriteLadder(new FavoriteEvent(ev.Id, ev.Name), m, prevHome, prevAway);
    }

    async Task OpenFavoriteLadder(FavoriteEvent fav, LiveMatch m, int prevHome, int prevAway)
    {
        try
        {
            if (!_matchOdds.TryGetValue(fav.EventId, out var market))
            {
                market = (await Bf.ListEventMarketsAsync(fav.EventId)).FirstOrDefault();
                if (market == null) return;
                _matchOdds[fav.EventId] = market;
            }
            ShowLadder(market, _ui as Form ?? Form.ActiveForm ?? new Form());
            Say($"Score change {m.Home} {prevHome}-{prevAway} -> {m.ScoreText} {m.Away}: ladder opened");
        }
        catch (Exception ex) { Say(ex.Message); }
    }

    /// <summary>Writes the smoke-test result next to the exe and exits (used by --smoke).</summary>
    public void Smoke(string message, int code)
    {
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke.txt"), message);
        Environment.Exit(code);
    }

    public void Dispose() { Rules.Disarm("closing"); _compTimer.Stop(); Web?.Dispose(); Scores.Dispose(); }
}
