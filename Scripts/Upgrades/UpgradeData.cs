namespace ShooterLoop;

public enum UpgradeType
{
    FireRange,
    ExtraProjectile,
    OrbitShield,
    FireRate,
    BulletDamage,
    MovementSpeed,
    Heart,
    HitShield,
    Companion,
    SideShot,
    Laser,
    Ultimate,
    Pierce,
    CritChance,
    BulletKnockback,
    CoinBonus,
    ShieldRegen,
    Missile,
    Burn,
    XpBonus,
    ShockwaveAura,
    Vendaval,
    Dodge,
    Fortune,
    Ricochet,
    Thorns,
    Mine,
}

public enum RewardTier
{
    Common,
    Rare,
    Epic,
    Legendary
}

// The 4 Ultimates a player can find in the shop. See Player.TriggerUltimate for what each does.
public enum UltimateKind
{
    Nova,
    TimeSlow,
    Frenzy,
    Invulnerability
}

public static class UltimateKindNames
{
    // The UI used to print the enum member directly, so the player read "TimeSlow" while the catalog
    // that sold it to them called it "Zona Lenta". These are the catalog's own names, minus the
    // "Ultimate: " prefix the shop rows carry.
    public static string Display(UltimateKind kind) => kind switch
    {
        UltimateKind.Nova => "Pulso Nova",
        UltimateKind.TimeSlow => "Zona Lenta",
        UltimateKind.Frenzy => "Sobrecarga",
        UltimateKind.Invulnerability => "Escudo Absoluto",
        _ => kind.ToString(),
    };
}

// Where a reward is allowed to show up. This replaced a pair of ad-hoc `includeHearts` /
// `includeUltimates` bools threaded through the catalog builder: with more than a couple of
// exclusives that approach needs a new flag per exclusive and encodes the rule at the *call site*
// instead of on the reward itself. Now each entry states its own availability, and the two pools
// — free level-up picks vs. paid shop stock — are a real, readable distinction.
[Flags]
public enum RewardSource
{
    LevelUp = 1,
    Shop = 2,
    Both = LevelUp | Shop,
}

public class UpgradeData
{
    public string Name;
    public string Description;
    public UpgradeType Type;
    public float Value;
    public int Cost;
    public RewardTier Tier;
    public RewardSource Source;
    public UltimateKind Ultimate;

    public UpgradeData(string name, string description, UpgradeType type, RewardTier tier, float value = 0f, int cost = 0, RewardSource source = RewardSource.Both, UltimateKind ultimate = UltimateKind.Nova)
    {
        Name = name;
        Description = description;
        Type = type;
        Tier = tier;
        Value = value;
        Cost = cost;
        Source = source;
        Ultimate = ultimate;
    }

    private static readonly Random Rng = new();

