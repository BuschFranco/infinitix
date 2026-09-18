namespace ShooterLoop;

public partial class HUD : Control
{
    private HeartIcon[] _heartIcons;
    private ShieldIcon[] _shieldIcons;
    private Control _shieldRow;
    private Label _statsLabel;

    // The active builds get their own bordered chip rather than a third line appended to
    // _statsLabel. Sharing that label meant they inherited its 11px grey — deliberately muted, since
    // raw stat numbers are reference material you go looking for — and a build is the opposite: a
    // thing you earned, that changes how the run plays, and that you need to *notice* the moment it
    // unlocks. Hence the separate node, the magenta border, and PlayBuildFlare below.
    private PanelContainer _buildPanel;
    private Label _buildLabel;
    private Tween _buildFlareTween;

    // Builds never deactivate (their thresholds are stat floors and stats only go up), so this only
    // ever grows — comparing the count is enough to spot a new unlock, no set diffing needed.
    private int _shownBuildCount;

    private ProgressBar _xpBar;
    private Label _levelLabel;
    private Label _roundLabel;
    private Label _roundTimerLabel;
    private Label _coinsLabel;
    private Label _scoreLabel;
    private Timer _scoreDeltaTimer;
    private int _lastScore = 0;
    private Button _ultimateButton;
    private UltimateButtonIcon _ultimateButtonIcon;
    private Label _countdownLabel;
    private Player _player;
    private PanelContainer _topBarPanel;
    private int _lastPulsedSecond = -1;
    private const int TimerPulseThreshold = 5;
    private Control _cooldownIcons;
    private CooldownIcon _laserIcon;
    private CooldownIcon _missileIcon;
    private CooldownIcon _shieldRegenIcon;
    private CooldownIcon _ultimateIcon;
    private CooldownIcon _ondaIcon;
    private CooldownIcon _vendavalIcon;
    private CooldownIcon _mineIcon;
    // The streak used to be a single long right-aligned Label ("RACHA 9 · ×1.9 XP y monedas") floating
    // roughly at screen centre — long enough to reach into the play area, and text-only for something
    // that's meant to be read at a glance mid-fight. It's now a compact badge pinned under the round
    // timer, in the same left column as everything else: the multiplier is the hero number, a bar
    // shows how close the streak is to its cap, and the whole thing heats up in colour as it climbs.
    private PanelContainer _streakPanel;
    private Label _streakMultLabel;
    private Label _streakCountLabel;
    private ProgressBar _streakBar;
    private StyleBoxFlat _streakPanelStyle;
    private StyleBoxFlat _streakBarFillStyle;
    private int _lastStreak;
    private int _lastStreakMilestone;
    private Tween _streakPulseTween;
    // Fraction of Player.KillStreakMax past which the badge starts glowing continuously, rather than
    // only punching on each milestone — the last stretch before the cap is rare enough (only reachable
    // deep into a run) that it earns a persistent "this is getting big" tell, not just another pop.
    private const float StreakGlowThreshold = 0.75f;
    private const float StreakGlowBoost = 1.7f;
    private Control _bossHealthBar;
    private ProgressBar _bossHpBar;
    private Label _bossNameLabel;
    private Enemy _currentBoss;
    private bool _bossBarWasVisible;

    // Tracked so a heart/shield loss vs. gain can flash/pop the one icon that actually changed,
    // instead of the row just snapping to the new totals with no feedback at all. -1 means "no
    // baseline yet" (first call, right after subscribing) so that call never treats itself as a
    // change.
    private int _lastLivesCurrent = -1;
    private int _lastShieldCurrent = -1;
    private int _lastCoins = -1;

    // First-visible tracking per cooldown icon, so a weapon unlocking mid-round gets a pop the
    // instant its icon appears instead of just popping into existence.
    private readonly Dictionary<CooldownIcon, bool> _cooldownWasVisible = new();

