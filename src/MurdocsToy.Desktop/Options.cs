using MurdocsToy.Core;
using System.Text.Json;

namespace MurdocsToy.Desktop;

/// <summary>A match the user starred.</summary>
/// <summary>SportId is empty on favorites saved before sports were told apart: those were all football.</summary>
public record FavoriteEvent(string EventId, string Name, string? SportId = null);

/// <summary>A competition the user starred (e.g. AFC Champions League, Primeira Liga); its matches count as favorites.</summary>
public record FavoriteCompetition(string CompetitionId, string Name, string SportId, string? Region);

/// <summary>User-editable settings, persisted to %APPDATA%\MurdocsToy\options.json.</summary>
public sealed class Options
{
    public List<double> StakePresets { get; set; } = new() { 2, 5, 10, 20, 50, 100 };
    public double DefaultStake { get; set; } = 2;
    public int RowHeight { get; set; } = 22;
    public int RefreshMs { get; set; } = 1000;
    public bool ConfirmBets { get; set; } = false;
    public string Persistence { get; set; } = "LAPSE";   // LAPSE | PERSIST | MARKET_ON_CLOSE
    public bool AlwaysOnTop { get; set; } = false;
    public bool AutoCentre { get; set; } = true;
    public List<FavoriteEvent> Favorites { get; set; } = new();
    public List<FavoriteCompetition> FavoriteCompetitions { get; set; } = new();
    public bool AutoOpenOnScore { get; set; } = false;   // open (or raise) a favorite match's ladder whenever its score changes
    // Classic interface staking
    public double ClassicBackStake { get; set; } = 2;
    public double ClassicLayStake { get; set; } = 10;
    public bool ClassicLayLiability { get; set; } = false;      // lay "stake" box means liability instead of stake
    public int ClassicScale { get; set; } = 150;
    // Classic automation (never saved as armed: it always starts off)
    public string AutoSide { get; set; } = "LAY";
    public double AutoAmount { get; set; } = 2;
    public bool AutoLiability { get; set; } = false;
    public bool AutoOnGoal { get; set; } = true;
    public bool AutoOnSuspend { get; set; } = false;
    public string AutoBasis { get; set; } = "LAST";      // LAST = last traded price, BEST = best price available
    public int AutoOffsetTicks { get; set; } = 0;        // ticks along the price ladder from the basis (+ higher odds, - lower odds)
    public int AutoCooldownSeconds { get; set; } = 60;
    public int AutoMaxBets { get; set; } = 20;
    public bool AutoAllowDelayed { get; set; } = false;

    // automation rules (the master switch is never saved: it always starts OFF)
    public List<AutomationRule> Rules { get; set; } = new();
    public bool RulesAllowDelayed { get; set; } = false;
    public int RulesMaxBets { get; set; } = 30;
    public string RelayUrl { get; set; } = "";   // live-score relay, e.g. ws://relay-host:9001 (empty = off)
    public bool MirrorToWeb { get; set; } = true;   // serve the web UI from this app (http://localhost:8080)
    public float FontSize { get; set; } = 9f;
    public string BackColour { get; set; } = "#A0D2FA";
    public string LayColour { get; set; } = "#FABECD";

    static string PathFile => Profile.File("options.json");

    public static Options Load()
    {
        try { return JsonSerializer.Deserialize<Options>(File.ReadAllText(PathFile)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFile)!);
            File.WriteAllText(PathFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* settings are a convenience; failing to save must not break trading */ }
    }

    public Color Back => ColorTranslator.FromHtml(BackColour);
    public Color Lay => ColorTranslator.FromHtml(LayColour);
}
