namespace MurdocsToy.Core;

/// <summary>
/// One automation rule: "when a goal is scored in a match in <see cref="Scope"/>, place this bet on these selections of the market(s) called
/// <see cref="MarketContains"/>". Plain data, saved with the settings.
/// </summary>
public class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New rule";
    public bool Enabled { get; set; } = true;
    /// <summary>Text the market name must contain (case-insensitive), e.g. "Correct Score".</summary>
    public string MarketContains { get; set; } = "Correct Score";
    /// <summary>FAVORITES = starred matches and starred competitions; OPEN = matches with an open ladder; ANY = any match the score relay reports.</summary>
    public string Scope { get; set; } = "FAVORITES";
    public string Side { get; set; } = "LAY";
    public double Amount { get; set; } = 2;
    public bool AmountIsLiability { get; set; }
    /// <summary>Selections to bet on: names separated by ';' (tokens allowed, see <see cref="RuleLogic.Expand"/>), or "*" for every selection.</summary>
    public string Selections { get; set; } = "{score}";
    public string Basis { get; set; } = "LAST";
    public int OffsetTicks { get; set; }
    public int CooldownSeconds { get; set; } = 30;
    public int MaxBetsPerMatch { get; set; } = 3;

    /// <summary>A copy with the same id (for editing without touching the original).</summary>
    public AutomationRule Copy() => (AutomationRule)MemberwiseClone();
    public AutomationRule Clone() { var c = (AutomationRule)MemberwiseClone(); c.Id = Guid.NewGuid().ToString("N"); c.Name += " (copy)"; return c; }
    public AutoBetSettings BetSettings() => new(Side, Amount, AmountIsLiability && Side == "LAY", Basis, OffsetTicks);
}

/// <summary>The pure decisions of the rules: what counts as a goal, which markets and selections a rule means.</summary>
public static class RuleLogic
{
    /// <summary>A goal is the total number of goals going up. A goal taken away (VAR) is not one.</summary>
    public static bool IsGoal(int prevHome, int prevAway, int home, int away) => home + away > prevHome + prevAway;

    public static bool MarketMatches(string marketName, string contains) =>
        !string.IsNullOrWhiteSpace(contains) && marketName.Contains(contains.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Replaces tokens with scores, so one rule can follow the game:
    /// {score} the score now ("1 - 0"), {before} the score before the goal, {home+1} the score if the home team scores again ("2 - 0"),
    /// {away+1} the score if the away team scores again ("1 - 1").
    /// </summary>
    public static string Expand(string text, int home, int away, int prevHome, int prevAway) => text
        .Replace("{score}", $"{home} - {away}", StringComparison.OrdinalIgnoreCase)
        .Replace("{before}", $"{prevHome} - {prevAway}", StringComparison.OrdinalIgnoreCase)
        .Replace("{home+1}", $"{home + 1} - {away}", StringComparison.OrdinalIgnoreCase)
        .Replace("{away+1}", $"{home} - {away + 1}", StringComparison.OrdinalIgnoreCase);

    /// <summary>"1-0", "1 - 0" and "1 – 0" are the same selection; case and extra spaces do not matter.</summary>
    public static string Norm(string s) =>
        string.Join(' ', s.Replace('–', '-').Replace('—', '-').Replace("-", " - ").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>The selections (by id) a rule's text means. "*" means every selection.</summary>
    public static List<long> Pick(IEnumerable<(long Id, string Name)> runners, string selections, int home, int away, int prevHome, int prevAway)
    {
        var all = runners.ToList();
        var parts = selections.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Contains("*")) return all.Select(r => r.Id).ToList();
        var wanted = parts.Select(p => Norm(Expand(p, home, away, prevHome, prevAway))).ToHashSet();
        return all.Where(r => wanted.Contains(Norm(r.Name))).Select(r => r.Id).ToList();
    }

    /// <summary>A short, plain description of what a rule does (shown in the list and in the confirmation).</summary>
    public static string Describe(AutomationRule r) =>
        $"On a goal in {ScopeText(r.Scope)}: {r.Side.ToLowerInvariant()} {r.Amount:0.##}{(r.AmountIsLiability && r.Side == "LAY" ? " liability" : "")} on " +
        $"{(r.Selections.Trim() == "*" ? "every selection" : r.Selections)} in \"{r.MarketContains}\" markets";

    public static string ScopeText(string scope) => scope switch { "OPEN" => "matches with an open ladder", "ANY" => "any match with a live score", _ => "favorite matches and competitions" };
}
