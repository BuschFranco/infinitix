namespace ShooterLoop;

// Shown exactly once per run — right after Round 1's shop closes, before Round 2 begins. Lives in
// Arena.tscn now (own CanvasLayer, right above Shop's), not the main menu: the player has already
// played one real round before deciding whether to raise the stakes, instead of choosing blind
// before Round 1 even starts. Shop.OnContinuePressed() is the only caller, gated on
// GameManager.RoundNumber == 1 -- the one moment in a run this screen can ever open.
//
// Mandatory, like Shop/UpgradePicker during this same interstitial chain -- no cancel/back button,
// picking 0 contracts is simply the "no thanks" outcome. ProcessMode.Always for the same reason Shop
// needs it: the tree is still paused (from EndRound) when this opens.
//
// Rows are a random 3 of RiskContractCatalog's larger pool, re-rolled every run (RiskContractCatalog.
// PickRandom, called from Open()) rather than a fixed list -- the pool exists precisely so a returning
// player doesn't see the same 3 choices every single run.
//
// Up to 2 of the 3 shown can be toggled on at once (not a ButtonGroup, which only ever allows exactly
// one) — selecting a 3rd while 2 are already active is a no-op, not an error, same "just don't do
// anything" convention the rest of the game's guards already follow.
public partial class RiskContractsMenu : Control
{
    private const int MaxSelected = 2;
    private const int OfferedCount = 3;

    private PanelContainer _panel;
    private VBoxContainer _rowsContainer;
    private Label _totalBonusLabel;
    private Button _confirmButton;

    private readonly List<(Button Button, RiskContractOption Option)> _rows = new();
    private readonly HashSet<string> _selected = new();
    private readonly Random _rng = new();

    // Same re-entry guard OnContinuePressed/OnReloadPressed use in Shop -- without it a fast double
    // tap on Confirm would call StartNextRound() twice.
    private bool _confirmed;

    public override void _Ready()
    {
        AddToGroup("risk_contracts");
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        _panel = GetNode<PanelContainer>("CenterContainer/Panel");
        _rowsContainer = GetNode<VBoxContainer>("CenterContainer/Panel/Box/ContractRows");
        _totalBonusLabel = GetNode<Label>("CenterContainer/Panel/Box/TotalBonusLabel");
        _confirmButton = GetNode<Button>("CenterContainer/Panel/Box/ConfirmButton");

        _panel.AddThemeStyleboxOverride("panel", UIUtil.CreatePanelStyle(new Color(1f, 0.35f, 0.45f)));
        var title = GetNode<Label>("CenterContainer/Panel/Box/Title");
        UIUtil.AddSpeedLines(title.GetParent<Control>(), title.GetIndex());

        _confirmButton.Pressed += Confirm;
        Juice.WireButtonFeedback(_confirmButton);
    }