    // Every reward in the game, keyed by tier. Callers filter by RewardSource rather than getting
    // a pre-trimmed list, so there's exactly one place to read the full catalog from.
    private static Dictionary<RewardTier, List<UpgradeData>> BuildFullCatalog() => new()
    {
        [RewardTier.Common] = new List<UpgradeData>
        {
            new("Alcance de Disparo I", "+70 alcance", UpgradeType.FireRange, RewardTier.Common, 70f, cost: 8),
            new("Fuego Rápido I", "+0.5 cadencia", UpgradeType.FireRate, RewardTier.Common, 0.5f, cost: 8),
            new("Balas Afiladas I", "+3 daño", UpgradeType.BulletDamage, RewardTier.Common, 3f, cost: 8),
            new("Cuchilla Orbital I", "1 cuchilla orbital", UpgradeType.OrbitShield, RewardTier.Common, 1f, cost: 20),
            new("Escudo I", "+1 escudo", UpgradeType.HitShield, RewardTier.Common, 1f, cost: 14),
            new("Dron I", "Dron al 30%", UpgradeType.Companion, RewardTier.Common, 30f, cost: 12),
            new("Láser I", "Láser cada 4s (12 daño, 5 enemigos)", UpgradeType.Laser, RewardTier.Common, 1f, cost: 20),
            new("Perforación I", "+1 perforación", UpgradeType.Pierce, RewardTier.Common, 1f, cost: 18),
            new("Crítico I", "+8% crítico (x2)", UpgradeType.CritChance, RewardTier.Common, 8f, cost: 12),
            new("Retroceso I", "Retroceso +60", UpgradeType.BulletKnockback, RewardTier.Common, 60f, cost: 10),
            new("Botín I", "+15% monedas", UpgradeType.CoinBonus, RewardTier.Common, 15f, cost: 14),
            new("Misil I", "Misil cada 4.5s (30 daño, radio 70)", UpgradeType.Missile, RewardTier.Common, 1f, cost: 24),
            new("Incendiario I", "Quema 6/s por 3s", UpgradeType.Burn, RewardTier.Common, 1f, cost: 20),
            new("Sabiduría I", "+20% XP", UpgradeType.XpBonus, RewardTier.Common, 20f, cost: 18),
            new("Onda de Choque I", "Onda cada 6s (20 daño, radio 65)", UpgradeType.ShockwaveAura, RewardTier.Common, 1f, cost: 20),
            new("Esquiva I", "5% esquiva", UpgradeType.Dodge, RewardTier.Common, 5f, cost: 16),
            new("Fortuna I", "+2% Raro/Épico/Legendario", UpgradeType.Fortune, RewardTier.Common, 2f, cost: 14),
            new("Rebote I", "1 rebote", UpgradeType.Ricochet, RewardTier.Common, 1f, cost: 18),
            new("Mina I", "Mina cada 5.5s (45 daño, radio 55)", UpgradeType.Mine, RewardTier.Common, 1f, cost: 24),
        },
        [RewardTier.Rare] = new List<UpgradeData>
        {
            new("Alcance de Disparo II", "+130 alcance", UpgradeType.FireRange, RewardTier.Rare, 130f, cost: 16),
            new("Fuego Rápido II", "+1 cadencia", UpgradeType.FireRate, RewardTier.Rare, 1f, cost: 16),
            new("Balas Afiladas II", "+5 daño", UpgradeType.BulletDamage, RewardTier.Rare, 5f, cost: 16),
            new("Velocidad II", "+20 velocidad", UpgradeType.MovementSpeed, RewardTier.Rare, 20f, cost: 18),
            new("Cuchilla Orbital II", "2 cuchillas orbitales", UpgradeType.OrbitShield, RewardTier.Rare, 2f, cost: 32),
            new("Escudo II", "+2 escudo", UpgradeType.HitShield, RewardTier.Rare, 2f, cost: 24),
            new("Dron II", "Dron al 35%", UpgradeType.Companion, RewardTier.Rare, 35f, cost: 22),
            new("Disparo Paralelo", "+1 línea paralela (máx 5)", UpgradeType.SideShot, RewardTier.Rare, 1f, cost: 22),
            new("Láser II", "Láser cada 3.2s (20 daño, todos)", UpgradeType.Laser, RewardTier.Rare, 2f, cost: 34),
            new("Perforación II", "+1 perforación", UpgradeType.Pierce, RewardTier.Rare, 1f, cost: 26),
            new("Crítico II", "+12% crítico (x2)", UpgradeType.CritChance, RewardTier.Rare, 12f, cost: 20),
            new("Retroceso II", "Retroceso +110", UpgradeType.BulletKnockback, RewardTier.Rare, 110f, cost: 18),
            new("Botín II", "+25% monedas", UpgradeType.CoinBonus, RewardTier.Rare, 25f, cost: 24),
            new("Misil II", "Misil cada 3.8s (50 daño, radio 85)", UpgradeType.Missile, RewardTier.Rare, 2f, cost: 38),
            new("Incendiario II", "Quema 10/s por 3s", UpgradeType.Burn, RewardTier.Rare, 2f, cost: 32),
            new("Onda de Choque II", "Onda cada 5s (35 daño, radio 78)", UpgradeType.ShockwaveAura, RewardTier.Rare, 2f, cost: 32),
            new("Esquiva II", "10% esquiva", UpgradeType.Dodge, RewardTier.Rare, 10f, cost: 28),
            new("Fortuna II", "+3% Raro/Épico/Legendario", UpgradeType.Fortune, RewardTier.Rare, 3f, cost: 24),
            new("Rebote II", "2 rebotes", UpgradeType.Ricochet, RewardTier.Rare, 2f, cost: 30),
            new("Mina II", "Mina cada 4.6s (70 daño, radio 65)", UpgradeType.Mine, RewardTier.Rare, 2f, cost: 38),
        },
        [RewardTier.Epic] = new List<UpgradeData>
        {
            new("Alcance de Disparo III", "+200 alcance", UpgradeType.FireRange, RewardTier.Epic, 200f, cost: 28),
            new("Fuego Rápido III", "+1.8 cadencia", UpgradeType.FireRate, RewardTier.Epic, 1.8f, cost: 28),
            new("Balas Afiladas III", "+9 daño", UpgradeType.BulletDamage, RewardTier.Epic, 9f, cost: 28),
            new("Disparo en Diagonal", "+2 proyectiles en diagonal", UpgradeType.ExtraProjectile, RewardTier.Epic, cost: 30),
            new("Cuchilla Orbital III", "3 cuchillas orbitales", UpgradeType.OrbitShield, RewardTier.Epic, 3f, cost: 46),
            new("Escudo III", "+3 escudo", UpgradeType.HitShield, RewardTier.Epic, 3f, cost: 36),
            new("Dron III", "Dron al 45%", UpgradeType.Companion, RewardTier.Epic, 45f, cost: 34),
            new("Láser III", "Láser cada 2.5s (31 daño, todos)", UpgradeType.Laser, RewardTier.Epic, 3f, cost: 50),
            new("Perforación III", "+2 perforación", UpgradeType.Pierce, RewardTier.Epic, 2f, cost: 38),
            new("Crítico III", "+18% crítico (x2)", UpgradeType.CritChance, RewardTier.Epic, 18f, cost: 32),
            new("Retroceso III", "Retroceso +170", UpgradeType.BulletKnockback, RewardTier.Epic, 170f, cost: 28),
            new("Botín III", "+40% monedas", UpgradeType.CoinBonus, RewardTier.Epic, 40f, cost: 36),
            new("Misil III", "Misil cada 3.2s (75 daño, radio 100)", UpgradeType.Missile, RewardTier.Epic, 3f, cost: 54),
            new("Incendiario III", "Quema 16/s por 3.5s", UpgradeType.Burn, RewardTier.Epic, 3f, cost: 46),
            new("Sabiduría II", "+50% XP", UpgradeType.XpBonus, RewardTier.Epic, 50f, cost: 44),
            new("Onda de Choque III", "Onda cada 4s (55 daño, radio 92)", UpgradeType.ShockwaveAura, RewardTier.Epic, 3f, cost: 46),
            new("Esquiva III", "15% esquiva", UpgradeType.Dodge, RewardTier.Epic, 15f, cost: 42),
            new("Fortuna III", "+5% Raro/Épico/Legendario", UpgradeType.Fortune, RewardTier.Epic, 5f, cost: 38),
            new("Rebote III", "3 rebotes", UpgradeType.Ricochet, RewardTier.Epic, 3f, cost: 46),
            new("Mina III", "Mina cada 3.8s (100 daño, radio 75)", UpgradeType.Mine, RewardTier.Epic, 3f, cost: 54),

            // --- Shop-exclusive from here ---
            new("Ultimate: Pulso Nova", "Daño masivo a todos los cercanos", UpgradeType.Ultimate, RewardTier.Epic, cost: 80, source: RewardSource.Shop, ultimate: UltimateKind.Nova),
            new("Ultimate: Zona Lenta", "Ralentiza a todos los enemigos", UpgradeType.Ultimate, RewardTier.Epic, cost: 80, source: RewardSource.Shop, ultimate: UltimateKind.TimeSlow),
            new("Ultimate: Sobrecarga", "Duplica cadencia y daño", UpgradeType.Ultimate, RewardTier.Epic, cost: 80, source: RewardSource.Shop, ultimate: UltimateKind.Frenzy),
            new("Ultimate: Escudo Absoluto", "Invulnerabilidad total por unos segundos", UpgradeType.Ultimate, RewardTier.Epic, cost: 80, source: RewardSource.Shop, ultimate: UltimateKind.Invulnerability),
            new("Regeneración", "+1 escudo cada 12s", UpgradeType.ShieldRegen, RewardTier.Epic, 5f, cost: 55, source: RewardSource.Shop),
            new("Vendaval I", "Ráfaga frontal cada 4.5s (90 daño, alcance 260)", UpgradeType.Vendaval, RewardTier.Epic, 1f, cost: 75, source: RewardSource.Shop),
            new("Escudo Voltáico", "Refleja 20 daño al ser golpeado (8s cd)", UpgradeType.Thorns, RewardTier.Epic, 15f, cost: 60, source: RewardSource.Shop),
        },
        [RewardTier.Legendary] = new List<UpgradeData>
        {
            new("Alcance de Disparo IV", "+300 alcance", UpgradeType.FireRange, RewardTier.Legendary, 300f, cost: 45),
            new("Fuego Rápido IV", "+3 cadencia", UpgradeType.FireRate, RewardTier.Legendary, 3f, cost: 45),
            new("Balas Afiladas IV", "+15 daño", UpgradeType.BulletDamage, RewardTier.Legendary, 15f, cost: 45),
            new("Velocidad IV", "+45 velocidad y deja una estela que quema a los enemigos al tocarla", UpgradeType.MovementSpeed, RewardTier.Legendary, 45f, cost: 42),
            new("Cuchilla Orbital IV", "4 cuchillas orbitales", UpgradeType.OrbitShield, RewardTier.Legendary, 4f, cost: 60),
            new("Escudo IV", "+4 escudo", UpgradeType.HitShield, RewardTier.Legendary, 4f, cost: 48),
            new("Dron IV", "Dron al 50% y aparece un segundo dron", UpgradeType.Companion, RewardTier.Legendary, 50f, cost: 50),
            new("Disparo Paralelo", "+2 líneas paralelas (máx 5)", UpgradeType.SideShot, RewardTier.Legendary, 2f, cost: 55),
            new("Láser IV", "Láser cada 1.8s (50 daño, todos)", UpgradeType.Laser, RewardTier.Legendary, 4f, cost: 65),
            new("Perforación IV", "+3 perforación", UpgradeType.Pierce, RewardTier.Legendary, 3f, cost: 55),
            new("Crítico IV", "+25% crítico (x2)", UpgradeType.CritChance, RewardTier.Legendary, 25f, cost: 48),
            new("Retroceso IV", "Retroceso +240", UpgradeType.BulletKnockback, RewardTier.Legendary, 240f, cost: 42),
            new("Botín IV", "+60% monedas", UpgradeType.CoinBonus, RewardTier.Legendary, 60f, cost: 52),
            new("Misil IV", "Misil cada 2.6s (110 daño, radio 120)", UpgradeType.Missile, RewardTier.Legendary, 4f, cost: 72),
            new("Incendiario IV", "Quema 24/s por 4s", UpgradeType.Burn, RewardTier.Legendary, 4f, cost: 62),
            new("Onda de Choque IV", "Onda cada 3.2s (80 daño, radio 105)", UpgradeType.ShockwaveAura, RewardTier.Legendary, 4f, cost: 62),
            new("Esquiva IV", "25% esquiva", UpgradeType.Dodge, RewardTier.Legendary, 25f, cost: 58),
            new("Fortuna IV", "+7% Raro/Épico/Legendario", UpgradeType.Fortune, RewardTier.Legendary, 7f, cost: 52),
            new("Rebote IV", "4 rebotes", UpgradeType.Ricochet, RewardTier.Legendary, 4f, cost: 64),
            new("Mina IV", "Mina cada 3.0s (140 daño, radio 85)", UpgradeType.Mine, RewardTier.Legendary, 4f, cost: 72),

            // --- Shop-exclusive from here ---
            new("Corazón", "+1 vida máxima, cura todo", UpgradeType.Heart, RewardTier.Legendary, 1f, cost: 60, source: RewardSource.Shop),
            new("Regeneración+", "+1 escudo cada 7.5s", UpgradeType.ShieldRegen, RewardTier.Legendary, 8f, cost: 75, source: RewardSource.Shop),
            new("Vendaval II", "Ráfaga frontal cada 3.6s (140 daño, alcance 320)", UpgradeType.Vendaval, RewardTier.Legendary, 2f, cost: 115, source: RewardSource.Shop),
        },
    };

