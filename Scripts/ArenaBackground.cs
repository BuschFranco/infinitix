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

    // Scanned at runtime rather than a hardcoded list -- DirAccess enumerates res:// paths even in
    // an exported build (Godot's .pck keeps the original resource paths as the lookup keys), so
    // dropping a new bg_*.png into this folder or deleting one just works, no code change needed.
    public override void _Ready()
    {
        var candidates = new List<Texture2D>();
        using var dir = DirAccess.Open(FolderPath);
        if (dir != null)
        {
            dir.ListDirBegin();
            for (string fileName = dir.GetNext(); fileName != ""; fileName = dir.GetNext())
            {
                if (dir.CurrentIsDir() || !fileName.EndsWith(".png")) continue;
                var texture = GD.Load<Texture2D>(FolderPath + fileName);
                if (texture != null) candidates.Add(texture);
            }
            dir.ListDirEnd();
        }

        if (candidates.Count > 0)
            Texture = candidates[(int)(GD.Randi() % candidates.Count)];
    }
}
