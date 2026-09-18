namespace ShooterLoop;

public partial class EnemySpawner : Node2D
{
    [Export] public PackedScene EnemyScene;
    [Export] public PackedScene TankScene;
    [Export] public PackedScene SpeedyScene;
    [Export] public PackedScene SplitterScene;
    [Export] public PackedScene ShooterScene;
    [Export] public PackedScene RareScene;
    [Export] public PackedScene HiddenScene;
    [Export] public PackedScene BossScene;
    [Export] public PackedScene DemonScene;
    [Export] public PackedScene DemonBruteScene;
    [Export] public PackedScene DemonStalkerScene;
    // Must stay outside the visible area or enemies pop in on screen. At the 1152x648 viewport
    // with the camera's 0.85 zoom the visible half-diagonal is ~780px, so this leaves a margin.
    [Export] public float SpawnRadius = 900f;

    // All difficulty knobs are percentage curves off round 1 baseline: Evaluate(round) = Base + (round-1)*PerRound, clamped [Min, Max].
    // Tune these to change pacing without touching per-enemy nominal stats anywhere else.
    private static readonly RoundCurve SpawnIntervalCurve = new(0.6f, -0.025f, 0.12f, 0.6f);   // seconds between spawn ticks (lower = faster)
    private static readonly RoundCurve BurstCountCurve = new(1f, 0.28f, 1f, 8f);                // enemies spawned per tick
    private static readonly RoundCurve MaxConcurrentCurve = new(25f, 3f, 25f, 160f);           // cap on enemies alive at once

    // Late-game crowd TAPER on top of MaxConcurrentCurve: flat 1.0x through round 10, then a linear
    // ramp DOWN to 0.66x by round 18, held from there. The odd-looking Base of 1.3825 is what makes
    // the clamps land on exactly those rounds — Evaluate is Base + (round-1)*PerRound clamped to
    // [Min, Max], so it reads 1.0 at round 10, 0.8725 at 13, and pins to 0.66 from 18 on.
    //
    // This used to be a crowd *bump* (1.0x -> 1.2x over rounds 10-13, i.e. deliberately MORE bodies
    // on screen late). It was reversed because rounds 10-18 are exactly where CommonSuppressionCurve
    // below swaps Grunts (30 HP) for Specials (avg ~82 HP weighted), so the same threat now needs far
    // fewer bodies — and the old bump was stacking extra count on top of a mix that was already
    // getting heavier, which is what made the late game read as saturating rather than hard.
    //
    // Holding at 0.66 rather than tapering forever matters: MaxConcurrentCurve keeps adding +4/round
    // underneath, so once the taper bottoms out at round 18 (the same round commons hit zero) the
    // count naturally resumes climbing again, with no separate re-climb mechanism needed.
    //
    // The taper used to stay clamped at its Max (1.0, i.e. no relief at all) through round 10, only
    // starting to bite at round 11 — which left rounds 6-10 with nothing easing the crowd while
    // MaxConcurrentCurve/BurstCountCurve/SpawnIntervalCurve were all still climbing hard and the
    // rounds 1-3 onboarding cushion had long since worn off. Base/PerRound moved so the decline
    // starts at round 5 (round 6 is the first round that actually sees relief) instead of round 10,
    // reaching the same 0.66 floor about a round earlier as a side effect — late game is
    // essentially untouched, the fix is entirely in the round 6-11 window.
    private static readonly RoundCurve LateCrowdMultCurve = new(1.112f, -0.028f, 0.66f, 1f);
    private static readonly RoundCurve SpecialChanceCurve = new(0.05f, 0.05f, 0.05f, 0.6f);    // chance a spawn is a special enemy
    private static readonly RoundCurve RareChanceCurve = new(0.15f, 0.02f, 0.15f, 0.35f);      // chance a spawn (that isn't hidden/special) is rare
    private const float HiddenChance = 0.05f;                                                   // flat 5% per user request, not round-scaled

