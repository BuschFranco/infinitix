namespace ShooterLoop;

// Side-by-side companion to the pause menu: shows every reward/item the run has equipped
// and the full build list with per-requirement progress — closing the gap (current value vs
// needed) plus the exact catalog rewards that would satisfy each unmet requirement.
// Requirements and thresholds come from Player.GetBuildRequirements, the same source
// CheckBuildClass evaluates, so the display can never disagree with activation logic.
public partial class LoadoutMenu : PanelContainer
{
    private RichTextLabel _itemsLabel;
    private RichTextLabel _buildsLabel;

    // Which build classes were fully met the last time Refresh ran, so a class flipping
    // unmet→met between two pause-menu opens gets a pop instead of the whole panel just
    // silently reprinting with a checkmark added.
    private readonly HashSet<Player.BuildClass> _metLastRefresh = new();

    public override void _Ready()
    {
        var title = GetNode<Label>("ScrollContainer/Box/Title");
        UIUtil.AddSpeedLines(title.GetParent<Control>(), title.GetIndex());

        _itemsLabel = GetNode<RichTextLabel>("ScrollContainer/Box/ItemsLabel");
        _buildsLabel = GetNode<RichTextLabel>("ScrollContainer/Box/BuildsLabel");
    }

    public void Refresh(Player player)
    {
        _itemsLabel.Text = player == null ? "" : BuildItemsText(player);
        _buildsLabel.Text = player == null ? "" : BuildBuildsText(player);

        if (player == null) return;

        bool anyNewlyMet = false;
        foreach (var cls in BuildCatalog.ClassOrder)
        {
            bool allMet = BuildCatalog.AllMet(cls, player);
            if (allMet && !_metLastRefresh.Contains(cls))
                anyNewlyMet = true;
            if (allMet) _metLastRefresh.Add(cls);
            else _metLastRefresh.Remove(cls);
        }

        if (anyNewlyMet)
            Juice.ValuePop(_buildsLabel, 1.05f, 0.25f);
    }

    // --- Items equipados ---

