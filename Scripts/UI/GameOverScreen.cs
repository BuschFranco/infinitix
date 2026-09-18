namespace ShooterLoop;

public partial class GameOverScreen : Control
{
    private Button _restartButton;
    private Button _shareButton;
    private Button _menuButton;
    private Button _statsButton;
    private Button _quitButton;
    private Label _scoreLabel;
    private PanelContainer _panel;
    private ScrollContainer _scroll;
    private VBoxContainer _summaryContainer;
    private VBoxContainer _achievementsContainer;
    private GameOverStatsMenu _statsMenu;

    private static readonly Texture2D LibrasCoinIcon = GD.Load<Texture2D>("res://Assets/Sprites/UI/coin_gem.png");

    // The bare default theme panel before this pass -- the only run-time modal with zero accent
    // colour at all. Red/pink to match the "FIN DEL JUEGO" title right inside it, rather than
    // reusing another screen's cyan/magenta/gold: this is the one modal that isn't a reward, a
    // purchase, or a pause, and shouldn't read as any of those at a glance.
    private static readonly StyleBoxFlat PanelStyle = UIUtil.CreatePanelStyle(new Color(1f, 0.35f, 0.45f));

    public override void _Ready()
    {
        AddToGroup("game_over_screen");
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        _panel = GetNode<PanelContainer>("Panel");
        _panel.AddThemeStyleboxOverride("panel", PanelStyle);
        _scroll = GetNode<ScrollContainer>("Panel/Scroll");
        var title = GetNode<Label>("Panel/Scroll/VBoxContainer/Title");
        UIUtil.AddSpeedLines(title.GetParent<Control>(), title.GetIndex());
        _scoreLabel = GetNode<Label>("Panel/Scroll/VBoxContainer/ScoreLabel");
        _summaryContainer = GetNode<VBoxContainer>("Panel/Scroll/VBoxContainer/SummaryContainer");
        _achievementsContainer = GetNode<VBoxContainer>("Panel/Scroll/VBoxContainer/AchievementsContainer");
        _restartButton = GetNode<Button>("Panel/Scroll/VBoxContainer/RestartButton");
        _shareButton = GetNode<Button>("Panel/Scroll/VBoxContainer/ShareButton");
        _menuButton = GetNode<Button>("Panel/Scroll/VBoxContainer/MenuButton");
        _statsButton = GetNode<Button>("Panel/Scroll/VBoxContainer/StatsButton");
        _quitButton = GetNode<Button>("Panel/Scroll/VBoxContainer/QuitButton");
        _statsMenu = GetNode<GameOverStatsMenu>("GameOverStatsMenu");

        _restartButton.Pressed += OnRestartPressed;
        _shareButton.Pressed += OnSharePressed;
        _menuButton.Pressed += OnMenuPressed;
        _statsButton.Pressed += _statsMenu.Open;
        _quitButton.Pressed += OnQuitPressed;

        Juice.WireButtonFeedback(_restartButton);
        Juice.WireButtonFeedback(_shareButton);
        Juice.WireButtonFeedback(_menuButton);
        Juice.WireButtonFeedback(_statsButton);
        Juice.WireButtonFeedback(_quitButton);
    }

