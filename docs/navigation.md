# Navigation: closing menus, and Android's back button

Three related pieces, all about leaving a screen without hunting for a specific button.

## Tap outside to close

Every modal follows the same `Dim` (a full-rect `ColorRect` behind the panel) → `CenterContainer` →
`Panel` structure. `Dim`'s default `mouse_filter` (`Stop`) already blocks clicks from reaching
whatever's underneath — that part was already true — but nothing ever listened on it, so tapping the
dimmed backdrop used to do nothing at all.

`UIUtil.WireDimToClose(Control dim, Action close)` (`Scripts/UI/UIUtil.cs`) wires it up: a tap/click
on `dim` calls `close`. One line in `_Ready()` per screen:

```csharp
UIUtil.WireDimToClose(GetNode<Control>("Dim"), Close);
```

It checks for both `InputEventMouseButton` and `InputEventScreenTouch` — a couple of screens had a
hand-rolled version of this already that only checked for the mouse button, which real touch on
Android never raises (`input_devices/pointing/emulate_touch_from_mouse` only makes *mouse* input
emulate touch, not the other way around), so those taps silently did nothing on-device. Worth
checking for that pattern if you're adding a new modal — copy `WireDimToClose`, don't hand-roll it.

**Applies to**: `OptionsMenu`, `BuildsMenu`, `CosmeticsShopMenu`, `AchievementsMenu`, `StatsMenu`,
`MissionsMenu`, `GameOverStatsMenu`, `CharacterSelectMenu`, `GameModeMenu`, `CharacterCreator`,
`ConfirmDialog` (tapping outside cancels — it never confirms the destructive action).

**Does not apply to** `Shop`/`UpgradePicker` (a forced choice — there's no "cancel" concept, tapping
past it shouldn't skip the step) or `PauseMenu` (resuming already has a deliberate 3s anti-mistoque
delay; tapping outside shouldn't bypass it) or `GameOverScreen` (no plain "close", every exit is a
scene change with a consequence).

## Android back button

`project.godot` sets `application/config/quit_on_go_back=false` — without it, Godot's default is to
quit the app immediately on the physical/gesture back button, which is what was happening before.

`GameManager.cs` keeps a small stack instead of a single "what's open" flag, because modals genuinely
nest today: `ConfirmDialog` opens on top of `PauseMenu`/`GameOverScreen` without hiding them, and
`CharacterCreator` opens on top of `CharacterSelectMenu` the same way. Back has to peel off only the
top one.

```csharp
public void PushBackHandler(Node owner, Action handler);
public void PopBackHandler(Node owner);
```

Every screen that can be open calls `PushBackHandler(this, Close)` wherever it currently does
`Visible = true` / `Juice.ModalIn(...)`, and `PopBackHandler(this)` wherever it closes — including
every alternate exit a screen has (`ConfirmDialog`'s Confirm button, `UpgradePicker`/`Shop` resolving
their step, `PauseMenu`/`CharacterSelectMenu` handing off into a scene change), not just the primary
Close/Cancel button.

**No attempt is made to guarantee a pop on every possible exit path.** `GameManager`'s
`HandleBackPressed()` validates each entry with `IsInstanceValid(owner)` before invoking it and
silently discards anything stale — so a screen that got freed by a scene reload without popping
itself first is simply skipped on the next back-press, not a crash. This is what makes it safe to
not chase down every exotic exit.

**What "Close" means per screen**:
- Plain browse/settings screens (`OptionsMenu`, `BuildsMenu`, `CosmeticsShopMenu`,
  `AchievementsMenu`, `StatsMenu`, `MissionsMenu`, `GameOverStatsMenu`, `CharacterCreator`,
  `CharacterSelectMenu`, `GameModeMenu`): back = the same `Close()` the Cancel/Volver button calls.
- `ConfirmDialog`: back = Cancel (never Confirm).
- `PauseMenu`: back = `OnResumePressed()`, the same 3s countdown "Reanudar" already has — back isn't
  a way to skip that friction.
- `Shop`/`UpgradePicker`: back pushes an empty handler (`() => {}`) while open — a forced choice, so
  back is absorbed rather than falling through to a contextually wrong action underneath (like
  reopening Pause mid-shop).

**With nothing on the stack** (`GameManager.HandleBackWithNothingOpen`): mid-run
(`Scenes/Arena.tscn` is the current scene), back opens Pause — the same thing the HUD's pause button
does. At the main menu with nothing open, back shows the same "¿Salir del juego?" confirmation
`GameOverScreen.OnQuitPressed` already uses, since `quit_on_go_back=false` means the root scene would
otherwise have no way to exit via back at all.

## The "Volver" button was unreachable in landscape

Not a sizing bug — `OptionsMenu` already derives its scroll height from the live viewport
(`UIUtil.FitScrollToViewport`) and "Volver" was already inside the scrollable area. The actual issue:
`FitToOrientation()` ran synchronously in the same call as `GameManager.SetOrientation(...)`, but
`DisplayServer.ScreenSetOrientation` doesn't necessarily land the resize the same frame it's
requested — so the fit calculation could run against the *old* viewport size and never get corrected,
since nothing else ever triggered a recompute.

Fix: `OptionsMenu._Ready()` subscribes once to `GetTree().Root.SizeChanged`, calling
`FitToOrientation()` whenever it fires — however many frames the real resize takes to land, this
catches it. It's the only screen that needs this: every other modal only recomputes on its own
`Open()`, and by the time that runs after an orientation change, the resize settled long ago.
