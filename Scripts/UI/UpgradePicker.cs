namespace ShooterLoop;

public partial class UpgradePicker : Control
{
    private static readonly PackedScene CardScene = GD.Load<PackedScene>("res://Scenes/UI/RewardCard.tscn");

    private static readonly StyleBoxFlat RewardPanelStyle = UIUtil.CreatePanelStyle(Palette.RewardPanelBorder);
    private static readonly StyleBoxFlat UltimatePanelStyle = UIUtil.CreatePanelStyle(Palette.UltimatePanelBorder);

    public const string LevelUpTitle = "¡SUBISTE DE NIVEL! Elige una mejora";
    public const string UltimateTitle = "¡JEFE DERROTADO! Elige tu Ultimate";

    // Kept here rather than left only in the .tscn: the all-useless path below overwrites this label,
    // so the normal wording has to be restorable from code on the next open.
    private const string FreeHintDefault = "Elección gratuita — no gastás monedas";

    private PanelContainer _panel;
    private Label _title;
    private Label _freeHint;
    private GridContainer _cardsContainer;
    private readonly List<RewardCard> _cards = new();
    private List<UpgradeData> _currentChoices;
    private bool _resolving;

    // Killed and recreated on every Open() rather than left running -- the accent colour (and so the
    // shimmer's from/to pair) changes between a level-up pick and the gold Ultimate choice, and this
    // node is reused across many opens in one run, so a fresh Shimmer call each time would otherwise
    // stack a second competing tween on the same property instead of replacing the first.
    private Tween _titleShimmer;

    public override void _Ready()
    {
        AddToGroup("upgrade_picker");
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        _panel = GetNode<PanelContainer>("CenterContainer/Panel");
        _title = GetNode<Label>("CenterContainer/Panel/VBoxContainer/Title");
        _freeHint = GetNode<Label>("CenterContainer/Panel/VBoxContainer/FreeHint");
        _cardsContainer = GetNode<GridContainer>("CenterContainer/Panel/VBoxContainer/CardsContainer");
        UIUtil.AddSpeedLines(_title.GetParent<Control>(), _title.GetIndex());

        ApplyOrientationLayout();
    }

    // Same reasoning as Shop's: three stacked RewardCards is a portrait layout, and in landscape
    // (1152x648) the stack pushes the panel past the viewport height, so it overflows its
    // CenterContainer through the top and bottom at once. Side by side in landscape halves the height.
    private const int LandscapeCardColumns = 3;
    private const float LandscapePanelWidth = 900f;
    private const float PortraitPanelWidth = 460f;

    private void ApplyOrientationLayout()
    {
        bool portrait = GameManager.Instance?.CurrentOrientation == GameManager.ScreenOrientation.Portrait;

        _cardsContainer.Columns = portrait ? 1 : LandscapeCardColumns;
        _panel.CustomMinimumSize = new Vector2(portrait ? PortraitPanelWidth : LandscapePanelWidth, 0f);
    }

