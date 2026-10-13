namespace OpenLadder.Core;

/// <summary>Betfair price increments (tick ladder).</summary>
public static class PriceLadder
{
    static readonly (double upTo, double step)[] Bands =
    {
        (2, 0.01), (3, 0.02), (4, 0.05), (6, 0.1), (10, 0.2),
        (20, 0.5), (30, 1), (50, 2), (100, 5), (1000, 10),
    };

    public const double Min = 1.01, Max = 1000;

    static double StepAt(double price)
    {
        foreach (var (upTo, step) in Bands) if (price < upTo - 1e-9) return step;
        return 10;
    }

    static double Round(double v) => Math.Round(v, 2);

    public static double Snap(double price)
    {
        price = Math.Clamp(price, Min, Max);
        double step = StepAt(price);
        return Round(Math.Round(price / step) * step);
    }

    public static double Up(double price)
    {
        price = Snap(price);
        return price >= Max ? Max : Snap(price + StepAt(price));
    }

    public static double Down(double price)
    {
        price = Snap(price);
        if (price <= Min) return Min;
        // stepping down from a band boundary uses the lower band's step
        double step = StepAt(price - 0.0001);
        return Snap(price - step);
    }

    public static IEnumerable<double> Window(double centre, int above, int below)
    {
        var list = new List<double>();
        double p = Snap(centre);
        for (int i = 0; i < above; i++) p = Up(p);
        for (int i = 0; i < above + below + 1; i++) { list.Add(p); p = Down(p); }
        return list; // descending prices, highest first
    }
}
