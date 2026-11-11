# TASK_FIX_SCENES.md — Scene loading, adding, unloading & prefabs (correct additive/replace)

**Status:** `[x]` code complete (Parts A–C, F, G) — Part H editor acceptance pending
**Depends on:** Tasks 1–3 (parser/runtime/bridge), Task 6 (scene words exist)
**Source of truth:** `DESIGN.md` §5 (word catalog), `TASKS.md` Task 17 Part D; this file for the
additive-vs-replace fix and the new overlay words.
**Editor setup:** some steps are the user's (creating overlay/UI scenes, prefab assets).
Code can land first.

> Fresh session: read `DESIGN.md` §5 ("Menus & game UI" + the action table), `TASKS.md`
> Task 17 Part D, then this file. Do the parts in order. Do not hardcode Flax types into the
> pure runtime test project.

---

## Goal

The event language must cover the **full scene lifecycle**, not just replace:

1. **Load (replace)** — `load scene "<name>"` swaps the whole level. ✅ works today.
2. **Add (on top)** — `add scene "<name>"` loads a scene additively, on top of the current
   one, for UI overlays, HUDs, pause menus, hero-selection panels. Does **not** touch
   existing actors. ❌ missing.
3. **Unload** — `unload scene "<name>"` removes one additive scene (close the overlay). ❌ missing.
4. **Spawn (clone)** — `spawn <thing> at <spot>` clones an in-scene actor. ✅ works.
5. **Prefab** — `spawn prefab "<name>" at <spot>"` instantiates a `.prefab` asset. ✅ works,
   but not in the word registry.
6. **Scene-at-spot** — `spawn scene "<name>" at <spot>"` instances a scene's actors at a
   position. ⚠️ partially works, has a real bug.

The Flax C# API (1.13) has everything needed — see the API table below. The gaps are in the
event-language coverage and one implementation bug, not in the engine.

---

## Confirmed Flax 1.13 API (verified against docs.flaxengine.com)

| Need | Flax API | Additive or replace |
|------|----------|---------------------|
| Load scene on top | `Level.LoadScene(Guid)` / `LoadScene(SceneReference)` / `LoadSceneAsync(Guid)` | **Additive** — adds to `Level.Scenes[]` (docs say only "Loads scene from the asset"; no unload) |
| Replace all + load one | `Level.ChangeSceneAsync(Guid)` / `ChangeSceneAsync(SceneReference)` | **Replace** — docs: "Unloads all active scenes and loads the given scene" |
| Remove one scene | `Level.UnloadScene(Scene)` / `UnloadScene(Guid)` / `UnloadScene(SceneReference)` / `UnloadSceneAsync(Scene)` | Removes a single scene |
| Remove everything | `Level.UnloadAllScenes()` / `UnloadAllScenesAsync()` | |
| Find loaded scene by id | `Level.FindScene(Guid)` → `Scene` | |
| Scene array / count | `Level.Scenes` (array), `ScenesCount`, `GetScene(i)` | Multiple scenes loaded at once |
| Spawn actor (clone) | `Level.SpawnActor(Actor)` / `SpawnActor(Actor, Actor parent)` | |
| Spawn prefab | `PrefabManager.SpawnPrefab(Prefab, Vector3, Quaternion)` (+ pos / parent / `Transform` overloads) | Spawns into the **first loaded scene** |
| Prefab default instance | `Prefab.GetDefaultInstance()` → `Actor` (not added to gameplay) | For reading defaults; not needed for spawn |
| Load assets by name | `Content.Load<Prefab>(path)` / `Content.Load<SceneAsset>(path)` + `Content.GetAllAssetsByType(typeof(Prefab/Scene))` + `Content.GetAssetInfo` | |
| Preload for streaming | `Level.PreloadSceneAsync(Guid)` → finish with `LoadScene` | Not needed for v1 |
| Scene load events | `Level.SceneLoaded` / `SceneUnloaded` / `SceneLoading` / `SceneLoadError` (`Action<Scene, Guid>`) | Optional hooks |

**The crux:** `Level.LoadScene` is **additive**; `Level.ChangeSceneAsync` is **replace**.
Flax deliberately keeps multiple scenes live at once (`Level.Scenes` is an array).

---

