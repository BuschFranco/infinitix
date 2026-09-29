namespace ShooterLoop;

// Single source of truth for "how dangerous does round N look". Every visual escalation consumer
// (DangerDirector, DangerOverlay, CameraRig's shake caller) reads its numbers from here so they can
// never drift out of step with each other.
//
// Stateless static class, same shape as DifficultyBalancer — this turns a round number into
// presentation values exactly the way that one turns it into a difficulty multiplier. It deliberately
// knows nothing about nodes or the scene tree.
public static class DangerLevel
{
    // 0 through round 3 (the onboarding rounds stay visually clean), first bite at round 4, pinned at
    // 1.0 from round 13 on. The odd-looking Base is derived the same way EnemySpawner's curves are:
    // it's what makes the Min/Max clamps land on exactly those rounds.
    //
    // This used to ramp over rounds 5-20, which meant the arena only looked properly dangerous at a
    // point most runs never reach. Escalating over 3-13 puts the visual language in step with when the
    // game actually gets hard.
    private static readonly RoundCurve Curve = new(-0.2f, 0.1f, 0f, 1f);

    public static float ForRound(int round) => Curve.Evaluate(round);

    // Below this the alarm bars stay hidden entirely (Visible = false, so they cost nothing). With the
    // curve above this now lands on round 6 (0.30 clears at -0.2 + 5*0.1), where it used to be round 9.
    public const float AlarmThreshold = 0.30f;

    // Accessibility escape hatch. When true the alarm holds at its dim trough instead of pulsing,
    // camera shake is halved, and every Juice helper drops its movement while keeping its meaning.
    // Lives here rather than on GameManager because the consumers are static and know nothing about
    // the scene tree; GameManager owns the persisted, user-facing half (SetReducedMotion), and the
    // Options screen has the toggle.
    public static bool Reduced;

    // --- Arena tone ---
    // The backdrop is the ONE thing that must never bloom: WorldEnvironment's glow_hdr_threshold of
    // 0.85 tests the composited pixel, and a glowing floor would wash out everything drawn on it.
    // Both ends of this lerp sit an order of magnitude below that threshold.
    //
    // The danger end keeps a violet undertone rather than going to pure red on purpose. The readability
    // risk here isn't the cyan player (which survives any dark red) — it's EnemyBullet #ff3860 and
    // Warning #ff2e88, which need some blue left in the floor to stay distinguishable from it.
    private static readonly Color BackdropCalm = new("0b0614");
    private static readonly Color BackdropDanger = new("1c0713");

    // Max-danger heartbeat: at danger 1.0 the backdrop breathes slowly between BackdropDanger and this
    // instead of holding a flat colour, so the late game never feels visually static.
    private static readonly Color BackdropDangerPeak = new("25091a");
    public const float HeartbeatLegDuration = 2.5f;

    public static Color BackdropColor(float danger) => BackdropCalm.Lerp(BackdropDanger, danger);
    public static Color BackdropHeartbeatColor() => BackdropDangerPeak;
    public static bool HeartbeatActive(float danger) => danger >= 1f;

    // Multiplicative tint for the arena wall, its grid children, and the obstacles. Capped well short
    // of a full red-shift: push the green/blue channels much lower and the wall's own #ff4fd8 lands on
    // EnemyBullet's hue, at which point the arena boundary starts reading as a threat rather than as
    // scenery. Alpha is deliberately left at 1.0 — Modulate multiplies, and these targets already
    // carry their own 0.7 (wall) and 0.16 (grid) alphas that must not be scaled.
    private static readonly Color BoundsTintDanger = new(1f, 0.72f, 0.80f, 1f);

    public static Color BoundsModulate(float danger) => Colors.White.Lerp(BoundsTintDanger, danger);

    public const float TintTweenDuration = 1.5f;

    // --- Alarm bars ---
    // Bars are drawn at full alpha and the pulse animates Modulate as a COLOUR, not as alpha. That's
    // forced by the same 0.85 composited-pixel threshold: a low-alpha red bar over the dark backdrop
    // composites far below it and would never bloom at any point in its cycle. At alpha 1.0 the bright
    // end of the pulse clears the threshold and blooms while the dim end doesn't — and that contrast
    // is the whole effect. It also means the WorldEnvironment's bloom provides the soft falloff for
    // free, which is why the bars are a single flat rect per edge rather than a stack faking a gradient.
    public static readonly Color AlarmBarColor = new(1f, 0.14f, 0.10f, 1f);
    public const float AlarmBarThicknessSide = 5f;
    public const float AlarmBarThicknessTopBottom = 7f;

    public static float AlarmTrough(float danger) => Mathf.Lerp(0.22f, 0.34f, danger);
    public static float AlarmPeak(float danger) => Mathf.Lerp(0.55f, 1.0f, danger);

    // 0.90s per leg at the threshold down to 0.42s at max danger — i.e. 0.56 Hz to 1.19 Hz, with a hard
    // floor so no future retune can push it into strobe territory. Stays well inside WCAG 2.3.1's
    // three-flashes-per-second limit even accounting for bloom enlarging the perceived flashing area.
    private const float AlarmLegFloor = 0.40f;

    public static float AlarmLegDuration(float danger) =>
        Mathf.Max(AlarmLegFloor, Mathf.Lerp(0.90f, 0.42f, danger));

    // --- Boss spawn ---
    public const float ShakeStrength = 14f;
    public const float ShakeDuration = 0.45f;

    // Threat-level callouts fire on non-boss rounds on purpose. Every multiple of 5 already stacks the
    // pre-round countdown, the round summary, the boss banner and the boss's own arrival — a fifth
    // overlay there is noise. These rounds each coincide with a real escalation step instead (9 is
    // where the alarm bars first appear), and the callout gets the screen to itself.
    public static string ThreatCallout(int round)
    {
        string text = round switch
        {
            9 => "AMENAZA CRECIENTE",
            14 => "ZONA HOSTIL",
            19 => "PELIGRO MÁXIMO",
            _ => null,
        };
        return text == null ? null : TranslationServer.Translate(text);
    }
}
