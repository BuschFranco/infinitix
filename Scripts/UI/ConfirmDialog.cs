namespace ShooterLoop;

// A yes/no gate in front of anything the player can't undo. Built because nothing in the game asked
// before destroying something: abandoning a run discarded its whole score, and deleting a custom
// pilot erased the photo the player had imported from their own gallery — both one tap away, both
// labelled as if they were ordinary navigation.
//
// The asymmetry this fixes is worth naming: resuming from pause already gets a deliberate 3-second
// protective countdown, and the shop deliberately refuses to auto-close in the player's face. That
// care was being spent on the reversible actions and withheld from every irreversible one.
//
// Callers hand over the consequence, not just the question — "vas a perder el puntaje" rather than
// "¿estás seguro?" — since a confirmation nobody reads is only a speed bump.
public partial class ConfirmDialog : Control
{
    private Label _titleLabel;
    private Label _consequenceLabel;
    private Button _confirmButton;
    private Button _cancelButton;
    private PanelContainer _panel;

    private Action _onConfirm;

    public override void _Ready()
    {
        AddToGroup("confirm_dialog");
        Visible = false;

        // Every caller so far is reachable while the tree is paused (pause menu, game over), so this
        // has to keep processing for its own tweens and input the same way those screens do.
        ProcessMode = ProcessModeEnum.Always;

        _panel = GetNode<PanelContainer>("CenterContainer/Panel");
        _panel.AddThemeStyleboxOverride("panel", UIUtil.CreatePanelStyle(Palette.Warning));
        _titleLabel = GetNode<Label>("CenterContainer/Panel/Box/Title");
        _consequenceLabel = GetNode<Label>("CenterContainer/Panel/Box/Consequence");
        _confirmButton = GetNode<Button>("CenterContainer/Panel/Box/Buttons/ConfirmButton");
        _cancelButton = GetNode<Button>("CenterContainer/Panel/Box/Buttons/CancelButton");

        _confirmButton.Pressed += OnConfirmPressed;
        _cancelButton.Pressed += Close;
        Juice.WireButtonFeedback(_confirmButton);
        Juice.WireButtonFeedback(_cancelButton);

        // Tapping outside cancels — never confirms the destructive action just because the player
        // tried to tap past the dialog.
        UIUtil.WireDimToClose(GetNode<Control>("Dim"), Close);
    }

    // confirmVerb names the actual outcome ("Abandonar", "Borrar") rather than a generic "Sí", so the
    // button still reads correctly to someone who tapped through the sentence above it.
    public void Ask(string title, string consequence, string confirmVerb, Action onConfirm)
    {
        _onConfirm = onConfirm;
        _titleLabel.Text = title;
        _consequenceLabel.Text = consequence;
        _confirmButton.Text = confirmVerb;

        Visible = true;
        Juice.ModalIn(_panel);

        // Cancel takes focus, not confirm: a stray keyboard/gamepad Enter should land on the harmless
        // option, and the destructive one should require actually aiming at it.
        _cancelButton.GrabFocus();

        GameManager.Instance?.PushBackHandler(this, Close);
    }

    private void OnConfirmPressed()
    {
        var action = _onConfirm;

        // Cleared and closed BEFORE invoking: several callers change scene or free this node's whole
        // tree, and a pending callback firing into a half-torn-down dialog is exactly the kind of
        // thing that survives testing and breaks later.
        _onConfirm = null;
        Visible = false;
        GameManager.Instance?.PopBackHandler(this);
        action?.Invoke();
    }

    private void Close()
    {
        GameManager.Instance?.PopBackHandler(this);
        Juice.ModalOut(_panel, () =>
        {
            Visible = false;
            _onConfirm = null;
        });
    }
}
