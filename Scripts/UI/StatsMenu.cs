namespace ShooterLoop;

// Read-only lifetime stats. Three sections: a 7-day trend chart (ActivityBarChart, the one part of
// this screen backed by real time-series data), an "Actividad" grid of colour-coded cards filtered by
// GameManager.StatsPeriod (Total/Mes/Semana), and a "Progreso" section mixing ratio-based
// StatProgressRings (achievements, builds, Legendarias, crit accuracy — stats that genuinely have a
// target to fill toward) with the handful of "Resumen" cards that are just a number (account level,
// best round, characters unlocked...).
//
// Was a single stacked list of identical grey rows before this pass — every stat looked the same
// regardless of what it meant, and nothing here was a chart despite several of these numbers being
// exactly the kind of thing a chart communicates faster than text (a ratio, a week of daily totals).
public partial class StatsMenu : Control
{
    private PanelContainer _panel;
    private ScrollContainer _scroll;
    private Button _closeButton;
    private VBoxContainer _content;
    private VBoxContainer _rows;

    private GameManager.StatsPeriod _period = GameManager.StatsPeriod.Total;
    private readonly Dictionary<GameManager.StatsPeriod, Button> _tabs = new();

    public override void _Ready()
    {
        Visible = false;

        _panel = GetNode<PanelContainer>("CenterContainer/Panel");
        _scroll = GetNode<ScrollContainer>("CenterContainer/Panel/Scroll");
        _closeButton = GetNode<Button>("CenterContainer/Panel/Scroll/Box/CloseButton");
        _content = GetNode<VBoxContainer>("CenterContainer/Panel/Scroll/Box/Content");

        _panel.AddThemeStyleboxOverride("panel", UIUtil.CreatePanelStyle(Palette.OndaBlast));
        var title = GetNode<Label>("CenterContainer/Panel/Scroll/Box/Title");
        UIUtil.AddSpeedLines(title.GetParent<Control>(), title.GetIndex());
        UIUtil.WireDimToClose(GetNode<Control>("Dim"), Close);

        BuildTabs();
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 14);
        _content.AddChild(_rows);

        _closeButton.Pressed += Close;
        Juice.WireButtonFeedback(_closeButton);