    // Fraction of would-be Grunt spawns promoted to a Special instead: 0 through round 9, ramping to
    // 1.0 (no Grunts at all) by round 18. Same derived-Base trick as LateCrowdMultCurve above — the
    // Base of -1.125 is what pins the Min clamp to round 10 and the Max clamp to round 18.
    //
    // Why this exists as its own knob instead of just raising SpecialChanceCurve's Max to 1.0: the
    // rolls in ChooseEnemyScene are *sequential*, so Rare sits downstream of Special. Driving
    // _specialChance to 1.0 would zero out the Rare branch as collateral damage, and Rare is meant
    // to survive as the late-game mid-tier. Suppressing the Common fallback instead leaves Rare's
    // share completely untouched (it holds at ~13.3% from round 12 on).
    private static readonly RoundCurve CommonSuppressionCurve = new(-1.125f, 0.125f, 0f, 1f);

    // Demons are the late-game escalation: nothing through round 14, then a ramp that reaches 65% of ALL
    // spawns by round 25 and holds there. Rolled first in ChooseEnemyScene so the share is exact and the
    // remainder is what's left over for everyone else — they're meant to crowd out the existing roster,
    // not sit alongside it. Base is derived so the Min clamp releases at round 15.
    private static readonly RoundCurve DemonShareCurve = new(-0.91f, 0.065f, 0f, 0.65f);
    private float _demonShare;

    // The Brute is the rarest of the three: at 400 HP a field full of them would grind rather than
    // threaten. The Stalker is the ambusher, so it stays a spike rather than the norm.
    private const int DemonWeight = 55;
    private const int DemonBruteWeight = 20;
    private const int DemonStalkerWeight = 25;
    // Onboarding cushion for rounds 1-2, indexed by round number. A linear RoundCurve can't express
    // "extra gentle for the first couple of rounds, then rejoin the normal ramp" — that shape is
    // piecewise by definition, and re-sloping the curves to soften the opening would drag every
    // later round along with it. Explicit per-round multipliers instead, tapering to 1x by round 3
    // where the normal curves take over untouched.
    //
    //                                            round 1  round 2  round 3
    private static readonly float[] EarlyRoundIntervalMult = { 1.60f, 1.25f, 1.10f };   // slower spawn ticks
    private static readonly float[] EarlyRoundConcurrentMult = { 0.40f, 0.60f, 0.80f }; // fewer alive at once (25 -> 10, 29 -> 17, 33 -> 26)
    private static readonly float[] EarlyRoundSpeedMult = { 0.80f, 0.80f, 0.90f };      // enemies at reduced speed

    // Round 1 is nothing but Grunts: at 0 the Hidden/Special/Rare rolls all fail and ChooseEnemyScene
    // falls through to EnemyScene. The opening round is the player's first look at the game and used to
    // throw ~13.5% Rare, 5% Hidden and 5% Specials at them before they'd learned what a Grunt does.
    //
    // Extended past round 3 with a second, gentler dip covering rounds 6-11: by round 6 alone
    // SpecialChanceCurve/RareChanceCurve are already at 30%/25% with nothing easing them (the crowd
    // taper above didn't kick in until round 11 either), so this is the same onboarding-cushion
    // mechanism reused for a second rough patch rather than a new one. Round 12 onward falls through
    // to the table's implicit 1x default, same as round 4/5 already did before this changed.
    //
    //                                     r1  r2  r3  r4  r5   r6    r7    r8   r9   r10   r11
    private static readonly float[] EarlyRoundVarietyMult = { 0f, 1f, 1f, 1f, 1f, 0.75f, 0.65f, 0.6f, 0.6f, 0.65f, 0.75f };

    // Rounds 11-16 relief, by request: the mid-game variety dip above tapers back to full at round
    // 12, and neither the crowd taper (still declining toward its round-18 floor here, not yet
    // biting hard) nor the toughness curves ease at all in this window — so rounds 11-16 stacked full
    // enemy variety on top of a still-climbing crowd cap and steadily-rising HP/damage/speed with
    // nothing easing any of it, right where the earlier dip's own relief had just worn off. Same
    // shape as the two dips above: ease in at 11, bottom out mid-window, ease back out by 16, then
    // rejoin the normal curves untouched at 17 (the table's implicit 1x default beyond its length).
    //
    // Deliberately NOT touching enemy variety this time (Rare/Special/Hidden/Demon share) — only
    // crowd size and per-enemy toughness, per what actually felt too hard here. The two dips
    // therefore have different shapes: crowd eases harder (perceived density is the bigger lever per
    // the late-game composition notes below) than toughness (which compounds across every enemy
    // alive, so a smaller per-enemy cut already adds up).
    //
    //                                      r11    r12    r13    r14    r15    r16
    private static readonly float[] MidGameCrowdMult =
        { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 0.85f, 0.75f, 0.70f, 0.70f, 0.75f, 0.85f };
    private static readonly float[] MidGameToughnessMult =
        { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 0.90f, 0.85f, 0.82f, 0.82f, 0.85f, 0.90f };