## Why it's incomplete today (do not re-diagnose from scratch)

### 1. `SpawnScene` is mislabeled and has a real bug — `FlaxActions.cs:1420-1456`

```csharp
// Load the scene (replaces all currently loaded scenes, same as load scene).   ← WRONG
Level.LoadScene(sceneAsset.ID);          // actually ADDITIVE — adds on top
Actor spawnAt = ResolveThing(context, spec.To) as Actor;
if (spawnAt != null)
{
    Vector3 offset = spawnAt.Position;
    foreach (var obj in context.Runtime.World.Actors())   // ALL actors in the world!
    {
        if (!(obj is Actor actor)) continue;
        if (actor == spawnAt) continue;
        actor.Position = offset;          // moves EVERY pre-existing actor too
    }
}
```

- The comment is wrong: `Level.LoadScene` does **not** replace; it adds on top.
- The positioning loop iterates **every actor in the world** and moves it to the spawn point,
  including actors from scenes that were already loaded. Opening a UI overlay scene would yank
  the whole level to the spawn marker.

### 2. No "add scene on top" word

There is no event word for a clean additive load that leaves existing actors alone — the
UI-overlay use case. `spawn scene` conflates additive loading with positional instancing.

### 3. No "unload scene" word

Once an overlay is added, there is no event word to remove just it. `Level.UnloadScene` is
used only inside `ReplaceScene` (`FlaxActions.cs:2168-2171`), never exposed to the language.

### 4. `spawn prefab` / `spawn scene` missing from `WordRegistry`

They parse fine in `ActionWords.cs:376-404` (`ActionVerb.SpawnPrefab` / `SpawnScene` exist),
but `WordRegistry.cs` has only `spawn` → `spawn Coin at SpawnPoint`. So autocomplete, near-miss
suggestions, and docs-on-hover do not cover the prefab/scene variants.

### 5. `ChangeSceneAsync` (the dedicated replace primitive) is bypassed

`ReplaceScene` (`FlaxActions.cs:2166-2172`) manually loops `Level.UnloadScene` over every
loaded scene, then calls `Level.LoadScene`. This works and is synchronous (predictable for
`when scene starts`), but it reimplements `ChangeSceneAsync`. Decision below keeps the
synchronous manual approach (Part E notes the alternative).

### 6. Prefab spawn target is implicit

`PrefabManager.SpawnPrefab` always spawns into the **first loaded scene** (per docs). With
additive UI scenes loaded on top, be aware the spawn destination is implicit, not nameable.
Note only — no API exists to target a specific scene; leave as-is for v1.

---

## Decisions

1. **`load scene "<name>"`** stays **replace** semantics (unload all, load one). Keep the
   current synchronous `ReplaceScene` (manual unload-all + `LoadScene`) so `when scene
   starts` timing stays predictable. `ChangeSceneAsync` is async; adopting it would change
   trigger timing. Leave the manual path; document the choice in Part E.
2. **`add scene "<name>"`** = **additive** load via `Level.LoadScene`. Leaves existing actors
   untouched. This is the UI-overlay word. Matches the user's "addition a scene on top" ask.
3. **`unload scene "<name>"`** = remove one scene via `Level.UnloadScene` (by `Scene` or
   `Guid`). Lets you close an overlay. Guard against unloading the last scene (warn).
4. **`spawn scene "<name>" at <spot>"`** is fixed to be additive + reposition **only the
   newly loaded scene's** root actors (found via `Level.FindScene(id)` after load), never the
   whole world. It keeps the existing verb/shape so existing scripts don't break.
5. **`add scene`** takes no `at <spot>` (overlay stays at authored positions); **`spawn scene`**
   keeps the `at <spot>` form for positional instancing.
6. Add `spawn prefab`, `spawn scene`, `add scene`, `unload scene` to `WordRegistry` with
   concrete one-line examples (same style as the rest of the catalog).
7. Scene lookup by name reuses the existing 3-tier search in `LoadSceneByName` (loaded
   scenes → `Content.GetAllAssetsByType(typeof(Scene))` → raw `.scene` files). Factor it into
   a shared helper so `add scene` / `unload scene` / `spawn scene` all resolve names the same
   way and share the friendly "couldn't find a scene" message + `SceneNames()` list.

---

