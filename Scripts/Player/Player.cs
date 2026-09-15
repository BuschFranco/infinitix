namespace ShooterLoop;

public partial class Player : CharacterBody2D
{
    [Export] public float MoveSpeed = 290f;
    [Export] public int MaxLives = 3;
    [Export] public float FireRate = 3f;
    [Export] public int BulletDamage = 10;
    [Export] public float FireRange = 220f;

    // Owned here rather than left to Bullet.tscn's own default so every shot the player fires gets
    // it from one place. Fixed stat — there's no reward that raises it.
    [Export] public float BulletSpeed = 500f;
    [Export] public PackedScene BulletScene;
    [Export] public PackedScene MissileScene;
    [Export] public PackedScene PlayerMineScene;
    [Export] public PackedScene OrbitBladeScene;
    [Export] public PackedScene CompanionScene;
    [Export] public PackedScene SpeedTrailSegmentScene;
    [Export] public Vector2 ArenaHalfExtents = new(2200f, 1400f);
    [Export] public float LevelUpBurstRadius = 220f;

    private const float MaxFireRate = 12f;

    // Hard ceilings for every freely-stacking reward, so repeated purchases can keep helping
    // without any single stat spiraling unboundedly over a long run.
    private const float MaxBulletDamageBonus = 200f;
    private const float MaxFireRangeBonus = 230f;

    // +70 over the 290 base (~+24%) — enough for Movement Speed picks to feel meaningfully faster
    // without a fully-invested late-round build turning into an unreadable blur ("flash").
    private const float MaxMoveSpeedBonus = 70f;

    // Absolute ceiling on FireRange, separate from the bonus cap above. Without it the only limit
    // was "base + 500", which isn't a stated ceiling so much as a side effect — and a range that
    // outgrows the visible screen stops being a meaningful stat, since you can't see what you're
    // shooting at anyway. This is the number that actually binds.
    private const float MaxFireRange = 450f;
    // 4, not 6: lowered as part of closing the late-round "practically invincible" problem — a
    // smaller life pool is less stack for DifficultyBalancer.GetSurvivabilityCatchUpMultiplier to
    // have to counteract (see GetSurvivabilityScore). Matches MaxShieldChargesCap below, and the
    // HUD's heart row is sized to exactly this many icons, same as the shield row.
    // Public because the loadout panel prints this ceiling to the player. It used to hardcode its own
    // "(máx 6)" against this being 6, and kept saying 6 after the cap dropped to 4 — i.e. the UI
    // promised a ceiling the game would never allow. Reading the constant is what makes that
    // class of drift impossible rather than merely fixed once.
    public const int MaxLivesCap = 4;

    // Was 8, which was unreachable dead code: Barrier tiers are worth 1/2/3/4 and apply as
    // Max(current, value) rather than summing, so 4 was always the real ceiling. Now the constant says
    // what the game actually does — which matters more than before, since the shield row is on screen.
    // Public for the same reason as MaxLivesCap above.
    public const int MaxShieldChargesCap = 4;

    // Part of the same late-round survivability nerf as MaxLivesCap: from round 17 on, the passive
    // Regeneración timer runs at half its purchased rate. ShieldRegenPerMinute itself is left
    // untouched (it still drives the upgrade-comparison logic and the shop tooltip's raw "X/min"),
    // this only slows down the timer that actually spends it.
    private const int LateShieldRegenRound = 17;
    private const float LateShieldRegenMultiplier = 0.5f;
    public const int MaxExtraFiringLinesCap = 5;
    private const float SideShotSpacing = 14f;

    private const int MaxPierceCap = 5;
    private const float MaxCritChance = 60f;        // percent
    private const float MaxBulletKnockbackBonus = 400f;
    private const float MaxCoinBonusPercent = 150f;
    private const float MaxXpBonusPercent = 100f;

    public int CurrentLives;
    public int MaxShieldCharges = 0;
    public int CurrentShieldCharges = 0;
    public int OrbitCount = 0;
    public float CompanionStatPercent = 0f;
    public int ExtraFiringLines = 0;
    public int LaserLevel = 0;
    public int MissileLevel = 0;
    public int MineLevel = 0;
    public int OndaLevel = 0;
    public int VendavalLevel = 0;
    public int BurnLevel = 0;
    public int BulletPierce = 0;
    public float CritChance = 0f;            // percent, 0-60
    public float BulletKnockback = 0f;
    public float CoinBonusPercent = 0f;      // percent, 0-150
    public float XpBonusPercent = 0f;        // percent, 0-100
    public float ShieldRegenPerMinute = 0f;
    public float DodgeChance = 0f;           // percent, 0-25
    public float FortuneBonus = 0f;          // percent added to Rare/Epic/Legendary odds
    public int RicochetCount = 0;            // number of additional bounces

    // Rebote IV (the Legendary tier) is the only ricochet that makes side-shot lines bounce too.
    // See FireInDirection: Common/Rare ranks would otherwise turn the whole side volley into free
    // multi-targeting.
    public const int LegendaryRicochetCount = 4;
    public enum BuildClass { None, Gunner, Tank, Assassin, Explorer, Pyromaniac, Armored, Hunter }

    public float EffectiveFireRange => FireRange * GetClassFireRangeMultiplier();

    // Build class system: grants passive bonuses based on stat thresholds.
    // Every class whose stat combination is met stays active — unlocked builds never deactivate
    // (stats only go up), so this is a growing set. Passives from multiple classes stack at once;
    // each one touches a different stat, so there's no conflict between them.
    private readonly HashSet<BuildClass> _activeClasses = new();
    public IEnumerable<BuildClass> ActiveClasses => _activeClasses;
    public bool IsClassActive(BuildClass cls) => cls != BuildClass.None && _activeClasses.Contains(cls);

    public float ThornsDamage = 0f;          // damage dealt to enemies on hit
    private float _thornsCooldown;

    // Kill streak system: rewards rapid kills with escalating XP/coin bonuses.
    private int _killStreak;
    private float _killStreakTimer;
    private const float KillStreakWindow = 2.0f;   // seconds between kills to maintain streak
    // Public so the HUD's streak badge can show progress toward the cap rather than an open-ended
    // number — the multiplier stops growing here, and a bar that fills to exactly this point is what
    // makes that legible without spelling it out.
    public const int KillStreakMax = 60;             // cap at ×5.5 bonus
    private const float KillStreakMaxBonus = 4.5f;   // +450% at the cap
    // Concave curve (exponent < 1): the early game feels the same as a flat +10%/kill (or a hair
    // better) all the way through the streak counts a casual run can reach, then keeps extending far
    // past that only for the long streaks that are realistically only sustainable in late rounds.
    // Anchored so streak=10 lands on exactly the same +100% the old flat formula gave, and streak=60
    // lands on exactly the new +450% cap.
    private const float KillStreakCurveExponent = 0.84f;

    public int KillStreak => _killStreak;

    public float KillStreakMultiplier
    {
        get
        {
            float t = Mathf.Min(_killStreak, KillStreakMax) / (float)KillStreakMax;
            return 1f + KillStreakMaxBonus * Mathf.Pow(t, KillStreakCurveExponent);
        }
    }

    public bool HasExtraProjectile => _hasExtraProjectile;
    public bool HasOrbitShield => OrbitCount > 0;

    // Coin payout multiplier applied in GameManager.RegisterKill (Botín reward).
    public float CoinMultiplier => 1f + CoinBonusPercent / 100f;

    // XP payout multiplier (Sabiduría reward). Applied to XP only — see RegisterKill for why Score
    // deliberately stays on the unmultiplied value.
    public float XpMultiplier => 1f + XpBonusPercent / 100f;

    public UltimateKind? EquippedUltimate = null;

    // Time-based cooldown, not a Score-driven charge meter — usable immediately on first pickup
    // (starts at 0, i.e. ready), then a flat wait after every use.
    //
    // Deliberately FLAT at 10s now. It used to shrink with RoundNumber down to a 3.5s floor by round 11,
    // and combined with the cooldown running *concurrently* with the effect (see TriggerUltimate) that
    // gave an 80% uptime on a 2.8s slow/damage-doubling — an Ultimate that's up four fifths of the time
    // stops being a panic button and just becomes the player's baseline state.
    public float UltimateCooldownRemaining { get; private set; } = 0f;
    public float UltimateCooldownDuration => UltimateCooldownCurve.Evaluate(GameManager.Instance?.RoundNumber ?? 1);
    private static readonly RoundCurve UltimateCooldownCurve = new(10f, 0f, 10f, 10f);

    public event Action<int, int> LivesChanged;
    public event Action<int, int> ShieldChanged;

    // Level → (fire interval, damage, max targets per zap). Index 0 = tier 1 (Common) ... index 3
    // = tier 4 (Legendary). Only tier 1 caps how many enemies a single zap can hit — higher tiers
    // are strong enough already without also needing a target-count leash.
    private static readonly (float Interval, float Damage, int MaxTargets)[] LaserTiers =
    {
        (4.0f, 12f, 5),
        (3.2f, 20f, int.MaxValue),
        (2.5f, 31f, int.MaxValue),
        (1.8f, 50f, int.MaxValue),
    };

    // Level → (fire interval, blast damage, blast radius). Deliberately slow: the missile trades
    // uptime for burst area damage, so tier 1 fires only every 4.5s.
    private static readonly (float Interval, int Damage, float Radius)[] MissileTiers =
    {
        (4.5f, 30, 70f),
        (3.8f, 50, 85f),
        (3.2f, 75, 100f),
        (2.6f, 110, 120f),
    };

    // Level → (drop interval, blast damage, blast radius). Mina drops at the player's own position
    // on a slow timer rather than being aimed — no "nothing in range" bail like Missile needs, it
    // always fires once the cooldown clears (see the _PhysicsProcess block below). Costs/cadence
    // mirror MissileTiers exactly: same class of AoE weapon, traded aim for placement instead.
    private static readonly (float Interval, int Damage, float Radius)[] MineTiers =
    {
        (5.5f, 45, 55f),
        (4.6f, 70, 65f),
        (3.8f, 100, 75f),
        (3.0f, 140, 85f),
    };

    // Level → (fire interval, damage, blast radius). Onda de Choque detonates in place around the
    // player rather than traveling, so it needs no aim and hits everything adjacent for free —
    // deliberately weaker per-tick than Missile and capped at a small radius (well under FireRange)
    // to stay a close-range panic button, not a second Missile.
    // Radii were originally 45/55/65/75, deliberately kept well under Missile's on the grounds that Onda
    // costs nothing to aim. In play that made it useless against exactly the threat it should answer: a
    // Speedy at 205 base speed covers ~315 px/s by round 10, so a 45px ring caught it only after it had
    // already closed to contact range and landed its hit. These are ~45% wider — still short enough to be
    // a close-range panic button rather than a second Missile, but wide enough to catch a rusher on the
    // way in.
    //
    // What makes the extra reach actually pay off is TriggerOnda holding the cooldown at 0 while nothing
    // is in range: the pulse is always armed and waiting, so a wider ring means it fires *earlier* on the
    // approach rather than just covering more ground at the same moment.
    private static readonly (float Interval, int Damage, float Radius)[] OndaTiers =
    {
        (6.0f, 20, 65f),
        (5.0f, 35, 78f),
        (4.0f, 55, 92f),
        (3.2f, 80, 105f),
    };

    // Level → (fire interval, damage, range, half-angle in degrees, knockback force). Vendaval is
    // shop-exclusive and only ever offered at Epic/Legendary (see UpgradeData), so it only needs 2
    // tiers. A directional cone in front of the player (not a full ring like Onda de Choque) that
    // hits hard AND shoves survivors out of the cone — the "clears you a path" effect the reward
    // is built around, so the knockback matters as much as the damage.
    private static readonly (float Interval, int Damage, float Range, float HalfAngleDegrees, float Knockback)[] VendavalTiers =
    {
        (4.5f, 90, 260f, 32f, 380f),
        (3.6f, 140, 320f, 36f, 460f),
    };

    // Level → (damage per second, duration). Incendiario is a modifier applied by every source of
    // player damage — basic shots, the Laser, Orbit Blades, and Missiles all read these two
    // properties rather than each keeping their own copy of the tier lookup.
    public float CurrentBurnDps => BurnLevel > 0 ? BurnTiers[BurnLevel - 1].Dps * GetClassBurnMultiplier() : 0f;
    public float CurrentBurnDuration => BurnLevel > 0 ? BurnTiers[BurnLevel - 1].Duration + GetClassBurnDurationBonus() : 0f;

    private static readonly (float Dps, float Duration)[] BurnTiers =
    {
        (6f, 3.0f),
        (10f, 3.0f),
        (16f, 3.5f),
        (24f, 4.0f),
    };

    private VirtualJoystick _joystick;
    private Timer _fireCooldown;
    private Node2D _bulletsContainer;
    private readonly List<OrbitShield> _orbitBlades = new();
    private Companion _companion;
    private Companion _companion2; // Legendary Drone only — see EnsureCompanion
    public bool HasSecondCompanion => _companion2 != null;