    public override void _Ready()
    {
        // Same reasoning Shop/UpgradePicker/PauseMenu already document: the tree now stays paused
        // through the whole "get ready" countdown between rounds (see GameManager.StartNextRound/
        // BeginRoundAfterCountdown), and without this the "Preparate... 3, 2, 1" label would freeze
        // on whatever it showed the instant the pause began instead of counting down live.
        ProcessMode = ProcessModeEnum.Always;

        // The arena's music starts here rather than from a script on Arena.tscn's root, which has
        // none. The HUD is the one node guaranteed to exist in every arena, and hanging it here also
        // covers the Game Over screen's ReloadCurrentScene() retry path for free.
        AudioManager.Instance?.PlayMusic(AudioManager.MusicTrack.Arena);

        _roundLabel = GetNode<Label>("TopBarPanel/TopBar/RoundLabel");
        _roundTimerLabel = GetNode<Label>("RoundTimerLabel");
        _levelLabel = GetNode<Label>("TopBarPanel/TopBar/LevelLabel");
        // One icon per point of the real caps (MaxLivesCap 4, MaxShieldChargesCap 4), so neither row ever
        // needs a text fallback. The old code fell back to "Vidas: 4/4" the moment a single Corazón was
        // bought, because it only had three heart nodes.
        _heartIcons = new[]
        {
            GetNode<HeartIcon>("TopBarPanel/TopBar/StatusRow/Meters/LivesRow/Heart1"),
            GetNode<HeartIcon>("TopBarPanel/TopBar/StatusRow/Meters/LivesRow/Heart2"),
            GetNode<HeartIcon>("TopBarPanel/TopBar/StatusRow/Meters/LivesRow/Heart3"),
            GetNode<HeartIcon>("TopBarPanel/TopBar/StatusRow/Meters/LivesRow/Heart4"),
        };
        _shieldRow = GetNode<Control>("TopBarPanel/TopBar/StatusRow/Meters/ShieldRow");
        _shieldIcons = new[]
        {
            GetNode<ShieldIcon>("TopBarPanel/TopBar/StatusRow/Meters/ShieldRow/Shield1"),
            GetNode<ShieldIcon>("TopBarPanel/TopBar/StatusRow/Meters/ShieldRow/Shield2"),
            GetNode<ShieldIcon>("TopBarPanel/TopBar/StatusRow/Meters/ShieldRow/Shield3"),
            GetNode<ShieldIcon>("TopBarPanel/TopBar/StatusRow/Meters/ShieldRow/Shield4"),
        };
        // Who you picked, shown left of the lives/shields column and vertically centred against it.
        // Read once here rather than polled: the selection is fixed for the whole run (it's chosen
        // before the arena scene even loads). Same texture and tint the ship itself uses, so a white
        // silhouette picks up its Color while a photo portrait (Color = White) shows through untouched.
        var character = CharacterCatalog.Get(GameManager.Instance.SelectedCharacter);
        var portrait = GetNode<TextureRect>("TopBarPanel/TopBar/StatusRow/PortraitFrame/Portrait");
        portrait.Texture = CharacterCatalog.Texture(character);
        portrait.Modulate = character.Color;

        // Own StyleBoxFlat copy per instance (not the shared .tscn sub_resource) — same convention
        // CharacterSelectMenu's Swatch just adopted, so the Marco cosmetic here can't affect anything
        // beyond this one frame.
        var portraitFrame = GetNode<Panel>("TopBarPanel/TopBar/StatusRow/PortraitFrame");
        var portraitFrameStyle = new StyleBoxFlat { BgColor = new Color(0.043f, 0.024f, 0.078f, 0.85f) };
        portraitFrameStyle.SetBorderWidthAll(2);
        portraitFrame.AddThemeStyleboxOverride("panel", portraitFrameStyle);
        Juice.ApplyCosmeticToStyleBox(portraitFrameStyle, CosmeticCategory.Marco, CosmeticCatalog.BaseColor(CosmeticCategory.Marco));

        _statsLabel = GetNode<Label>("TopBarPanel/TopBar/StatsLabel");
        _buildPanel = GetNode<PanelContainer>("TopBarPanel/TopBar/BuildPanel");
        _buildLabel = GetNode<Label>("TopBarPanel/TopBar/BuildPanel/BuildLabel");
        _xpBar = GetNode<ProgressBar>("TopBarPanel/TopBar/XpBar");
        _coinsLabel = GetNode<Label>("TopBarPanel/TopBar/PointsLabel");
        _scoreLabel = GetNode<Label>("TopBarPanel/TopBar/ScoreLabel");
        _countdownLabel = GetNode<Label>("CountdownLabel");
        _topBarPanel = GetNode<PanelContainer>("TopBarPanel");
        // Duplicated rather than mutated in place — the original is a .tscn sub_resource, and cloning
        // it before overriding keeps this instance's colour from leaking into anything else that
        // might reference the same resource.
        var topBarStyle = (StyleBoxFlat)_topBarPanel.GetThemeStylebox("panel").Duplicate();
        _topBarPanel.AddThemeStyleboxOverride("panel", topBarStyle);
        Juice.ApplyCosmeticToStyleBox(topBarStyle, CosmeticCategory.Hud, Palette.HudPanelBorder);
        _cooldownIcons = GetNode<Control>("CooldownIcons");
        _laserIcon = GetNode<CooldownIcon>("CooldownIcons/LaserIcon");
        _missileIcon = GetNode<CooldownIcon>("CooldownIcons/MissileIcon");
        _shieldRegenIcon = GetNode<CooldownIcon>("CooldownIcons/ShieldRegenIcon");
        _ultimateIcon = GetNode<CooldownIcon>("CooldownIcons/UltimateIcon");
        _ondaIcon = GetNode<CooldownIcon>("CooldownIcons/OndaIcon");
        _vendavalIcon = GetNode<CooldownIcon>("CooldownIcons/VendavalIcon");
        _mineIcon = GetNode<CooldownIcon>("CooldownIcons/MineIcon");

        // Which ability each icon draws is assigned here rather than exported from HUD.tscn. The
        // scene used to carry an icon_color and a one-letter glyph per icon and they never actually
        // reached the script — all seven rendered as the compiled-in default, a white disc with a
        // "?". Set from code it simply can't silently not apply. See CooldownIcon's header.
        _laserIcon.Kind = CooldownIcon.Ability.Laser;
        _missileIcon.Kind = CooldownIcon.Ability.Missile;
        _shieldRegenIcon.Kind = CooldownIcon.Ability.ShieldRegen;
        _ultimateIcon.Kind = CooldownIcon.Ability.Ultimate;
        _ondaIcon.Kind = CooldownIcon.Ability.Onda;
        _vendavalIcon.Kind = CooldownIcon.Ability.Vendaval;
        _mineIcon.Kind = CooldownIcon.Ability.Mine;

        _bossHealthBar = GetNode<Control>("BossHealthBar");
        _bossHpBar = GetNode<ProgressBar>("BossHealthBar/BossHpBar");
        _bossNameLabel = GetNode<Label>("BossHealthBar/BossNameLabel");

        BuildStreakBadge();

        // Build classes live in their own chip inside the stats panel (see UpdateStatsLabel), not as
        // a floating label below the top bar.

        _scoreDeltaTimer = new Timer();
        _scoreDeltaTimer.OneShot = true;
        _scoreDeltaTimer.WaitTime = 1.2;
        AddChild(_scoreDeltaTimer);
        _scoreDeltaTimer.Timeout += () => _scoreLabel.Text = string.Format(Tr("Puntaje: {0}"), GameManager.Instance.Score);

        GameManager.Instance.XpChanged += OnXpChanged;
        GameManager.Instance.LevelUp += OnLevelUp;
        GameManager.Instance.LevelsGained += OnLevelsGained;
        GameManager.Instance.LevelUpCelebration += SpawnLevelUpFireworks;
        GameManager.Instance.RoundChanged += OnRoundChanged;
        GameManager.Instance.CoinsChanged += OnCoinsChanged;
        GameManager.Instance.ScoreChanged += OnScoreChanged;

        OnXpChanged(GameManager.Instance.Xp, GameManager.Instance.XpToNextLevel);
        OnLevelUp(GameManager.Instance.Level);
        OnRoundChanged(GameManager.Instance.RoundNumber);
        OnCoinsChanged(GameManager.Instance.Coins);
        OnScoreChanged(GameManager.Instance.Score);

        _player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (_player != null)
        {
            _player.LivesChanged += OnLivesChanged;
            OnLivesChanged(_player.CurrentLives, _player.MaxLives);

            _player.ShieldChanged += OnShieldChanged;
            OnShieldChanged(_player.CurrentShieldCharges, _player.MaxShieldCharges);
        }

        _ultimateButton = GetNode<Button>("UltimateButton");
        _ultimateButtonIcon = GetNode<UltimateButtonIcon>("UltimateButton/UltimateButtonIcon");
        _ultimateButton.Pressed += OnUltimatePressed;
        UpdateUltimateUi();

        // GameManager's ApplyUltimateButtonOpacity targets this group (same pattern as the
        // joystick) so the setting can be applied from the main menu's options screen too.
        _ultimateButton.AddToGroup("ultimate_button");

        // Read here rather than pushed from the options screen — like the joystick, this scene
        // only exists during a run and the setting can't change mid-run. Modulate propagates to
        // the icon child; it doesn't affect hit-testing, so 0% keeps the button functional.
        _ultimateButton.Modulate = new Color(1f, 1f, 1f, GameManager.Instance?.UltimateButtonOpacity ?? 1f);

        var pauseButton = GetNode<Button>("PauseButton");
        pauseButton.Pressed += OnPausePressed;
        Juice.WireButtonFeedback(pauseButton);
        Juice.WireButtonFeedback(_ultimateButton);

        // The .tscn's own offsets give TopBarPanel a fixed 200x200 box purely as a safe non-zero
        // fallback. ResetSize() (called here and every frame below) shrinks it to exactly fit its
        // labels instead, so the panel hugs whatever text is actually showing rather than leaving
        // dead space or, worse, clipping it.
        _topBarPanel.ResetSize();
    }

