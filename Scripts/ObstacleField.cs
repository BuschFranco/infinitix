namespace ShooterLoop;

// Attached to the "Obstacles" node in Arena.tscn, parent of the 16 hand-placed Obstacle children.
// Redistributes them on every round transition rather than destroying/rebuilding: Obstacle._Ready()
// builds its whole visual (shadow, extrusion, outline) and its CollisionShape2D from Size once, so
// moving the already-built node is far cheaper and safer than freeing and re-instantiating it --
// repositioning the parent StaticBody2D carries every child visual with it for free.
public partial class ObstacleField : Node2D
{
    private readonly List<Obstacle> _obstacles = new();
    private readonly Random _rng = new();

    // Same "N attempts, fall back rather than loop forever" shape EnemySpawner.PickPositionAround
    // already uses for its own placement search.
    private const int PlacementAttempts = 10;
    private const float PlayerClearance = 260f;
    private const float ObstacleMargin = 24f;
    private const float EdgeMargin = 160f;

    public override void _Ready()
    {
        AddToGroup("obstacle_field");
        foreach (var child in GetChildren())
            if (child is Obstacle obstacle) _obstacles.Add(obstacle);
    }

    // Second-chance attempt count once the full search (player + other obstacles) below has failed
    // every one of its own tries -- this pass only re-checks the player, since that clearance is the
    // one rule that must never be broken. A large arena against a ~260px clearance radius means this
    // essentially always succeeds within a couple of tries.
    private const int PlayerOnlyAttempts = 20;

    // Called from GameManager.StartNextRound(), inside RoundTransitionOverlay's darkest frame -- the
    // player never sees an obstacle actually move, only that it's somewhere new once the fade clears.
    //
    // Player clearance is a hard requirement: an obstacle must never end up on top of the ship the
    // player is currently piloting. Spacing from other obstacles is a soft preference underneath
    // that -- worth trying for, but never worth risking the hard rule over. Three phases, in
    // descending order of what they're willing to give up: full search -> player-only search ->
    // guaranteed opposite-side-of-the-arena placement, which by construction can't land on the player
    // (short of the player standing exactly on the arena's center, an edge case not worth coding
    // around).
    public void Randomize()
    {
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        Vector2 extents = player?.ArenaHalfExtents ?? new Vector2(2200f, 1400f);
        Vector2 playerPos = player?.GlobalPosition ?? Vector2.Zero;

        var placed = new List<(Vector2 Position, float Radius)>();

        foreach (var obstacle in _obstacles)
        {
            // Treated as a circle for clearance purposes -- cheap, and a couple of extra px of gap
            // around a rectangular block is never visible the way an actual overlap would be.
            float radius = Mathf.Max(obstacle.Size.X, obstacle.Size.Y) / 2f;
            float playerRadius = PlayerClearance + radius;

            Vector2? chosen = TryFindPosition(extents, playerPos, playerRadius, placed, radius, PlacementAttempts, requireObstacleClearance: true);
            chosen ??= TryFindPosition(extents, playerPos, playerRadius, placed, radius, PlayerOnlyAttempts, requireObstacleClearance: false);
            chosen ??= OppositeSideOfArena(extents, playerPos);

            obstacle.Position = chosen.Value;
            placed.Add((chosen.Value, radius));
        }
    }

    private Vector2? TryFindPosition(Vector2 extents, Vector2 playerPos, float playerRadius,
        List<(Vector2 Position, float Radius)> placed, float radius, int attempts, bool requireObstacleClearance)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            var candidate = new Vector2(
                (float)(_rng.NextDouble() * 2.0 - 1.0) * (extents.X - EdgeMargin),
                (float)(_rng.NextDouble() * 2.0 - 1.0) * (extents.Y - EdgeMargin));

            if (candidate.DistanceTo(playerPos) < playerRadius) continue;

            if (requireObstacleClearance)
            {
                bool overlaps = false;
                foreach (var other in placed)
                {
                    if (candidate.DistanceTo(other.Position) < radius + other.Radius + ObstacleMargin)
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (overlaps) continue;
            }

            return candidate;
        }
        return null;
    }

    // The absolute last resort, not a random guess -- mirrors the player's position through the
    // arena's center and clamps it inside bounds, so it's always as far from the player as this
    // arena can put it.
    private static Vector2 OppositeSideOfArena(Vector2 extents, Vector2 playerPos)
    {
        Vector2 away = playerPos.LengthSquared() > 1f
            ? -playerPos.Normalized() * Mathf.Min(extents.X, extents.Y - EdgeMargin) * 0.85f
            : new Vector2(extents.X - EdgeMargin, 0f);

        return new Vector2(
            Mathf.Clamp(away.X, -(extents.X - EdgeMargin), extents.X - EdgeMargin),
            Mathf.Clamp(away.Y, -(extents.Y - EdgeMargin), extents.Y - EdgeMargin));
    }
}
