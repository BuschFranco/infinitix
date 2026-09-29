using System.Collections.Generic;
using System.Text;

namespace ShooterLoop;

// Player-created characters: persisted locally, never bundled with the game.
//
// Two pieces on disk, both under user:// (AppData on Windows, the app's private dir on Android):
//   user://characters.cfg      one section per character, holding name/description/ring
//   user://characters/<slug>.png   the processed portrait
//
// The portrait is processed on save rather than on load — cropping, resizing and masking a photo
// every time the carousel steps would be wasteful, and it means the file on disk is already the
// exact 144x144 circle the rest of the game expects. It mirrors tools/prep_character_sprite.py, so
// a hand-made portrait and a player-made one are the same kind of asset.
public static class CustomCharacterStore
{
    private const string ConfigPath = "user://characters.cfg";
    private const string ImageDir = "user://characters";
    private const string SlugPrefix = "custom_";

    // Matches the built-in portraits: 4x the ~36px the ship occupies, which keeps the edge clean
    // through the breathe/punch tweens that scale the sprite up.
    private const int PortraitSize = 144;
    private const float RingWidthRatio = 0.055f;

    public const int MaxNameLength = 18;
    public const int MaxDescriptionLength = 600;

    // Cycled through for each new character so they're distinguishable at a glance without asking
    // the player to pick a colour. Same neon set the built-in portraits use.
    private static readonly Color[] RingColors =
    {
        new("ff4fd8"), new("3dff8f"), new("ffe066"),
        new("7dfdfe"), new("c65bff"), new("ff8a5c"),
    };

    public static CharacterInfo[] LoadAll()
    {
        var config = new ConfigFile();
        if (config.Load(ConfigPath) != Error.Ok) return System.Array.Empty<CharacterInfo>();

        var result = new List<CharacterInfo>();
        foreach (string slug in config.GetSections())
        {
            string imagePath = $"{ImageDir}/{slug}.png";
            if (!FileAccess.FileExists(imagePath)) continue;   // image gone: skip rather than show a blank

            result.Add(new CharacterInfo
            {
                Slug = slug,
                Name = (string)config.GetValue(slug, "name", "Sin nombre"),
                Description = (string)config.GetValue(slug, "description", ""),

                // Hardcoded, not read from the file: these are cosmetic characters, and reading
                // multipliers back from a text file the player can edit would be a cheat menu.
                MoveSpeedMultiplier = 1f,
                BulletDamageMultiplier = 1f,

                Color = Colors.White,   // the portrait carries its own colours; see CharacterInfo.Color
                SpritePath = imagePath,
                IsCustom = true,
                IsCircular = true,
            });
        }
        return result.ToArray();
    }

    // Returns the new character's slug, or null with a player-facing message — every failure here is
    // something the player did (no image picked, unreadable file) and belongs on screen.
    //
    // Returning the slug matters: the caller frames the new character in the carousel, and it cannot
    // just take the last entry. NextSlug reuses the lowest free number, so creating one after
    // deleting an earlier one lands in the middle of the list, not at the end.
    public static string Create(string name, string description, string sourcePath, out string error)
    {
        error = null;
        name = name?.Trim() ?? "";
        description = description?.Trim() ?? "";

        if (name.Length == 0) { error = TranslationServer.Translate("Ponele un nombre."); return null; }
        if (name.Length > MaxNameLength) { error = string.Format(TranslationServer.Translate("El nombre no puede pasar de {0} caracteres."), MaxNameLength); return null; }
        if (description.Length > MaxDescriptionLength) { error = string.Format(TranslationServer.Translate("La descripción no puede pasar de {0} caracteres."), MaxDescriptionLength); return null; }
        if (string.IsNullOrEmpty(sourcePath)) { error = TranslationServer.Translate("Elegí una imagen."); return null; }

        var source = Image.LoadFromFile(sourcePath);
        if (source == null && sourcePath.StartsWith("/"))
        {
            byte[] imgBytes = null;
            try { imgBytes = System.IO.File.ReadAllBytes(sourcePath); }
            catch { }
            if (imgBytes != null && imgBytes.Length > 0)
            {
                string tmp = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "sholoop_src.png");
                System.IO.File.WriteAllBytes(tmp, imgBytes);
                source = Image.LoadFromFile(tmp);
                try { System.IO.File.Delete(tmp); } catch { }
            }
        }
        if (source == null) { error = TranslationServer.Translate("No pude leer esa imagen."); return null; }