    public static Dictionary<RewardTier, List<UpgradeData>> BuildCatalog(RewardSource source)
    {
        var full = BuildFullCatalog();

        // Hardcore pins MaxLives to 1 forever (Player._Ready) and never lets shield charges exist —
        // filtered out here, at the source, rather than via the isUseless predicate PickFromTier
        // already takes: that predicate falls back to the unfiltered pool once filtering leaves it
        // empty, which would occasionally leak a Heart/Shield offer back in. Removing them from the
        // catalog itself means there's nothing for that fallback to resurrect.
        bool hardcore = GameManager.Instance?.CurrentGameMode == GameManager.GameMode.Hardcore;

        var filtered = new Dictionary<RewardTier, List<UpgradeData>>();
        foreach (var kv in full)
            filtered[kv.Key] = kv.Value.FindAll(u => (u.Source & source) != 0
                && (!hardcore || (u.Type != UpgradeType.Heart && u.Type != UpgradeType.HitShield && u.Type != UpgradeType.ShieldRegen)));
        return filtered;
    }

    // isUseless: optional filter (typically Player.IsRewardUseless) so a reward that would have
    // zero effect right now — e.g. a Common Barrier while already topped off on a better tier —
    // is avoided when there's any alternative, instead of wasting one of the offered slots (or,
    // worse, leaving the free level-up picker with all 3 choices disabled and no way to proceed).
    public static List<UpgradeData> PickRandomTiered(int count, int round, RewardSource source, Func<UpgradeData, bool> isUseless = null, float fortuneBonus = 0f)
    {
        var catalog = BuildCatalog(source);
        var weights = RewardTierRoller.GetWeights(round, fortuneBonus);
        var usedNames = new HashSet<string>();
        var result = new List<UpgradeData>();

        for (int i = 0; i < count; i++)
        {
            var tier = RewardTierRoller.RollTier(weights, Rng);
            var pick = PickFromTier(catalog, tier, usedNames, isUseless) ?? PickFromAnyTier(catalog, usedNames, isUseless);
            if (pick == null) break;

            usedNames.Add(pick.Name);
            result.Add(pick);
        }

        return result;
    }

