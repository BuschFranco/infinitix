namespace ShooterLoop;

public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    [Export] public float RoundDuration = 60f;

    public int Level = 1;
    public int Xp = 0;
    public int XpToNextLevel = 10;
    public int EnemiesKilled = 0;
    public int SpecialEnemiesKilled = 0;
    public int BossesKilled = 0;
    public int Coins = 0;
    public int Score = 0;
    public int RoundNumber = 1;
    public bool IsPaused = false;

    public enum ScreenOrientation { Landscape, Portrait }
    public ScreenOrientation CurrentOrientation { get; private set; } = ScreenOrientation.Portrait;

    public float CameraDistance = 2000f;

    // The project's reference/base size (matches project.godot's implicit 1152x648 default, made
    // explicit there) and its portrait counterpart. window/stretch/mode="canvas_items" + "expand"
    // scales UI, and reveals extra world through the camera, relative to whichever of these is
    // currently set as the root Window's ContentScaleSize — swapping it to match the chosen
    // orientation is what keeps both UI layout and how much of the arena is visible consistent
    // between landscape and portrait, instead of "expand" distorting one of them.
    private static readonly Vector2I LandscapeBaseSize = new(1152, 648);
    private static readonly Vector2I PortraitBaseSize = new(648, 1152);

    // Prices track the player's *current* coin balance, not lifetime earnings. Spending in the shop
    // lowers prices on the next refresh; hoarding raises them. Base is 3.5 so a Common (~8) costs a
    // heavy ~28 coins at round 1 — enough to buy one thing, never everything — and every coin over
    // 40 raises it further (~0.3 per 40), so the more you hoard the steeper the shop gets. Late
    // rounds compound +10%/round on top, so income can't outrun the shop forever.
    private const float WealthCostBase = 3.5f;
    private const float WealthCostPerStep = 0.3f;     // 0.3 per 40 coins = 0.0075/coin
    private const float WealthCostStep = 40f;
    private const float WealthCostRoundStep = 0.1f;   // +10% per round
    private const float WealthCostMax = 4000f;

    public int TotalCoinsEarned = 0;

    public float UpgradeCostMultiplier
    {
        get
        {
            float mult = WealthCostBase + (Coins / WealthCostStep) * WealthCostPerStep;
            // Escalado por ronda: +10% por ronda para que rondas altas mantengan presión
            float round = GameManager.Instance?.RoundNumber ?? 1;
            float roundMult = 1f + (round - 1) * WealthCostRoundStep;
            return Mathf.Clamp(mult * roundMult, 1.5f, WealthCostMax);
        }
    }

    private readonly Dictionary<UpgradeType, int> _shopPurchaseCounts = new();

    // Everything that has to interrupt gameplay with a modal, queued rather than opened directly.
    // A single kill can trigger several of these at once — the boss's XP payout alone can cross
    // multiple level thresholds *and* end the round *and* (the first time) grant an Ultimate — and
    // they all share one UpgradePicker plus the shop, so opening any of them eagerly means the last
    // one silently clobbers the rest. ResolveNextInterstitial() drains this queue one modal at a
    // time instead, in priority order: level-ups, then the Ultimate choice, then the round-end shop.
    private int _pendingLevelUps = 0;
    private bool _pendingUltimateChoice = false;
    private bool _pendingRoundEnd = false;
    private bool _bossUltimateGranted = false;

    // Which queue the currently-open picker is resolving, so ApplyUpgradeAndResume decrements the
    // right one instead of having to infer it from the chosen upgrade's type.
    private bool _pickerIsUltimateChoice = false;

    // Every counter above is cumulative for the run, so the end-of-round recap has to diff against
    // a snapshot taken when the round began. Re-taken in StartNextRound and ResetRun.
    private int _roundStartKills;
    private int _roundStartEliteKills;
    private int _roundStartCoins;
    private int _roundStartScore;
    private int _roundStartLevel;

    public event Action<int, int> XpChanged;
    public event Action<int> LevelUp;

    // Distinct from LevelUp above: LevelUp fires once per threshold crossed (so a triple level-up
    // invokes it 3 times, correctly updating "Nv N" to its final value one step at a time).
    // LevelsGained fires once per AddXp call with the *total* crossed, so the HUD popup reads as
    // one "+3" rather than three overlapping "+1"s in the same frame.
    public event Action<int> LevelsGained;
    // Fired once the player has actually picked every reward from a level-up (or streak of them) and
    // play is about to resume -- the fireworks/"SUBE DE NIVEL" celebration listens to this rather
    // than LevelsGained so it lands after the upgrade picker closes instead of competing with it for
    // attention while it's still open.
    public event Action LevelUpCelebration;
    public event Action<int> CoinsChanged;
    public event Action<int> ScoreChanged;
    public event Action<int> RoundChanged;
    public event Action PlayerDied;

    public float RoundTimeRemaining => (float)(_roundTimer?.TimeLeft ?? 0f);

    private Timer _roundTimer;

    public override void _Ready()
    {
        Instance = this;

        // Read BEFORE LoadLanguagePreference below, which returns a sensible default but never
        // writes the file itself -- so this is the one reliable "has the player ever touched
        // language/onboarding at all" signal, used by ShouldShowOnboarding further down.
        bool hadLanguageFile = FileAccess.FileExists(LanguageFilePath);

        // Applied before anything else so the very first frame (main menu included) already
        // renders in whatever language and orientation the player last picked, not a flash of
        // the wrong one that then snaps over.
        CurrentLanguage = LoadLanguagePreference();
        TranslationServer.SetLocale(CurrentLanguage);
        CurrentOrientation = LoadOrientationPreference();
        ApplyOrientation();
        LoadSettings();
        EnsureMissionsForToday();

        // A true first launch, not just "no name yet" or "no language yet" alone -- an existing
        // player who happens to clear just one of the two save files shouldn't get sent through
        // onboarding again.
        ShouldShowOnboarding = !hadLanguageFile && string.IsNullOrEmpty(PlayerName);

        // Not started here — this runs at app boot, while the player is still in the main menu.
        // Starting it immediately meant it could burn all the way down before Arena.tscn even
        // loaded (character select, reading options, etc. all ate into round 1's 60s for free),
        // surfacing as "round 1 shows 0 time and jumps straight to round 2". See
        // StartRoundOneTimer, called once Arena/the spawner actually exists.
        _roundTimer = new Timer();
        _roundTimer.OneShot = true;
        _roundTimer.WaitTime = RoundDuration;
        AddChild(_roundTimer);
        _roundTimer.Timeout += OnRoundTimeout;
    }

    // Called by EnemySpawner._Ready() — the one thing that only exists once Arena.tscn has actually
    // loaded, i.e. gameplay has genuinely begun. Every later round instead goes through
    // StartNextRound()'s own countdown-then-arm sequence; round 1 has no such trigger of its own; this
    // is that trigger.
    public void StartRoundOneTimer()
    {
        _roundTimer.Stop();
        _roundTimer.Start();

        // Marks the start of this run for the Estadísticas screen's "Tiempo jugado" — the one stat
        // that can't be summed from per-kill/per-round counters. Includes time spent in shops/paused,
        // same "close enough, first guess" tolerance the rest of this catalog already accepts.
        _runStartTimeMsec = Time.GetTicksMsec();
    }

    private ulong _runStartTimeMsec;

    public bool IsBossRound => RoundNumber % 5 == 0;

    // Score is intentionally synced 1:1 with XP — same value feeds both, so the number shown as
    // "Puntaje" always tracks the same progression that drives leveling, instead of following its
    // own separate (and previously flat, un-scaled) per-category table. Coins remain completely
    // separate — this only concerns Score/XP.
    public int RegisterKill(int xpReward, int coinsReward, EnemyCategory category = EnemyCategory.Common)
    {
        EnemiesKilled++;
        // Live, not batched at run end (see EvaluateAchievements below) -- same "bump it where the
        // per-run counter already bumps" pattern EverCompletedBuilds/EverGotLegendary already use,
        // so Exterminador/Cazajefes can notify the instant they're crossed instead of only at Game Over.
        TotalEnemiesKilled++;
        if (category != EnemyCategory.Common) SpecialEnemiesKilled++;
        NotifyMissionProgress(MissionKind.Kill, 1);

        var player = GetTree().GetFirstNodeInGroup("player") as Player;

        // Kill streak: rapid kills boost XP/coin payouts.
        player?.RegisterKillForStreak();
        float streakMult = player?.KillStreakMultiplier ?? 1f;

        // Sabiduría scales XP; Botín scales coins. Streak boosts both.
        int finalXp = Mathf.RoundToInt(xpReward * (player?.XpMultiplier ?? 1f) * streakMult);
        AddXp(finalXp);
        AddCoins(Mathf.RoundToInt(coinsReward * (player?.CoinMultiplier ?? 1f) * streakMult));

        // Deliberately the *unmultiplied* xpReward, which breaks the otherwise 1:1 Score/XP sync.
        // Score is persisted as a high score, so letting a reward choice inflate it would make runs
        // incomparable.
        AddScore(xpReward);

        if (category == EnemyCategory.Boss)
        {
            BossesKilled++;
            TotalBossesKilled++;
            NotifyMissionProgress(MissionKind.BossKill, 1);
            AudioManager.Instance?.Play(AudioManager.Sfx.BossDie);

            // Beating the very first boss is what unlocks Ultimates as a mechanic — a free choice
            // between three of them, once per run. Queued *before* BeginRoundEnd so it's already in
            // the queue when that drains it, and so the recap is up when the picker opens.
            if (!_bossUltimateGranted)
            {
                _bossUltimateGranted = true;
                _pendingUltimateChoice = true;
            }

            BeginRoundEnd();
        }

        // Low-frequency checkpoint (once per kill, not per hit/frame) -- same convention the rest of
        // meta-progression already follows. Idempotent per achievement id, so calling it here in
        // addition to round-end/build/legendary checkpoints below never double-pays anything; it just
        // lets Exterminador/Cazajefes/Puntería notify live instead of only at Game Over.
        EvaluateAchievements(_unlockedAchievements);

        return finalXp;
    }

    public void AddCoins(int amount)
    {
        Coins += amount;
        TotalCoinsEarned += amount;
        RecordCoinsEarned(amount);
        CoinsChanged?.Invoke(Coins);
    }

    public void SpendCoins(int amount)
    {
        Coins = Mathf.Max(0, Coins - amount);
        CoinsChanged?.Invoke(Coins);
    }

    public void AddScore(int amount)
    {
        Score += amount;
        ScoreChanged?.Invoke(Score);
    }

    // Global multiplier every Enemy's velocity is scaled by — centralized here (rather than
    // mutating each enemy's own MoveSpeed) so the Zona Lenta ultimate affects enemies spawned
    // mid-effect too, not just the ones that existed at the moment it was triggered.
    public float EnemySpeedMultiplier = 1f;

    // What EnemySpeedMultiplier reverts to once a temporary slow (Zona Lenta) expires — set by
    // RoundEventDirector to Frenzy's speed-up while that event is active, 1f otherwise. Without this,
    // firing Zona Lenta during a Frenzy round used to leave speed at 1f (a normal round's pace) once
    // the ultimate wore off, instead of snapping back to Frenzy's still-active speed-up.
    public float BaseEnemySpeedMultiplier = 1f;

    // Extra XP/coin payout from a round event (Frenesí doubles it). Applied per spawn in
    // EnemySpawner.SpawnOne alongside RewardMultCurve, and reset by RoundEventDirector at round end.
    public float EventRewardMultiplier = 1f;

    // Extra enemy HP from a round event (Blindaje's +40%). Applied per spawn in
    // EnemySpawner.SpawnOne alongside the round/character HP multipliers, and reset by
    // RoundEventDirector at round end -- same shape as EventRewardMultiplier above.
    public float EventHpMultiplier = 1f;
    private Timer _slowTimer;

    // Set once per round by EnemySpawner.EvaluateStatCurves from DifficultyBalancer's survivability
    // catch-up, and read by Player.TakeHit — living here (rather than a direct EnemySpawner<->Player
    // reference) matches how EnemySpeedMultiplier above already brokers a difficulty signal between
    // systems that otherwise don't know about each other.
    public float SurvivabilityCatchUpMultiplier = 1f;

    public void ApplyTemporarySlow(float multiplier, float duration)
    {
        EnemySpeedMultiplier = multiplier;

        if (_slowTimer == null)
        {
            _slowTimer = new Timer { OneShot = true };
            AddChild(_slowTimer);
            _slowTimer.Timeout += () => EnemySpeedMultiplier = BaseEnemySpeedMultiplier;
        }
        _slowTimer.WaitTime = duration;
        _slowTimer.Start();
    }

    // Was a flat 1.3x per level. The problem with a flat factor is that it loses badly to how income
    // actually grows: RewardMultCurve alone adds 35% *per round*, and that compounds again with burst
    // count, spawn rate and a progressively heavier enemy mix. By round 20 a player earns ~11,000 XP in
    // a single round against a 1,486 level cost — roughly seven levels per round, which is what made
    // late-game leveling feel free.
    //
    // A factor that itself grows per level fixes the shape rather than just shifting it: the early
    // game is left almost exactly as it was (level 5 even gets slightly cheaper), and the curve only
    // steepens from around level 12 — which is about where a player sits by round 11.
    private const float XpGrowthBase = 1.30f;
    private const float XpGrowthPerLevel = 0.02f;
    private const float XpGrowthMax = 1.75f;

    private float XpGrowthFactor() =>
        Mathf.Min(XpGrowthBase + XpGrowthPerLevel * Level, XpGrowthMax);

    public void AddXp(int amount)
    {
        Xp += amount;

        // Counted rather than fired inline per threshold: a single big XP reward can cross several
        // thresholds in one call (see _pendingLevelUps above), and the level-up popup should read
        // as one "+3", not three separate "+1"s stacking on top of each other in the same frame.
        int levelsGained = 0;
        while (Xp >= XpToNextLevel)
        {
            Xp -= XpToNextLevel;
            Level++;
            XpToNextLevel = Mathf.RoundToInt(XpToNextLevel * XpGrowthFactor());
            levelsGained++;
            OnLevelGained();
        }

        // Fired once, AFTER any rollover — this used to fire before the loop, against the
        // pre-rollover Xp and XpToNextLevel. That reads as "at/over max" the instant a level-up
        // happens, and since nothing re-fired the event once the loop corrected Xp/XpToNextLevel,
        // the bar just sat there looking full until the next kill's AddXp call happened to invoke
        // it again with values that were, by then, already correct.
        XpChanged?.Invoke(Xp, XpToNextLevel);

        if (levelsGained > 0)
        {
            LevelsGained?.Invoke(levelsGained);

            // Here rather than in OnLevelGained, which runs once per threshold crossed: a kill that
            // grants three levels at once should chime once, not stack three copies of the same
            // arpeggio on one frame. Exactly the reasoning the "+3" popup above already follows.
            AudioManager.Instance?.Play(AudioManager.Sfx.LevelUp);
        }
    }

    private void OnLevelGained()
    {
        LevelUp?.Invoke(Level);

        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        player?.TriggerLevelUpBurst();

        _pendingLevelUps++;
        ResolveNextInterstitial();
    }

    // Opens the next queued modal, or unpauses if the queue is empty. Bails out while a picker is
    // already on screen — whatever is left in the queue gets picked up by ApplyUpgradeAndResume
    // when that pick resolves, so this is safe to call from anywhere that enqueues something.
    private void ResolveNextInterstitial()
    {
        var picker = GetTree().GetFirstNodeInGroup("upgrade_picker") as UpgradePicker;
        if (picker == null || picker.Visible) return;

        var player = GetTree().GetFirstNodeInGroup("player") as Player;

        if (_pendingLevelUps > 0)
        {
            _pickerIsUltimateChoice = false;
            picker.Open(UpgradeData.PickRandomTiered(3, RoundNumber, RewardSource.LevelUp, isUseless: player != null ? player.IsRewardUseless : null, fortuneBonus: player?.FortuneBonus ?? 0f));
            Pause();
            return;
        }

        if (_pendingUltimateChoice)
        {
            _pickerIsUltimateChoice = true;
            picker.Open(UpgradeData.PickUltimateChoices(3, player != null ? player.IsRewardUseless : null), UpgradePicker.UltimateTitle, isUltimate: true);
            Pause();
            return;
        }

        if (_pendingRoundEnd)
        {
            _pendingRoundEnd = false;
            EndRound();
            return;
        }

        Resume();
    }

    private void OnRoundTimeout()
    {
        BeginRoundEnd();
    }

    // Single entry point for "the round is over", from either the timer or a boss kill. Puts the
    // recap on screen up front so it's already visible behind the level-up / Ultimate / shop
    // modals, then hands off to the queue. StartNextRound is what takes it back down.
    // What the round just produced. Snapshotted here rather than recomputed later because the deltas
    // are against _roundStart* values that StartNextRound resets — by the time the shop opens (after
    // any level-up pickers have been resolved) the originals are still intact, but tying the numbers
    // to the moment the round actually ended keeps them correct regardless of how long the
    // interstitial chain takes.
    public readonly record struct RoundRecap(int Round, int Kills, int EliteKills, int Coins, int Score, int Levels);

    public RoundRecap LastRoundRecap { get; private set; }

    private void BeginRoundEnd()
    {
        // The single entry point for "the round is over", whether it ended on the timer or on the
        // boss dying — so one hook covers both without either path needing to know about audio.
        AudioManager.Instance?.Play(AudioManager.Sfx.RoundComplete);

        _pendingRoundEnd = true;

        // The recap used to be its own floating panel on its own CanvasLayer, shown here and left up
        // through the whole interstitial chain. In practice the shop is nearly full-height, so the
        // recap sat entirely behind it and was only ever glimpsed during the shop's fade-out — two
        // windows fighting for the same space to show one moment's worth of information. It's now
        // rendered inside the shop panel itself, which is also where the coins it reports get spent.
        LastRoundRecap = new RoundRecap(
            RoundNumber,
            EnemiesKilled - _roundStartKills,
            SpecialEnemiesKilled - _roundStartEliteKills,
            TotalCoinsEarned - _roundStartCoins,
            Score - _roundStartScore,
            Level - _roundStartLevel);

        ResolveNextInterstitial();
    }

    private void SnapshotRoundStart()
    {
        _roundStartKills = EnemiesKilled;
        _roundStartEliteKills = SpecialEnemiesKilled;
        _roundStartCoins = TotalCoinsEarned;
        _roundStartScore = Score;
        _roundStartLevel = Level;
    }

    private void EndRound()
    {
        // Enemies and their in-flight shots both vanish the instant a round ends — the next round's
        // board should start clean, not with whatever was left standing (or mid-Split, or mid-air)
        // from the previous one. Clearing the bullets matters for fairness as much as tidiness: one
        // frozen by the shop's pause would otherwise resume and land on the player during the next
        // round's "get ready" countdown.
        foreach (Node enemy in GetTree().GetNodesInGroup("enemies"))
            enemy.QueueFree();

        foreach (Node bullet in GetTree().GetNodesInGroup("enemy_bullets"))
            bullet.QueueFree();

        // Pickups deliberately survive the round boundary, unlike the two groups above. A drop that
        // lands in the last seconds of a round was earned, and sweeping it here meant the kill that
        // produced it was silently wasted. StartNextRound refreshes their lifetimes so they get a
        // real window to be collected rather than expiring two seconds into the new round.

        // A Timer only freezes its remaining time while the tree is paused — it doesn't reset.
        // Without this, the spawner's own timer could have almost no time left by the time the
        // shop closes, firing an immediate spawn the instant the tree unpauses and completely
        // ignoring the round-start countdown below.
        var spawner = GetTree().GetFirstNodeInGroup("enemy_spawner") as EnemySpawner;
        spawner?.StopSpawning();

        // Clears every hazard the round's event left on the field and resets its global multipliers, so
        // nothing bleeds into the shop or the next round.
        RoundEventDirectorNode?.EndActiveEvent();

        var shop = GetTree().GetFirstNodeInGroup("shop") as Shop;
        shop?.Open();

        Pause();
    }

    private const float RoundStartDelay = 3f;
    private Timer _roundStartTimer;

    public float RoundStartTimeRemaining => (float)(_roundStartTimer?.TimeLeft ?? 0f);
    public bool IsRoundStarting => _roundStartTimer != null && !_roundStartTimer.IsStopped();

    public void StartNextRound()
    {
        RoundNumber++;
        RoundChanged?.Invoke(RoundNumber);

        // Live, not batched at run end -- one more round just got cleared (the one RoundNumber was
        // at before this increment). Same live-counter reasoning as RegisterKill's TotalEnemiesKilled
        // bump above; RegisterFinalScore no longer re-adds this (see roundsClearedThisRun there).
        TotalRoundsCleared++;

        // RoundReached missions and round-based achievements (Sobreviviente/Curtido/Imparable/
        // Leyenda/Veterano) both read values that are live as of the two lines above -- checking them
        // right here is what lets "llegá a la ronda 8" notify the instant round 8 begins, instead of
        // only at Game Over. Both calls are idempotent (per mission slot / per achievement id), so
        // the existing end-of-run calls stay in place as a harmless safety net.
        NotifyMissionRoundReached(RoundNumber);
        EvaluateAchievements(_unlockedAchievements);

        // The recap has been up since the round ended, through every modal. This is the point the
        // player has finished choosing everything, so it comes down as the countdown begins.
        SnapshotRoundStart();

        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        player?.HealFullLives();
        player?.RefillShield();
        player?.RefreshShieldRegenRate();
        player?.ResetKillStreak();

        // Pickups are no longer swept at round end (see EndRound), so anything left on the field gets
        // its full lifetime back here — otherwise a drop from the last second of the previous round
        // would expire almost immediately into this one.
        foreach (Node node in GetTree().GetNodesInGroup("pickups"))
            (node as PickupBase)?.RefreshLifetime();

        // Enemies don't start appearing immediately — a short "get ready" countdown plays first
        // (HUD shows it in place of the round timer/boss label), with the round's own timer/spawns
        // deferred until it elapses.
        _roundTimer.Stop();

        if (_roundStartTimer == null)
        {
            _roundStartTimer = new Timer { OneShot = true };
            // The tree stays paused for the whole "get ready" delay now (see the Resume() move
            // below), so this has to keep ticking through that pause the same way Shop/UpgradePicker
            // already stay interactive through it — otherwise the countdown itself would freeze.
            _roundStartTimer.ProcessMode = ProcessModeEnum.Always;
            AddChild(_roundStartTimer);
            _roundStartTimer.Timeout += BeginRoundAfterCountdown;
        }
        _roundStartTimer.WaitTime = RoundStartDelay;
        _roundStartTimer.Start();

        // The alarm goes off during the countdown, not when the boss lands — the point is to telegraph
        // what's coming. This is also what gives the round-5 boss an alarm at all, since normal round
        // escalation hasn't reached the alarm threshold that early.
        DangerDirectorNode?.SetBossAlert(IsBossRound);

        // Rolled now, applied after the countdown. Splitting it that way means the outcome is settled
        // before the round begins while the announcement still gets the screen to itself.
        RoundEventDirectorNode?.RollForRound(RoundNumber, IsBossRound);

        // Deliberately NOT Resume() here — see BeginRoundAfterCountdown. The tree stays paused
        // through the whole "get ready" delay, not just while pickers/the shop are open.
    }

    private DangerDirector DangerDirectorNode =>
        GetTree().GetFirstNodeInGroup("danger_director") as DangerDirector;

    private RoundEventDirector RoundEventDirectorNode =>
        GetTree().GetFirstNodeInGroup("round_event_director") as RoundEventDirector;

    // Set when the "get ready" countdown finishes while the player has the pause menu open — the
    // countdown's own Timer runs with ProcessMode.Always specifically so it keeps ticking through
    // the pause between rounds, but that meant it used to fire straight through a player-initiated
    // pause too, resuming the tree and starting the round in the background behind the still-open
    // menu. Cleared (and the round actually started) from ResumeAfterPause, once the pause menu's
    // own "Reanudando en..." countdown genuinely closes it.
    private bool _roundStartDeferred;

    private void BeginRoundAfterCountdown()
    {
        if (GetTree().GetFirstNodeInGroup("pause_menu") is PauseMenu pauseMenu && pauseMenu.Visible)
        {
            _roundStartDeferred = true;
            return;
        }
        _roundStartDeferred = false;

        // The "GO" at the end of the 3-2-1 the HUD is beeping out — same note family an octave up,
        // so it lands as the resolution of that sequence rather than as an unrelated noise.
        AudioManager.Instance?.Play(AudioManager.Sfx.RoundStart);

        // This — not StartNextRound() — is the actual start of live gameplay: the tree has stayed
        // paused since the round ended (through every picker, the shop, and the whole "get ready"
        // countdown) specifically so nothing could move or collect a leftover pickup and sneak in one
        // more level-up dialog after the player thought they were done. Resuming right where spawning
        // is about to (re)start closes that gap.
        Resume();

        var spawner = GetTree().GetFirstNodeInGroup("enemy_spawner") as EnemySpawner;
        var director = DangerDirectorNode;

        if (IsBossRound)
        {
            spawner?.ConfigureForBossRound(RoundNumber);

            var banner = GetTree().GetFirstNodeInGroup("boss_banner") as BossBanner;
            banner?.Announce(RoundNumber);

            // The pre-boss burst hands back to the round's own danger level now that the boss is here.
            director?.SetBossAlert(false);

            if (GetTree().GetFirstNodeInGroup("camera_rig") is CameraRig camera)
                camera.Shake(DangerLevel.ShakeStrength, DangerLevel.ShakeDuration);
        }
        else
        {
            // Started before ConfigureForRound: Frenesí sets EventRewardMultiplier, and the spawner reads
            // that when it evaluates its stat curves for this round.
            RoundEventDirectorNode?.BeginActiveEvent();

            spawner?.ConfigureForRound(RoundNumber);
            _roundTimer.Start();

            // Fired here rather than from RoundChanged so the callout doesn't share the screen with the
            // countdown it would otherwise overlap. Only lands on the few non-boss rounds that mark an
            // escalation step — see DangerLevel.ThreatCallout.
            string callout = DangerLevel.ThreatCallout(RoundNumber);
            if (callout != null) director?.AnnounceThreat(callout);
        }
    }

    public void OpenPauseMenu()
    {
        var menu = GetTree().GetFirstNodeInGroup("pause_menu") as PauseMenu;
        menu?.Open();
        Pause();
    }

    // --- Android back button ----------------------------------------------------------------------
    //
    // Every modal that can be open pushes "what back should do" here on Open() and pops it on
    // Close() — a stack, not a single slot, because modals genuinely nest today (ConfirmDialog opens
    // on top of PauseMenu/GameOverScreen without hiding them; CharacterCreator opens on top of
    // CharacterSelectMenu the same way) and back has to peel off only the top one.
    //
    // No attempt is made to guarantee every push is popped on every exit path (a scene reload, an
    // abandoned run) — HandleBackPressed already skips any entry whose owner got freed without
    // popping, so a missed pop is inert rather than a crash. See docs/navigation.md.
    private readonly List<(Node Owner, Action Handler)> _backStack = new();

    public void PushBackHandler(Node owner, Action handler) => _backStack.Add((owner, handler));

    public void PopBackHandler(Node owner)
    {
        for (int i = _backStack.Count - 1; i >= 0; i--)
        {
            if (_backStack[i].Owner != owner) continue;
            _backStack.RemoveAt(i);
            return;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMGoBackRequest) HandleBackPressed();
    }

    private void HandleBackPressed()
    {
        while (_backStack.Count > 0)
        {
            var (owner, handler) = _backStack[^1];
            _backStack.RemoveAt(_backStack.Count - 1);

            // The owner closed through some other path (scene reload, abandoning the run) without
            // popping itself first — stale, skip it rather than call into a freed node.
            if (!IsInstanceValid(owner)) continue;

            handler();
            return;
        }

        HandleBackWithNothingOpen();
    }

    // Nothing was open to close, so back needs a contextual meaning of its own: mid-run, the same
    // thing the pause button does; at the main menu, the one place back can still mean "quit" now
    // that quit_on_go_back is off project-wide (see project.godot).
    private void HandleBackWithNothingOpen()
    {
        if (GetTree().CurrentScene?.SceneFilePath == "res://Scenes/Arena.tscn")
        {
            OpenPauseMenu();
            return;
        }

        var dialog = GetTree().GetFirstNodeInGroup("confirm_dialog") as ConfirmDialog;
        dialog?.Ask("¿Salir del juego?", "Se va a cerrar la aplicación.", "Salir", () => GetTree().Quit());
    }

    public void UpdateCameraExtents()
    {
        if (GetTree().GetFirstNodeInGroup("camera_rig") is CameraRig camera)
        {
            float defaultDistance = 2000f;
            float zoomValue = defaultDistance / CameraDistance;
            camera.Zoom = new Vector2(zoomValue, zoomValue);
        }
    }

    public float GetShopSurchargeMultiplier(UpgradeType type)
    {
        return Mathf.Pow(1.1f, _shopPurchaseCounts.GetValueOrDefault(type, 0));
    }

    public void RegisterShopPurchase(UpgradeType type)
    {
        _shopPurchaseCounts[type] = _shopPurchaseCounts.GetValueOrDefault(type, 0) + 1;
    }

    public void ApplyUpgrade(UpgradeData upgrade)
    {
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        player?.ApplyUpgrade(upgrade);
    }

    public void ApplyUpgradeAndResume(UpgradeData upgrade)
    {
        ApplyUpgrade(upgrade);

        if (_pickerIsUltimateChoice)
        {
            _pickerIsUltimateChoice = false;
            _pendingUltimateChoice = false;
        }
        else if (_pendingLevelUps > 0)
        {
            _pendingLevelUps--;

            // Every level-up reward from this streak has now been picked -- celebrate now rather
            // than back when the level-up actually happened (still true even if something else
            // queued behind it, like the round-end shop, opens right after via
            // ResolveNextInterstitial below).
            if (_pendingLevelUps == 0)
            {
                var player = GetTree().GetFirstNodeInGroup("player") as Player;
                player?.ShowLevelUpText();
                LevelUpCelebration?.Invoke();
            }
        }

        // Whatever's still queued (another level-up, the Ultimate choice, the round-end shop) opens
        // next; ResolveNextInterstitial unpauses on its own once nothing is left.
        ResolveNextInterstitial();
    }

    public void Pause()
    {
        GetTree().Paused = true;
        IsPaused = true;
    }

    public void Resume()
    {
        GetTree().Paused = false;
        IsPaused = false;
    }

    // The one path a player actually resumes gameplay through (PauseMenu's own "Reanudando en..."
    // countdown finishing). Distinct from a plain Resume() so it can also release a round-start that
    // got deferred because the "get ready" countdown finished while this same menu was still open —
    // see BeginRoundAfterCountdown.
    public void ResumeAfterPause()
    {
        Resume();
        if (_roundStartDeferred) BeginRoundAfterCountdown();
    }

    public void NotifyPlayerDied()
    {
        if (IsPaused) return;
        PlayerDied?.Invoke();

        // Only audible because AudioManager runs with ProcessMode.Always — Pause() four lines down
        // freezes the tree, and a Pausable audio node would be cut off mid-sting.
        AudioManager.Instance?.Play(AudioManager.Sfx.PlayerDie);

        RegisterFinalScore();

        var screen = GetTree().GetFirstNodeInGroup("game_over_screen") as GameOverScreen;
        screen?.Open();

        Pause();
    }

    private const string HighScoreFilePath = "user://highscore.save";

    // Local-install save data — a single plain-text integer, not tied to any account or cloud
    // sync. Static since MainMenu (no live run in progress) needs to read it too.
    public static int LoadHighScore()
    {
        if (!FileAccess.FileExists(HighScoreFilePath)) return 0;
        using var file = FileAccess.Open(HighScoreFilePath, FileAccess.ModeFlags.Read);
        if (file == null) return 0;
        int.TryParse(file.GetLine(), out int score);
        return score;
    }

    private static void SaveHighScore(int score)
    {
        using var file = FileAccess.Open(HighScoreFilePath, FileAccess.ModeFlags.Write);
        file?.StoreLine(score.ToString());
    }

    private const string OrientationFilePath = "user://orientation.save";

    // Same plain-text-file pattern as HighScore above. Static since MainMenu (no GameManager
    // instance guaranteed loaded yet the very first time) needs to read the saved choice too.
    // Portrait is the default for a first-ever launch (no save file yet) — only an explicit
    // saved "Landscape" switches it, everything else (missing file, unreadable, unrecognized
    // text) falls back to Portrait.
    public static ScreenOrientation LoadOrientationPreference()
    {
        if (!FileAccess.FileExists(OrientationFilePath)) return ScreenOrientation.Portrait;
        using var file = FileAccess.Open(OrientationFilePath, FileAccess.ModeFlags.Read);
        if (file == null) return ScreenOrientation.Portrait;
        return file.GetLine() == "Landscape" ? ScreenOrientation.Landscape : ScreenOrientation.Portrait;
    }

    private static void SaveOrientationPreference(ScreenOrientation orientation)
    {
        using var file = FileAccess.Open(OrientationFilePath, FileAccess.ModeFlags.Write);
        file?.StoreLine(orientation.ToString());
    }

    // Called from the main menu's orientation picker. Takes effect immediately — no restart
    // needed — since both DisplayServer's orientation request and ContentScaleSize are live
    // runtime properties.
    public void SetOrientation(ScreenOrientation orientation)
    {
        CurrentOrientation = orientation;
        SaveOrientationPreference(orientation);
        ApplyOrientation();
    }

    private void ApplyOrientation()
    {
        bool portrait = CurrentOrientation == ScreenOrientation.Portrait;

        DisplayServer.ScreenSetOrientation(portrait
            ? DisplayServer.ScreenOrientation.Portrait
            : DisplayServer.ScreenOrientation.Landscape);

        GetTree().Root.ContentScaleSize = portrait ? PortraitBaseSize : LandscapeBaseSize;
    }

    private const string LanguageFilePath = "user://language.save";

    // Same plain-text-file pattern as Orientation above, for the same reason: applied in _Ready
    // before LoadSettings/the first frame, so the main menu never flashes in the wrong language.
    // First ever launch (no save file yet) defaults to the device's own language rather than a
    // fixed choice -- Spanish if the system locale is Spanish, English otherwise -- since the
    // store listing is already bilingual and a non-Spanish device is more likely to want English.
    public static string LoadLanguagePreference()
    {
        if (!FileAccess.FileExists(LanguageFilePath))
            return OS.GetLocaleLanguage().StartsWith("es") ? "es" : "en";
        using var file = FileAccess.Open(LanguageFilePath, FileAccess.ModeFlags.Read);
        string saved = file?.GetLine();
        return string.IsNullOrEmpty(saved) ? "es" : saved;
    }

    private static void SaveLanguagePreference(string code)
    {
        using var file = FileAccess.Open(LanguageFilePath, FileAccess.ModeFlags.Write);
        file?.StoreLine(code);
    }

    // "es" isn't a typo for a missing default -- every hardcoded string in the game already IS
    // Spanish (see docs/localization.md), so Spanish needs no translation CSV entries at all; it's
    // simply what TranslationServer falls back to showing when a key has no row for the active
    // locale. "en" is the first (and so far only) locale that actually has a CSV column.
    public string CurrentLanguage { get; private set; } = "es";

    // Called from the Options screen's language picker. Takes effect immediately: Godot re-translates
    // every already-displayed Control automatically on SetLocale, EXCEPT text that was built by
    // interpolating an already-translated template (the result no longer matches any CSV key) --
    // MainMenu re-renders those itself when Options closes (see its VisibilityChanged hook), since
    // Options is the only screen this picker is reachable from.
    public void SetLanguage(string code)
    {
        CurrentLanguage = code;
        SaveLanguagePreference(code);
        TranslationServer.SetLocale(code);
    }

    // Computed once in _Ready (see the top of this file) from whether a language save file already
    // existed at boot AND whether a player name is on record -- true only on a genuine first launch,
    // read once by MainMenu to decide whether to show OnboardingMenu.
    public bool ShouldShowOnboarding { get; private set; }

    // 1 per round survived, counted from round 1 (no free/unpaid opening rounds) — simple and
    // transparent enough that the player can predict it before the run ends, and it scales with skill
    // (a better run pays out more) without needing to weigh kills vs. score vs. coins into one
    // made-up formula. Retune after playtesting.
    private const int LibrasPerRound = 1;

    // Kept (rather than deleted) as the single knob for reintroducing an opening grace period later;
    // 0 means every round pays out from the start. Public so GameOverScreen can explain a 0-Libras
    // run without hardcoding the number itself.
    public const int LibrasFreeRounds = 0;

    private void RegisterFinalScore()
    {
        if (Score > LoadHighScore())
            SaveHighScore(Score);

        AppendRecord(Score, RoundNumber, CurrentGameMode);
        AppendCharacterRecord(SelectedCharacter, Score, RoundNumber, CurrentGameMode);

        // Read before ResetRun() zeroes RoundNumber. This is the single point both exit paths
        // (death via NotifyPlayerDied, manual quit via AbandonRun) already funnel through, so it
        // covers both without duplicating the award logic at each call site.
        LastRunLibrasEarned = Mathf.Max(0, RoundNumber - LibrasFreeRounds) * LibrasPerRound;
        AddLibras(LastRunLibrasEarned);
        LastRunAccountLevelsGained = AddAccountXp(LastRunLibrasEarned);
        LastRunCharacterLevelsGained = AddCharacterXp(SelectedCharacter, LastRunLibrasEarned);

        NotifyMissionRoundReached(RoundNumber);

        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        int critsThisRun = player?.CritsLandedThisRun ?? 0;
        int roundsClearedThisRun = Mathf.Max(0, RoundNumber - 1);
        // TotalEnemiesKilled/TotalBossesKilled/TotalCritsLanded/TotalRoundsCleared are no longer
        // batched here -- they're bumped live (RegisterKill, Player.ApplyCrit via NotifyCritLanded,
        // StartNextRound) the same way EverCompletedBuilds/EverGotLegendary already were, so the
        // matching achievements can notify mid-run instead of only at Game Over. critsThisRun/
        // roundsClearedThisRun above are still needed for RecordRunStats' per-run daily breakdown.
        BestRoundReached = Mathf.Max(BestRoundReached, RoundNumber);

        int playTimeSeconds = _runStartTimeMsec > 0 ? (int)((Time.GetTicksMsec() - _runStartTimeMsec) / 1000) : 0;
        _runStartTimeMsec = 0;
        RecordRunStats(EnemiesKilled, BossesKilled, critsThisRun, roundsClearedThisRun, playTimeSeconds);

        EvaluateAchievements(_unlockedAchievements);
        SaveMetaProgress();
    }

    // Set by RegisterFinalScore just before Game Over is shown, so GameOverScreen can display "+N"
    // without recomputing the formula itself or racing ResetRun's RoundNumber reset.
    public int LastRunLibrasEarned { get; private set; }
    public int LastRunAccountLevelsGained { get; private set; }
    public int LastRunCharacterLevelsGained { get; private set; }

    // --- Achievements and missions ---
    //
    // Lifetime counters, alongside AccountLevel/Libras above: unlike EnemiesKilled/RoundNumber
    // (purely per-run, zeroed by ResetRun), these only ever go up across the player's whole history.
    // Rolled up once, in RegisterFinalScore, from the run that just ended — no per-kill/per-crit disk
    // writes.
    public int TotalEnemiesKilled { get; private set; }
    public int TotalBossesKilled { get; private set; }
    public int TotalCritsLanded { get; private set; }
    public int TotalRoundsCleared { get; private set; }

    // Unlike Libras (drops on spending) or TotalCoinsEarned (reset every run), these two only ever
    // go up across the whole account. TotalLibrasEarned is bumped alongside every existing
    // `Libras +=` site (AddLibras, EvaluateAchievements, NotifyMissionProgress/RoundReached) via
    // RecordLibrasEarned; TotalRunsPlayed once per run end, in RecordRunStats.
    public int TotalLibrasEarned { get; private set; }
    public int TotalRunsPlayed { get; private set; }

    // Rounds a life can't already answer: TotalRoundsCleared sums every round crossed across every
    // run, but says nothing about any single run's best result. Updated in RegisterFinalScore
    // alongside the others.
    public int BestRoundReached { get; private set; }

    // Lifetime coin income — distinct from TotalCoinsEarned above, which is per-run (reset by
    // ResetRun, used for shop price inflation). Bumped by RecordCoinsEarned, called from AddCoins.
    public int TotalCoinsEarnedLifetime { get; private set; }

    // Real seconds spent in a run, from StartRoundOneTimer to RegisterFinalScore — includes
    // shop/pause time, same "close enough" tolerance as the rest of this catalog.
    public int TotalPlayTimeSeconds { get; private set; }

    // Bumped once per completed mission slot, in NotifyMissionProgress/NotifyMissionRoundReached.
    public int TotalMissionsCompleted { get; private set; }

    // Permanent, per-run-instant unlock records — same shape as UnlockedCharacters/OwnedCosmetics
    // above (a HashSet, nothing to accumulate), but sourced from Player rather than GameManager
    // itself, since builds/tiers are tracked on the per-run Player instance.
    public HashSet<Player.BuildClass> EverCompletedBuilds { get; private set; } = new();
    public HashSet<UpgradeType> EverGotLegendary { get; private set; } = new();

    // Newly unlocked achievements *this run*, filled by EvaluateAchievements -- called from several
    // live checkpoints now (RegisterKill, StartNextRound, NotifyBuildCompleted/NotifyLegendaryObtained,
    // RegisterFinalScore), not just at run end, so it accumulates across the whole run. Cleared once
    // per run in ResetRun (NOT inside EvaluateAchievements itself, which would wipe out anything
    // already unlocked earlier in the same run every time it's called live). GameOverScreen reveals
    // the final list without re-deriving anything itself.
    public List<AchievementDef> LastRunNewAchievements { get; private set; } = new();

    // Fired the instant an achievement crosses its threshold, from wherever EvaluateAchievements
    // happens to be called live -- same shape as LevelsGained above. A toast controller subscribes to
    // this to notify in the moment, separately from LastRunNewAchievements' end-of-run recap use.
    public event Action<AchievementDef> AchievementUnlocked;

    // Same shape as AchievementUnlocked, fired the instant a mission slot completes (from
    // NotifyMissionProgress/NotifyMissionRoundReached below) -- the string is the slot's display name
    // already resolved via MissionCatalog.FormatName, so a toast subscriber doesn't need to re-derive it.
    public event Action<MissionSlot, string> MissionCompleted;

    public void NotifyBuildCompleted(Player.BuildClass cls)
    {
        if (!EverCompletedBuilds.Add(cls)) return;
        EvaluateAchievements(_unlockedAchievements);
        SaveMetaProgress();
    }

    public void NotifyLegendaryObtained(UpgradeType type)
    {
        if (!EverGotLegendary.Add(type)) return;
        EvaluateAchievements(_unlockedAchievements);
        SaveMetaProgress();
    }

    // Live counterpart to Player.CritsLandedThisRun -- TotalCritsLanded used to only roll up in
    // RegisterFinalScore (see there), which meant Puntería could never notify mid-run. Called from
    // Player.ApplyCrit right alongside CritsLandedThisRun++. No SaveMetaProgress here on purpose: a
    // crit can land many times a second, and this in-memory bump is durable enough by the next save
    // checkpoint (kill, round start, run end) the same way EnemiesKilled/BossesKilled already are.
    public void NotifyCritLanded()
    {
        TotalCritsLanded++;
    }

    // Compares every achievement's unlocked state before vs. after -- safe to call from many live
    // checkpoints (RegisterKill, StartNextRound, NotifyBuildCompleted, NotifyLegendaryObtained,
    // RegisterFinalScore) since it's idempotent per achievement id; it never double-pays one, it just
    // never misses whichever moment actually crossed the threshold.
    private void EvaluateAchievements(HashSet<string> alreadyUnlocked)
    {
        foreach (var def in AchievementCatalog.All)
        {
            if (alreadyUnlocked.Contains(def.Id)) continue;
            if (!AchievementCatalog.IsUnlocked(def, this)) continue;

            alreadyUnlocked.Add(def.Id);
            LastRunNewAchievements.Add(def);
            Libras += def.RewardLibras;
            RecordLibrasEarned(def.RewardLibras);
            AchievementUnlocked?.Invoke(def);
        }
        _unlockedAchievements = alreadyUnlocked;
    }

    // Which achievement ids already paid out, so EvaluateAchievements never double-pays one across
    // runs. Persisted the same "one HashSet, save/load as string[]" way as RedeemedCodes.
    private HashSet<string> _unlockedAchievements = new();
    public bool IsAchievementUnlocked(string id) => _unlockedAchievements.Contains(id);
    public int UnlockedAchievementsCount => _unlockedAchievements.Count;

    // --- Missions ---
    //
    // 3 slots, rotated daily. Progress only checked at the same low-frequency points the rest of the
    // meta-progression already touches (kill, boss kill, round end, Libras earned, cosmetic bought) —
    // no new per-frame or per-hit hook.
    public class MissionSlot
    {
        public string TemplateId;
        public int Target;
        public int Reward;
        public int Progress;
        public bool Completed;
    }

    public MissionSlot[] Missions { get; private set; } = new MissionSlot[3];
    private string _missionsDate = "";

    // "aaaa-mm-dd", the same Time.GetDatetimeDictFromSystem() source FormatNow already uses —
    // just unabbreviated and zero-order so string comparison across days works without parsing.
    private static string TodayKey()
    {
        var now = Time.GetDatetimeDictFromSystem();
        return $"{now["year"].AsInt32():D4}-{now["month"].AsInt32():D2}-{now["day"].AsInt32():D2}";
    }

    // Called from _Ready (via LoadSettings) and whenever the Achievements screen opens, so a
    // still-running session picks up the new day's missions without needing a restart.
    public void EnsureMissionsForToday()
    {
        string today = TodayKey();
        if (_missionsDate == today && Missions[0] != null) return;

        _missionsDate = today;

        // Seeded off the date so re-launching the game the same day rolls the same 3 missions
        // instead of re-randomizing on every boot.
        var rng = new Random(today.GetHashCode());
        var pool = new List<MissionTemplate>(MissionCatalog.Pool);
        for (int i = 0; i < Missions.Length; i++)
        {
            if (pool.Count == 0) pool = new List<MissionTemplate>(MissionCatalog.Pool);
            int idx = rng.Next(pool.Count);
            var template = pool[idx];
            pool.RemoveAt(idx);

            int optionIdx = rng.Next(template.TargetOptions.Length);
            Missions[i] = new MissionSlot
            {
                TemplateId = template.Id,
                Target = template.TargetOptions[optionIdx],
                Reward = template.RewardOptions[optionIdx],
                Progress = 0,
                Completed = false,
            };
        }

        SaveMetaProgress();
    }

    private static MissionKind KindForTemplateId(string templateId)
    {
        foreach (var t in MissionCatalog.Pool)
            if (t.Id == templateId) return t.Kind;
        return MissionKind.Kill;
    }

    private void NotifyMissionProgress(MissionKind kind, int amount)
    {
        if (amount <= 0 || Missions[0] == null) return;

        bool changed = false;
        foreach (var slot in Missions)
        {
            if (slot == null || slot.Completed) continue;
            if (KindForTemplateId(slot.TemplateId) != kind) continue;

            slot.Progress = Math.Min(slot.Target, slot.Progress + amount);
            if (slot.Progress >= slot.Target)
            {
                slot.Completed = true;
                Libras += slot.Reward;
                RecordLibrasEarned(slot.Reward);
                TotalMissionsCompleted++;
                MissionCompleted?.Invoke(slot, MissionCatalog.FormatName(slot));
            }
            changed = true;
        }

        if (changed) SaveMetaProgress();
    }

    // RoundReached is absolute (not incremental like Kill/LibrasEarned), so it goes through its own
    // setter rather than NotifyMissionProgress's additive amount.
    private void NotifyMissionRoundReached(int round)
    {
        if (Missions[0] == null) return;

        bool changed = false;
        foreach (var slot in Missions)
        {
            if (slot == null || slot.Completed) continue;
            if (KindForTemplateId(slot.TemplateId) != MissionKind.RoundReached) continue;

            slot.Progress = Math.Min(slot.Target, Math.Max(slot.Progress, round));
            if (slot.Progress >= slot.Target)
            {
                slot.Completed = true;
                Libras += slot.Reward;
                RecordLibrasEarned(slot.Reward);
                TotalMissionsCompleted++;
                MissionCompleted?.Invoke(slot, MissionCatalog.FormatName(slot));
            }
            changed = true;
        }

        if (changed) SaveMetaProgress();
    }

    // --- Stats screen (Total / Mes / Semana) ---
    //
    // The 6 Total* counters above answer "how much, ever" but not "how much this week" — that needs
    // a per-day breakdown. This is that breakdown: one entry per calendar day, summed on demand by
    // GetStats. "Total" never reads this map (it reads the lifetime counters directly), so pruning
    // old entries is always safe.
    private struct DailyStats
    {
        public int EnemiesKilled, BossesKilled, CritsLanded, RoundsCleared, LibrasEarned, RunsPlayed,
            CoinsEarned, PlayTimeSeconds;
    }

    private readonly Dictionary<string, DailyStats> _dailyStats = new();
    private const int DailyStatsRetentionDays = 40;

    private DailyStats TodayDailyStats() => _dailyStats.TryGetValue(TodayKey(), out var entry) ? entry : default;

    // Called from every existing `Libras +=` site (AddLibras, EvaluateAchievements,
    // NotifyMissionProgress/NotifyMissionRoundReached) so both the lifetime total and the daily log
    // capture every source Libras can come from, not just the end-of-run payout.
    private void RecordLibrasEarned(int amount)
    {
        if (amount <= 0) return;
        TotalLibrasEarned += amount;
        var entry = TodayDailyStats();
        entry.LibrasEarned += amount;
        _dailyStats[TodayKey()] = entry;
    }

    // Called from AddCoins — every source of coin income, not just kills — same shape as
    // RecordLibrasEarned above but for the lifetime coin total.
    private void RecordCoinsEarned(int amount)
    {
        if (amount <= 0) return;
        TotalCoinsEarnedLifetime += amount;
        var entry = TodayDailyStats();
        entry.CoinsEarned += amount;
        _dailyStats[TodayKey()] = entry;
    }

    // Called once per run end, from RegisterFinalScore — the same funnel that already rolls this
    // run's counters into TotalEnemiesKilled/TotalBossesKilled/TotalCritsLanded/TotalRoundsCleared.
    private void RecordRunStats(int enemiesKilled, int bossesKilled, int critsLanded, int roundsCleared, int playTimeSeconds)
    {
        TotalRunsPlayed++;
        TotalPlayTimeSeconds += playTimeSeconds;
        var entry = TodayDailyStats();
        entry.EnemiesKilled += enemiesKilled;
        entry.BossesKilled += bossesKilled;
        entry.CritsLanded += critsLanded;
        entry.RoundsCleared += roundsCleared;
        entry.RunsPlayed += 1;
        entry.PlayTimeSeconds += playTimeSeconds;
        _dailyStats[TodayKey()] = entry;
        PruneOldDailyStats();
    }

    private void PruneOldDailyStats()
    {
        string cutoff = DateTime.Today.AddDays(-DailyStatsRetentionDays).ToString("yyyy-MM-dd");
        List<string> stale = null;
        foreach (var key in _dailyStats.Keys)
        {
            if (string.CompareOrdinal(key, cutoff) >= 0) continue;
            stale ??= new List<string>();
            stale.Add(key);
        }
        if (stale == null) return;
        foreach (var key in stale) _dailyStats.Remove(key);
    }

    public enum StatsPeriod { Total, Month, Week }

    // The period-filterable "how much did I do" numbers. Everything that isn't a sum over time —
    // current level, best-ever round, collection counts — lives directly on GameManager instead and
    // is read straight from there by the UI, since "this week's account level" isn't a meaningful
    // question.
    public readonly record struct LifetimeStats(
        int EnemiesKilled, int BossesKilled, int CritsLanded, int RoundsCleared,
        int LibrasEarned, int RunsPlayed, int CoinsEarned, int PlayTimeSeconds);

    public LifetimeStats GetStats(StatsPeriod period)
    {
        if (period == StatsPeriod.Total)
            return new LifetimeStats(TotalEnemiesKilled, TotalBossesKilled, TotalCritsLanded,
                TotalRoundsCleared, TotalLibrasEarned, TotalRunsPlayed, TotalCoinsEarnedLifetime,
                TotalPlayTimeSeconds);

        string cutoffKey = period == StatsPeriod.Week ? ThisWeekStartKey() : ThisMonthStartKey();
        int enemies = 0, bosses = 0, crits = 0, rounds = 0, libras = 0, runs = 0, coins = 0, playTime = 0;
        foreach (var kv in _dailyStats)
        {
            if (string.CompareOrdinal(kv.Key, cutoffKey) < 0) continue;
            enemies += kv.Value.EnemiesKilled;
            bosses += kv.Value.BossesKilled;
            crits += kv.Value.CritsLanded;
            rounds += kv.Value.RoundsCleared;
            libras += kv.Value.LibrasEarned;
            runs += kv.Value.RunsPlayed;
            coins += kv.Value.CoinsEarned;
            playTime += kv.Value.PlayTimeSeconds;
        }
        return new LifetimeStats(enemies, bosses, crits, rounds, libras, runs, coins, playTime);
    }

    // One entry per of the last `days` calendar days, oldest first, for the Stats screen's trend
    // chart. A day with no run played comes back as 0 rather than being omitted, so the chart's
    // x-axis stays evenly spaced instead of compressing around whichever days actually have data.
    public readonly record struct DailyActivity(string DateKey, int EnemiesKilled);

    public List<DailyActivity> GetRecentActivity(int days)
    {
        var result = new List<DailyActivity>(days);
        var today = DateTime.Today;
        for (int i = days - 1; i >= 0; i--)
        {
            string key = today.AddDays(-i).ToString("yyyy-MM-dd");
            int kills = _dailyStats.TryGetValue(key, out var entry) ? entry.EnemiesKilled : 0;
            result.Add(new DailyActivity(key, kills));
        }
        return result;
    }

    private static string ThisMonthStartKey()
    {
        var today = DateTime.Today;
        return new DateTime(today.Year, today.Month, 1).ToString("yyyy-MM-dd");
    }

    // Monday-start calendar week. DayOfWeek is Sunday=0..Saturday=6; (+6)%7 turns that into "days
    // since Monday" (Sunday -> 6, Monday -> 0, ...).
    private static string ThisWeekStartKey()
    {
        var today = DateTime.Today;
        int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        return today.AddDays(-daysSinceMonday).ToString("yyyy-MM-dd");
    }

    // --- Settings and records (ConfigFile) ---
    //
    // HighScore and Orientation above are one-value-per-file plain text, which was fine for a single int
    // each. A slider value plus a dated top-ten list is structured data, so new persistence goes through
    // ConfigFile rather than adding more one-line files.

    private const string SettingsFilePath = "user://settings.cfg";
    private const string SettingsSection = "settings";

    // 0 = fully transparent, 1 = fully opaque. Applied as Modulate.A on the joystick's root Control,
    // which propagates to both of its Polygon2D children and multiplies their own alphas (0.28 base /
    // 0.85 knob), preserving the contrast between them. Note Modulate does not affect hit-testing, so
    // the joystick keeps working even at 0 — which is the point of allowing 0 at all.
    public float JoystickOpacity { get; private set; } = 1f;

    public void SetJoystickOpacity(float opacity)
    {
        JoystickOpacity = Mathf.Clamp(opacity, 0f, 1f);

        var config = new ConfigFile();
        config.Load(SettingsFilePath);   // a missing file just means "defaults", not an error worth acting on
        config.SetValue(SettingsSection, "joystick_opacity", JoystickOpacity);
        config.Save(SettingsFilePath);

        ApplyJoystickOpacity();
    }

    // Called on boot and whenever the slider moves. Safe to call with no joystick in the tree (the main
    // menu has none) — it simply does nothing until a run starts and VirtualJoystick reads the value in
    // its own _Ready.
    public void ApplyJoystickOpacity()
    {
        if (GetTree().GetFirstNodeInGroup("virtual_joystick") is CanvasItem joystick)
            joystick.Modulate = new Color(1f, 1f, 1f, JoystickOpacity);
    }

    // Same shape as JoystickOpacity above, for the circular Ultimate button (HUD.tscn). Applied as
    // Modulate on the button, which propagates to the icon child; Modulate doesn't affect
    // hit-testing, so at 0% the button is invisible but still fully tappable — the same rule the
    // joystick already follows.
    public float UltimateButtonOpacity { get; private set; } = 0.65f;

    public void SetUltimateButtonOpacity(float opacity)
    {
        UltimateButtonOpacity = Mathf.Clamp(opacity, 0f, 1f);

        var config = new ConfigFile();
        config.Load(SettingsFilePath);
        config.SetValue(SettingsSection, "ultimate_button_opacity", UltimateButtonOpacity);
        config.Save(SettingsFilePath);

        ApplyUltimateButtonOpacity();
    }

    public void ApplyUltimateButtonOpacity()
    {
        if (GetTree().GetFirstNodeInGroup("ultimate_button") is CanvasItem button)
            button.Modulate = new Color(1f, 1f, 1f, UltimateButtonOpacity);
    }

    // Which character was picked at the character-select screen (CharacterSelectMenu), read by
    // Player._Ready() when a run starts. Persisted the same way as the opacity sliders above, so
    // the last pick is remembered across sessions instead of resetting to Equilibrado every launch.
    // A slug rather than an enum value, because player-created characters have no compile-time
    // identity — see CharacterCatalog.
    public string SelectedCharacter { get; private set; } = CharacterCatalog.DefaultSlug;

    public void SetSelectedCharacter(string slug)
    {
        SelectedCharacter = slug;

        var config = new ConfigFile();
        config.Load(SettingsFilePath);
        config.SetValue(SettingsSection, "selected_character_slug", slug);
        config.Save(SettingsFilePath);
    }

    // The account's own display name, set once at OnboardingMenu and shown in MainMenu's top-center
    // identity box -- distinct from a pilot/character name (CharacterCatalog), which names the ship
    // being flown, not the person flying it. Empty until set is exactly the signal
    // ShouldShowOnboarding above reads, so there's no separate "has it been set" flag to keep in sync.
    public string PlayerName { get; private set; } = "";

    public void SetPlayerName(string name)
    {
        PlayerName = name;

        var config = new ConfigFile();
        config.Load(SettingsFilePath);
        config.SetValue(SettingsSection, "player_name", name);
        config.Save(SettingsFilePath);
    }

    // Clásico is the game as it's always been; Hardcore pins the player to exactly 1 heart forever
    // (Player._Ready) and strips Heart/Shield rewards out of every pool before they're even rolled
    // (UpgradeData.BuildCatalog) rather than filtering them out after the fact — see that method for
    // why a post-hoc filter isn't enough (PickFromTier falls back to the unfiltered pool once a
    // filtered one comes up empty).
    public enum GameMode { Classic, Hardcore }

    public GameMode CurrentGameMode { get; private set; } = GameMode.Classic;

    public void SetGameMode(GameMode mode)
    {
        CurrentGameMode = mode;

        var config = new ConfigFile();
        config.Load(SettingsFilePath);
        config.SetValue(SettingsSection, "game_mode", (int)mode);
        config.Save(SettingsFilePath);
    }

    // Reduced motion. The actual flag lives on DangerLevel (which is where the consumers already read
    // it from and which knows nothing about the scene tree); this is the persisted, user-facing half.
    // It existed as a hardcoded `false` with a comment saying it was there so wiring a toggle later
    // would be one line rather than a refactor — this is that line being cashed in.
    public bool ReducedMotion { get; private set; }

    public void SetReducedMotion(bool enabled)
    {
        ReducedMotion = enabled;
        DangerLevel.Reduced = enabled;

        var config = new ConfigFile();
        config.Load(SettingsFilePath);
        config.SetValue(SettingsSection, "reduced_motion", enabled);
        config.Save(SettingsFilePath);
    }

    // Gates the joke roster's visibility (CharacterCatalog.IsVisible) -- redeemed once via the MDG
    // secret code (SecretCodeCatalog), same set-and-save-immediately shape as SetReducedMotion above
    // rather than routing through SaveMetaProgress, since this is one flag with nothing else to batch.
    public bool CoworkerRosterUnlocked { get; private set; }

    public void UnlockCoworkerRoster()
    {
        if (CoworkerRosterUnlocked) return;
        CoworkerRosterUnlocked = true;

        var config = new ConfigFile();
        config.Load(SettingsFilePath);
        config.SetValue(SettingsSection, "coworker_roster_unlocked", true);
        config.Save(SettingsFilePath);
    }

    // Three sliders, each 0 = silent and 1 = full, each mapped onto its own audio bus. None of them
    // gets a mute toggle: reaching 0 already is the mute, exactly as it is for the two opacity
    // settings above.
    //
    // Master needs no multiplication against the other two — SFX and Music send to it in
    // default_bus_layout.tres, so the mixing is the bus graph's job, not this class's.
    //
    // The defaults live here on the declarations rather than in LoadSettings, because that method
    // returns early when there's no settings file yet and never reaches its own defaults on a
    // first-ever launch.
    public float MasterVolume { get; private set; } = 1f;
    public float SfxVolume { get; private set; } = 1f;
    public float MusicVolume { get; private set; } = 0.7f;

    public void SetMasterVolume(float volume) =>
        SetVolume(volume, "master_volume", AudioManager.MasterBus, v => MasterVolume = v);

    public void SetSfxVolume(float volume) =>
        SetVolume(volume, "sfx_volume", AudioManager.SfxBus, v => SfxVolume = v);

    public void SetMusicVolume(float volume) =>
        SetVolume(volume, "music_volume", AudioManager.MusicBus, v => MusicVolume = v);

    // The three setters differ only in which key they write and which bus they drive, so the shape
    // (clamp → ConfigFile → Load → SetValue → Save → apply) lives once.
    private void SetVolume(float volume, string key, string bus, Action<float> assign)
    {
        float clamped = Mathf.Clamp(volume, 0f, 1f);
        assign(clamped);

        var config = new ConfigFile();
        config.Load(SettingsFilePath);
        config.SetValue(SettingsSection, key, clamped);
        config.Save(SettingsFilePath);

        AudioManager.Instance?.ApplyBusVolume(bus, clamped);
    }

    // The persistent, cross-run currency — deliberately not Coins (which resets to 0 every run,
    // AddCoins/SpendCoins never touch disk) and not Score (a run-scoped counter whose only
    // persistence is a read-only high score/records list). Named "Libras" everywhere player-facing
    // specifically so it can't be confused with either — docs/economy.md already flags "confusing
    // the two currencies" as the most common bug source here, and this makes it three. (Formerly
    // called Núcleos; renamed with no other behavior change beyond RegisterFinalScore's formula.)
    //
    // Persisted immediately on every change, unlike Coins: this has to survive the app being killed
    // mid-run on a phone, not just a clean return to the main menu.
    public int Libras { get; private set; }

    // Slugs the player has spent Libras on. A HashSet, not a count — unlocking is permanent
    // and per-character, there's nothing to accumulate. Built-ins that don't require unlocking (see
    // CharacterCatalog.CharacterInfo.RequiresUnlock) and every custom character never consult this
    // at all, so it only ever needs entries for the handful of gated built-ins.
    public HashSet<string> UnlockedCharacters { get; private set; } = new();

    public void AddLibras(int amount)
    {
        if (amount <= 0) return;
        Libras += amount;
        RecordLibrasEarned(amount);
        NotifyMissionProgress(MissionKind.LibrasEarned, amount);
        SaveMetaProgress();
    }

    // Returns false (spending nothing) if the character is already unlocked, doesn't require
    // unlocking at all, or the player can't afford it — callers can treat any false as "nothing to
    // do" without needing to distinguish why.
    // Granting without spending, for secret codes. Deliberately separate from TryUnlockCharacter and
    // TryBuyCosmetic rather than a "free" flag on them: those two are the *purchase* path and their
    // affordability check is the whole point, so a caller that skips it should have to say so by
    // name. Both are idempotent, since a code that grants something already owned is a no-op, not an
    // error the caller has to handle.
    public void GrantCharacter(string slug)
    {
        if (!UnlockedCharacters.Add(slug)) return;
        SaveMetaProgress();
    }

    public void GrantCosmetic(CosmeticCategory category, string id)
    {
        if (!OwnedCosmetics.Add(CosmeticCatalog.ItemKey(category, id))) return;
        SaveMetaProgress();
    }

    // Codes already redeemed, so each one pays out once. Stored normalised (see
    // SecretCodeCatalog.Normalise) so casing and stray spaces can't buy a second payout.
    public HashSet<string> RedeemedCodes { get; private set; } = new();

    public CodeRedeemResult RedeemCode(string input)
    {
        string normalised = SecretCodeCatalog.Normalise(input);
        if (!SecretCodeCatalog.TryGet(normalised, out var code)) return CodeRedeemResult.Unknown;
        if (RedeemedCodes.Contains(normalised)) return CodeRedeemResult.AlreadyUsed;

        // Recorded before the grant runs: every grant below ends in SaveMetaProgress(), and if the
        // code weren't already in the set that save would persist the reward without the record of
        // it — leaving it redeemable again on the next launch.
        RedeemedCodes.Add(normalised);
        code.Grant(this);
        SaveMetaProgress();
        return CodeRedeemResult.Ok;
    }

    public bool TryUnlockCharacter(string slug, int cost)
    {
        if (UnlockedCharacters.Contains(slug) || Libras < cost) return false;

        Libras -= cost;
        UnlockedCharacters.Add(slug);
        SaveMetaProgress();
        return true;
    }

    // Libras' second sink, alongside UnlockedCharacters — same shape (a HashSet of permanent
    // purchases, no signal, UI re-reads on its own). Keyed by CosmeticCatalog.ItemKey(category, id)
    // rather than by id alone, since owning a color for Bullet says nothing about owning it for Trail.
    public HashSet<string> OwnedCosmetics { get; private set; } = new();

    // One entry per category rather than a property each. With four categories the properties were
    // merely repetitive; the moment there were eight, every new one cost a property, a switch case, a
    // save line and a load line — four chances to add seven and forget the eighth. Now a category is
    // just an enum member.
    private readonly Dictionary<CosmeticCategory, string> _equippedCosmetics = new();

    /// <summary>The cosmetic id equipped for a category, or "original" if none is.</summary>
    public string EquippedCosmetic(CosmeticCategory category) =>
        _equippedCosmetics.TryGetValue(category, out string id) ? id : CosmeticCatalog.DefaultId;

    /// <summary>The colour a render site should paint, falling back to its own default.</summary>
    // The shorthand nearly every consumer wants, so they don't each repeat the Resolve call.
    public Color CosmeticColor(CosmeticCategory category, Color baseColor) =>
        CosmeticCatalog.Resolve(EquippedCosmetic(category), baseColor);

    // The ConfigFile key for a category. Derived rather than listed so a new category persists with no
    // extra code — and it reproduces the four keys that already exist in players' settings.cfg
    // ("equipped_bullet_cosmetic" and friends), so nothing anyone has equipped is lost.
    private static string CosmeticKey(CosmeticCategory category) =>
        $"equipped_{category.ToString().ToLowerInvariant()}_cosmetic";

    // "Original" is always owned without an OwnedCosmetics entry — it's the free, no-purchase-needed
    // way back to how the game looked before this feature existed, not something anyone had to buy.
    public bool IsCosmeticOwned(CosmeticCategory category, string id) =>
        id == CosmeticCatalog.DefaultId || OwnedCosmetics.Contains(CosmeticCatalog.ItemKey(category, id));

    public bool TryBuyCosmetic(CosmeticCategory category, string id, int cost)
    {
        if (IsCosmeticOwned(category, id) || Libras < cost) return false;

        Libras -= cost;
        OwnedCosmetics.Add(CosmeticCatalog.ItemKey(category, id));
        NotifyMissionProgress(MissionKind.CosmeticPurchase, 1);
        SaveMetaProgress();
        return true;
    }

    // No-op (not a false return) if the id isn't owned — the shop UI never offers an Equip action on
    // an unowned swatch, so reaching this without ownership would be a caller bug, not a user action
    // to report false for.
    public void EquipCosmetic(CosmeticCategory category, string id)
    {
        if (!IsCosmeticOwned(category, id)) return;

        _equippedCosmetics[category] = id;
        SaveMetaProgress();
    }

    // A second, independent track of permanent progression — separate from Libras (spendable) the
    // same way in-run Level is separate from Coins. Nothing consumes AccountLevel yet; it exists so
    // the player has a number that only ever goes up across their whole history with the game, ahead
    // of deciding what unlocks it should drive. Same threshold-growth shape as the in-run Level/Xp
    // pair above, just re-scaled: Libras per run are small (rarely above ~15), so the curve starts
    // much lower.
    public int AccountLevel { get; private set; } = 1;
    public int AccountXp { get; private set; }
    public int AccountXpToNextLevel { get; private set; } = 5;

    private const float AccountXpGrowthFactor = 1.35f;

    // Returns how many levels this call crossed, so RegisterFinalScore can hand GameOverScreen a
    // "+N nivel de cuenta" without it recomputing the curve itself.
    private int AddAccountXp(int amount)
    {
        if (amount <= 0) return 0;

        AccountXp += amount;
        int levelsGained = 0;
        while (AccountXp >= AccountXpToNextLevel)
        {
            AccountXp -= AccountXpToNextLevel;
            AccountLevel++;
            AccountXpToNextLevel = Mathf.RoundToInt(AccountXpToNextLevel * AccountXpGrowthFactor);
            levelsGained++;
        }

        SaveMetaProgress();
        return levelsGained;
    }

    // A THIRD progression track, alongside AccountLevel — one per character slug rather than one for
    // the whole account, so switching pilots doesn't blend their progress into a single number and a
    // brand-new pilot always starts at Level 1 regardless of how long the account has played. Built-ins
    // and custom characters (CharacterCatalog.CharacterInfo.IsCustom) are keyed the same way, by slug,
    // so this needs no special-casing between them.
    private readonly Dictionary<string, int> _characterLevel = new();
    private readonly Dictionary<string, int> _characterXp = new();
    private readonly Dictionary<string, int> _characterXpToNext = new();

    private const int CharacterXpToNextLevelStart = 5;
    private const float CharacterXpGrowthFactor = 1.35f;

    public int GetCharacterLevel(string slug) => _characterLevel.GetValueOrDefault(slug, 1);
    public int GetCharacterXp(string slug) => _characterXp.GetValueOrDefault(slug, 0);
    public int GetCharacterXpToNextLevel(string slug) => _characterXpToNext.GetValueOrDefault(slug, CharacterXpToNextLevelStart);

    // Same "1 Libra earned = 1 XP" rule as AddAccountXp, on whichever pilot was actually flown —
    // GameManager.SelectedCharacter still names that pilot when this runs, since ResetRun never
    // touches it (it's the persisted choice, not run state) and the next selection only happens back
    // on the character-select screen. Returns levels gained, same reason as AddAccountXp.
    private int AddCharacterXp(string slug, int amount)
    {
        if (amount <= 0) return 0;

        int level = GetCharacterLevel(slug);
        int xp = GetCharacterXp(slug) + amount;
        int xpToNext = GetCharacterXpToNextLevel(slug);

        int levelsGained = 0;
        while (xp >= xpToNext)
        {
            xp -= xpToNext;
            level++;
            xpToNext = Mathf.RoundToInt(xpToNext * CharacterXpGrowthFactor);
            levelsGained++;
        }

        _characterLevel[slug] = level;
        _characterXp[slug] = xp;
        _characterXpToNext[slug] = xpToNext;

        SaveMetaProgress();
        return levelsGained;
    }

    private const string CharacterProgressSection = "character_progress";
    private const string MissionsSection = "missions";
    private const string StatsDailySection = "stats_daily";

    private void SaveMetaProgress()
    {
        var config = new ConfigFile();
        config.Load(SettingsFilePath);
        config.SetValue(SettingsSection, "libras", Libras);
        config.SetValue(SettingsSection, "unlocked_characters", new List<string>(UnlockedCharacters).ToArray());
        config.SetValue(SettingsSection, "owned_cosmetics", new List<string>(OwnedCosmetics).ToArray());
        config.SetValue(SettingsSection, "redeemed_codes", new List<string>(RedeemedCodes).ToArray());
        foreach (CosmeticCategory category in System.Enum.GetValues<CosmeticCategory>())
            config.SetValue(SettingsSection, CosmeticKey(category), EquippedCosmetic(category));
        config.SetValue(SettingsSection, "account_level", AccountLevel);
        config.SetValue(SettingsSection, "account_xp", AccountXp);
        config.SetValue(SettingsSection, "account_xp_to_next", AccountXpToNextLevel);

        config.SetValue(SettingsSection, "total_enemies_killed", TotalEnemiesKilled);
        config.SetValue(SettingsSection, "total_bosses_killed", TotalBossesKilled);
        config.SetValue(SettingsSection, "total_crits_landed", TotalCritsLanded);
        config.SetValue(SettingsSection, "total_rounds_cleared", TotalRoundsCleared);
        config.SetValue(SettingsSection, "total_libras_earned", TotalLibrasEarned);
        config.SetValue(SettingsSection, "total_runs_played", TotalRunsPlayed);
        config.SetValue(SettingsSection, "best_round_reached", BestRoundReached);
        config.SetValue(SettingsSection, "total_coins_earned_lifetime", TotalCoinsEarnedLifetime);
        config.SetValue(SettingsSection, "total_play_time_seconds", TotalPlayTimeSeconds);
        config.SetValue(SettingsSection, "total_missions_completed", TotalMissionsCompleted);

        foreach (var kv in _dailyStats)
        {
            var d = kv.Value;
            config.SetValue(StatsDailySection, kv.Key,
                $"{d.EnemiesKilled}|{d.BossesKilled}|{d.CritsLanded}|{d.RoundsCleared}|{d.LibrasEarned}|{d.RunsPlayed}|{d.CoinsEarned}|{d.PlayTimeSeconds}");
        }
        var everCompletedBuilds = new List<string>();
        foreach (var cls in EverCompletedBuilds) everCompletedBuilds.Add(cls.ToString());
        config.SetValue(SettingsSection, "ever_completed_builds", everCompletedBuilds.ToArray());

        var everGotLegendary = new List<string>();
        foreach (var type in EverGotLegendary) everGotLegendary.Add(type.ToString());
        config.SetValue(SettingsSection, "ever_got_legendary", everGotLegendary.ToArray());

        config.SetValue(SettingsSection, "unlocked_achievements", new List<string>(_unlockedAchievements).ToArray());

        config.SetValue(MissionsSection, "date", _missionsDate);
        for (int i = 0; i < Missions.Length; i++)
        {
            var slot = Missions[i];
            config.SetValue(MissionsSection, $"slot_{i}",
                slot == null ? "" : $"{slot.TemplateId}|{slot.Target}|{slot.Reward}|{slot.Progress}|{slot.Completed}");
        }

        // One key per slug ("level|xp|xpToNext") rather than three parallel arrays — a slug is never
        // dropped from the middle of an array (custom characters can be deleted, but that just leaves
        // an orphaned key here, same as it already does for UnlockedCharacters), and GetSectionKeys
        // means loading doesn't need to know the full slug list up front.
        foreach (var kv in _characterLevel)
        {
            string slug = kv.Key;
            config.SetValue(CharacterProgressSection, slug,
                $"{kv.Value}|{GetCharacterXp(slug)}|{GetCharacterXpToNextLevel(slug)}");
        }

        config.Save(SettingsFilePath);
    }

    private void LoadSettings()
    {
        var config = new ConfigFile();
        if (config.Load(SettingsFilePath) != Error.Ok) return;
        JoystickOpacity = Mathf.Clamp((float)config.GetValue(SettingsSection, "joystick_opacity", 1f), 0f, 1f);
        UltimateButtonOpacity = Mathf.Clamp((float)config.GetValue(SettingsSection, "ultimate_button_opacity", 0.65f), 0f, 1f);

        // Pushed straight onto DangerLevel here rather than waiting for a screen to apply it: the
        // consumers are static and scene-independent, so the flag has to be correct from boot, before
        // any UI exists to set it.
        ReducedMotion = (bool)config.GetValue(SettingsSection, "reduced_motion", false);
        DangerLevel.Reduced = ReducedMotion;
        CoworkerRosterUnlocked = (bool)config.GetValue(SettingsSection, "coworker_roster_unlocked", false);

        // Not pushed to AudioManager here: that autoload is declared after this one and doesn't
        // exist yet on the first boot frame. It reads these values itself in its own _Ready.
        MasterVolume = Mathf.Clamp((float)config.GetValue(SettingsSection, "master_volume", 1f), 0f, 1f);
        SfxVolume = Mathf.Clamp((float)config.GetValue(SettingsSection, "sfx_volume", 1f), 0f, 1f);
        MusicVolume = Mathf.Clamp((float)config.GetValue(SettingsSection, "music_volume", 0.7f), 0f, 1f);

        // The selection used to be stored as an index into the built-in cast. Fall back to it when
        // the slug key is absent, so an existing settings.cfg keeps whoever it had picked instead of
        // silently resetting to Equilibrado.
        string slug = (string)config.GetValue(SettingsSection, "selected_character_slug", "");
        if (string.IsNullOrEmpty(slug))
            slug = CharacterCatalog.SlugForLegacyIndex((int)config.GetValue(SettingsSection, "selected_character", 0));

        SelectedCharacter = slug;

        PlayerName = (string)config.GetValue(SettingsSection, "player_name", "");

        CurrentGameMode = (GameMode)(int)config.GetValue(SettingsSection, "game_mode", (int)GameMode.Classic);

        // Falls back to the old "meta_currency" key so a save from before the Núcleos → Libras rename
        // doesn't lose an existing balance.
        Libras = (int)config.GetValue(SettingsSection, "libras",
            config.GetValue(SettingsSection, "meta_currency", 0));
        var unlocked = (string[])config.GetValue(SettingsSection, "unlocked_characters", System.Array.Empty<string>());
        UnlockedCharacters = new HashSet<string>(unlocked);

        var ownedCosmetics = (string[])config.GetValue(SettingsSection, "owned_cosmetics", System.Array.Empty<string>());
        OwnedCosmetics = new HashSet<string>(ownedCosmetics);
        var redeemed = (string[])config.GetValue(SettingsSection, "redeemed_codes", System.Array.Empty<string>());
        RedeemedCodes = new HashSet<string>(redeemed);
        _equippedCosmetics.Clear();
        foreach (CosmeticCategory category in System.Enum.GetValues<CosmeticCategory>())
            _equippedCosmetics[category] =
                (string)config.GetValue(SettingsSection, CosmeticKey(category), CosmeticCatalog.DefaultId);

        AccountLevel = (int)config.GetValue(SettingsSection, "account_level", 1);
        AccountXp = (int)config.GetValue(SettingsSection, "account_xp", 0);
        AccountXpToNextLevel = (int)config.GetValue(SettingsSection, "account_xp_to_next", 5);

        TotalEnemiesKilled = (int)config.GetValue(SettingsSection, "total_enemies_killed", 0);
        TotalBossesKilled = (int)config.GetValue(SettingsSection, "total_bosses_killed", 0);
        TotalCritsLanded = (int)config.GetValue(SettingsSection, "total_crits_landed", 0);
        TotalRoundsCleared = (int)config.GetValue(SettingsSection, "total_rounds_cleared", 0);
        TotalLibrasEarned = (int)config.GetValue(SettingsSection, "total_libras_earned", 0);
        TotalRunsPlayed = (int)config.GetValue(SettingsSection, "total_runs_played", 0);
        BestRoundReached = (int)config.GetValue(SettingsSection, "best_round_reached", 0);
        TotalCoinsEarnedLifetime = (int)config.GetValue(SettingsSection, "total_coins_earned_lifetime", 0);
        TotalPlayTimeSeconds = (int)config.GetValue(SettingsSection, "total_play_time_seconds", 0);
        TotalMissionsCompleted = (int)config.GetValue(SettingsSection, "total_missions_completed", 0);

        _dailyStats.Clear();
        foreach (string dateKey in config.GetSectionKeys(StatsDailySection))
        {
            string raw = (string)config.GetValue(StatsDailySection, dateKey, "");
            string[] parts = raw.Split('|');
            if (parts.Length != 8
                || !int.TryParse(parts[0], out int enemies)
                || !int.TryParse(parts[1], out int bosses)
                || !int.TryParse(parts[2], out int crits)
                || !int.TryParse(parts[3], out int rounds)
                || !int.TryParse(parts[4], out int libras)
                || !int.TryParse(parts[5], out int runs)
                || !int.TryParse(parts[6], out int coinsEarned)
                || !int.TryParse(parts[7], out int playTime))
                continue;

            _dailyStats[dateKey] = new DailyStats
            {
                EnemiesKilled = enemies,
                BossesKilled = bosses,
                CritsLanded = crits,
                RoundsCleared = rounds,
                LibrasEarned = libras,
                RunsPlayed = runs,
                CoinsEarned = coinsEarned,
                PlayTimeSeconds = playTime,
            };
        }
        PruneOldDailyStats();

        EverCompletedBuilds.Clear();
        var everCompletedBuilds = (string[])config.GetValue(SettingsSection, "ever_completed_builds", System.Array.Empty<string>());
        foreach (string raw in everCompletedBuilds)
            if (System.Enum.TryParse<Player.BuildClass>(raw, out var cls)) EverCompletedBuilds.Add(cls);

        EverGotLegendary.Clear();
        var everGotLegendary = (string[])config.GetValue(SettingsSection, "ever_got_legendary", System.Array.Empty<string>());
        foreach (string raw in everGotLegendary)
            if (System.Enum.TryParse<UpgradeType>(raw, out var type)) EverGotLegendary.Add(type);

        var unlockedAchievements = (string[])config.GetValue(SettingsSection, "unlocked_achievements", System.Array.Empty<string>());
        _unlockedAchievements = new HashSet<string>(unlockedAchievements);

        _missionsDate = (string)config.GetValue(MissionsSection, "date", "");
        for (int i = 0; i < Missions.Length; i++)
        {
            string raw = (string)config.GetValue(MissionsSection, $"slot_{i}", "");
            string[] parts = raw.Split('|');
            if (parts.Length != 5
                || !int.TryParse(parts[1], out int target)
                || !int.TryParse(parts[2], out int reward)
                || !int.TryParse(parts[3], out int progress)
                || !bool.TryParse(parts[4], out bool completed))
            {
                Missions[i] = null;
                continue;
            }

            Missions[i] = new MissionSlot
            {
                TemplateId = parts[0],
                Target = target,
                Reward = reward,
                Progress = progress,
                Completed = completed,
            };
        }

        _characterLevel.Clear();
        _characterXp.Clear();
        _characterXpToNext.Clear();
        foreach (string charSlug in config.GetSectionKeys(CharacterProgressSection))
        {
            string raw = (string)config.GetValue(CharacterProgressSection, charSlug, "");
            string[] parts = raw.Split('|');
            if (parts.Length != 3
                || !int.TryParse(parts[0], out int level)
                || !int.TryParse(parts[1], out int xp)
                || !int.TryParse(parts[2], out int xpToNext))
                continue;

            _characterLevel[charSlug] = level;
            _characterXp[charSlug] = xp;
            _characterXpToNext[charSlug] = xpToNext;
        }
    }

    private const string RecordsFilePath = "user://records.cfg";
    private const int MaxRecords = 10;

    // Round is the round the run reached, not necessarily completed — RegisterFinalScore reads it
    // before ResetRun zeroes it, same as LastRunLibrasEarned above. Old save files only ever wrote
    // "score|date" (no round); those parse back in as Round = 0, which the UI treats as "unknown"
    // rather than crashing or discarding decades-old — well, days-old — records.
    public readonly record struct ScoreRecord(int Score, int Round, string Date);

    // Hardcore gets its own suffixed section rather than a shared one -- a 1-life Hardcore run and a
    // 3-life Classic run aren't the same achievement, so mixing them into one leaderboard would make
    // neither number mean anything. Classic keeps the original, unsuffixed section name on purpose:
    // every record ever saved before Hardcore existed is Classic data, and this way it's still read
    // as such with no migration step.
    private static string ModeSuffix(GameMode mode) => mode == GameMode.Hardcore ? "_hardcore" : "";

    // Stored as "score|round|date" strings rather than nested dictionaries: ConfigFile round-trips a
    // PackedStringArray cleanly and there's nothing here worth the extra parsing surface.
    public static List<ScoreRecord> LoadRecords(GameMode mode)
    {
        var records = LoadRecordSection("records" + ModeSuffix(mode));

        // Both of these repair pre-Hardcore save data (a stand-in entry from before rounds were
        // tracked, a high score that predates the records list existing at all) -- Hardcore never had
        // either problem, since it didn't exist yet when they happened.
        if (mode == GameMode.Classic)
        {
            DropSupersededLegacyEntries(records);
            BackfillLegacyHighScore(records);
        }
        return records;
    }

    // Removes a backfilled "unknown round" entry once the run it stood in for is also present as a
    // real record.
    //
    // BackfillLegacyHighScore refuses to add one when an equal-or-better record already exists, so it
    // can't create a duplicate itself. These are older damage: AppendRecord used to save without
    // loading first and wiped the whole file, after which the next load saw an empty table, decided
    // the legacy high score was missing, and re-added it -- while the real record for that same run
    // came back later. The result on disk is pairs like "88531|0|—" sitting directly above
    // "88531|19|7/9/26": the same run listed twice, once with no round and no date.
    //
    // Matching on score alone is safe here precisely because a stand-in has no other identity. Two
    // genuinely different runs that scored exactly the same still both survive -- they both carry a
    // real round, and only the Round == 0 copy is ever dropped.
    private static void DropSupersededLegacyEntries(List<ScoreRecord> records)
    {
        var realScores = new HashSet<int>();
        foreach (var r in records)
            if (r.Round > 0) realScores.Add(r.Score);

        int removed = records.RemoveAll(r => r.Round <= 0 && realScores.Contains(r.Score));
        if (removed > 0) SaveRecordSection("records", records);
    }

    // highscore.save predates the records leaderboard by however long AppendRecord took to get added
    // after it — a run that set a high score before then saved it there and nowhere else, so it can sit
    // far above the records table's own #1, reading as a bug ("why doesn't my best score show up
    // here?") rather than what it actually is. Backfilled once and persisted (not just patched at read
    // time), so it becomes a real, sorted entry from here on rather than a live correction reapplied on
    // every load. Round/date are unknown for a run that predates tracking them, same "unknown" ScoreRecord
    // shape a pre-round save already parses into (see ShortenDate/Round==0 above).
    private static void BackfillLegacyHighScore(List<ScoreRecord> records)
    {
        int highScore = LoadHighScore();
        if (highScore <= 0) return;

        bool alreadyTracked = false;
        bool somethingAlreadyAsHighOrHigher = false;
        foreach (var r in records)
        {
            if (r.Score == highScore) alreadyTracked = true;
            if (r.Score >= highScore) somethingAlreadyAsHighOrHigher = true;
        }
        if (alreadyTracked || somethingAlreadyAsHighOrHigher) return;

        records.Add(new ScoreRecord(highScore, 0, "—"));
        SaveRecordSection("records", records);
    }

    // Short D/M/YY, no leading zeros and no time-of-day — the records tables are tight on width (see
    // CharacterSelectMenu's fixed-height RecordsBox), and which run happened at 14:32 vs 14:35 isn't
    // information anyone reading this table needs. Records already saved with the old, longer
    // "DD/MM/YYYY HH:MM" format are untouched — Date is stored and displayed as a plain string, so old
    // and new formats simply coexist rather than needing a migration.
    private static string FormatNow()
    {
        var now = Time.GetDatetimeDictFromSystem();
        return $"{now["day"].AsInt32()}/{now["month"].AsInt32()}/{now["year"].AsInt32() % 100}";
    }

    private static void AppendRecord(int score, int round, GameMode mode)
    {
        var records = LoadRecords(mode);
        records.Add(new ScoreRecord(score, round, FormatNow()));
        SaveRecordSection("records" + ModeSuffix(mode), records);
    }

    // One section per character slug (plus mode suffix), in the same records.cfg file as the overall
    // leaderboard above — works for custom characters too, since their slugs are just as valid a
    // section name as a built-in's. Kept separate from the overall list (rather than replacing it)
    // because a per-pilot table answers "how am I doing with THIS pilot", while the main menu's table
    // answers "what's my best run ever" — two different questions that would erase each other's
    // history if merged into one list.
    private static string CharacterRecordsSection(string slug, GameMode mode) => $"records_{slug}{ModeSuffix(mode)}";

    public static List<ScoreRecord> LoadCharacterRecords(string slug, GameMode mode) =>
        LoadRecordSection(CharacterRecordsSection(slug, mode));

    private static void AppendCharacterRecord(string slug, int score, int round, GameMode mode)
    {
        var records = LoadCharacterRecords(slug, mode);
        records.Add(new ScoreRecord(score, round, FormatNow()));
        SaveRecordSection(CharacterRecordsSection(slug, mode), records);
    }

    private static List<ScoreRecord> LoadRecordSection(string section)
    {
        var records = new List<ScoreRecord>();

        var config = new ConfigFile();
        if (config.Load(RecordsFilePath) != Error.Ok) return records;

        var raw = (string[])config.GetValue(section, "entries", System.Array.Empty<string>());
        foreach (string line in raw)
        {
            string[] parts = line.Split('|');
            if (parts.Length < 2 || !int.TryParse(parts[0], out int score)) continue;

            // 3 parts is the current format (score|round|date); 2 parts is a pre-round save, where
            // parts[1] is the date and the round is simply not known.
            if (parts.Length >= 3 && int.TryParse(parts[1], out int round))
                records.Add(new ScoreRecord(score, round, ShortenDate(parts[2])));
            else
                records.Add(new ScoreRecord(score, 0, ShortenDate(parts[1])));
        }
        return records;
    }

    // Normalizes an on-disk date string to the short D/M/YY display format, regardless of which format
    // it was actually saved in — records written before FormatNow was shortened still have the old
    // "DD/MM/YYYY HH:MM" on disk, and this reformats them at load time rather than requiring a one-time
    // file migration. The next SaveRecordSection call (any future append) re-persists whatever's in
    // memory, which is already short by then, so the file quietly upgrades itself the next time each
    // section is written to.
    private static string ShortenDate(string raw)
    {
        string[] dateParts = raw.Split(' ')[0].Split('/');
        if (dateParts.Length != 3
            || !int.TryParse(dateParts[0], out int day)
            || !int.TryParse(dateParts[1], out int month)
            || !int.TryParse(dateParts[2], out int year))
            return raw;   // unrecognized shape — show it verbatim rather than mangling it

        return $"{day}/{month}/{year % 100}";
    }

    // Highest first, then keep only the top few — this is a leaderboard, not a full history, so an
    // unbounded file would grow forever for no benefit. Loads before writing (unlike a plain overwrite)
    // so appending one section's record doesn't wipe every other section already in the same file —
    // the overall list and however many per-character sections have been written so far.
    private static void SaveRecordSection(string section, List<ScoreRecord> records)
    {
        records.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (records.Count > MaxRecords) records.RemoveRange(MaxRecords, records.Count - MaxRecords);

        var lines = new string[records.Count];
        for (int i = 0; i < records.Count; i++) lines[i] = $"{records[i].Score}|{records[i].Round}|{records[i].Date}";

        var config = new ConfigFile();
        config.Load(RecordsFilePath);
        config.SetValue(section, "entries", lines);
        config.Save(RecordsFilePath);
    }

    // Leaving a run early on purpose. Records the score first, then resets — abandoning used to call
    // ResetRun directly, and since RegisterFinalScore only ever ran from NotifyPlayerDied, the entire
    // run's result was discarded with no warning and no way to get it back. A run you walked away
    // from still happened, so it still counts toward the high score and the records list.
    public void AbandonRun()
    {
        RegisterFinalScore();
        ResetRun();
    }

    public void ResetRun()
    {
        // A new run's own recap, not a continuation of whichever run last called EvaluateAchievements
        // live -- see LastRunNewAchievements' comment above for why this can't live inside
        // EvaluateAchievements itself now that it's called from several points during a run.
        LastRunNewAchievements.Clear();

        Xp = 0;
        Level = 1;
        XpToNextLevel = 10;
        EnemiesKilled = 0;
        SpecialEnemiesKilled = 0;
        BossesKilled = 0;
        Coins = 0;
        TotalCoinsEarned = 0;
        Score = 0;
        RoundNumber = 1;
        _pendingLevelUps = 0;
        _pendingUltimateChoice = false;
        _pendingRoundEnd = false;
        _bossUltimateGranted = false;
        _pickerIsUltimateChoice = false;
        _shopPurchaseCounts.Clear();
        // Stopped, not restarted — this runs on returning to the main menu, before Arena.tscn (and
        // therefore round 1) has loaded again. StartRoundOneTimer (called from EnemySpawner._Ready)
        // is what actually arms it once the player is back in gameplay.
        _roundTimer.Stop();
        EnemySpeedMultiplier = 1f;
        BaseEnemySpeedMultiplier = 1f;
        EventRewardMultiplier = 1f;
        EventHpMultiplier = 1f;
        SurvivabilityCatchUpMultiplier = 1f;
        _slowTimer?.Stop();
        _roundStartTimer?.Stop();
        SnapshotRoundStart();


        Resume();
    }
}
