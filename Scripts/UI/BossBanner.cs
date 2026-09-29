namespace ShooterLoop;

public partial class BossBanner : Control
{
    private Label _label;
    private Control _stripes;

    // Hazard chevrons framing the announcement, built procedurally like everything else in the project.
    // Top edge only: in portrait a bottom row would land squarely on the virtual joystick's touch area.
    private const int StripeCount = 8;
    private const float StripeWidth = 44f;
    private const float StripeHeight = 22f;
    private const float StripeSlant = 26f;
    private static readonly Color StripeColor = new(1f, 0.63f, 0.04f, 0.85f);

    public override void _Ready()
    {
        AddToGroup("boss_banner");
        MouseFilter = MouseFilterEnum.Ignore;
        _label = GetNode<Label>("Label");
        BuildStripes();
        Modulate = new Color(1f, 1f, 1f, 0f);
    }

    // Children of this node, so they inherit its Modulate — the existing 2s-hold + 0.6s fade in
    // Announce covers them for free without a second animation to keep in sync.
    private void BuildStripes()
    {
        _stripes = new Control();
        _stripes.MouseFilter = MouseFilterEnum.Ignore;
        _stripes.AnchorLeft = 0f;
        _stripes.AnchorRight = 1f;
        _stripes.AnchorTop = 0f;
        _stripes.AnchorBottom = 0f;
        _stripes.OffsetTop = 0f;
        _stripes.OffsetBottom = StripeHeight;
        AddChild(_stripes);

        for (int i = 0; i < StripeCount; i++)
        {
            var stripe = new Polygon2D();
            stripe.Color = StripeColor;
            stripe.Polygon = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(StripeWidth, 0f),
                new Vector2(StripeWidth - StripeSlant, StripeHeight),
                new Vector2(-StripeSlant, StripeHeight),
            };
            stripe.Position = new Vector2(i * (StripeWidth * 2f), 0f);
            _stripes.AddChild(stripe);
        }
    }

    // How long the banner holds at full opacity before fading. Long enough for the klaxon
    // (boss_alarm.wav is ~1.02s) to finish underneath it, so the sound and the sign end together
    // rather than the text vanishing mid-alarm.
    private const float HoldDuration = 1.6f;
    private const float SlamTime = 0.18f;
    private const int ThrobCycles = 3;
    private const float ThrobTime = 0.22f;

    private static readonly Color AlarmRed = new(1f, 0.15f, 0.1f);
    private static readonly Color AlarmWhite = new(1f, 0.86f, 0.72f);

    public void Announce(int round)
    {
        _label.Text = string.Format(Tr("¡RONDA DE JEFE {0}!"), round);
        Modulate = new Color(1f, 1f, 1f, 0f);

        AudioManager.Instance?.Play(AudioManager.Sfx.BossAlarm);

        // Slides in from the left as the banner appears, then rides the banner's own fade out.
        _stripes.Position = new Vector2(-120f, 0f);
        var slide = _stripes.CreateTween();
        slide.TweenProperty(_stripes, "position", Vector2.Zero, 0.35f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        // The label slams in oversized and settles, rather than the whole banner simply fading up.
        // Pivot has to be set from the label's own size, and that size isn't resolved until the
        // control has been laid out at least once — hence reading Size here rather than in _Ready.
        _label.PivotOffset = _label.Size / 2f;
        _label.Scale = Vector2.One * 1.6f;
        var slam = _label.CreateTween();
        slam.TweenProperty(_label, "scale", Vector2.One, SlamTime)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        // Then it throbs, in step with the klaxon's two-tone cycle — three pulses over roughly the
        // 1.02s the sound runs for, so the sign looks like it's making the noise.
        for (int i = 0; i < ThrobCycles; i++)
        {
            slam.TweenProperty(_label, "scale", Vector2.One * 1.09f, ThrobTime * 0.5f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
            slam.TweenProperty(_label, "scale", Vector2.One, ThrobTime * 0.5f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        }

        // A separate tween on the colour so it can flash on its own cadence: red is the resting
        // state and the white flashes are the "light" turning over, which is what sells it as an
        // alarm rather than as a title card.
        var flash = _label.CreateTween();
        for (int i = 0; i < ThrobCycles + 1; i++)
        {
            flash.TweenProperty(_label, "theme_override_colors/font_color", AlarmWhite, ThrobTime * 0.35f);
            flash.TweenProperty(_label, "theme_override_colors/font_color", AlarmRed, ThrobTime * 0.65f);
        }

        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 1f, 0.12f);
        tween.TweenInterval(HoldDuration);
        tween.TweenProperty(this, "modulate:a", 0f, 0.5f);
        // Leaves the label at its resting colour and scale — the banner is reused every boss round,
        // and a half-finished throb would be where the next Announce starts from.
        tween.TweenCallback(Callable.From(() =>
        {
            _label.Scale = Vector2.One;
            _label.AddThemeColorOverride("font_color", AlarmRed);
        }));
    }
}
