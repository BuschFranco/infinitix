namespace ShooterLoop;

public partial class Shop : Control
{
    private static readonly PackedScene CardScene = GD.Load<PackedScene>("res://Scenes/UI/RewardCard.tscn");

    // Built in code rather than left in the .tscn so it reads from Palette.ShopPanelBorder instead of a
    // hand-synced duplicate of the same hex — the scene used to carry its own copy, which meant the
    // Palette constant was dead code that could silently drift from what shipped.
    private static readonly StyleBoxFlat PanelStyle = UIUtil.CreatePanelStyle(Palette.ShopPanelBorder);

    // Every 10th round's shop (10, 20, 30…) is forced to 3 Legendary offers, gold instead of the
    // usual magenta. Reuses Palette.UltimatePanelBorder rather than a new near-identical gold — that
    // constant already means "the rare, special pick" in this game (UpgradePicker's post-boss
    // Ultimate choice), which is exactly what this shop visit is too.
    private static readonly StyleBoxFlat LegendaryPanelStyle = UIUtil.CreatePanelStyle(Palette.UltimatePanelBorder);
    private const int LegendaryShopInterval = 10;

    private Label _coinsLabel;
    private GridContainer _cardsContainer;
    private Button _continueButton;
    private Button _reloadButton;
    private PanelContainer _panel;
    private Label _emptyHint;
    private Label _title;
    private Label _recapLabel;
    private readonly List<RewardCard> _cards = new();
    private List<UpgradeData> _items;

    // "Nothing more to do with this offer" — true both for something bought and for something that
    // was already useless when the shop opened. Drives CheckAutoAdvance, which only cares whether an
    // offer is still actionable, not why.
    private bool[] _resolved;

    // Strictly "the player paid for this one". Kept separate from _resolved because the card's label
    // reads off it: conflating the two made a reward that was useless from the moment the shop opened
    // render as "Comprada", telling the player they'd bought something they never bought.
    private bool[] _purchased;
    private Tween _coinsTween;

    // Killed and recreated in ShowRoundRecap() rather than left running -- the accent flips between
    // magenta and gold on a Legendary round, and this node is reused for every shop visit in a run,
    // so a fresh Shimmer call each open would otherwise stack a second competing tween on the same
    // property instead of replacing the first.
    private Tween _titleShimmer;

    // 20% of the coin balance at the moment the shop opened — fixed for the whole visit, so buying
    // something (which lowers the balance) never changes what a reroll costs mid-visit.
    private int _reloadCost;
    private const int MinReloadCost = 5;

    // Guards the auto-close path from firing twice (it would call StartNextRound twice).
    private bool _closing;

    // Separate from _closing because CheckAutoAdvance sets that one *before* calling
    // OnContinuePressed, so it can't double as the re-entry guard there. This one covers the window
    // between the button being tapped and the panel's fade-out actually clearing Visible — without
    // it, a fast double tap runs StartNextRound twice.
    private bool _advancing;

    // Gates auto-advance behind the player having actually bought something this visit — see
    // CheckAutoAdvance for why opening into a dead end must NOT close itself.
    private bool _purchasedThisVisit;

    public override void _Ready()
    {
        AddToGroup("shop");
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        _panel = GetNode<PanelContainer>("CenterContainer/Panel");
        _panel.AddThemeStyleboxOverride("panel", PanelStyle);
        _coinsLabel = GetNode<Label>("CenterContainer/Panel/VBoxContainer/CoinsRow/CoinsLabel");
        _cardsContainer = GetNode<GridContainer>("CenterContainer/Panel/VBoxContainer/CardsContainer");
        _continueButton = GetNode<Button>("CenterContainer/Panel/VBoxContainer/ButtonsRow/ContinueButton");
        _reloadButton = GetNode<Button>("CenterContainer/Panel/VBoxContainer/ButtonsRow/ReloadButton");
        _emptyHint = GetNode<Label>("CenterContainer/Panel/VBoxContainer/EmptyHint");
        _title = GetNode<Label>("CenterContainer/Panel/VBoxContainer/Title");
        _recapLabel = GetNode<Label>("CenterContainer/Panel/VBoxContainer/RecapLabel");
        UIUtil.AddSpeedLines(_title.GetParent<Control>(), _title.GetIndex());

        _continueButton.Pressed += OnContinuePressed;
        _reloadButton.Pressed += OnReloadPressed;
        Juice.WireButtonFeedback(_continueButton);
        Juice.WireButtonFeedback(_reloadButton);
        GameManager.Instance.CoinsChanged += OnCoinsChanged;

        ApplyOrientationLayout();
    }

