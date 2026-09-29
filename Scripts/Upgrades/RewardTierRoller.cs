namespace ShooterLoop;

public static class RewardTierRoller
{
    // The early rounds are deliberately almost pure Common/Rare.
    //
    // These used to open at 65/27/7/1, i.e. an 8% shot at Epic-or-better on *every card*. That reads
    // as fine per card and is far too generous in aggregate: a picker shows 3 cards at once, and a
    // first round with several level-ups puts 12-15 cards in front of the player, so a round-1 Epic
    // stopped being a jackpot and became the expected outcome. Handing out top-tier rewards before
    // the player has any build to improve also flattens the whole progression — there's nothing left
    // to climb toward.
    //
    // Epic now opens at 0.5% (14x rarer) and Legendary is mathematically impossible until round 4,
    // because its Base is negative and clamps at 0 — the curve has to climb into positive territory
    // before the tier exists at all. Both then ramp faster than before, so the late game is no poorer
    // for it; the change moves *when* the good rolls happen, it doesn't remove them.
    private static readonly RoundCurve Common = new(80f, -2.8f, 28f, 80f);
    private static readonly RoundCurve Rare = new(19f, 0.9f, 19f, 36f);
    private static readonly RoundCurve Epic = new(0.5f, 1.1f, 0f, 28f);
    private static readonly RoundCurve Legendary = new(-2.5f, 0.85f, 0f, 18f);

    // Explicit order for the cumulative roll below. RollTier used to walk the weights Dictionary
    // directly, which made correctness depend on Dictionary preserving insertion order — it happens
    // to, but nothing guarantees it, and if the order ever came out differently the cumulative
    // comparison would silently hand back the wrong tier for every roll.
    private static readonly RewardTier[] RollOrder =
    {
        RewardTier.Common, RewardTier.Rare, RewardTier.Epic, RewardTier.Legendary,
    };

    public static Dictionary<RewardTier, float> GetWeights(int round, float fortuneBonus = 0f)
    {
        var raw = new Dictionary<RewardTier, float>
        {
            [RewardTier.Common] = Common.Evaluate(round),
            [RewardTier.Rare] = Rare.Evaluate(round) + fortuneBonus,
            [RewardTier.Epic] = Epic.Evaluate(round) + fortuneBonus,
            [RewardTier.Legendary] = Legendary.Evaluate(round) + fortuneBonus,
        };

        float sum = 0f;
        foreach (var value in raw.Values) sum += value;

        var normalized = new Dictionary<RewardTier, float>();
        foreach (var kv in raw) normalized[kv.Key] = kv.Value / sum;
        return normalized;
    }

    public static RewardTier RollTier(Dictionary<RewardTier, float> weights, Random rng)
    {
        double roll = rng.NextDouble();
        double cumulative = 0;

        // Walked in RollOrder (rarest last) rather than in whatever order the Dictionary yields —
        // see the comment on RollOrder. Common-first also means the overwhelmingly likely outcome
        // is decided on the first comparison.
        foreach (var tier in RollOrder)
        {
            if (!weights.TryGetValue(tier, out float weight)) continue;
            cumulative += weight;
            if (roll <= cumulative) return tier;
        }
        return RewardTier.Common;
    }

    // Brightened into the neon range to match the rest of the palette, but the four hues stay
    // *deliberately distinct* rather than being pulled into the pink family. Tier is information the
    // player reads at a glance while deciding what to buy — four shades of pink would cost more in
    // legibility than it gains in cohesion.
    public static Color GetTierColor(RewardTier tier) => tier switch
    {
        RewardTier.Common => new Color(0.45f, 1f, 0.6f),      // mint
        RewardTier.Rare => new Color(0.42f, 0.72f, 1f),       // sky
        RewardTier.Epic => new Color(0.78f, 0.42f, 1f),       // violet
        RewardTier.Legendary => new Color(1f, 0.82f, 0.35f),  // gold
        _ => Colors.White,
    };

    // TranslationServer.Translate rather than a plain literal -- same "resolve live against whatever
    // language is active" reasoning as Glossary's properties, since this is a static method with no
    // Node to call the Tr() shorthand on.
    public static string GetTierName(RewardTier tier) => TranslationServer.Translate(tier switch
    {
        RewardTier.Common => "Común",
        RewardTier.Rare => "Raro",
        RewardTier.Epic => "Épico",
        RewardTier.Legendary => "Legendario",
        _ => tier.ToString(),
    });
}
