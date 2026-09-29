namespace ShooterLoop;

// Random scenery skin for the arena, picked once per run so consecutive rounds don't repeat one
// backdrop. Drawn as a sibling ON TOP of the Backdrop ColorRect (see DangerDirector/DangerLevel),
// never behind or instead of it: the danger-tint colour tween that ColorRect drives is what keeps
// the arena floor below WorldEnvironment's glow_hdr_threshold, and this node's own partial Modulate
// alpha only ever darkens the composited pixel relative to the image's own brightness -- it can't
// push the result over that threshold the way drawing it at full opacity might.
public partial class ArenaBackground : TextureRect
{
    private const string FolderPath = "res://Assets/Sprites/Backgrounds/";

    // Scanned once and cached rather than re-scanned every call -- DirAccess enumerates res:// paths
    // even in an exported build (Godot's .pck keeps the original resource paths as the lookup keys),
    // so dropping a new bg_*.png into this folder or deleting one still just works, no code change
    // needed, but there's no reason to hit the filesystem again every round transition.
    private readonly List<Texture2D> _candidates = new();

    public override void _Ready()
    {
        AddToGroup("arena_background");

        using var dir = DirAccess.Open(FolderPath);
        if (dir != null)
        {
            dir.ListDirBegin();
            for (string fileName = dir.GetNext(); fileName != ""; fileName = dir.GetNext())
            {
                if (dir.CurrentIsDir() || !fileName.EndsWith(".png")) continue;
                var texture = GD.Load<Texture2D>(FolderPath + fileName);
                if (texture != null) _candidates.Add(texture);
            }
            dir.ListDirEnd();
        }

        PickRandom();
    }

    // Called again on every round transition (see GameManager.StartNextRound, timed to the
    // RoundTransitionOverlay's darkest frame so the swap is invisible) -- excludes whatever's
    // currently shown so the change always actually reads as a change when there's more than one
    // candidate to pick from.
    public void PickRandom()
    {
        if (_candidates.Count == 0) return;

        var pool = _candidates.Count > 1 ? _candidates.FindAll(t => t != Texture) : _candidates;
        Texture = pool[(int)(GD.Randi() % pool.Count)];
    }
}