    public override void _ExitTree()
    {
        // GameManager is a persistent autoload; HUD is scene-scoped and gets freed on
        // ReloadCurrentScene(). Without unsubscribing, these delegates would keep pointing at
        // this freed instance and blow up (aborting whatever triggered the event) the next
        // time GameManager raises them after a restart.
        if (GameManager.Instance == null) return;
        GameManager.Instance.XpChanged -= OnXpChanged;
        GameManager.Instance.LevelUp -= OnLevelUp;
        GameManager.Instance.LevelsGained -= OnLevelsGained;
        GameManager.Instance.LevelUpCelebration -= SpawnLevelUpFireworks;
        GameManager.Instance.RoundChanged -= OnRoundChanged;
        GameManager.Instance.CoinsChanged -= OnCoinsChanged;
        GameManager.Instance.ScoreChanged -= OnScoreChanged;
    }

    private void OnPausePressed()
    {
        GameManager.Instance.OpenPauseMenu();
    }

    private void OnUltimatePressed()
    {
        _player?.TriggerUltimate();
        UpdateUltimateUi();
    }

    // Polled every frame from _Process rather than pushed off an event — the cooldown just
    // decays continuously (see Player._PhysicsProcess), there's no discrete "it changed" moment
    // to hang an event off of the way the old Score-driven charge meter had.
    private void UpdateUltimateUi()
    {
        bool hasUltimate = _player != null && _player.EquippedUltimate != null;
        _ultimateButton.Visible = hasUltimate;
        if (!hasUltimate) return;

        _ultimateButtonIcon.Kind = _player.EquippedUltimate.Value;

        float remaining = _player.UltimateCooldownRemaining;
        float duration = _player.UltimateCooldownDuration;
        bool ready = remaining <= 0f;

        _ultimateButtonIcon.CooldownFraction = duration > 0f ? Mathf.Clamp(remaining / duration, 0f, 1f) : 0f;
        _ultimateButtonIcon.CooldownSeconds = ready ? 0f : remaining;
        _ultimateButton.Disabled = !ready;
    }