    private static float EarlyRoundMult(float[] table, int round)
    {
        int index = round - 1;
        return index >= 0 && index < table.Length ? table[index] : 1f;
    }

    // HP/damage ramp slower so the mid-game doesn't spike: +18% HP and +16% damage per round
    // (was +25%/+22%), topping out at ×8 instead of ×10. Same shape, gentler climb.
    private static readonly RoundCurve HpMultCurve = new(1f, 0.18f, 1f, 8f);
    private static readonly RoundCurve DmgMultCurve = new(1f, 0.16f, 1f, 8f);
    private static readonly RoundCurve SpeedMultCurve = new(1f, 0.05f, 1f, 2f);
    // PerRound raised from 0.25 to 0.35 to compensate the late-game crowd taper above. Coins largely
    // self-correct (GameManager.UpgradeCostMultiplier indexes on current Coins, not on round, so
    // less income also means cheaper prices) but XP has no such damper — with ~46% fewer bodies by
    // round 18, the old rate would have cut XP income ~32%, directly slowing the level-ups that are
    // the game's only free-reward source. This brings the net change to roughly -10%.
    private static readonly RoundCurve RewardMultCurve = new(1f, 0.35f, 1f, 12f);

    private Timer _spawnTimer;
    private Node2D _enemiesContainer;
    private Node2D _player;
    private readonly Random _rng = new();

    private float _specialChance;
    private float _rareChance;
    private float _commonSuppression;

    // Hidden's base rate is a flat const rather than a curve, so it needs its own live field to be
    // reachable by EarlyRoundVarietyMult — otherwise round 1 would still roll 5% Hidden.
    private float _hiddenChance = HiddenChance;

    // When set, OnSpawnTimeout spawns this scene directly instead of rolling ChooseEnemyScene().
    // Boss rounds use it to spawn nothing but Grunts; cleared by ConfigureForRound.
    private PackedScene _forcedSpawnScene;
    private int _maxConcurrentEnemies;
    private int _burstCount = 1;
    private float _hpMult = 1f;
    private float _dmgMult = 1f;
    private float _speedMult = 1f;
    private float _rewardMult = 1f;

    // Last evaluated adaptive-difficulty multiplier — surfaced for the pause menu's stat readout
    // so the system is inspectable rather than invisible.
    public float CatchUpMultiplier => _catchUpMult;
    private float _catchUpMult = 1f;

    public override void _Ready()
    {
        AddToGroup("enemy_spawner");

        _spawnTimer = GetNode<Timer>("SpawnTimer");
        _spawnTimer.Timeout += OnSpawnTimeout;

        _enemiesContainer = GetTree().CurrentScene.GetNode<Node2D>("Enemies");
        _player = GetTree().GetFirstNodeInGroup("player") as Node2D;

        ConfigureForRound(1);

        // This node only exists once Arena.tscn has actually loaded — the one reliable "gameplay
        // genuinely began" signal round 1 has. See GameManager.StartRoundOneTimer for why round 1
        // needs this at all (every later round arms its own timer from StartNextRound instead).
        //
        // The player's very first-ever run gets the controls tutorial first, gating this same
        // trigger — TotalRunsPlayed only increments at run end (RegisterFinalScore), so it's still
        // 0 throughout this, the player's first round 1. Pausing the whole tree (not just delaying
        // this call) matters because ConfigureForRound above already armed _spawnTimer -- a Timer
        // only freezes when the tree itself pauses (see StopSpawning below), so without this,
        // enemies would start spawning behind the tutorial.
        if (GameManager.Instance != null && GameManager.Instance.TotalRunsPlayed == 0)
        {
            GameManager.Instance.Pause();
            var tutorial = GetTree().CurrentScene.GetNode<ControlsTutorial>("ControlsTutorialLayer/ControlsTutorial");
            tutorial.Closed += () =>
            {
                GameManager.Instance.Resume();
                GameManager.Instance.StartRoundOneTimer();
            };
            tutorial.Open();
        }
        else
        {
            GameManager.Instance?.StartRoundOneTimer();
        }
    }

