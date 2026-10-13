namespace MurdocsToy.Core;

/// <summary>Profit/loss exposure of one runner from matched bets, and the bet that evens it out.</summary>
public record Position(double IfWins, double IfLoses)
{
    public static Position Of(IEnumerable<CurrentOrder> orders, long selectionId)
    {
        double win = 0, lose = 0;
        foreach (var o in orders.Where(o => o.SelectionId == selectionId && o.SizeMatched > 0))
        {
            double price = o.AveragePriceMatched > 0 ? o.AveragePriceMatched : o.Price;
            if (o.Side == "BACK") { win += o.SizeMatched * (price - 1); lose -= o.SizeMatched; }
            else { win -= o.SizeMatched * (price - 1); lose += o.SizeMatched; }
        }
        return new Position(Math.Round(win, 2), Math.Round(lose, 2));
    }

    /// <summary>Bet at <paramref name="price"/> that leaves the same profit whether the runner wins or loses; null if already level.</summary>
    public (string Side, double Size)? GreenUp(double price)
    {
        double h = (IfLoses - IfWins) / price;
        double size = Math.Round(Math.Abs(h), 2);
        if (size < 0.01) return null;
        return (h > 0 ? "BACK" : "LAY", size);
    }

    /// <summary>The equal profit after greening up at <paramref name="price"/>.</summary>
    public double LockedProfit(double price)
    {
        var g = GreenUp(price);
        if (g == null) return IfWins;
        var (side, size) = g.Value;
        return Math.Round(side == "BACK" ? IfLoses - size : IfLoses + size, 2);
    }
}