## Part A — Shared scene-name resolver

`FlaxActions.cs`: extract the body of `LoadSceneByName` (`:2173-2210`) into a reusable
resolver that returns the scene asset **id** (not loads it), so callers can load/unload:

```csharp
/// <summary>Resolves a scene name to its asset id, ignoring folder/extension/case.
/// Searches loaded scenes first, then the content database, then raw .scene files.</summary>
private static bool TryFindSceneId(string name, out Guid id)
{
    id = Guid.Empty;
    if (string.IsNullOrEmpty(name)) return false;
    // 1. loaded scenes (by scene.Name)
    // 2. Content.GetAllAssetsByType(typeof(Scene)) by filename
    // 3. raw Content/*.scene files on disk
    // ...same 3 tiers as LoadSceneByName, returning the id...
}
```

Keep `LoadSceneByName` calling `TryFindSceneId` then `ReplaceScene`. Add `SceneNames()` reuse.
On failure, every scene verb emits one friendly warning: `I couldn't find a scene named
"<name>". Scenes: <list>.`

- [x] `TryFindSceneId` extracted; `LoadSceneByName` uses it
- [x] `add scene` / `unload scene` / `spawn scene` share the resolver and the friendly message

---

## Part B — `add scene "<name>"` (additive, on top)

New `ActionVerb.AddScene` + parser branch + `FlaxActions.AddScene`:

```
add scene "HUD"
```

```csharp
private static void AddScene(EventContext context, ActionSpec spec)
{
    if (!TryFindSceneId(spec.Thing, out Guid id))
    {
        WarnOnce($"I couldn't find a scene named \"{spec.Thing}\". Scenes: {SceneNames()}.");
        return;
    }
    if (Level.FindScene(id) != null)
    {
        WarnOnce($"The scene \"{spec.Thing}\" is already loaded — add scene loads it on top, so you may want unload scene first.");
        return;   // or no-op; decide: warn is safer
    }
    Level.LoadScene(id);   // additive — existing actors untouched
}
```

`when scene starts` already fires on every scene load, so a freshly added overlay scene's
actors get their sheets. No actor repositioning.

- [x] `ActionVerb.AddScene` added to enum
- [x] parser branch: `add scene "<name>"` (string token after `scene`)
- [x] `FlaxActions.AddScene` implemented (additive, no move)
- [x] duplicate-load guard (warn once)

---

## Part C — `unload scene "<name>"` (remove one)

New `ActionVerb.UnloadScene` + parser branch + `FlaxActions.UnloadScene`:

```
unload scene "HUD"
```

```csharp
private static void UnloadScene(EventContext context, ActionSpec spec)
{
    // resolve by name: loaded scene whose Scene.Name matches, else asset id via TryFindSceneId
    Scene target = FindLoadedSceneByName(spec.Thing);
    if (target == null)
    {
        WarnOnce($"I couldn't find a loaded scene named \"{spec.Thing}\" to unload. Loaded scenes: {LoadedSceneNames()}.");
        return;
    }
    if (Level.Scenes.Length <= 1)
    {
        WarnOnce($"\"{spec.Thing}\" is the only loaded scene — unloading it would leave nothing. Use load scene to switch instead.");
        return;
    }
    Level.UnloadScene(target);
}
```

`Level.UnloadScene(Scene)` is synchronous and returns bool. Add a `LoadedSceneNames()` helper
(lists only currently-loaded scene names) for the friendly message.

- [x] `ActionVerb.UnloadScene` added to enum
- [x] parser branch: `unload scene "<name>"`
- [x] `FlaxActions.UnloadScene` implemented (remove one, last-scene guard)
- [x] `LoadedSceneNames()` helper added

---

## Part D — Fix `spawn scene "<name>" at <spot>"` (additive + reposition only the new scene)

Fix `SpawnScene` (`FlaxActions.cs:1420-1456`):

