namespace ShooterLoop;

// Split out of AchievementsMenu — missions have no categories, so this is just a fixed list of 3
// rows, no tabs at all. Read-only, same as an achievement row: a mission resolves itself as
// progress is made, nothing here is tappable.
public partial class MissionsMenu : Control
{
    private PanelContainer _panel;
    private ScrollContainer _scroll;
    private Button _closeButton;
    private VBoxContainer _content;
    private VBoxContainer _rows;

    // Same chrome-neon coin icon AchievementsMenu uses for its own Libras reward labels.
    private static readonly Texture2D LibrasCoinIcon = GD.Load<Texture2D>("res://Assets/Sprites/UI/coin_gem.png");

    public override void _Ready()
    {
        Visible = false;

        _panel = GetNode<PanelContainer>("CenterContainer/Panel");
        _scroll = GetNode<ScrollContainer>("CenterContainer/Panel/Scroll");
        _closeButton = GetNode<Button>("CenterContainer/Panel/Scroll/Box/CloseButton");
        _content = GetNode<VBoxContainer>("CenterContainer/Panel/Scroll/Box/Content");

        _panel.AddThemeStyleboxOverride("panel", UIUtil.CreatePanelStyle(Palette.Player));
        var title = GetNode<Label>("CenterContainer/Panel/Scroll/Box/Title");
        UIUtil.AddSpeedLines(title.GetParent<Control>(), title.GetIndex());
        UIUtil.WireDimToClose(GetNode<Control>("Dim"), Close);

        var hint = new Label { Text = "Se renuevan mañana", HorizontalAlignment = HorizontalAlignment.Center };
        hint.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
        hint.AddThemeColorOverride("font_color", new Color(0.65f, 0.72f, 0.82f));
        _content.AddChild(hint);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 8);
        _content.AddChild(_rows);

        _closeButton.Pressed += Close;
        Juice.WireButtonFeedback(_closeButton);

        FitToOrientation();
    }

    private void RebuildRows()
    {
        foreach (var child in _rows.GetChildren()) child.QueueFree();

        foreach (var slot in GameManager.Instance.Missions)
            if (slot != null) _rows.AddChild(BuildMissionRow(slot));

        FitToOrientation();
    }

    private Control BuildMissionRow(GameManager.MissionSlot slot)
    {
        var template = MissionCatalog.Get(slot.TemplateId);
        string name = template.Name.Contains("{0}") ? string.Format(template.Name, slot.Target) : template.Name;

        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.102f, 0.0588f, 0.1686f, 0.75f),
            BorderColor = slot.Completed ? Palette.Player : new Color(0.4902f, 0.9922f, 0.9961f, 0.25f),
        };
        style.SetBorderWidthAll(2);
        style.SetContentMarginAll(8f);
        panel.AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 3);
        panel.AddChild(box);

        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(nameRow);

        var nameLabel = new Label { Text = name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        nameLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Body);
        nameLabel.AddThemeColorOverride("font_color", slot.Completed ? Colors.White : new Color(0.72f, 0.76f, 0.84f));
        nameRow.AddChild(nameLabel);

        if (slot.Completed)
        {
            var checkLabel = new Label { Text = "✓" };
            checkLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Body);
            checkLabel.AddThemeColorOverride("font_color", Palette.Player);
            nameRow.AddChild(checkLabel);
        }

        var coinIcon = new TextureRect
        {
            Texture = LibrasCoinIcon,
            CustomMinimumSize = new Vector2(16f, 16f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        nameRow.AddChild(coinIcon);

        var rewardLabel = new Label { Text = $"+{slot.Reward}" };
        rewardLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Body);
        rewardLabel.AddThemeColorOverride("font_color", slot.Completed ? Palette.Player : new Color(0.75f, 0.55f, 1f));
        nameRow.AddChild(rewardLabel);

        var bar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(0f, 10f),
            MaxValue = slot.Target,
            Value = Mathf.Min(slot.Progress, slot.Target),
            ShowPercentage = false,
        };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.102f, 0.0588f, 0.1686f, 0.8f) });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = slot.Completed ? Palette.Player : new Color(Palette.Player, 0.6f) });
        box.AddChild(bar);

        return panel;
    }

    private void FitToOrientation()
    {
        UIUtil.FitScrollToViewport(_scroll, _panel);
    }

    public void Open()
    {
        GameManager.Instance.EnsureMissionsForToday();
        RebuildRows();

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
