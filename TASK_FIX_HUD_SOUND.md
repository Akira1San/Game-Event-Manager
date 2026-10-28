# TASK_FIX_HUD_SOUND.md — HUD labels and sound loading actually work

**Status:** `[~]` code complete (Parts 1–5 landed 2026-10-28); editor acceptance pending
**Depends on:** Tasks 1–3 (bridge), Task 4 Part A (scene + example events)
**Source of truth:** `DESIGN.md` §3–§4, §7; this file for the two bridge bugs
**Scene work:** HealthLabel deleted from `Scene.scene` — user's other uncommitted edits preserved

> Fresh session: read `DESIGN.md` §3–§4 and this file. Work the parts in order.
> Do not rebuild the UI, do not touch label positions, do not hardcode Flax types
> into the pure runtime project.

---

## Goal

Two things work in play mode, with no scary log noise:

1. **HUD:** `show text` / `hide text` / `set text of <actor>` find real
   `FlaxEngine.GUI.Label`s and update them live.
2. **Sounds:** `play sound "<name>"` finds imported `AudioClip`s wherever they live in
   `Content/` (folders and extensions are irrelevant), `stop sound` actually stops them,
   and a missing clip produces **one** friendly warning instead of three engine errors.

Bonus, same bug class: `play anim` / `loop anim` / `stop anim` and the
`when anim "<name>" ends` subscription stop looking for their engine actor as a script.

---

## Root causes (verified)

### HUD: `UIControl` is an Actor, not a Script

`FlaxEngine.UIControl : Actor` (1.13 headers + docs). `FlaxActions.cs:375,385` call
`actor.GetScript<UIControl>()`, which only returns `Script` components and is therefore
**always null** for a `UIControl` actor. Hence the four warnings in
`Logs/Log_2026_10_27_21_43_10.txt` (00:00:11.316, 13.298, 13.299) even though
`GameText`, `ScoreLabel`, `LivesLabel`, `HealthLabel` all exist under `ui canvas`.

Correct: `Level.FindActor(name) as UIControl` → `.Control as FlaxEngine.GUI.Label`.

Layout is **not** broken (checked against `Control.UpdateBounds()` with the serialized
anchors/offsets; parent canvas evaluates to 1920×1080): GameText x 20.7→1080, y 50;
ScoreLabel y 583; LivesLabel 24,587; HealthLabel 1223,13. Do not move anything.

### Sounds: the loader only knows the content root

`LoadAudioClip` (`FlaxActions.cs:420-437`) probes `Content/<name>.flax`,
`Content/<name>`, `<name>`. The clip lives at `Content/sounds/hurt.wav.flax`, so every
probe misses and `Content.Load` logs an `[Error] Missing file` for each attempt, then the
`WarnOnce`. Asset name (`hurt.wav`) and event name (`hurt.wav`) also disagree with the
filesystem shape (`sounds/hurt.wav.flax`).

### Same cast bug elsewhere

| File:line | Wrong call | Reality |
| --- | --- | --- |
| `FlaxActions.cs:253` | `me.GetScript<AudioSource>()` | `AudioSource : Actor` |
| `FlaxActions.cs:459,484,510` | `me.GetScript<AnimatedModel>()` | `AnimatedModel : ModelInstanceActor` |
| `EventSheet.cs:87` | `actor.GetScript<AnimatedModel>()` | same |

So `stop sound` and every animation word silently no-op today.

---

## Decisions (confirmed with the user)

1. **Fix the loader**, keep `Content/sounds/hurt.wav.flax` as the placeholder hurt sound
   (it is a re-import of `sounds/boom9.wav`; swapping the source later needs no code).
2. **Delete `HealthLabel`** — it duplicates `LivesLabel`. Remove the actor from
   `Scene.scene` and its two `set text of` lines from `Game.events`.
3. **`stop sound` gets real tracking** (not just the cast fix): it must stop the
   transient sources that `play sound` spawns, which the actor's child source never sees.
4. **Fix the `AnimatedModel` casts** too (4 call sites).

Out of scope: volume/pitch variety, 3D audio, scene switching, rebuilding the UI,
anim-end API naming (Task 5 Part B owns the reflection details), renaming the hurt asset.

