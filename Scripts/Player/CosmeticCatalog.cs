namespace ShooterLoop;

using Godot;

// Which visual system a cosmetic colour applies to. Ownership is tracked per (category, id) pair —
// buying "Rojo" for Bullet doesn't grant it for free on Trail — even though the colour list below is
// shared across every category.
//
// ADDING A CATEGORY is deliberately cheap: add the member, give it a Label and a BaseColor below, and
// resolve it at the one place that renders it. GameManager stores equipped ids in a dictionary keyed
// by this enum and the shop builds its rows by iterating it, so neither needs touching. (It used to
// cost four properties, a switch case, a save line, a load line and a hand-placed scene row.)
public enum CosmeticCategory
{
    Bullet,
    Trail,
    Outline,
    Arena,
    Shield,
    Blades,
    // These three colour a Control's StyleBoxFlat border rather than a CanvasItem property, so they
    // go through Juice.ApplyCosmeticToStyleBox instead of Juice.ApplyCosmetic — see that method for
    // why (no Epico Shimmer: a UI border isn't in the arena's glow pass either way).
    Marco,
    Hud,
    // Unlike every other category, "Original" here has no pre-existing look to reproduce — there was
    // no kill effect before this category existed. See BaseColor below.
    KillEffect,
    // Not a colour at all — an actual picked image, shown next to the player's name in MainMenu's
    // identity box. Reuses this same enum (and GameManager's generic ownership/equip machinery) purely
    // for the free persistence; its options, catalog shape and shop rendering all live in
    // ProfileIconCatalog instead of here, since CosmeticOption below has no texture field.
    ProfileIcon,
}

// Rarity, which drives price and the frame drawn around a swatch. This is not decoration: the tiers
// differ in how they actually render. See Options below.
public enum CosmeticTier { Comun, Raro, Epico }

public readonly struct CosmeticOption
{
    public string Id { get; init; }
    public string Name { get; init; }
    public Color Color { get; init; }
    public int Cost { get; init; }
    public CosmeticTier Tier { get; init; }

    /// <summary>The far end of an Epico colour's animation. Unset (alpha 0) means it doesn't move.</summary>
    public Color Pulse { get; init; }

    /// <summary>Seconds for one full there-and-back cycle. This is the character of the effect: a
    /// tenth of a second reads as an electrical crackle, a quarter as a flame, most of a second as
    /// something breathing.</summary>
    public float Period { get; init; }

    public bool IsAnimated => Period > 0f && Pulse.A > 0f;
}

// Presentation only, same role as CharacterCatalog: no gameplay logic lives here.
//
// One shared colour list for every category — a colour is a colour, only where it gets applied
// differs — rather than per-category palettes with no reason to differ. "Original" is free, always
// owned, and resolves to White/identity so equipping it reproduces exactly what the game looked like
// before this feature existed (see GameManager.IsCosmeticOwned).
public static class CosmeticCatalog
{
    public const string DefaultId = "original";