    // The one-time reward for killing the first boss: a straight choice between Ultimates, outside
    // the tier system entirely (Ultimates have no tiers — they all sit in the Epic bucket purely so
    // the normal shop roll can surface them). Prefers ones the player doesn't already have
    // equipped, falling back to the full set if that leaves too few to fill the picker.
    public static List<UpgradeData> PickUltimateChoices(int count, Func<UpgradeData, bool> isUseless = null)
    {
        var all = BuildCatalog(RewardSource.Shop)[RewardTier.Epic]
            .FindAll(u => u.Type == UpgradeType.Ultimate);

        var preferred = isUseless == null ? all : all.FindAll(u => !isUseless(u));
        var pool = new List<UpgradeData>(preferred.Count >= count ? preferred : all);

        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return pool.GetRange(0, Mathf.Min(count, pool.Count));
    }

    // The round-10 shop: forces every offer to Legendary tier, no roll. Reuses PickFromTier's
    // existing dedup/isUseless-fallback behaviour instead of duplicating it, same as
    // PickUltimateChoices reuses BuildCatalog rather than inventing its own filtering.
    public static List<UpgradeData> PickLegendaryOnly(int count, Func<UpgradeData, bool> isUseless = null)
    {
        var catalog = BuildCatalog(RewardSource.Shop);
        var usedNames = new HashSet<string>();
        var result = new List<UpgradeData>();

        for (int i = 0; i < count; i++)
        {
            var pick = PickFromTier(catalog, RewardTier.Legendary, usedNames, isUseless);
            if (pick == null) break;
            usedNames.Add(pick.Name);
            result.Add(pick);
        }
        return result;
    }

    private static UpgradeData PickFromTier(Dictionary<RewardTier, List<UpgradeData>> catalog, RewardTier tier, HashSet<string> usedNames, Func<UpgradeData, bool> isUseless)
    {
        var pool = catalog[tier].FindAll(u => !usedNames.Contains(u.Name) && (isUseless == null || !isUseless(u)));
        if (pool.Count == 0)
            pool = catalog[tier].FindAll(u => !usedNames.Contains(u.Name)); // nothing useful left — fall back rather than skip the slot
        if (pool.Count == 0) return null;
        return pool[Rng.Next(pool.Count)];
    }

    private static UpgradeData PickFromAnyTier(Dictionary<RewardTier, List<UpgradeData>> catalog, HashSet<string> usedNames, Func<UpgradeData, bool> isUseless)
    {
        foreach (var tier in catalog.Keys)
        {
            var pick = PickFromTier(catalog, tier, usedNames, isUseless);
            if (pick != null) return pick;
        }
        return null;
    }
}
