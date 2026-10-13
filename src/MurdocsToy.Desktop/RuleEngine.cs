using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>
/// Runs the automation rules: when the score relay reports a goal, every enabled rule whose scope contains that match finds the match's
/// market(s) by name, picks the selections it names, and places its bet. One master switch (always OFF at start-up), a cooldown per rule
/// and match, caps per match and per session, a stop after repeated failures, and no action on delayed prices unless allowed.
/// </summary>
sealed class RuleEngine
{
    // test seam only (not a feature): MT_TEST_NO_SEND=1 logs what would be placed instead of calling Betfair
    static readonly bool TestNoSend = Environment.GetEnvironmentVariable("MT_TEST_NO_SEND") == "1";

    readonly AppHost _host;
    readonly List<string> _log = new();
    readonly Dictionary<string, (DateTime Last, int Bets)> _state = new();
    readonly Dictionary<string, (DateTime At, List<MarketCatalogEntry> Markets)> _markets = new();
    readonly Dictionary<string, EventNode?> _anyEvents = new();
    int _total, _failures;

    public bool Armed { get; private set; }
    public IReadOnlyList<string> Log { get { lock (_log) return _log.ToList(); } }
    public event Action? Changed;

    public RuleEngine(AppHost host) => _host = host;
    Options Opt => _host.Opt;

    void Say(string line)
    {
        lock (_log) { _log.Add($"{DateTime.Now:HH:mm:ss}  {line}"); while (_log.Count > 200) _log.RemoveAt(0); }
        _host.Say("Rules: " + line);
        Changed?.Invoke();
    }

    public void Arm() { Armed = true; _total = 0; _failures = 0; _state.Clear(); Say("Rules ON"); }
    public void Disarm(string why) { if (!Armed) return; Armed = false; Say("Rules OFF: " + why); }

    /// <summary>Called for every score change the relay reports (on the UI thread).</summary>
    public void OnScoreChanged(LiveMatch m, int prevHome, int prevAway)
    {
        if (!Armed || !RuleLogic.IsGoal(prevHome, prevAway, m.HomeScore, m.AwayScore)) return;
        foreach (var rule in Opt.Rules.Where(r => r.Enabled).ToList()) _ = Fire(rule, m, prevHome, prevAway);
    }