    // Three cards stacked vertically is a *portrait* layout, and it was being used in both
    // orientations. Stacked, the panel needs ~630px of height — against a landscape viewport that is
    // only 648 tall, so the panel overflowed its CenterContainer symmetrically and ran off the top
    // AND bottom of the screen at once, hiding its own title and its own buttons.
    //
    // Landscape has the opposite budget: 1152 wide, 648 tall. So the cards go side by side there and
    // the panel widens to match, which drops the panel to roughly half the height. Same per-orientation
    // branch PauseMenu already uses for its own panel sizing.
    private const int LandscapeCardColumns = 3;
    private const float LandscapePanelWidth = 920f;
    private const float PortraitPanelWidth = 480f;

    private void ApplyOrientationLayout()
    {
        bool portrait = GameManager.Instance?.CurrentOrientation == GameManager.ScreenOrientation.Portrait;

        _cardsContainer.Columns = portrait ? 1 : LandscapeCardColumns;
        _panel.CustomMinimumSize = new Vector2(portrait ? PortraitPanelWidth : LandscapePanelWidth, 0f);
    }

    public override void _ExitTree()
    {
        // Same reasoning as HUD._ExitTree(): GameManager outlives this scene-scoped node.
        if (GameManager.Instance == null) return;
        GameManager.Instance.CoinsChanged -= OnCoinsChanged;
    }

    public void Open()
    {
        _closing = false;
        _advancing = false;
        _purchasedThisVisit = false;

        // Re-applied per open, not just in _Ready: orientation is a settings-menu toggle, and this
        // costs two assignments against a modal that opens once a round.
        ApplyOrientationLayout();
        ShowRoundRecap();

        // Fixed for the whole visit — see the field comment on _reloadCost. Floored so a player with
        // almost nothing doesn't get free infinite rerolls: 20% of 0 rounds to 0, which made the
        // button both free and unlimited exactly when the offers mattered most.
        _reloadCost = Mathf.Max(MinReloadCost, Mathf.RoundToInt(GameManager.Instance.Coins * 0.2f));

        // RollItems refreshes the reload button itself, once the offers it depends on exist. Calling
        // it here as well — before the first roll — threw on a null _items and killed the whole
        // Open() chain, which left the round-end summary on screen with no shop and no way forward.
        RollItems();
        UpdateCoinsLabel(GameManager.Instance.Coins);
        Visible = true;
        Juice.ModalIn(_panel);

        // A no-op, not a Close — this is a mandatory round-transition step, not a screen with a
        // "cancel". Pushing an empty handler still means back gets absorbed here instead of falling
        // through to some contextually wrong action (like reopening Pause) while the shop is up.
        GameManager.Instance?.PushBackHandler(this, () => { });

        CheckAutoAdvance();
    }