        FitToOrientation();
    }

    // --- Total / Mes / Semana tabs ----------------------------------------------------------------

    private void BuildTabs()
    {
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        _content.AddChild(grid);

        AddTab(grid, GameManager.StatsPeriod.Total, "Total");
        AddTab(grid, GameManager.StatsPeriod.Month, "Mes");
        AddTab(grid, GameManager.StatsPeriod.Week, "Semana");
    }

    private void AddTab(GridContainer grid, GameManager.StatsPeriod period, string label)
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

        button.Pressed += () => SelectPeriod(period);
        _tabs[period] = button;
    }

    private void SelectPeriod(GameManager.StatsPeriod period)
    {
        if (_period == period) return;
        _period = period;
        RefreshTabs();
        RebuildRows();
    }

    private void RefreshTabs()
    {
        foreach (var (period, button) in _tabs)
        {
            bool active = period == _period;
            var style = new StyleBoxFlat
            {
                BgColor = active ? new Color(Palette.OndaBlast, 0.28f) : new Color(0.043f, 0.024f, 0.078f, 0.7f),
                BorderColor = Palette.OndaBlast,
            };
            style.SetBorderWidthAll(active ? 3 : 1);
            style.SetContentMarginAll(4f);

            button.AddThemeStyleboxOverride("normal", style);
            button.AddThemeStyleboxOverride("hover", style);
            button.AddThemeStyleboxOverride("pressed", style);
            button.AddThemeColorOverride("font_color", active ? Colors.White : new Color(0.72f, 0.76f, 0.84f));
        }
    }

    // --- Sections --------------------------------------------------------------------------------

    private void RebuildRows()
    {
        foreach (var child in _rows.GetChildren()) child.QueueFree();

        var gm = GameManager.Instance;
        var stats = gm.GetStats(_period);

        _rows.AddChild(SectionHeading("ACTIVIDAD RECIENTE (7 DÍAS)"));
        _rows.AddChild(BuildActivityChart(gm));

        _rows.AddChild(SectionHeading("ACTIVIDAD"));
        var activityGrid = NewCardGrid();
        _rows.AddChild(activityGrid);
        AddCard(activityGrid, "Enemigos\neliminados", stats.EnemiesKilled.ToString(), Palette.Accent);
        AddCard(activityGrid, "Jefes\nderrotados", stats.BossesKilled.ToString(), Palette.UltimatePanelBorder);
        AddCard(activityGrid, "Críticos", stats.CritsLanded.ToString(), Palette.DamageNumber);
        AddCard(activityGrid, "Rondas\nsuperadas", stats.RoundsCleared.ToString(), Palette.OndaBlast);
        AddCard(activityGrid, "Monedas\nganadas", stats.CoinsEarned.ToString(), Palette.MineBlast);
        AddCard(activityGrid, "Libras\nganadas", stats.LibrasEarned.ToString(), Palette.LevelPopup);
        AddCard(activityGrid, "Partidas\njugadas", stats.RunsPlayed.ToString(), Palette.ShopPanelBorder);
        AddCard(activityGrid, "Tiempo\njugado", FormatDuration(stats.PlayTimeSeconds), Palette.ShieldAura);

        _rows.AddChild(SectionHeading("PROGRESO"));
        var ringGrid = NewRingGrid();
        _rows.AddChild(ringGrid);
        AddRing(ringGrid, gm.TotalEnemiesKilled > 0 ? gm.TotalCritsLanded / (float)gm.TotalEnemiesKilled : 0f,
            FormatPercent(gm.TotalCritsLanded, gm.TotalEnemiesKilled), Palette.DamageNumber, "Precisión\n(críticos)");
        AddRing(ringGrid, gm.UnlockedAchievementsCount / (float)AchievementCatalog.All.Length,
            $"{gm.UnlockedAchievementsCount}/{AchievementCatalog.All.Length}", Palette.UltimatePanelBorder, "Logros");
        AddRing(ringGrid, gm.EverCompletedBuilds.Count / (float)BuildCatalog.ClassOrder.Length,
            $"{gm.EverCompletedBuilds.Count}/{BuildCatalog.ClassOrder.Length}", Palette.ShopPanelBorder, "Builds");
        int legendaryTotal = System.Enum.GetValues<UpgradeType>().Length;
        AddRing(ringGrid, gm.EverGotLegendary.Count / (float)legendaryTotal,
            $"{gm.EverGotLegendary.Count}/{legendaryTotal}", Palette.ShieldAura, "Legendarias\ndistintas");

        _rows.AddChild(SectionHeading("RESUMEN GENERAL"));
        var summaryGrid = NewCardGrid();
        _rows.AddChild(summaryGrid);
        AddCard(summaryGrid, "Mejor\npuntaje", GameManager.LoadHighScore().ToString(), Palette.Accent);
        AddCard(summaryGrid, "Mejor ronda\nalcanzada", gm.BestRoundReached.ToString(), Palette.VendavalBlast);
        AddCard(summaryGrid, "Nivel de\ncuenta", gm.AccountLevel.ToString(), Palette.ShieldAura);
        AddCard(summaryGrid, "Misiones\ncompletadas", gm.TotalMissionsCompleted.ToString(), Palette.OndaBlast);
        AddCard(summaryGrid, "Personajes\ndesbloqueados", gm.UnlockedCharacters.Count.ToString(), Palette.LevelPopup);
        AddCard(summaryGrid, "Cosméticos\ncomprados", gm.OwnedCosmetics.Count.ToString(), Palette.MineBlast);

        FitToOrientation();
    }

    private static Control BuildActivityChart(GameManager gm)
    {
        var chart = new ActivityBarChart
        {
            CustomMinimumSize = new Vector2(0f, 100f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            BarColor = Palette.OndaBlast,
        };
        chart.SetData(gm.GetRecentActivity(7));
        return chart;
    }

    private static Label SectionHeading(string text)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
        label.AddThemeColorOverride("font_color", Palette.OndaBlast);
        return label;
    }

    // --- Cards (colour-coded "just a number" stats) -----------------------------------------------

    private static GridContainer NewCardGrid()
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        return grid;
    }

    // A left accent stripe (the stat's own colour) plus a dimmed tint of that colour behind the
    // value, instead of the identical grey panel every row used to share — the colour is what makes
    // this screen readable at a glance instead of a wall of numbers that all look equally important.
    private static void AddCard(GridContainer grid, string label, string value, Color color)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(color, 0.14f),
            BorderColor = new Color(color, 0.8f),
        };
        style.SetBorderWidthAll(1);
        style.BorderWidthLeft = 4;
        style.SetContentMarginAll(8f);
        panel.AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        panel.AddChild(box);

        var valueLabel = new Label { Text = value };
        valueLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Subtitle);
        valueLabel.AddThemeColorOverride("font_color", Colors.White);
        box.AddChild(valueLabel);

        var nameLabel = new Label { Text = label };
        nameLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.76f, 0.84f));
        box.AddChild(nameLabel);

        grid.AddChild(panel);
    }

    // --- Rings (ratio-based "N of M" stats) --------------------------------------------------------

    private static GridContainer NewRingGrid()
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 10);
        return grid;
    }

    private const float RingSize = 76f;

    private static void AddRing(GridContainer grid, float fraction, string centerText, Color color, string caption)
    {
        var cell = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cell.AddThemeConstantOverride("separation", 4);

        var ringRow = new CenterContainer();
        var ring = new StatProgressRing { CustomMinimumSize = new Vector2(RingSize, RingSize) };
        ring.SetValue(fraction, centerText, color);
        ringRow.AddChild(ring);
        cell.AddChild(ringRow);

        var captionLabel = new Label { Text = caption, HorizontalAlignment = HorizontalAlignment.Center };
        captionLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
        captionLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.76f, 0.84f));
        cell.AddChild(captionLabel);

        grid.AddChild(cell);
    }

    // "Xh Ym" above an hour, "Xm Ys" above a minute, "Xs" below — never more than 2 units, so it
    // reads at a glance instead of as a stopwatch readout.
    private static string FormatDuration(int totalSeconds)
    {
        if (totalSeconds >= 3600)
            return $"{totalSeconds / 3600}h {totalSeconds % 3600 / 60}m";
        if (totalSeconds >= 60)
            return $"{totalSeconds / 60}m {totalSeconds % 60}s";
        return $"{totalSeconds}s";
    }

    private static string FormatPercent(int part, int total) =>
        total > 0 ? $"{part * 100f / total:0.#}%" : "—";

    private void FitToOrientation()
    {
        UIUtil.FitScrollToViewport(_scroll, _panel);
    }

    public void Open()
    {
        RefreshTabs();
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
