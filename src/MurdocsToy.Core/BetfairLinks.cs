namespace MurdocsToy.Core;

/// <summary>Addresses of pages on betfair.com. Live video is part of those pages (for accounts that qualify), not of the API.</summary>
public static class BetfairLinks
{
    static readonly Dictionary<string, string> Slugs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Soccer"] = "football", ["Horse Racing"] = "horse-racing", ["Greyhound Racing"] = "greyhound-racing",
        ["Rugby Union"] = "rugby-union", ["Rugby League"] = "rugby-league", ["American Football"] = "american-football",
        ["Ice Hockey"] = "ice-hockey", ["Gaelic Games"] = "gaelic-games", ["Mixed Martial Arts"] = "mixed-martial-arts",
    };

    /// <summary>The market's page on the Betfair exchange website, e.g. https://www.betfair.com/exchange/plus/football/market/1.2345.</summary>
    public static string MarketPage(string marketId, string? sportName)
    {
        var name = (sportName ?? "Soccer").Trim();
        var slug = Slugs.TryGetValue(name, out var s) ? s : name.ToLowerInvariant().Replace(' ', '-');
        return $"https://www.betfair.com/exchange/plus/{slug}/market/{Uri.EscapeDataString(marketId)}";
    }
}