    // The same picker serves both level-up rewards and the one-time post-boss Ultimate choice — the
    // heading is passed in rather than baked into the scene, and isUltimate swaps the panel's
    // border/title colour (gold vs. the level-up picker's cyan) so the two can't be mistaken for each
    // other at a glance, the way two identically-styled modals easily could. Both stay visibly distinct
    // from the shop, which is magenta and shows prices where these show GRATIS.
    public void Open(List<UpgradeData> choices, string title = LevelUpTitle, bool isUltimate = false)
    {
        // Nothing to offer at all shouldn't happen (the catalog is never empty), but opening an empty
        // picker would be an unexitable modal, so it's refused outright rather than half-shown.
        if (choices == null || choices.Count == 0) return;

        _currentChoices = choices;
        _resolving = false;
        _title.Text = title;

        // Re-applied per open for the same reason as Shop's: orientation is a settings toggle.
        ApplyOrientationLayout();

        var panelStyle = isUltimate ? UltimatePanelStyle : RewardPanelStyle;
        var accent = isUltimate ? Palette.UltimatePanelBorder : Palette.RewardPanelBorder;
        _panel.AddThemeStyleboxOverride("panel", panelStyle);
        _title.AddThemeColorOverride("font_color", accent);

        // The hint is deliberately a dimmed version of the accent rather than the accent itself.
        // Both used to get the identical colour, so a 20px heading and a 13px sub-line were separated
        // by size alone — and at that distance they read as one block of equally-important text.
        _freeHint.AddThemeColorOverride("font_color", new Color(accent, 0.7f));

        _titleShimmer?.Kill();
        _titleShimmer = Juice.Shimmer(_title, "theme_override_colors/font_color",
            accent, accent.Lightened(0.55f), 1.8f);

        var player = GetTree().GetFirstNodeInGroup("player") as Player;

        // Computed across all 3 offers at once so only the strongest of each reward family is flagged,
        // rather than every offer that merely beats the player's current state.
        bool[] highlight = player?.GetHighlightFlags(choices) ?? new bool[choices.Count];

        // Softlock guard. A pick is free, so there's normally always something takeable — but a player
        // with everything maxed can be offered three useless rewards (PickRandomTiered only falls back to
        // useless picks when nothing useful is left), and this modal has no continue button, so all three
        // cards would be disabled with no way out.
        //
        // Rather than resolving it automatically, the cards are left tappable: the player still decides
        // and still acts, they just can't gain anything from it. Each card's "NO SUMA" chip says so
        // outright, so it isn't a hidden dud.
        bool allUseless = AllChoicesUseless(player, choices);

        // Says so at screen level, not just per-card. The cards do carry a "NO SUMA" chip, but it's
        // two small grey words at the end of a stack row while the button beside it still says the
        // normal "Elegir" — so the honest reading of that screen was "pick the best of three", and a
        // player would take one and wonder why nothing changed.
        if (allUseless)
        {
            _title.Text = "Ya tenés todo esto al máximo";
            _freeHint.Text = "Ninguna de estas opciones te suma nada. Elegí cualquiera para seguir.";
        }
        else
        {
            _freeHint.Text = FreeHintDefault;
        }

        BuildCards(choices, highlight, allUseless);
        Visible = true;
        Juice.ModalIn(_panel);

        for (int i = 0; i < _cards.Count; i++)
            _cards[i].PlayAppearFlare(i * 0.08f);

        // A no-op, not a Close — picking a reward is mandatory, there's no "cancel" here. Absorbs
        // back instead of letting it fall through to something contextually wrong while this is up.
        GameManager.Instance?.PushBackHandler(this, () => { });
    }

    private static bool AllChoicesUseless(Player player, List<UpgradeData> choices)
    {
        if (player == null) return false;
        foreach (var choice in choices)
            if (!player.IsRewardUseless(choice)) return false;
        return true;
    }

    private void BuildCards(List<UpgradeData> choices, bool[] highlight, bool allowUseless)
    {
        foreach (var card in _cards) card.QueueFree();
        _cards.Clear();

        for (int i = 0; i < choices.Count; i++)
        {
            int index = i;
            var card = CardScene.Instantiate<RewardCard>();

            // Even column widths and row heights — without this the grid sizes each cell to its own
            // content, so cards with longer descriptions come out both wider and taller than the rest.
            card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            card.SizeFlagsVertical = SizeFlags.ExpandFill;
            _cardsContainer.AddChild(card);
            // cost: null is what makes the card show GRATIS instead of a price.
            card.Configure(choices[i], cost: null, surcharge: 0, highlight[i], "Elegir", alreadyTaken: false,
                allowUseless: allowUseless);
            card.Activated += () => OnChoicePressed(index);
            _cards.Add(card);
        }

        SyncCardContentHeights();
    }

    // Every card gets the same description/stack-info height — the max needed across the current
    // choices — so a row never ends up uneven, and long text never gets clipped instead of wrapping
    // (RewardCard.ApplyContentHeights). See Shop.cs's identical helper for the full rationale.
    private void SyncCardContentHeights()
    {
        float maxDesc = 0f, maxStack = 0f;
        foreach (var card in _cards)
        {
            maxDesc = Mathf.Max(maxDesc, card.MeasureDescriptionHeight());
            maxStack = Mathf.Max(maxStack, card.MeasureStackHeight());
        }
        foreach (var card in _cards) card.ApplyContentHeights(maxDesc, maxStack);
    }

    private void OnChoicePressed(int index)
    {
        // A single guard covers both input paths (the button and the whole-card tap) — unlike the shop's
        // buy handler, this one has nothing to re-validate against, so without it a fast double tap could
        // apply two upgrades from one level-up.
        if (_resolving) return;
        _resolving = true;
        GameManager.Instance?.PopBackHandler(this);

        var chosen = _currentChoices[index];
        Visible = false;

        // Deferred by one frame rather than called straight from the signal handler. This used to wait
        // out the confirm flare, which read as the game hanging on the click — but it can't be a plain
        // synchronous call either: ApplyUpgradeAndResume may immediately reopen this picker for a queued
        // level-up, and Open() QueueFrees the cards, including the very card whose Activated signal is
        // still on the stack. One deferred frame is imperceptible and sidesteps that entirely.
        Callable.From(() => GameManager.Instance.ApplyUpgradeAndResume(chosen)).CallDeferred();
    }
}
