namespace ShooterLoop;

// Achievements only — missions live in their own screen (MissionsMenu) now that the two are
// separate buttons on MainMenu. Same tab-row-on-top pattern CosmeticsShopMenu uses, just one level
// (category: Progreso/Combate/Builds). Rows are read-only — an achievement resolves itself as stats
// change, there's nothing to tap.
public partial class AchievementsMenu : Control
{
    private PanelContainer _panel;
    private ScrollContainer _scroll;
    private Button _closeButton;
    private VBoxContainer _content;

    private AchievementCategory _category = AchievementCategory.Progress;
    private readonly Dictionary<AchievementCategory, Button> _categoryTabs = new();
    private VBoxContainer _rows;

    private static readonly Texture2D LibrasCoinIcon = GD.Load<Texture2D>("res://Assets/Sprites/UI/coin_gem.png");

    public override void _Ready()
    {
        Visible = false;

        _panel = GetNode<PanelContainer>("CenterContainer/Panel");
        _scroll = GetNode<ScrollContainer>("CenterContainer/Panel/Scroll");
        _closeButton = GetNode<Button>("CenterContainer/Panel/Scroll/Box/CloseButton");
        _content = GetNode<VBoxContainer>("CenterContainer/Panel/Scroll/Box/Content");

        _panel.AddThemeStyleboxOverride("panel", UIUtil.CreatePanelStyle(Palette.UltimatePanelBorder));
        var title = GetNode<Label>("CenterContainer/Panel/Scroll/Box/Title");
        UIUtil.AddSpeedLines(title.GetParent<Control>(), title.GetIndex());
        UIUtil.WireDimToClose(GetNode<Control>("Dim"), Close);

        BuildCategoryTabs();
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 8);
        _content.AddChild(_rows);

        _closeButton.Pressed += Close;
        Juice.WireButtonFeedback(_closeButton);

        FitToOrientation();
    }

    // --- Category tabs -----------------------------------------------------------------------

    private void BuildCategoryTabs()
    {
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        _content.AddChild(grid);

        AddCategoryTab(grid, AchievementCategory.Progress, "Progreso");
        AddCategoryTab(grid, AchievementCategory.Combat, "Combate");
        AddCategoryTab(grid, AchievementCategory.Builds, "Builds");
    }

    private void AddCategoryTab(GridContainer grid, AchievementCategory category, string label)
    {
        var button = new Button
        {
            Text = label,
            CustomMinimumSize = new Vector2(0f, 38f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        button.AddThemeFontSizeOverride("font_size", Palette.FontSize.Body);
        grid.AddChild(button);
        Juice.WireButtonFeedback(button);

        button.Pressed += () => SelectCategory(category);
        _categoryTabs[category] = button;
    }

    private void SelectCategory(AchievementCategory category)
    {
        if (_category == category) return;
        _category = category;
        RefreshCategoryTabs();
        RebuildRows();
    }

    private void RefreshCategoryTabs()
    {
        foreach (var (category, button) in _categoryTabs)
        {
            bool active = category == _category;
            var style = new StyleBoxFlat
            {
                BgColor = active ? new Color(Palette.Player, 0.28f) : new Color(0.043f, 0.024f, 0.078f, 0.7f),
                BorderColor = Palette.Player,
            };
            style.SetBorderWidthAll(active ? 3 : 1);
            style.SetContentMarginAll(4f);

            button.AddThemeStyleboxOverride("normal", style);
            button.AddThemeStyleboxOverride("hover", style);
            button.AddThemeStyleboxOverride("pressed", style);
            button.AddThemeColorOverride("font_color", active ? Colors.White : new Color(0.72f, 0.76f, 0.84f));
        }
    }

    // --- Rows ----------------------------------------------------------------------------------

    private void RebuildRows()
    {
        foreach (var child in _rows.GetChildren()) child.QueueFree();

        foreach (var def in AchievementCatalog.All)
            if (def.Category == _category) _rows.AddChild(BuildAchievementRow(def));

        FitToOrientation();
    }

    private Control BuildAchievementRow(AchievementDef def)
    {
        var gm = GameManager.Instance;
        bool unlocked = gm.IsAchievementUnlocked(def.Id);

        // Once unlocked, the bar reads full regardless of what def.Current(gm) says right now --
        // several achievements track a value that resets or fluctuates within a single run (e.g.
        // "survivor" reads gm.RoundNumber, which is back at 1 the moment a new run starts), while
        // IsUnlocked persists forever once earned. Trusting the live stat for an already-unlocked
        // achievement was showing a checkmark next to a near-empty bar.
        float current = unlocked ? def.Needed : Mathf.Min(def.Current(gm), def.Needed);

        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.102f, 0.0588f, 0.1686f, 0.75f),
            BorderColor = unlocked ? Palette.UltimatePanelBorder : new Color(0.4902f, 0.9922f, 0.9961f, 0.25f),
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

        // Dimmed rather than hidden while locked — same "you can see what you're working toward"
        // reasoning the name/description text already follows, just applied to the badge too.
        var badgeIcon = new TextureRect
        {
            Texture = AchievementCatalog.TierBadges[def.Tier],
            CustomMinimumSize = new Vector2(28f, 28f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Modulate = unlocked ? Colors.White : new Color(1f, 1f, 1f, 0.35f),
        };
        nameRow.AddChild(badgeIcon);

        var nameLabel = new Label { Text = def.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        nameLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Body);
        nameLabel.AddThemeColorOverride("font_color", unlocked ? Colors.White : new Color(0.72f, 0.76f, 0.84f));
        nameRow.AddChild(nameLabel);

        if (unlocked)
        {
            var checkLabel = new Label { Text = "✓" };
            checkLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Body);
            checkLabel.AddThemeColorOverride("font_color", Palette.UltimatePanelBorder);
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

        var rewardLabel = new Label { Text = $"+{def.RewardLibras}" };
        rewardLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Body);
        rewardLabel.AddThemeColorOverride("font_color", unlocked ? Palette.UltimatePanelBorder : new Color(0.75f, 0.55f, 1f));
        nameRow.AddChild(rewardLabel);

        var descLabel = new Label { Text = def.Description };
        descLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
        descLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.72f, 0.82f));
        box.AddChild(descLabel);

        var bar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(0f, 10f),
            MaxValue = def.Needed,
            Value = current,
            ShowPercentage = false,
        };
        bar.AddThemeStyleboxOverride("background", BarStyle(new Color(0.102f, 0.0588f, 0.1686f, 0.8f)));
        bar.AddThemeStyleboxOverride("fill", BarStyle(unlocked ? Palette.UltimatePanelBorder : Palette.Player));
        box.AddChild(bar);

        return panel;
    }

    private static StyleBoxFlat BarStyle(Color color) => new() { BgColor = color };

    private void FitToOrientation()
    {
        UIUtil.FitScrollToViewport(_scroll, _panel);
    }

    public void Open()
    {
        RefreshCategoryTabs();
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