    // This is the win/loss moment of a whole run, so it gets the heaviest treatment in the pass:
    // the panel fades+scales in, then once that settles the score — the one number the player
    // actually came here to see — gets a bigger punch than the standard ValuePop, and the run
    // recap reveals one line at a time instead of landing as a single block of text.
    public void Open()
    {
        var gm = GameManager.Instance;
        var player = GetTree().GetFirstNodeInGroup("player") as Player;

        _scoreLabel.Text = string.Format(Tr("Puntaje Final: {0}"), gm.Score);

        var lines = new List<string>
        {
            string.Format(Tr("Ronda {0}   Nv {1}   {2}: {3}"), gm.RoundNumber, gm.Level, Glossary.Kills, gm.EnemiesKilled),
            string.Format(Tr("Monedas: {0}"), gm.Coins),
            string.Format(Tr("+{0} Dinero (total: {1})"), gm.LastRunLibrasEarned, gm.Libras),
        };

        // Always shown, even when this run earned 0 Libras — a silent 0 used to read as a bug ("did I
        // not get anything?"). With GameManager.LibrasFreeRounds at 0 every run earns something, but
        // the 0-case stays here (rather than assuming it can't happen) so a future grace period doesn't
        // silently reintroduce the same confusing blank line.
        lines.Add(gm.LastRunLibrasEarned > 0
            ? string.Format(Tr("XP cuenta: +{0}   ({1}/{2} para Nv {3})"), gm.LastRunLibrasEarned, gm.AccountXp, gm.AccountXpToNextLevel, gm.AccountLevel + 1)
            : string.Format(Tr("XP cuenta: +0   (superá la ronda {0} para empezar a ganar)"), GameManager.LibrasFreeRounds + 1));

        // The pilot actually flown this run, separate from the account-wide line above — shown with
        // its own name so a level-up reads as "this pilot got stronger," not a duplicate of the line
        // right above it.
        if (gm.LastRunCharacterLevelsGained > 0)
        {
            string pilotName = CharacterCatalog.Get(gm.SelectedCharacter).Name;
            lines.Add(string.Format(Tr("¡{0} subió a Nivel {1}!"), pilotName, gm.GetCharacterLevel(gm.SelectedCharacter)));
        }

        if (gm.LastRunAccountLevelsGained > 0)
            lines.Add(string.Format(Tr("¡Nivel de cuenta {0}!"), gm.AccountLevel));

        if (player != null)
        {
            var builds = new List<string>();
            foreach (var cls in BuildCatalog.ClassOrder)
                if (player.IsClassActive(cls)) builds.Add(BuildCatalog.Name(cls));
            if (builds.Count > 0)
                lines.Add(string.Format(Tr("Build: {0}"), string.Join(" + ", builds)));
        }

        foreach (Node child in _summaryContainer.GetChildren())
            child.QueueFree();
        foreach (Node child in _achievementsContainer.GetChildren())
            child.QueueFree();

        var summaryLabels = new List<Label>();
        foreach (string line in lines)
        {
            var label = new Label();
            label.Text = line;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
            label.AddThemeColorOverride("font_color", new Color(0.65f, 0.72f, 0.82f));
            label.Modulate = new Color(1f, 1f, 1f, 0f);
            _summaryContainer.AddChild(label);
            summaryLabels.Add(label);
        }

        // Evaluated inside RegisterFinalScore, which already ran before Open() is called (see
        // NotifyPlayerDied/AbandonRun) — this just reveals whatever it found. Shown as gold-bordered
        // cards (badge + name + reward) rather than another plain text line -- the same "this is an
        // achievement" visual identity AchievementsMenu and the live in-run toast already use, so a
        // logro reads as one consistent thing across all three places it can appear, not three
        // different looks for the same concept. Built AFTER the summary lines above are added but
        // BEFORE Visible = true, so the shared clear-and-rebuild-on-every-Open() pattern still holds.
        var achievementCards = new List<PanelContainer>();
        if (gm.LastRunNewAchievements.Count > 0)
        {
            var sectionLabel = new Label { Text = Tr("Logros desbloqueados"), HorizontalAlignment = HorizontalAlignment.Center };
            sectionLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Subtitle);
            sectionLabel.AddThemeColorOverride("font_color", Palette.UltimatePanelBorder);
            sectionLabel.Modulate = new Color(1f, 1f, 1f, 0f);
            _achievementsContainer.AddChild(sectionLabel);
            summaryLabels.Add(sectionLabel);

            foreach (var achievement in gm.LastRunNewAchievements)
                achievementCards.Add(BuildAchievementCard(achievement));
        }

        Visible = true;
        Juice.ModalIn(_panel);

        // A small rattle once the modal has settled into place -- nothing else on this screen says
        // "you just died" specifically; every other beat here (the fade-in, the score pop, the line
        // reveal) is the same choreography any modal could use.
        var shakeTimer = GetTree().CreateTimer(0.22f);
        shakeTimer.Timeout += () => Juice.Shake(_panel, strength: 5f, duration: 0.3f);

        var scorePop = GetTree().CreateTimer(0.18f);
        scorePop.Timeout += () => Juice.ValuePop(_scoreLabel, 1.6f, 0.35f);

        for (int i = 0; i < summaryLabels.Count; i++)
        {
            var label = summaryLabels[i];
            var timer = GetTree().CreateTimer(0.2f + i * 0.08f);
            timer.Timeout += () =>
            {
                if (IsInstanceValid(label))
                    label.CreateTween().TweenProperty(label, "modulate:a", 1f, 0.2f);
            };
        }

