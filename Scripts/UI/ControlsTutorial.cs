namespace ShooterLoop;

// Shown once, on the player's genuine first run (see EnemySpawner._Ready, gated on
// GameManager.TotalRunsPlayed == 0) -- explains movement and the Ultimate before round 1's spawns
// start. Same "mandatory, not dismissible" shape as OnboardingMenu: no UIUtil.WireDimToClose, no
// PushBackHandler, ProcessMode.Always so its own Confirm button works while the caller has paused
// the tree (see EnemySpawner -- a Timer only freezes when the WHOLE tree pauses, not on request).
public partial class ControlsTutorial : Control
{
    [Signal]
    public delegate void ClosedEventHandler();

    private PanelContainer _panel;
    private Button _confirmButton;
    private Label _touchMoveLabel;
    private Label _keyboardMoveLabel;
    private Label _touchUltimateLabel;
    private Label _keyboardUltimateLabel;

    public override void _Ready()
    {
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        _panel = GetNode<PanelContainer>("CenterContainer/Panel");
        _confirmButton = GetNode<Button>("CenterContainer/Panel/Box/ConfirmButton");
        _touchMoveLabel = GetNode<Label>("CenterContainer/Panel/Box/MoveRow/TouchMoveLabel");
        _keyboardMoveLabel = GetNode<Label>("CenterContainer/Panel/Box/MoveRow/KeyboardMoveLabel");
        _touchUltimateLabel = GetNode<Label>("CenterContainer/Panel/Box/UltimateRow/TouchUltimateLabel");
        _keyboardUltimateLabel = GetNode<Label>("CenterContainer/Panel/Box/UltimateRow/KeyboardUltimateLabel");

        _confirmButton.Pressed += Close;
        Juice.WireButtonFeedback(_confirmButton);
    }

    public void Open()
    {
        // The only real touch/keyboard signal Godot exposes -- there's no way to know which one the
        // player will actually reach for, so both control schemes stay fully visible always (see the
        // .tscn); this only dims whichever one the device makes less likely, as a hint, not a hider.
        bool touchLikely = DisplayServer.IsTouchscreenAvailable();
        Highlight(_touchMoveLabel, touchLikely);
        Highlight(_keyboardMoveLabel, !touchLikely);
        Highlight(_touchUltimateLabel, touchLikely);
        Highlight(_keyboardUltimateLabel, !touchLikely);

        Visible = true;
        Juice.ModalIn(_panel);
    }

    private static void Highlight(Label label, bool emphasized) =>
        label.Modulate = emphasized ? Colors.White : new Color(1f, 1f, 1f, 0.55f);

    private void Close()
    {
        Juice.ModalOut(_panel, () =>
        {
            Visible = false;
            EmitSignal(SignalName.Closed);
        });
    }
}
