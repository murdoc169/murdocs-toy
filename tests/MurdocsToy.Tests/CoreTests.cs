using MurdocsToy.Core;
using Xunit;

public class LadderTests
{
    [Theory]
    [InlineData(1.99, 2.0)] [InlineData(2.0, 2.02)] [InlineData(2.98, 3.0)] [InlineData(3.0, 3.05)]
    [InlineData(5.9, 6.0)] [InlineData(6.0, 6.2)] [InlineData(19.5, 20.0)] [InlineData(20, 21)]
    [InlineData(1000, 1000)]
    public void Up(double from, double to) => Assert.Equal(to, PriceLadder.Up(from));

    [Theory]
    [InlineData(2.0, 1.99)] [InlineData(3.0, 2.98)] [InlineData(6.0, 5.9)] [InlineData(20, 19.5)]
    [InlineData(1.01, 1.01)] [InlineData(1000, 990)]
    public void Down(double from, double to) => Assert.Equal(to, PriceLadder.Down(from));

    [Fact] public void SnapRounds() => Assert.Equal(2.02, PriceLadder.Snap(2.019));

    [Fact]
    public void WindowDescending()
    {
        var w = PriceLadder.Window(3.0, 3, 3).ToList();
        Assert.Equal(7, w.Count);
        Assert.Equal(3.0, w[3]);
        Assert.True(w[0] > w[6]);
    }
}

/// <summary>Live test against the real API; runs only when BETFAIR_* env vars are set.</summary>
public class LiveTests
{
    [Fact]
    public async Task LoginAndReadMarkets()
    {
        var u = Environment.GetEnvironmentVariable("BETFAIR_USERNAME");
        var p = Environment.GetEnvironmentVariable("BETFAIR_PASSWORD");
        var k = Environment.GetEnvironmentVariable("BETFAIR_APP_KEY");
        if (u == null || p == null || k == null) return;
        using var c = new BetfairClient(k);
        await c.LoginAsync(u, p);
        var types = await c.ListEventTypesAsync();
        Assert.Contains(types, t => t.Name == "Soccer");
        var cat = await c.ListMarketCatalogueAsync(types.First(t => t.Name == "Soccer").Id, marketTypeCode: "MATCH_ODDS", max: 3);
        var books = await c.ListMarketBookAsync(cat.Select(m => m.MarketId));
        Assert.Equal(cat.Count, books.Count);
    }
}

public class PositionTests
{
    static CurrentOrder O(string side, double price, double matched) =>
        new("1", "m", 7, side, "EXECUTION_COMPLETE", price, matched, matched, 0, price);

    [Fact]
    public void BackThenLayBackOutLocksProfit()
    {
        var pos = Position.Of(new[] { O("BACK", 4.0, 10) }, 7);
        Assert.Equal(30, pos.IfWins); Assert.Equal(-10, pos.IfLoses);
        var (side, size) = pos.GreenUp(3.0)!.Value;      // price shortened: lay to lock a profit
        Assert.Equal("LAY", side); Assert.Equal(13.33, size);
        Assert.Equal(3.33, pos.LockedProfit(3.0));
    }

    [Fact]
    public void LayPositionGreensUpWithBack()
    {
        var pos = Position.Of(new[] { O("LAY", 2.0, 10) }, 7);
        Assert.Equal(-10, pos.IfWins); Assert.Equal(10, pos.IfLoses);
        var (side, size) = pos.GreenUp(2.5)!.Value;
        Assert.Equal("BACK", side); Assert.Equal(8, size);
        Assert.Equal(2, pos.LockedProfit(2.5));
    }

    [Fact] public void LevelPositionNeedsNoHedge() => Assert.Null(new Position(5, 5).GreenUp(2.0));
    [Fact] public void OtherRunnersIgnored() => Assert.Equal(new Position(0, 0), Position.Of(new[] { O("BACK", 2, 5) }, 99));
}

public class WindowEdgeTests
{
    [Theory] [InlineData(1.01)] [InlineData(1.02)] [InlineData(1000)] [InlineData(3.0)]
    public void AlwaysFullAndDistinct(double centre)
    {
        var w = PriceLadder.Window(centre, 10, 10).ToList();
        Assert.Equal(21, w.Count);
        Assert.Equal(w.Count, w.Distinct().Count());
        Assert.Equal(w.OrderByDescending(x => x), w);
        Assert.InRange(w.Last(), 1.01, 1000); Assert.InRange(w.First(), 1.01, 1000);
    }
}

