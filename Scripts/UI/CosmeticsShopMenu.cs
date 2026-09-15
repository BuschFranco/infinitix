namespace ShooterLoop;

using System.Collections.Generic;
using Godot;

// Where Libras' second sink lives: swap purely cosmetic colours for a currency whose only other use
// is the three "secreto" pilot slots.
//
// WHY THIS IS A TAB PICKER AND NOT ONE ROW PER CATEGORY
// -----------------------------------------------------
// Every category offers the same colour list, so the obvious layout -- one row of swatches per
// category -- means printing the identical palette once per category. At four categories and six
// colours that was merely repetitive; at six and sixteen it's 96 squares in a scroll view, and
// finding the one you want means counting rows.
//
// So the category is chosen first, on a compact grid of tabs, and only that category's palette is
// built. The tabs double as a summary of the whole loadout: each carries a chip of the colour
// currently equipped for it, so one glance says what your ship looks like without opening anything.
//
// The palette itself is grouped by tier with a heading per group, because the tiers are a real
// rendering difference (see CosmeticCatalog.Options) and pricing that isn't explained just looks
// arbitrary.
public partial class CosmeticsShopMenu : Control
{
    private PanelContainer _panel;
    private ScrollContainer _scroll;
    private Label _librasLabel;
    private Button _closeButton;
    private VBoxContainer _content;
    private Tween _librasTween;

    private CosmeticCategory _selected = CosmeticCategory.Bullet;

    private readonly Dictionary<CosmeticCategory, Button> _tabs = new();
    private readonly List<(Button Button, string Id)> _swatches = new();
    private Label _sectionLabel;
    private VBoxContainer _paletteBox;

    // Personajes isn't a CosmeticCategory — a locked pilot has a portrait/name/cost, not a colour
    // that gets equipped elsewhere, so it gets its own view mode instead of a swatch grid.
    private Button _charactersTabButton;
    private bool _charactersTabSelected;

    private const float SwatchSize = 44f;

    // 7 x 44px swatches plus 6 x 6px gaps is 344px, inside the ~382px the 420-wide panel leaves after
    // UIUtil.CreatePanelStyle's 16px content margin and 3px border on each side.
    private const int PaletteColumns = 7;
    private const int TabColumns = 4;

    // Locked (not-yet-bought) swatches dim to this — same "disabled" convention RewardCard and
    // CharacterSelectMenu already use, so a locked colour reads the same way a locked pilot does.
    private const float LockedAlpha = 0.55f;

    public override void _Ready()
    {
        Visible = false;

        _panel = GetNode<PanelContainer>("CenterContainer/Panel");
        _scroll = GetNode<ScrollContainer>("CenterContainer/Panel/Scroll");
        _librasLabel = GetNode<Label>("CenterContainer/Panel/Scroll/Box/LibrasLabel");
        _closeButton = GetNode<Button>("CenterContainer/Panel/Scroll/Box/CloseButton");
        _content = GetNode<VBoxContainer>("CenterContainer/Panel/Scroll/Box/Content");

        // Magenta, not the generic Player cyan every other screen defaults to -- this is the shop,
        // same "spending" colour the in-run Shop already uses, and it ties straight back to the
        // logo's own magenta half.
        _panel.AddThemeStyleboxOverride("panel", UIUtil.CreatePanelStyle(Palette.ShopPanelBorder));
        var title = GetNode<Label>("CenterContainer/Panel/Scroll/Box/Title");
        UIUtil.AddSpeedLines(title.GetParent<Control>(), title.GetIndex());

        UIUtil.WireDimToClose(GetNode<Control>("Dim"), Close);

        BuildTabs();
        BuildPaletteArea();

        _closeButton.Pressed += Close;
        Juice.WireButtonFeedback(_closeButton);

        FitToOrientation();
    }

    // --- Category tabs -------------------------------------------------------------------------