    // The end-of-round recap, rendered as the shop's own header rather than as a second window.
    //
    // It used to be a separate panel on its own CanvasLayer, shown when the round ended and left up
    // "so the numbers stay on screen while you decide what to spend them on". The intent was right;
    // the execution couldn't deliver it — this shop panel is nearly full-height in landscape, so the
    // recap sat entirely behind it and was only ever glimpsed for a frame or two as the shop faded
    // out, which read as a window opening and closing by itself. Folding it in here is the same
    // information in the one place the player is already looking, with no second window to layer.
    private void ShowRoundRecap()
    {
        var recap = GameManager.Instance.LastRoundRecap;
        bool legendary = recap.Round > 0 && recap.Round % LegendaryShopInterval == 0;

        // Re-applied per open (like UpgradePicker.Open() already does for its own isUltimate variant)
        // rather than fixed once in _Ready() — this shop needs to look different only on Legendary
        // rounds, every other round.
        _panel.AddThemeStyleboxOverride("panel", legendary ? LegendaryPanelStyle : PanelStyle);
        var accent = legendary ? Palette.UltimatePanelBorder : Palette.ShopPanelBorder;
        _title.AddThemeColorOverride("font_color", accent);
        _recapLabel.AddThemeColorOverride("font_color", new Color(accent, 0.7f));

        _title.Text = legendary ? "★ TIENDA LEGENDARIA ★" : $"RONDA {recap.Round} COMPLETADA";

        _titleShimmer?.Kill();
        _titleShimmer = Juice.Shimmer(_title, "theme_override_colors/font_color",
            accent, accent.Lightened(0.55f), 1.8f);

        // Built as parts so a round with no level-ups doesn't print "+0 niveles", which reads as a
        // thing that failed to happen rather than a thing that didn't apply.
        var parts = new List<string>
        {
            $"+{recap.Coins} monedas",
            $"+{recap.Score} puntaje",
            $"{recap.Kills} {Glossary.Kills.ToLowerInvariant()}",
        };
        if (recap.EliteKills > 0) parts.Add($"{recap.EliteKills} especiales");
        if (recap.Levels > 0) parts.Add($"+{recap.Levels} {(recap.Levels == 1 ? "nivel" : "niveles")}");

        _recapLabel.Text = string.Join("  ·  ", parts);
    }

    // Rolls a fresh set of 3 offers and rebuilds the cards for them. Shared by Open() and the Reload
    // button — a reroll is exactly "open again" for the offers themselves, just without touching the
    // per-visit state (_closing, _purchasedThisVisit, _reloadCost) that Open() alone owns.
    private void RollItems()
    {
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        int round = GameManager.Instance.RoundNumber;
        _items = round > 0 && round % LegendaryShopInterval == 0
            ? UpgradeData.PickLegendaryOnly(3, isUseless: player != null ? player.IsRewardUseless : null)
            : UpgradeData.PickRandomTiered(3, round, RewardSource.Shop, isUseless: player != null ? player.IsRewardUseless : null, fortuneBonus: player?.FortuneBonus ?? 0f);
        _resolved = new bool[_items.Count];
        _purchased = new bool[_items.Count];
        for (int i = 0; i < _items.Count; i++)
            _resolved[i] = player != null && player.IsRewardUseless(_items[i]);

        // Cards are instantiated per roll rather than being three fixed slots in the scene. PickRandomTiered
        // can legitimately return fewer than 3 offers when the pool runs dry, and the old fixed slots left
        // the leftovers on screen as empty cards.
        BuildCards();
        RefreshCards();
        RefreshReloadButton();   // also drives the "nothing here" hint, which depends on the new roll

        for (int i = 0; i < _cards.Count; i++)
            _cards[i].PlayAppearFlare(i * 0.08f);
    }

    private void BuildCards()
    {
        foreach (var card in _cards) card.QueueFree();
        _cards.Clear();

        for (int i = 0; i < _items.Count; i++)
        {
            int index = i;
            var card = CardScene.Instantiate<RewardCard>();

            // Splits the row evenly across however many columns the orientation asked for. Without
            // this the grid sizes each cell to its own content, so three cards with different-length
            // descriptions come out at three different widths — and, same reasoning, three different
            // heights, since a longer description wraps to more lines than a shorter one.
            card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            card.SizeFlagsVertical = SizeFlags.ExpandFill;
            _cardsContainer.AddChild(card);
            card.Activated += () => OnBuyPressed(index);
            _cards.Add(card);
        }
    }

    // Recomputed in full on every refresh: buying one offer changes the coin balance, can re-price a
    // sibling offer of the same UpgradeType via the repurchase surcharge, and can move which offer counts
    // as the strongest of its family.
    private void RefreshCards()
    {
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        bool[] highlight = player?.GetHighlightFlags(_items) ?? new bool[_items.Count];

        for (int i = 0; i < _cards.Count; i++)
        {
            int roundCost = GetRoundAdjustedCost(i);
            int finalCost = GetCost(i);
            _cards[i].Configure(_items[i], finalCost, finalCost - roundCost, highlight[i], "Comprar", _purchased[i]);
        }

        SyncCardContentHeights();
    }

