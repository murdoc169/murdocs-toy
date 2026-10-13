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
        var markets = await c.ListEventMarketsAsync(events[0].Id);
        Assert.NotEmpty(markets);
        Assert.Equal("Match Odds", markets[0].MarketName);
    }
}
