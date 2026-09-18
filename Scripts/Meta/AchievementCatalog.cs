namespace ShooterLoop;

public enum AchievementCategory { Progress, Combat, Builds }

// Purely cosmetic — which badge icon a row shows (Assets/Sprites/UI/badge_*.png). Assigned by each
// achievement's rank within its own family (survivor/hardened/unstoppable/legend, exterminator_1..4,
// etc.), not by anything the player can miss or lose — a family with only 3 entries tops out at
// Gold rather than skipping straight to Platinum.
public enum AchievementTier { Bronze, Silver, Gold, Platinum }

// Same shape as Player.BuildRequirement: a threshold plus a way to read the current value, so
// evaluating a whole category is just "iterate and compare" with no per-achievement special case.
public readonly record struct AchievementDef(
    string Id, string Name, string Description, AchievementCategory Category,
    int RewardLibras, Func<GameManager, float> Current, float Needed, AchievementTier Tier);

public static class AchievementCatalog
{
    // Which badge icon a row shows, keyed by tier -- was duplicated per-consumer (AchievementsMenu's
    // row icon, NotificationToastController's live toast) until GameOverScreen became a third. One
    // dictionary here instead of three copies quietly drifting apart.
    public static readonly Dictionary<AchievementTier, Texture2D> TierBadges = new()
    {
        [AchievementTier.Bronze] = GD.Load<Texture2D>("res://Assets/Sprites/UI/badge_bronze.png"),
        [AchievementTier.Silver] = GD.Load<Texture2D>("res://Assets/Sprites/UI/badge_silver.png"),
        [AchievementTier.Gold] = GD.Load<Texture2D>("res://Assets/Sprites/UI/badge_gold.png"),
        [AchievementTier.Platinum] = GD.Load<Texture2D>("res://Assets/Sprites/UI/badge_platinum.png"),
    };

    // First-guess numbers, pending playtesting — same spirit as the rest of the reward/economy
    // catalogs in this project. Not a closed list; more get added later with the same shape. Each
    // tiered family (Exterminador, Cazajefes, Puntería...) climbs into genuinely grindy endgame
    // territory at its higher tiers — those are meant to take a committed player weeks, not one
    // good run. Round-based Progress achievements are deliberately capped at ronda 50 (both the
    // single-run "reach round N" ladder and the lifetime "rounds cleared" one) — beyond that the
    // ask stops being a milestone and starts being a grind for its own sake.
    public static readonly AchievementDef[] All =
    {
        // --- Progreso ---
        new("survivor", "Sobreviviente", "Llegar a la ronda 10", AchievementCategory.Progress,
            20, gm => gm.RoundNumber, 10, AchievementTier.Bronze),
        new("hardened", "Curtido", "Llegar a la ronda 20", AchievementCategory.Progress,
            40, gm => gm.RoundNumber, 20, AchievementTier.Silver),
        new("unstoppable", "Imparable", "Llegar a la ronda 30", AchievementCategory.Progress,
            60, gm => gm.RoundNumber, 30, AchievementTier.Gold),
        new("legend", "Leyenda", "Llegar a la ronda 50", AchievementCategory.Progress,
            100, gm => gm.RoundNumber, 50, AchievementTier.Platinum),
        new("veteran", "Veterano", "Superar 50 rondas", AchievementCategory.Progress,
            30, gm => gm.TotalRoundsCleared, 50, AchievementTier.Silver),
        new("account_10", "Cuenta nivel 10", "Alcanzar el nivel de cuenta 10", AchievementCategory.Progress,
            30, gm => gm.AccountLevel, 10, AchievementTier.Bronze),
        new("account_25", "Cuenta nivel 25", "Alcanzar el nivel de cuenta 25", AchievementCategory.Progress,
            60, gm => gm.AccountLevel, 25, AchievementTier.Silver),
        new("account_50", "Cuenta nivel 50", "Alcanzar el nivel de cuenta 50", AchievementCategory.Progress,
            120, gm => gm.AccountLevel, 50, AchievementTier.Gold),

        // --- Combate ---
        new("exterminator_1", "Exterminador", "100 bajas", AchievementCategory.Combat,
            15, gm => gm.TotalEnemiesKilled, 100, AchievementTier.Bronze),
        new("exterminator_2", "Exterminador II", "1000 bajas", AchievementCategory.Combat,
            40, gm => gm.TotalEnemiesKilled, 1000, AchievementTier.Silver),
        new("exterminator_3", "Exterminador III", "5000 bajas", AchievementCategory.Combat,
            80, gm => gm.TotalEnemiesKilled, 5000, AchievementTier.Gold),
        new("exterminator_4", "Exterminador IV", "20000 bajas", AchievementCategory.Combat,
            150, gm => gm.TotalEnemiesKilled, 20000, AchievementTier.Platinum),
        new("boss_hunter_1", "Cazajefes", "5 jefes derrotados", AchievementCategory.Combat,
            25, gm => gm.TotalBossesKilled, 5, AchievementTier.Bronze),
        new("boss_hunter_2", "Cazajefes II", "20 jefes derrotados", AchievementCategory.Combat,
            60, gm => gm.TotalBossesKilled, 20, AchievementTier.Silver),
        new("boss_hunter_3", "Cazajefes III", "50 jefes derrotados", AchievementCategory.Combat,
            120, gm => gm.TotalBossesKilled, 50, AchievementTier.Gold),
        new("marksman_1", "Puntería", "100 críticos", AchievementCategory.Combat,
            20, gm => gm.TotalCritsLanded, 100, AchievementTier.Bronze),
        new("marksman_2", "Puntería II", "500 críticos", AchievementCategory.Combat,
            60, gm => gm.TotalCritsLanded, 500, AchievementTier.Silver),
        new("marksman_3", "Puntería III", "2000 críticos", AchievementCategory.Combat,
            120, gm => gm.TotalCritsLanded, 2000, AchievementTier.Gold),

        // --- Builds ---
        new("first_build", "Primera build", "Completar cualquier build al menos una vez", AchievementCategory.Builds,
            20, gm => gm.EverCompletedBuilds.Count > 0 ? 1 : 0, 1, AchievementTier.Bronze),
        new("specialist", "Especialista", "Completar 3 builds distintas al menos una vez cada una", AchievementCategory.Builds,
            35, gm => gm.EverCompletedBuilds.Count, 3, AchievementTier.Silver),
        new("completionist", "Completista", "Completar las 7 builds al menos una vez cada una", AchievementCategory.Builds,
            60, gm => gm.EverCompletedBuilds.Count, 7, AchievementTier.Gold),
        new("collector", "Coleccionista", "Conseguir una Legendaria de cualquier tipo", AchievementCategory.Builds,
            15, gm => gm.EverGotLegendary.Count > 0 ? 1 : 0, 1, AchievementTier.Bronze),
        new("legendary_arsenal", "Arsenal legendario", "Conseguir 5 Legendarias distintas", AchievementCategory.Builds,
            60, gm => gm.EverGotLegendary.Count, 5, AchievementTier.Silver),
        new("full_arsenal", "Arsenal completo", "Conseguir 15 Legendarias distintas", AchievementCategory.Builds,
            120, gm => gm.EverGotLegendary.Count, 15, AchievementTier.Gold),
    };

    public static bool IsUnlocked(AchievementDef def, GameManager gm) => def.Current(gm) >= def.Needed;
}
