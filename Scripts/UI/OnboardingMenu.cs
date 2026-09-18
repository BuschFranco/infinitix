namespace ShooterLoop;

using System.Collections.Generic;

// Shown once, on a genuine first launch (see GameManager.ShouldShowOnboarding), before the player
// ever sees the normal main menu -- picks a language and a display name. Unlike every other overlay
// in MainMenu.tscn, this one is NOT dismissible: no UIUtil.WireDimToClose, no PushBackHandler, since
// there's nothing sensible to fall back to if the player backs out without naming themselves.
public partial class OnboardingMenu : Control
{
    private PanelContainer _panel;
    private Button _spanishButton;
    private Button _englishButton;
    private LineEdit _nameEdit;
    private Label _errorLabel;
    private HBoxContainer _iconRow;
    private Button _confirmButton;

    // Tracked separately from GameManager.CurrentLanguage so tapping a button here previews the
    // language live (see the Toggled handlers below) without committing/saving it until Confirm --
    // backing out mid-pick by just closing the app shouldn't half-persist a choice.
    private string _selectedLanguage = "es";

    // Same idea for the icon: only the free options are offered here (see ProfileIconCatalog), and
    // nothing is granted/equipped in GameManager until Confirm.
    private string _selectedIconId;
    private readonly List<Button> _iconButtons = new();

    public override void _Ready()
    {
        Visible = false;

        _panel = GetNode<PanelContainer>("CenterContainer/Panel");
        _spanishButton = GetNode<Button>("CenterContainer/Panel/Box/LanguageRow/SpanishButton");
        _englishButton = GetNode<Button>("CenterContainer/Panel/Box/LanguageRow/EnglishButton");
        _nameEdit = GetNode<LineEdit>("CenterContainer/Panel/Box/NameEdit");
        _errorLabel = GetNode<Label>("CenterContainer/Panel/Box/ErrorLabel");
        _iconRow = GetNode<HBoxContainer>("CenterContainer/Panel/Box/IconRow");
        _confirmButton = GetNode<Button>("CenterContainer/Panel/Box/ConfirmButton");

        BuildIconRow();

        // Live preview, same as OptionsMenu's language picker -- the rest of THIS screen's own
        // static text re-translates immediately too, so picking English shows the name prompt in
        // English right away, before the player has even confirmed anything.
        _spanishButton.Toggled += pressed => { if (pressed) SelectLanguage("es"); };
        _englishButton.Toggled += pressed => { if (pressed) SelectLanguage("en"); };

        _confirmButton.Pressed += Confirm;
        _nameEdit.TextSubmitted += _ => Confirm();

        Juice.WireButtonFeedback(_spanishButton);
        Juice.WireButtonFeedback(_englishButton);
        Juice.WireButtonFeedback(_confirmButton);
    }

    private void SelectLanguage(string code)
    {
        _selectedLanguage = code;
        GameManager.Instance?.SetLanguage(code);
    }

    // Only ProfileIconCatalog.FreeOptions are offered here -- the rest wait for the Tienda, same
    // split as the Comun/Raro/Epico tiers everywhere else in the cosmetics system. Built once in
    // _Ready rather than per-Open since the free list never changes at runtime.
    private void BuildIconRow()
    {
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.102f, 0.0588f, 0.1686f, 0.75f),
            BorderColor = new Color(0.4902f, 0.9922f, 0.9961f, 0.4f),
        };
        normal.SetBorderWidthAll(2);
        var selected = new StyleBoxFlat
        {
            BgColor = new Color(1f, 0.1843f, 0.7255f, 0.85f),
            BorderColor = new Color(0.4902f, 0.9922f, 0.9961f, 1f),
        };
        selected.SetBorderWidthAll(3);

        // A real ButtonGroup, same as LanguageRow's -- lets Godot enforce "exactly one pressed" for
        // free, rather than this code un-pressing siblings by hand.
        var group = new ButtonGroup();

        foreach (var option in ProfileIconCatalog.FreeOptions)
        {
            var button = new Button
            {
                CustomMinimumSize = new Vector2(52f, 52f),
                ToggleMode = true,
                ButtonGroup = group,
                TooltipText = option.Name,
            };
            button.AddThemeStyleboxOverride("normal", normal);
            button.AddThemeStyleboxOverride("hover", normal);
            button.AddThemeStyleboxOverride("pressed", selected);
            _iconRow.AddChild(button);
            Juice.WireButtonFeedback(button);

            var icon = new TextureRect
            {
                Texture = ProfileIconCatalog.Texture(option.Id),
                MouseFilter = MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                AnchorRight = 1f,
                AnchorBottom = 1f,
            };
            button.AddChild(icon);

            string id = option.Id;
            button.Toggled += pressed => { if (pressed) SelectIcon(id); };
            _iconButtons.Add(button);
        }
    }

    private void SelectIcon(string id)
    {
        _selectedIconId = id;
    }

    public void Open()
    {
        _nameEdit.Text = "";
        ShowError("");

        _selectedLanguage = GameManager.Instance?.CurrentLanguage ?? "es";
        _spanishButton.SetPressedNoSignal(_selectedLanguage != "en");
        _englishButton.SetPressedNoSignal(_selectedLanguage == "en");

        // Default to the first free icon so Confirm always has something to equip even if the player
        // never taps a button -- same "sensible default, no forced choice" approach as the language row.
        var firstFree = default(ProfileIconOption?);
        foreach (var option in ProfileIconCatalog.FreeOptions) { firstFree = option; break; }
        _selectedIconId = firstFree?.Id;
        for (int i = 0; i < _iconButtons.Count; i++)
            _iconButtons[i].SetPressedNoSignal(i == 0);

        Visible = true;
        Juice.ModalIn(_panel);
        _nameEdit.GrabFocus();
    }

    private void Confirm()
    {
        string name = _nameEdit.Text.Trim();
        if (name.Length == 0)
        {
            ShowError(Tr("Ingresá un nombre para continuar."));
            return;
        }

        GameManager.Instance?.SetPlayerName(name);
        // Called unconditionally, not just from the Toggled handlers above -- if the player never
        // touched either button (Spanish was already the default), no SetLanguage call would ever
        // have run, and the language save file (what ShouldShowOnboarding actually checks) would
        // never get created -- this screen would come back on every future launch.
        GameManager.Instance?.SetLanguage(_selectedLanguage);

        // Every free icon is granted regardless of which one was tapped -- picking one now shouldn't
        // block switching to the other free option later without a Tienda visit. Only the tapped one
        // gets equipped.
        foreach (var option in ProfileIconCatalog.FreeOptions)
            GameManager.Instance?.GrantCosmetic(CosmeticCategory.ProfileIcon, option.Id);
        if (_selectedIconId != null)
            GameManager.Instance?.EquipCosmetic(CosmeticCategory.ProfileIcon, _selectedIconId);

        Juice.ModalOut(_panel, () => Visible = false);
    }

    // Same shake + red flash as CharacterCreator.ShowError -- this is validation feedback ("that
    // didn't work"), not just changing a label.
    private void ShowError(string message)
    {
        _errorLabel.Text = message;
        _errorLabel.Visible = message.Length > 0;
        if (message.Length > 0)
            Juice.Shake(_errorLabel, 6f, 0.3f, new Color(1f, 0.3f, 0.35f));
    }
}
