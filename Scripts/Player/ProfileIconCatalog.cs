namespace ShooterLoop;

using System.Collections.Generic;
using Godot;

public readonly struct ProfileIconOption
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string TexturePath { get; init; }
    public CosmeticTier Tier { get; init; }
    public int Cost { get; init; }
}

// The emblem shown next to the CEO's name in MainMenu's identity box — picked first at onboarding,
// expanded later in the Tienda. Unlike CosmeticCatalog's palette, an option here IS an image, not a
// colour applied to an existing shape, so it gets its own small catalog rather than adding a texture
// field to CosmeticOption (which every render site — bullets, trails, the HUD border... — assumes is
// a Color). Ownership/equip still goes through GameManager's generic cosmetic system via
// CosmeticCategory.ProfileIcon; only the data shape and the shop's row rendering are new.
public static class ProfileIconCatalog
{
    // Reuses CosmeticCatalog.DefaultId ("original") so GameManager.IsCosmeticOwned's existing rule —
    // DefaultId is always owned for any category, no purchase or grant needed — covers the "no icon
    // picked yet" case for free. An existing save from before this feature existed has this equipped
    // and Texture(DefaultId) resolves to null, so the identity box simply shows no icon, same as today.
    public const string DefaultId = CosmeticCatalog.DefaultId;

    public static readonly ProfileIconOption[] Options =
    {
        new() { Id = "rayo", Name = "Rayo", TexturePath = "res://Assets/Sprites/UI/icon_profile_rayo.png", Tier = CosmeticTier.Comun, Cost = 0 },
        new() { Id = "cohete", Name = "Cohete", TexturePath = "res://Assets/Sprites/UI/icon_profile_cohete.png", Tier = CosmeticTier.Comun, Cost = 0 },
        new() { Id = "mira", Name = "Mira", TexturePath = "res://Assets/Sprites/UI/icon_profile_mira.png", Tier = CosmeticTier.Raro, Cost = 25 },
        new() { Id = "corona", Name = "Corona", TexturePath = "res://Assets/Sprites/UI/icon_profile_corona.png", Tier = CosmeticTier.Raro, Cost = 40 },
        new() { Id = "diamante", Name = "Diamante", TexturePath = "res://Assets/Sprites/UI/icon_profile_diamante.png", Tier = CosmeticTier.Epico, Cost = 70 },
        new() { Id = "calavera", Name = "Calavera", TexturePath = "res://Assets/Sprites/UI/icon_profile_calavera.png", Tier = CosmeticTier.Epico, Cost = 85 },
    };

    // Offered as pickable, no-cost options during onboarding — everything else waits for the Tienda.
    public static IEnumerable<ProfileIconOption> FreeOptions
    {
        get
        {
            foreach (var option in Options)
                if (option.Cost == 0) yield return option;
        }
    }

    public static ProfileIconOption? Find(string id)
    {
        foreach (var option in Options)
            if (option.Id == id) return option;
        return null;
    }

    // Cached the same way CharacterCatalog.Texture caches portraits — the shop, the onboarding picker
    // and MainMenu's identity box all ask for the same handful of textures.
    private static readonly Dictionary<string, Texture2D> _textures = new();

    /// <summary>Null for DefaultId (no icon picked) or an unknown/removed id — callers should hide
    /// the icon slot rather than show a broken texture.</summary>
    public static Texture2D Texture(string id)
    {
        if (id == DefaultId) return null;
        if (_textures.TryGetValue(id, out var cached) && cached != null) return cached;

        var option = Find(id);
        if (option == null) return null;

        var texture = GD.Load<Texture2D>(option.Value.TexturePath);
        if (texture != null) _textures[id] = texture;
        return texture;
    }
}