public class LiveInPlayTests
{
    [Fact]
    public async Task InPlayFilterWorksForEverySportAndHasNoStartWindow()
    {
        var u = Environment.GetEnvironmentVariable("BETFAIR_USERNAME");
        var p = Environment.GetEnvironmentVariable("BETFAIR_PASSWORD");
        var k = Environment.GetEnvironmentVariable("BETFAIR_APP_KEY");
        if (u == null || p == null || k == null) return;
        using var c = new BetfairClient(k);
        await c.LoginAsync(u, p);
        foreach (var sport in await c.ListEventTypesAsync())
        {
            var comps = await c.ListCompetitionsAsync(sport.Id, inPlayOnly: true);   // must not throw for any sport
            var events = await c.ListEventsAsync(sport.Id, null, null, inPlayOnly: true);
            Assert.True(comps.Count == 0 || events.Count > 0, sport.Name);
        }
    }
}

public class LiveTreeTests
{
    [Fact]
    public async Task SportToCompetitionToEventToMarkets()
    {
        var u = Environment.GetEnvironmentVariable("BETFAIR_USERNAME");
        var p = Environment.GetEnvironmentVariable("BETFAIR_PASSWORD");
        var k = Environment.GetEnvironmentVariable("BETFAIR_APP_KEY");
        if (u == null || p == null || k == null) return;
        using var c = new BetfairClient(k);
        await c.LoginAsync(u, p);
        var soccer = (await c.ListEventTypesAsync()).First(t => t.Name == "Soccer");
        var comps = await c.ListCompetitionsAsync(soccer.Id);
        Assert.NotEmpty(comps);
        var comp = comps.OrderByDescending(x => x.MarketCount).First();
        var events = await c.ListEventsAsync(soccer.Id, comp.Id);
        Assert.NotEmpty(events);
        // the Today/Tomorrow filters: every event returned must start inside the requested window
        var from = DateTime.Today.AddDays(1).ToUniversalTime(); var to = DateTime.Today.AddDays(2).ToUniversalTime();
        var tomorrow = await c.ListEventsAsync(soccer.Id, null, null, false, from, to);
        Assert.NotEmpty(tomorrow);
        Assert.All(tomorrow, e => Assert.InRange(e.OpenDate!.Value.ToUniversalTime(), from, to));

        var markets = await c.ListEventMarketsAsync(events[0].Id);
        Assert.NotEmpty(markets);
        Assert.Equal("Match Odds", markets[0].MarketName);

        // per-match volume for the whole level in one go
        var vols = await c.GetEventVolumesAsync(events.Select(e => e.Id));
        Assert.NotEmpty(vols);
        Assert.All(vols, kv => Assert.True(kv.Value > 0));
        var biggest = markets.Max(m => m.TotalMatched ?? 0);                          // live markets move between the two calls
        Assert.InRange(vols[events[0].Id], biggest * 0.9, biggest * 1.1 + 1);
    }
}

public class LiquidityTests
{
    static MarketBook Book(double matched, double backSize, double laySize, double bb = 2.0, double bl = 2.1) => new("1.1", "OPEN", false, false, matched, new()
    {
        new Runner(1, "ACTIVE", 2, 0, new ExchangePrices(new() { new(bb, backSize) }, new() { new(bl, laySize) }, null)),
        new Runner(2, "ACTIVE", 2, 0, new ExchangePrices(new() { new(bb, backSize) }, new() { new(bl, laySize) }, null)),
        new Runner(3, "REMOVED", 2, 0, new ExchangePrices(new() { new(9, 999) }, new() { new(9, 999) }, null)),
    });

    [Fact]
    public void SumsOnlyActiveRunners()
    {
        var s = Liquidity.Sample(Book(1000, 50, 70), DateTime.UtcNow);
        Assert.Equal(100, s.AvailBack); Assert.Equal(140, s.AvailLay); Assert.Equal(1000, s.Matched);
    }

    [Fact] public void BookPercentages()
    {
        Assert.Equal(100, Liquidity.BookPct(Book(0, 1, 1), true));            // 2 runners at 2.0
        Assert.Equal(95.2, Liquidity.BookPct(Book(0, 1, 1), false));          // 2 runners at 2.1
    }

    [Fact]
    public void MatchedRatePerMinute()
    {
        var t = new LiquidityTracker(); var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        t.Add(Book(1000, 1, 1), t0);
        var snap = t.Add(Book(1500, 1, 1), t0.AddSeconds(30));
        Assert.Equal(1000, snap.MatchedPerMin);                               // 500 in half a minute
        Assert.Equal(0, new LiquidityTracker().RatePerMin(t0));
    }

    [Fact] public void RateNeverNegativeAndHistoryBounded()
    {
        var t = new LiquidityTracker { MaxSamples = 3 }; var t0 = DateTime.UtcNow;
        for (int i = 0; i < 10; i++) t.Add(Book(1000 - i, 1, 1), t0.AddSeconds(i));
        Assert.Equal(3, t.Samples.Count); Assert.Equal(0, t.Last!.MatchedPerMin);
    }

