namespace ShooterLoop;

// Settings overlay opened from the main menu. Currently: joystick and Ultimate-button opacity, the
// three volumes (general / effects / music), camera distance (zoom), screen orientation and reduced
// motion.
//
// The three volume rows are laid out as a group on purpose: General carries the explanatory hint and
// a full-height separator, while Effects and Music sit under it with tighter 14px gaps and no hints
// of their own. Three consecutive hint paragraphs would be noise, and the indentation-by-spacing is
// what says "these two are inside that one" without needing to write it.
public partial class OptionsMenu : Control
{
    private Label _joystickLabel;
    private HSlider _joystickSlider;
    private Label _ultimateButtonLabel;
    private HSlider _ultimateButtonSlider;
    private Label _masterVolumeLabel;
    private HSlider _masterVolumeSlider;
    private Label _volumeLabel;
    private HSlider _volumeSlider;
    private Label _musicVolumeLabel;
    private HSlider _musicVolumeSlider;
    private Control _musicSeparator;
    private Label _cameraDistanceLabel;
    private HSlider _cameraDistanceSlider;
    private Button _landscapeButton;
    private Button _portraitButton;
    private Button _closeButton;
    private Button _reducedMotionButton;
    private PanelContainer _panel;
    private ScrollContainer _scroll;

    // The rows add up to roughly 1100px, against a logical viewport of 648 in landscape and 1152 in
    // portrait — so the panel overflowed off both ends of the screen in landscape, and portrait had
    // barely 40px of slack that the music row would have eaten as soon as music came back. Hence the
    // ScrollContainer: it fixes both orientations rather than just the one that was reported.
    //
    // A ScrollContainer only scrolls if something bounds its height, and inside a CenterContainer
    // nothing does — it would just grow to fit its content again. These are that bound, sized to
    // leave room for the panel's 16px margins and a margin of comfort at the screen edge. Same
    // approach PauseMenu already uses for its loadout panel.
    // Against 648 and 1152 logical pixels respectively, plus the panel's own 32px of margins: that
    // leaves ~56px of screen edge in landscape (where notches are on the sides) and ~120px in
    // portrait (where they're on top). Portrait barely scrolls at these numbers, which is the point —
    // the cap is there to stop overflow, not to make a short list scroll for no reason.
    private LineEdit _codeInput;
    private Button _codeButton;
    private Label _codeFeedback;

    // The hint the feedback line falls back to when nothing has been submitted yet.
    private const string CodeIdleHint = "Los códigos se canjean una sola vez.";