    public override void _Process(double delta)
    {
        // Re-fit every frame: round number, timer, and coin/score text all change length at
        // runtime (e.g. "Ronda 9" -> "Ronda 10"), and a stale size would either clip the new text
        // or leave the panel oversized for shorter text. Cheap for a handful of labels.
        _topBarPanel.ResetSize();

        // Pinned to the stats panel's right edge rather than a fixed offset, since the panel's
        // own width breathes with its text (see above) — a fixed offset would drift away from or
        // overlap the panel as "Ronda 9" becomes "Ronda 10".
        _cooldownIcons.Position = _topBarPanel.Position + new Vector2(_topBarPanel.Size.X + 12f, 0f);

        // Same reasoning, but pinned below the panel's bottom edge instead of its right. The height
        // moves less than it used to: the shield row now lives inside StatusRow, which is sized by
        // the 80px portrait, so unlocking a Barrier no longer makes the whole panel taller. What
        // still moves it is the build chip appearing and wrapping to a second line.
        _roundTimerLabel.Position = _topBarPanel.Position + new Vector2(0f, _topBarPanel.Size.Y + 10f);

        // Stacked directly under the round timer and flush with the panel's left edge, so the whole
        // HUD stays one left-hand column. It used to be offset +150px horizontally, which put it near
        // the middle of the screen — over the arena, competing with the thing the player is aiming at.
        // Chained off the timer's live size rather than a fixed offset, same reasoning as the timer's
        // own positioning above.
        _streakPanel.Position = _roundTimerLabel.Position + new Vector2(0f, _roundTimerLabel.Size.Y + 6f);
        _streakPanel.ResetSize();

        UpdateCooldownIcons();
        UpdateUltimateUi();
        UpdateBossHealthBar();
        UpdateStatsLabel();
        UpdateKillStreak();

        // The pre-round countdown gets its own big centered label rather than the small top-bar
        // one — at 72px in the middle of the screen it's actually noticeable, which the corner
        // timer wasn't.
        if (GameManager.Instance.IsRoundStarting)
        {
            _countdownLabel.Visible = true;
            string countdownText = Mathf.CeilToInt(GameManager.Instance.RoundStartTimeRemaining).ToString();
            if (_countdownLabel.Text != countdownText)
            {
                _countdownLabel.Text = countdownText;
                Juice.ValuePop(_countdownLabel, 1.3f, 0.3f);

                // This `if` is already a once-per-second change detector — it's what drives the pop.
                // Reusing it is what keeps the beep at three beeps rather than one per frame.
                AudioManager.Instance?.Play(AudioManager.Sfx.Countdown);
            }
            _roundTimerLabel.Text = "Preparate...";
            ResetRoundTimerLook();
            return;
        }

        _countdownLabel.Visible = false;

        if (GameManager.Instance.IsBossRound)
        {
            _roundTimerLabel.Text = "¡JEFE!";
            ResetRoundTimerLook();
            return;
        }

        int secondsLeft = Mathf.CeilToInt(GameManager.Instance.RoundTimeRemaining);
        _roundTimerLabel.Text = $"{secondsLeft / 60}:{secondsLeft % 60:D2}";

        // The countdown used to be buried at font_size 15 inside the corner stat list — easy to
        // miss even though it's arguably the most time-pressured number on screen. Pulsing on
        // every one of the last 5 seconds (rather than just turning red once) is what actually
        // catches the eye at a glance without having to read the number.
        bool urgent = secondsLeft > 0 && secondsLeft <= TimerPulseThreshold;
        _roundTimerLabel.AddThemeColorOverride("font_color", urgent ? Palette.Warning : new Color(0.85f, 0.9f, 0.95f));

        if (urgent && secondsLeft != _lastPulsedSecond)
        {
            _lastPulsedSecond = secondsLeft;
            PulseRoundTimer();
        }
        else if (!urgent)
        {
            _lastPulsedSecond = -1;
        }
    }

