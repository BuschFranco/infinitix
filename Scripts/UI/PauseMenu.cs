namespace ShooterLoop;

public partial class PauseMenu : Control
{
    private Label _statsLabel;
    private Button _resumeButton;
    private Button _menuButton;
    private PanelContainer _pausePanel;
    private ScrollContainer _scroll;
    private LoadoutMenu _loadoutMenu;
    private HSlider _cameraDistanceSlider;
    private Label _cameraDistanceLabel;
    private HSlider _masterVolumeSlider;
    private Label _masterVolumeLabel;
    private HSlider _joystickSlider;
    private Label _joystickLabel;
    private HSlider _ultimateButtonSlider;
    private Label _ultimateButtonLabel;
    private Label _resumeCountdownLabel;
    private Control _hbox;

    // Gives the player a beat to get their thumbs back on the joystick instead of gameplay resuming
    // the instant they tap Reanudar — the same reasoning as the round-start "Preparate..." countdown
    // in HUD/GameManager, just scoped to this menu since resuming from pause has no round-start event
    // of its own to piggyback on.
    private bool _resuming;
    private float _resumeCountdownRemaining;
    private const float ResumeCountdownDuration = 3f;

    public override void _Ready()
    {
        AddToGroup("pause_menu");
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        _statsLabel = GetNode<Label>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/StatsLabel");
        _resumeButton = GetNode<Button>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/ResumeButton");
        _menuButton = GetNode<Button>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/MenuButton");
        _cameraDistanceSlider = GetNode<HSlider>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/CameraDistanceSlider");
        _cameraDistanceLabel = GetNode<Label>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/CameraDistanceLabel");
        _masterVolumeSlider = GetNode<HSlider>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/MasterVolumeSlider");
        _masterVolumeLabel = GetNode<Label>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/MasterVolumeLabel");
        _joystickSlider = GetNode<HSlider>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/JoystickSlider");
        _joystickLabel = GetNode<Label>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/JoystickLabel");
        _ultimateButtonSlider = GetNode<HSlider>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/UltimateButtonSlider");
        _ultimateButtonLabel = GetNode<Label>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/UltimateButtonLabel");
        _pausePanel = GetNode<PanelContainer>("CenterContainer/HBox/Panel");
        _scroll = GetNode<ScrollContainer>("CenterContainer/HBox/Panel/Scroll");
        _loadoutMenu = GetNode<LoadoutMenu>("CenterContainer/HBox/LoadoutMenu");

        // Was the bare default theme panel, the only run-time modal with no accent colour at all --
        // cyan to match PAUSA's own title colour and LoadoutMenu's border right next to it.
        _pausePanel.AddThemeStyleboxOverride("panel", UIUtil.CreatePanelStyle(Palette.Player));
        var title = GetNode<Label>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/Title");
        UIUtil.AddSpeedLines(title.GetParent<Control>(), title.GetIndex());
        _resumeCountdownLabel = GetNode<Label>("ResumeCountdownLabel");
        _hbox = GetNode<Control>("CenterContainer/HBox");
        _resumeButton.Pressed += OnResumePressed;
        _menuButton.Pressed += OnMenuPressed;
        _cameraDistanceSlider.ValueChanged += OnCameraDistanceChanged;
        _masterVolumeSlider.ValueChanged += OnMasterVolumeChanged;
        _joystickSlider.ValueChanged += OnJoystickOpacityChanged;
        _ultimateButtonSlider.ValueChanged += OnUltimateButtonOpacityChanged;
        Juice.WireButtonFeedback(_resumeButton);
        Juice.WireButtonFeedback(_menuButton);

        // The stats/settings column grew past screen height in landscape once the volume slider
        // joined the camera-zoom one below it — same overflow OptionsMenu hit for the same reason
        // (a CenterContainer never bounds a ScrollContainer's height on its own). Bounding Scroll to
        // the same height already used for the loadout panel on the right keeps both columns the same
        // height instead of one overflowing past the other.
        // One height for both columns, derived from the live viewport rather than a landscape/portrait
        // pair of constants. They stay equal on purpose (see above); only where the number comes from
        // changed -- see UIUtil.AvailableScrollHeight.
        bool portrait = GameManager.Instance?.CurrentOrientation == GameManager.ScreenOrientation.Portrait;
        _pausePanel.CustomMinimumSize = new Vector2(portrait ? 250f : 340f, 0f);

        float height = UIUtil.AvailableScrollHeight(_pausePanel, _scroll);
        _scroll.CustomMinimumSize = new Vector2(0f, height);
        _loadoutMenu.CustomMinimumSize = new Vector2(portrait ? 360f : 460f, height);
    }