---

## Part 1 — HUD lookups (`Source/Game/Bridge/FlaxActions.cs`)

### 1.1 `FindLabel`

Add one resolver and make the four entry points use it:

```csharp
private static FlaxEngine.GUI.Label FindLabel(string thing, out UIControl ui)
{
    ui = null;
    if (string.IsNullOrEmpty(thing))
        return null;

    Actor actor = Level.FindActor(thing);
    if (actor == null)
        return null;

    ui = actor as UIControl;
    if (ui == null)
    {
        // A panel/canvas name is allowed only when it owns exactly one label.
        var found = new List<UIControl>();
        CollectLabels(actor, found);
        if (found.Count != 1)
            return null;               // 0 → not a label, >1 → ambiguous, name one
        ui = found[0];
    }
    return ui.Control as FlaxEngine.GUI.Label;
}
```

`CollectLabels(Actor node, List<UIControl> found)` mirrors the existing
`CollectNamed` walk (children first, then recurse; skip nulls). `UIControl` is not a
`Script`, so use `as`/`is` — **never** `GetScript<UIControl>()`.

### 1.2 `SetLabel` (replaces `FlaxActions.cs:382-392`)

```csharp
private static bool SetLabel(string thing, string text)
{
    var label = FindLabel(thing, out UIControl ui);
    if (label == null)
        return false;
    if (ui.Actor != null && !ui.Actor.IsActive)
        ui.Actor.IsActive = true;      // `hide GameText` must not brick `show text`
    label.Text = text;                 // Label.Text is LocalizedString; string converts implicitly
    label.Visible = true;
    return true;
}
```

### 1.3 `ShowHudText` / `HideHudText` (`:366-380`)

- `ShowHudText` keeps `SetLabel("GameText", text)`.
- `HideHudText` becomes `FindLabel("GameText", out _)?.Visible = false` (no `GetScript`).
- Both warnings gain the fix instructions and the labels that *do* exist:
  `... Create a UIControl actor named "GameText" with a Label. Labels in this scene: GameText, ScoreLabel, LivesLabel.`
  Implement `LabelActorNames()` with the same scene walk (join with `, `, `"(none)"` if empty).

### 1.4 `SetText` (`:439-448`)

Use the same resolver; on failure warn once with the actor name **and** `LabelActorNames()`.

### 1.5 Warning wording

