using System.Globalization;

namespace MurdocsToy.Core;

public record LiquiditySample(DateTime Time, double Matched, double AvailBack, double AvailLay);

public record LiquiditySnapshot(double Matched, double AvailBack, double AvailLay,
    double BackBookPct, double LayBookPct, double MatchedPerMin);

/// <summary>Market liquidity from a market book, and a rolling history of it.</summary>
public static class Liquidity
{
    /// <summary>Money waiting on the book: sum of every offer on active runners.</summary>
    public static LiquiditySample Sample(MarketBook book, DateTime now)
    {
        double back = 0, lay = 0;
        foreach (var r in (book.Runners ?? new()).Where(r => r.Status == "ACTIVE"))
        {
            back += (r.Ex?.AvailableToBack ?? new()).Sum(p => p.Size);
            lay += (r.Ex?.AvailableToLay ?? new()).Sum(p => p.Size);
        }
        return new LiquiditySample(now, book.TotalMatched ?? 0, Math.Round(back, 2), Math.Round(lay, 2));
    }

    /// <summary>Sum of 1/price over best prices, as a percentage. Back book is normally under 100, lay book over.</summary>
    public static double BookPct(MarketBook book, bool back)
    {
        double sum = 0;
        foreach (var r in (book.Runners ?? new()).Where(r => r.Status == "ACTIVE"))
        {
            var best = (back ? r.Ex?.AvailableToBack : r.Ex?.AvailableToLay)?.FirstOrDefault();
            if (best == null || best.Price <= 0) return 0;      // incomplete book: no meaningful percentage
            sum += 1 / best.Price;
        }
        return Math.Round(sum * 100, 1);
    }

    /// <summary>Whole units with a space between thousands: 302 293 or 1 302 212.</summary>
    public static string Spaced(double v) => ((long)Math.Round(v)).ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');

    public static string Short(double v) =>
        v >= 1e6 ? (v / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + "m"
        : v >= 1e3 ? (v / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + "k" : v.ToString("0", CultureInfo.InvariantCulture);
}

public sealed class LiquidityTracker
{
    readonly List<LiquiditySample> _s = new();
    public int MaxSamples { get; init; } = 900;
    public TimeSpan RateWindow { get; init; } = TimeSpan.FromSeconds(60);
    public IReadOnlyList<LiquiditySample> Samples => _s;
    LiquiditySnapshot? _last;

    public LiquiditySnapshot Add(MarketBook book, DateTime now)
    {
        var sample = Liquidity.Sample(book, now);
        _s.Add(sample);
        if (_s.Count > MaxSamples) _s.RemoveAt(0);
        return _last = new LiquiditySnapshot(sample.Matched, sample.AvailBack, sample.AvailLay,
            Liquidity.BookPct(book, true), Liquidity.BookPct(book, false), RatePerMin(now));
    }

    public LiquiditySnapshot? Last => _last;

    /// <summary>Matched volume added per minute over the rate window (needs two samples).</summary>
    public double RatePerMin(DateTime now)
    {
        if (_s.Count < 2) return 0;
        var latest = _s[^1];
        var start = _s.FirstOrDefault(x => x.Time >= now - RateWindow) ?? _s[0];
        double minutes = (latest.Time - start.Time).TotalMinutes;
        return minutes <= 0 ? 0 : Math.Round(Math.Max(0, latest.Matched - start.Matched) / minutes, 2);
    }
}