        var config = new ConfigFile();
        config.Load(ConfigPath);   // missing file is fine: we're about to create it

        string slug = NextSlug(config);
        var portrait = MakePortrait(source, RingColors[config.GetSections().Length % RingColors.Length]);

        DirAccess.MakeDirRecursiveAbsolute(ImageDir);
        if (portrait.SavePng($"{ImageDir}/{slug}.png") != Error.Ok)
        {
            error = TranslationServer.Translate("No pude guardar la imagen.");
            return null;
        }

        config.SetValue(slug, "name", name);
        config.SetValue(slug, "description", description);
        config.Save(ConfigPath);

        CharacterCatalog.Invalidate();
        return slug;
    }

    public static void Delete(string slug)
    {
        var config = new ConfigFile();
        if (config.Load(ConfigPath) == Error.Ok && config.HasSection(slug))
        {
            config.EraseSection(slug);
            config.Save(ConfigPath);
        }

        string imagePath = $"{ImageDir}/{slug}.png";
        if (FileAccess.FileExists(imagePath))
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(imagePath));

        CharacterCatalog.Invalidate();
    }

    // user:// is outside the import pipeline, so the PNG has to be decoded at runtime instead of
    // resolved to an imported CompressedTexture2D. Goes through CharacterCatalog.Texture, which
    // caches the result.
    public static Texture2D LoadTexture(string userPath)
    {
        var image = Image.LoadFromFile(userPath);
        return image == null ? null : ImageTexture.CreateFromImage(image);
    }

    // Sequential rather than derived from the name, so two characters called the same thing don't
    // collide and a name with accents or spaces doesn't have to be sanitised into a filename.
    private static string NextSlug(ConfigFile config)
    {
        for (int i = 1; ; i++)
        {
            string candidate = SlugPrefix + i;
            if (!config.HasSection(candidate) && !FileAccess.FileExists($"{ImageDir}/{candidate}.png"))
                return candidate;
        }
    }

    // Centre-crop to a square, resize, then stamp a hard neon frame on the edge -- the same shape
    // tools/prep_character_sprite.py produces, so player portraits and bundled ones match.
    //
    // KEEP THESE TWO IN STEP. The algorithm lives in two places by necessity (one runs at build time
    // in Python, one at runtime in C#) and they have drifted apart once already. If you change the
    // frame here, change prep_character_sprite.make_portrait in the same commit.
    private static Image MakePortrait(Image source, Color ring)
    {
        int side = Mathf.Min(source.GetWidth(), source.GetHeight());
        var square = source.GetRegion(new Rect2I(
            (source.GetWidth() - side) / 2, (source.GetHeight() - side) / 2, side, side));

        square.Resize(PortraitSize, PortraitSize, Image.Interpolation.Lanczos);
        square.Convert(Image.Format.Rgba8);

        // No feather and no distance test any more. The old version masked to a circle, which has no
        // way to end except in a soft antialiased rim -- exactly the edge the pixel-art pass removed
        // everywhere else. A square frame is exact: a pixel is either in the border or it isn't.
        int border = Mathf.Max(2, Mathf.RoundToInt(PortraitSize * RingWidthRatio));

        for (int y = 0; y < PortraitSize; y++)
        {
            for (int x = 0; x < PortraitSize; x++)
            {
                int edge = Mathf.Min(
                    Mathf.Min(x, y),
                    Mathf.Min(PortraitSize - 1 - x, PortraitSize - 1 - y));
                if (edge < border) square.SetPixel(x, y, ring);
            }
        }

        return square;
    }

    // Godot's LineEdit/TextEdit will happily accept a newline-laden paste; the config file stores
    // these verbatim, so collapse anything that would break a single-line value.
    public static string Sanitise(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
            builder.Append(c == '\n' || c == '\r' || c == '\t' ? ' ' : c);
        return builder.ToString().Trim();
    }
}