    public override void _Ready()
    {
        Visible = false;

        _panel = GetNode<PanelContainer>("CenterContainer/Panel");
        _scroll = GetNode<ScrollContainer>("CenterContainer/Panel/Scroll");
        UIUtil.WireDimToClose(GetNode<Control>("Dim"), Close);
        var title = GetNode<Label>("CenterContainer/Panel/Scroll/Box/Title");
        UIUtil.AddSpeedLines(title.GetParent<Control>(), title.GetIndex());
        _joystickLabel = GetNode<Label>("CenterContainer/Panel/Scroll/Box/JoystickLabel");
        _joystickSlider = GetNode<HSlider>("CenterContainer/Panel/Scroll/Box/JoystickSlider");
        _ultimateButtonLabel = GetNode<Label>("CenterContainer/Panel/Scroll/Box/UltimateButtonLabel");
        _ultimateButtonSlider = GetNode<HSlider>("CenterContainer/Panel/Scroll/Box/UltimateButtonSlider");
        _masterVolumeLabel = GetNode<Label>("CenterContainer/Panel/Scroll/Box/MasterVolumeLabel");
        _masterVolumeSlider = GetNode<HSlider>("CenterContainer/Panel/Scroll/Box/MasterVolumeSlider");
        _volumeLabel = GetNode<Label>("CenterContainer/Panel/Scroll/Box/VolumeLabel");
        _volumeSlider = GetNode<HSlider>("CenterContainer/Panel/Scroll/Box/VolumeSlider");
        _musicVolumeLabel = GetNode<Label>("CenterContainer/Panel/Scroll/Box/MusicVolumeLabel");
        _musicVolumeSlider = GetNode<HSlider>("CenterContainer/Panel/Scroll/Box/MusicVolumeSlider");
        _musicSeparator = GetNode<Control>("CenterContainer/Panel/Scroll/Box/SepSfx");
        _cameraDistanceLabel = GetNode<Label>("CenterContainer/Panel/Scroll/Box/CameraDistanceLabel");
        _cameraDistanceSlider = GetNode<HSlider>("CenterContainer/Panel/Scroll/Box/CameraDistanceSlider");
        _landscapeButton = GetNode<Button>("CenterContainer/Panel/Scroll/Box/OrientationRow/LandscapeButton");
        _portraitButton = GetNode<Button>("CenterContainer/Panel/Scroll/Box/OrientationRow/PortraitButton");
        _closeButton = GetNode<Button>("CenterContainer/Panel/Scroll/Box/CloseButton");
        _reducedMotionButton = GetNode<Button>("CenterContainer/Panel/Scroll/Box/ReducedMotionButton");
        _codeInput = GetNode<LineEdit>("CenterContainer/Panel/Scroll/Box/CodeRow/CodeInput");
        _codeButton = GetNode<Button>("CenterContainer/Panel/Scroll/Box/CodeRow/CodeButton");
        _codeFeedback = GetNode<Label>("CenterContainer/Panel/Scroll/Box/CodeFeedback");

        _codeButton.Pressed += SubmitCode;
        // Enter submits too — on a phone keyboard the "go" key is closer than the button is.
        _codeInput.TextSubmitted += _ => SubmitCode();
        Juice.WireButtonFeedback(_codeButton);

        _joystickSlider.ValueChanged += OnJoystickOpacityChanged;
        _ultimateButtonSlider.ValueChanged += OnUltimateButtonOpacityChanged;
        _masterVolumeSlider.ValueChanged += OnMasterVolumeChanged;
        _volumeSlider.ValueChanged += OnVolumeChanged;
        _musicVolumeSlider.ValueChanged += OnMusicVolumeChanged;
        _cameraDistanceSlider.ValueChanged += OnCameraDistanceChanged;
        _closeButton.Pressed += Close;
        Juice.WireButtonFeedback(_closeButton);
        Juice.WireButtonFeedback(_landscapeButton);
        Juice.WireButtonFeedback(_portraitButton);
        Juice.WireButtonFeedback(_reducedMotionButton);

        _reducedMotionButton.Toggled += OnReducedMotionToggled;

        // Re-fitting after the switch matters: this is the one screen that can change the viewport
        // out from under itself, and the whole reason the scroll box needs a height is that the two
        // orientations have very different ones. Without this, flipping to landscape from in here
        // would leave the panel sized for portrait and hanging off both edges.
        _landscapeButton.Toggled += pressed =>
        {
            if (!pressed) return;
            GameManager.Instance?.SetOrientation(GameManager.ScreenOrientation.Landscape);
            FitToOrientation();
        };
        _portraitButton.Toggled += pressed =>
        {
            if (!pressed) return;
            GameManager.Instance?.SetOrientation(GameManager.ScreenOrientation.Portrait);
            FitToOrientation();
        };

        // Belt-and-suspenders for the toggles above: DisplayServer.ScreenSetOrientation (inside
        // SetOrientation) doesn't necessarily land the same frame it's requested, so the FitToOrientation
        // call right after it can race the real resize and compute against the stale viewport size —
        // this is what "Volver" went unreachable behind. Whenever the resize actually lands, however
        // many frames later, this recomputes for real.
        GetTree().Root.SizeChanged += FitToOrientation;

        FitToOrientation();
    }

    // Caps the scrollable area so the panel fits on screen, and drops the cap when the content is
    // short enough not to need it — otherwise a short options list would sit in a tall box with dead
    // space under it.
    private void SubmitCode()
    {
        var result = GameManager.Instance.RedeemCode(_codeInput.Text);

        switch (result)
        {
            case CodeRedeemResult.Ok:
                // Re-read the code that was actually matched rather than echoing what was typed, so
                // the confirmation names the reward.
                SecretCodeCatalog.TryGet(SecretCodeCatalog.Normalise(_codeInput.Text), out var code);
                SetCodeFeedback($"¡Canjeado! {code.Reward}", Palette.Player);
                AudioManager.Instance?.Play(AudioManager.Sfx.UiBuy);
                _codeInput.Clear();
                break;

            case CodeRedeemResult.AlreadyUsed:
                SetCodeFeedback("Ese código ya lo usaste.", Palette.Warning);
                AudioManager.Instance?.Play(AudioManager.Sfx.UiDenied);
                Juice.Shake(_codeInput);
                break;

            default:
                SetCodeFeedback("Código inválido.", Palette.Warning);
                AudioManager.Instance?.Play(AudioManager.Sfx.UiDenied);
                Juice.Shake(_codeInput);
                break;
        }
    }

