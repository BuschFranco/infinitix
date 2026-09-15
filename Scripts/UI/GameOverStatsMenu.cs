namespace ShooterLoop;

// "Ver Stats" from the Game Over screen — read-only, no Reanudar/Abandonar/settings (the run is
// already over), just the same COMBATE/DEFENSAS/PODERES readout PauseMenu shows
// (Player.BuildCombatStatsLines, shared so the two can't drift apart) plus the same LoadoutMenu
// panel (equipped items + build progress) side by side, same two-panel layout PauseMenu already
// uses. GameOverScreen keeps its own summary (round reached, score, coins, Libras) — this only adds
// what that summary doesn't cover.
public partial class GameOverStatsMenu : Control
{
    private Label _statsLabel;
    private Button _closeButton;
    private PanelContainer _panel;
    private ScrollContainer _scroll;
    private LoadoutMenu _loadoutMenu;
    private Control _hbox;

    // Same widths PauseMenu uses. The matching *heights* are gone: they're derived from the viewport
    // now (UIUtil.AvailableScrollHeight), which is what stops this panel running off a short screen.
    private const float PortraitPanelWidth = 250f;
    private const float PortraitLoadoutWidth = 360f;
    private const float LandscapePanelWidth = 340f;
    private const float LandscapeLoadoutWidth = 460f;

    public override void _Ready()
    {
        AddToGroup("game_over_stats_menu");
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        _statsLabel = GetNode<Label>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/StatsLabel");
        _closeButton = GetNode<Button>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/CloseButton");
        _panel = GetNode<PanelContainer>("CenterContainer/HBox/Panel");
        _scroll = GetNode<ScrollContainer>("CenterContainer/HBox/Panel/Scroll");
        _loadoutMenu = GetNode<LoadoutMenu>("CenterContainer/HBox/LoadoutMenu");
        _hbox = GetNode<Control>("CenterContainer/HBox");

        _panel.AddThemeStyleboxOverride("panel", UIUtil.CreatePanelStyle(Palette.Player));
        var title = GetNode<Label>("CenterContainer/HBox/Panel/Scroll/VBoxContainer/Title");
        UIUtil.AddSpeedLines(title.GetParent<Control>(), title.GetIndex());

        _closeButton.Pressed += Close;
        Juice.WireButtonFeedback(_closeButton);
        UIUtil.WireDimToClose(GetNode<Control>("Dim"), Close);

        // One height for both columns, derived from the live viewport rather than a landscape/portrait
        // pair of constants. They stay equal on purpose (see above); only where the number comes from
        // changed -- see UIUtil.AvailableScrollHeight.
        bool portrait = GameManager.Instance?.CurrentOrientation == GameManager.ScreenOrientation.Portrait;
        _panel.CustomMinimumSize = new Vector2(portrait ? PortraitPanelWidth : LandscapePanelWidth, 0f);

        float height = UIUtil.AvailableScrollHeight(_panel, _scroll);
        _scroll.CustomMinimumSize = new Vector2(0f, height);
        _loadoutMenu.CustomMinimumSize =
            new Vector2(portrait ? PortraitLoadoutWidth : LandscapeLoadoutWidth, height);
    }

    public void Open()
    {
        // The player is still in the tree at this point — GameOverScreen.Open() already relies on
        // the same lookup succeeding (it reads live stats into its own run summary), and nothing
        // frees the player until the user actually leaves this screen (Jugar de nuevo/Menú/Salir).
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        _statsLabel.Text = player != null ? string.Join("\n", player.BuildCombatStatsLines(runEnded: true)) : "";
        _loadoutMenu.Refresh(player);

        Visible = true;
        Juice.ModalIn(_hbox);

        GameManager.Instance?.PushBackHandler(this, Close);
    }

    private void Close()
    {
        GameManager.Instance?.PopBackHandler(this);
        Juice.ModalOut(_hbox, () => Visible = false);
    }
}