    // Every card in the row gets the same description/stack-info height — the max needed across the
    // whole batch — so a short-text card doesn't end up shorter than its neighbours, and so a long
    // one never gets clipped (RewardCard.ApplyContentHeights). Re-run whenever Configure runs, not
    // just on the initial build: stack-info text ("Tenés: ...") can change length after a purchase.
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

    private void OnCoinsChanged(int coins)
    {
        UpdateCoinsLabel(coins);
        if (!Visible) return;

        RefreshCards();

        // The reload cost itself is frozen for the visit, but affordability isn't — buying something
        // can drop the balance below it, and the button has to stop claiming otherwise.
        RefreshReloadButton();
    }

    // The balance is the single most-consulted number on this screen and used to be a default-size
    // uncoloured label. It's now large and in the same gold as the in-world coin pickup, and it reacts
    // when it changes — the HUD's own coin readout is on a lower CanvasLayer and therefore hidden behind
    // this modal, so this label is the player's only view of their wallet while shopping.
    private void UpdateCoinsLabel(int coins) => _coinsLabel.Text = $"{coins} monedas";

    private void PulseCoins(int spent)
    {
        _coinsTween?.Kill();
        _coinsLabel.PivotOffset = _coinsLabel.Size / 2f;
        _coinsLabel.Scale = Vector2.One * 1.25f;
        _coinsTween = _coinsLabel.CreateTween();
        _coinsTween.TweenProperty(_coinsLabel, "scale", Vector2.One, 0.3f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        SpawnSpentLabel(spent);
    }

    // Spawned as a sibling of the coins row, not a child: a child of a container gets laid out by it,
    // which is the same reason HUD.OnLevelsGained parents its popup where it does.
    private void SpawnSpentLabel(int spent)
    {
        Juice.FloatingLabel(this, $"−{spent}", _coinsLabel.GlobalPosition + new Vector2(_coinsLabel.Size.X + 10f, 0f),
            Palette.Warning, Palette.FontSize.Subtitle, driftY: -26f, holdBeforeFade: 0.3f, lifetime: 0.8f);
    }

    private int GetRoundAdjustedCost(int index)
    {
        return Mathf.RoundToInt(_items[index].Cost * GameManager.Instance.UpgradeCostMultiplier);
    }

    private int GetCost(int index)
    {
        float surcharge = GameManager.Instance.GetShopSurchargeMultiplier(_items[index].Type);
        return Mathf.RoundToInt(GetRoundAdjustedCost(index) * surcharge);
    }

    private void OnBuyPressed(int index)
    {
        if (_resolved[index]) return;

        var item = _items[index];
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        if (player != null && player.IsRewardUseless(item)) return;

        int cost = GetCost(index);
        if (GameManager.Instance.Coins < cost)
        {
            // The one branch a player can reach by tapping a card that looks buyable — the other two
            // returns above are already-bought or maxed-out cards, which say so on their face. This
            // is the case that otherwise produces a tap and total silence.
            AudioManager.Instance?.Play(AudioManager.Sfx.UiDenied);
            return;
        }

        // The purchase itself lands immediately — only the modal's *exit* waits for the flare, so the
        // confirmation animation never delays gameplay state.
        GameManager.Instance.SpendCoins(cost);
        GameManager.Instance.ApplyUpgrade(item);
        GameManager.Instance.RegisterShopPurchase(item.Type);
        _resolved[index] = true;
        _purchased[index] = true;
        _purchasedThisVisit = true;

        _cards[index].PlayConfirmFlare();
        PulseCoins(cost);
        AudioManager.Instance?.Play(AudioManager.Sfx.UiBuy);

        RefreshCards();
        CheckAutoAdvance();
    }

    // Auto-advance is strictly a *reward* for having acted: it fires only after a real purchase, once
    // there's provably nothing left to buy. A shop that opens with everything already maxed or nothing
    // affordable deliberately stays put and waits for "Siguiente Ronda" — closing itself in the player's
    // face before they've read a single offer is worse than asking for one extra tap.
    private void CheckAutoAdvance()
    {
        if (_closing || !_purchasedThisVisit) return;

        bool allResolved = true;
        foreach (var resolved in _resolved)
            if (!resolved) { allResolved = false; break; }

        // Either everything is bought/maxed, or what's left is out of reach. Safe to decide here because
        // the tree is paused while the shop is open: no coins can come in, and buying only ever lowers
        // the balance (and raises that type's surcharge), so neither state can un-fire.
        //
        // Still doesn't close if the player can afford a Reload, though — a fresh roll is another real
        // option on screen, and auto-closing out from under someone who was about to reroll for a
        // better offer is the exact "closing itself in the player's face" this was written to avoid.
        bool canReload = GameManager.Instance.Coins >= _reloadCost;
        if ((allResolved || !AnyPendingAffordable()) && !canReload)
        {
            _closing = true;
            OnContinuePressed();
        }
    }

    // Costs a fixed 20% of the coins the player had when the shop opened (_reloadCost), regardless of
    // what the balance is now — see the field comment. Doesn't touch _purchasedThisVisit: rerolling
    // isn't "having acted on an offer", so it shouldn't by itself make CheckAutoAdvance start closing
    // the shop — but it's still worth re-checking in case an earlier purchase already unlocked it and
    // this reroll happens to leave nothing pending/affordable.
    private void OnReloadPressed()
    {
        // Still guarded even though RefreshReloadButton disables the button below — a disabled Godot
        // Button can't be pressed, but the guard is what makes that a redundancy rather than the only
        // thing standing between the player and a negative balance.
        if (GameManager.Instance.Coins < _reloadCost) return;

        GameManager.Instance.SpendCoins(_reloadCost);
        RollItems();
        PulseCoins(_reloadCost);

        CheckAutoAdvance();
    }

    // The button used to stay fully lit and simply do nothing when the player couldn't afford it —
    // a tap that produced no reroll, no message, and no reason. This mirrors what RewardCard already
    // does for an unaffordable offer ("Faltan N"): say how short you are, not just refuse.
    private void RefreshReloadButton()
    {
        int coins = GameManager.Instance?.Coins ?? 0;
        bool affordable = coins >= _reloadCost;

        _reloadButton.Disabled = !affordable;
        _reloadButton.Text = affordable
            ? $"Recargar · {_reloadCost} monedas"
            : $"Recargar · faltan {_reloadCost - coins}";

        RefreshEmptyHint(affordable);
    }

    // The shop deliberately refuses to auto-close when there's nothing to buy (see CheckAutoAdvance),
    // which is right — but it left the player looking at three greyed-out cards and a dead Reload
    // button with nothing saying "there's nothing here for you". "Siguiente Ronda" doesn't read as
    // "leave the shop" on its own, so this names the situation and points at the way out.
    private void RefreshEmptyHint(bool canReload)
    {
        // Guarded rather than assumed: this runs off the reload button's refresh, which is easy to
        // call from a path that hasn't rolled offers yet — and when that happened the exception took
        // the entire shop-opening chain down with it rather than degrading.
        if (_items == null || _resolved == null)
        {
            _emptyHint.Visible = false;
            return;
        }

        bool anythingToBuy = AnyPendingAffordable();
        _emptyHint.Visible = !anythingToBuy;
        if (anythingToBuy) return;

        bool allResolved = true;
        foreach (var resolved in _resolved)
            if (!resolved) { allResolved = false; break; }

        _emptyHint.Text = allResolved
            ? "Ya no queda nada que te sirva de esta tanda."
            : canReload
                ? "No te alcanza para nada de esto. Podés recargar la tanda o seguir."
                : "No te alcanza para nada de esto ni para recargar. Seguí a la próxima ronda.";
    }

    private bool AnyPendingAffordable()
    {
        int coins = GameManager.Instance.Coins;
        for (int i = 0; i < _items.Count; i++)
        {
            if (_resolved[i]) continue;
            if (coins >= GetCost(i)) return true;
        }
        return false;
    }


    private void OnContinuePressed()
    {
        if (!Visible || _advancing) return;
        _advancing = true;

        GameManager.Instance.PopBackHandler(this);
        Juice.ModalOut(_panel, () => Visible = false);
        GameManager.Instance.StartNextRound();
    }
}