    private void SetCodeFeedback(string text, Color color)
    {
        _codeFeedback.Text = text;
        _codeFeedback.AddThemeColorOverride("font_color", color);
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
        // Cleared per visit: the result of a code redeemed minutes ago, still sitting there in green
        // the next time Options opens, reads as if something just happened.
        _codeInput.Clear();
        SetCodeFeedback(CodeIdleHint, new Color(0.6f, 0.64f, 0.72f));

        float opacity = GameManager.Instance?.JoystickOpacity ?? 1f;
        _joystickSlider.SetValueNoSignal(Mathf.Round(opacity * 100f));
        UpdateJoystickLabel(_joystickSlider.Value);

        float ultimateOpacity = GameManager.Instance?.UltimateButtonOpacity ?? 1f;
        _ultimateButtonSlider.SetValueNoSignal(Mathf.Round(ultimateOpacity * 100f));
        UpdateUltimateButtonLabel(_ultimateButtonSlider.Value);

        float masterVolume = GameManager.Instance?.MasterVolume ?? 1f;
        _masterVolumeSlider.SetValueNoSignal(Mathf.Round(masterVolume * 100f));
        UpdateMasterVolumeLabel(_masterVolumeSlider.Value);

        float volume = GameManager.Instance?.SfxVolume ?? 1f;
        _volumeSlider.SetValueNoSignal(Mathf.Round(volume * 100f));
        UpdateVolumeLabel(_volumeSlider.Value);

        // Hidden while the game ships no music — a slider that provably controls nothing is worse
        // than an absent one. Its separator goes with it, or the two remaining rows sit in a gap
        // twice the size of the one above them. Drop a music track into Assets/Audio and the row
        // comes back on its own; the setting keeps its saved value in the meantime.
        bool hasMusic = AudioManager.Instance?.HasMusic ?? false;
        _musicVolumeLabel.Visible = hasMusic;
        _musicVolumeSlider.Visible = hasMusic;
        _musicSeparator.Visible = hasMusic;

        float musicVolume = GameManager.Instance?.MusicVolume ?? 0.7f;
        _musicVolumeSlider.SetValueNoSignal(Mathf.Round(musicVolume * 100f));
        UpdateMusicVolumeLabel(_musicVolumeSlider.Value);

        float camDist = GameManager.Instance?.CameraDistance ?? 2000f;
        _cameraDistanceSlider.SetValueNoSignal(camDist);
        UpdateCameraDistanceLabel(camDist);

        bool isPortrait = GameManager.Instance?.CurrentOrientation != GameManager.ScreenOrientation.Landscape;
        _landscapeButton.SetPressedNoSignal(!isPortrait);
        _portraitButton.SetPressedNoSignal(isPortrait);

        bool reduced = GameManager.Instance?.ReducedMotion ?? false;
        _reducedMotionButton.SetPressedNoSignal(reduced);
        UpdateReducedMotionLabel(reduced);

        // Also here, not just in _Ready: the orientation can be changed from the main menu's own
        // toggles or restored from settings after this node was built.
        FitToOrientation();

        // Always reopen at the top. Scrolled to the bottom on the way out, the next Open() would
        // otherwise show the middle of the list with no title, which reads as a broken screen.
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

    // A toggle rather than a slider: it's a binary preference, and toggle_mode gives it a pressed
    // StyleBox that reads as "on" without needing a separate checkbox widget the project doesn't
    // otherwise use. The label states the current state in words too, so "on" doesn't rest purely
    // on the button's fill colour.
    private void OnReducedMotionToggled(bool pressed)
    {
        GameManager.Instance?.SetReducedMotion(pressed);
        UpdateReducedMotionLabel(pressed);
    }

    private void UpdateReducedMotionLabel(bool enabled) =>
        _reducedMotionButton.Text = enabled ? "Movimiento reducido: SÍ" : "Movimiento reducido: NO";

    // No separate mute buttons: a slider that reaches 0 already is the mute, same as the two opacity
    // settings above, and each setter drives its audio bus directly so dragging is audible live.
    private void OnMasterVolumeChanged(double value)
    {
        GameManager.Instance?.SetMasterVolume((float)value / 100f);
        UpdateMasterVolumeLabel(value);
    }

    private void UpdateMasterVolumeLabel(double value) =>
        _masterVolumeLabel.Text = $"Volumen general: {value:0}%";

    private void OnVolumeChanged(double value)
    {
        GameManager.Instance?.SetSfxVolume((float)value / 100f);
        UpdateVolumeLabel(value);
    }

    private void UpdateVolumeLabel(double value) =>
        _volumeLabel.Text = $"Efectos: {value:0}%";

    private void OnMusicVolumeChanged(double value)
    {
        GameManager.Instance?.SetMusicVolume((float)value / 100f);
        UpdateMusicVolumeLabel(value);
    }

    private void UpdateMusicVolumeLabel(double value) =>
        _musicVolumeLabel.Text = $"Música: {value:0}%";

    private void OnJoystickOpacityChanged(double value)
    {
        GameManager.Instance?.SetJoystickOpacity((float)value / 100f);
        UpdateJoystickLabel(value);
    }

    private void UpdateJoystickLabel(double value) =>
        _joystickLabel.Text = $"Opacidad del joystick: {value:0}%";

    private void OnUltimateButtonOpacityChanged(double value)
    {
        GameManager.Instance?.SetUltimateButtonOpacity((float)value / 100f);
        UpdateUltimateButtonLabel(value);
    }

    private void UpdateUltimateButtonLabel(double value) =>
        _ultimateButtonLabel.Text = $"Opacidad del botón Ultimate: {value:0}%";

    private void OnCameraDistanceChanged(double value)
    {
        if (GameManager.Instance == null) return;
        GameManager.Instance.CameraDistance = (float)value;
        GameManager.Instance.UpdateCameraExtents();
        UpdateCameraDistanceLabel(value);
    }

    private void UpdateCameraDistanceLabel(double value)
    {
        float pct = 2000f / (float)value * 100f;
        _cameraDistanceLabel.Text = $"Zoom de cámara: {pct:0}%";
    }
}