        // Cards pop in one beat at a time, right after the last text line -- Back-ease scale, not the
        // text lines' plain fade, so a logro visibly "arrives" instead of just materializing. One
        // fanfare for the whole section (not per card) so a run that unlocked several at once doesn't
        // spam the same sound.
        if (achievementCards.Count > 0)
        {
            float cardsStart = 0.2f + summaryLabels.Count * 0.08f;
            var fanfare = GetTree().CreateTimer(cardsStart);
            fanfare.Timeout += () => AudioManager.Instance?.Play(AudioManager.Sfx.LevelUp);

            for (int i = 0; i < achievementCards.Count; i++)
            {
                var card = achievementCards[i];
                var timer = GetTree().CreateTimer(cardsStart + i * 0.12f);
                timer.Timeout += () =>
                {
                    if (!IsInstanceValid(card)) return;
                    // Only valid once the card has actually been laid out, which by the time this
                    // timer fires (0.2s+ after Open()) it always has been.
                    card.PivotOffset = card.Size / 2f;
                    var tween = card.CreateTween();
                    if (!DangerLevel.Reduced)
                    {
                        tween.SetParallel(true);
                        tween.TweenProperty(card, "scale", Vector2.One, 0.25f)
                            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
                        tween.TweenProperty(card, "modulate:a", 1f, 0.2f);
                        tween.SetParallel(false);
                    }
                    else
                    {
                        tween.TweenProperty(card, "modulate:a", 1f, 0.2f);
                    }
                };
            }

            // FitScrollToViewport needs the achievements' final height, but that's only correct once
            // the cards above have had a frame to lay out (a freshly-added Control's minimum size
            // isn't valid until then) -- deferring one frame is the standard Godot way to wait for that.
            CallDeferred(nameof(RefitScroll));
        }
        else
        {
            RefitScroll();
        }
    }

    // Same gold-badge-card identity as AchievementsMenu's rows and NotificationToastController's
    // live in-run toast (see AchievementCatalog.TierBadges) -- added here as a third consumer of the
    // same shared dictionary rather than a fourth one-off style. Starts hidden/shrunk; the caller's
    // staggered timer animates it in (see Open() above) once it's this card's turn.
    private PanelContainer BuildAchievementCard(AchievementDef achievement)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", UIUtil.CreatePanelStyle(Palette.UltimatePanelBorder));
        card.Modulate = new Color(1f, 1f, 1f, 0f);
        if (!DangerLevel.Reduced) card.Scale = new Vector2(0.85f, 0.85f);
        _achievementsContainer.AddChild(card);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        card.AddChild(row);

        row.AddChild(new TextureRect
        {
            Texture = AchievementCatalog.TierBadges[achievement.Tier],
            CustomMinimumSize = new Vector2(32f, 32f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });

        var nameLabel = new Label
        {
            Text = achievement.Name,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        nameLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Body);
        nameLabel.AddThemeColorOverride("font_color", Colors.White);
        row.AddChild(nameLabel);

        row.AddChild(new TextureRect
        {
            Texture = LibrasCoinIcon,
            CustomMinimumSize = new Vector2(16f, 16f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });
        var rewardLabel = new Label { Text = $"+{achievement.RewardLibras}" };
        rewardLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
        rewardLabel.AddThemeColorOverride("font_color", Palette.UltimatePanelBorder);
        row.AddChild(rewardLabel);

        return card;
    }

    private void RefitScroll() => UIUtil.FitScrollToViewport(_scroll, _panel);

    private void OnRestartPressed()
    {
        GameManager.Instance.ResetRun();
        GetTree().ReloadCurrentScene();
    }

    // wa.me opens WhatsApp itself if it's installed, or falls back to the browser's own "open in
    // WhatsApp Web" prompt otherwise — no native share-sheet plugin needed on either Android or
    // desktop. The website link is the one already live (see web/astro.config.mjs) — not a Play
    // Store link, since that listing isn't published yet (see store-assets/play-console-launch-
    // checklist.md's closed-testing step).
    private void OnSharePressed()
    {
        var gm = GameManager.Instance;
        string text = string.Format(Tr("¡Llegué a la ronda {0} con {1} puntos en Infinitix! 🚀 {2}"),
            gm.RoundNumber, gm.Score, "https://buschfranco.github.io/infinitix/");
        OS.ShellOpen($"https://wa.me/?text={Uri.EscapeDataString(text)}");
    }

    private void OnMenuPressed()
    {
        GameManager.Instance.ResetRun();
        GetTree().ChangeSceneToFile("res://Scenes/UI/MainMenu.tscn");
    }

    // Confirmed because it's the one button here that ends the process rather than the run, and it
    // sits in a stack of three similarly-styled buttons where a mistap costs the player the screen
    // they're still reading.
    private void OnQuitPressed()
    {
        var dialog = GetTree().GetFirstNodeInGroup("confirm_dialog") as ConfirmDialog;
        if (dialog == null)
        {
            GetTree().Quit();
            return;
        }

        dialog.Ask(
            "¿Salir del juego?",
            "Tu puntaje ya quedó guardado. Se va a cerrar la aplicación.",
            "Salir",
            () => GetTree().Quit());
    }
}
