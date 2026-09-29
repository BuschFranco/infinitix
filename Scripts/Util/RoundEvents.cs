namespace ShooterLoop;

public enum RoundEventKind
{
    None,
    MissileStrike,
    Fog,
    Frenzy,
    ShrinkingZone,
    Minefield,
    Armor,
}

// Static data for the per-round modifiers: which ones exist, how likely one is, and what the player is
// told when it starts. Same stateless-helper shape as DangerLevel and DifficultyBalancer — the director
// owns the behaviour, this owns the numbers and the wording.
public static class RoundEvents
{
    // 20% of non-boss rounds. Boss rounds are excluded deliberately: they already carry the banner, the
    // pre-boss alarm and the boss's own arrival, and stacking a hazard on top would be unreadable.
    public const float Chance = 0.20f;

    // Boss rounds aside, the opening rounds stay clean — an event during the tutorial rounds would land
    // before the player knows what normal looks like.
    public const int FirstRound = 4;

    private static readonly RoundEventKind[] All =
    {
        RoundEventKind.MissileStrike,
        RoundEventKind.Fog,
        RoundEventKind.Frenzy,
        RoundEventKind.ShrinkingZone,
        RoundEventKind.Minefield,
        RoundEventKind.Armor,
    };

    public static RoundEventKind Roll(Random rng, int round)
    {
        if (round < FirstRound) return RoundEventKind.None;
        if (rng.NextDouble() >= Chance) return RoundEventKind.None;
        return All[rng.Next(All.Length)];
    }

    // Shown once after the countdown, via the same callout DangerOverlay uses for threat levels. Kept
    // short: it's on screen for under two seconds while the round is already starting.
    public static string Announcement(RoundEventKind kind)
    {
        string text = kind switch
        {
            RoundEventKind.MissileStrike => "¡LLUVIA DE MISILES!",
            RoundEventKind.Fog => "¡NIEBLA!",
            RoundEventKind.Frenzy => "¡FRENESÍ! BOTÍN DOBLE",
            RoundEventKind.ShrinkingZone => "¡ZONA QUE SE ENCOGE!",
            RoundEventKind.Minefield => "¡CAMPO MINADO!",
            RoundEventKind.Armor => "¡BLINDAJE! BOTÍN +60%",
            _ => null,
        };
        return text == null ? null : TranslationServer.Translate(text);
    }
}