    // A Timer only pauses (freezes its remaining time) when the tree pauses — it doesn't reset.
    // Left running, it can have almost no time left by the time the round-end shop closes and the
    // tree unpauses, firing an immediate spawn burst that ignores the round-start countdown
    // entirely. GameManager.EndRound() calls this so spawning stays off until the countdown
    // elapses and ConfigureForRound/ConfigureForBossRound explicitly restarts it.
    public void StopSpawning() => _spawnTimer.Stop();

    public void ConfigureForRound(int round)
    {
        _spawnTimer.WaitTime = SpawnIntervalCurve.Evaluate(round) * EarlyRoundMult(EarlyRoundIntervalMult, round);
        _burstCount = Mathf.RoundToInt(BurstCountCurve.Evaluate(round));
        float varietyMult = EarlyRoundMult(EarlyRoundVarietyMult, round);
        _specialChance = SpecialChanceCurve.Evaluate(round) * varietyMult;
        _rareChance = RareChanceCurve.Evaluate(round) * varietyMult;
        _hiddenChance = HiddenChance * varietyMult;
        _demonShare = DemonShareCurve.Evaluate(round) * varietyMult;
        _commonSuppression = CommonSuppressionCurve.Evaluate(round);
        _forcedSpawnScene = null;
        _maxConcurrentEnemies = Mathf.RoundToInt(NormalConcurrentCap(round));

        // Normal rounds are bounded by their own 60-second timer, so payout is never gated there.
        _bossRoundRewardedLeft = int.MaxValue;

        EvaluateStatCurves(round);

        _spawnTimer.Start();
    }

    private float NormalConcurrentCap(int round) =>
        MaxConcurrentCurve.Evaluate(round)
        * LateCrowdMultCurve.Evaluate(round)
        * EarlyRoundMult(EarlyRoundConcurrentMult, round)
        * EarlyRoundMult(MidGameCrowdMult, round);

    // The round-indexed HP/damage curves assume the player's power grows on a predictable
    // schedule; DifficultyBalancer measures whether it actually did and scales those two (only)
    // up if the player has run far ahead. Speed and reward payout are deliberately left alone —
    // an over-powered player shouldn't earn less per kill for the same work. Evaluated once per
    // round here, alongside every other curve, rather than per-spawn.
    private void EvaluateStatCurves(int round)
    {
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        _catchUpMult = player != null
            ? DifficultyBalancer.GetCatchUpMultiplier(player.GetOffensivePower(), round)
            : 1f;

        // Symmetric counterpart for a defensively-stacked build: GetOffensivePower reads such a
        // build as approximately zero power, so the correction above never touches it even though
        // it can be nearly unkillable. Published to GameManager rather than applied here, since the
        // actual counter-pressure (a hit occasionally costing 2 lives/shield charges instead of 1)
        // lives in Player.TakeHit, which has no reference to this spawner.
        if (player != null && GameManager.Instance != null)
        {
            GameManager.Instance.SurvivabilityCatchUpMultiplier =
                DifficultyBalancer.GetSurvivabilityCatchUpMultiplier(player.GetSurvivabilityScore(), round);
        }

        float toughnessMult = EarlyRoundMult(MidGameToughnessMult, round);
        _hpMult = HpMultCurve.Evaluate(round) * _catchUpMult * toughnessMult;
        _dmgMult = DmgMultCurve.Evaluate(round) * _catchUpMult * toughnessMult;
        _speedMult = SpeedMultCurve.Evaluate(round) * EarlyRoundMult(EarlyRoundSpeedMult, round) * toughnessMult;
        // Read live rather than snapshotted, so a Frenesí round's doubled payout applies to everything it
        // spawns. EvaluateStatCurves runs after RoundEventDirector.BeginActiveEvent has set it.
        _rewardMult = RewardMultCurve.Evaluate(round) * (GameManager.Instance?.EventRewardMultiplier ?? 1f);
    }

    // Boss rounds keep spawning, but only Grunts and only a fraction as many as a normal round would.
    // The chaff exists to crowd the player's movement while the boss is the actual threat — so it
    // bypasses ChooseEnemyScene entirely rather than rolling the normal composition, which keeps the
    // boss the sole Special/elite on the field (and keeps the round recap's "especiales" count reading
    // exactly 1, since that counter tallies every non-Common category).
    private const float BossRoundCrowdMult = 0.20f;
    private const float BossRoundIntervalMult = 2f;