    // The tiers are a real rendering difference, not a price label.
    //
    // Arena.tscn's WorldEnvironment has glow_hdr_threshold = 0.85 with glow_hdr_scale = 2.0, so what
    // a colour does to the bloom is decided by its brightest channel:
    //
    //   Comun  — every channel below 0.85, so it never crosses the threshold and never blooms. These
    //            read as matte, which no colour in the shop could do before; they're a genuinely
    //            different look, not just a cheaper one.
    //   Raro   — the five colours this shop shipped with, all of which already peak at 1.0 and bloom.
    //            Their ids and names are untouched so nobody's purchases or equipped choices break.
    //   Epico  — channels ABOVE 1.0. Godot's Color doesn't clamp, and past the HDR threshold the glow
    //            scales with how far over you are, so these blow out into the bloom in a way an
    //            ordinary colour can't reach no matter how saturated it is.
    //
    // The catch worth knowing: the main menu has no WorldEnvironment, so an Epico swatch previews the
    // same as a Raro one in the shop and only separates in the arena. That's what the tier frame on
    // the swatch is for.
    public static readonly CosmeticOption[] Options =
    {
        new() { Id = DefaultId, Name = "Original", Color = Colors.White, Cost = 0, Tier = CosmeticTier.Comun },

        // --- Comun: matte, below the bloom threshold ---
        new() { Id = "ceniza", Name = "Ceniza", Color = new Color("a8b0bc"), Cost = 8, Tier = CosmeticTier.Comun },
        new() { Id = "arena", Name = "Arena", Color = new Color("cbb98f"), Cost = 8, Tier = CosmeticTier.Comun },
        new() { Id = "musgo", Name = "Musgo", Color = new Color("7fa87c"), Cost = 10, Tier = CosmeticTier.Comun },
        new() { Id = "oxido", Name = "Óxido", Color = new Color("b3703f"), Cost = 10, Tier = CosmeticTier.Comun },
        new() { Id = "bruma", Name = "Bruma", Color = new Color("9d94c4"), Cost = 12, Tier = CosmeticTier.Comun },

        // --- Raro: the original five, plus two that fill obvious gaps in the hue wheel ---
        new() { Id = "rojo", Name = "Rojo", Color = new Color("ff4b4b"), Cost = 10, Tier = CosmeticTier.Raro },
        new() { Id = "azul", Name = "Azul", Color = new Color("4fa8ff"), Cost = 15, Tier = CosmeticTier.Raro },
        new() { Id = "verde_lima", Name = "Verde Lima", Color = new Color("9bff4d"), Cost = 15, Tier = CosmeticTier.Raro },
        new() { Id = "dorado", Name = "Dorado", Color = new Color("ffe066"), Cost = 20, Tier = CosmeticTier.Raro },
        new() { Id = "violeta", Name = "Violeta", Color = new Color("c65bff"), Cost = 25, Tier = CosmeticTier.Raro },
        new() { Id = "cian", Name = "Cian", Color = new Color("3dfff0"), Cost = 22, Tier = CosmeticTier.Raro },
        new() { Id = "rosa", Name = "Rosa", Color = new Color("ff5fa8"), Cost = 22, Tier = CosmeticTier.Raro },

        // --- Epico: HDR, past 1.0 on purpose, and the only tier that moves ---
        //
        // Each one animates between Color and Pulse, and the period is what separates them: Plasma
        // snaps white nine times a second and reads as electricity, Fusión rolls between orange and a
        // deep ember at roughly the rate a flame gutters, and Vacío breathes. Without the differing
        // periods all three would just be "a colour that pulses".
        new()
        {
            Id = "plasma", Name = "Plasma", Cost = 70, Tier = CosmeticTier.Epico,
            Color = new Color(2.1f, 0.35f, 1.5f), Pulse = new Color(3.2f, 2.4f, 3.4f), Period = 0.11f,
        },
        new()
        {
            Id = "fusion", Name = "Fusión", Cost = 80, Tier = CosmeticTier.Epico,
            Color = new Color(2.3f, 1.45f, 0.25f), Pulse = new Color(3.0f, 0.55f, 0.08f), Period = 0.26f,
        },
        new()
        {
            Id = "vacio", Name = "Vacío", Cost = 90, Tier = CosmeticTier.Epico,
            Color = new Color(0.4f, 1.0f, 2.5f), Pulse = new Color(1.7f, 0.45f, 3.2f), Period = 0.62f,
        },
    };

    public static CosmeticOption Get(string id)
    {
        foreach (var option in Options)
            if (option.Id == id) return option;

        // An id can vanish if a colour is ever removed from the catalog after being equipped —
        // fall back to Original rather than crash on a stale settings.cfg value.
        return Options[0];
    }

    public static Color ColorFor(string id) => Get(id).Color;

    // The ownership-tracking key: one entry per (category, id) pair in GameManager.OwnedCosmetics.
    public static string ItemKey(CosmeticCategory category, string id) => $"{category}:{id}";

    /// <summary>
    /// The colour a render site should use: its own default when nothing is equipped, otherwise the
    /// cosmetic — keeping the base colour's alpha, since that alpha is a design decision about how
    /// present the element should be (the range ring is 0.3, the grid 0.16) and not something a
    /// colour choice should overwrite.
    /// </summary>
    // Lived in ArenaBounds as a private helper until there were more than two categories wanting it.
    public static Color Resolve(string cosmeticId, Color baseColor) =>
        cosmeticId == DefaultId
            ? baseColor
            : new Color(ColorFor(cosmeticId), baseColor.A);

    /// <summary>The other end of an animated colour, or the same colour when it doesn't move — so a
    /// caller can ask for both ends unconditionally and let the period decide whether to animate.</summary>
    public static Color ResolvePulse(string cosmeticId, Color baseColor)
    {
        if (cosmeticId == DefaultId) return baseColor;
        var option = Get(cosmeticId);
        return option.IsAnimated ? new Color(option.Pulse, baseColor.A) : new Color(option.Color, baseColor.A);
    }