    private void BuildRows(List<RiskContractOption> offered)
    {
        foreach (var child in _rowsContainer.GetChildren()) child.QueueFree();
        _rows.Clear();

        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.102f, 0.0588f, 0.1686f, 0.75f),
            BorderColor = new Color(0.4902f, 0.9922f, 0.9961f, 0.4f),
        };
        normal.SetBorderWidthAll(2);
        normal.SetContentMarginAll(10f);
        var selected = new StyleBoxFlat
        {
            BgColor = new Color(1f, 0.1843f, 0.7255f, 0.28f),
            BorderColor = new Color(1f, 0.35f, 0.45f, 1f),
        };
        selected.SetBorderWidthAll(3);
        selected.SetContentMarginAll(10f);

        foreach (var option in offered)
        {
            var button = new Button
            {
                ToggleMode = true,
                Alignment = HorizontalAlignment.Left,
            };
            button.AddThemeStyleboxOverride("normal", normal);
            button.AddThemeStyleboxOverride("hover", normal);
            button.AddThemeStyleboxOverride("pressed", selected);
            button.AddThemeFontSizeOverride("font_size", Palette.FontSize.Body);

            // Name + description + the actual Dinero payoff, all as the button's own multi-line text
            // -- the earlier version left the bonus unstated, so there was no way to tell whether a
            // contract was worth the risk before taking it.
            button.Text = string.Format(Tr("{0}\n{1}\n+{2}% Dinero"), Tr(option.Name), Tr(option.Description), BonusPercent(option));
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            button.ClipText = false;
            button.CustomMinimumSize = new Vector2(0f, 70f);

            _rowsContainer.AddChild(button);
            Juice.WireButtonFeedback(button);

            var captured = option;
            button.Toggled += pressed => OnRowToggled(captured, pressed);
            _rows.Add((button, option));
        }
    }

    private static int BonusPercent(RiskContractOption option) => Mathf.RoundToInt((option.LibrasBonusMultiplier - 1f) * 100f);

    private void OnRowToggled(RiskContractOption option, bool pressed)
    {
        if (pressed)
        {
            if (_selected.Count >= MaxSelected)
            {
                // Reverts the tap rather than bumping something else off -- picking exactly which 2
                // is the player's decision, not an FIFO the UI makes for them.
                SetRowPressedNoSignal(option.Id, false);
                AudioManager.Instance?.Play(AudioManager.Sfx.UiDenied);
                return;
            }
            _selected.Add(option.Id);
        }
        else
        {
            _selected.Remove(option.Id);
        }

        RefreshTotalBonus();
    }

    private void SetRowPressedNoSignal(string id, bool pressed)
    {
        foreach (var (button, option) in _rows)
            if (option.Id == id) button.SetPressedNoSignal(pressed);
    }

    // The running total across every contract currently toggled on -- this is the actual answer to
    // "does taking these together pay off", since the individual per-row percentages on their own
    // don't say how they combine (multiplicatively, same as everywhere else in the economy).
    private void RefreshTotalBonus()
    {
        float total = 1f;
        foreach (var (_, option) in _rows)
            if (_selected.Contains(option.Id)) total *= option.LibrasBonusMultiplier;

        int percent = Mathf.RoundToInt((total - 1f) * 100f);
        _totalBonusLabel.Text = percent > 0
            ? string.Format(Tr("Bono total: +{0}% Dinero"), percent)
            : Tr("Sin contratos elegidos — Dinero normal.");
    }

    private void Confirm()
    {
        if (_confirmed) return;
        _confirmed = true;

        var gm = GameManager.Instance;
        foreach (string id in _selected)
        {
            var option = RiskContractCatalog.Find(id);
            if (option == null) continue;

            gm.ContractSpeedMultiplier *= option.Value.SpeedMultiplier;
            gm.ContractHpMultiplier *= option.Value.HpMultiplier;
            gm.ContractRewardMultiplier *= option.Value.RewardMultiplier;
            gm.ContractLibrasBonusMultiplier *= option.Value.LibrasBonusMultiplier;
            if (option.Value.NoShield) gm.ContractNoShield = true;
            if (option.Value.NoHeart) gm.ContractNoHeart = true;
            if (option.Value.NoUltimate) gm.ContractNoUltimate = true;
            gm.ActiveContractNames.Add(option.Value.Name);
        }

        gm.PopBackHandler(this);
        Juice.ModalOut(_panel, () => Visible = false);
        gm.StartNextRound();
    }

    public void Open()
    {
        _selected.Clear();
        _confirmed = false;

        BuildRows(RiskContractCatalog.PickRandom(OfferedCount, _rng));
        RefreshTotalBonus();

        Visible = true;
        Juice.ModalIn(_panel);

        // A no-op, not a Close -- same reasoning Shop.Open() already documents for its own back
        // handler: this is a mandatory round-transition step, not a screen with a cancel.
        GameManager.Instance?.PushBackHandler(this, () => { });
    }
}