    // Runs unconditionally every frame (called before the round-starting/boss-round early returns
    // above) — it used to sit after them and would freeze mid-value for the whole "Preparate..."
    // countdown, and for an entire boss round, since both paths return before ever reaching it.
    private void UpdateKillStreak()
    {
        if (_player == null || _player.KillStreak < 3)
        {
            _streakPanel.Visible = false;
            _lastStreak = 0;
            _lastStreakMilestone = 0;
            _streakPulseTween?.Kill();
            _streakPulseTween = null;
            return;
        }

        int streak = _player.KillStreak;

        _streakMultLabel.Text = $"×{_player.KillStreakMultiplier:0.0}";
        _streakCountLabel.Text = string.Format(Tr("RACHA {0}"), streak);
        Juice.BarFill(_streakBar, Mathf.Min(streak, Player.KillStreakMax));
        _streakPanel.Visible = true;

        // Heats up toward the cap rather than toward an arbitrary 40 (the old divisor), so the colour
        // reaches full intensity exactly when the multiplier stops growing.
        float t = Mathf.Min(streak / (float)Player.KillStreakMax, 1f);
        Color heat = Palette.CoinPickup.Lerp(Palette.Warning, t);
        _streakMultLabel.AddThemeColorOverride("font_color", heat);
        _streakBarFillStyle.BgColor = heat;

        bool glowing = t >= StreakGlowThreshold;
        if (glowing && _streakPulseTween == null)
        {
            // Only (re)started when crossing into the final stretch — the shimmer owns border_color
            // continuously from here on, so the direct assignment below is skipped while it runs.
            var boosted = new Color(heat.R * StreakGlowBoost, heat.G * StreakGlowBoost, heat.B * StreakGlowBoost, 0.9f);
            _streakPulseTween = Juice.Shimmer(this, _streakPanelStyle, "border_color", new Color(heat, 0.9f), boosted, 0.9f);
        }
        else if (!glowing)
        {
            _streakPulseTween?.Kill();
            _streakPulseTween = null;
            _streakPanelStyle.BorderColor = new Color(heat, 0.9f);
        }

        // Every 10th kill is a milestone: a bigger, escalating punch than the routine per-kill pop, so
        // the streak keeps feeling like it's building toward something all the way to the (now much
        // farther away) cap instead of the celebration flattening out early.
        int milestone = streak / 10;
        if (milestone > _lastStreakMilestone)
        {
            Juice.Shake(_streakPanel, 6f + milestone * 2f, 0.35f, Colors.White);
            Juice.ValuePop(_streakPanel, 1.3f + milestone * 0.03f, 0.3f);
            _lastStreakMilestone = milestone;
        }
        else if (streak > _lastStreak)
        {
            Juice.ValuePop(_streakPanel, 1.18f, 0.25f);
        }

        _lastStreak = streak;
    }

