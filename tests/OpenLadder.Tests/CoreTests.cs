using OpenLadder.Core;
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