Keep them actionable one-liners (this is the project's style), e.g.
`"set text of" needs a Label on a UIControl actor — I couldn't find one called "ScoreLabel". Labels here: ...`

---

## Part 2 — Sound lookup

### 2.1 New pure helper: `Source/Game/Runtime/AssetNames.cs`

Same namespace `Game`, no Flax types (the test project globs `Runtime/*.cs`):

```csharp
public static class AssetNames
{
    public static string Key(string pathOrName);   // "sounds/hurt.wav.flax" -> "hurt"
}
```

Rules (all case-insensitive):

1. null/empty/whitespace → `""`.
2. `\` → `/`; take everything after the last `/`.
3. strip one trailing `.flax`.
4. strip one known audio extension: `.wav .mp3 .ogg .flac .aiff .aif .wma .m4a`.
5. trim, lowercase (`ToLowerInvariant`).

`"shot"` → `shot`; `"shot.wav"` → `shot`; `"Content\\sounds\\boom9.flax"` → `boom9`;
`"boom.flax"` → `boom`; `"Hurt.WAV"` → `hurt`.

### 2.2 Registry index (`FlaxActions.cs`)

```csharp
private static Dictionary<string, Guid> _soundIndex;

private static Dictionary<string, Guid> SoundIndex()
{
    if (_soundIndex != null)
        return _soundIndex;
    _soundIndex = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
    foreach (Guid id in Content.GetAllAssetsByType(typeof(AudioClip)))
    {
        if (!Content.GetAssetInfo(id, out AssetInfo info))
            continue;
        string key = AssetNames.Key(info.Path);
        if (key.Length == 0 || _soundIndex.ContainsKey(key))
            continue;                   // first asset wins; no duplicate warnings
        _soundIndex[key] = id;
    }
    return _soundIndex;
}
```

`LoadAudioClip(string name)` (`:420-437`) becomes:

```csharp
return SoundIndex().TryGetValue(AssetNames.Key(name), out Guid id)
    ? Content.Load<AudioClip>(id)
    : null;
```

No path probing anywhere → no `Missing file` spam. `Content.Load` caches, so repeated
plays are cheap. Reset the index in `ClearSession()` (`_soundIndex = null;`) so
re-imports are seen on the next play.

### 2.3 `PlaySound` (`:394-418`)

- Resolve `var me = context.Me as Actor;`
- Warn once with the available names:
  `Could not load sound clip "hurt2". Imported clips: boom, hurt, shot, shot2.`
  (`AvailableSoundNames()` → keys sorted, `"(none)"` if empty.)
- Spawn as today but `Position = me != null ? me.Position : Vector3.Zero`.
- Keep one transient `AudioSource` per call (overlapping shots must not cut each other
  off); do **not** reuse the actor's child source.
- Register `(owner: me, source: audio)` in `ActiveSounds`; destroy after `clip.Length`
  as now.

### 2.4 `stop sound` (`:250-256`)

```csharp
case ActionVerb.StopSound:
{
    var me = context.Me as Actor;
    if (me != null)
        me.FindActor<AudioSource>()?.Stop();          // actor's own source, if any

    for (int i = ActiveSounds.Count - 1; i >= 0; i--)
    {
        var (owner, source) = ActiveSounds[i];
        if (source == null) { ActiveSounds.RemoveAt(i); continue; }
        if (me != null && owner != me) continue;
        source.Stop();
        FlaxEngine.Object.Destroy(source);
        ActiveSounds.RemoveAt(i);
    }
    break;
}
```

Semantics: on an actor sheet, stop that actor's sounds; on the global `Game` sheet stop all
(document that). Prune entries for objects destroyed elsewhere in `Step()` (cheap loop,
`source == null` → remove) so the list cannot grow unbounded.

---

## Part 3 — Animation actor casts

Replace with `FindActor<T>()` (searches self + children, so both a bare `AnimatedModel`
actor and a `StaticModel` with an `AnimatedModel` child work):

| File:line | New call |
| --- | --- |
| `FlaxActions.cs:459` (`PlayAnim`) | `me.FindActor<FlaxEngine.AnimatedModel>()` |
| `FlaxActions.cs:484` (`LoopAnim`) | same |
| `FlaxActions.cs:510` (`StopAnim`) | same |
| `EventSheet.cs:87` (`SubscribeAnimEnd`) | `actor.FindActor<FlaxEngine.AnimatedModel>()` |

Keep the existing "needs an AnimatedModel on X" warnings. `Scene.scene` has no
`AnimatedModel` (Player is a `StaticModel`), so the shooter scene is unaffected; this
unblocks `char_test.scene` / `Character.events`.

---

## Part 4 — Delete `HealthLabel`

1. `Source/Game/GameFiles/Game.events`:
   - remove `set text of HealthLabel to Lives` from `when game starts` (line 10)
   - remove `set text of HealthLabel to Lives` from `when Lives changes` (line 21)
   - `LivesLabel` stays in both places.
2. `Content/Scene.scene`: delete the `UIControl` object with
   `"ID": "847e1ff5493e521c7c7758bacc30814d"`, `"Name": "HealthLabel"` (child of
   `ui canvas` = `fed0f53b428de3cf2055b891ee40be9b`).
   - `Scene.scene` currently has **uncommitted** positioning edits (GameText translation,
     label offsets). Remove only the HealthLabel block; keep everything else byte-identical.
   - If the Flax editor is open with unsaved changes, prefer deleting the actor in the
     editor instead, then re-check the file; otherwise close the editor before editing.
3. No C# references remain (`set text of` is generic), so nothing else to change.

---

## Part 5 — Tests, docs, build hygiene

### 5.1 Unit tests (`Tests/Program.cs`)

Pure checks (no Flax types needed):

```csharp
Check(AssetNames.Key("sounds/hurt.wav.flax") == "hurt", "...");
Check(AssetNames.Key(@"Content\sounds\boom.flax") == "boom", "...");
Check(AssetNames.Key("Hurt.WAV") == "hurt", "...");
Check(AssetNames.Key("shot") == "shot", "...");
Check(AssetNames.Key("boom9") == "boom9", "...");
Check(AssetNames.Key("") == "" && AssetNames.Key(null) == "", "...");
```

### 5.2 Docs

- `DESIGN.md`: add a short **HUD** note — labels are `UIControl` actors with
  `FlaxEngine.GUI.Label` controls under a `UICanvas`; `show text` / `hide text` target the
  actor named `GameText` (the one magic name, now documented); `set text of <name>` targets
  any label actor, or a parent with exactly one label; the actor must be active.
- `DESIGN.md` **sound names**: lookup ignores folders, `.flax` and the audio extension and
  is case-insensitive, so `"shot"`, `"shot.wav"`, `"sounds/shot"` are the same clip;
  `stop sound` stops this object's sounds (global sheet = all).
- `DESIGN.md`, one line under the bridge notes: `UIControl`, `AudioSource` and
  `AnimatedModel` are **actors**, not scripts — `FindActor<T>()`, never `GetScript<T>()`.
- `TASKS.md`: log entry for this fix (root cause + files) and a verification checklist.

### 5.3 Hygiene (optional, same file)

- `FlaxActions.cs:179` pre-existing `CS0253` — cast the right-hand side to `(object)`.
- Delete the dead `"Content/" + name` candidate strings as part of 2.2 (no leftovers).

---

## Verification

Build / tests (from repo root):

```
dotnet build "EventManager/EventManager/Source/Game.csproj"
dotnet run --project "EventManager/EventManager/Tests/EventManagerTests.csproj"
```

Editor checklist (requires a human, cannot be automated here):

1. Play `Scene`: "Shoot the enemies!" appears at the top, disappears after 2 s.
2. HUD reads Score `0`, Lives `3` (no HealthLabel any more).
3. Shooting an enemy increments ScoreLabel; `when Score changes` fires.
4. Taking a hit plays the hurt clip (currently boom9), LivesLabel drops to `2`, player
   blinks for 1 s and resumes.
5. `stop sound` (add a temporary event or test via `Character.events`) silences the
   transient source.
6. New log contains **zero** `Missing file` lines and none of the five old warnings.
7. Deliberately misspell a clip once (`play sound "hurt2"`): exactly one warning listing
   `boom, hurt, shot, shot2`.
8. `char_test.scene` (optional sanity): `play anim "Shoot"` no longer warns
   "needs an AnimatedModel".

## Risks / notes

- `Content.GetAllAssetsByType` uses the asset registry (editor and cooked builds). If a
  future packaged build ever returns an empty list, fall back to a single
  `Content.Load<AudioClip>("Content/" + name + ".flax")` attempt only when the index is
  empty (never probe three paths).
- Index is built once per play session; re-importing a clip mid-play needs a restart
  (or `ClearSession`).
- Duplicate base names (`shot` vs `shot.wav` in two folders) resolve to the first registry
  entry; do not add extra warnings for it.
- `Label.Text` is `LocalizedString` in 1.13 with implicit `string` conversions — no casts
  needed, and do not hand-build `{"Id":..,"Value":..}` JSON.
- Do not touch label transforms or anchors; the reported "they do nothing" was the cast
  bug, not layout.

## Landed notes (2026-10-28)

Code complete; build 0 warnings, **198/198 tests green**. Three deviations worth knowing:

- `UIControl` has no `Actor` property (it *is* the actor), so `SetLabel` reactivates the
  label with `ui.IsActive = true`.
- `EventSheet.SubscribeAnimEnd` uses `actor as AnimatedModel` rather than
  `FindActor<AnimatedModel>()`: `SubscribeTree` already visits every node, so a tree search
  would subscribe the same model twice (double `when anim ends`). `FlaxActions` anim words
  do use `FindActor<AnimatedModel>()` because they run from one sheet actor.
- The `Content.GetAllAssetsByType` fallback for an empty registry was not added; the
  registry is populated in the editor, which is where this project runs. Add it only if a
  packaged build ever shows an empty clip list.

Editor acceptance still pending: HUD visible/updating, hurt sound audible, no
`Missing file` lines.