    // "COMBATE"/"DEFENSAS"/"PODERES" — the full build readout, shared by PauseMenu (which prepends its
    // own "ESTADO" section, round-in-progress info that doesn't apply once a run has ended) and
    // GameOverStatsMenu (which shows this alone — the round already ended, GameOverScreen's own
    // summary already covers what round/score/kills were reached). One source so the two screens'
    // wording can't drift apart.
    // runEnded: true from GameOverStatsMenu, false (the default) from PauseMenu. The DEFENSAS line
    // otherwise reads CurrentLives/CurrentShieldCharges — meaningful mid-run, but by the time a death
    // screen shows this, both have already hit 0 (that's what death means), so a Game Over recap
    // always reads "0 corazones" regardless of how well-defended the run actually was. runEnded shows
    // the build totals instead, the same values LoadoutMenu.cs already reads for its own recap.
    public List<string> BuildCombatStatsLines(bool runEnded = false)
    {
        var lines = new List<string>
        {
            "── COMBATE ──",
            $"{Glossary.Damage}: {BulletDamage}   {Glossary.FireRate}: {FireRate:0.0}/s   {Glossary.Crit}: {CritChance:0}%",
            $"{Glossary.Range}: {FireRange:0}   {Glossary.Pierce}: {BulletPierce}   Rebote: {RicochetCount}",
            $"Retroceso: {BulletKnockback:0}   {Glossary.Dodge}: {DodgeChance:0}%",
            $"Disparo en Diagonal: {(HasExtraProjectile ? "Sí" : "No")}   Disparo Paralelo: {ExtraFiringLines}/{MaxExtraFiringLinesCap}",
            $"Cuchillas Orbitales: {OrbitCount}   Escudo Voltáico: {(ThornsDamage > 0 ? $"{ThornsDamage:0} daño" : "No")}",
            "",
            "── DEFENSAS ──",
            runEnded
                ? $"Vidas (máx): {MaxLives}   Escudos (máx): {MaxShieldCharges}"
                : $"Vidas: {CurrentLives}/{MaxLives}   Escudos: {CurrentShieldCharges}/{MaxShieldCharges}",
            $"Regeneración: {(ShieldRegenPerMinute > 0 ? $"{ShieldRegenPerMinute:0.#}/min" : "No")}",
            "",
            "── PODERES ──",
        };

        string companionSuffix = HasSecondCompanion ? " (x2)" : "";
        lines.Add($"Dron: {(CompanionStatPercent > 0 ? $"{CompanionStatPercent * 100:0}%{companionSuffix}" : "No")}");

        // "Nv" here too — this block used to be the one place in the game that said "Lv", three
        // lines below its own "Nv 5" in the status section above. Mina was also simply missing:
        // the loadout panel and the HUD both showed it, this didn't.
        if (LaserLevel > 0) lines.Add($"Láser: {Glossary.LevelPrefix}{LaserLevel}");
        if (MissileLevel > 0) lines.Add($"Misil: {Glossary.LevelPrefix}{MissileLevel}");
        if (MineLevel > 0) lines.Add($"Mina: {Glossary.LevelPrefix}{MineLevel}");
        if (BurnLevel > 0) lines.Add($"Incendiario: {Glossary.LevelPrefix}{BurnLevel}");
        if (OndaLevel > 0) lines.Add($"Onda de Choque: {Glossary.LevelPrefix}{OndaLevel}");
        if (VendavalLevel > 0) lines.Add($"Vendaval: {Glossary.LevelPrefix}{VendavalLevel}");
        if (EquippedUltimate != null) lines.Add($"Ultimate: {UltimateKindNames.Display(EquippedUltimate.Value)}");

        return lines;
    }

    private Polygon2D _shieldAura;
    private bool _hasExtraProjectile = false;
    private float _invulnTimer = 0f;
    // True for the invuln window opened by a hit a shield charge absorbed — blinks the shield aura
    // instead of the ship itself, so a hit that cost you a charge doesn't read identically to one
    // that cost you a life.
    private bool _blinkShieldInstead = false;
    private const float InvulnDuration = 2f;
    private float _blinkTimer = 0f;
    private const float BlinkInterval = 0.1f;
    private Sprite2D _visual;

    // The ship's on-screen size lives in the Visual's own Scale rather than in vertex coordinates
    // the way it did when Visual was a Polygon2D. The breathe loop below therefore has to return
    // here, not to Vector2.One — doing the latter blew the ship up to its texture's full resolution
    // within the first second of every run.
    private Vector2 _visualBaseScale = Vector2.One;
    private Sprite2D _shadow;
    private Sprite2D _outline;

    // Same halo-behind-a-flat-shape it looks like from a distance, scaled up slightly rather than
    // outlined per-pixel — cheap, and consistent with how icon.svg/BootSplash fake a glow.
    private const float OutlineScaleMultiplier = 1.12f;
    private const float OutlineAlpha = 0.6f;
    private Tween _breatheTween;
    private Tween _launchPunchTween;
    private bool _wasInputMoving = false;

    // Extra coin-pickup drop chance granted by the selected character (Manu), read by
    // Enemy.TryDropPickup off the player node. Lives here rather than on GameManager for the same
    // reason MoveSpeed/BulletDamage do: the character's effect on a run is applied in one place.
    public float CoinDropBonus;

    // The rest of the selected character's effects on enemies, re-exposed for Enemy/EnemySpawner to
    // read off the player node so neither has to know CharacterCatalog exists.
    public float EnemyHpMultiplier = 1f;
    public Color? EnemyTint;
    public float EnemyHackChance;
    public float EnemySuicideChance;

    // How wide the ship draws, in world px, whatever the selected character's texture resolution is
    // (_Ready divides by the texture's longest side to get there). Matches the 36px the original
    // Polygon2D triangle spanned, which is roughly the 16px-radius collision circle — the visual
    // and the hitbox agreeing is what keeps a near miss reading as a near miss.
    private const float CharacterSpriteWorldSize = 36f;
    private Vector2 _knockbackVelocity = Vector2.Zero;
    private const float KnockbackForce = 220f;
    private const float KnockbackDecay = 900f;

    // How fast the Visual triangle turns to face the movement direction, as an exponential
    // catch-up rate (bigger = snappier). Kept separate from MoveAcceleration/Deceleration below —
    // rotation and translation reading as loosely coupled, rather than locked in lockstep, is what
    // makes the turn look like banking instead of the whole ship instantly snapping to face a
    // direction change.
    private const float FacingTurnRate = 16f;
    private bool _isCircularCharacter;

    // A lean on top of the rotation above — driven by how much turn is still left to complete, so a
    // small course correction barely tilts it while a sharp reversal banks hard and eases out
    // smoothly as the turn finishes. Kept on Sprite2D.Skew rather than Scale so it can't fight the
    // idle "breathe" tween in StartIdleAnimations, which already owns Scale. Capped small (~13°) and
    // eased fast in both directions — enough to read as motion, not a wobble.
    private float _visualBankSkew = 0f;
    private const float BankSkewPerRadRemaining = 0.3f;
    private const float MaxBankSkew = 0.22f;
    private const float BankSmoothingRate = 10f;

    // Movement ramps in and out instead of snapping between full speed and a dead stop, so the
    // player carries real inertia. Both rates are px/s²: at MoveSpeed 290 that's ~0.48s to reach
    // top speed and ~0.39s to stop. Went through two rounds of softening from an original 1800/2400
    // (~0.16s/0.12s, effectively an instant velocity flip) — the first pass to 1300/1700
    // (~0.22s/0.17s) still read as no inertia at all, so this jumps much further rather than
    // nudging again. The stop still stays quicker than the start so the ship doesn't feel like
    // it's sliding on ice, just genuinely carrying momentum instead of snapping to a stop.
    private Vector2 _moveVelocity = Vector2.Zero;
    private const float MoveAcceleration = 600f;
    private const float MoveDeceleration = 750f;

    // Per-type accumulated bonus for FireRange/FireRate/BulletDamage — every pick adds its Value,
    // any tier, same as Side Shot's ExtraFiringLines. Used to bucket by RewardTier and take only the
    // highest bucket (a Common pick contributed nothing once you'd found a Legendary); that's gone
    // for these three specifically — the flat per-stat cap applied where this is read
    // (MaxFireRangeBonus/MaxFireRate/MaxBulletDamageBonus) is what bounds the total now, same as it
    // already bounds Side Shot. BulletKnockback still wants the old tier-bucketing (see
    // _tierStackTotals below) — it wasn't part of this change, so it keeps its own dictionary and
    // helper methods rather than sharing this one.
    private readonly Dictionary<UpgradeType, float> _stackTotals = new();

    // BulletKnockback's own per-tier bucket totals — same shape ApplyTieredStack/PreviewTieredBonus
    // implemented for all four stats before FireRange/FireRate/BulletDamage moved to the flat
    // accumulator above. Same tier stacks additively; different tiers don't sum on top of each
    // other, the final bonus is whichever single tier-bucket is currently highest.
    private readonly Dictionary<UpgradeType, Dictionary<RewardTier, float>> _tierStackTotals = new();

    private float _baseFireRate;
    private float _baseBulletDamage;
    private float _baseFireRange;
    private float _baseMoveSpeed;
    private Timer _shieldRegenTimer;
    private Line2D _fireRangeRing;
    private Timer _frenzyTimer;

    // Read by HUD's cooldown icons. 1 = just fired at an enemy (icon fully covered), decaying
    // to 0 and HOLDING there. Laser/Missile aren't driven by a repeating Timer at all (see
    // TryFireLaser/TryFireMissile, polled every physics frame instead) — a Timer fires on its own
    // schedule regardless of whether anything is in range, which both made the icon flicker
    // uncovered for a single frame on every empty retry, AND meant an enemy walking into range
    // right after an empty tick had to wait out a whole extra interval before actually firing.
    public float LaserCooldownFraction { get; private set; }
    public float MissileCooldownFraction { get; private set; }
    public float MineCooldownFraction { get; private set; }
    public float OndaCooldownFraction { get; private set; }
    public float VendavalCooldownFraction { get; private set; }

    public float ShieldRegenCooldownFraction =>
        _shieldRegenTimer == null || _shieldRegenTimer.IsStopped() || _shieldRegenTimer.WaitTime <= 0f
            ? 0f : (float)(_shieldRegenTimer.TimeLeft / _shieldRegenTimer.WaitTime);

    public override void _Ready()
    {
        AddToGroup("player");

        // Applied before anything below snapshots or consumes these fields, so the chosen character
        // shifts the run's whole stat curve rather than being overwritten by it.
        var character = CharacterCatalog.Get(GameManager.Instance.SelectedCharacter);
        _isCircularCharacter = character.IsCircular;
        MoveSpeed *= character.MoveSpeedMultiplier;
        BulletDamage = Mathf.RoundToInt(BulletDamage * character.BulletDamageMultiplier);
        CoinDropBonus = character.CoinDropBonus;
        EnemyTint = character.EnemyTint;
        EnemyHackChance = character.EnemyHackChance;
        EnemySuicideChance = character.EnemySuicideChance;

        // Guarded rather than assigned straight across: CharacterInfo is a struct, so an entry that
        // simply doesn't set this arrives as 0 — which would delete every enemy's health instead of
        // leaving it alone. A real 0.9 still gets through.
        EnemyHpMultiplier = character.EnemyHpMultiplier > 0f ? character.EnemyHpMultiplier : 1f;

        // Hardcore: always exactly 1 heart, no matter the scene default or any character perk. The
        // Heart reward that would normally raise this never appears in the first place (see
        // UpgradeData.BuildCatalog), so nothing downstream can push this back up mid-run.
        if (GameManager.Instance?.CurrentGameMode == GameManager.GameMode.Hardcore)
            MaxLives = 1;

        CurrentLives = MaxLives;

        _baseFireRate = FireRate;
        _baseBulletDamage = BulletDamage;
        _baseFireRange = FireRange;
        _baseMoveSpeed = MoveSpeed;

        _fireCooldown = GetNode<Timer>("FireCooldown");
        _fireCooldown.WaitTime = 1f / FireRate;
        _fireCooldown.Timeout += OnFireCooldownTimeout;
        _fireCooldown.Start();

        _bulletsContainer = GetTree().CurrentScene.GetNode<Node2D>("Bullets");

        var hurtbox = GetNode<Area2D>("Hurtbox");
        hurtbox.BodyEntered += OnHurtboxBodyEntered;

        _joystick = GetTree().GetFirstNodeInGroup("virtual_joystick") as VirtualJoystick;

        _visual = GetNode<Sprite2D>("Visual");
        _visual.Texture = CharacterCatalog.Texture(character);
        _visual.Modulate = character.Color;

        // Scale is derived from the texture rather than taken from the scene, because the texture is
        // swapped per character and the scene's own value can only ever be right for one of them.
        // Leaving it fixed meant any sprite authored at a different resolution rendered at the wrong
        // size — a 1024px photo would come out roughly seven times the ship's size. Deriving it here
        // means a portrait can be dropped in at whatever resolution it happens to be and still
        // occupy exactly CharacterSpriteWorldSize on screen, matching the ship and the hurtbox.
        var texture = _visual.Texture;
        if (texture != null)
        {
            float longestSide = Mathf.Max(texture.GetWidth(), texture.GetHeight());
            if (longestSide > 0f)
                _visual.Scale = Vector2.One * SnapToPixelGrid(CharacterSpriteWorldSize / longestSide);
        }

        // Snapshotted after the scale is settled — the breathe loop returns to this value, so
        // capturing the scene's placeholder instead would undo the line above on the first tween.
        _visualBaseScale = _visual.Scale;

        // Hidden unless the player has actually equipped a border color — with Original equipped
        // (the default for everyone who's never opened the shop) the game looks exactly as it did
        // before this cosmetic existed. A flat-shape halo behind the same texture, not a real edge
        // outline — same trick as icon.svg's glow layer, and it works on any portrait, not just ship.png.
        _outline = GetNode<Sprite2D>("Outline");
        _outline.Texture = _visual.Texture;
        _outline.Scale = _visualBaseScale * OutlineScaleMultiplier;
        string outlineCosmetic = GameManager.Instance.EquippedCosmetic(CosmeticCategory.Outline);
        _outline.Visible = outlineCosmetic != CosmeticCatalog.DefaultId;
        if (_outline.Visible)
        {
            // The base colour passed in only carries the alpha here — the outline is hidden whenever
            // "Original" is equipped, so the branch Resolve would take for it is unreachable.
            Juice.ApplyCosmetic(_outline, "modulate", CosmeticCategory.Outline,
                new Color(0f, 0f, 0f, OutlineAlpha));
        }

        // Attached to the player root, NOT to Visual. As a child of Visual it would inherit the
        // facing rotation, which would swing the shadow around the ship as it turned and destroy the
        // fixed-light illusion the whole effect rests on. Kept as a sibling, its offset stays put and
        // UpdateFacing copies just the rotation across — silhouette turns, light doesn't.
        _shadow = Juice.AttachShadow(this, _visual, _visualBaseScale);

        _shieldAura = GetNode<Polygon2D>("ShieldAura");
        // CosmeticCatalog.ShieldAuraBase mirrors the colour Player.tscn sets on this node — see the
        // note there. Resolving against it keeps the aura's 0.35 alpha whichever colour is equipped.
        Juice.ApplyCosmetic(_shieldAura, "color", CosmeticCategory.Shield, CosmeticCatalog.ShieldAuraBase);

        // Skipped under reduced motion — the aura's mere presence already says "you have a shield
        // charge"; the pulse is ambience on top of that, and it sits directly under the player's
        // eyes for the whole run.
        if (!DangerLevel.Reduced)
        {
            var pulse = CreateTween();
            pulse.SetLoops();
            pulse.TweenProperty(_shieldAura, "scale", Vector2.One * 1.1f, 0.6f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            pulse.TweenProperty(_shieldAura, "scale", Vector2.One, 0.6f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        SpawnFireRangeIndicator();
        StartIdleAnimations();
    }

    // Looping ambient motion so the ship and its range ring never sit perfectly static. Both run on
    // the Visual/ring nodes rather than the player itself, so they can't interfere with movement,
    // the invuln blink (which toggles _visual.Visible, not its scale), or the arena clamp.
    private void StartIdleAnimations()
    {
        // Purely ambient — nothing here encodes state, so reduced motion drops both loops outright
        // rather than substituting anything. The ship simply sits still.
        if (DangerLevel.Reduced) return;

        _breatheTween = CreateTween();
        _breatheTween.SetLoops();
        _breatheTween.TweenProperty(_visual, "scale", _visualBaseScale * 1.06f, 0.9f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _breatheTween.TweenProperty(_visual, "scale", _visualBaseScale, 0.9f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        // The ring slowly counter-rotates — a static circle reads as UI, a drifting one as a field.
        var spin = CreateTween();
        spin.SetLoops();
        spin.TweenProperty(_fireRangeRing, "rotation", Mathf.Tau, 24f);
        spin.TweenCallback(Callable.From(() => _fireRangeRing.Rotation = 0f));
    }

    // A quick squash-then-stretch the instant the ship goes from a standstill to moving — sells the
    // impulse itself, on top of the velocity ramp already giving it inertia. Elongating local X
    // (not a screen axis) reads as "surging forward" for the triangle ships, since UpdateFacing
    // already keeps X pointed the way it's traveling; for a circular portrait it's just a punch
    // with no particular direction, which still reads fine since there's no "facing" to align to.
    //
    // Rounds a sprite scale to something that keeps texels whole: a whole number when scaling up, and
    // one-over-a-whole-number when scaling down.
    //
    // This matters because the ship is the one entity whose scale is computed rather than authored.
    // A 19-texel ship asked to fill 36px wants 1.894x, and at a fraction like that the rasteriser
    // rounds each block unevenly -- some 1px, some 2px -- which is the exact softness the pixel-art
    // pass removed everywhere else. Snapping to 2x renders it at 38px instead of 36; the hurtbox is a
    // separate CollisionShape2D, so that 2px is cosmetic.
    //
    // Photo portraits go the other way: 36/144 is already exactly 1/4, and the reciprocal branch keeps
    // it there rather than rounding it up to 1x and blowing a 144px face up to four times the ship.
    private static float SnapToPixelGrid(float scale)
    {
        if (scale <= 0f) return 1f;
        return scale >= 1f
            ? Mathf.Max(1f, Mathf.Round(scale))
            : 1f / Mathf.Max(1f, Mathf.Round(1f / scale));
    }

    // Pauses (not kills) _breatheTween for the duration rather than letting both drive Scale at
    // once — two tweens racing the same property every frame is exactly the jitter the bank-lean
    // fix earlier had to work around, and pausing is free since Godot's Tween supports it natively.
    private void PlayLaunchPunch()
    {
        if (DangerLevel.Reduced) return;

        _launchPunchTween?.Kill();
        _breatheTween?.Pause();
        _visual.Scale = _visualBaseScale;

        _launchPunchTween = _visual.CreateTween();
        _launchPunchTween.TweenProperty(_visual, "scale", _visualBaseScale * new Vector2(1.35f, 0.75f), 0.08f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _launchPunchTween.TweenProperty(_visual, "scale", _visualBaseScale, 0.22f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _launchPunchTween.TweenCallback(Callable.From(() => _breatheTween?.Play()));
    }

    // A faint ring showing FireRange — the player only auto-fires at enemies inside it, so it
    // needs to be visible rather than an invisible gameplay number. Rebuilt whenever the Fire
    // Range reward raises FireRange, since a Line2D's points are baked at the radius they were
    // drawn at, not tied live to the FireRange field.
    private void SpawnFireRangeIndicator()
    {
        _fireRangeRing = new Line2D();
        _fireRangeRing.Width = 2f;
        // Bullet, not a slot of its own: the ring exists to show how far your shots reach, so it
        // should always match them. Resolving against Palette.FireRangeRing keeps its 0.3 alpha, which
        // is what stops it competing with the bullets themselves.
        Juice.ApplyCosmetic(_fireRangeRing, "default_color", CosmeticCategory.Bullet, Palette.FireRangeRing);
        _fireRangeRing.ZIndex = -1;
        AddChild(_fireRangeRing);
        RebuildFireRangeIndicator();
    }

    private void RebuildFireRangeIndicator()
    {
        _fireRangeRing.ClearPoints();
        var ring = Juice.CirclePoints(EffectiveFireRange);
        foreach (var point in ring) _fireRangeRing.AddPoint(point);
        _fireRangeRing.AddPoint(ring[0]);   // close the loop
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_joystick == null)
            _joystick = GetTree().GetFirstNodeInGroup("virtual_joystick") as VirtualJoystick;

        Vector2 dir = _joystick?.GetVector() ?? Vector2.Zero;
        if (dir == Vector2.Zero)
            dir = GetKeyboardDirection();

        // Fires exactly once per genuine 0 -> moving transition, not every frame input is held — a
        // continuous stick hold shouldn't keep re-punching the ship every physics tick.
        bool isInputMoving = dir != Vector2.Zero;
        if (isInputMoving && !_wasInputMoving)
            PlayLaunchPunch();
        _wasInputMoving = isInputMoving;

        // Ease toward the requested velocity rather than assigning it outright — this is what gives
        // the movement its arranque/frenada. Steering mid-move interpolates through the turn too,
        // which reads as the ship banking rather than pivoting on the spot.
        Vector2 targetVelocity = dir * MoveSpeed * GetClassSpeedMultiplier();
        float rate = dir == Vector2.Zero ? MoveDeceleration : MoveAcceleration;
        _moveVelocity = _moveVelocity.MoveToward(targetVelocity, rate * (float)delta);

        _knockbackVelocity = _knockbackVelocity.MoveToward(Vector2.Zero, KnockbackDecay * (float)delta);

        // Laser/Missile fire from here directly (no repeating Timer) — see the property comments
        // on LaserCooldownFraction above for why. Decay first so a tick that just cleared the
        // cooldown can also fire immediately in the same frame, rather than firing next frame.
        if (LaserLevel > 0)
        {
            if (LaserCooldownFraction > 0f)
                LaserCooldownFraction = Mathf.Max(0f, LaserCooldownFraction - (float)delta / GetLaserInterval());
            if (LaserCooldownFraction <= 0f)
                TryFireLaser();
        }
        if (MissileLevel > 0)
        {
            if (MissileCooldownFraction > 0f)
                MissileCooldownFraction = Mathf.Max(0f, MissileCooldownFraction - (float)delta / MissileTiers[MissileLevel - 1].Interval);
            if (MissileCooldownFraction <= 0f)
                TryFireMissile();
        }

        // Unlike Missile, dropping a mine doesn't need a target in range — it's placed at the
        // player's own position regardless, so (like Onda/Vendaval) it always fires once the
        // cooldown clears.
        if (MineLevel > 0)
        {
            if (MineCooldownFraction > 0f)
                MineCooldownFraction = Mathf.Max(0f, MineCooldownFraction - (float)delta / MineTiers[MineLevel - 1].Interval);
            if (MineCooldownFraction <= 0f)
                DropMine();
        }

        // Onda de Choque/Vendaval are always centered on/in front of the player, so unlike
        // Laser/Missile there's no "nothing in range" case to bail on — they simply always fire
        // once the cooldown clears.
        if (OndaLevel > 0)
        {
            if (OndaCooldownFraction > 0f)
                OndaCooldownFraction = Mathf.Max(0f, OndaCooldownFraction - (float)delta / OndaTiers[OndaLevel - 1].Interval);
            if (OndaCooldownFraction <= 0f)
                TriggerOnda();
        }

        if (VendavalLevel > 0)
        {
            if (VendavalCooldownFraction > 0f)
                VendavalCooldownFraction = Mathf.Max(0f, VendavalCooldownFraction - (float)delta / VendavalTiers[VendavalLevel - 1].Interval);
            if (VendavalCooldownFraction <= 0f)
                TriggerVendaval();
        }

        if (UltimateCooldownRemaining > 0f)
            UltimateCooldownRemaining = Mathf.Max(0f, UltimateCooldownRemaining - (float)delta);

        Velocity = _moveVelocity + _knockbackVelocity;
        MoveAndSlide();

        UpdateFacing(dir, (float)delta);
        UpdateThruster((float)delta);

        GlobalPosition = new Vector2(
            Mathf.Clamp(GlobalPosition.X, -ArenaHalfExtents.X, ArenaHalfExtents.X),
            Mathf.Clamp(GlobalPosition.Y, -ArenaHalfExtents.Y, ArenaHalfExtents.Y)
        );

        if (_invulnTimer > 0f)
        {
            _invulnTimer -= (float)delta;
            _blinkTimer += (float)delta;
            bool blinkOn = ((int)(_blinkTimer / BlinkInterval)) % 2 == 0;

            if (_blinkShieldInstead)
                _shieldAura.Visible = blinkOn && CurrentShieldCharges > 0;
            else
                _visual.Visible = blinkOn;

            if (_invulnTimer <= 0f)
            {
                _visual.Visible = true;
                _shieldAura.Visible = CurrentShieldCharges > 0;
            }
        }

        if (_thornsCooldown > 0f)
            _thornsCooldown -= (float)delta;

        if (_killStreakTimer > 0f)
        {
            _killStreakTimer -= (float)delta;
            if (_killStreakTimer <= 0f)
                _killStreak = 0;
        }
    }

    // Turns the triangle to face the direction the player is actually moving in — it used to be
    // rigidly fixed pointing right regardless of input. Prefers raw input direction (the player's
    // immediate intent) and falls back to the smoothed velocity while decelerating with no input
    // held, so the ship keeps facing forward as it coasts to a stop rather than snapping to face
    // whatever residual coast direction. Holds its last facing entirely when both are ~zero,
    // instead of resetting to some default — a stationary ship has no "forward" to snap back to.
    //
    // Skipped entirely for circular portraits: a circle reads identically at every rotation, so
    // spinning one to "face" a direction is motion with no visible meaning.
    private void UpdateFacing(Vector2 inputDir, float delta)
    {
        if (_isCircularCharacter) return;
        Vector2 facing = inputDir != Vector2.Zero ? inputDir
            : (_moveVelocity.LengthSquared() > 25f ? _moveVelocity : Vector2.Zero);

        float targetSkew = 0f;
        if (facing != Vector2.Zero)
        {
            // How much turn is still left to complete, not how fast the rotation is currently
            // changing — the first version of this used the rotation's frame-to-frame delta, which
            // (being the derivative of an already-fast exponential catch-up) saturates to the max
            // lean on almost any input nudge and then snaps back a few frames later, reading as a
            // jittery pulse rather than a lean. The remaining angle is naturally bounded to
            // [-pi, pi] and shrinks smoothly toward 0 as the rotation lerp below catches up to it,
            // so the lean eases out exactly as the turn finishes.
            float angleRemaining = Mathf.Wrap(facing.Angle() - _visual.Rotation, -Mathf.Pi, Mathf.Pi);
            targetSkew = Mathf.Clamp(-angleRemaining * BankSkewPerRadRemaining, -MaxBankSkew, MaxBankSkew);

            // Exponential catch-up rather than an instant snap, so a sharp direction change reads as
            // the triangle banking through the turn instead of teleporting to face the new heading.
            float weight = 1f - Mathf.Exp(-FacingTurnRate * delta);
            _visual.Rotation = Mathf.LerpAngle(_visual.Rotation, facing.Angle(), weight);
        }

        // Eases toward 0 the same way whether the ship is turning, holding a straight line, or
        // stopped — no separate idle case needed to un-bank it.
        _visualBankSkew = Mathf.Lerp(_visualBankSkew, targetSkew, 1f - Mathf.Exp(-BankSmoothingRate * delta));
        _visual.Skew = _visualBankSkew;

        // Rotation and lean only — never Position. The shadow's offset is what encodes the light
        // direction, so it has to stay fixed while the silhouette above it turns.
        if (_shadow != null)
        {
            _shadow.Rotation = _visual.Rotation;
            _shadow.Skew = _visual.Skew;
        }

        // Same sibling-not-child reasoning as the shadow above: outline follows facing without
        // inheriting it structurally. Position never needs copying — it sits at the origin either way.
        if (_outline != null)
        {
            _outline.Rotation = _visual.Rotation;
            _outline.Skew = _visual.Skew;
        }
    }

    // A trail of small fading puffs behind the ship while it's actually moving, not a constant
    // engine glow — the point is to sell *motion*, so it starts/stops with the ship rather than
    // running whenever the player merely holds a direction against a wall. Purely decorative (a
    // Polygon2D circle, same fade-out-then-QueueFree pattern as Enemy/Mine's death blasts), tinted
    // to the ship's own Modulate so it reads as its own exhaust rather than a generic effect.
    private float _thrusterAccumulator = 0f;
    private const float ThrusterInterval = 0.05f;
    private const float ThrusterMinSpeedRatio = 0.35f;
    private const float ThrusterPuffOffset = 14f;

    // Legendary Movement Speed's Tron trail — a much longer interval than the cosmetic thruster
    // puff above, since each segment is a real Area2D with its own damage tick (SpeedTrailSegment.cs)
    // rather than a throwaway Polygon2D; spawning one every 0.05s the way the puff does would leave
    // dozens alive at once for no gameplay benefit.
    private float _speedTrailAccumulator = 0f;
    private const float SpeedTrailInterval = 0.12f;

    private void UpdateThruster(float delta)
    {
        float topSpeed = MoveSpeed * GetClassSpeedMultiplier();
        float speedRatio = topSpeed > 0f ? _moveVelocity.Length() / topSpeed : 0f;

        if (speedRatio < ThrusterMinSpeedRatio || DangerLevel.Reduced)
        {
            _thrusterAccumulator = 0f;
            _speedTrailAccumulator = 0f;
            return;
        }

        _thrusterAccumulator += delta;
        if (_thrusterAccumulator >= ThrusterInterval)
        {
            _thrusterAccumulator = 0f;
            SpawnThrusterPuff(speedRatio);
        }

        if (HasLegendarySpeedTrail())
        {
            _speedTrailAccumulator += delta;
            if (_speedTrailAccumulator >= SpeedTrailInterval)
            {
                _speedTrailAccumulator = 0f;
                SpawnSpeedTrailSegment();
            }
        }
        else
        {
            _speedTrailAccumulator = 0f;
        }
    }

    // Checked live off the best-tier-ever-owned map rather than a cached flag set once at pick time —
    // same gate Laser uses for its own tier-3+ behavior switch (LaserLevel >= 3).
    private bool HasLegendarySpeedTrail() =>
        _ownedTiers.TryGetValue(UpgradeType.MovementSpeed, out var tier) && tier == RewardTier.Legendary;

    private void SpawnSpeedTrailSegment()
    {
        if (SpeedTrailSegmentScene == null) return;
        var parent = GetParent();
        if (parent == null) return;

        var segment = SpeedTrailSegmentScene.Instantiate<SpeedTrailSegment>();
        segment.GlobalPosition = GlobalPosition;
        parent.AddChild(segment);
        segment.Launch(this);
    }

    private void SpawnThrusterPuff(float speedRatio)
    {
        var parent = GetParent();
        if (parent == null) return;

        Vector2 backward = -_moveVelocity.Normalized();
        float size = Mathf.Lerp(3f, 6f, speedRatio);

        // One fixed color for every pilot, Original included — same green as the player's own
        // weapon family (Palette.PlayerBullet), so the trail reads as "exhaust" independent of
        // whichever ship color happens to be equipped. Used to copy _visual.Modulate (the ship's own
        // color) instead, which meant "Original" looked different per pilot and made the shop's
        // preview swatch depend on whoever was currently selected — confusing to compare against.
        var puff = new Polygon2D
        {
            Polygon = Juice.CirclePoints(size),
            GlobalPosition = GlobalPosition + backward * ThrusterPuffOffset,
            ZIndex = -1,
        };
        parent.AddChild(puff);

        // After AddChild for the same reason the bullets are: an animated Epico colour needs a tree
        // to tween in. The shimmer drives "color" while the fade below drives "modulate:a", so the
        // two run on the same node without fighting over a property.
        Juice.ApplyCosmetic(puff, "color", CosmeticCategory.Trail, Palette.PlayerBullet);

        var tween = puff.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(puff, "scale", Vector2.One * 0.2f, 0.35f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(puff, "modulate:a", 0f, 0.35f);
        tween.Chain().TweenCallback(Callable.From(puff.QueueFree));
    }

    // WASD as a desktop-friendly alternative to the virtual joystick — only consulted when the
    // joystick itself isn't providing input, so touch input always takes priority and the two
    // never fight over movement.
    private static Vector2 GetKeyboardDirection()
    {
        Vector2 dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W)) dir.Y -= 1f;
        if (Input.IsKeyPressed(Key.S)) dir.Y += 1f;
        if (Input.IsKeyPressed(Key.A)) dir.X -= 1f;
        if (Input.IsKeyPressed(Key.D)) dir.X += 1f;
        return dir.Normalized();
    }

    // R triggers the Ultimate — via _UnhandledInput (not polled every physics frame) so it fires
    // exactly once per key-down and is automatically ignored while the tree is paused (shop,
    // pause menu, game over), same as every other gameplay input.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.R)
            TriggerUltimate();
    }

    private void OnHurtboxBodyEntered(Node2D body)
    {
        if (body is Enemy enemy)
            TakeHit(enemy.GlobalPosition);
    }

    // forcedCost lets a caller spend a specific number of shield/life points on this hit instead of
    // the usual ComputeHitCost() roll — e.g. Boss.cs's round-10+ heavy attacks, which are meant to
    // cost 2 hearts outright rather than relying on the probabilistic survivability catch-up path.
    public void TakeHit(Vector2? sourcePosition = null, int forcedCost = 0)
    {
        if (_invulnTimer > 0f) return;

        // Dodge: chance to avoid the hit entirely
        if (DodgeChance > 0f && _dodgeRng.NextDouble() < DodgeChance / 100f)
        {
            // A dodge has no visual tell at all — no flash, no number, nothing. Until now the only
            // way to know Esquiva had done anything was to notice a hit that didn't cost you, which
            // is indistinguishable from never having been hit. This sound is the whole feedback.
            AudioManager.Instance?.Play(AudioManager.Sfx.Dodge);

            _invulnTimer = InvulnDuration * 0.5f; // shorter invuln on dodge
            return;
        }

        // Any hit that lands (shield or heart) resets the kill streak.
        ResetKillStreak();

        // Armored class: reflect 60 damage to nearby enemies on hit (no cooldown).
        if (IsClassActive(BuildClass.Armored) && sourcePosition.HasValue)
        {
            var enemies = GetTree().GetNodesInGroup("enemies");
            int hit = 0;
            foreach (var node in enemies)
            {
                if (node is Enemy e && !e.IsQueuedForDeletion())
                {
                    float dist = GlobalPosition.DistanceTo(e.GlobalPosition);
                    if (dist <= 100f)
                    {
                        e.TakeDamage(60);
                        hit++;
                        if (hit >= 10) break;
                    }
                }
            }
        }

        // Thorns: damage enemies when hit
        if (ThornsDamage > 0f && _thornsCooldown <= 0f)
        {
            _thornsCooldown = 8f;
            var enemies = GetTree().GetNodesInGroup("enemies");
            int hit = 0;
            foreach (var node in enemies)
            {
                if (node is Enemy e && !e.IsQueuedForDeletion())
                {
                    e.TakeDamage(Mathf.RoundToInt(ThornsDamage));
                    hit++;
                    if (hit >= 15) break;
                }
            }
        }

        // How many points of the shield/life pool this hit actually spends. Normally 1; a heavily
        // defensively-stacked build (see GetSurvivabilityScore) can have this read 2 once
        // DifficultyBalancer's survivability catch-up kicks in — the counter-pressure for a build
        // that dodge/Tank/shield have made nearly unkillable, since raising enemy damage can't touch
        // that (every hit in this game costs a flat life/shield charge, never a variable amount).
        int cost = forcedCost > 0 ? forcedCost : ComputeHitCost();
        bool shieldAbsorbed = false;
        bool lifeLost = false;
        for (int i = 0; i < cost; i++)
        {
            if (CurrentShieldCharges > 0)
            {
                // Deliberately a different sound from losing a life, not a quieter version of it.
                // "The shield ate that" and "that cost you a heart" are different facts about your
                // run, and the player is usually looking at the enemy, not at the HUD.
                AudioManager.Instance?.Play(AudioManager.Sfx.Shield);

                CurrentShieldCharges--;
                RaiseShieldChanged();
                _blinkShieldInstead = true;
                shieldAbsorbed = true;
            }
            else
            {
                // Tank class passive: 25% chance to shrug off this point for free. Rolled per point
                // rather than once per hit — a cost-2 hit is conceptually "hit twice", and two
                // separate TakeHit calls would already give Tank two independent rolls today, so
                // this isn't a bonus, it's the same rule applied consistently.
                if (IsClassActive(BuildClass.Tank) && _dodgeRng.NextDouble() < TankShrugChance)
                {
                    _blinkShieldInstead = false;
                    continue;
                }
                _blinkShieldInstead = false;
                LoseLife();
                lifeLost = true;
                if (CurrentLives <= 0) break;
            }
        }

        // Full-screen-edge flash so a hit registers even if the player isn't looking at the HUD or
        // listening for the sound cue — blue for a shield absorb, red for an actual life lost. A
        // cost-2 hit that spends both in the same call flashes twice; the red one (called second)
        // wins visually, which is fine since losing a life is the more important of the two facts.
        if (GetTree().GetFirstNodeInGroup("danger_overlay") is DangerOverlay overlay)
        {
            if (shieldAbsorbed) overlay.FlashDamage(Palette.ShieldPickupColor);
            if (lifeLost) overlay.FlashDamage(DangerLevel.AlarmBarColor);
        }

        // Invuln + knockback apply once per hit event, not per cost point — and now apply even if
        // every point got shrugged by Tank. That's a small, deliberate change from before (a
        // shrugged hit used to return early and skip both): something still physically touched the
        // player, so it should still knock them back and open the i-frame window, the same as a
        // dodge does not (a dodge means nothing touched you at all).
        _invulnTimer = InvulnDuration;
        _blinkTimer = 0f;

        if (sourcePosition.HasValue)
        {
            Vector2 away = (GlobalPosition - sourcePosition.Value);
            if (away != Vector2.Zero)
                _knockbackVelocity = away.Normalized() * KnockbackForce * GetClassKnockbackMultiplier();
        }
    }

    public void LoseLife()
    {
        CurrentLives--;
        LivesChanged?.Invoke(CurrentLives, MaxLives);

        if (CurrentLives <= 0)
        {
            GameManager.Instance?.NotifyPlayerDied();
            return;
        }

        // After the death check, so the last life lost plays the death sting instead of stacking the
        // hurt sound underneath it. Hooked here rather than on the LivesChanged event, which also
        // fires on heals and on AddLife.
        AudioManager.Instance?.Play(AudioManager.Sfx.PlayerHurt);
    }

    private void RaiseShieldChanged()
    {
        ShieldChanged?.Invoke(CurrentShieldCharges, MaxShieldCharges);
        _shieldAura.Visible = CurrentShieldCharges > 0;
    }

    public void HealFullLives()
    {
        CurrentLives = MaxLives;
        LivesChanged?.Invoke(CurrentLives, MaxLives);
    }

    // Call from GameManager.RegisterKill to maintain the kill streak counter.
    public void RegisterKillForStreak()
    {
        _killStreak++;
        _killStreakTimer = KillStreakWindow;
    }

    public void ResetKillStreak()
    {
        _killStreak = 0;
        _killStreakTimer = 0f;
    }

    // Build class: check stat thresholds and activate every build whose combination is met. Each
    // build needs a *combination* of rewards (some including shop items), so a single lucky pickup
    // can't unlock one — you have to commit to the stat spread the build is themed around.
    // Thresholds live in GetBuildRequirements so CheckBuildClass and the Loadout UI share them.
    public void CheckBuildClass()
    {
        TryActivate(BuildClass.Gunner);
        TryActivate(BuildClass.Tank);
        TryActivate(BuildClass.Assassin);
        TryActivate(BuildClass.Explorer);
        TryActivate(BuildClass.Pyromaniac);
        TryActivate(BuildClass.Armored);
        TryActivate(BuildClass.Hunter);
    }

    private void TryActivate(BuildClass cls)
    {
        if (_activeClasses.Contains(cls)) return;
        foreach (var req in GetBuildRequirements(cls))
            if (!req.IsMet(this)) return;
        _activeClasses.Add(cls);
        ApplyClassPassive(cls);
        GameManager.Instance?.NotifyBuildCompleted(cls);
    }

    // One-shot setup when a class activates (rather than every frame).
    private void ApplyClassPassive(BuildClass cls)
    {
        if (cls == BuildClass.Armored)
        {
            // Acorazado: free shield regen (1 charge / 12s) even without the shop item.
            if (ShieldRegenPerMinute < 5f)
            {
                ShieldRegenPerMinute = 5f;
                EnsureShieldRegenTimer();
            }
        }
    }

    // One entry per stat a build requires, shared by CheckBuildClass (who decides activation)
    // and the Loadout UI (who shows ✓/✗ progress and which catalog rewards would close the gap) —
    // one source of thresholds so the two can never drift apart again.
    public readonly record struct BuildRequirement(UpgradeType Type, string Label, Func<Player, float> Current, float Needed, bool Additive)
    {
        public bool IsMet(Player p) => Current(p) >= Needed;
    }

    public static BuildRequirement[] GetBuildRequirements(BuildClass cls) => cls switch
    {
        BuildClass.Gunner => new[]
        {
            new BuildRequirement(UpgradeType.FireRate, "Cadencia", p => p.FireRate, 5f, Additive: true),
            new BuildRequirement(UpgradeType.SideShot, "Disparo Paralelo", p => p.ExtraFiringLines, 1f, Additive: true),
        },
        BuildClass.Tank => new[]
        {
            new BuildRequirement(UpgradeType.Heart, "Vidas", p => p.MaxLives, 4f, Additive: true),
            new BuildRequirement(UpgradeType.HitShield, "Escudos", p => p.MaxShieldCharges, 3f, Additive: false),
            new BuildRequirement(UpgradeType.ShieldRegen, "Regeneración", p => p.ShieldRegenPerMinute, 5f, Additive: false),
        },
        BuildClass.Assassin => new[]
        {
            new BuildRequirement(UpgradeType.CritChance, "Crítico", p => p.CritChance, 25f, Additive: true),
            new BuildRequirement(UpgradeType.BulletDamage, "Daño", p => p.BulletDamage, 25f, Additive: true),
            new BuildRequirement(UpgradeType.Ricochet, "Rebote", p => p.RicochetCount, 2f, Additive: false),
        },
        BuildClass.Explorer => new[]
        {
            new BuildRequirement(UpgradeType.FireRange, "Alcance", p => p.FireRange, 350f, Additive: true),
            new BuildRequirement(UpgradeType.Pierce, "Perforación", p => p.BulletPierce, 2f, Additive: true),
        },
        BuildClass.Pyromaniac => new[]
        {
            new BuildRequirement(UpgradeType.Burn, "Incendiario", p => p.BurnLevel, 3f, Additive: false),
            new BuildRequirement(UpgradeType.ShockwaveAura, "Onda de Choque", p => p.OndaLevel, 2f, Additive: false),
        },
        BuildClass.Armored => new[]
        {
            new BuildRequirement(UpgradeType.OrbitShield, "Cuchillas", p => p.OrbitCount, 2f, Additive: false),
            new BuildRequirement(UpgradeType.HitShield, "Escudos", p => p.MaxShieldCharges, 3f, Additive: false),
            new BuildRequirement(UpgradeType.Laser, "Láser", p => p.LaserLevel, 1f, Additive: false),
        },
        BuildClass.Hunter => new[]
        {
            new BuildRequirement(UpgradeType.Ricochet, "Rebote", p => p.RicochetCount, 2f, Additive: false),
            new BuildRequirement(UpgradeType.Companion, "Dron", p => p.CompanionStatPercent, 0.3f, Additive: false),
        },
        _ => Array.Empty<BuildRequirement>(),
    };

    // Heals 1 life, capped at MaxLives — used by the Heart pickup an enemy can drop. A mid-run
    // top-up, distinct from the Corazón Legendario reward which raises MaxLives itself.
    public void AddLife(int amount = 1)
    {
        if (CurrentLives >= MaxLives) return;
        CurrentLives = Mathf.Min(CurrentLives + amount, MaxLives);
        LivesChanged?.Invoke(CurrentLives, MaxLives);
    }

    // Shield charges are consumable and never regenerate during a round, but a new round tops them
    // back up to full — the same "every round starts fresh" rule lives already follow. No-ops for a
    // player who's never bought a Barrier, so it can be called unconditionally on round start.
    public void RefillShield()
    {
        if (MaxShieldCharges <= 0) return;
        CurrentShieldCharges = MaxShieldCharges;
        RaiseShieldChanged();
    }

    public void TriggerLevelUpBurst()
    {
        // No nova on round 1 — leveling up that early (before the player has any real threat
        // pressure yet) shouldn't hand out a free screen-clear; the nova starts from round 2 on.
        if (GameManager.Instance != null && GameManager.Instance.RoundNumber == 1)
            return;

        var enemies = GetTree().GetNodesInGroup("enemies");
        foreach (var n in enemies)
        {
            // Bosses are excluded from the instant-kill nova — otherwise any level-up that fires
            // while a boss is in range deletes it in one hit regardless of the fight's actual
            // difficulty, which defeats the point of a boss encounter.
            if (n is Enemy enemy && IsInstanceValid(enemy) && enemy.Category != EnemyCategory.Boss)
            {
                float dist = GlobalPosition.DistanceTo(enemy.GlobalPosition);
                if (dist <= LevelUpBurstRadius)
                    enemy.TakeDamage(99999);
            }
        }

        SpawnBurstVisual();
    }

    // Parented to the player rather than to the arena, unlike the projectile blasts: the player is
    // still alive and moving, and these read as coming off them, so they should travel with them.
    // zIndex 0 keeps the previous layering — the burst covers the ship instead of sitting under it.
    private void SpawnBurstVisual() =>
        Juice.Blast(this, GlobalPosition, LevelUpBurstRadius, Palette.LevelUpNova,
            growTime: 0.3f, fadeTime: 0.35f, zIndex: 0);

    // Called by GameManager once every reward from a level-up streak has been picked, rather than at
    // the moment the level-up itself happens — the picker is still open then, and competing with it
    // for attention was the opposite of what a celebration text is for. World-space, not a HUD popup,
    // so it reads as coming off the ship and keeps tracking it through the drift/fade instead of
    // freezing at wherever the player was standing when they made their pick.
    public void ShowLevelUpText() =>
        Juice.FloatingLabel(this, "¡SUBE DE NIVEL!", GlobalPosition + new Vector2(0f, -46f),
            Palette.LevelPopup, Palette.FontSize.Title, driftY: -46f, holdBeforeFade: 0.55f, lifetime: 1.1f);

    // From the Epic tier up (LaserLevel 3+), the Laser couples to the player's current attack
    // stats instead of using its own fixed numbers — its damage scales with BulletDamage and its
    // fire interval scales with FireRate (both relative to their base values), so buying more
    // damage/fire-rate upgrades later also makes the Laser stronger/faster, and it'd scale back
    // down too if those stats were ever reduced.
    private const int LaserStatCoupledMinLevel = 3;

    private float GetLaserInterval()
    {
        float baseInterval = LaserTiers[LaserLevel - 1].Interval;
        if (LaserLevel < LaserStatCoupledMinLevel) return baseInterval;
        return Mathf.Max(0.3f, baseInterval * (_baseFireRate / FireRate));
    }

    private float GetLaserDamage()
    {
        float baseDamage = LaserTiers[LaserLevel - 1].Damage;
        if (LaserLevel < LaserStatCoupledMinLevel) return baseDamage;
        return baseDamage * (BulletDamage / _baseBulletDamage);
    }

    // Called every physics frame while LaserLevel > 0 and the cooldown has fully cleared — NOT a
    // repeating Timer. A Timer fires on its own schedule regardless of whether a target is in
    // range, so an enemy walking into range right after an empty tick had to wait out a whole
    // extra interval before the laser could actually go off. Polling every frame means it fires
    // the instant both conditions are true, with no dead time in between.
    private void TryFireLaser()
    {
        float damage = GetLaserDamage();
        int maxTargets = LaserTiers[LaserLevel - 1].MaxTargets;
        var enemies = GetTree().GetNodesInGroup("enemies");

        var inRange = new List<Enemy>();
        foreach (var n in enemies)
        {
            if (n is Enemy enemy && IsInstanceValid(enemy) && GlobalPosition.DistanceTo(enemy.GlobalPosition) <= EffectiveFireRange)
                inRange.Add(enemy);
        }

        // Capped tiers hit the closest N instead of an arbitrary N from group order.
        if (inRange.Count > maxTargets)
        {
            inRange.Sort((a, b) => GlobalPosition.DistanceSquaredTo(a.GlobalPosition)
                .CompareTo(GlobalPosition.DistanceSquaredTo(b.GlobalPosition)));
            inRange.RemoveRange(maxTargets, inRange.Count - maxTargets);
        }

        if (inRange.Count > 0)
            LaserCooldownFraction = 1f;

        foreach (var enemy in inRange)
        {
            if (CurrentBurnDps > 0f)
                enemy.ApplyBurn(CurrentBurnDps, CurrentBurnDuration);

            int finalDamage = ApplyCrit(Mathf.RoundToInt(damage), out bool isCrit);
            enemy.TakeDamage(finalDamage);
            SpawnLaserBeam(enemy.GlobalPosition, isCrit);
        }
    }

    // Called every physics frame while MissileLevel > 0 and the cooldown has fully cleared — same
    // reasoning as TryFireLaser above.
    private void TryFireMissile()
    {
        if (MissileScene == null || _bulletsContainer == null) return;

        var target = FindNearestEnemyInRange();
        if (target == null) return;   // nothing to shoot at; cooldown stays at 0, retried next frame

        MissileCooldownFraction = 1f;
        var (_, damage, radius) = MissileTiers[MissileLevel - 1];

        var missile = MissileScene.Instantiate<Missile>();
        missile.GlobalPosition = GlobalPosition;

        // Snapshot of where the target is *now*. Missile.cs never re-reads it, which is what makes
        // the shot dodgeable rather than homing.
        missile.TargetPosition = target.GlobalPosition;
        missile.Damage = ApplyCrit(damage, out bool isCrit);
        if (isCrit) missile.Modulate = Palette.CritBullet;
        missile.ExplosionRadius = radius;
        missile.BurnDps = CurrentBurnDps;
        missile.BurnDuration = CurrentBurnDuration;

        _bulletsContainer.AddChild(missile);
    }

    // Called every physics frame while MineLevel > 0 and the cooldown has fully cleared. No
    // targeting to do — it drops right where the player is standing, same reasoning as Onda/Vendaval.
    private void DropMine()
    {
        if (PlayerMineScene == null || _bulletsContainer == null) return;

        MineCooldownFraction = 1f;
        var (_, damage, radius) = MineTiers[MineLevel - 1];

        var mine = PlayerMineScene.Instantiate<PlayerMine>();
        mine.Damage = ApplyCrit(damage, out bool isCrit);
        if (isCrit) mine.Modulate = Palette.CritBullet;
        mine.Radius = radius;
        mine.BurnDps = CurrentBurnDps;
        mine.BurnDuration = CurrentBurnDuration;

        _bulletsContainer.AddChild(mine);
        mine.GlobalPosition = GlobalPosition;
    }

    // Shared by the gun and the missile so every auto-weapon respects FireRange identically.
    // Same obstacle-aware pick FindNearestVisibleEnemy already does for the main gun — a missile
    // shouldn't launch at the nearest enemy on the other side of a wall either.
    private Node2D FindNearestEnemyInRange() =>
        FindNearestVisibleEnemy(GetTree().GetNodesInGroup("enemies"), GlobalPosition, EffectiveFireRange);

    private void SpawnLaserBeam(Vector2 targetGlobalPos, bool isCrit = false)
    {
        var beam = new Line2D();
        beam.AddPoint(Vector2.Zero);
        beam.AddPoint(ToLocal(targetGlobalPos));
        beam.Width = 3f;
        beam.DefaultColor = isCrit ? Palette.CritBullet : Palette.LaserBeam;
        AddChild(beam);

        var tween = beam.CreateTween();
        tween.TweenProperty(beam, "modulate:a", 0f, 0.15f);
        tween.TweenCallback(Callable.From(() => beam.QueueFree()));
    }

    // Current fire rate as a multiple of the round-1 baseline — 1.0 at run start, rising to
    // MaxFireRate/base (4x) when fully upgraded. Orbit blades read this so their spin couples to
    // attack speed; exposing the ratio rather than _baseFireRate keeps that snapshot private and
    // the conversion in one place.
    public float FireRateRatio => _baseFireRate > 0f ? FireRate / _baseFireRate : 1f;

    // Rough DPS-equivalent of the player's whole offensive kit, expressed as a ratio against the
    // round-1 baseline (an untouched player scores exactly 1.0). Feeds DifficultyBalancer, which
    // compares it to what the current round expected and nudges enemy HP/damage if the player has
    // run far ahead of the curve. Lives here rather than in the balancer because it needs the
    // private _base* snapshots and the LaserTiers lookup.
    //
    // It's an approximation on purpose — the point is detecting order-of-magnitude runaway builds,
    // not simulating combat exactly.
    private const float OrbitHitsPerSecondEstimate = 2f;

    public float GetOffensivePower()
    {
        float baselineDps = _baseBulletDamage * _baseFireRate;
        if (baselineDps <= 0f) return 1f;

        int shotsPerVolley = 1 + (_hasExtraProjectile ? 2 : 0) + ExtraFiringLines;

        // Crit and pierce are genuine damage multipliers, so they belong here — without them a
        // crit/pierce-heavy build would read as far weaker than it plays and dodge the adaptive
        // difficulty correction entirely.
        float critMult = 1f + (CritChance / 100f) * (CritMultiplier - 1f);
        float pierceMult = 1f + BulletPierce * 0.5f;   // extra enemies hit per shot, discounted
        float gunDps = BulletDamage * FireRate * shotsPerVolley * critMult * pierceMult;

        float laserDps = LaserLevel > 0 ? GetLaserDamage() / GetLaserInterval() * critMult : 0f;

        // Both AoE, so their nominal single-target DPS understates them — a modest multiplier keeps
        // a missile/burn build from reading as weaker than it plays and slipping past the balancer.
        float missileDps = 0f;
        if (MissileLevel > 0)
        {
            var (interval, damage, _) = MissileTiers[MissileLevel - 1];
            missileDps = damage / interval * 1.5f * critMult;
        }

        // Sustained fire keeps burn permanently refreshed, so its DPS is effectively always on.
        // Applies once here even though it's now layered onto every weapon (bullets/laser/blades/
        // missiles) — modeling per-weapon burn uptime precisely isn't worth the complexity for an
        // estimate that only needs to catch order-of-magnitude runaway builds.
        float burnDps = CurrentBurnDps;

        float orbitDps = OrbitCount * OrbitBladeDamage * OrbitHitsPerSecondEstimate * critMult;

        float ondaDps = 0f;
        if (OndaLevel > 0)
        {
            var (interval, damage, _) = OndaTiers[OndaLevel - 1];
            ondaDps = damage / interval * critMult;
        }

        float vendavalDps = 0f;
        if (VendavalLevel > 0)
        {
            var (interval, damage, _, _, _) = VendavalTiers[VendavalLevel - 1];
            vendavalDps = damage / interval * critMult;
        }

        // Same AoE-discount reasoning as missileDps — a mine's single blast can hit several enemies
        // at once, so its nominal per-drop damage understates it just like Missile's does.
        float mineDps = 0f;
        if (MineLevel > 0)
        {
            var (interval, damage, _) = MineTiers[MineLevel - 1];
            mineDps = damage / interval * 1.5f * critMult;
        }

        // Ultimates bypass every stat term above entirely — Nova/Zona Lenta have no persistent
        // stat to read, and even Sobrecarga's temporary FireRate/BulletDamage doubling would only
        // get caught here if this happened to be sampled mid-buff. Modeled explicitly instead, as
        // an average over one full cycle — this is what makes a build that leans entirely on spamming
        // its Ultimate read as the load-bearing power source it actually is, instead of silently
        // reading as "doing nothing" and dodging the adaptive difficulty correction altogether (see
        // docs/difficulty-scaling.md — this was a real exploit, not hypothetical).
        float ultimateDps = 0f;
        if (EquippedUltimate != null)
        {
            // A full cycle is now effect-then-cooldown, not cooldown-with-effect-inside, so the
            // denominator has to be the sum. Dividing by the cooldown alone (as this did while the two
            // overlapped) would over-report uptime and hand the player a difficulty penalty they
            // haven't earned.
            float active = GetUltimateActiveDuration(EquippedUltimate.Value);
            float cooldown = Mathf.Max(UltimateCooldownDuration, 0.1f);
            float cycle = active + cooldown;
            float uptimeFraction = Mathf.Clamp(active / cycle, 0f, 1f);

            ultimateDps = EquippedUltimate.Value switch
            {
                // A flat burst every cycle — genuine average DPS, no approximation needed.
                UltimateKind.Nova => UltimateNovaDamage / cycle,

                // No direct damage number to convert — models "enemies can't catch you" as extra
                // effective throughput roughly on par with the player's whole raw kit, since a
                // permanent slow is close to permanent safety. Same "order of magnitude, not exact
                // combat simulation" approximation this whole method already leans on.
                UltimateKind.TimeSlow => baselineDps * uptimeFraction,

                // Already doubles gunDps live; averaged over the cycle so a snapshot taken between
                // uses doesn't read Sobrecarga as contributing nothing.
                UltimateKind.Frenzy => gunDps * uptimeFraction,

                // Same "permanent safety" modelling as TimeSlow, just literal rather than approximate
                // -- nothing can land at all while it's up, so it's worth at least as much.
                UltimateKind.Invulnerability => baselineDps * uptimeFraction,

                _ => 0f,
            };
        }

        // The companion re-derives its own output from the player's live stats, so it's a flat
        // percentage bonus on top of everything rather than its own independent term.
        float total = (gunDps + laserDps + missileDps + burnDps + orbitDps + ondaDps + vendavalDps + mineDps) * (1f + CompanionStatPercent) + ultimateDps;
        return total / baselineDps;
    }

    // Same contract as GetOffensivePower (a ratio against a round-1 baseline of exactly 1.0), but
    // for tankiness rather than damage — feeds DifficultyBalancer.GetSurvivabilityCatchUpMultiplier.
    // This exists because a defensively-stacked build (dodge + Tank's free shrug + shield + regen)
    // reads as approximately zero offensive power, so GetOffensivePower's correction never touches
    // it — it can become nearly unkillable with no adaptive response at all. Deliberately
    // approximate, same spirit as GetOffensivePower: detecting an order-of-magnitude stack, not
    // simulating combat exactly.
    private const float TankShrugChance = 0.25f;    // must match the literal in TakeHit's Tank branch
    private const float SurvivabilityBaseline = 3f; // MaxLives=3, no shield/dodge/Tank/regen

    public float GetSurvivabilityScore()
    {
        float dodgeMult = 1f / Mathf.Max(0.01f, 1f - DodgeChance / 100f);
        float tankMult = IsClassActive(BuildClass.Tank) ? 1f / Mathf.Max(0.01f, 1f - TankShrugChance) : 1f;

        // Lives + shield charges are the absorbable pool; dodge/Tank stretch it by making each point
        // absorb more incoming hits before it's actually spent.
        float pool = (MaxLives + MaxShieldCharges) * dodgeMult * tankMult;

        // Regen isn't part of the pool, it refills it — added rather than multiplied in.
        return (pool + ShieldRegenPerMinute) / SurvivabilityBaseline;
    }

    // Nominal duration for the two time-based Ultimates before the anti-permanent-uptime cap in
    // GetUltimateEffectDuration() below.
    private const float UltimateEffectDurationBase = 6f;

    // Once UltimateCooldownDuration floors out at 3.5s (round 11+), a flat 6s effect would overlap
    // itself and grant up to 100% uptime — enemies permanently slowed to 30% speed, or gun damage
    // permanently doubled, forever, rather than an occasional panic button. Capping the live
    // duration to a fraction of the CURRENT cooldown guarantees a real downtime window no matter
    // how short the cooldown gets, while leaving early rounds (where the cooldown is already long
    // enough that this never binds) completely unaffected.
    private const float UltimateUptimeCap = 0.8f;

    private float GetUltimateEffectDuration() =>
        Mathf.Min(UltimateEffectDurationBase, UltimateCooldownDuration * UltimateUptimeCap);

    // Mirrors OrbitShield.Damage's default. Kept in sync manually rather than read off a live
    // blade so the power estimate still works before any blade has been instantiated.
    private const int OrbitBladeDamage = 7;

    public void TriggerUltimate()
    {
        if (EquippedUltimate == null || UltimateCooldownRemaining > 0f) return;

        switch (EquippedUltimate.Value)
        {
            case UltimateKind.Nova:
                TriggerUltimateNova();
                break;
            case UltimateKind.TimeSlow:
                GameManager.Instance?.ApplyTemporarySlow(0.3f, GetUltimateEffectDuration());
                break;
            case UltimateKind.Frenzy:
                TriggerUltimateFrenzy();
                break;
            case UltimateKind.Invulnerability:
                TriggerUltimateInvulnerability();
                break;
        }

        // The cooldown starts counting only once the effect has finished, rather than running
        // concurrently with it the way it used to. Modelled by simply adding the active duration on
        // top — no extra state or callback needed, and the HUD's readout stays honest because it shows
        // the whole remaining lock either way. Nova is instantaneous, so it gets the bare cooldown.
        UltimateCooldownRemaining = GetUltimateActiveDuration(EquippedUltimate.Value) + UltimateCooldownDuration;
    }

    // Nova detonates on the frame it's triggered; the rest run for a while. Only the timed ones push
    // their cooldown back.
    private float GetUltimateActiveDuration(UltimateKind kind) => kind switch
    {
        UltimateKind.Nova => 0f,
        UltimateKind.Invulnerability => UltimateInvulnDuration,
        _ => GetUltimateEffectDuration(),
    };

    private const float UltimateNovaRadius = 500f;

    // Mirrors EnemySpawner.HpMultCurve (kept in sync manually, same "kept in sync" spirit as
    // OrbitBladeDamage above) -- a flat 500 stopped meaningfully denting anything once enemy HP had
    // climbed to 8x baseline, so Nova's damage scales by the same curve enemy HP does, keeping it
    // worth roughly the same fraction of a target's health at round 40 that it was at round 1.
    private static readonly RoundCurve UltimateNovaDamageCurve = new(500f, 90f, 500f, 4000f);
    private int UltimateNovaDamage => Mathf.RoundToInt(UltimateNovaDamageCurve.Evaluate(GameManager.Instance?.RoundNumber ?? 1));

    private void TriggerUltimateNova()
    {
        int damage = UltimateNovaDamage;
        var enemies = GetTree().GetNodesInGroup("enemies");
        foreach (var n in enemies)
        {
            if (n is Enemy enemy && IsInstanceValid(enemy))
            {
                float dist = GlobalPosition.DistanceTo(enemy.GlobalPosition);
                if (dist <= UltimateNovaRadius)
                    enemy.TakeDamage(damage);
            }
        }

        Juice.Blast(this, GlobalPosition, UltimateNovaRadius, Palette.UltimateNova,
            growTime: 0.35f, fadeTime: 0.5f, zIndex: 0);
    }

    // Escudo Absoluto: total damage immunity for the effect's duration. Reuses the exact i-frame
    // window a normal hit already opens (_invulnTimer + the blink loop in _PhysicsProcess and the
    // early-out at the top of TakeHit) rather than a second parallel immunity flag — the ultimate
    // is "a much longer i-frame", not a different mechanic, so there's nothing else to keep in sync.
    // This is the most absolute Ultimate: not a damage reduction or a chance to shrug a hit like
    // Dodge/Tank, a hit simply cannot land at all while the timer is running. Its own fixed 3s
    // (rather than the other timed Ultimates' shared GetUltimateEffectDuration) is deliberate — total
    // immunity is worth more per second than a slow or a damage buff, so it gets a shorter window.
    private const float UltimateInvulnDuration = 3f;

    // Purely additive on top of the blink above -- the ship still blinks exactly like a normal hit's
    // i-frames, but this aura stays solidly visible and pulsing the whole 3s so the ultimate reads as
    // its own distinct thing rather than "a really long version of getting hit". Same same-texture
    // Sprite2D-copy technique Enemy.cs already uses for its rim flare/elite glow (CreateRimFlare/
    // CreateEliteGlow): a scaled-up, tinted copy sitting behind the real sprite (ZIndex -1).
    private Sprite2D _ultimateShieldAura;
    private Tween _ultimateShieldTween;
    private Timer _ultimateShieldSparkTimer;
    private const float UltimateShieldAuraScale = 1.2f;
    private const float UltimateShieldPulsePeriod = 0.5f;
    private const float UltimateShieldSparkInterval = 0.15f;

    private void TriggerUltimateInvulnerability()
    {
        _invulnTimer = UltimateInvulnDuration;
        _blinkTimer = 0f;

        if (_ultimateShieldAura == null)
        {
            _ultimateShieldAura = new Sprite2D { Texture = _visual.Texture, ZIndex = -1 };
            AddChild(_ultimateShieldAura);
        }
        _ultimateShieldAura.Scale = _visualBaseScale * UltimateShieldAuraScale;
        _ultimateShieldAura.Visible = true;

        // Pushed past 1.0 so the pulse's bright end actually clears the arena's WorldEnvironment
        // glow threshold and blooms -- same HDR-boost idiom used for the menu's title/streak badge.
        _ultimateShieldTween?.Kill();
        var boosted = new Color(Palette.ShieldAura.R * 1.8f, Palette.ShieldAura.G * 1.8f, Palette.ShieldAura.B * 1.8f, Palette.ShieldAura.A);
        _ultimateShieldTween = Juice.Shimmer(this, _ultimateShieldAura, "modulate", Palette.ShieldAura, boosted, UltimateShieldPulsePeriod);

        if (_ultimateShieldSparkTimer == null)
        {
            _ultimateShieldSparkTimer = new Timer { WaitTime = UltimateShieldSparkInterval };
            AddChild(_ultimateShieldSparkTimer);
            _ultimateShieldSparkTimer.Timeout += SpawnUltimateShieldSpark;
        }
        _ultimateShieldSparkTimer.Start();

        GetTree().CreateTimer(UltimateInvulnDuration).Timeout += EndUltimateShieldAura;
    }

    // Small directional flashes bursting outward from the aura's rim -- reuses the same Spark helper
    // bullet impacts already use, so the "electricity" reads as crackling rather than needing any new
    // draw code.
    private void SpawnUltimateShieldSpark()
    {
        if (!IsInstanceValid(this) || _ultimateShieldAura is not { Visible: true }) return;

        float angle = (float)GD.RandRange(0f, Mathf.Tau);
        var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        float radius = _ultimateShieldAura.Texture.GetSize().X * _ultimateShieldAura.Scale.X * 0.5f;
        Juice.Spark(this, GlobalPosition + dir * radius, dir, Palette.ShieldAura, 8f);
    }

    private void EndUltimateShieldAura()
    {
        if (!IsInstanceValid(this) || _ultimateShieldAura == null) return;

        _ultimateShieldAura.Visible = false;
        _ultimateShieldTween?.Kill();
        _ultimateShieldSparkTimer?.Stop();
    }

    // Onda de Choque: the periodic small AoE reward. Same enemies-in-radius loop as
    // TriggerUltimateNova above, just centered on the player continuously rather than on demand.
    private void TriggerOnda()
    {
        var (_, damage, radius) = OndaTiers[OndaLevel - 1];

        var targets = new List<Enemy>();
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy enemy || !IsInstanceValid(enemy)) continue;
            if (GlobalPosition.DistanceTo(enemy.GlobalPosition) <= radius) targets.Add(enemy);
        }

        // Nothing in range yet — leave the cooldown at 0 rather than spending it on an empty pop,
        // so it fires the instant an enemy actually wanders into radius instead of on a fixed
        // schedule that can whiff entirely. Same "stays at 0, retried next frame" idea as
        // TryFireMissile's own no-target bail above.
        if (targets.Count == 0) return;

        OndaCooldownFraction = 1f;
        int dmg = ApplyCrit(damage, out bool isCrit);

        foreach (var enemy in targets)
        {
            if (CurrentBurnDps > 0f) enemy.ApplyBurn(CurrentBurnDps, CurrentBurnDuration);
            enemy.TakeDamage(dmg);
        }

        SpawnOndaVisual(radius, isCrit);
    }

    private void SpawnOndaVisual(float radius, bool isCrit) =>
        Juice.Blast(this, GlobalPosition, radius, isCrit ? Palette.CritBullet : Palette.OndaBlast,
            growTime: 0.25f, fadeTime: 0.4f, zIndex: 0);

    // Vendaval: shop-exclusive Epic/Legendary reward. A directional cone in front of the player
    // (facing direction, same angle the player's own triangle sprite is already turned to — see
    // UpdateFacing) that hits hard and shoves survivors out of the cone, clearing a path rather
    // than just clearing HP.
    private void TriggerVendaval()
    {
        VendavalCooldownFraction = 1f;
        var (_, damage, range, halfAngleDegrees, knockback) = VendavalTiers[VendavalLevel - 1];
        int dmg = ApplyCrit(damage, out bool isCrit);

        Vector2 facing = Vector2.Right.Rotated(_visual.Rotation);
        float halfAngleRad = Mathf.DegToRad(halfAngleDegrees);

        foreach (var n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy enemy || !IsInstanceValid(enemy)) continue;

            Vector2 toEnemy = enemy.GlobalPosition - GlobalPosition;
            float dist = toEnemy.Length();
            if (dist > range || dist <= 0f) continue;
            if (Mathf.Abs(facing.AngleTo(toEnemy)) > halfAngleRad) continue;

            if (CurrentBurnDps > 0f) enemy.ApplyBurn(CurrentBurnDps, CurrentBurnDuration);
            enemy.TakeDamage(dmg);
            enemy.ApplyKnockback(toEnemy / dist * knockback);
        }

        SpawnVendavalVisual(range, halfAngleDegrees, facing, isCrit);
    }

    private void SpawnVendavalVisual(float range, float halfAngleDegrees, Vector2 facing, bool isCrit)
    {
        const int segments = 14;
        var points = new Vector2[segments + 2];
        points[0] = Vector2.Zero;

        float halfAngleRad = Mathf.DegToRad(halfAngleDegrees);
        float startAngle = facing.Angle() - halfAngleRad;
        float endAngle = facing.Angle() + halfAngleRad;
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(startAngle, endAngle, i / (float)segments);
            points[i + 1] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * range;
        }

        var visual = new Polygon2D();
        visual.Polygon = points;
        visual.Color = isCrit ? Palette.CritBullet : Palette.VendavalBlast;
        AddChild(visual);

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(visual, "modulate:a", 0f, 0.3f);
        tween.Chain().TweenCallback(Callable.From(() => visual.QueueFree()));
    }


    private void TriggerUltimateFrenzy()
    {
        FireRate *= 2f;
        BulletDamage *= 2;
        _fireCooldown.WaitTime = 1f / FireRate;

        if (_frenzyTimer == null)
        {
            _frenzyTimer = new Timer { OneShot = true };
            AddChild(_frenzyTimer);
            _frenzyTimer.Timeout += () =>
            {
                RecomputeFireRateAndDamage();
                _fireCooldown.WaitTime = 1f / FireRate;
            };
        }
        _frenzyTimer.WaitTime = GetUltimateEffectDuration();
        _frenzyTimer.Start();
    }

    // Re-derives the permanent FireRate/BulletDamage from base + accumulated bonus — the same
    // formula ApplyUpgrade uses, but without recording a new pick. Called when the Sobrecarga
    // ultimate's temporary doubling expires, so it reverts to whatever the real permanent stat is
    // at that moment (correct even if a permanent Fire Rate/Damage upgrade was picked mid-buff),
    // not a stale pre-buff snapshot.
    private void RecomputeFireRateAndDamage()
    {
        FireRate = Mathf.Min(_baseFireRate + _stackTotals.GetValueOrDefault(UpgradeType.FireRate, 0f), MaxFireRate);
        BulletDamage = Mathf.RoundToInt(_baseBulletDamage + Mathf.Min(_stackTotals.GetValueOrDefault(UpgradeType.BulletDamage, 0f), MaxBulletDamageBonus));
    }

    // Nearest enemy within range that isn't hidden behind an obstacle — a bullet already stops dead
    // against one (Bullet.OnBodyEntered), so picking the geometrically-nearest target regardless of
    // what's in the way used to waste the shot (and that fraction of the cooldown) on a wall. Sorted
    // by distance rather than raycasting every candidate in the group: the common case (nothing
    // blocking the nearest enemy) costs exactly one raycast, same as before this existed.
    private Node2D FindNearestVisibleEnemy(Godot.Collections.Array<Node> enemies, Vector2 origin, float range)
    {
        float rangeSq = range * range;
        var candidates = new List<Node2D>();
        foreach (var n in enemies)
            if (n is Node2D e2d && IsInstanceValid(e2d) && origin.DistanceSquaredTo(e2d.GlobalPosition) <= rangeSq)
                candidates.Add(e2d);

        candidates.Sort((a, b) => origin.DistanceSquaredTo(a.GlobalPosition)
            .CompareTo(origin.DistanceSquaredTo(b.GlobalPosition)));

        foreach (var candidate in candidates)
            if (!Targeting.HasObstacleBetween(this, origin, candidate.GlobalPosition))
                return candidate;

        return null;
    }

    private void OnFireCooldownTimeout()
    {
        var enemies = GetTree().GetNodesInGroup("enemies");
        if (enemies.Count == 0) return;

        Node2D nearest = FindNearestVisibleEnemy(enemies, GlobalPosition, EffectiveFireRange);
        if (nearest == null) return;

        Vector2 baseDir = (nearest.GlobalPosition - GlobalPosition).Normalized();
        SpawnMuzzleFlash(baseDir);
        FireInDirection(baseDir);

        if (_hasExtraProjectile)
        {
            FireInDirection(baseDir.Rotated(Mathf.DegToRad(25)));
            FireInDirection(baseDir.Rotated(Mathf.DegToRad(-25)));
        }

        if (ExtraFiringLines > 0)
        {
            Vector2 perpendicular = baseDir.Rotated(Mathf.Pi / 2f);
            for (int i = 1; i <= ExtraFiringLines; i++)
            {
                float side = (i % 2 == 1) ? 1f : -1f;
                int rank = (i + 1) / 2;
                Vector2 offset = perpendicular * SideShotSpacing * rank * side;
                FireInDirection(baseDir, offset, isSideShot: true);
            }
        }
    }

    // Bullets spawn at the player's exact centre (FireInDirection applies no forward offset), so the
    // flash is pushed out to roughly the nose to read as coming from a barrel rather than from inside
    // the hull.
    private const float MuzzleFlashOffset = 20f;
    private const float MuzzleFlashSize = 13f;

    // Deliberately called once per volley from OnFireCooldownTimeout, NOT from FireInDirection: with
    // Disparo en Diagonal plus Disparo Paralelo maxed, one volley is five FireInDirection calls, and
    // five overlapping flashes on the same frame are noise and wasted nodes rather than five times the
    // feedback. The player reads "I fired", which happens once.
    //
    // The shot sound rides along here for exactly the same reason, and it matters more for audio than
    // for the flash: five copies of one 85ms blip on a single frame don't sound five times louder,
    // they sound like a broken speaker.
    private void SpawnMuzzleFlash(Vector2 dir)
    {
        AudioManager.Instance?.Play(AudioManager.Sfx.Shoot);

        // Into the Bullets container, alongside the shot it belongs to, rather than as a child of the
        // player — a flash parented to the ship would slide along with it for its whole (short) life
        // instead of staying where the gun actually went off.
        Juice.Spark(_bulletsContainer, GlobalPosition + dir * MuzzleFlashOffset, dir,
            Palette.PlayerBullet.Lightened(0.4f), MuzzleFlashSize);
    }

    private readonly Random _critRng = new();
    private readonly Random _dodgeRng = new();
    private const float CritMultiplier = 2f;

    // Runs GameManager.SurvivabilityCatchUpMultiplier down into an integer 1-or-2 cost per landed
    // hit, via a deterministic accumulator rather than a second layer of RNG stacked on top of
    // Dodge/Tank — a 1.5x multiplier means "every other hit costs double", not a noisy coin flip.
    private float _hitCostAccumulator = 0f;

    private int ComputeHitCost()
    {
        float mult = GameManager.Instance?.SurvivabilityCatchUpMultiplier ?? 1f;
        _hitCostAccumulator += mult - 1f;
        if (_hitCostAccumulator >= 1f)
        {
            _hitCostAccumulator -= 1f;
            return 2;
        }
        return 1;
    }

    // Rolled per hit, not per volley/tick — same reasoning as burn being applied per hit rather
    // than per pickup. Shared by every weapon (bullets, laser, blades, missiles, the drone) so a
    // Crítico build feels like it crits everywhere, not just on the basic gun.
    // Lifetime crit total (GameManager.TotalCritsLanded) is rolled up from this at end of run —
    // Player is recreated fresh every run, so no reset needed here.
    public int CritsLandedThisRun { get; private set; }

    public int ApplyCrit(int baseDamage, out bool isCrit)
    {
        isCrit = CritChance > 0f && _critRng.NextDouble() * 100.0 < CritChance;
        if (isCrit) CritsLandedThisRun++;
        float classBonus = IsClassActive(BuildClass.Assassin) ? 1.25f : 1f;
        return isCrit ? Mathf.RoundToInt(baseDamage * CritMultiplier * classBonus) : Mathf.RoundToInt(baseDamage * classBonus);
    }

    // Class passive: Explorer gets +30% move speed and +10% fire range.
    public float GetClassSpeedMultiplier()
    {
        return IsClassActive(BuildClass.Explorer) ? 1.3f : 1f;
    }

    public float GetClassFireRangeMultiplier()
    {
        return IsClassActive(BuildClass.Explorer) ? 1.1f : 1f;
    }

    // Class passive: Pyromaniac gets +75% burn DPS and +1s burn duration.
    public float GetClassBurnMultiplier()
    {
        return IsClassActive(BuildClass.Pyromaniac) ? 1.75f : 1f;
    }

    public float GetClassBurnDurationBonus()
    {
        return IsClassActive(BuildClass.Pyromaniac) ? 1f : 0f;
    }

    // Class passive: Hunter gets +1 ricochet (beyond the owned reward count) and a stronger drone.
    public int GetClassRicochetBonus()
    {
        return IsClassActive(BuildClass.Hunter) ? 1 : 0;
    }

    public float GetClassCompanionMultiplier()
    {
        return IsClassActive(BuildClass.Hunter) ? 1.1f : 1f;
    }

    // Class passive: Tank shrugs off 25% of hits and takes 40% less knockback.
    public float GetClassKnockbackMultiplier()
    {
        return IsClassActive(BuildClass.Tank) ? 0.6f : 1f;
    }

    private void FireInDirection(Vector2 dir, Vector2 positionOffset = default, bool isSideShot = false)
    {
        if (BulletScene == null || _bulletsContainer == null) return;

        var bullet = BulletScene.Instantiate<Bullet>();
        bullet.GlobalPosition = GlobalPosition + positionOffset;
        bullet.Direction = dir;
        bullet.Speed = BulletSpeed;
        bullet.Pierce = BulletPierce;
        bullet.Knockback = BulletKnockback;

        // Side lines only bounce once the player has legendary-tier Rebote (Rebote IV, count 4):
        // letting Common/Rare ricochet spread the whole side volley would make side shots strictly
        // free multi-targeting. Main and diagonal Twin lines bounce at whatever tier the player has.
        bullet.Ricochet = isSideShot
            ? (RicochetCount >= LegendaryRicochetCount ? RicochetCount : 0)
            : RicochetCount + GetClassRicochetBonus();

        bullet.BurnDps = CurrentBurnDps;
        bullet.BurnDuration = CurrentBurnDuration;

        // Rolled per bullet, not per volley, so a Twin/Side Shot spread can crit on some lines and
        // not others — more visible feedback than an all-or-nothing volley.
        bullet.Damage = ApplyCrit(BulletDamage, out bool isCrit);
        // Crit always wins over the cosmetic — it's a gameplay signal (an extra-damage hit), not a
        // style choice, so it can't be customized away.
        //
        // Original resolves to Palette.PlayerBullet explicitly rather than CosmeticCatalog's own
        // White for that id: Bullet.tscn's Visual/Halo are plain white (Modulate is the only thing
        // that ever colours a bullet), so an unresolved White here would paint bullets white instead
        // of leaving them the game's actual default green.
        _bulletsContainer.AddChild(bullet);

        // Applied after AddChild, not before: an animated Epico colour starts a Tween, and
        // CreateTween on a node that isn't in the tree yet fails. A crit keeps its own fixed colour —
        // crits have to stay instantly distinguishable, and a shimmering one wouldn't be.
        if (isCrit) bullet.Modulate = Palette.CritBullet;
        else Juice.ApplyCosmetic(bullet, "modulate", CosmeticCategory.Bullet, Palette.PlayerBullet);
    }

    // Every upgrade ever applied this run, mapped to its strongest tier — the single entry point
    // is ApplyUpgrade, so this covers level-up picks, shop purchases and the boss Ultimate choice
    // alike. Drives the Loadout menu's "items equipados" list.
    private readonly Dictionary<UpgradeType, RewardTier> _ownedTiers = new();
    public IReadOnlyDictionary<UpgradeType, RewardTier> OwnedTiers => _ownedTiers;

    public void ApplyUpgrade(UpgradeData upgrade)
    {
        // Only ever raises the recorded tier, never lowers it. Used to overwrite unconditionally —
        // buying a Common of a type you already had at Legendary (legitimate; every pick of
        // FireRange/FireRate/BulletDamage adds to the total regardless of tier, see docs/rewards.md)
        // would silently "forget" the Legendary here even though the stat itself keeps accumulating
        // the Common's value on top. BuildCatalog's Loadout hint relies on this being accurate.
        if (!_ownedTiers.TryGetValue(upgrade.Type, out var existingTier) || upgrade.Tier > existingTier)
        {
            _ownedTiers[upgrade.Type] = upgrade.Tier;
            if (upgrade.Tier == RewardTier.Legendary)
                GameManager.Instance?.NotifyLegendaryObtained(upgrade.Type);
        }
        switch (upgrade.Type)
        {
            case UpgradeType.FireRange:
                FireRange = Mathf.Min(_baseFireRange + Mathf.Min(ApplyFlatStack(upgrade), MaxFireRangeBonus), MaxFireRange);
                RebuildFireRangeIndicator();
                break;
            case UpgradeType.ExtraProjectile:
                _hasExtraProjectile = true;
                break;
            case UpgradeType.OrbitShield:
                OrbitCount = Mathf.Max(OrbitCount, (int)upgrade.Value);
                RefreshOrbitBlades();
                break;
            case UpgradeType.FireRate:
                FireRate = Mathf.Min(_baseFireRate + ApplyFlatStack(upgrade), MaxFireRate);
                _fireCooldown.WaitTime = 1f / FireRate;
                break;
            case UpgradeType.BulletDamage:
                BulletDamage = Mathf.RoundToInt(_baseBulletDamage + Mathf.Min(ApplyFlatStack(upgrade), MaxBulletDamageBonus));
                break;
            // Legendary additionally arms the Tron-style speed trail — see UpdateThruster/
            // SpawnSpeedTrailSegment, gated live off _ownedTiers rather than a cached flag here.
            case UpgradeType.MovementSpeed:
                MoveSpeed = Mathf.Min(_baseMoveSpeed + ApplyFlatStack(upgrade), _baseMoveSpeed + MaxMoveSpeedBonus);
                break;
            // Heart is a single Legendary tier now, so the old "max tracks the best tier ever
            // picked" rule is meaningless (every pick is the same tier) — it's straightforwardly
            // additive instead, +1 max per purchase up to the cap, and always a full heal.
            case UpgradeType.Heart:
                MaxLives = Mathf.Min(MaxLives + (int)upgrade.Value, MaxLivesCap);
                CurrentLives = MaxLives;
                LivesChanged?.Invoke(CurrentLives, MaxLives);
                break;
            case UpgradeType.HitShield:
                MaxShieldCharges = Mathf.Min(Mathf.Max(MaxShieldCharges, (int)upgrade.Value), MaxShieldChargesCap);
                CurrentShieldCharges = Mathf.Min(CurrentShieldCharges + (int)upgrade.Value, MaxShieldCharges);
                RaiseShieldChanged();
                break;
            case UpgradeType.Companion:
                CompanionStatPercent = Mathf.Max(CompanionStatPercent, upgrade.Value / 100f);
                EnsureCompanion();
                break;
            case UpgradeType.SideShot:
                ExtraFiringLines = Mathf.Min(ExtraFiringLines + (int)upgrade.Value, MaxExtraFiringLinesCap);
                break;
            case UpgradeType.Laser:
                LaserLevel = Mathf.Max(LaserLevel, (int)upgrade.Value);
                break;
            case UpgradeType.Missile:
                MissileLevel = Mathf.Max(MissileLevel, (int)upgrade.Value);
                break;
            case UpgradeType.ShockwaveAura:
                OndaLevel = Mathf.Max(OndaLevel, (int)upgrade.Value);
                break;
            case UpgradeType.Vendaval:
                VendavalLevel = Mathf.Max(VendavalLevel, (int)upgrade.Value);
                break;
            case UpgradeType.Mine:
                MineLevel = Mathf.Max(MineLevel, (int)upgrade.Value);
                break;
            case UpgradeType.Burn:
                BurnLevel = Mathf.Max(BurnLevel, (int)upgrade.Value);
                break;
            case UpgradeType.Ultimate:
                EquippedUltimate = upgrade.Ultimate;
                UltimateCooldownRemaining = 0f;
                break;

            // Counts and percentages accumulate flatly up to a cap (the SideShot model) — tier
            // bucketing would be strange for "how many enemies does a bullet pass through".
            case UpgradeType.Pierce:
                BulletPierce = Mathf.Min(BulletPierce + (int)upgrade.Value, MaxPierceCap);
                break;
            case UpgradeType.CritChance:
                CritChance = Mathf.Min(CritChance + upgrade.Value, MaxCritChance);
                break;
            case UpgradeType.CoinBonus:
                CoinBonusPercent = Mathf.Min(CoinBonusPercent + upgrade.Value, MaxCoinBonusPercent);
                break;
            case UpgradeType.XpBonus:
                XpBonusPercent = Mathf.Min(XpBonusPercent + upgrade.Value, MaxXpBonusPercent);
                break;

            // Magnitude stat — unlike FireRange/FireRate/BulletDamage (flat-additive, see
            // ApplyFlatStack), this one still uses the older tier-bucketing (ApplyTieredStack):
            // same tier stacks, different tiers don't sum, the highest bucket wins.
            case UpgradeType.BulletKnockback:
                BulletKnockback = Mathf.Min(ApplyTieredStack(upgrade), MaxBulletKnockbackBonus);
                break;

            case UpgradeType.ShieldRegen:
                ShieldRegenPerMinute = Mathf.Max(ShieldRegenPerMinute, upgrade.Value);
                EnsureShieldRegenTimer();
                break;
            case UpgradeType.Dodge:
                DodgeChance = Mathf.Min(DodgeChance + upgrade.Value, 25f);
                break;
            case UpgradeType.Fortune:
                // Cap matches the sum of all four tiers (2+3+5+7=17) — nerfed down from 25 (3+5+7+10)
                // because a flat bonus added to Rare/Epic/Legendary's raw weight before renormalizing
                // (see RewardTierRoller.GetWeights) skews the whole roll harder than the headline
                // percentage suggests, especially against Legendary's tiny early-round raw weight.
                FortuneBonus = Mathf.Min(FortuneBonus + upgrade.Value, 17f);
                break;
            case UpgradeType.Ricochet:
                RicochetCount = Mathf.Max(RicochetCount, (int)upgrade.Value);
                break;
            case UpgradeType.Thorns:
                ThornsDamage = Mathf.Max(ThornsDamage, upgrade.Value);
                break;
        }
        CheckBuildClass();
    }

    // Shield regen (shop-only Regeneración). Stored as charges-per-minute so "higher is better"
    // stays uniform with every other take-the-best reward; the timer interval is derived from it.
    private void EnsureShieldRegenTimer()
    {
        if (ShieldRegenPerMinute <= 0f) return;

        if (_shieldRegenTimer == null)
        {
            _shieldRegenTimer = new Timer();
            AddChild(_shieldRegenTimer);
            _shieldRegenTimer.Timeout += OnShieldRegenTimeout;
        }
        int round = GameManager.Instance?.RoundNumber ?? 1;
        float effectiveRate = round >= LateShieldRegenRound
            ? ShieldRegenPerMinute * LateShieldRegenMultiplier
            : ShieldRegenPerMinute;
        _shieldRegenTimer.WaitTime = 60f / effectiveRate;
        _shieldRegenTimer.Start();
    }

    // Re-derives the timer's interval against the current round without touching ShieldRegenPerMinute
    // itself. Needed because EnsureShieldRegenTimer otherwise only runs once, at purchase time — a run
    // that buys Regeneración before round 17 would never feel the late-round slowdown without this
    // being called again as rounds advance (see GameManager.StartNextRound).
    public void RefreshShieldRegenRate() => EnsureShieldRegenTimer();

    private void OnShieldRegenTimeout()
    {
        AddShieldCharge();
    }

    // Adds one shield charge, capped at MaxShieldCharges. No-ops for a player with no Barrier
    // (MaxShieldCharges == 0). Shared by the passive Regeneración timer above and the Shield
    // pickup an enemy can drop.
    public void AddShieldCharge()
    {
        if (MaxShieldCharges <= 0 || CurrentShieldCharges >= MaxShieldCharges) return;

        CurrentShieldCharges++;
        RaiseShieldChanged();
    }

    // True when actually picking this exact offer right now would improve the player's current
    // state at all. Value-based (simulates the real result), not just a tier-number comparison —
    // e.g. a Common Fire Range boost after two stacked Rares is correctly "no" even though Rare > Common
    // nominally, because the Rare bucket already wins. Drives both the glow highlight and the
    // "se suma" / "no suma" wording below.
    public bool IsUpgradeOverCurrent(UpgradeData upgrade)
    {
        switch (upgrade.Type)
        {
            case UpgradeType.FireRange:
                return Mathf.Min(_baseFireRange + Mathf.Min(PreviewFlatBonus(upgrade), MaxFireRangeBonus), MaxFireRange) > FireRange;
            case UpgradeType.BulletDamage:
                return Mathf.Min(PreviewFlatBonus(upgrade), MaxBulletDamageBonus) > (BulletDamage - _baseBulletDamage);
            case UpgradeType.FireRate:
                return Mathf.Min(PreviewFlatBonus(upgrade), MaxFireRate - _baseFireRate) > (FireRate - _baseFireRate);
            case UpgradeType.MovementSpeed:
                return Mathf.Min(PreviewFlatBonus(upgrade), MaxMoveSpeedBonus) > (MoveSpeed - _baseMoveSpeed);
            // Helps if it can still raise the ceiling, or if there's any damage for its full heal
            // to undo — only a player at the cap *and* on full lives gains nothing.
            case UpgradeType.Heart:
                return MaxLives < MaxLivesCap || CurrentLives < MaxLives;
            case UpgradeType.HitShield:
            {
                int newMax = Mathf.Min(Mathf.Max(MaxShieldCharges, (int)upgrade.Value), MaxShieldChargesCap);
                int newCurrent = Mathf.Min(CurrentShieldCharges + (int)upgrade.Value, newMax);
                return newMax > MaxShieldCharges || newCurrent > CurrentShieldCharges;
            }
            case UpgradeType.SideShot:
                return ExtraFiringLines < MaxExtraFiringLinesCap;
            // Was missing entirely, which meant Twin Shot always read as "improves nothing" and so
            // could never be flagged as the best offer even when unowned.
            case UpgradeType.ExtraProjectile:
                return !_hasExtraProjectile;
            case UpgradeType.OrbitShield:
                return (int)upgrade.Value > OrbitCount;
            case UpgradeType.Companion:
                return upgrade.Value / 100f > CompanionStatPercent;
            case UpgradeType.Laser:
                return (int)upgrade.Value > LaserLevel;
            case UpgradeType.Missile:
                return (int)upgrade.Value > MissileLevel;
            case UpgradeType.ShockwaveAura:
                return (int)upgrade.Value > OndaLevel;
            case UpgradeType.Vendaval:
                return (int)upgrade.Value > VendavalLevel;
            case UpgradeType.Mine:
                return (int)upgrade.Value > MineLevel;
            case UpgradeType.Burn:
                return (int)upgrade.Value > BurnLevel;
            case UpgradeType.Ultimate:
                return EquippedUltimate != upgrade.Ultimate;
            case UpgradeType.Pierce:
                return BulletPierce < MaxPierceCap;
            case UpgradeType.CritChance:
                return CritChance < MaxCritChance;
            case UpgradeType.CoinBonus:
                return CoinBonusPercent < MaxCoinBonusPercent;
            case UpgradeType.XpBonus:
                return XpBonusPercent < MaxXpBonusPercent;
            case UpgradeType.BulletKnockback:
                return Mathf.Min(PreviewTieredBonus(upgrade), MaxBulletKnockbackBonus) > BulletKnockback;
            case UpgradeType.ShieldRegen:
                return upgrade.Value > ShieldRegenPerMinute;
            case UpgradeType.Dodge:
                return upgrade.Value > DodgeChance;
            case UpgradeType.Fortune:
                return upgrade.Value > FortuneBonus;
            case UpgradeType.Ricochet:
                return (int)upgrade.Value > RicochetCount;
            case UpgradeType.Thorns:
                return upgrade.Value > ThornsDamage;
            default:
                return false;
        }
    }

    // True when picking this specific offer right now would do absolutely nothing — used to
    // disable/grey out the option (in both the shop and the free level-up picker) so the player
    // never spends a purchase or a pick on something with zero effect. The offer stays visible;
    // only its button is disabled.
    //
    // For the tiered-stacking types (FireRange/FireRate/BulletDamage) merely being a *losing tier*
    // is NOT useless — picking one still feeds that tier's bucket, which is a legitimate long-game
    // "stack the same tier" strategy, and those cases are flagged via GetStackInfoText's wording
    // instead. But once the stat has hit its hard cap, feeding the bucket provably changes nothing,
    // so at-cap is genuinely useless and does get blocked.
    public bool IsRewardUseless(UpgradeData upgrade)
    {
        switch (upgrade.Type)
        {
            case UpgradeType.ExtraProjectile:
                return HasExtraProjectile;
            case UpgradeType.OrbitShield:
            case UpgradeType.Companion:
            case UpgradeType.SideShot:
            case UpgradeType.Heart:
            case UpgradeType.HitShield:
            case UpgradeType.Laser:
            case UpgradeType.Missile:
            case UpgradeType.ShockwaveAura:
            case UpgradeType.Vendaval:
            case UpgradeType.Mine:
            case UpgradeType.Burn:
            case UpgradeType.Ultimate:
            case UpgradeType.Pierce:
            case UpgradeType.CritChance:
            case UpgradeType.CoinBonus:
            case UpgradeType.XpBonus:
            case UpgradeType.ShieldRegen:
            case UpgradeType.Dodge:
            case UpgradeType.Fortune:
            case UpgradeType.Ricochet:
            case UpgradeType.Thorns:
                return !IsUpgradeOverCurrent(upgrade);
            case UpgradeType.FireRange:
                return FireRange >= MaxFireRange;
            case UpgradeType.BulletDamage:
                return BulletDamage - _baseBulletDamage >= MaxBulletDamageBonus;
            case UpgradeType.FireRate:
                return FireRate >= MaxFireRate;
            case UpgradeType.MovementSpeed:
                return MoveSpeed - _baseMoveSpeed >= MaxMoveSpeedBonus;
            // Same hard-cap rule as the three above; it was absent, so a maxed-out Knockback offer
            // stayed enabled and could be bought for nothing.
            case UpgradeType.BulletKnockback:
                return BulletKnockback >= MaxBulletKnockbackBonus;
            default:
                return false;
        }
    }

    // Wording for a disabled offer's button, shared by the shop and the level-up picker so they
    // can't drift. Three distinct reasons, three distinct sentences — "Adquirido" used to be the
    // catch-all default, which meant it also answered for the level-based powers (Láser, Misil,
    // Mina, Onda, Vendaval, Incendiario, Dron, Cuchillas). For those it was simply wrong: the
    // player doesn't own *that* offer, they own an equal or better level of it, and being told
    // "acquired" about something they never bought is the kind of small lie that erodes trust in
    // every other label on the screen.
    public string GetUnavailableLabel(UpgradeData upgrade)
    {
        switch (upgrade.Type)
        {
            // Stats with a hard ceiling: nothing is owned, the number simply can't go higher.
            case UpgradeType.FireRange:
            case UpgradeType.BulletDamage:
            case UpgradeType.FireRate:
            case UpgradeType.SideShot:
            case UpgradeType.Heart:
            case UpgradeType.HitShield:
            case UpgradeType.Pierce:
            case UpgradeType.CritChance:
            case UpgradeType.CoinBonus:
            case UpgradeType.XpBonus:
            case UpgradeType.BulletKnockback:
            case UpgradeType.Dodge:
            case UpgradeType.Fortune:
            case UpgradeType.Ricochet:
                return Glossary.AtCap;

            // Genuine one-offs — you either have it or you don't.
            case UpgradeType.ExtraProjectile:
            case UpgradeType.Ultimate:
                return Glossary.Owned;

            // Levelled powers: you hold this level or a higher one.
            default:
                return Glossary.OwnedBetter;
        }
    }

    // Which of the currently-offered rewards should get the "this is the one to take" glow.
    //
    // IsUpgradeOverCurrent alone can't answer this: it's a per-offer question ("does this beat what
    // I already have?"), so with a tier-1 stat owned and tier 2 + tier 3 both on the table, both
    // pass and the strongest isn't distinguished. This adds the missing cross-offer pass — among
    // the offers that do improve on the player's current state, only the single best of each
    // reward family wins. Shared by the shop and the level-up picker so the two can't disagree.
    //
    // Returns a parallel bool[] indexed the same as `offers`.
    public bool[] GetHighlightFlags(List<UpgradeData> offers)
    {
        var flags = new bool[offers.Count];
        var bestByType = new Dictionary<UpgradeType, int>();

        for (int i = 0; i < offers.Count; i++)
        {
            if (!IsUpgradeOverCurrent(offers[i])) continue;

            if (!bestByType.TryGetValue(offers[i].Type, out int bestIndex) || IsStronger(offers[i], offers[bestIndex]))
                bestByType[offers[i].Type] = i;
        }

        foreach (int index in bestByType.Values)
            flags[index] = true;

        return flags;
    }

    // Value is the primary comparison (it's the actual magnitude of the effect); Tier breaks ties
    // for families where two tiers share a value, like Heart's Epic/Legendary +3.
    private static bool IsStronger(UpgradeData candidate, UpgradeData current)
    {
        if (candidate.Value != current.Value) return candidate.Value > current.Value;
        return candidate.Tier > current.Tier;
    }

    // Always-shown "here's where you stand" text for every offer except the one-time Twin Shot
    // toggle (which has nothing to show beyond the shop's own "Adquirido" state).
    public string GetStackInfoText(UpgradeData upgrade)
    {
        bool helps = IsUpgradeOverCurrent(upgrade);
        switch (upgrade.Type)
        {
            case UpgradeType.FireRange:
                return $"Tenés: {FireRange:0} de rango (tope {MaxFireRange:0}) — {(helps ? "se suma" : $"no suma ({Glossary.AtCapSentence})")}";
            case UpgradeType.BulletDamage:
                return $"Tenés: +{BulletDamage - Mathf.RoundToInt(_baseBulletDamage)} (tope +{(int)MaxBulletDamageBonus}) — {(helps ? "se suma" : $"no suma ({Glossary.AtCapSentence})")}";
            case UpgradeType.FireRate:
                return $"Tenés: {FireRate:0.0}/s (tope {MaxFireRate:0}/s) — {(helps ? "se suma" : $"no suma ({Glossary.AtCapSentence})")}";
            case UpgradeType.MovementSpeed:
                return $"Tenés: +{MoveSpeed - _baseMoveSpeed:0} (tope +{(int)MaxMoveSpeedBonus}) — {(helps ? "se suma" : $"no suma ({Glossary.AtCapSentence})")}";
            case UpgradeType.Heart:
                return $"Tenés: {CurrentLives}/{MaxLives} vidas (tope {MaxLivesCap}) — {(helps ? "+1 al máximo y cura total" : $"no suma ({Glossary.AtCapSentence} y con vidas llenas)")}";
            case UpgradeType.HitShield:
                return $"Tenés: {MaxShieldCharges} cargas (tope {MaxShieldChargesCap}) — {(helps ? "se suma" : $"no suma ({Glossary.AtCapSentence})")}";
            case UpgradeType.SideShot:
                return $"Tenés: {ExtraFiringLines} líneas (tope {MaxExtraFiringLinesCap}) — {(helps ? "se suma" : Glossary.AtCapSentence)}";
            case UpgradeType.OrbitShield:
                return $"Tenés: {OrbitCount} cuchillas (tope 4) — {(helps ? "mejora" : "no mejora (ya tenés igual o mejor)")}";
            case UpgradeType.Companion:
                return $"Tenés: {CompanionStatPercent * 100:0}% stats (tope 50%) — {(helps ? "mejora" : "no mejora (ya tenés igual o mejor)")}";
            case UpgradeType.Laser:
                return $"Tenés: Láser {Glossary.LevelPrefix}{LaserLevel} (tope {Glossary.LevelPrefix}4) — {(helps ? "mejora" : "no mejora (ya tenés igual o mejor)")}";
            case UpgradeType.Missile:
                return MissileLevel > 0
                    ? $"Tenés: Misil {Glossary.LevelPrefix}{MissileLevel} cada {MissileTiers[MissileLevel - 1].Interval:0.#}s — {(helps ? "mejora" : "no mejora (ya tenés igual o mejor)")}"
                    : "No tenés misiles todavía";
            case UpgradeType.ShockwaveAura:
                return OndaLevel > 0
                    ? $"Tenés: Onda de Choque {Glossary.LevelPrefix}{OndaLevel} cada {OndaTiers[OndaLevel - 1].Interval:0.#}s — {(helps ? "mejora" : "no mejora (ya tenés igual o mejor)")}"
                    : "No tenés Onda de Choque todavía";
            case UpgradeType.Vendaval:
                return VendavalLevel > 0
                    ? $"Tenés: Vendaval {Glossary.LevelPrefix}{VendavalLevel} cada {VendavalTiers[VendavalLevel - 1].Interval:0.#}s — {(helps ? "mejora" : "no mejora (ya tenés igual o mejor)")}"
                    : "No tenés Vendaval todavía";
            case UpgradeType.Mine:
                return MineLevel > 0
                    ? $"Tenés: Mina {Glossary.LevelPrefix}{MineLevel} cada {MineTiers[MineLevel - 1].Interval:0.#}s — {(helps ? "mejora" : "no mejora (ya tenés igual o mejor)")}"
                    : "No tenés minas todavía";
            case UpgradeType.Burn:
                return BurnLevel > 0
                    ? $"Tenés: Incendiario {Glossary.LevelPrefix}{BurnLevel} ({BurnTiers[BurnLevel - 1].Dps:0}/seg) — {(helps ? "mejora" : "no mejora (ya tenés igual o mejor)")}"
                    : "No tenés quemadura todavía";
            case UpgradeType.Ultimate:
            {
                string current = EquippedUltimate == null
                    ? "ninguna"
                    : UltimateKindNames.Display(EquippedUltimate.Value);
                return $"Ultimate equipada: {current} — {(helps ? "la reemplaza" : "ya es esta")}";
            }
            case UpgradeType.Pierce:
                return $"Tenés: atraviesa {BulletPierce} (tope {MaxPierceCap}) — {(helps ? "se suma" : Glossary.AtCapSentence)}";
            case UpgradeType.CritChance:
                return $"Tenés: {CritChance:0}% crítico (tope {MaxCritChance:0}%) — {(helps ? "se suma" : Glossary.AtCapSentence)}";
            case UpgradeType.CoinBonus:
                return $"Tenés: +{CoinBonusPercent:0}% monedas (tope {MaxCoinBonusPercent:0}%) — {(helps ? "se suma" : Glossary.AtCapSentence)}";
            case UpgradeType.XpBonus:
                return $"Tenés: +{XpBonusPercent:0}% experiencia (tope {MaxXpBonusPercent:0}%) — {(helps ? "se suma" : Glossary.AtCapSentence)}";
            case UpgradeType.BulletKnockback:
                return $"Tenés: +{BulletKnockback:0} empuje (tope {MaxBulletKnockbackBonus:0}) — {(helps ? "se suma" : $"no suma (ya tenés una de mejor {Glossary.Rarity})")}";
            case UpgradeType.ShieldRegen:
                return ShieldRegenPerMinute > 0f
                    ? $"Tenés: 1 carga cada {60f / ShieldRegenPerMinute:0.#}s — {(helps ? "mejora" : "no mejora (ya tenés igual o mejor)")}"
                    : "No tenés regeneración de escudo todavía";
            case UpgradeType.Dodge:
                return $"Tenés: {DodgeChance:0}% esquiva (tope 25%) — {(helps ? "se suma" : Glossary.AtCapSentence)}";
            case UpgradeType.Fortune:
                return $"Tenés: +{FortuneBonus:0}% fortuna (tope 17%) — {(helps ? "se suma" : Glossary.AtCapSentence)}";
            case UpgradeType.Ricochet:
                return $"Tenés: {RicochetCount} rebotes (tope 4) — {(helps ? "se suma" : Glossary.AtCapSentence)}";
            case UpgradeType.Thorns:
                return ThornsDamage > 0f
                    ? $"Tenés: {ThornsDamage:0} daño de escudo voltáico — {(helps ? "mejora" : "no mejora (ya tenés igual o mejor)")}"
                    : "No tenés escudo voltáico todavía";
            default:
                return null;
        }
    }

    // Non-mutating version of ApplyFlatStack: what the accumulated total would become if this pick
    // were applied, without actually recording it. Used for the "would this help" preview.
    private float PreviewFlatBonus(UpgradeData upgrade) =>
        _stackTotals.GetValueOrDefault(upgrade.Type, 0f) + upgrade.Value;

    // Adds this pick's Value to its running total for the type — any tier, always summed, same shape
    // as Side Shot's ExtraFiringLines — and returns the new total. FireRange/FireRate/BulletDamage
    // only; BulletKnockback still wants ApplyTieredStack below.
    private float ApplyFlatStack(UpgradeData upgrade)
    {
        float total = _stackTotals.GetValueOrDefault(upgrade.Type, 0f) + upgrade.Value;
        _stackTotals[upgrade.Type] = total;
        return total;
    }

    // Non-mutating version of ApplyTieredStack: what the winning bucket total would become if this
    // pick were applied, without actually recording it. Used for the "would this help" preview.
    private float PreviewTieredBonus(UpgradeData upgrade)
    {
        float best = upgrade.Value;
        if (_tierStackTotals.TryGetValue(upgrade.Type, out var tierTotals))
        {
            best = tierTotals.GetValueOrDefault(upgrade.Tier, 0f) + upgrade.Value;
            foreach (var kv in tierTotals)
                if (kv.Key != upgrade.Tier && kv.Value > best) best = kv.Value;
        }
        return best;
    }

    // Adds this pick's Value to its (Type, Tier) bucket, then returns the highest bucket total
    // across all tiers seen so far for that Type — same-tier picks stack, cross-tier ones don't.
    // BulletKnockback only; FireRange/FireRate/BulletDamage use the flat ApplyFlatStack above.
    private float ApplyTieredStack(UpgradeData upgrade)
    {
        if (!_tierStackTotals.TryGetValue(upgrade.Type, out var tierTotals))
        {
            tierTotals = new Dictionary<RewardTier, float>();
            _tierStackTotals[upgrade.Type] = tierTotals;
        }

        tierTotals[upgrade.Tier] = tierTotals.GetValueOrDefault(upgrade.Tier, 0f) + upgrade.Value;

        float best = 0f;
        foreach (var total in tierTotals.Values)
            if (total > best) best = total;

        return best;
    }

    private void RefreshOrbitBlades()
    {
        if (OrbitBladeScene == null) return;

        while (_orbitBlades.Count < OrbitCount)
        {
            var blade = OrbitBladeScene.Instantiate<OrbitShield>();
            blade.AngleOffset = _orbitBlades.Count / (float)OrbitCount * Mathf.Tau;
            AddChild(blade);
            _orbitBlades.Add(blade);
        }
    }

    private void EnsureCompanion()
    {
        if (CompanionScene == null) return;

        if (_companion == null)
        {
            _companion = CompanionScene.Instantiate<Companion>();
            _companion.OwnerPlayer = this;
            _companion.BulletScene = BulletScene;
            _companion.Position = new Vector2(-36f, -36f);
            AddChild(_companion);
        }

        _companion.StatPercent = CompanionStatPercent;

        // Legendary Drone's qualitative upgrade: a second drone, mirrored on the other side, rather
        // than a bigger number — same "top tier changes the mechanism, not just the magnitude"
        // pattern as Laser's tier 3+ behavior and Movement Speed's Legendary trail. Gated the same
        // way those are: the best tier ever owned for this type, not a cached flag.
        if (_companion2 == null
            && _ownedTiers.TryGetValue(UpgradeType.Companion, out var companionTier)
            && companionTier == RewardTier.Legendary)
        {
            _companion2 = CompanionScene.Instantiate<Companion>();
            _companion2.OwnerPlayer = this;
            _companion2.BulletScene = BulletScene;
            _companion2.Position = new Vector2(36f, -36f);
            AddChild(_companion2);
        }

        if (_companion2 != null) _companion2.StatPercent = CompanionStatPercent;
    }
}
