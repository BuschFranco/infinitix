namespace ShooterLoop;

// The boss-round encounter: a big, slow, very tanky melee threat that also plinks away with a
// low-cadence ranged attack (inherited unchanged from ShooterEnemy — the fire timer keeps ticking
// no matter what movement pattern below is doing). On top of that baseline, each boss instance
// rolls ONE movement pattern once at spawn and commits to it for its whole life, so different boss
// rounds actually read as different encounters instead of every boss being the same straight-line
// chaser. See docs/enemies.md for the full breakdown of what each pattern does and why.
//
// On top of the movement pattern, every boss ALSO rolls a random fan of AttackAbility instances
// (see the Attack abilities region below) — a second, independent axis of variety layered on the
// same "roll once, commit for the fight" philosophy. At 50% HP it additionally rolls one random
// Phase2 effect, once — see TryTriggerPhase2.
public partial class Boss : ShooterEnemy
{
    private enum Pattern { Chase, Charge, Lunge }
    private enum Phase { Idle, Windup, Execute, Cooldown }

    // Own RNG, own timer — matches the "every class rolls its own randomness" convention used by
    // Enemy/EnemySpawner rather than sharing Enemy's private `_rng`.
    private readonly Random _rng = new();
    private Pattern _pattern;
    private Phase _phase = Phase.Idle;
    private Timer _phaseTimer;

    // Idle/Cooldown sub-phases look identical to plain Chase, so they're kept short relative to
    // the special sub-phase itself — otherwise the pattern spends most of its time looking like it
    // isn't doing anything special at all, which read as "the boss has no special moves" during
    // testing even though the state machine was running correctly.
    private const float ChargeIdleDuration = 1.1f;
    private const float ChargeWindupDuration = 0.45f;
    private const float ChargeExecuteDuration = 0.7f;
    private const float ChargeCooldownDuration = 1.5f;
    private const float ChargeSpeedMultiplier = 6f;

    // How far the Execute phase actually carries the boss, used to probe for obstacles before
    // committing. Derived rather than hardcoded so retuning the two constants above can't silently
    // desync it from the real dash length.
    private float ChargeDashDistance => MoveSpeed * ChargeSpeedMultiplier * ChargeExecuteDuration;

    // Lunge: a short, sharp burst that only triggers once the boss has actually closed to melee range —
    // it's the "catch up and land the hit" reaction a slow boss needs, rather than a long-range opener
    // like Charge. Unlike Charge, Idle isn't timer-driven for this pattern; every physics frame checks
    // the live distance to the player and fires the windup the instant it closes inside LungeTriggerRadius.
    private const float LungeTriggerRadius = 190f;
    private const float LungeWindupDuration = 0.22f;
    private const float LungeExecuteDuration = 0.42f;
    private const float LungeCooldownDuration = 1.3f;
    private const float LungeSpeedMultiplier = 5f;

    // Shared by Charge and Lunge — only one pattern is ever active per boss instance, so both can use
    // the same "direction locked at windup-end" field without stepping on each other.
    private Vector2 _dashDirection = Vector2.Right;
    private Tween _telegraphTween;
    private static readonly Color TelegraphTint = Palette.Warning;

    // The scene's own default is the generic boss.png hexagon (a placeholder-but-real silhouette,
    // same "armoured" shape language as Tank -- see tools/gen_sprites.py). The coworker photo only
    // replaces it once the "MDG" secret code has actually been redeemed, same gate CharacterCatalog
    // already uses to hide that whole joke roster (GameManager.CoworkerRosterUnlocked).
    private static readonly Texture2D CoworkerPortrait = GD.Load<Texture2D>("res://Assets/Sprites/Characters/conrado.png");
    private static readonly Vector2 CoworkerPortraitScale = new(0.7f, 0.7f);

    // Past round 5 the player's own build outpaces the boss (see EnemySpawner's shared HpMultCurve,
    // which trash mobs get too) enough that boss fights stop being a real threat. From round 10 on,
    // the boss additionally gets tougher, rolls more of its attack pool at once, and its two direct
    // hits start costing 2 hearts instead of 1 — see below, HeavyHitCost and ComputeAbilityCount.
    private const int HeavyScalingStartRound = 10;

    // Extra multiplier ON TOP OF the curve EnemySpawner already applies to every enemy alike — 1x
    // (no-op) before round 10, growing to a 3x extra by round 24+.
    private static readonly RoundCurve BossLateHpCurve = new(1f, 0.15f, 1f, 3f);

