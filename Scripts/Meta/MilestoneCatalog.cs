namespace ShooterLoop;

// Turns two already-tracked, already-persisted progress numbers (GameManager.AccountLevel and
// per-pilot character level) into an actual payoff -- both used to be pure numbers with a progress
// bar and nothing behind them (see docs/economy.md). Every LevelStep levels crossed, on either track,
// hands out something for free -- GameManager.EvaluateAccountMilestones/EvaluateCharacterMilestones
// call in here to ask "is level N a milestone, and what does it pay", then handle the actual granting
// and the idempotent claimed-tracking themselves.
public static class MilestoneCatalog
{
    public const int LevelStep = 5;

    // Account-level milestones hand out a free profile icon -- specifically the ones that otherwise
    // only exist behind a Tienda price (see ProfileIconCatalog), so leveling the account is a second,
    // no-cost way to reach the same emblems. Runs out once the paid icons do; levels past that fall
    // back to a flat Libras bonus below so a milestone never becomes a silent no-op.
    private static readonly string[] AccountIconRewards = { "mira", "corona", "diamante", "calavera" };

    /// <summary>True if this level is a claimable account milestone. Exactly one of
    /// <paramref name="profileIconId"/> (a free icon) or <paramref name="libras"/> (a Dinero bonus,
    /// once the icon list runs out) is set.</summary>
    public static bool TryGetAccountReward(int level, out string profileIconId, out int libras)
    {
        profileIconId = null;
        libras = 0;
        if (level <= 0 || level % LevelStep != 0) return false;

        int index = level / LevelStep - 1;
        if (index < AccountIconRewards.Length)
        {
            profileIconId = AccountIconRewards[index];
            return true;
        }

        // Every step past the icon list still pays out, just growing in Libras instead of a fixed
        // asset list -- same "never silently do nothing" reasoning as the icon branch above.
        libras = 10 + index * 5;
        return true;
    }

    // Character-level milestones are a flat, growing Libras bonus -- no per-pilot cosmetic slot exists
    // to hand out instead, and a currency reward scales cleanly to however many pilots the player
    // levels up, built-in or custom alike.
    public static bool TryGetCharacterReward(int level, out int libras)
    {
        libras = 0;
        if (level <= 0 || level % LevelStep != 0) return false;

        libras = 8 + (level / LevelStep - 1) * 4;
        return true;
    }
}