    // A boss round has NO time limit — it ends only when the boss dies. The concurrent cap alone
    // therefore bounds nothing over time: every chaff kill immediately frees a slot, so at round 20's
    // steady rate (~12/s against a cap of 24) a two-minute boss fight could spawn well over a thousand
    // Grunts. Stalling the boss was free, unlimited XP.
    //
    // Rather than capping the count outright (which left the arena eerily empty once the budget ran dry),
    // spawning continues forever and only the *payout* is capped: past this quota the chaff arrives with
    // GrantsRewards = false, so it's still a real obstacle but pays nothing except the occasional heart.
    // Stalling the boss now costs time and gains nothing, which closes the exploit without making the
    // fight less busy.
    private const int BossRoundRewardedSpawns = 20;
    private int _bossRoundRewardedLeft = int.MaxValue;

    public void ConfigureForBossRound(int round)
    {
        foreach (Node child in _enemiesContainer.GetChildren())
            child.QueueFree();

        // Every one of these has to be set explicitly. This method used to just Stop() the timer, so
        // none of the spawn config was ever assigned for a boss round — merely restarting the timer
        // would silently inherit the *previous* round's interval, burst and cap wholesale.
        _spawnTimer.WaitTime = SpawnIntervalCurve.Evaluate(round)
            * EarlyRoundMult(EarlyRoundIntervalMult, round) * BossRoundIntervalMult;
        _burstCount = Mathf.Max(1, Mathf.RoundToInt(BurstCountCurve.Evaluate(round) * BossRoundCrowdMult));
        _forcedSpawnScene = EnemyScene;

        // +1 for the boss's own slot: it joins the "enemies" group too, and OnSpawnTimeout counts that
        // group against this cap, so without the allowance the boss would eat one Grunt's worth of it.
        _maxConcurrentEnemies = Mathf.RoundToInt(NormalConcurrentCap(round) * BossRoundCrowdMult) + 1;
        _bossRoundRewardedLeft = BossRoundRewardedSpawns;

        EvaluateStatCurves(round);

        SpawnOne(BossScene);
        _spawnTimer.Start();
    }

    private void OnSpawnTimeout()
    {
        if (EnemyScene == null) return;

        if (_player == null || !IsInstanceValid(_player))
        {
            _player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            if (_player == null) return;
        }

        // Count the "enemies" group, NOT _enemiesContainer.GetChildCount(). Enemies parent their
        // score popups, damage numbers, and fired bullets into that same container, so the child
        // count silently includes transient VFX — which made the cap bind far earlier than its
        // number suggests, worst exactly in the busy late rounds where it matters most.
        // Read once per tick rather than per burst item, then tracked locally.
        int aliveEnemies = GetTree().GetNodesInGroup("enemies").Count;

        for (int i = 0; i < _burstCount; i++)
        {
            if (aliveEnemies >= _maxConcurrentEnemies) return;

            // Counted here rather than inside SpawnOne so the boss's own spawn — which goes through
            // SpawnOne directly from ConfigureForBossRound — never eats a unit of the chaff quota.
            bool rewarded = _bossRoundRewardedLeft > 0;
            if (rewarded) _bossRoundRewardedLeft--;

            SpawnOne(_forcedSpawnScene ?? ChooseEnemyScene(), rewarded);
            aliveEnemies++;
        }
    }

    private const float SpawnClearanceMargin = 120f;
    private const int SpawnPositionAttempts = 8;

    private Vector2 ChooseSpawnPosition() => PickPositionAround(Vector2.Zero, 360f);

