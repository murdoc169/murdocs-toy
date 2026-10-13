namespace MurdocsToy.Core;

/// <summary>What the automation should place when it fires.</summary>
/// <param name="Side">"BACK" or "LAY".</param>
/// <param name="Amount">Stake, or liability when <paramref name="AmountIsLiability"/> (lay only).</param>
/// <param name="Basis">"LAST" = start from the last traded price; "BEST" = start from the best price available to take (back: best back, lay: best lay).</param>
/// <param name="OffsetTicks">Moves the price along the Betfair price ladder from the basis: positive = higher odds, negative = lower odds, one tick per step.</param>
public record AutoBetSettings(string Side, double Amount, bool AmountIsLiability, string Basis, int OffsetTicks);

public record PlannedBet(long SelectionId, string Side, double Price, double Size)
{
    /// <summary>What can be lost if the selection goes the wrong way: the stake when backing, the liability when laying.</summary>
    public double Risk => Side == "BACK" ? Size : Size * (Price - 1);
}

/// <summary>Works out one bet per chosen selection from the current book. Pure, so it can be tested without any account.</summary>
public static class AutoBet
{
    public static List<PlannedBet> Plan(MarketBook book, IEnumerable<long> selections, AutoBetSettings s)
    {
        var wanted = selections.ToHashSet();
        var plan = new List<PlannedBet>();
        bool back = s.Side == "BACK";
        foreach (var r in book.Runners ?? new())
        {
            if (r.Status != "ACTIVE" || !wanted.Contains(r.SelectionId)) continue;
            double? best = (back ? r.Ex?.AvailableToBack : r.Ex?.AvailableToLay)?.FirstOrDefault()?.Price;
            double? basis = s.Basis == "LAST" ? (r.LastPriceTraded ?? best) : best;      // no last price yet: fall back to the best price
            if (basis == null || basis <= 1) continue;                                    // nothing to anchor the price on: skip this selection
            double price = PriceLadder.Snap(basis.Value);
            for (int i = 0; i < Math.Abs(s.OffsetTicks); i++) price = s.OffsetTicks > 0 ? PriceLadder.Up(price) : PriceLadder.Down(price);
            double size = !back && s.AmountIsLiability ? s.Amount / (price - 1) : s.Amount;
            size = Math.Round(size, 2);
            if (size < 0.01) continue;
            plan.Add(new PlannedBet(r.SelectionId, s.Side, price, size));
        }
        return plan;
    }

    /// <summary>The most that can be lost if every planned bet goes wrong.</summary>
    public static double WorstCase(IEnumerable<PlannedBet> plan) => Math.Round(plan.Sum(b => b.Risk), 2);
}