    // Row headings for the shop, kept here rather than in CosmeticsShopMenu.tscn so a new category
    // needs no scene edit at all.
    public static string Label(CosmeticCategory category) => category switch
    {
        // Bullet also colours the fire-range ring: the ring marks where your shots reach, so it reading
    // in the shot's own colour is the point of it -- a separate slot for it just meant the two could
    // disagree about what "your weapon" looks like.
    CosmeticCategory.Bullet => "Color de los disparos y el alcance",
        CosmeticCategory.Trail => "Color de la estela",
        CosmeticCategory.Outline => "Color del borde del personaje",
        CosmeticCategory.Arena => "Color del mapa",
        CosmeticCategory.Shield => "Color del escudo",
        CosmeticCategory.Blades => "Color de las cuchillas",
        CosmeticCategory.Marco => "Color del marco del retrato",
        CosmeticCategory.Hud => "Color del borde del HUD",
        CosmeticCategory.KillEffect => "Color del efecto al matar",
        _ => category.ToString(),
    };

    // Short enough to fit a category tab. Label() above is the full sentence, shown once the tab is
    // selected -- a tab row of "Color del borde del personaje" would not fit anything.
    public static string ShortLabel(CosmeticCategory category) => category switch
    {
        CosmeticCategory.Bullet => "Disparos",
        CosmeticCategory.Trail => "Estela",
        CosmeticCategory.Outline => "Borde",
        CosmeticCategory.Arena => "Mapa",
        CosmeticCategory.Shield => "Escudo",
        CosmeticCategory.Blades => "Cuchillas",
        CosmeticCategory.Marco => "Marco",
        CosmeticCategory.Hud => "HUD",
        CosmeticCategory.KillEffect => "Muertes",
        _ => category.ToString(),
    };

    /// <summary>What "Original" actually looks like for a category — used both as the render-site
    /// fallback and as the shop's preview swatch for the free option.</summary>
    // The catalog's own Color for "Original" is White (identity — apply no tint), which is not what
    // the game looks like. The shop showed a white square for every category before this existed.
    public static Color BaseColor(CosmeticCategory category) => category switch
    {
        CosmeticCategory.Bullet => Palette.PlayerBullet,
        CosmeticCategory.Trail => Palette.PlayerBullet,
        // Outline's default is "no outline at all", so there is no colour to preview — shown hollow.
        CosmeticCategory.Outline => new Color(0f, 0f, 0f, 0f),
        CosmeticCategory.Arena => Palette.ArenaBounds,
        CosmeticCategory.Shield => ShieldAuraBase,
        CosmeticCategory.Blades => Palette.OrbitBlade,
        // Reproduces the Swatch panel's current hardcoded border in CharacterSelectMenu.tscn/HUD.tscn.
        CosmeticCategory.Marco => new Color(0.4902f, 0.9922f, 0.9961f, 0.3f),
        // Palette.HudPanelBorder existed unused until this category — matches the TopBarPanel border
        // already hardcoded in HUD.tscn.
        CosmeticCategory.Hud => Palette.HudPanelBorder,
        // No prior effect to reproduce (see the enum's doc comment) — just a sensible default.
        CosmeticCategory.KillEffect => Palette.EnemyBullet,
        _ => Colors.White,
    };

    // Player.tscn's ShieldAura sets this colour on the node itself; it has no Palette entry because
    // nothing else ever referenced it. Duplicated here so the shop can preview it and Player can
    // resolve against it — if the scene's colour changes, change this too.
    public static readonly Color ShieldAuraBase = new(0.31f, 0.66f, 1f, 0.35f);

    /// <summary>Frame colour for a swatch, so rarity reads at a glance without a text label.</summary>
    public static Color TierColor(CosmeticTier tier) => tier switch
    {
        CosmeticTier.Comun => new Color("8a94a6"),
        CosmeticTier.Raro => new Color("4fa8ff"),
        CosmeticTier.Epico => new Color("ffb43d"),
        _ => Colors.White,
    };

    public static string TierName(CosmeticTier tier) => tier switch
    {
        CosmeticTier.Comun => "Común",
        CosmeticTier.Raro => "Raro",
        CosmeticTier.Epico => "Épico",
        _ => "",
    };
}