    // Places a point on the clearance ring around the player. `forward` biases which side of the ring:
    // pass Vector2.Zero for a uniformly random angle (normal spawning), or a direction plus a narrower
    // spread to land the point ahead of where the player is going.
    //
    // Prefers a spot inside the arena: near an edge, a random angle can easily land outside it, leaving
    // the enemy with a long walk in from off-map. A handful of re-rolls almost always finds an in-bounds
    // angle; if none does (player cornered), it falls back to the first pick and lets the enemy walk in
    // rather than skipping the spawn.
    private Vector2 PickPositionAround(Vector2 forward, float spreadDegrees)
    {
        // Never inside the player's own fire-range ring — that reads as enemies materialising on top of
        // you. Derived from the live ring rather than trusting SpawnRadius, since Fire Range upgrades
        // grow it.
        float distance = SpawnRadius;
        if (_player is Player player)
            distance = Mathf.Max(distance, player.FireRange + SpawnClearanceMargin);

        bool directed = forward.LengthSquared() > 0.0001f;
        float baseAngle = directed ? forward.Angle() : 0f;
        float halfSpread = Mathf.DegToRad(spreadDegrees) * 0.5f;

        Vector2 fallback = Vector2.Zero;
        for (int attempt = 0; attempt < SpawnPositionAttempts; attempt++)
        {
            float angle = directed
                ? baseAngle + (float)(_rng.NextDouble() * 2.0 - 1.0) * halfSpread
                : (float)(_rng.NextDouble() * Mathf.Tau);

            Vector2 candidate = _player.GlobalPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;

            if (attempt == 0) fallback = candidate;
            if (IsInsideArena(candidate)) return candidate;
        }
        return fallback;
    }

    // A spot in the player's path rather than a random one: used both by stragglers being relocated and
    // by the ambushing Demon Stalker. Player.Velocity is public (inherited from CharacterBody2D), so
    // nothing extra has to be exposed; when the player is standing still it degrades to a random angle,
    // which is the right answer since there's no "ahead" to aim at.
    private const float InterceptSpreadDegrees = 100f;

    public Vector2 PickInterceptPosition()
    {
        if (_player == null || !IsInstanceValid(_player)) return Vector2.Zero;

        Vector2 travel = _player is CharacterBody2D body ? body.Velocity : Vector2.Zero;
        return PickPositionAround(travel.Normalized(), InterceptSpreadDegrees);
    }

    private bool IsInsideArena(Vector2 position)
    {
        if (_player is not Player player) return true;

        var extents = player.ArenaHalfExtents;
        return Mathf.Abs(position.X) <= extents.X && Mathf.Abs(position.Y) <= extents.Y;
    }

    private void SpawnOne(PackedScene scene, bool grantsRewards = true)
    {
        if (scene == null || _player == null) return;

        // The Stalker is the one enemy that picks its side of the ring deliberately: it lands in the
        // player's path instead of a random angle, which is the whole reason it reads as an ambush.
        Vector2 spawnPos = scene == DemonStalkerScene ? PickInterceptPosition() : ChooseSpawnPosition();

        var enemy = scene.Instantiate<Enemy>();
        enemy.SelfScene = scene;

        // The selected character's own HP effect (Juli's -10%) folds in with the round curve here,
        // before the enemy enters the tree — Enemy._Ready sets CurrentHp = MaxHp, so anything applied
        // later would leave it spawning at more than full health. Mathf.Max keeps a rounding-down at
        // low base HP from producing a 0-HP enemy that dies to nothing.
        float characterHpMult = (_player as Player)?.EnemyHpMultiplier ?? 1f;
        float eventHpMult = GameManager.Instance?.EventHpMultiplier ?? 1f;
        enemy.MaxHp = Mathf.Max(1, Mathf.RoundToInt(enemy.MaxHp * _hpMult * characterHpMult * eventHpMult));
        enemy.ContactDamage = Mathf.RoundToInt(enemy.ContactDamage * _dmgMult);
        enemy.MoveSpeed *= _speedMult;
        enemy.XpReward = Mathf.RoundToInt(enemy.XpReward * _rewardMult);
        enemy.CoinsReward = Mathf.RoundToInt(enemy.CoinsReward * _rewardMult);
        enemy.GlobalPosition = spawnPos;
        enemy.GrantsRewards = grantsRewards;

        // Elite spawn: none before round 3 (opening rounds stay gentle), then 1.5% per round,
        // capped at 20%. Only Common/Rare enemies can be elite.
        if (!enemy.IsQueuedForDeletion() && (enemy.Category == EnemyCategory.Common || enemy.Category == EnemyCategory.Rare))
        {
            int round = GameManager.Instance?.RoundNumber ?? 1;
            if (round >= 3)
            {
                float eliteChance = Mathf.Min((round - 2) * 0.015f, 0.20f);
                if (_rng.NextDouble() < eliteChance)
                {
                    var modifiers = new[] { EliteModifier.Vampiric, EliteModifier.Shielded, EliteModifier.Explosive, EliteModifier.Fast, EliteModifier.Regenerating };
                    enemy.MakeElite(modifiers[_rng.Next(modifiers.Length)]);
                }
            }
        }

        // Lets the HUD's fixed boss health bar find the live boss instance by polling the group,
        // the same way everything else here discovers the player/enemies — no signal needed.
        if (scene == BossScene) enemy.AddToGroup("boss");

        _enemiesContainer.AddChild(enemy);
    }