    public override void _Ready()
    {
        // Before base._Ready(): that's where Enemy._Ready sets CurrentHp = MaxHp, same ordering
        // requirement as the coworker-portrait swap below — anything that touches MaxHp has to land
        // before it, or the boss ends up under-healed relative to its own max.
        int round = GameManager.Instance?.RoundNumber ?? 1;
        if (round >= HeavyScalingStartRound)
            MaxHp = Mathf.RoundToInt(MaxHp * BossLateHpCurve.Evaluate(round - HeavyScalingStartRound + 1));

        // Has to happen BEFORE base._Ready(): that's where Enemy caches _visualBaseScale from
        // Visual's current Scale (used to reset the sprite's size after every telegraph/spawn
        // animation). Swapping the texture/scale afterward left that cache pointing at the scene's
        // default (boss.png at 2,2) while Visual itself sat at the photo's 0.7,0.7 -- so the very
        // next telegraph or spawn animation snapped it back to roughly triple size instead of
        // restoring it, which is why the boss looked gigantic once "MDG" was redeemed.
        if (GameManager.Instance.CoworkerRosterUnlocked)
        {
            var visual = GetNode<Sprite2D>("Visual");
            visual.Texture = CoworkerPortrait;
            visual.Scale = CoworkerPortraitScale;
        }

        base._Ready();

        // Chase (the plain baseline behavior, unchanged) is a real possible outcome of the roll —
        // not every boss should feel gimmicked, and it keeps Charge/Lunge from feeling like "the"
        // boss pattern rather than one of a few.
        _pattern = (Pattern)_rng.Next(3);
        if (_pattern != Pattern.Chase)
        {
            _phaseTimer = new Timer { OneShot = true };
            AddChild(_phaseTimer);
            _phaseTimer.Timeout += OnPhaseTimeout;

            // Charge's Idle is a fixed delay before its next windup. Lunge's Idle has no timer at
            // all — it just sits in Phase.Idle until ComputeLungeMoveDir's per-frame proximity
            // check fires it.
            if (_pattern == Pattern.Charge)
                EnterPhase(Phase.Idle, ChargeIdleDuration);
        }

        RollAbilities();
    }

    private void EnterPhase(Phase phase, float duration)
    {
        _phase = phase;
        _phaseTimer.WaitTime = duration;
        _phaseTimer.Start();
    }

    private void OnPhaseTimeout()
    {
        if (_pattern == Pattern.Charge) AdvanceChargePhase();
        else AdvanceLungePhase();
    }

    private void AdvanceChargePhase()
    {
        switch (_phase)
        {
            case Phase.Idle:
                EnterPhase(Phase.Windup, ChargeWindupDuration);
                PlayTelegraph(ChargeWindupDuration);
                SpawnFloatingLabel("¡EMBESTIDA!", TelegraphTint, 22, new Vector2(-50f, HealthBarOffset - 30f));
                break;
            case Phase.Windup:
                StartCharge();
                break;
            case Phase.Execute:
                EnterPhase(Phase.Cooldown, ChargeCooldownDuration);
                break;
            case Phase.Cooldown:
                EnterPhase(Phase.Idle, ChargeIdleDuration);
                break;
        }
    }

    // Locks the dash direction at the moment the windup ends (not re-aimed mid-dash), and bails
    // into Cooldown instead of dashing if the straight line ahead is blocked — a committed lunge
    // shouldn't clip through an obstacle just because the telegraph already played.
    private void StartCharge()
    {
        // Probes the dash's own full travel distance, not the shorter steering probe — see the
        // IsPathBlocked overload in Enemy.cs for why those are deliberately different numbers.
        if (!EnsurePlayer() || IsPathBlocked(ChaseDirection(), ChargeDashDistance))
        {
            EnterPhase(Phase.Cooldown, ChargeCooldownDuration);
            return;
        }

        _dashDirection = ChaseDirection();
        EnterPhase(Phase.Execute, ChargeExecuteDuration);
    }

    private void AdvanceLungePhase()
    {
        switch (_phase)
        {
            case Phase.Windup:
                StartLunge();
                break;
            case Phase.Execute:
                EnterPhase(Phase.Cooldown, LungeCooldownDuration);
                break;
            case Phase.Cooldown:
                // No timer to start here — Idle just sits and waits for the next proximity check to
                // trip it, same as it did before this pattern ever triggered.
                _phase = Phase.Idle;
                break;
        }
    }

