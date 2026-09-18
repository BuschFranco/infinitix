namespace ShooterLoop;

public partial class MainMenu : Control
{
    public override void _Ready()
    {
        // Character select, Options and Builds are overlay *children* of this scene rather than
        // separate scenes, so this runs once per actual visit to the menu — opening a submenu won't
        // restart the track. (PlayMusic also no-ops when the requested track is already playing.)
        AudioManager.Instance?.PlayMusic(AudioManager.MusicTrack.Menu);

        var startButton = GetNode<Button>("VBoxContainer/ButtonsRow/StartButton");
        var optionsButton = GetNode<Button>("VBoxContainer/ButtonsRow/OptionsButton");
        var buildsButton = GetNode<Button>("VBoxContainer/ButtonsRow/BuildsButton");
        var tiendaButton = GetNode<Button>("VBoxContainer/ButtonsRow/TiendaButton");

        // Typed (and scened) as the base BoxContainer, not HBoxContainer — Godot 4 hard-locks
        // HBoxContainer/VBoxContainer's orientation at construction and silently rejects (with an
        // error log) any later attempt to flip it, so toggling Vertical only actually works on the
        // unspecialized base class. This lets the same four buttons stack in portrait instead of
        // squeezing into a row barely wider than the 648px portrait viewport (170+150*3 plus
        // separation is ~650px, right at the edge). Landscape's 1152px keeps the row as-is.
        var buttonsRow = GetNode<BoxContainer>("VBoxContainer/ButtonsRow");

        void ApplyButtonsRowLayout()
        {
            bool portrait = GameManager.Instance.CurrentOrientation == GameManager.ScreenOrientation.Portrait;
            buttonsRow.Vertical = portrait;
            // Fill in landscape (the row's normal look), ShrinkCenter in portrait — otherwise each
            // button's cross-axis (now horizontal) would stretch to the stack's full width instead
            // of keeping its own pill size. Reapplied every time, not just once, since switching back
            // to landscape from here needs to undo it just as much as portrait needs to set it.
            foreach (var b in new[] { startButton, tiendaButton, optionsButton, buildsButton })
                b.SizeFlagsHorizontal = portrait ? SizeFlags.ShrinkCenter : SizeFlags.Fill;
        }

        ApplyButtonsRowLayout();

        var characterSelect = GetNode<CharacterSelectMenu>("CharacterSelectMenu");
        var gameModeMenu = GetNode<GameModeMenu>("GameModeMenu");
        startButton.Pressed += gameModeMenu.Open;
        startButton.GrabFocus();

        Juice.WireButtonFeedback(startButton);
        Juice.WireButtonFeedback(optionsButton);
        Juice.WireButtonFeedback(buildsButton);
        Juice.WireButtonFeedback(tiendaButton);

        var highScoreLabel = GetNode<Label>("VBoxContainer/HighScoreLabel");
        highScoreLabel.Text = string.Format(Tr("Mejor puntaje: {0}"), GameManager.LoadHighScore());

        // Top-center identity box: name (set once at OnboardingMenu) + account level + Libras, all
        // in one place instead of the level/Libras text that used to float in the centered column
        // (see docs — that column keeps only the XP progress bar now, purely visual). Built here in
        // code, not the .tscn, same reasoning as WrapWithCoinIcon below: keeps the coin-icon markup
        // in one place rather than hand-authoring it twice.
        var playerNameLabel = GetNode<Label>("PlayerInfoBox/PlayerInfoColumn/NameRow/PlayerNameLabel");
        var profileIconButton = GetNode<Button>("PlayerInfoBox/PlayerInfoColumn/NameRow/ProfileIconButton");
        var profileIconRect = GetNode<TextureRect>("PlayerInfoBox/PlayerInfoColumn/NameRow/ProfileIconButton/ProfileIconRect");
        var statsRow = GetNode<HBoxContainer>("PlayerInfoBox/PlayerInfoColumn/StatsRow");
        var levelValueLabel = new Label();
        levelValueLabel.AddThemeFontSizeOverride("font_size", 13);
        levelValueLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.75f, 1f));
        levelValueLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        levelValueLabel.AddThemeConstantOverride("outline_size", 2);
        statsRow.AddChild(levelValueLabel);
        var coinIcon = new TextureRect
        {
            Texture = LibrasCoinIcon,
            CustomMinimumSize = new Vector2(14f, 14f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        statsRow.AddChild(coinIcon);
        var librasValueLabel = new Label();
        librasValueLabel.AddThemeFontSizeOverride("font_size", 13);
        librasValueLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.55f, 1f));
        librasValueLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        librasValueLabel.AddThemeConstantOverride("outline_size", 2);
        statsRow.AddChild(librasValueLabel);

        var accountLevelBar = GetNode<ProgressBar>("VBoxContainer/AccountLevelBarRow/AccountLevelBar");
        RefreshTopBox(playerNameLabel, profileIconRect, levelValueLabel, librasValueLabel, accountLevelBar);

        // Tapping the icon itself opens the quick swap picker (see ProfileIconPicker) instead of
        // sending the player all the way to the Tienda just to switch between icons they already own.
        var iconPicker = GetNode<ProfileIconPicker>("ProfileIconPicker");
        profileIconButton.Pressed += iconPicker.Open;
        Juice.WireButtonFeedback(profileIconButton);
        iconPicker.VisibilityChanged += () =>
        {
            if (!iconPicker.Visible) RefreshTopBox(playerNameLabel, profileIconRect, levelValueLabel, librasValueLabel, accountLevelBar);
        };

        // Read from the live instance, not a static file re-read like the high score above — Libras
        // is already loaded into GameManager.Instance at boot. CharacterSelectMenu is an overlay
        // *child* of this menu, not a scene swap, so MainMenu's own Visible never toggles while it's
        // open — the refresh has to hook the overlay's visibility instead, so spending Libras in
        // there and hitting Cancel updates the balance shown underneath.
        characterSelect.VisibilityChanged += () =>
        {
            if (!characterSelect.Visible) RefreshTopBox(playerNameLabel, profileIconRect, levelValueLabel, librasValueLabel, accountLevelBar);
        };

        var options = GetNode<OptionsMenu>("OptionsMenu");
        optionsButton.Pressed += options.Open;
        // Options changes GameManager.CurrentOrientation/CurrentLanguage live (see OptionsMenu's
        // landscape/portrait and Español/English toggles) without ever reloading MainMenu, so the
        // row layout and this screen's own interpolated labels would otherwise go stale the moment
        // the player switches either and comes back here. Godot re-translates static Control text on
        // its own when the locale changes; these two are the only labels MainMenu built by
        // interpolating an already-translated template, which is why they need refreshing by hand.
        options.VisibilityChanged += () =>
        {
            if (options.Visible) return;
            ApplyButtonsRowLayout();
            highScoreLabel.Text = string.Format(Tr("Mejor puntaje: {0}"), GameManager.LoadHighScore());
            RefreshTopBox(playerNameLabel, profileIconRect, levelValueLabel, librasValueLabel, accountLevelBar);
        };

        var builds = GetNode<BuildsMenu>("BuildsMenu");
        buildsButton.Pressed += builds.Open;

        // Same refresh-on-close hook as CharacterSelectMenu above — the shop is the other place Libras
        // can be spent, and this is the label that has to notice.
        var cosmeticsShop = GetNode<CosmeticsShopMenu>("CosmeticsShopMenu");
        tiendaButton.Pressed += cosmeticsShop.Open;
        cosmeticsShop.VisibilityChanged += () =>
        {
            if (!cosmeticsShop.Visible) RefreshTopBox(playerNameLabel, profileIconRect, levelValueLabel, librasValueLabel, accountLevelBar);
        };

        // Same refresh-on-close hook — achievement/mission payouts also spend into the same Libras
        // balance shown here.
        var achievementsButton = GetNode<Button>("AchievementsButton");
        var achievementsMenu = GetNode<AchievementsMenu>("AchievementsMenu");
        achievementsButton.Pressed += achievementsMenu.Open;
        Juice.WireButtonFeedback(achievementsButton);
        achievementsMenu.VisibilityChanged += () =>
        {
            if (!achievementsMenu.Visible) RefreshTopBox(playerNameLabel, profileIconRect, levelValueLabel, librasValueLabel, accountLevelBar);
        };

        // Split out of the combined Logros screen into its own button/screen, immediately to the
        // left of it — same refresh-on-close reasoning (missions also pay Libras).
        var missionsButton = GetNode<Button>("MissionsButton");
        var missionsMenu = GetNode<MissionsMenu>("MissionsMenu");
        missionsButton.Pressed += missionsMenu.Open;
        Juice.WireButtonFeedback(missionsButton);
        missionsMenu.VisibilityChanged += () =>
        {
            if (!missionsMenu.Visible) RefreshTopBox(playerNameLabel, profileIconRect, levelValueLabel, librasValueLabel, accountLevelBar);
        };

        // Read-only — no VisibilityChanged refresh hook needed, nothing here spends or earns Libras.
        var statsButton = GetNode<Button>("StatsButton");
        var statsMenu = GetNode<StatsMenu>("StatsMenu");
        statsButton.Pressed += statsMenu.Open;
        Juice.WireButtonFeedback(statsButton);

        // Not opened from a button like every overlay above — GameManager.ShouldShowOnboarding is
        // computed once at boot (see GameManager._Ready), so this either shows itself right away on
        // a genuine first launch, or never shows at all this session.
        var onboarding = GetNode<OnboardingMenu>("OnboardingMenu");
        onboarding.VisibilityChanged += () =>
        {
            if (!onboarding.Visible) RefreshTopBox(playerNameLabel, profileIconRect, levelValueLabel, librasValueLabel, accountLevelBar);
        };
        if (GameManager.Instance.ShouldShowOnboarding) onboarding.Open();

        AnimateTitle();
        PopulateRecords();
        PlayEntranceAnimation(highScoreLabel);
        AnimateAccents();
    }

    // Neon "breathing" glow on the menu's own accents. The WorldEnvironment this scene carries now
    // (see MainMenu.tscn) only blooms a colour whose brightest channel clears glow_hdr_threshold
    // (0.85) -- a plain border colour here tops out at 1.0, so each one pulses from its resting,
    // non-HDR colour up past that threshold and back, rather than sitting at one fixed brightness
    // the way a normal UI colour would. Several buttons share one StyleBoxFlat resource (see
    // MainMenu.tscn's sub_resources), so animating one instance is enough to shimmer all of them
    // together -- that's the point, not an oversight: a row of same-coloured buttons breathing in
    // sync reads as one theme, not as three separate effects that happen to match.
    private const float BorderGlowBoost = 1.8f;

    private static readonly Texture2D LibrasCoinIcon = GD.Load<Texture2D>("res://Assets/Sprites/UI/coin_gem.png");

    private void AnimateAccents()
    {
        ShimmerBorder(GetNode<PanelContainer>("VBoxContainer/RecordsRow/CasualPanel"), 2.4f);
        ShimmerBorder(GetNode<PanelContainer>("VBoxContainer/RecordsRow/HardcorePanel"), 2.1f);

        ShimmerButtonBorder(GetNode<Button>("VBoxContainer/ButtonsRow/StartButton"), 2.3f);   // shared: Start/Builds/Misiones
        ShimmerButtonBorder(GetNode<Button>("VBoxContainer/ButtonsRow/TiendaButton"), 2.0f);
        ShimmerButtonBorder(GetNode<Button>("VBoxContainer/ButtonsRow/OptionsButton"), 2.6f); // shared: Opciones/Estadísticas
        ShimmerButtonBorder(GetNode<Button>("AchievementsButton"), 2.5f); // StatsButton shares OptionsButton's violet style, already shimmering above
    }

    private void ShimmerBorder(PanelContainer panel, float period)
    {
        if (panel.GetThemeStylebox("panel") is not StyleBoxFlat style) return;
        Color from = style.BorderColor;
        var to = new Color(from.R * BorderGlowBoost, from.G * BorderGlowBoost, from.B * BorderGlowBoost, from.A);
        Juice.Shimmer(this, style, "border_color", from, to, period);
    }

    private void ShimmerButtonBorder(Button button, float period)
    {
        if (button.GetThemeStylebox("normal") is not StyleBoxFlat style) return;
        Color from = style.BorderColor;
        var to = new Color(from.R * BorderGlowBoost, from.G * BorderGlowBoost, from.B * BorderGlowBoost, from.A);
        Juice.Shimmer(this, style, "border_color", from, to, period);
    }

    // The title used to be per-letter animated BBCode text ([wave] offsets each glyph on its own
    // phase, which only a RichTextLabel can do); it's a pixelated logo image now, so a whole raster
    // texture has no per-glyph nodes to offset individually. This keeps the same attract-mode
    // *feeling* -- a gentle sway plus a colour breath -- adapted to something a single TextureRect
    // actually has: Position for the sway, Modulate for the breath (a TextureRect has no
    // theme_override_colors/default_color the way a Label does; Modulate tints the whole texture).
    //
    // Both are skipped under reduced motion: a title that never stops moving is exactly what that
    // setting exists to turn off.
    private void AnimateTitle()
    {
        // Title lives inside TitleSlot, a plain (non-Container) Control, rather than directly under
        // VBoxContainer -- otherwise every VBoxContainer re-layout (triggered by something as
        // unrelated as an overlay tab opening/closing, or coming back to the menu after a run) resets
        // Title's container-assigned Position out from under the bob tween below. AsRelative legs
        // sum to zero only if nothing else ever touches Position in between; a direct Container child
        // doesn't get that guarantee, and the title used to end up stuck wherever it happened to be
        // mid-bob when the reset hit. TitleSlot still reserves the row's height in the VBox; only the
        // slot's own Position gets reflowed now, never Title's.
        var titleSlot = GetNode<Control>("VBoxContainer/TitleSlot");
        var title = GetNode<TextureRect>("VBoxContainer/TitleSlot/Title");
        UIUtil.AddSpeedLines(GetNode<Control>("VBoxContainer"), titleSlot.GetIndex());

        if (DangerLevel.Reduced)
        {
            title.Modulate = Colors.White;
            return;
        }

        // AsRelative() so this never has to read the container-assigned Position (still 0,0 this
        // early in the layout pass) -- each leg moves the title by a delta instead of tweening to an
        // absolute Y, and the two legs sum to zero so looping forever never drifts.
        var bobTween = title.CreateTween();
        bobTween.SetLoops();
        bobTween.TweenProperty(title, "position:y", -6f, 1.3f).AsRelative()
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        bobTween.TweenProperty(title, "position:y", 6f, 1.3f).AsRelative()
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        // Pushed past 1.0 so the logo's brightest pixels clear the new WorldEnvironment's
        // glow_hdr_threshold (0.85) and actually bloom, instead of just pulsing between two
        // ordinary, non-glowing tones.
        Juice.Shimmer(title, "modulate", new Color(1.3f, 1.4f, 1.5f), new Color(1.7f, 1.8f, 1.9f), 2.2f);
    }

    // The title screen's own "arrival" — the first thing a player sees, so it fades+scales up as a
    // whole rather than snapping into place, and the high score/records callout (the part most
    // worth a second look) settles in a beat after the rest.
    private void PlayEntranceAnimation(Label highScoreLabel)
    {
        var vbox = GetNode<Control>("VBoxContainer");
        var recordsRow = GetNode<Control>("VBoxContainer/RecordsRow");

        highScoreLabel.Modulate = new Color(1f, 1f, 1f, 0f);
        recordsRow.Modulate = new Color(1f, 1f, 1f, 0f);

        Juice.ModalIn(vbox, 0.35f, 0.92f);

        var timer = GetTree().CreateTimer(0.2f);
        timer.Timeout += () =>
        {
            highScoreLabel.CreateTween().TweenProperty(highScoreLabel, "modulate:a", 1f, 0.25f);
        };

        var timer2 = GetTree().CreateTimer(0.32f);
        timer2.Timeout += () =>
        {
            recordsRow.CreateTween().TweenProperty(recordsRow, "modulate:a", 1f, 0.25f);
        };
    }

    private void RefreshTopBox(Label playerNameLabel, TextureRect profileIconRect, Label levelValueLabel, Label librasValueLabel, ProgressBar accountLevelBar)
    {
        var gm = GameManager.Instance;
        playerNameLabel.Text = gm.PlayerName;
        // Null (DefaultId, or a saved id whose icon got removed from the catalog) hides the slot
        // rather than showing a broken/blank square -- same "graceful fallback" ProfileIconCatalog
        // documents for Texture().
        var iconTexture = ProfileIconCatalog.Texture(gm.EquippedCosmetic(CosmeticCategory.ProfileIcon));
        profileIconRect.Texture = iconTexture;
        profileIconRect.Visible = iconTexture != null;
        levelValueLabel.Text = string.Format(Tr("Nv {0}"), gm.AccountLevel);
        librasValueLabel.Text = gm.Libras.ToString();
        accountLevelBar.MaxValue = gm.AccountXpToNextLevel;
        Juice.BarFill(accountLevelBar, gm.AccountXp);
    }

    // The full top-10 the save file keeps. This panel has the room for all of it, unlike character
    // select's, which is squeezed in beside a portrait.
    private const int MaxRecordsShown = 10;

    // The stylebox both records panels share (StyleBoxFlat_records_panel / _hardcore) has a 14px
    // content margin on each side.
    private const float RecordsPanelContentMargins = 28f;

    private void PopulateRecords()
    {
        var row = GetNode<HBoxContainer>("VBoxContainer/RecordsRow");
        var casualPanel = GetNode<Control>("VBoxContainer/RecordsRow/CasualPanel");
        var hardcorePanel = GetNode<Control>("VBoxContainer/RecordsRow/HardcorePanel");
        var casualList = GetNode<RichTextLabel>("VBoxContainer/RecordsRow/CasualPanel/CasualBox/CasualList");
        var hardcoreList = GetNode<RichTextLabel>("VBoxContainer/RecordsRow/HardcorePanel/HardcoreBox/HardcoreList");

        // Two tables side by side get roughly half the width one used to have on its own, so that
        // width has to be measured against the real screen rather than assumed -- guessing a fixed
        // px value is exactly what made a single panel run off screen once already (see
        // UIUtil.AvailableScrollHeight for the same lesson applied to height instead of width).
        //
        // Capped, though: on a wide/landscape viewport half the screen is far more than a 10-row
        // table needs, and letting the panel grow to fill it just stretches an empty gap to the right
        // of every row instead of anything readable. 260px comfortably fits the widest realistic row
        // (2-digit rank, 7-digit score, "R" + 2 digits, an 8-char date) with the normal 2-space gap,
        // same margin RecordTable already had when this was one 340px table.
        const float MaxPanelWidth = 260f;
        float viewportWidth = GetViewportRect().Size.X;
        float rowSeparation = row.GetThemeConstant("separation");
        const float SideMargin = 24f; // breathing room so neither panel touches the screen edge
        float panelWidth = Mathf.Clamp((viewportWidth - rowSeparation - SideMargin) / 2f, 140f, MaxPanelWidth);

        casualPanel.CustomMinimumSize = new Vector2(panelWidth, 0);
        hardcorePanel.CustomMinimumSize = new Vector2(panelWidth, 0);

        int charBudget = EstimateCharBudget(casualList, panelWidth);

        casualList.Text = RecordTable.Build(GameManager.LoadRecords(GameManager.GameMode.Classic),
            MaxRecordsShown, charBudget, "Todavía no hay récords", animateFirst: true);
        hardcoreList.Text = RecordTable.Build(GameManager.LoadRecords(GameManager.GameMode.Hardcore),
            MaxRecordsShown, charBudget, "Todavía no hay récords", animateFirst: true);
    }

    // How many monospaced characters actually fit in a panel of this width, measured against
    // PixelFont's real advance rather than an assumed em/px ratio -- the two tables can end up
    // narrower than the original single one ever was, so an assumed ratio could be wrong in exactly
    // the direction that overflows the panel. RecordTable only uses this to decide whether the column
    // gap is one space or two, so a slightly conservative estimate costs a little polish, never rows.
    private static int EstimateCharBudget(RichTextLabel label, float panelWidth)
    {
        var font = label.GetThemeFont("normal_font") ?? label.GetThemeDefaultFont();
        int fontSize = label.GetThemeFontSize("normal_font_size");
        float charWidth = font.GetStringSize("0", HorizontalAlignment.Left, -1, fontSize).X;
        return Mathf.Max(6, Mathf.FloorToInt((panelWidth - RecordsPanelContentMargins) / charWidth));
    }
}