    // Sequential independent rolls, rarest first: Hidden is a flat 5% regardless of anything
    // else, then Special, then Rare, else Common. Not a normalized single distribution — simple
    // to tune per-tier without the categories needing to sum to 100%.
    private PackedScene ChooseEnemyScene()
    {
        // Rolled before everything else so DemonShareCurve means exactly what it says: 65% of all spawns
        // at round 25, taken off the top rather than out of whatever the other branches leave behind.
        if (_demonShare > 0f && _rng.NextDouble() < _demonShare)
        {
            var demon = ChooseDemonScene();
            if (demon != null) return demon;
        }

        if (HiddenScene != null && _rng.NextDouble() < _hiddenChance)
            return HiddenScene;

        if (_rng.NextDouble() < _specialChance)
        {
            var special = ChooseSpecialScene();
            if (special != null) return special;
        }

        if (RareScene != null && _rng.NextDouble() < _rareChance)
            return RareScene;

        // Late-game common taper (see CommonSuppressionCurve): rather than raising _specialChance —
        // which would zero out the Rare branch above, since these rolls are sequential — an
        // increasing share of what would have been a Grunt is promoted to a Special instead,
        // reaching all of them by round 18.
        if (_commonSuppression > 0f && _rng.NextDouble() < _commonSuppression)
        {
            var promoted = ChooseSpecialScene();
            if (promoted != null) return promoted;
        }

        return EnemyScene;
    }

    // Weighted rather than the uniform 25%-each this used to be. Once CommonSuppressionCurve pushes
    // Specials from ~57% to ~82% of every spawn, a uniform pick would make Shooter ~20% of the whole
    // field (a wall of enemy bullets, which reads as far more overwhelming than the same number of
    // melee bodies) and Splitter another ~20% — and every Splitter becomes 15 bodies across its
    // generations, re-inflating exactly the crowd count LateCrowdMultCurve is working to bring down.
    // Tank absorbs most of the shifted share instead: at 120 HP it's what keeps threat-per-body high
    // while the total count drops.
    private const int TankWeight = 40;
    private const int SpeedyWeight = 25;
    private const int ShooterWeight = 20;
    private const int SplitterWeight = 15;

    // Same weighted-pick shape as ChooseSpecialScene below; returns null if no demon scene is assigned so
    // the caller falls through to the normal roster (which is also what keeps the game working if the
    // scenes are ever unwired in Arena.tscn).
    private PackedScene ChooseDemonScene()
    {
        int total = 0;
        if (DemonScene != null) total += DemonWeight;
        if (DemonBruteScene != null) total += DemonBruteWeight;
        if (DemonStalkerScene != null) total += DemonStalkerWeight;
        if (total == 0) return null;

        int roll = _rng.Next(total);
        if (DemonScene != null && (roll -= DemonWeight) < 0) return DemonScene;
        if (DemonBruteScene != null && (roll -= DemonBruteWeight) < 0) return DemonBruteScene;
        return DemonStalkerScene;
    }

    // Returns null only if no special scene is assigned at all, so callers can fall back to Common.
    private PackedScene ChooseSpecialScene()
    {
        int total = 0;
        if (TankScene != null) total += TankWeight;
        if (SpeedyScene != null) total += SpeedyWeight;
        if (ShooterScene != null) total += ShooterWeight;
        if (SplitterScene != null) total += SplitterWeight;
        if (total == 0) return null;

        int roll = _rng.Next(total);
        if (TankScene != null && (roll -= TankWeight) < 0) return TankScene;
        if (SpeedyScene != null && (roll -= SpeedyWeight) < 0) return SpeedyScene;
        if (ShooterScene != null && (roll -= ShooterWeight) < 0) return ShooterScene;
        return SplitterScene;
    }
}