    // Checked every physics frame while Idle (see ComputeLungeMoveDir) rather than on a fixed delay:
    // the whole point of this pattern is reacting to the player actually being close, not to a clock.
    private void TryTriggerLunge()
    {
        if (!EnsurePlayer()) return;
        if (GlobalPosition.DistanceSquaredTo(PlayerNode.GlobalPosition) > LungeTriggerRadius * LungeTriggerRadius) return;

        EnterPhase(Phase.Windup, LungeWindupDuration);
        PlayTelegraph(LungeWindupDuration);
        SpawnFloatingLabel("¡ACELERÓN!", TelegraphTint, 22, new Vector2(-50f, HealthBarOffset - 30f));
    }

    private void StartLunge()
    {
        if (!EnsurePlayer())
        {
            EnterPhase(Phase.Cooldown, LungeCooldownDuration);
            return;
        }

        _dashDirection = ChaseDirection();
        EnterPhase(Phase.Execute, LungeExecuteDuration);
    }

    // Fair warning before the dash: a warm tint ramping in plus a slow scale pulse, so a player
    // watching the boss actually sees the tell.
    //
    // Both properties are borrowed, not owned: Modulate holds the boss's identity colour and Scale
    // holds its on-screen size, so ResetTelegraph has to hand them back (see VisualBaseColor /
    // VisualBaseScale on Enemy). Pulsing to a literal Vector2.One instead of off the base scale is
    // what used to inflate the boss to its texture's full resolution mid-windup.
    private void PlayTelegraph(float duration)
    {
        if (Visual == null) return;

        _telegraphTween?.Kill();
        _telegraphTween = CreateTween();
        _telegraphTween.SetParallel(true);
        _telegraphTween.TweenProperty(Visual, "modulate", TelegraphTint, duration * 0.6f);
        _telegraphTween.TweenProperty(Visual, "scale", VisualBaseScale * 1.25f, duration * 0.5f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    // Restores both properties the telegraph borrowed. Modulate must go back to VisualBaseColor,
    // not to Colors.White: the scene's Modulate is the boss's identity colour, so resetting to
    // white left it a plain white shape for the rest of the fight after its very first dash.
    private void ResetTelegraph()
    {
        _telegraphTween?.Kill();
        if (Visual == null) return;

        Visual.Modulate = VisualBaseColor;
        Visual.Scale = VisualBaseScale;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!EnsurePlayer()) return;

        // Same burn-then-death guard as Enemy._PhysicsProcess (TickBurn/CurrentHp are shared with
        // the base class; this override replaces the movement half only, not the whole method).
        TickBurn((float)delta);
        if (CurrentHp <= 0) return;

        TryTriggerPhase2();

        if (_spinLaserExecuting)
            AdvanceSpinLaser((float)delta);

        // Channeling an attack ability freezes movement entirely, same reasoning as Charge/Lunge's
        // own Windup freeze: a boss that keeps chasing while unleashing a telegraphed attack
        // undercuts the "get ready to dodge" read this whole system is built around.
        Vector2 moveDir = _channeling
            ? Vector2.Zero
            : _pattern switch
            {
                Pattern.Charge => ComputeChargeMoveDir(),
                Pattern.Lunge => ComputeLungeMoveDir(),
                _ => ComputeAvoidanceDirection(ChaseDirection()),
            };

        FinishMovement(moveDir, delta);
    }

    private Vector2 ComputeChargeMoveDir()
    {
        if (_phase == Phase.Execute)
        {
            // A committed lunge doesn't politely detour around obstacles it meets mid-dash — it
            // already checked the way was clear at windup-end.
            ResetTelegraph();
            return _dashDirection * ChargeSpeedMultiplier;
        }

        if (_phase != Phase.Windup) return ComputeAvoidanceDirection(ChaseDirection());

        // Frozen in place during the telegraph — a boss that keeps chasing while flashing a
        // warning undercuts the "get ready to dodge" read.
        return Vector2.Zero;
    }

    private Vector2 ComputeLungeMoveDir()
    {
        // Checked before the phase switch below so a trigger this frame freezes immediately instead
        // of taking one more frame of normal chase before the windup visibly starts.
        if (_phase == Phase.Idle) TryTriggerLunge();

        return _phase switch
        {
            Phase.Execute => LungeBurst(),
            Phase.Windup => Vector2.Zero,
            _ => ComputeAvoidanceDirection(ChaseDirection()),
        };
    }

    private Vector2 LungeBurst()
    {
        ResetTelegraph();
        return _dashDirection * LungeSpeedMultiplier;
    }

    // --- Attack abilities ---
    //
    // A second, independent axis of variety on top of the movement Pattern above: every boss rolls
    // AbilityCount distinct AttackAbility values from the full pool at spawn and commits to that
    // fan for its whole life, same "roll once" philosophy as Pattern. Each active ability runs on
    // its own repeating Timer; the ones NOT rolled simply never get a timer and never fire.
    //
    // Every ability follows the same Windup-then-Execute shape as Charge/Lunge (telegraph tint +
    // floating label, then the actual effect after a beat), and _channeling gates movement AND
    // every other ability from firing at the same time — only one "special thing" is ever visible
    // at once, which is what keeps every telegraph readable.
    private enum AttackAbility { FanBarrage, MeteorRain, ShockwaveRing, MineDrop, SummonAdds, SpinLaser }

    private const float AbilityInitialDelayBase = 3.5f;
    private const float AbilityInitialDelayStagger = 2.5f;
    private const float AbilityRetryDelay = 0.6f;

    private static readonly AttackAbility[] AllAbilities = Enum.GetValues<AttackAbility>();

    private static readonly Dictionary<AttackAbility, string> AbilityLabels = new()
    {
        [AttackAbility.FanBarrage] = "¡RÁFAGA!",
        [AttackAbility.MeteorRain] = "¡LLUVIA DE METEOROS!",
        [AttackAbility.ShockwaveRing] = "¡ONDA DEVASTADORA!",
        [AttackAbility.MineDrop] = "¡CAMPO MINADO!",
        [AttackAbility.SummonAdds] = "¡REFUERZOS!",
        [AttackAbility.SpinLaser] = "¡RAYO GIRATORIO!",
    };

    private static readonly Dictionary<AttackAbility, float> AbilityCooldowns = new()
    {
        [AttackAbility.FanBarrage] = 6.5f,
        [AttackAbility.MeteorRain] = 9.5f,
        [AttackAbility.ShockwaveRing] = 8.5f,
        [AttackAbility.MineDrop] = 11f,
        [AttackAbility.SummonAdds] = 15f,
        [AttackAbility.SpinLaser] = 12f,
    };

    private static readonly Dictionary<AttackAbility, float> AbilityWindups = new()
    {
        [AttackAbility.FanBarrage] = 0.4f,
        [AttackAbility.MeteorRain] = 0.5f,
        [AttackAbility.ShockwaveRing] = 0.9f,
        [AttackAbility.MineDrop] = 0.4f,
        [AttackAbility.SummonAdds] = 0.4f,
        [AttackAbility.SpinLaser] = 0.6f,
    };

    private readonly HashSet<AttackAbility> _activeAbilities = new();
    private readonly Dictionary<AttackAbility, Timer> _abilityTimers = new();
    private bool _channeling;

    // Baseline 2, same as always, up to round 9. From round 10 on, one more slot opens every 5
    // rounds (10, 15, 20...) — the same cadence as boss rounds themselves — capped at the full pool
    // so a round-25+ boss just runs every ability it has.
    private static int ComputeAbilityCount(int round) =>
        round < HeavyScalingStartRound
            ? 2
            : Mathf.Min(AllAbilities.Length, 2 + (round - HeavyScalingStartRound) / 5 + 1);

    private void RollAbilities()
    {
        var pool = new List<AttackAbility>(AllAbilities);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int round = GameManager.Instance?.RoundNumber ?? 1;
        int count = ComputeAbilityCount(round);
        for (int i = 0; i < Mathf.Min(count, pool.Count); i++)
            ActivateAbility(pool[i], AbilityInitialDelayBase + i * AbilityInitialDelayStagger);
    }

    private void ActivateAbility(AttackAbility ability, float initialDelay)
    {
        if (_activeAbilities.Contains(ability)) return;
        _activeAbilities.Add(ability);

        var timer = new Timer { OneShot = true };
        AddChild(timer);
        timer.Timeout += () => BeginAbility(ability);
        _abilityTimers[ability] = timer;

        timer.WaitTime = initialDelay;
        timer.Start();
    }

    private void RestartAbilityTimer(AttackAbility ability, float delay)
    {
        if (_abilityTimers.TryGetValue(ability, out var timer))
        {
            timer.WaitTime = Mathf.Max(0.05f, delay);
            timer.Start();
        }
    }

    private void BeginAbility(AttackAbility ability)
    {
        // Busy guard: if the boss is already channeling another ability, or mid-dash on its
        // movement pattern, defer briefly rather than firing on top of it — self-healing, no
        // queue to manage, and it keeps two telegraphs from ever overlapping on screen.
        bool patternBusy = _pattern != Pattern.Chase && _phase != Phase.Idle;
        if (_channeling || patternBusy || !EnsurePlayer())
        {
            RestartAbilityTimer(ability, AbilityRetryDelay);
            return;
        }

        _channeling = true;
        float windup = AbilityWindups[ability];
        PlayTelegraph(windup);
        SpawnFloatingLabel(AbilityLabels[ability], TelegraphTint, 22, new Vector2(-70f, HealthBarOffset - 30f));

        var windupTimer = GetTree().CreateTimer(windup);
        windupTimer.Timeout += () => ExecuteAbility(ability);
    }

    private void ExecuteAbility(AttackAbility ability)
    {
        if (!IsInstanceValid(this) || CurrentHp <= 0) return;   // died mid-windup

        ResetTelegraph();

        // SpinLaser is channeled (it runs for a duration via AdvanceSpinLaser, ticked from
        // _PhysicsProcess) rather than instantaneous — it clears _channeling and restarts its own
        // cooldown timer itself once the sweep finishes, so it returns early here instead of
        // falling through to the shared instantaneous-ability cleanup below.
        if (ability == AttackAbility.SpinLaser)
        {
            BeginSpinLaserExecute();
            return;
        }

        switch (ability)
        {
            case AttackAbility.FanBarrage: ExecuteFanBarrage(); break;
            case AttackAbility.MeteorRain: ExecuteMeteorRain(); break;
            case AttackAbility.ShockwaveRing: ExecuteShockwaveRing(); break;
            case AttackAbility.MineDrop: ExecuteMineDrop(); break;
            case AttackAbility.SummonAdds: ExecuteSummonAdds(); break;
        }

        _channeling = false;
        RestartAbilityTimer(ability, AbilityCooldowns[ability]);
    }

    // Five bullets fanned around the aimed direction rather than one — a burst attack distinct
    // from the baseline single-target fire the boss already ticks on its own timer.
    private const int FanBarrageBulletCount = 5;
    private const float FanBarrageSpreadDegrees = 16f;

    private void ExecuteFanBarrage()
    {
        if (EnemyBulletScene == null || !EnsurePlayer()) return;

        Vector2 baseDir = (PlayerNode.GlobalPosition - GlobalPosition).Normalized();
        int half = FanBarrageBulletCount / 2;
        for (int i = -half; i <= half; i++)
        {
            var bullet = EnemyBulletScene.Instantiate<EnemyBullet>();
            bullet.GlobalPosition = GlobalPosition;
            bullet.Direction = baseDir.Rotated(Mathf.DegToRad(FanBarrageSpreadDegrees * i));
            GetParent().AddChild(bullet);
        }
    }

    // Scattered around the PLAYER (not the boss) so it's a real positioning threat rather than
    // guaranteed to land on empty ground — reuses MissileZone.cs verbatim (fill-then-detonate
    // telegraph), the exact same class the Missile Strike round event already spawns.
    private const int MeteorCount = 3;
    private const float MeteorScatterRadius = 150f;

    private void ExecuteMeteorRain()
    {
        if (!EnsurePlayer()) return;
        if (GetTree().CurrentScene is not Node2D parent) return;

        for (int i = 0; i < MeteorCount; i++)
        {
            float angle = (float)(_rng.NextDouble() * Mathf.Tau);
            float dist = (float)(_rng.NextDouble() * MeteorScatterRadius);
            var zone = new MissileZone();
            zone.AddToGroup("round_event_hazards");   // swept for free by RoundEventDirector.EndActiveEvent at round end
            parent.AddChild(zone);
            zone.GlobalPosition = PlayerNode.GlobalPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
        }
    }

    // A single big ring centered on the boss itself — the "don't stand next to me" answer to a
    // player who just closes distance and facetanks. Long windup (0.9s) since it's otherwise
    // undodgeable once it pops.
    private const float ShockwaveRadius = 190f;

    // Round-10+ punish for the two abilities that hit the player directly rather than through a
    // dodgeable projectile/hazard — both already carry a long, readable telegraph, so making them
    // cost more keeps the boss dangerous without punishing a hit the player had no way to see coming.
    private int HeavyHitCost() =>
        (GameManager.Instance?.RoundNumber ?? 1) >= HeavyScalingStartRound ? 2 : 1;

    private void ExecuteShockwaveRing()
    {
        if (EnsurePlayer() && GlobalPosition.DistanceTo(PlayerNode.GlobalPosition) <= ShockwaveRadius
            && PlayerNode is Player player)
        {
            player.TakeHit(GlobalPosition, forcedCost: HeavyHitCost());
        }

        SpawnShockwaveVisual();
    }

    // This used to be the one copy of the shared blast pattern that never set its starting Scale, so
    // it popped in at full size and its expand tween — animating toward a Vector2.One it was already
    // at — did nothing. The shockwave now actually rolls outward, which is what a shockwave has to do
    // to read as one you should be running away from.
    private void SpawnShockwaveVisual() =>
        Juice.Blast(GetParent(), GlobalPosition, ShockwaveRadius, Palette.EnemyBullet,
            growTime: 0.3f, fadeTime: 0.4f);

    // Scattered around the player like MeteorRain, but with a minimum clearance — a mine dropped
    // directly underfoot would be undodgeable, the whole point of Mine.cs's pulse is to bait a step
    // away from it. Reuses Scripts/Hazards/Mine.cs verbatim (same class the Campo Minado round
    // event seeds the whole arena with).
    private const int MineDropCount = 3;
    private const float MineDropClearance = 90f;
    private const float MineDropScatterRadius = 200f;

    private void ExecuteMineDrop()
    {
        if (!EnsurePlayer()) return;
        if (GetTree().CurrentScene is not Node2D parent) return;

        for (int i = 0; i < MineDropCount; i++)
        {
            float angle = (float)(_rng.NextDouble() * Mathf.Tau);
            float dist = MineDropClearance + (float)(_rng.NextDouble() * (MineDropScatterRadius - MineDropClearance));
            var mine = new Mine();
            mine.AddToGroup("round_event_hazards");
            parent.AddChild(mine);
            mine.GlobalPosition = PlayerNode.GlobalPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
        }
    }

    // Deliberately unscaled (round-1 Grunt stats) — these are meant as a brief crowding distraction
    // during the boss fight, not full-strength threats. Loaded once via GD.Load, same convention
    // Enemy.cs already uses for its own pickup scenes.
    private static readonly PackedScene GruntScene = GD.Load<PackedScene>("res://Scenes/Enemy/EnemyGrunt.tscn");
    private const int SummonAddsCount = 2;
    private const float SummonAddsScatter = 60f;

    private void ExecuteSummonAdds()
    {
        var parent = GetParent();
        if (parent == null || GruntScene == null) return;

        for (int i = 0; i < SummonAddsCount; i++)
        {
            float angle = (float)(_rng.NextDouble() * Mathf.Tau);
            var grunt = GruntScene.Instantiate<Enemy>();
            grunt.SelfScene = GruntScene;
            parent.AddChild(grunt);
            grunt.GlobalPosition = GlobalPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * SummonAddsScatter;
        }
    }

    // A rotating beam sweeping around the boss for its full Execute duration — the one ability
    // that's channeled over time rather than instantaneous, so it gets its own per-frame advance
    // (AdvanceSpinLaser, ticked from _PhysicsProcess) instead of firing-and-forgetting inside
    // ExecuteAbility like every other ability above.
    private Line2D _spinBeam;
    private bool _spinLaserExecuting;
    private bool _spinLaserHitPlayer;
    private float _spinLaserAngle;
    private float _spinLaserElapsed;
    private const float SpinLaserRadius = 260f;
    private const float SpinLaserDuration = 2.4f;
    private const float SpinLaserRotations = 1.5f;
    private const float SpinLaserHitToleranceDegrees = 9f;

    private void BeginSpinLaserExecute()
    {
        if (_spinBeam == null)
        {
            _spinBeam = new Line2D();
            _spinBeam.Width = 6f;
            _spinBeam.DefaultColor = Palette.EnemyBullet;
            _spinBeam.AddPoint(Vector2.Zero);
            _spinBeam.AddPoint(new Vector2(SpinLaserRadius, 0f));
            AddChild(_spinBeam);
        }

        _spinBeam.Visible = true;
        _spinBeam.Rotation = 0f;
        _spinLaserAngle = 0f;
        _spinLaserElapsed = 0f;
        _spinLaserHitPlayer = false;
        _spinLaserExecuting = true;
    }

    private void AdvanceSpinLaser(float delta)
    {
        _spinLaserElapsed += delta;
        float angularSpeed = Mathf.Tau * SpinLaserRotations / SpinLaserDuration;
        _spinLaserAngle += angularSpeed * delta;
        _spinBeam.Rotation = _spinLaserAngle;

        // Boss.Rotation itself never changes (enemies don't turn to face anything — see
        // Enemy.cs), so the beam's world-space angle is exactly _spinLaserAngle with no offset.
        if (!_spinLaserHitPlayer && EnsurePlayer())
        {
            Vector2 toPlayer = PlayerNode.GlobalPosition - GlobalPosition;
            if (toPlayer.Length() <= SpinLaserRadius)
            {
                float diff = Mathf.Wrap(_spinLaserAngle - toPlayer.Angle(), -Mathf.Pi, Mathf.Pi);
                if (Mathf.Abs(diff) <= Mathf.DegToRad(SpinLaserHitToleranceDegrees) && PlayerNode is Player player)
                {
                    _spinLaserHitPlayer = true;
                    player.TakeHit(GlobalPosition, forcedCost: HeavyHitCost());
                }
            }
        }

        if (_spinLaserElapsed < SpinLaserDuration) return;

        _spinLaserExecuting = false;
        _spinBeam.Visible = false;
        _channeling = false;
        RestartAbilityTimer(AttackAbility.SpinLaser, AbilityCooldowns[AttackAbility.SpinLaser]);
    }

    // --- Phase 2 ---
    //
    // A one-time, randomly-chosen shakeup at 50% HP — literally "another ability" per the design
    // brief: it plays the same telegraph+announcement beat as every attack ability above, then
    // permanently changes something about the fight rather than dealing a one-off hit.
    private bool _phase2Triggered;
    private const float Phase2SpeedMultiplier = 1.35f;
    private const float Phase2CooldownMultiplier = 0.6f;
    private const int Phase2EffectCount = 3;

    private void TryTriggerPhase2()
    {
        if (_phase2Triggered || CurrentHp * 2 > MaxHp) return;
        _phase2Triggered = true;

        PlayTelegraph(1.2f);
        SpawnFloatingLabel("¡FURIA!", Palette.Warning, 26, new Vector2(-40f, HealthBarOffset - 46f));
        if (GetTree().GetFirstNodeInGroup("danger_director") is DangerDirector director)
            director.AnnounceThreat("¡EL JEFE SE ENFURECE!");

        switch (_rng.Next(Phase2EffectCount))
        {
            case 0: ApplyPhase2Frenzy(); break;
            case 1: ApplyPhase2ExtraAbility(); break;
            default: ExecuteSummonAdds(); break;   // Oleada de Refuerzos: an immediate wave, no telegraph of its own
        }
    }

    // Speeds the boss up and halves every currently active ability's cooldown. The halving only
    // takes effect from each ability's NEXT Start() call — Godot's Timer.WaitTime doesn't retroactively
    // shrink a countdown already in flight — which in practice means "abilities start coming twice as
    // often starting from about now," close enough for an enrage effect that isn't meant to be exact.
    private void ApplyPhase2Frenzy()
    {
        MoveSpeed *= Phase2SpeedMultiplier;
        foreach (var timer in _abilityTimers.Values)
            timer.WaitTime = Mathf.Max(0.05f, timer.WaitTime * Phase2CooldownMultiplier);
    }

    // Grants one more ability the boss didn't already roll — as close to "gains an ability" as this
    // system can express. Falls back to the Frenzy effect if every ability is somehow already active
    // (AbilityCount would have to exceed the pool size for that to happen).
    private void ApplyPhase2ExtraAbility()
    {
        var candidates = new List<AttackAbility>();
        foreach (var ability in AllAbilities)
            if (!_activeAbilities.Contains(ability)) candidates.Add(ability);

        if (candidates.Count == 0)
        {
            ApplyPhase2Frenzy();
            return;
        }

        ActivateAbility(candidates[_rng.Next(candidates.Count)], 1.5f);
    }
}