    public void Open()
    {
        var gm = GameManager.Instance;
        var player = GetTree().GetFirstNodeInGroup("player") as Player;

        var lines = new List<string>();

        lines.Add(Tr("── ESTADO ──"));
        lines.Add(string.Format(Tr("Ronda {0}   Tiempo: {1}s"), gm.RoundNumber, Mathf.CeilToInt(gm.RoundTimeRemaining)));
        lines.Add(string.Format(Tr("{0} {1}   Monedas: {2}   Puntaje: {3}"), Glossary.LevelPrefix, gm.Level, gm.Coins, gm.Score));
        lines.Add(string.Format(Tr("{0}: {1}   Especiales: {2}"), Glossary.Kills, gm.EnemiesKilled, gm.SpecialEnemiesKilled));

        if (player != null)
        {
            lines.Add("");
            lines.AddRange(player.BuildCombatStatsLines());
        }

        _statsLabel.Text = string.Join("\n", lines);
        _cameraDistanceSlider.Value = gm.CameraDistance;
        UpdateCameraDistanceLabel(gm.CameraDistance);
        _masterVolumeSlider.SetValueNoSignal(Mathf.Round(gm.MasterVolume * 100f));
        UpdateMasterVolumeLabel(_masterVolumeSlider.Value);

        _joystickSlider.SetValueNoSignal(Mathf.Round(gm.JoystickOpacity * 100f));
        UpdateJoystickLabel(_joystickSlider.Value);
        _ultimateButtonSlider.SetValueNoSignal(Mathf.Round(gm.UltimateButtonOpacity * 100f));
        UpdateUltimateButtonLabel(_ultimateButtonSlider.Value);

        _loadoutMenu.Refresh(player);

        // Defensive reset: Visible only ever goes false once the countdown below completes, so this
        // shouldn't be reachable mid-countdown — but a fresh Open() is the right place to guarantee
        // the menu never reopens stuck disabled/mid-count regardless.
        _resuming = false;
        _resumeCountdownLabel.Visible = false;
        _resumeButton.Disabled = false;
        _menuButton.Disabled = false;

        Visible = true;
        Juice.ModalIn(_hbox);

        // Back means the same thing "Reanudar" does here, countdown and all — resuming already has a
        // deliberate 3s anti-mistoque delay, and back shouldn't be a way to skip it.
        GameManager.Instance?.PushBackHandler(this, OnResumePressed);
    }

    public override void _Process(double delta)
    {
        if (!_resuming) return;

        _resumeCountdownRemaining -= (float)delta;
        if (_resumeCountdownRemaining <= 0f)
        {
            _resuming = false;
            _resumeCountdownLabel.Visible = false;
            _resumeButton.Disabled = false;
            _menuButton.Disabled = false;
            Visible = false;
            GameManager.Instance.PopBackHandler(this);
            GameManager.Instance.ResumeAfterPause();
            return;
        }

        UpdateResumeCountdownLabel();
    }

    private int _lastCountdownSecond = -1;

    private void UpdateResumeCountdownLabel()
    {
        int secondsLeft = Mathf.CeilToInt(_resumeCountdownRemaining);
        if (secondsLeft != _lastCountdownSecond)
        {
            _resumeCountdownLabel.Text = string.Format(Tr("Reanudando en...\n{0}"), secondsLeft);
            Juice.ValuePop(_resumeCountdownLabel, 1.3f, 0.3f);
            _lastCountdownSecond = secondsLeft;
        }
    }

