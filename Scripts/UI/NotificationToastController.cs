namespace ShooterLoop;

// Live "achievement/mission just completed" toasts, spawned during actual gameplay -- distinct from
// AchievementsMenu/MissionsMenu, which only reveal state when the player opens them. Subscribes to
// GameManager's AchievementUnlocked/MissionCompleted events (see GameManager.cs) and stacks a small
// card per completion in this node (a VBoxContainer, see NotificationLayer in Arena.tscn).
//
// Deliberately NOT ProcessMode.Always: a toast fired right as the tree pauses (e.g. a round-reached
// mission completing the instant the shop opens) just freezes mid-animation and resumes once the
// player closes whatever modal is up, the same way BossBanner/DangerOverlay already behave -- so a
// toast never renders on top of a modal.
public partial class NotificationToastController : VBoxContainer
{
    private const float HoldDuration = 3.5f;
    private const float FadeDuration = 0.4f;

    private static readonly Texture2D LibrasCoinIcon = GD.Load<Texture2D>("res://Assets/Sprites/UI/coin_gem.png");

    public override void _Ready()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        gm.AchievementUnlocked += def => SpawnToast(
            AchievementCatalog.TierBadges[def.Tier], Palette.UltimatePanelBorder, "¡Logro desbloqueado!", def.Name, def.RewardLibras);

        gm.MissionCompleted += (slot, text) => SpawnToast(
            null, Palette.Player, "¡Misión cumplida!", text, slot.Reward);
    }

    private void SpawnToast(Texture2D icon, Color accent, string kicker, string title, int reward)
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(260f, 0f),
        };
        panel.AddThemeStyleboxOverride("panel", UIUtil.CreatePanelStyle(accent));
        AddChild(panel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        panel.AddChild(row);

        // Achievements show their tier badge; missions have no dedicated icon yet, so a plain "✓" in
        // the accent colour fills the same slot -- same shorthand AchievementsMenu already uses for
        // an unlocked row's checkmark.
        if (icon != null)
        {
            row.AddChild(new TextureRect
            {
                Texture = icon,
                CustomMinimumSize = new Vector2(32f, 32f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
        }
        else
        {
            var check = new Label { Text = "✓", VerticalAlignment = VerticalAlignment.Center };
            check.AddThemeFontSizeOverride("font_size", Palette.FontSize.Title);
            check.AddThemeColorOverride("font_color", accent);
            row.AddChild(check);
        }

        var textColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(textColumn);

        var kickerLabel = new Label { Text = kicker };
        kickerLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
        kickerLabel.AddThemeColorOverride("font_color", accent);
        textColumn.AddChild(kickerLabel);

        var titleLabel = new Label { Text = title, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        titleLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Body);
        titleLabel.AddThemeColorOverride("font_color", Colors.White);
        textColumn.AddChild(titleLabel);

        if (reward > 0)
        {
            var rewardRow = new HBoxContainer();
            rewardRow.AddThemeConstantOverride("separation", 4);
            textColumn.AddChild(rewardRow);

            rewardRow.AddChild(new TextureRect
            {
                Texture = LibrasCoinIcon,
                CustomMinimumSize = new Vector2(14f, 14f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
            var rewardLabel = new Label { Text = $"+{reward}" };
            rewardLabel.AddThemeFontSizeOverride("font_size", Palette.FontSize.Caption);
            rewardLabel.AddThemeColorOverride("font_color", Palette.UltimatePanelBorder);
            rewardRow.AddChild(rewardLabel);
        }

        AudioManager.Instance?.Play(AudioManager.Sfx.LevelUp);

        // Same fade+scale-in / hold / fade-out shape as Juice.FloatingLabel and BossBanner -- built
        // directly here (rather than through a new Juice helper) since this target is a composite
        // PanelContainer, not a bare Label.
        panel.PivotOffset = new Vector2(130f, 0f);
        var tween = panel.CreateTween();
        if (!DangerLevel.Reduced)
        {
            panel.Scale = new Vector2(0.85f, 0.85f);
            panel.Modulate = new Color(1f, 1f, 1f, 0f);
            tween.SetParallel(true);
            tween.TweenProperty(panel, "scale", Vector2.One, 0.25f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(panel, "modulate:a", 1f, 0.2f);
            tween.SetParallel(false);
        }
        else
        {
            panel.Modulate = new Color(1f, 1f, 1f, 0f);
            tween.TweenProperty(panel, "modulate:a", 1f, 0.2f);
        }

        tween.TweenInterval(HoldDuration);
        tween.TweenProperty(panel, "modulate:a", 0f, FadeDuration);
        tween.TweenCallback(Callable.From(() => panel.QueueFree()));
    }
}