```csharp
private static void SpawnScene(EventContext context, ActionSpec spec)
{
    if (!TryFindSceneId(spec.Thing, out Guid id))
    {
        WarnOnce($"Couldn't load scene \"{spec.Thing}\". Scenes: {SceneNames()}.");
        return;
    }
    // additive load — does NOT replace existing scenes
    Level.LoadScene(id);
    Scene loaded = Level.FindScene(id);          // the newly added scene root
    Actor spawnAt = ResolveThing(context, spec.To) as Actor;
    if (spawnAt != null && loaded != null)
    {
        // offset ONLY the new scene's root children, by the spawn point's position
        // (relative to authored positions, so a scene built at origin lands at the spot)
        Vector3 to = spawnAt.Position;
        foreach (Actor child in loaded.Children)
            if (child != null)
                child.Position = child.Position + (to - loaded.Position);
    }
}
```

Notes:
- `Level.LoadScene` is additive — the existing comment ("replaces all") is deleted.
- Only `loaded.Children` (the new scene's root actors) move, never the whole world.
- The offset is relative to the scene's own root position, so a scene authored at origin lands
  at the spawn point and a scene authored elsewhere keeps its authored layout relative to the
  spot. Confirm the math in the editor test (Part G).
- If `Level.FindScene(id)` returns null right after a synchronous `LoadScene` (timing edge),
  fall back to diffing `Level.Scenes` (capture the set before, the new one is the extra) — note
  this as a backup if the editor test shows a null.

- [x] wrong comment removed; additive semantics documented in code
- [x] reposition loop limited to the new scene's root children
- [x] `Level.FindScene(id)` used to locate the new scene (diff fallback noted)

---

## Part E — `load scene` replace path (verify, optional cleanup)

`load scene` and `restart scene` already work (manual unload-all + `LoadScene`). Verify only:

- [x] `ReplaceScene` unloads every scene in `Level.Scenes` then loads — confirm no stragglers.
- [x] Decide whether to adopt `Level.ChangeSceneAsync` (async; changes `when scene starts`
      timing). **Recommendation: keep synchronous manual replace** — document the choice in a
      code comment so a future reader knows `ChangeSceneAsync` was considered.
- [ ] `restart scene` uses `scenes[0].ID` — confirm it restarts the first/main scene, not an
      overlay added on top. If overlays are loaded, `restart scene` may target the wrong one;
      if so, restart the **first non-overlay** scene or the scene flagged as the main level
      (note in `DESIGN.md` §9 if behavior needs clarifying).

---

## Part F — Word registry entries

`WordRegistry.cs` — add (next to the existing `spawn` / `load` / `restart` entries):

```csharp
new WordEntry { Kind = "action", Word = "spawn", Example = "spawn prefab \"Hero\" at SpawnPoint" },
new WordEntry { Kind = "action", Word = "spawn", Example = "spawn scene \"HeroScene\" at SpawnPoint" },
new WordEntry { Kind = "action", Word = "add", Example = "add scene \"HUD\"" },
new WordEntry { Kind = "action", Word = "unload", Example = "unload scene \"HUD\"" },
```

Keep the existing `spawn Coin at SpawnPoint` entry. This makes autocomplete, near-miss
suggestions, and docs-on-hover cover the prefab/scene/add/unload variants.

- [ ] four entries added; existing `spawn` / `load scene` / `restart scene` entries kept

---

## Part G — Tests + docs

### G.1 Pure parser tests (`Tests/Program.cs`)

- `add scene "HUD"` parses → `ActionVerb.AddScene`, `Thing == "HUD"`.
- `unload scene "HUD"` parses → `ActionVerb.UnloadScene`, `Thing == "HUD"`.
- `spawn prefab "Hero" at SpawnPoint` still parses (regression).
- `spawn scene "HeroScene" at SpawnPoint` still parses (regression).
- Unknown form of each verb gets the shape-explaining friendly error (e.g.
  `add scene` without a quoted name).

### G.2 Docs

- `DESIGN.md` §5 word table: add `add scene "<name>"` and `unload scene "<name>"` rows;
  clarify `load scene` = replace, `add scene` = on top. Add a short "Scenes (additive overlays)"
  note near "Menus & game UI" describing the overlay pattern (add HUD scene on top, unload to
  close; `load scene` wipes everything including overlays).
- `DESIGN.md` §9: note the `restart scene` + overlay interaction (Part E) if it needs a rule.
- `TASKS.md`: log this fix under Task 17 Part D (now expanded: prefab spawn ✓, scene spawn
  fixed, add scene + unload scene added).

- [x] parser tests added (4 new + 2 regression)
- [x] `DESIGN.md` §5 table + overlay note added
- [x] `TASKS.md` Task 17 Part D updated

---

## Part H — Editor acceptance (user)

1. **Replace:** `load scene "Level2"` swaps the whole level; `when scene starts` fires; no
   actors from the old scene remain.
2. **Add overlay:** `add scene "HUD"` adds the HUD on top; the level underneath is untouched
   and still running (player still controllable); HUD actors exist.
3. **Unload overlay:** `unload scene "HUD"` removes only the HUD; the level keeps running.
4. **Last-scene guard:** `unload scene` on the only loaded scene warns and does nothing.
5. **Prefab:** `spawn prefab "Hero" at SpawnPoint"` instantiates the prefab asset at the
   spawn point; the clone's `EventSheet` (if the prefab carries one) runs `when me appears`.
6. **Scene-at-spot:** `spawn scene "HeroScene" at SpawnPoint"` loads the scene additively and
   its root actors appear at the spawn point; pre-existing actors do **not** jump.
7. Console: no "couldn't find a scene" spam, no "replaces all" miscomment confusion.

---

## Files likely touched

| File | Change |
|---|---|
| `Source/Game/Runtime/ActionWords.cs` | `AddScene` / `UnloadScene` enum + parser branches |
| `Source/Game/Bridge/FlaxActions.cs` | `TryFindSceneId`, `AddScene`, `UnloadScene`, fix `SpawnScene`, `LoadedSceneNames` |
| `Source/Game/Events/WordRegistry.cs` | 4 new entries (spawn prefab/scene, add scene, unload scene) |
| `Tests/Program.cs` | parser tests + regressions |
| `DESIGN.md` | §5 table + overlay note, §9 if needed |
| `TASKS.md` | Task 17 Part D log |

Do **not** edit `.scene` files unless the user explicitly asks; overlay/prefab asset creation
is the user's editor work.

---

## Done when

- [x] `load scene` still replaces (regression-free).
- [x] `add scene "<name>"` loads a scene on top without moving existing actors.
- [x] `unload scene "<name>"` removes one scene; guards the last scene.
- [x] `spawn scene "<name>" at <spot>"` is additive and repositions only the new scene's actors.
- [x] `spawn prefab "<name>" at <spot>"` instantiates a prefab asset (already works; registry added).
- [x] `spawn prefab` / `spawn scene` / `add scene` / `unload scene` are in `WordRegistry`.
- [x] Parser tests + regressions pass; `Source/Game.csproj` builds clean.
- [x] `DESIGN.md` / `TASKS.md` updated.
- [ ] Editor acceptance (Part H) confirmed by the user.

---

## Watch-outs

- **`Level.LoadScene` is additive; `Level.ChangeSceneAsync` is replace.** Never assume
  `LoadScene` replaces — it adds to `Level.Scenes[]`.
- **`Level.UnloadScene` on the last scene leaves nothing.** The guard prevents an empty world.
- **`restart scene` targets `scenes[0]`.** If overlays are loaded on top, `scenes[0]` is still
  the first/main scene (overlays are appended), so it should stay correct — confirm in Part H.
- **Prefab spawn target is implicit** (`PrefabManager.SpawnPrefab` → first loaded scene). With
  overlays loaded on top, "first loaded scene" is still the main level (loaded first). No API
  to target a specific scene exists in 1.13; leave as-is.
- **`when scene starts` fires on every scene load**, including additive `add scene`. An overlay
  scene's `when scene starts` will run when it's added — usually desired for HUD init.
- **Hot reload + scenes:** the `EventsManager` lives on a permanent scene-root actor (per the
  shooter fix). An `add scene` overlay that contains its own `EventsManager` would spawn a
  duplicate runtime — keep the manager only in the main scene, never in overlay scenes.
- **Synchronous vs async:** `LoadScene` / `UnloadScene` are synchronous (return bool).
  `LoadSceneAsync` / `ChangeSceneAsync` are background; v1 uses synchronous for predictable
  `when scene starts` timing.
- **Name matching:** scene lookup is by filename (folder/extension/case-insensitive), same as
  the existing `LoadSceneByName` tiers. Two scenes with the same filename resolve to the first
  found — note if it bites.
