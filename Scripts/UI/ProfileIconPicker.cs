namespace ShooterLoop;

using Godot;

// Opened by tapping the icon next to the player's name in MainMenu's identity box -- lets the CEO
// swap between any ALREADY-OWNED profile icon without a trip to the Tienda. Buying new ones still
// only happens there; this is just the quick "switch what I already have" picker.
//
// Swatches are plain squares (TextureRect on a flat, zero-corner-radius StyleBoxFlat), matching
// UIUtil.CreatePanelStyle's "square, like every other edge in the game" rule -- no circular portrait
// framing like CharacterSelectMenu uses for pilots, since an icon here is an emblem, not a face.
public partial class ProfileIconPicker : Control
{
    private PanelContainer _panel;
    private GridContainer _grid;
    private Button _closeButton;

    private const float SwatchSize = 48f;
    private const float LockedAlpha = 0.4f;

    public override void _Ready()
    {
        Visible = false;

        _panel = GetNode<PanelContainer>("CenterContainer/Panel");
        _grid = GetNode<GridContainer>("CenterContainer/Panel/Box/Grid");
        _closeButton = GetNode<Button>("CenterContainer/Panel/Box/CloseButton");

        _panel.AddThemeStyleboxOverride("panel", UIUtil.CreatePanelStyle(Palette.Player));

        UIUtil.WireDimToClose(GetNode<Control>("Dim"), Close);
        _closeButton.Pressed += Close;
        Juice.WireButtonFeedback(_closeButton);
    }

    private void RebuildGrid()
    {
        foreach (var child in _grid.GetChildren()) child.QueueFree();

        var gm = GameManager.Instance;
        string equippedId = gm.EquippedCosmetic(CosmeticCategory.ProfileIcon);

        foreach (var option in ProfileIconCatalog.Options)
        {
            bool owned = gm.IsCosmeticOwned(CosmeticCategory.ProfileIcon, option.Id);
            bool equipped = option.Id == equippedId;

            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.102f, 0.0588f, 0.1686f, 0.75f),
                BorderColor = equipped ? Colors.White : owned ? CosmeticCatalog.TierColor(option.Tier) : new Color(0.4902f, 0.9922f, 0.9961f, 0.25f),
            };
            style.SetBorderWidthAll(equipped ? 3 : 2);

            var button = new Button
            {
                CustomMinimumSize = new Vector2(SwatchSize, SwatchSize),
                TooltipText = owned ? option.Name : $"{option.Name} ({option.Cost} Dinero) — Tienda",
            };
            button.AddThemeStyleboxOverride("normal", style);
            button.AddThemeStyleboxOverride("hover", style);
            button.AddThemeStyleboxOverride("pressed", style);
            _grid.AddChild(button);
            Juice.WireButtonFeedback(button);

            var icon = new TextureRect
            {
                Texture = ProfileIconCatalog.Texture(option.Id),
                MouseFilter = MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                AnchorRight = 1f,
                AnchorBottom = 1f,
                Modulate = owned ? Colors.White : new Color(1f, 1f, 1f, LockedAlpha),
            };
            button.AddChild(icon);

            // Locked swatches stay visible (so the picker doubles as a small preview of what the
            // Tienda sells) but do nothing on tap -- buying is the Tienda's job, not this popup's.
            if (owned)
            {
                string id = option.Id;
                button.Pressed += () => Equip(id);
            }
        }
    }

    private void Equip(string id)
    {
        GameManager.Instance?.EquipCosmetic(CosmeticCategory.ProfileIcon, id);
        AudioManager.Instance?.Play(AudioManager.Sfx.UiBuy);
        Close();
    }

    public void Open()
    {
        RebuildGrid();
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