    private void BuildTabs()
    {
        var grid = new GridContainer { Columns = TabColumns };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        _content.AddChild(grid);

        foreach (CosmeticCategory category in System.Enum.GetValues<CosmeticCategory>())
        {
            var button = new Button
            {
                Text = CosmeticCatalog.ShortLabel(category),
                CustomMinimumSize = new Vector2(0f, 38f),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            button.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
            grid.AddChild(button);
            Juice.WireButtonFeedback(button);

            var captured = category;
            button.Pressed += () => SelectCategory(captured);
            _tabs[category] = button;
        }

        // Not from the enum loop above — Personajes has no colour to equip, it's a separate view
        // mode (see RebuildContent).
        _charactersTabButton = new Button
        {
            Text = "Personajes",
            CustomMinimumSize = new Vector2(0f, 38f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _charactersTabButton.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
        grid.AddChild(_charactersTabButton);
        Juice.WireButtonFeedback(_charactersTabButton);
        _charactersTabButton.Pressed += SelectCharactersTab;
    }

    private void SelectCategory(CosmeticCategory category)
    {
        if (!_charactersTabSelected && _selected == category) return;
        _selected = category;
        _charactersTabSelected = false;
        RebuildContent();
        RefreshTabs();
    }

    private void SelectCharactersTab()
    {
        if (_charactersTabSelected) return;
        _charactersTabSelected = true;
        RebuildContent();
        RefreshTabs();
    }

    // Personajes rebuilds into rows (name/portrait/price) instead of a swatch grid — everything else
    // downstream (Open, SelectCategory, SelectCharactersTab) goes through this instead of calling
    // RebuildPalette directly, so neither has to know which mode is active.
    private void RebuildContent()
    {
        if (_charactersTabSelected) RebuildCharacterRows();
        else RebuildPalette();
    }

    // A tab's border is the colour that category currently renders with, so the row reads as a
    // summary of the equipped loadout rather than eight identical buttons.
    private void RefreshTabs()
    {
        foreach (var (category, button) in _tabs)
        {
            bool active = !_charactersTabSelected && category == _selected;
            Color equipped = GameManager.Instance.CosmeticColor(category, CosmeticCatalog.BaseColor(category));

            // The Outline category's "Original" is fully transparent (no outline at all), which would
            // render as an invisible border. Fall back to the panel's own accent so the tab still has
            // an edge.
            if (equipped.A <= 0.01f) equipped = new Color(Palette.Player, 0.35f);
            equipped.A = 1f;

            var style = new StyleBoxFlat
            {
                BgColor = active ? new Color(equipped, 0.28f) : new Color(0.043f, 0.024f, 0.078f, 0.7f),
                BorderColor = equipped,
            };
            style.SetBorderWidthAll(active ? 3 : 1);
            style.SetContentMarginAll(4f);

            button.AddThemeStyleboxOverride("normal", style);
            button.AddThemeStyleboxOverride("hover", style);
            button.AddThemeStyleboxOverride("pressed", style);
            button.AddThemeColorOverride("font_color", active ? Colors.White : new Color(0.72f, 0.76f, 0.84f));
        }

        // Personajes has no equipped colour to summarize, so it just gets the plain active/inactive
        // treatment every other tab already falls back to for a transparent equipped colour.
        var charStyle = new StyleBoxFlat
        {
            BgColor = _charactersTabSelected ? new Color(Palette.Player, 0.28f) : new Color(0.043f, 0.024f, 0.078f, 0.7f),
            BorderColor = Palette.Player,
        };
        charStyle.SetBorderWidthAll(_charactersTabSelected ? 3 : 1);
        charStyle.SetContentMarginAll(4f);

        _charactersTabButton.AddThemeStyleboxOverride("normal", charStyle);
        _charactersTabButton.AddThemeStyleboxOverride("hover", charStyle);
        _charactersTabButton.AddThemeStyleboxOverride("pressed", charStyle);
        _charactersTabButton.AddThemeColorOverride("font_color", _charactersTabSelected ? Colors.White : new Color(0.72f, 0.76f, 0.84f));
    }

    // --- Palette -------------------------------------------------------------------------------

    private void BuildPaletteArea()
    {
        _sectionLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _sectionLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Body);
        _content.AddChild(_sectionLabel);

        _paletteBox = new VBoxContainer();
        _paletteBox.AddThemeConstantOverride("separation", 6);
        _content.AddChild(_paletteBox);

        RebuildContent();
        RefreshTabs();
    }

    // Rebuilt per category rather than built once and re-styled: the swatches differ per category
    // only in their owned/equipped state, but the *closures* that buy and equip them carry the
    // category, and rebuilding is far simpler than rebinding sixteen handlers.
    private void RebuildPalette()
    {
        foreach (var child in _paletteBox.GetChildren()) child.QueueFree();
        _swatches.Clear();

        _sectionLabel.Text = CosmeticCatalog.Label(_selected);

        CosmeticTier? currentTier = null;
        GridContainer grid = null;

        foreach (var option in CosmeticCatalog.Options)
        {
            if (currentTier != option.Tier)
            {
                currentTier = option.Tier;
                _paletteBox.AddChild(TierHeading(option.Tier));

                grid = new GridContainer { Columns = PaletteColumns };
                grid.AddThemeConstantOverride("h_separation", 6);
                grid.AddThemeConstantOverride("v_separation", 6);
                _paletteBox.AddChild(grid);
            }

            var button = new Button
            {
                CustomMinimumSize = new Vector2(SwatchSize, SwatchSize),
                TooltipText = option.Cost > 0 ? $"{option.Name} ({option.Cost} Libras)" : option.Name,
            };
            button.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
            grid.AddChild(button);
            Juice.WireButtonFeedback(button);

            string id = option.Id;
            int cost = option.Cost;
            button.Pressed += () => OnSwatchPressed(id, cost);

            _swatches.Add((button, id));
        }

        RefreshSwatches();
    }

    // --- Personajes ------------------------------------------------------------------------------
    //
    // Same row shape AchievementsMenu/MissionsMenu already use (bordered panel, name + state on one
    // line, a description underneath) — a locked pilot has a portrait, name and cost, not a colour,
    // so the swatch grid above doesn't fit it.

    private void RebuildCharacterRows()
    {
        foreach (var child in _paletteBox.GetChildren()) child.QueueFree();
        _swatches.Clear();

        _sectionLabel.Text = "Pilotos secretos";

        foreach (var info in CharacterCatalog.All)
        {
            if (!info.RequiresUnlock) continue;
            _paletteBox.AddChild(BuildCharacterRow(info));
        }
    }

    private Control BuildCharacterRow(CharacterInfo info)
    {
        bool unlocked = CharacterCatalog.IsUnlocked(info);

        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.102f, 0.0588f, 0.1686f, 0.75f),
            BorderColor = unlocked ? Palette.Player : new Color(0.4902f, 0.9922f, 0.9961f, 0.25f),
        };
        style.SetBorderWidthAll(2);
        style.SetContentMarginAll(8f);
        panel.AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        panel.AddChild(row);

        // Same texture/tint the carousel and HUD use, dimmed the same way a locked carousel preview
        // already dims — seeing who you're saving up for is part of the motivation.
        var portrait = new TextureRect
        {
            Texture = CharacterCatalog.Texture(info),
            CustomMinimumSize = new Vector2(48f, 48f),
            ExpandMode = TextureRect.ExpandModeEnum.FitWidth,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Modulate = unlocked ? info.Color : new Color(info.Color.R, info.Color.G, info.Color.B, 0.55f),
        };
        row.AddChild(portrait);

        var textBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        textBox.AddThemeConstantOverride("separation", 2);
        row.AddChild(textBox);

        var nameLabel = new Label { Text = info.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Body);
        nameLabel.AddThemeColorOverride("font_color", unlocked ? Colors.White : new Color(0.72f, 0.76f, 0.84f));
        textBox.AddChild(nameLabel);

        var descLabel = new Label
        {
            Text = info.Description ?? "",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        descLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
        descLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.72f, 0.82f));
        textBox.AddChild(descLabel);

