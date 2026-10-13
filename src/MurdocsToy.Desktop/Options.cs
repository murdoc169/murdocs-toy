using System.Text.Json;

namespace MurdocsToy.Desktop;

/// <summary>A match the user starred.</summary>
public record FavoriteEvent(string EventId, string Name);

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
    public bool AutoOpenOnScore { get; set; } = false;   // open (or raise) a favorite match's ladder whenever its score changes
    // Classic interface staking
    public double ClassicBackStake { get; set; } = 2;
    public double ClassicLayStake { get; set; } = 10;
    public bool ClassicLayLiability { get; set; } = false;      // lay "stake" box means liability instead of stake
    public int ClassicScale { get; set; } = 150;
    public string RelayUrl { get; set; } = "";   // live-score relay, e.g. ws://relay-host:9001 (empty = off)
    public bool MirrorToWeb { get; set; } = true;   // serve the web UI from this app (http://localhost:8080)
    public float FontSize { get; set; } = 9f;
    public string BackColour { get; set; } = "#A0D2FA";
    public string LayColour { get; set; } = "#FABECD";

    static readonly string PathFile = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MurdocsToy", "options.json");

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