    // Doesn't resume immediately — counts down first so the player has a beat to get ready instead
    // of gameplay picking back up on the same frame they tapped the button. The pause/loadout panels
    // close first (rather than staying up underneath) so the countdown reads against the gameplay
    // you're about to drop back into, not a menu that's still on screen.
    private void OnResumePressed()
    {
        if (_resuming) return;

        _resuming = true;
        _resumeCountdownRemaining = ResumeCountdownDuration;
        _lastCountdownSecond = -1;
        _resumeButton.Disabled = true;
        _menuButton.Disabled = true;
        Juice.ModalOut(_hbox);
        _resumeCountdownLabel.Visible = true;
        UpdateResumeCountdownLabel();
    }

    // Abandoning a run is the most destructive thing the player can do outside of dying, and it used
    // to be one unconfirmed tap on a button labelled "Menu Principal" — which promises navigation,
    // not the end of the run. Worse, the score vanished with it: RegisterFinalScore only ran from
    // NotifyPlayerDied, so a 40-minute run abandoned at round 19 left no high score and no record row.
    //
    // Both halves are fixed here: it asks first, and GameManager.AbandonRun records the score on the
    // way out, so quitting early costs you the run but not the result.
    private void OnMenuPressed()
    {
        var dialog = GetTree().GetFirstNodeInGroup("confirm_dialog") as ConfirmDialog;
        if (dialog == null)
        {
            AbandonToMenu();
            return;
        }

        dialog.Ask(
            Tr("¿Abandonar la operación?"),
            string.Format(Tr("Vas a volver al menú principal en la ronda {0}. Tu puntaje de {1} queda guardado, pero la operación termina acá."),
                GameManager.Instance.RoundNumber, GameManager.Instance.Score),
            Tr("Abandonar"),
            AbandonToMenu);
    }

    private void AbandonToMenu()
    {
        GameManager.Instance.PopBackHandler(this);
        GameManager.Instance.AbandonRun();
        GetTree().ChangeSceneToFile("res://Scenes/UI/MainMenu.tscn");
    }

    private void OnCameraDistanceChanged(double value)
    {
        GameManager.Instance.CameraDistance = (float)value;
        GameManager.Instance.UpdateCameraExtents();
        UpdateCameraDistanceLabel((float)value);
    }

    // One name for one control. This label used to read "Zoom: N%" when the menu opened and rename
    // itself to "Distancia Cámara: N%" the moment you dragged the slider — the same value, two names,
    // and a third ("Zoom de cámara") on the options screen for the identical setting.
    private void UpdateCameraDistanceLabel(float distance)
    {
        float pct = 2000f / distance * 100f;
        _cameraDistanceLabel.Text = string.Format(Tr("Zoom de cámara: {0}%"), Mathf.RoundToInt(pct));
    }

    // Same setting as OptionsMenu's "Volumen general" slider (GameManager.MasterVolume) — surfaced
    // here too so muting/adjusting doesn't require leaving the run to reach the main menu's Options.
    private void OnMasterVolumeChanged(double value)
    {
        GameManager.Instance.SetMasterVolume((float)value / 100f);
        UpdateMasterVolumeLabel(value);
    }

    private void UpdateMasterVolumeLabel(double value) =>
        _masterVolumeLabel.Text = string.Format(Tr("Volumen general: {0}%"), Mathf.RoundToInt((float)value));

    // Same settings as OptionsMenu's joystick/Ultimate-button opacity sliders — surfaced here too so
    // adjusting them doesn't require abandoning the run to reach the main menu's Options, same
    // reasoning as the master volume slider right above.
    private void OnJoystickOpacityChanged(double value)
    {
        GameManager.Instance?.SetJoystickOpacity((float)value / 100f);
        UpdateJoystickLabel(value);
    }

    private void UpdateJoystickLabel(double value) =>
        _joystickLabel.Text = string.Format(Tr("Opacidad del joystick: {0}%"), Mathf.RoundToInt((float)value));

    private void OnUltimateButtonOpacityChanged(double value)
    {
        GameManager.Instance?.SetUltimateButtonOpacity((float)value / 100f);
        UpdateUltimateButtonLabel(value);
    }

    private void UpdateUltimateButtonLabel(double value) =>
        _ultimateButtonLabel.Text = string.Format(Tr("Opacidad del botón Ultimate: {0}%"), Mathf.RoundToInt((float)value));
}
