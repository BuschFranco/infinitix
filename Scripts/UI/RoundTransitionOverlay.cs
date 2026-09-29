namespace ShooterLoop;

// A short full-screen fade played across every round boundary from round 2 onward (see
// GameManager.StartNextRound) -- long enough to read as a deliberate transition, short enough to sit
// inside the first beat of the existing 3s "get ready" countdown rather than eating the whole thing.
// The callback runs at the fade's darkest frame, so swapping the background/obstacles underneath it
// is invisible.
//
// ProcessMode.Always for the same reason HUD's own countdown label needs it: StartNextRound() keeps
// the tree paused for the whole countdown (see BeginRoundAfterCountdown), and a Tween on a node that
// isn't Always would simply freeze the instant it starts.
public partial class RoundTransitionOverlay : ColorRect
{
    private const float DefaultFadeTime = 0.35f;

    public override void _Ready()
    {
        AddToGroup("round_transition");
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        Color = Colors.Black;
        Modulate = new Color(1f, 1f, 1f, 0f);
    }

    public void PlayTransition(Action onDarkest, float fadeTime = DefaultFadeTime)
    {
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 1f, fadeTime).SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(Callable.From(() => onDarkest?.Invoke()));
        tween.TweenProperty(this, "modulate:a", 0f, fadeTime).SetTrans(Tween.TransitionType.Sine);
    }
}
