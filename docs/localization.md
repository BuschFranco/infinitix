# Localization: Godot's CSV translations, keyed on the existing Spanish text

Every string in the game was hardcoded Spanish until this system existed — no `TranslationServer`
call anywhere, no `.csv`/`.po`/`.translation` resource in the repo. This is that system, and English
is the first (and so far only) language added on top of it.

## The key IS the Spanish text

`Assets/Localization/strings.csv` has two columns: `keys` and `en`. The `keys` column is the
**original Spanish string, verbatim** — not an invented identifier like `ui.mainmenu.start`. A row
looks like:

```csv
keys,en
Comenzar,Start
Ronda {0},Round {0}
```

This is the classic gettext convention, and it's what makes an incremental migration safe: a string
with no row yet just renders as the Spanish it always was (`tr()`/`Tr()` fall back to returning the
key unchanged when nothing matches), so nothing can break by migrating one screen without migrating
all of them. `Español` needs zero rows of its own — see [Why Spanish needs no column](#why-spanish-needs-no-column-locale-fallback)
below.

## Two cases, two amounts of code

Godot auto-translates the `text` (and `placeholder_text`) of any `Control` — whether it was set in
the `.tscn` at design time or with `label.Text = "..."` in C# — automatically, the moment a matching
CSV row exists. That splits every string into two very different amounts of work:

**Static text** — a button caption, a title, a hint paragraph, any `.Text = "literal"` with no
interpolation. **Needs zero code changes.** Add the row to the CSV; Godot does the rest, including
re-translating already-visible Controls live when the player switches language.

**Interpolated text** — `$"Ronda {round}"`. The *rendered* string ("Ronda 8") never matches a fixed
CSV key, so it never auto-translates. This needs the template wrapped explicitly, translated first,
then formatted:

```csharp
// Before
_roundLabel.Text = $"Ronda {round}";

// After
_roundLabel.Text = string.Format(Tr("Ronda {0}"), round);
```

`Tr(...)` is inherited from `Node` (every UI script already extends `Control : ... : Node`, so it's
always in scope). For a static class with no `Node` context — `Glossary.cs`, `MissionCatalog.cs` —
use `TranslationServer.Translate(...)` instead; it's the exact same lookup, just callable without an
instance.

**Static class fields can't call it.** `public const string Damage = "Daño";` is a compile-time
constant — it can never re-resolve at runtime. `Glossary.cs` converts every field to a property
instead (`public static string Damage => TranslationServer.Translate("Daño");`), which keeps every
existing call site (`Glossary.Damage`) unchanged while making it genuinely live.

**A ternary that swaps in a different clause, not just a value, needs no special handling at all** —
each branch is already a separate string literal in the source, so it's already two separate CSV
rows once both get wrapped. There's nothing to design here beyond wrapping each branch.

## Why Spanish needs no column (locale fallback)

Godot has a project setting, `internationalization/locale/fallback` (`project.godot`), for "which
locale to use when nothing else matches." It defaults to `"en"` — which, with only one CSV column
registered, meant *every* other locale (including `"es"`) silently fell through to the English
translation instead of the untranslated Spanish key. This project sets it to `"es"` instead, which is
the actually-correct value for a Spanish-native game: Spanish is the true fallback, not a second
language that happens to need its own column. Verified directly (headless Godot, `TranslationServer`)
before relying on it — see the git history for the throwaway repro if this ever needs re-checking.

## Adding a string to an already-translated screen

1. Find (or add) the string's row in `Assets/Localization/strings.csv`, keyed on the exact Spanish
   text.
2. If it's interpolated, wrap it: `Tr("template {0}")` + `string.Format(...)`. If it's a plain
   `.Text = "literal"` or `.tscn` static text, do nothing else — it already auto-translates.
3. `python tools/check_translations.py` — confirms every `Tr(...)` call site in the code has a
   matching CSV row. It can tell you a key is *missing*; it can't write the English for you.
4. Re-import so Godot compiles the CSV into a `Translation` resource:
   `Godot --headless --import` (from the repo root). This regenerates
   `Assets/Localization/strings.en.translation` — that's the file `project.godot` actually loads, not
   the CSV directly.

## Adding a new language

1. Add a new column to `strings.csv` (e.g. `pt` for Portuguese) with that language's translation per
   row.
2. Re-import (`Godot --headless --import`) — Godot generates one `.translation` resource **per
   locale column**, e.g. `strings.pt.translation`.
3. Register the new resource in `project.godot`'s `[internationalization]` →
   `locale/translations` array, alongside `strings.en.translation`.
4. Add a button to `OptionsMenu.tscn`'s `LanguageRow` (same `ButtonGroup_language`, same
   `toggle_mode`/`button_group` pattern as `SpanishButton`/`EnglishButton`) and wire its `Toggled` in
   `OptionsMenu.cs` the same way: `GameManager.Instance?.SetLanguage("pt")`.

## Language selection & persistence

`GameManager.cs` mirrors the existing screen-orientation pattern exactly (`LoadOrientationPreference`/
`SetOrientation`, same file): a small plain-text save file (`user://language.save`), loaded and
applied *before* `LoadSettings()` in `_Ready()` so the very first frame already renders in the right
language instead of flashing the wrong one. First-ever launch (no save file yet) defaults to the
device's own language — Spanish if `OS.GetLocaleLanguage()` starts with `"es"`, English otherwise.

The picker itself lives in `OptionsMenu.tscn`/`.cs` (`LanguageRow`, `SpanishButton`/`EnglishButton`),
reachable only from `MainMenu` (not from Pause — `PauseMenu.cs` has its own duplicated sliders and
never opens `OptionsMenu`). Godot re-translates already-visible static Controls live when the locale
changes, but a label built by *interpolating* an already-translated template can't re-derive itself
that way — `MainMenu.cs`'s existing `options.VisibilityChanged` hook (originally added for
orientation) re-renders those specific labels when Options closes, since Options is the only place
this picker is reachable from.

## What's translated so far, and what isn't yet

| Screen | Status |
|---|---|
| Main Menu | ✅ Translated (incl. the language/orientation pickers themselves) |
| HUD (in-run) | ✅ Translated |
| Pause menu | ✅ Translated |
| Game Over screen | ✅ Translated |
| Options menu | ✅ Translated |
| Shop / Upgrade Picker | Pending — same pattern, not yet migrated |
| Achievements / Missions menus | Pending — `MissionCatalog.FormatName` is already translation-aware (shared with the live notification toast), the menu rows themselves aren't yet |
| Character select / Builds / Cosmetics / Stats menus | Pending |
| Content catalogs (`AchievementCatalog`, `CharacterCatalog`, `BuildCatalog`, `UpgradeData`, `CosmeticCatalog`) | Pending — these are plain `Name`/`Description` string fields already read through a `Control.Text` setter wherever they're displayed, so migrating them is *only* CSV rows, zero code changes, same as any other static text |

Extending coverage to any of the above is the same three steps as "Adding a string to an
already-translated screen" above — there's no new pattern to invent, just more CSV rows (and, for the
handful of interpolated call sites each remaining screen has, a `Tr()` wrap to match).