    private string BuildItemsText(Player p)
    {
        var lines = new List<string>();
        string group = null;

        void Section(string name)
        {
            string translated = Tr(name);
            if (group == translated) return;
            group = translated;
            lines.Add($"[color=#7dfdfe]{translated}[/color]");
        }

        void Row(Func<Player, bool> owned, string name, Func<Player, string> value)
        {
            if (!owned(p)) return;
            lines.Add($"[color=#cfd8e8]• {Tr(name)}[/color]  [color=#9aa8ba]{value(p)}[/color]");
        }

        Section("ARMAS");
        Row(p => p.BulletDamage > 0, "Balas Afiladas", p => string.Format(Tr("{0} daño"), p.BulletDamage));
        Row(p => p.FireRate > 0, "Fuego Rápido", p => $"{p.FireRate:0.0}/s");
        Row(p => p.FireRange > 0, "Alcance de Disparo", p => $"{p.FireRange:0}");
        Row(p => p.CritChance > 0, "Crítico", p => $"{p.CritChance:0}% (x2)");
        Row(p => p.BulletPierce > 0, "Perforación", p => string.Format(Tr("{0} enemigos"), p.BulletPierce));
        Row(p => p.RicochetCount > 0, "Rebote", p => string.Format(Tr("{0} rebotes"), p.RicochetCount));
        Row(p => p.BulletKnockback > 0, "Retroceso", p => $"{p.BulletKnockback:0}");
        Row(p => p.HasExtraProjectile, "Disparo en Diagonal", _ => Tr("activo"));
        Row(p => p.ExtraFiringLines > 0, "Disparo Paralelo", p => string.Format(Tr("{0} líneas (tope {1})"), p.ExtraFiringLines, Player.MaxExtraFiringLinesCap));
        Row(p => p.CoinBonusPercent > 0, "Botín", p => string.Format(Tr("+{0}% monedas"), p.CoinBonusPercent.ToString("0")));
        Row(p => p.XpBonusPercent > 0, "Sabiduría", p => $"+{p.XpBonusPercent:0}% XP");
        Row(p => p.FortuneBonus > 0, "Fortuna", p => string.Format(Tr("+{0}% Raro/Épico/Legendario"), p.FortuneBonus.ToString("0")));
        Row(p => p.DodgeChance > 0, "Esquiva", p => $"{p.DodgeChance:0}%");

        Section("DEFENSAS");
        // Both ceilings are read from the constants rather than typed here. This line used to say
        // "(máx 6)" against a cap that is 4 — the UI was promising two lives the game would never
        // grant, and a player could spend coins chasing them.
        Row(p => p.MaxLives > 0, "Vidas", p => string.Format(Tr("{0} (tope {1})"), p.MaxLives, Player.MaxLivesCap));
        Row(p => p.MaxShieldCharges > 0, "Escudos", p => string.Format(Tr("{0} cargas (tope {1})"), p.MaxShieldCharges, Player.MaxShieldChargesCap));
        Row(p => p.ShieldRegenPerMinute > 0, "Regeneración", p => string.Format(Tr("{0} cargas/min"), p.ShieldRegenPerMinute.ToString("0.#")));

        // These used to print a bracketed letter beside the name ("[N] Mina"), because the HUD
        // identified each cooldown icon by exactly that letter and there was no legend anywhere else.
        // The icons draw real symbols now, so the letters would be a legend for something that no
        // longer exists — worse than no legend. They're plain rows like everything else, and the
        // HUD's own first-appearance callout is what teaches each symbol.
        Section("PODERES");
        Row(p => p.OrbitCount > 0, "Cuchillas Orbitales", p => $"{p.OrbitCount}");
        Row(p => p.CompanionStatPercent > 0, "Dron", p => string.Format(Tr("{0}% de tus estadísticas"), (p.CompanionStatPercent * 100f).ToString("0")));
        Row(p => p.LaserLevel > 0, "Láser", p => $"{Glossary.LevelPrefix}{p.LaserLevel}");
        Row(p => p.MissileLevel > 0, "Misil", p => $"{Glossary.LevelPrefix}{p.MissileLevel}");
        Row(p => p.MineLevel > 0, "Mina", p => $"{Glossary.LevelPrefix}{p.MineLevel}");
        Row(p => p.OndaLevel > 0, "Onda de Choque", p => $"{Glossary.LevelPrefix}{p.OndaLevel}");
        Row(p => p.VendavalLevel > 0, "Vendaval", p => $"{Glossary.LevelPrefix}{p.VendavalLevel}");
        // Shield regen is NOT repeated here. It was listed in both sections — once under DEFENSAS
        // with its rate, once here purely to carry its "[R]" legend entry — and with the letters gone
        // the second copy is the same predicate and the same number under a longer name.
        Row(p => p.BurnLevel > 0, "Incendiario", p => $"{Glossary.LevelPrefix}{p.BurnLevel}");
        Row(p => p.ThornsDamage > 0, "Escudo Voltáico", p => string.Format(Tr("{0} daño"), p.ThornsDamage.ToString("0")));
        Row(p => p.HasMagnetReward, "Imán", _ => Tr("activo"));

        Section("ULTIMATE");
        Row(p => p.EquippedUltimate != null, "Ultimate", p => Tr(UltimateKindNames.Display(p.EquippedUltimate.Value)));

        return lines.Count == 0
            ? $"[color=#9aa8ba]{Tr("No tenés items equipados todavía.")}[/color]"
            : string.Join("\n", lines);
    }

    // --- Builds con progreso ---

    private string BuildBuildsText(Player p)
    {
        var lines = new List<string>();

        foreach (var cls in BuildCatalog.ClassOrder)
        {
            var reqs = Player.GetBuildRequirements(cls);
            bool allMet = BuildCatalog.AllMet(cls, p);
            string bonus = BuildCatalog.Bonus(cls);

            if (allMet)
            {
                lines.Add($"[color=#7dfdfe]{Tr(BuildCatalog.Name(cls))}[/color]  [color=#7fff7f]{Tr("✓ ACTIVA")}[/color]  →  [color=#7fff7f]{Tr(bonus)}[/color]");
            }
            else
            {
                lines.Add($"[color=#9aa0b0]{Tr(BuildCatalog.Name(cls))}[/color]  →  [color=#86a0b8]{Tr(bonus)}[/color]");
            }

            foreach (var req in reqs)
            {
                if (req.IsMet(p))
                {
                    lines.Add($"   [color=#7fff7f]✓[/color] {Tr(BuildCatalog.RequirementText(req))}  [color=#9aa8ba]{string.Format(Tr("(tenés {0})"), BuildCatalog.CurrentText(req, p))}[/color]");
                }
                else
                {
                    string missing = string.Join(", ", BuildCatalog.RewardsThatFulfill(req, p));
                    lines.Add($"   [color=#ffc24a]✗[/color] {Tr(BuildCatalog.RequirementText(req))}  [color=#9aa8ba]{string.Format(Tr("(tenés {0})"), BuildCatalog.CurrentText(req, p))}[/color]" +
                              (missing.Length > 0 ? $"  [color=#ffc24a]{string.Format(Tr("falta: {0}"), missing)}[/color]" : ""));
                }
            }
        }

        return string.Join("\n", lines);
    }
}