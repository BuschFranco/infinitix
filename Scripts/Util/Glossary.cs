namespace ShooterLoop;

// One name per concept, for every screen that shows it.
//
// This exists because the same thing had up to four names depending on where you looked: fire rate
// was "CAD" in the HUD, "Cadencia" in the pause menu and "Fuego Rápido" in the shop; kills were
// "BAJAS", "Eliminados" and "Enemigos"; a level was "Nv" in four places and "Lv" in one. None of
// that was a typo — each screen was written on its own and picked its own wording, which is exactly
// the failure mode a shared constant prevents.
//
// The split this encodes: a STAT is a number the player watches (Daño, Cadencia, Alcance), a REWARD
// is a product they buy ("Balas Afiladas", "Fuego Rápido"). Those stay different vocabularies on
// purpose — the reward names carry the game's flavour and shouldn't be flattened into stat labels —
// but each side must be internally consistent, and each reward's description names the stat it
// moves so the player can connect the two.
//
// Same shape as Palette and BuildCatalog: presentation only, no logic, no scene-tree knowledge.
//
// Fields are properties, not const strings, so each one re-resolves against whatever language is
// active right now via TranslationServer -- a const is baked in at compile time and could never be
// anything but Spanish. The Spanish text inside each getter IS the translation key (see
// docs/localization.md): Assets/Localization/strings.csv keys on the original Spanish string, so
// every existing call site (Glossary.Damage, etc.) keeps working unchanged.
public static class Glossary
{
    // --- Stats ---
    // Deliberately unabbreviated. "CAD" saved six characters in the HUD and cost the player any
    // chance of connecting it to the "Fuego Rápido" they had just bought.
    public static string Damage => TranslationServer.Translate("Daño");
    public static string FireRate => TranslationServer.Translate("Cadencia");
    public static string Range => TranslationServer.Translate("Alcance");
    public static string Crit => TranslationServer.Translate("Crítico");
    public static string Kills => TranslationServer.Translate("Bajas");
    public static string Pierce => TranslationServer.Translate("Perforación");
    public static string Dodge => TranslationServer.Translate("Esquiva");

    // --- Shared vocabulary ---
    // "Nv" everywhere. The pause menu's powers block used "Lv" while every other screen — including
    // the pause menu's own status block, three lines above — used "Nv".
    public static string LevelPrefix => TranslationServer.Translate("Nv");

    // One phrasing for "you can't improve this any further". There were five: "Al tope",
    // "ya estás en el tope", "estás al tope y a full", "ya estás a full", and "(tope N)".
    public static string AtCap => TranslationServer.Translate("Al máximo");
    public static string AtCapSentence => TranslationServer.Translate("ya estás al máximo");
    public static string Owned => TranslationServer.Translate("Ya lo tenés");
    public static string OwnedBetter => TranslationServer.Translate("Ya tenés esto o mejor");

    // "tier" was internal jargon leaking into player-facing copy — the cards themselves never use
    // the word, they say COMÚN/RARO/ÉPICO/LEGENDARIO.
    public static string Rarity => TranslationServer.Translate("rareza");

    // --- Ability icons ---
    // The HUD identifies seven abilities by a single letter each, with no legend anywhere and no
    // hover to hang a tooltip off (this is a touch game). These are the letter→name pairs the pause
    // menu's legend prints, and they're the single source both it and the HUD scene agree on.
    public static (string Glyph, string Name)[] AbilityLegend => new (string, string)[]
    {
        ("L", TranslationServer.Translate("Láser")),
        ("M", TranslationServer.Translate("Misil")),
        ("N", TranslationServer.Translate("Mina")),
        ("O", TranslationServer.Translate("Onda de Choque")),
        ("V", TranslationServer.Translate("Vendaval")),
        ("R", TranslationServer.Translate("Regeneración de escudo")),
        ("U", TranslationServer.Translate("Ultimate")),
    };
}