    async Task Fire(AutomationRule rule, LiveMatch m, int ph, int pa)
    {
        try
        {
            var ev = await FindEvent(rule, m);
            if (ev == null) return;                                           // this match is not in the rule's scope
            var key = rule.Id + "|" + ev.Id;
            _state.TryGetValue(key, out var st);
            if ((DateTime.UtcNow - st.Last).TotalSeconds < rule.CooldownSeconds) { Say($"{rule.Name}: {ev.Name}: skipped (cooldown)"); return; }
            if (st.Bets >= rule.MaxBetsPerMatch) { Say($"{rule.Name}: {ev.Name}: skipped (already placed {st.Bets} bets here)"); return; }

            var markets = (await MarketsOf(ev.Id)).Where(mk => RuleLogic.MarketMatches(mk.MarketName, rule.MarketContains)).Take(3).ToList();
            if (markets.Count == 0) { Say($"{rule.Name}: {ev.Name}: no market called '{rule.MarketContains}'"); return; }
            _state[key] = (DateTime.UtcNow, st.Bets);
            Say($"{rule.Name}: goal in {ev.Name} ({m.HomeScore}-{m.AwayScore})");

            foreach (var mk in markets)
            {
                var book = await WhenOpen(mk.MarketId);
                if (book == null) { Say($"{rule.Name}: {mk.MarketName}: market not open, skipped"); continue; }
                if (book.IsMarketDataDelayed && !Opt.RulesAllowDelayed) { Say($"{rule.Name}: {mk.MarketName}: skipped, prices are delayed (allow it in Settings > Automation rules)"); continue; }

                var runners = (mk.Runners ?? new()).Select(r => (r.SelectionId, r.RunnerName));
                var ids = RuleLogic.Pick(runners, rule.Selections, m.HomeScore, m.AwayScore, ph, pa);
                if (ids.Count == 0) { Say($"{rule.Name}: {mk.MarketName}: no selection called '{RuleLogic.Expand(rule.Selections, m.HomeScore, m.AwayScore, ph, pa)}'"); continue; }
                var plan = AutoBet.Plan(book, ids, rule.BetSettings());
                if (plan.Count == 0) { Say($"{rule.Name}: {mk.MarketName}: no price to take on those selections"); continue; }

                _state.TryGetValue(key, out st);
                int room = rule.MaxBetsPerMatch - st.Bets;
                if (room < plan.Count) plan = plan.Take(Math.Max(0, room)).ToList();
                if (plan.Count == 0) continue;
                if (_total + plan.Count > Opt.RulesMaxBets) { Disarm($"session limit of {Opt.RulesMaxBets} bets reached"); return; }

                foreach (var bet in plan)
                {
                    _total++; _state[key] = (DateTime.UtcNow, st.Bets + 1); st = _state[key];
                    var name = (mk.Runners ?? new()).FirstOrDefault(r => r.SelectionId == bet.SelectionId)?.RunnerName ?? bet.SelectionId.ToString();
                    if (TestNoSend) { Say($"[test] would {bet.Side} {name} @ {bet.Price:0.##} size {bet.Size:0.00} in {mk.MarketName}"); continue; }
                    try
                    {
                        var rep = await _host.Bf.PlaceOrderAsync(mk.MarketId, new PlaceInstruction(bet.SelectionId, bet.Side, bet.Price, bet.Size, Opt.Persistence));
                        Say($"{bet.Side} {name} @ {bet.Price:0.##} x{bet.Size:0.00} in {mk.MarketName}: {rep.Status}{(rep.ErrorCode == null ? "" : " " + rep.ErrorCode)}");
                        _failures = rep.Status == "SUCCESS" ? 0 : _failures + 1;
                    }
                    catch (BetfairException ex) { _failures++; Say($"{bet.Side} {name} failed: {ex.Code}"); }
                    if (_failures >= 3) { Disarm("3 bets in a row failed"); return; }
                }
            }
        }
        catch (Exception ex) { Say($"{rule.Name}: error: {ex.Message}"); }
    }

    /// <summary>Waits (up to 90 s) for a suspended market to reopen, since Betfair refuses bets while it is suspended.</summary>
    async Task<MarketBook?> WhenOpen(string marketId)
    {
        var until = DateTime.UtcNow.AddSeconds(90);
        while (Armed)
        {
            var book = (await _host.Bf.ListMarketBookAsync(new[] { marketId })).FirstOrDefault();
            if (book?.Status == "OPEN") return book;
            if (book == null || book.Status == "CLOSED" || DateTime.UtcNow > until) return null;
            await Task.Delay(2000);
        }
        return null;
    }

    async Task<List<MarketCatalogEntry>> MarketsOf(string eventId)
    {
        if (_markets.TryGetValue(eventId, out var c) && (DateTime.UtcNow - c.At).TotalMinutes < 5) return c.Markets;
        var list = await _host.Bf.ListEventMarketsAsync(eventId);
        _markets[eventId] = (DateTime.UtcNow, list);
        return list;
    }

    /// <summary>The Betfair match this score update belongs to, if it is inside the rule's scope.</summary>
    async Task<EventNode?> FindEvent(AutomationRule rule, LiveMatch m)
    {
        bool Same(string eventName) => TeamMatcher.Score(eventName, m.Home, m.Away) > 0;
        switch (rule.Scope)
        {
            case "OPEN":
                return _host.OpenEvents.FirstOrDefault(e => Same(e.Name));
            case "ANY":
                var k = m.Home + "|" + m.Away;
                if (_anyEvents.TryGetValue(k, out var hit)) return hit;
                var word = m.Home.Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderByDescending(w => w.Length).FirstOrDefault() ?? m.Home;
                var found = (await _host.Bf.ListEventsAsync("1", null, word, inPlayOnly: true)).FirstOrDefault(e => Same(e.Name));
                return _anyEvents[k] = found;
            default:   // FAVORITES: starred matches, and matches in starred competitions
                var fav = Opt.Favorites.FirstOrDefault(f => Same(f.Name));
                if (fav != null) return new EventNode(fav.EventId, fav.Name, null, null, 0);
                return _host.CompetitionEvents.FirstOrDefault(e => Same(e.Name));
        }
    }
}