    [Theory] [InlineData(0, "0")] [InlineData(950, "950")] [InlineData(302293, "302 293")] [InlineData(1302212, "1 302 212")] [InlineData(145643.6, "145 644")]
    public void SpacedThousands(double v, string s) => Assert.Equal(s, Liquidity.Spaced(v));

    [Theory] [InlineData(950, "950")] [InlineData(12500, "12.5k")] [InlineData(1_240_000, "1.2m")]
    public void ShortFormat(double v, string s) => Assert.Equal(s, Liquidity.Short(v));
}

public class LiveScoreTests
{
    // Real pairs: Betfair event name vs the relay's (SofaScore) team names.
    [Theory]
    [InlineData("Buriram Utd v Beijing Guoan", "Buriram United", "Beijing Guoan")]
    [InlineData("Cong An Nhan Dan v Kashima", "Công An Hà Nội", "Kashima Antlers")]
    [InlineData("Viking v Bayern Munich", "Viking FK", "Bayern München")]
    [InlineData("Moreirense FC U23 v Santa Clara U23", "Moreirense FC U23", "Santa Clara U23")]
    [InlineData("Galatasaray U19 v Barcelona U19", "Galatasaray U19", "Barcelona U19")]
    [InlineData("Man Utd v Arsenal", "Manchester United", "Arsenal FC")]
    public void SameMatchIsRecognised(string betfair, string home, string away) =>
        Assert.True(TeamMatcher.Score(betfair, home, away) > 0, betfair);

    [Theory]
    [InlineData("Arsenal v Lille", "Arsenal U19", "Lille U19")]                  // senior vs youth
    [InlineData("Arsenal v Lille", "Arsenal Women", "Lille Women")]
    [InlineData("Viking v Bayern Munich", "Bayern Munich", "Viking FK")]          // home/away swapped
    [InlineData("Arsenal v Chelsea", "Liverpool", "Everton")]
    [InlineData("Man Utd v Chelsea", "Manchester City", "Chelsea")]
    [InlineData("Real Madrid v Barcelona", "Real Sociedad", "Barcelona")]          // one team only
    public void DifferentMatchIsRejected(string betfair, string home, string away) =>
        Assert.Equal(0, TeamMatcher.Score(betfair, home, away));

    [Fact]
    public void SnapshotThenGoalUpdatesScore()
    {
        var f = new LiveScoreFeed();
        LiveAlert? got = null; f.Alert += a => got = a;
        f.Handle("{\"type\":\"snapshot\",\"matches\":[{\"sofa_event_id\":0,\"home_team\":\"Buriram United\",\"away_team\":\"Beijing Guoan\",\"home_score\":0,\"away_score\":0}]}");
        Assert.Equal("0-0", f.Find("Buriram Utd v Beijing Guoan")!.ScoreText);
        f.Handle("{\"type\":\"goal\",\"sofa_event_id\":4866014,\"home_team\":\"Buriram United\",\"away_team\":\"Beijing Guoan\",\"home_score\":1,\"away_score\":0,\"scoring_team\":\"home\"}");
        Assert.Equal("1-0", f.Find("Buriram Utd v Beijing Guoan")!.ScoreText);
        Assert.Equal("goal", got!.Kind); Assert.EndsWith("(Buriram United)", got.Text);
        Assert.Single(f.Matches);
    }

    [Fact]
    public void ScoreChangeFiresOncePerChangeAndNeverOnFirstSight()
    {
        var f = new LiveScoreFeed(); var seen = new List<string>();
        f.ScoreChanged += (m, ph, pa) => seen.Add($"{ph}-{pa}->{m.ScoreText}");
        string snap(int h, int a) => "{\"type\":\"snapshot\",\"matches\":[{\"home_team\":\"Viking FK\",\"away_team\":\"Bayern Munich\",\"home_score\":" + h + ",\"away_score\":" + a + "}]}";
        f.Handle(snap(0, 0));
        Assert.Empty(seen);                                                   // first sight
        f.Handle("{\"type\":\"goal\",\"home_team\":\"Viking FK\",\"away_team\":\"Bayern Munich\",\"home_score\":0,\"away_score\":1,\"scoring_team\":\"away\"}");
        f.Handle(snap(0, 1));                                                 // snapshot repeating the same score: no second event
        Assert.Equal(new[] { "0-0->0-1" }, seen);
        f.Handle(snap(0, 0));                                                 // goal disallowed by VAR
        Assert.Equal(new[] { "0-0->0-1", "0-1->0-0" }, seen);
        f.Handle(snap(0, 0)); Assert.Equal(2, seen.Count);
    }

    [Fact] public void BadMessagesAreIgnored()
    {
        var f = new LiveScoreFeed(); f.Handle("not json"); f.Handle("{}"); f.Handle("{\"type\":\"snapshot\"}");
        Assert.Empty(f.Matches); Assert.Null(f.Find("A v B"));
    }
}