    // Built in code rather than authored in HUD.tscn to match how the streak already worked (it was a
    // code-created Label) and so the two styleboxes below can be recoloured live as the streak heats up.
    private void BuildStreakBadge()
    {
        _streakPanelStyle = new StyleBoxFlat();
        _streakPanelStyle.BgColor = new Color(0.043f, 0.024f, 0.078f, 0.82f);
        _streakPanelStyle.SetBorderWidthAll(2);
        _streakPanelStyle.SetCornerRadiusAll(0);
        _streakPanelStyle.SetContentMarginAll(6f);
        _streakPanelStyle.ContentMarginLeft = 10f;
        _streakPanelStyle.ContentMarginRight = 10f;

        _streakPanel = new PanelContainer();
        _streakPanel.AddThemeStyleboxOverride("panel", _streakPanelStyle);
        _streakPanel.MouseFilter = MouseFilterEnum.Ignore;
        _streakPanel.Visible = false;
        AddChild(_streakPanel);

        var box = new VBoxContainer();
        box.MouseFilter = MouseFilterEnum.Ignore;
        box.AddThemeConstantOverride("separation", 2);
        _streakPanel.AddChild(box);

        var row = new HBoxContainer();
        row.MouseFilter = MouseFilterEnum.Ignore;
        row.AddThemeConstantOverride("separation", 8);
        box.AddChild(row);

        // The multiplier is what the streak actually *does*, so it's the big number; the raw count is
        // supporting detail. The old label led with "RACHA 9" and buried the ×1.9 behind a separator.
        _streakMultLabel = new Label();
        _streakMultLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Subtitle);
        _streakMultLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _streakMultLabel.AddThemeConstantOverride("outline_size", 3);
        _streakMultLabel.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_streakMultLabel);

        _streakCountLabel = new Label();
        _streakCountLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
        _streakCountLabel.AddThemeColorOverride("font_color", new Color(0.78f, 0.83f, 0.9f));
        _streakCountLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _streakCountLabel.AddThemeConstantOverride("outline_size", 2);
        _streakCountLabel.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_streakCountLabel);

        // Fills as the streak approaches Player.KillStreakMax, i.e. the point where the multiplier
        // stops growing. That's the one piece of the mechanic the player could never see before.
        var barBg = new StyleBoxFlat();
        barBg.BgColor = new Color(0.102f, 0.0588f, 0.1686f, 0.9f);
        barBg.SetCornerRadiusAll(0);

        _streakBarFillStyle = new StyleBoxFlat();
        _streakBarFillStyle.SetCornerRadiusAll(0);

        _streakBar = new ProgressBar();
        _streakBar.CustomMinimumSize = new Vector2(0, 6);
        _streakBar.ShowPercentage = false;
        _streakBar.MaxValue = Player.KillStreakMax;
        _streakBar.MouseFilter = MouseFilterEnum.Ignore;
        _streakBar.AddThemeStyleboxOverride("background", barBg);
        _streakBar.AddThemeStyleboxOverride("fill", _streakBarFillStyle);
        box.AddChild(_streakBar);

        // What the multiplier applies to, on its own short line. Inline it was "×1.9 XP y monedas",
        // which is what made the old one-line version wide enough to reach into the arena — stacked,
        // the same words cost height the badge already has and no width at all.
        var unitLabel = new Label();
        unitLabel.Text = "XP y monedas";
        unitLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
        unitLabel.AddThemeColorOverride("font_color", new Color(0.68f, 0.72f, 0.8f));
        unitLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        unitLabel.AddThemeConstantOverride("outline_size", 2);
        unitLabel.MouseFilter = MouseFilterEnum.Ignore;
        box.AddChild(unitLabel);
    }

    private Tween _roundTimerPulseTween;

    private void PulseRoundTimer()
    {
        _roundTimerPulseTween?.Kill();
        _roundTimerLabel.Scale = Vector2.One * 1.5f;
        _roundTimerPulseTween = _roundTimerLabel.CreateTween();
        _roundTimerPulseTween.TweenProperty(_roundTimerLabel, "scale", Vector2.One, 0.35)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private void ResetRoundTimerLook()
    {
        _lastPulsedSecond = -1;
        _roundTimerPulseTween?.Kill();
        _roundTimerLabel.Scale = Vector2.One;
        _roundTimerLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.9f, 0.95f));
    }

    private void UpdateCooldownIcons()
    {
        if (_player == null) return;

        SetCooldownVisible(_laserIcon, _player.LaserLevel > 0, "Láser");
        _laserIcon.CooldownFraction = _player.LaserCooldownFraction;
        _laserIcon.Accent = TierAccent(UpgradeType.Laser);

        SetCooldownVisible(_missileIcon, _player.MissileLevel > 0, "Misil");
        _missileIcon.CooldownFraction = _player.MissileCooldownFraction;
        _missileIcon.Accent = TierAccent(UpgradeType.Missile);

        SetCooldownVisible(_shieldRegenIcon, _player.ShieldRegenPerMinute > 0f, "Regeneración");
        _shieldRegenIcon.CooldownFraction = _player.ShieldRegenCooldownFraction;
        _shieldRegenIcon.Accent = TierAccent(UpgradeType.ShieldRegen);

        SetCooldownVisible(_ondaIcon, _player.OndaLevel > 0, "Onda de Choque");
        _ondaIcon.CooldownFraction = _player.OndaCooldownFraction;
        _ondaIcon.Accent = TierAccent(UpgradeType.ShockwaveAura);

        SetCooldownVisible(_vendavalIcon, _player.VendavalLevel > 0, "Vendaval");
        _vendavalIcon.CooldownFraction = _player.VendavalCooldownFraction;
        _vendavalIcon.Accent = TierAccent(UpgradeType.Vendaval);

        SetCooldownVisible(_mineIcon, _player.MineLevel > 0, "Mina");
        _mineIcon.CooldownFraction = _player.MineCooldownFraction;
        _mineIcon.Accent = TierAccent(UpgradeType.Mine);

        bool hasUltimate = _player.EquippedUltimate != null;
        SetCooldownVisible(_ultimateIcon, hasUltimate, "Ultimate");
        if (hasUltimate)
            _ultimateIcon.CooldownFraction = _player.UltimateCooldownRemaining / _player.UltimateCooldownDuration;
        _ultimateIcon.Accent = TierAccent(UpgradeType.Ultimate);
    }

    // The icon's colour follows the highest tier ever reached for that ability
    // (Player.OwnedTiers never lowers, see docs/rewards.md), not a fixed per-ability colour anymore —
    // reuses the same 4 tier colours the reward cards already use, rather than inventing a second
    // palette just for the HUD.
    private Color TierAccent(UpgradeType type) =>
        _player.OwnedTiers.TryGetValue(type, out var tier) ? RewardTierRoller.GetTierColor(tier) : Colors.White;

    // Announces the ability by name the first time its icon appears. The icon is a wordless symbol
    // with no legend on screen, so without this the player's first encounter with a new ability is an
    // unexplained shape materialising in the corner. Saying it once, at the moment it unlocks, is
    // what teaches the symbol — and the pause menu's loadout panel lists the same abilities by name
    // for anyone who missed it.
    private void SetCooldownVisible(CooldownIcon icon, bool visible, string abilityName)
    {
        bool wasVisible = _cooldownWasVisible.TryGetValue(icon, out var prev) && prev;
        icon.Visible = visible;

        if (visible && !wasVisible)
        {
            Juice.ValuePop(icon, 1.5f, 0.3f);

            // Skipped on the very first pass, when every already-owned icon "appears" at once —
            // that's the HUD initialising, not the player unlocking anything.
            if (_cooldownWasVisible.Count > 0)
            {
                Juice.FloatingLabel(this, abilityName, icon.GlobalPosition + new Vector2(0f, -22f),
                    Palette.Accent, Palette.FontSize.Caption, driftY: -18f, lifetime: 1.4f);
            }
        }

        _cooldownWasVisible[icon] = visible;
    }

    // Polled the same way _currentBoss/_topBarPanel are — no HP-changed signal exists on Enemy,
    // and everything else in this file already reads live state per frame instead.
    private void UpdateBossHealthBar()
    {
        if (_currentBoss == null || !IsInstanceValid(_currentBoss))
            _currentBoss = GetTree().GetFirstNodeInGroup("boss") as Enemy;

        bool bossAlive = _currentBoss != null && IsInstanceValid(_currentBoss);

        // Fade only on the appear/disappear edge — fromScale 1f since a health bar scaling up/down
        // reads oddly, it just wants the fade. Not a scale-punch modal like everything else.
        if (bossAlive != _bossBarWasVisible)
        {
            // sound: false — this is a health bar, not a modal. The boss arrival has its own sting;
            // a menu whoosh on top of it would read as a UI screen opening that never opened.
            if (bossAlive)
                Juice.ModalIn(_bossHealthBar, 0.25f, 1f, sound: false);
            else
                Juice.ModalOut(_bossHealthBar, null, 0.25f, 1f, sound: false);
            _bossBarWasVisible = bossAlive;
        }

        if (!bossAlive) return;

        _bossHpBar.MaxValue = _currentBoss.MaxHp;
        Juice.BarFill(_bossHpBar, _currentBoss.CurrentHp, 0.25f);

        // A boss round replaces the round timer with a bare "¡JEFE!", so this bar is the player's
        // only sense of how far along the fight is — and the label above it was a hardcoded "JEFE"
        // that repeated what the timer already said and added nothing. A percentage turns the bar
        // from a shape into a readable number.
        int pct = _currentBoss.MaxHp > 0
            ? Mathf.Clamp(Mathf.CeilToInt(_currentBoss.CurrentHp * 100f / _currentBoss.MaxHp), 0, 100)
            : 0;
        _bossNameLabel.Text = string.Format(Tr("JEFE · {0}%"), pct);
    }

    // One heart per life slot, filled/unfilled to show current vs. lost. There's one node per point of
    // Player.MaxLivesCap, so the row never needs a text fallback — which is what the old three-icon
    // version degraded to as soon as a single Corazón was bought.
    private void OnLivesChanged(int current, int max)
    {
        for (int i = 0; i < _heartIcons.Length; i++)
        {
            _heartIcons[i].Visible = i < max;
            _heartIcons[i].Filled = i < current;
            _heartIcons[i].QueueRedraw();
        }

        if (_lastLivesCurrent >= 0 && current != _lastLivesCurrent)
        {
            if (current < _lastLivesCurrent)
            {
                int lostIndex = current;
                if (lostIndex >= 0 && lostIndex < _heartIcons.Length)
                    Juice.Shake(_heartIcons[lostIndex], 5f, 0.3f, new Color(1f, 0.25f, 0.35f));
            }
            else
            {
                int gainedIndex = current - 1;
                if (gainedIndex >= 0 && gainedIndex < _heartIcons.Length)
                    Juice.ValuePop(_heartIcons[gainedIndex], 1.5f, 0.3f);
            }
        }
        _lastLivesCurrent = current;
    }

    // Mirrors the lives row exactly. The whole row hides when the player owns no Barrier, same as the
    // old text line did — an empty row of four unfilled shields would suggest charges they can't have.
    private void OnShieldChanged(int current, int max)
    {
        _shieldRow.Visible = max > 0;
        for (int i = 0; i < _shieldIcons.Length; i++)
        {
            _shieldIcons[i].Visible = i < max;
            _shieldIcons[i].Filled = i < current;
            _shieldIcons[i].QueueRedraw();
        }

        if (_lastShieldCurrent >= 0 && current != _lastShieldCurrent)
        {
            if (current < _lastShieldCurrent)
            {
                int lostIndex = current;
                if (lostIndex >= 0 && lostIndex < _shieldIcons.Length)
                    Juice.Shake(_shieldIcons[lostIndex], 5f, 0.3f, new Color(0.6f, 0.8f, 1f));
            }
            else
            {
                int gainedIndex = current - 1;
                if (gainedIndex >= 0 && gainedIndex < _shieldIcons.Length)
                    Juice.ValuePop(_shieldIcons[gainedIndex], 1.5f, 0.3f);
            }
        }
        _lastShieldCurrent = current;
    }

    // Polled rather than pushed: none of these five have a change event. Damage/fire rate/crit/range only
    // move inside Player.ApplyUpgrade, and EnemiesKilled is a plain field on GameManager — so this follows
    // the same per-frame-read approach UpdateCooldownIcons and UpdateUltimateUi already use.
    private void UpdateStatsLabel()
    {
        if (_player == null) return;

        // Spelled out rather than abbreviated. "CAD" appeared nowhere else in the game — not in the
        // pause menu (which says "Cadencia"), not in the shop (which sells "Fuego Rápido") — so the
        // one number that proved a purchase had worked was labelled with a word the player had never
        // seen. Same for CRIT/RANGO/BAJAS, each of which had two or three other names elsewhere.
        //
        // Two stats per line rather than three, because this label is the widest thing in the panel
        // and therefore the only thing setting its width — the whole top bar was being stretched
        // well past the portrait/hearts row by one long line, leaving dead space beside everything
        // else. Spelling the names out (correctly) made that worse, so the fix is the wrap, not a
        // return to abbreviations.
        string stats =
            $"{Glossary.Damage} {_player.BulletDamage} · {Glossary.FireRate} {_player.FireRate:0.0}/s\n" +
            $"{Glossary.Crit} {_player.CritChance:0}% · {Glossary.Range} {_player.FireRange:0}\n" +
            $"{Glossary.Kills} {GameManager.Instance.EnemiesKilled}";

        _statsLabel.Text = stats;

        int activeCount = 0;
        foreach (var cls in BuildCatalog.ClassOrder)
            if (_player.IsClassActive(cls)) activeCount++;

        _buildPanel.Visible = activeCount > 0;
        if (activeCount == 0)
        {
            _shownBuildCount = 0;
            return;
        }

        // Builds only ever accumulate, so the count changing is the only way the text can change.
        // Everything below is therefore skipped on the vast majority of frames — this method runs
        // every frame, and rebuilding the list and re-joining the string each time would be pure
        // garbage for a label that changes maybe five times in a whole run.
        if (activeCount == _shownBuildCount) return;

        var names = new List<string>(activeCount);
        foreach (var cls in BuildCatalog.ClassOrder)
            if (_player.IsClassActive(cls)) names.Add(BuildCatalog.Name(cls));

        _buildLabel.Text = $"BUILD  {string.Join(" · ", names)}";
        _shownBuildCount = activeCount;

        // A real new unlock — including the very first one, which is the one the player has never
        // seen before and most needs pointed out.
        PlayBuildFlare();
    }

    // Fixed listing order, so the chip never re-shuffles as builds unlock: the set grows but each
    // name keeps its place from run to run. The names themselves come from BuildCatalog, the same
    // source the builds menu and the loadout panel read, so the three can't drift apart.

    // A brief highlight on the whole chip, so a build unlocking mid-fight registers even with the
    // player's eyes on the arena rather than the top bar. Modulate only: the chip lives in a
    // VBoxContainer, and scaling a Control there would visually overlap its neighbours without the
    // layout accounting for it.
    //
    // This was three flashes at a 0.25s period — 4 Hz — peaking at an overbright 2.2x that blew past
    // WorldEnvironment's glow threshold and bloomed hard. Three flashes inside a ~0.75s window is at
    // WCAG 2.3.1's general threshold, fired unprompted during combat. It's now a single, slower,
    // non-overbright pulse: still the brightest thing the chip ever does, but not a strobe.
    // DangerLevel.AlarmLegFloor already applies exactly this reasoning to the alarm bars.
    private void PlayBuildFlare()
    {
        _buildFlareTween?.Kill();

        // Reduced motion drops the animation entirely rather than softening it — the chip's text and
        // border already say a build unlocked; the flare is pure emphasis.
        if (DangerLevel.Reduced)
        {
            _buildPanel.Modulate = Colors.White;
            return;
        }

        _buildFlareTween = CreateTween();
        _buildFlareTween.TweenProperty(_buildPanel, "modulate", new Color(1.5f, 1.5f, 1.5f), 0.22f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _buildFlareTween.TweenProperty(_buildPanel, "modulate", Colors.White, 0.42f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    private void OnXpChanged(int xp, int xpToNext)
    {
        _xpBar.MaxValue = xpToNext;
        Juice.BarFill(_xpBar, xp);
    }

    private void OnLevelUp(int level)
    {
        _levelLabel.Text = string.Format(Tr("Nv {0}"), level);
    }

    // Fires once per AddXp call with the TOTAL levels crossed (not once per threshold like
    // LevelUp above), so a triple level-up from one big XP reward shows a single "+3" rather than
    // three "+1"s landing on top of each other in the same frame. Spawned as a sibling of TopBar,
    // not a child of _levelLabel — parenting it into the VBoxContainer would make VBoxContainer try
    // to lay it out as part of the stat list instead of letting it float freely over everything.
    private void OnLevelsGained(int count)
    {
        Juice.FloatingLabel(this, $"+{count}", _levelLabel.GlobalPosition + new Vector2(_levelLabel.Size.X + 6f, 0f),
            Palette.LevelPopup, Palette.FontSize.Body);
        Juice.ValuePop(_levelLabel, 1.4f, 0.25f);
    }

    // A brief taste of the main menu's fireworks backdrop as an in-game celebration. Same script as
    // the menu's ambient decoration (MenuFireworks) — just given a couple of seconds to live instead
    // of running forever, since here it's a one-off flourish for a level-up rather than a permanent
    // backdrop. Reused rather than duplicated so the two only ever need tuning in one place.
    //
    // Wired to GameManager.LevelUpCelebration rather than LevelsGained/OnLevelsGained above:
    // LevelsGained fires the instant XP crosses a threshold, which is also the instant the upgrade
    // picker opens and the game pauses — fireworks competing with that modal for attention was worse
    // than just waiting the beat until the player has actually picked their reward and play resumes.
    private const float LevelUpFireworksDuration = 2.2f;

    private void SpawnLevelUpFireworks()
    {
        if (DangerLevel.Reduced) return;

        var fireworks = new MenuFireworks();
        AddChild(fireworks);
        GetTree().CreateTimer(LevelUpFireworksDuration).Timeout += () =>
        {
            if (IsInstanceValid(fireworks)) fireworks.QueueFree();
        };
    }

    private void OnRoundChanged(int round)
    {
        _roundLabel.Text = string.Format(Tr("Ronda {0}"), round);
    }

    private void OnCoinsChanged(int coins)
    {
        _coinsLabel.Text = string.Format(Tr("Monedas: {0}"), coins);
        if (_lastCoins >= 0 && coins > _lastCoins)
            Juice.ValuePop(_coinsLabel, 1.25f, 0.2f);
        _lastCoins = coins;
    }

    private void OnScoreChanged(int score)
    {
        int delta = score - _lastScore;
        _lastScore = score;

        if (delta > 0)
        {
            _scoreLabel.Text = string.Format(Tr("Puntaje: {0} (+{1})"), score, delta);
            _scoreDeltaTimer.Start();
            Juice.ValuePop(_scoreLabel, 1.25f, 0.2f);
        }
        else
        {
            _scoreLabel.Text = string.Format(Tr("Puntaje: {0}"), score);
        }
    }
}
