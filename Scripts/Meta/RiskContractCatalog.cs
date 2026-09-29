namespace ShooterLoop;

public readonly struct RiskContractOption
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Description { get; init; }

    // Applied once by RiskContractsMenu.Confirm() straight onto GameManager's Contract* fields --
    // multiple selected contracts stack multiplicatively, same "each layer multiplies the last"
    // reasoning the Tienda's own wealth × repurchase-surcharge pricing already uses. Defaults (1f/
    // false) mean "this contract doesn't touch that axis" -- an entry only ever sets the fields its
    // own effect actually needs.
    public float SpeedMultiplier { get; init; }
    public float HpMultiplier { get; init; }
    public float RewardMultiplier { get; init; }
    public bool NoShield { get; init; }
    public bool NoHeart { get; init; }
    public bool NoUltimate { get; init; }
    public float LibrasBonusMultiplier { get; init; }
}

// Optional, run-wide difficulty modifiers offered right after Round 1 -- see RiskContractsMenu, which
// shows a random 3 of these each run (RiskContractsMenu.PickThree) rather than the full list, so a
// deep pool here doesn't turn into a wall of checkboxes on screen. Every effect reuses an existing
// GameManager field rather than inventing new plumbing into Enemy/EnemySpawner (see
// RoundEventDirector, which treats the Speed/Hp/Reward ones as the baseline a per-round event's own
// multiplier stacks on top of) or an existing catalog filter (UpgradeData.BuildCatalog, for the three
// No* ones).
public static class RiskContractCatalog
{
    // Every entry explicitly sets all three numeric multipliers, even the ones its own effect
    // doesn't touch -- SpeedMultiplier/HpMultiplier/RewardMultiplier all get multiplied onto a
    // GameManager field in Confirm() unconditionally, so an axis left at the struct's own default
    // (0f) would zero that stat out instead of leaving it untouched.
    public static readonly RiskContractOption[] Options =
    {
        new()
        {
            Id = "no_shield", Name = "Sin escudos",
            Description = "No se ofrece Barrera ni Regeneración esta operación.",
            SpeedMultiplier = 1f, HpMultiplier = 1f, RewardMultiplier = 1f,
            NoShield = true, LibrasBonusMultiplier = 1.25f,
        },
        new()
        {
            Id = "no_heart", Name = "Sin corazones",
            Description = "No se ofrece Corazón esta operación — tu vida máxima queda fija.",
            SpeedMultiplier = 1f, HpMultiplier = 1f, RewardMultiplier = 1f,
            NoHeart = true, LibrasBonusMultiplier = 1.20f,
        },
        new()
        {
            Id = "no_ultimate", Name = "Sin Ultimate",
            Description = "No se ofrece ninguna Ultimate esta operación.",
            SpeedMultiplier = 1f, HpMultiplier = 1f, RewardMultiplier = 1f,
            NoUltimate = true, LibrasBonusMultiplier = 1.20f,
        },
        new()
        {
            Id = "fast_enemies", Name = "Enemigos más veloces",
            Description = "+20% de velocidad de movimiento en todos los enemigos.",
            SpeedMultiplier = 1.2f, HpMultiplier = 1f, RewardMultiplier = 1f, LibrasBonusMultiplier = 1.2f,
        },
        new()
        {
            Id = "very_fast_enemies", Name = "Enemigos muy veloces",
            Description = "+35% de velocidad de movimiento en todos los enemigos.",
            SpeedMultiplier = 1.35f, HpMultiplier = 1f, RewardMultiplier = 1f, LibrasBonusMultiplier = 1.35f,
        },
        new()
        {
            Id = "tough_enemies", Name = "Enemigos más resistentes",
            Description = "+25% de vida en todos los enemigos.",
            SpeedMultiplier = 1f, HpMultiplier = 1.25f, RewardMultiplier = 1f, LibrasBonusMultiplier = 1.2f,
        },
        new()
        {
            Id = "very_tough_enemies", Name = "Enemigos muy resistentes",
            Description = "+45% de vida en todos los enemigos.",
            SpeedMultiplier = 1f, HpMultiplier = 1.45f, RewardMultiplier = 1f, LibrasBonusMultiplier = 1.35f,
        },
        new()
        {
            Id = "double_trouble", Name = "Doble riesgo",
            Description = "+15% de velocidad y +15% de vida en todos los enemigos.",
            SpeedMultiplier = 1.15f, HpMultiplier = 1.15f, RewardMultiplier = 1f, LibrasBonusMultiplier = 1.3f,
        },
        new()
        {
            Id = "low_loot", Name = "Botín reducido",
            Description = "-20% de monedas y XP ganados durante toda la operación.",
            SpeedMultiplier = 1f, HpMultiplier = 1f, RewardMultiplier = 0.8f, LibrasBonusMultiplier = 1.3f,
        },
    };

    public static RiskContractOption? Find(string id)
    {
        foreach (var option in Options)
            if (option.Id == id) return option;
        return null;
    }

    // Fisher-Yates over a copy of Options, truncated to `count` -- used by RiskContractsMenu.Open()
    // so a run only ever sees 3 of the pool, re-rolled every run rather than shown as a fixed 3.
    public static List<RiskContractOption> PickRandom(int count, Random rng)
    {
        var pool = new List<RiskContractOption>(Options);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool.GetRange(0, Mathf.Min(count, pool.Count));
    }
}
