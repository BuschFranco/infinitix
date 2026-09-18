namespace ShooterLoop;

public enum MissionKind { Kill, BossKill, RoundReached, LibrasEarned, CosmeticPurchase }

// A pool entry names a *range* of possible targets (TargetOptions), not one fixed target — the
// daily roll picks one option per template, same reasoning as the rest of the difficulty tables in
// this project preferring a small table over a formula for numbers still being tuned.
public readonly record struct MissionTemplate(
    string Id, string Name, MissionKind Kind, int[] TargetOptions, int[] RewardOptions);

public static class MissionCatalog
{
    public static readonly MissionTemplate[] Pool =
    {
        new("kill", "Matá {0} enemigos", MissionKind.Kill,
            new[] { 20, 40, 60 }, new[] { 8, 12, 18 }),
        new("boss_kill", "Derrotá un jefe", MissionKind.BossKill,
            new[] { 1 }, new[] { 15 }),
        new("round_reached", "Llegá a la ronda {0}", MissionKind.RoundReached,
            new[] { 5, 8, 12 }, new[] { 10, 15, 22 }),
        new("libras_earned", "Ganá {0} Dinero", MissionKind.LibrasEarned,
            new[] { 10, 20 }, new[] { 8, 15 }),
        new("cosmetic_purchase", "Comprá un cosmético", MissionKind.CosmeticPurchase,
            new[] { 1 }, new[] { 10 }),
    };

    public static MissionTemplate Get(string id)
    {
        foreach (var t in Pool)
            if (t.Id == id) return t;
        return Pool[0];
    }

    // Resolves a slot's template name against its rolled Target -- shared by MissionsMenu (the row
    // label) and GameManager's MissionCompleted event (the live toast text) so the "{0}" substitution
    // rule only lives in one place. Translates the TEMPLATE first, then formats -- formatting first
    // would bake the target number into the string (e.g. "Llegá a la ronda 8"), which then could
    // never match the CSV's generic "{0}"-keyed row.
    public static string FormatName(GameManager.MissionSlot slot)
    {
        var template = Get(slot.TemplateId);
        string name = TranslationServer.Translate(template.Name);
        return name.Contains("{0}") ? string.Format(name, slot.Target) : name;
    }
}