        if (unlocked)
        {
            var ownedLabel = new Label { Text = "✓ Desbloqueado" };
            ownedLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Body);
            ownedLabel.AddThemeColorOverride("font_color", Palette.Player);
            row.AddChild(ownedLabel);
        }
        else
        {
            var buyButton = new Button { Text = $"{info.UnlockCost} Libras" };
            buyButton.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
            Juice.WireButtonFeedback(buyButton);
            string slug = info.Slug;
            int cost = info.UnlockCost;
            buyButton.Pressed += () => OnCharacterRowPressed(slug, cost);
            row.AddChild(buyButton);
        }

        return panel;
    }

    private void OnCharacterRowPressed(string slug, int cost)
    {
        if (!GameManager.Instance.TryUnlockCharacter(slug, cost))
        {
            AudioManager.Instance?.Play(AudioManager.Sfx.UiDenied);
            Juice.Shake(_librasLabel, flashColor: Palette.Warning);
            return;
        }

        AudioManager.Instance?.Play(AudioManager.Sfx.UiBuy);
        PulseLibras(cost);
        RebuildCharacterRows();
    }

    private static Label TierHeading(CosmeticTier tier)
    {
        var label = new Label { Text = CosmeticCatalog.TierName(tier) };
        label.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
        label.AddThemeColorOverride("font_color", CosmeticCatalog.TierColor(tier));
        return label;
    }

    private void OnSwatchPressed(string id, int cost)
    {
        var gm = GameManager.Instance;
        if (!gm.IsCosmeticOwned(_selected, id))
        {
            if (!gm.TryBuyCosmetic(_selected, id, cost))
            {
                // The one branch a tap on an unlocked-looking swatch can still fail on: not enough
                // Libras. Owned-already and already-equipped never reach TryBuyCosmetic at all.
                AudioManager.Instance?.Play(AudioManager.Sfx.UiDenied);
                // Shakes and flashes the balance itself red — the number that's actually short is
                // what should read as the problem, not just an anonymous "denied" beep.
                Juice.Shake(_librasLabel, flashColor: Palette.Warning);
                return;
            }

            AudioManager.Instance?.Play(AudioManager.Sfx.UiBuy);
            PulseLibras(cost);
        }

        gm.EquipCosmetic(_selected, id);
        RefreshSwatches();
        RefreshTabs();   // the tab's chip is the equipped colour, so it changes with the selection
    }

    // Same pattern Shop.cs uses for its own coin balance: a scale-pop on the number itself plus a
    // "−N" label that floats up beside it and fades — so spending Libras here reads the same way
    // spending Coins already does in the round shop, not a silent number swap.
    private void PulseLibras(int spent)
    {
        _librasLabel.Text = $"Libras: {GameManager.Instance.Libras}";

        _librasTween?.Kill();
        _librasLabel.PivotOffset = _librasLabel.Size / 2f;
        _librasLabel.Scale = Vector2.One * 1.25f;
        _librasTween = _librasLabel.CreateTween();
        _librasTween.TweenProperty(_librasLabel, "scale", Vector2.One, 0.3f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        Juice.FloatingLabel(this, $"−{spent}", _librasLabel.GlobalPosition + new Vector2(_librasLabel.Size.X + 10f, 0f),
            Palette.Warning, Palette.FontSize.Subtitle, driftY: -26f, holdBeforeFade: 0.3f, lifetime: 0.8f);
    }

    private void RefreshSwatches()
    {
        var gm = GameManager.Instance;
        string equippedId = gm.EquippedCosmetic(_selected);

        foreach (var (button, id) in _swatches)
        {
            var option = CosmeticCatalog.Get(id);
            bool owned = gm.IsCosmeticOwned(_selected, id);
            bool equipped = id == equippedId;
            bool affordable = owned || gm.Libras >= option.Cost;

            // The catalog's own Colour for "Original" is just White (identity — apply no tint), not
            // what the game actually looks like. Show the real per-category default instead, so the
            // swatch reads as a preview of the look you'd get, not a literal Modulate value.
            Color preview = id == CosmeticCatalog.DefaultId
                ? CosmeticCatalog.BaseColor(_selected)
                : option.Color;

            // Outline's default is "no outline at all" — there is no colour to preview, so it gets a
            // dark well and a dash rather than an invisible square.
            bool hollow = preview.A <= 0.01f;
            if (hollow) preview = new Color(0.10f, 0.07f, 0.16f, 1f);
            preview.A = 1f;

            var style = new StyleBoxFlat
            {
                BgColor = owned ? preview : new Color(preview, LockedAlpha),
                // The frame carries rarity, which is the only cue that separates an Epico colour from
                // a Raro one in this menu: the main menu has no WorldEnvironment, so the HDR channels
                // that make Epico bloom in the arena are clamped away in this preview.
                BorderColor = equipped ? Colors.White : CosmeticCatalog.TierColor(option.Tier),
            };
            style.SetBorderWidthAll(equipped ? 4 : 2);

            button.AddThemeStyleboxOverride("normal", style);
            button.AddThemeStyleboxOverride("hover", style);
            button.AddThemeStyleboxOverride("pressed", style);

            // Tooltips don't exist on a touchscreen, so the price has to be on the swatch itself.
            button.Text = hollow ? "—" : equipped ? "✓" : owned ? "" : option.Cost.ToString();
            button.AddThemeColorOverride("font_color", ReadableOn(preview));
            button.AddThemeColorOverride("font_outline_color", Colors.Black);
            button.AddThemeConstantOverride("outline_size", 3);

            // Dimmed rather than disabled: a tap still reaches OnSwatchPressed, which shakes the
            // balance to say *why* it failed. A disabled button would swallow the tap silently.
            button.Modulate = affordable ? Colors.White : new Color(1f, 1f, 1f, 0.7f);
        }
    }

    // Black on a light swatch, white on a dark one. Rec. 601 luma, which is close enough for a
    // one-or-two-character label and needs no colour-space conversion.
    private static Color ReadableOn(Color background)
    {
        float luma = 0.299f * background.R + 0.587f * background.G + 0.114f * background.B;
        return luma > 0.6f ? Colors.Black : Colors.White;
    }

    // Derived from the live viewport rather than a pair of hardcoded landscape/portrait numbers. Those
    // were measured against 648px and were only ever right for the text that existed when they were
    // written -- see UIUtil.FitScrollToViewport.
    private void FitToOrientation()
    {
        UIUtil.FitScrollToViewport(_scroll, _panel);
    }

    public void Open()
    {
        _librasLabel.Text = $"Libras: {GameManager.Instance.Libras}";
        RebuildContent();
        RefreshTabs();
        FitToOrientation();

        // Always reopen at the top — see OptionsMenu for why (a mid-scroll reopen reads as broken).
        _scroll.ScrollVertical = 0;

        Visible = true;
        Juice.ModalIn(_panel);

        GameManager.Instance?.PushBackHandler(this, Close);
    }

    private void Close()
    {
        GameManager.Instance?.PopBackHandler(this);
        Juice.ModalOut(_panel, () => Visible = false);
    }
}
