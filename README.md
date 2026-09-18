# Infinitix

A mobile auto-shooter arena game built in **Godot 4.7** with **C# / .NET 9**. Vampire-Survivors-style
round loop, Geometry-Wars-style visuals — nearly everything on screen is drawn in code, and the few
files that do exist are *generated* by the scripts in `tools/` rather than hand-authored. See
[assets.md](docs/assets.md).

**Premise:** no hay historia que termine porque la ronda no termina. Te subís a la arena, los
demonios no paran de escalar, y la única salida es sobrevivir una más — y después otra. Infinitix
es ese "una más" convertido en juego: cada ronda te hace más fuerte, cada ronda manda algo peor a
buscarte, y no hay final feliz esperándote al otro lado. Solo el próximo récord.

## The loop

Move with a virtual joystick (or WASD); your gun fires automatically at the nearest enemy inside
your range ring. Survive a 60-second round, spend Coins in the shop, repeat. Every 5th round is a
Boss Round. Killing your first boss unlocks an Ultimate — a Score-charged special you fire with a
HUD button or **R**.

## Running it

Open the project folder in Godot 4.7 (Mono/.NET build) and press Play. The main scene is
`Scenes/UI/MainMenu.tscn`. The .NET SDK 9 toolchain must be installed for the C# assembly to build
(`ShooterLoop.csproj` targets `net9.0`).

To regenerate the sounds, music and silhouettes: `python tools/build_assets.py` — see
[assets.md](docs/assets.md).

## Layout

| Path | What's in it |
|---|---|
| `Scenes/` | All `.tscn` files — arena, player, enemies, UI |
| `Scripts/Autoload/` | `GameManager` (rounds, XP, economy, run state) and `AudioManager` (SFX) — the two autoload singletons |
| `Assets/Audio/` | Generated `.wav` effects — see `tools/gen_sfx.py`, not hand-authored |
| `tools/` | Python asset generators: character portraits and sound effects |
| `Scripts/Player/` | Player movement/combat, orbit blades, companion drone |
| `Scripts/Enemy/` | Base `Enemy`, `ShooterEnemy`, `Boss`, and the spawner |
| `Scripts/Upgrades/` | Reward catalog and tier rolling |
| `Scripts/UI/` | HUD, shop, upgrade picker, pause/game-over screens |
| `Scripts/Util/` | `RoundCurve`, `DifficultyBalancer`, `CameraRig` |
| `docs/` | Design documentation — see below |

## Documentation

The `docs/` folder explains *why* each system works the way it does, not just what it does. Worth
reading before changing balance numbers:

- [rounds.md](docs/rounds.md) — the round lifecycle, boss rounds, the modal queue
- [enemies.md](docs/enemies.md) — enemy categories, shooters, splitting, collision layers
- [player.md](docs/player.md) — movement/inertia, lives, shield, abilities, the arena & camera
- [characters.md](docs/characters.md) — the playable cast, their perks, and how to change them
- [rewards.md](docs/rewards.md) — the reward catalog and the stacking rules
- [economy.md](docs/economy.md) — Coins vs. Score, shop pricing
- [difficulty-scaling.md](docs/difficulty-scaling.md) — `RoundCurve` and adaptive difficulty
- [visuals.md](docs/visuals.md) — palette, glow, and animation
- [audio.md](docs/audio.md) — generated SFX and music, throttling, and the volume settings
- [assets.md](docs/assets.md) — the `tools/` pipeline: where every file in the repo comes from
- [localization.md](docs/localization.md) — the CSV translation system, and how to add a string or a language

Nearly all balance tuning is done by editing named constants in one place per system — the
`RoundCurve` fields at the top of `EnemySpawner.cs`, the cap constants in `Player.cs`, and the
catalog in `UpgradeData.cs`.
