# TASK_FIX_ANIMATION.md — dragon/character animation actually plays (Flax 1.13)

**Status:** `[~]` code complete (2026-11-01); editor acceptance pending
**Scene:** `Content/scenes/char_animated_test.scene` (dragon = `Content/chars/BGE Dragon 2.07.flax`)
**Depends on:** Task 3 bridge (`FlaxActions`), Task 5 Part B (anim-end)
**Source of truth:** `DESIGN.md` §"Armature characters"; this file for the 1.13 API fix

> Fresh session: read `DESIGN.md` §"Armature characters" and this file. Do the parts in order.
> `when <key> is held` fires **every tick** (`Tests/Program.cs:651-662`), so animation words must
> never restart a clip that is already playing.

---

## Goal

1. `play anim "<clip>"` and `loop anim "<clip>"` visibly animate an `AnimatedModel` in play mode.
2. `stop anim` returns to the previous clip (or bind pose when there is none).
3. `when anim "<clip>" ends` fires once for one-shots.
4. A missing/typo'd clip produces **one** friendly warning listing the available clips.
5. No per-character Anim Graph authoring: the bridge builds the graph in code.

Out of scope: animation cross-blending, root motion, layered/masked animation, animation events.

---

## Root causes (verified 2026-11-01)

### 1. The bridge targets a Flax API that does not exist in 1.13

Installed engine: `C:\Program Files (x86)\Flax\Flax_1.13`. Its `FlaxEngine.CSharp.xml` has **no**
`AnimatedModel.PlayState`, `AnimatedModel.AnimationController`, `CurrentState` or `Loop`.
The 1.13 surface is:

- `AnimatedModel.AnimationGraph` (assign an `AnimationGraph`)
- `AnimatedModel.PlaySlotAnimation(...)` / `StopSlotAnimation(...)` (needs a Slot node in the graph)
- `AnimatedModel.SetParameterValue(...)`, `ResetAnimation()`, `UpdateAnimation()`
- `Animation.Length` (seconds), `Animation.Duration` (frames)

`Source/Game/Bridge/FlaxActions.cs`:

- `CallPlayState()` (`:989-1000`) reflects `GetMethod("PlayState")` → `null` → silently does nothing.
- `HasAnim()` (`:920-941`) sees `AnimationController == null` and returns **`true`**, so the
  "I don't know the anim" warning never fires.
- `CurrentAnim()` / `SetLooping()` (`:943-974`) also read the missing controller.

`Source/Game/Bridge/EventSheet.cs` (`:93-121`) reflects `AnimationController` too, returns early,
and therefore never subscribes anything → `when anim "…" ends` is dead.

### 2. `char_animated_test.scene` has no Anim Graph

`char_animated_test.scene:177` only sets `"SkinnedModel": "e8d3c77043c605ef45357397f332e52b"`.
`char_test.scene:122` has `"AnimationGraph": "f1440d044fc8419c0f21b48dfdcf0c9d"` — but that graph
was deleted, so `char_test.scene` is stale too.

### 3. The EventSheet points at the wrong file and names

`char_animated_test.scene:205-207` sets `"EventFile": "Character"`, so `dragon.events`
(`when game starts → play anim "walk"`) never runs.

`Character.events` has no `when game starts`; it only reacts to W/LMB and uses
`"Walk"` / `"Idle"` / `"Shoot"`. The dragon clips are `walk`, `Run`, `idle Pose`
(`Content/BGE Dragon 2.07/…`), and the old `Content/chars/BGE Dragon_animeations …` set.

### 4. Held triggers fire every tick

`loop anim "Walk"` under `when W is held` runs ~60×/s. Any implementation that re-creates or
re-assigns the graph each call will restart the clip every frame and look frozen. This must be a
no-op when the same clip is already playing.

---

## Decisions

1. **Play clips by Animation asset name**, not by graph state (1.13 has no named states).
2. **Build a virtual graph per clip** in code:
   `Content.CreateVirtualAsset<AnimationGraph>()` + `InitAsAnimation(skinnedModel, anim, loop, false)`.
   No graph asset needs to be authored or assigned. (`chars/Animation Graph.flax` is optional and
   can stay for experiments.)
3. `play anim` → `loop: false` (holds last frame). `loop anim` → `loop: true`.
4. `stop anim` → re-assign the previous graph; clear to bind pose when there is no previous.
5. One-shot end = timer against `Animation.Length`, checked once per frame in `FlaxActions.Step`.
6. **No cross-blend in v1** — swapping graphs may pop. A later upgrade can use a shipped
   Slot graph + `PlaySlotAnimation(..., blendIn, blendOut, ...)` if blending becomes necessary.
7. Clip lookup ignores folder/`.flax`/case and also accepts space-boundary suffixes, so
   `"walk"` finds `BGE Dragon 2.07 animation walk.flax` and `"idle Pose"` finds
   `… animation idle Pose.flax`.

---

## Part 1 — Animation name index

### 1.1 `Source/Game/Runtime/AssetNames.cs`

Add a pure helper (no Flax types, unit-testable):

```csharp
/// <summary>
/// Yields the key plus every space-boundary suffix:
/// "bge dragon 2.07 animation walk" → "walk", "animation walk", …
/// </summary>
public static IEnumerable<string> Suffixes(string key)
{
    if (string.IsNullOrEmpty(key))
        yield break;
    yield return key;
    int space = key.IndexOf(' ');
    while (space >= 0 && space + 1 < key.Length)
    {
        yield return key.Substring(space + 1);
        space = key.IndexOf(' ', space + 1);
    }
}
```

Add `using System.Collections.Generic;`.

### 1.2 `FlaxActions.cs` — registry index (mirrors the sound index)

```csharp
private static Dictionary<string, Guid> AnimIndexCache;
private static List<string> AnimFullNames;

private static Dictionary<string, Guid> AnimIndex()
{
    if (AnimIndexCache != null)
        return AnimIndexCache;

    var index = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
    var full = new List<string>();
    foreach (Guid id in Content.GetAllAssetsByType(typeof(Animation)))
    {
        if (!Content.GetAssetInfo(id, out AssetInfo info))
            continue;
        string key = AssetNames.Key(info.Path);
        if (key.Length == 0)
            continue;
        if (!full.Contains(key))
            full.Add(key);
        foreach (string alias in AssetNames.Suffixes(key))
        {
            if (alias.Length == 0 || index.ContainsKey(alias))
                continue;                       // first asset wins
            index[alias] = id;
        }
    }
    AnimIndexCache = index;
    AnimFullNames = full;
    return index;
}

private static Animation LoadAnimation(string name)
{
    return AnimIndex().TryGetValue(AssetNames.Key(name), out Guid id)
        ? Content.Load<Animation>(id)
        : null;
}

private static string AvailableAnimNames()
{
    AnimIndex();
    var names = new List<string>(AnimFullNames);
    names.Sort(StringComparer.OrdinalIgnoreCase);
    return names.Count == 0 ? "(none)" : string.Join(", ", names);
}
```

Reset both in `ClearSession()` (`AnimIndexCache = null; AnimFullNames = null;`).

- [x] `AssetNames.Suffixes` added
- [x] `AnimIndex` / `LoadAnimation` / `AvailableAnimNames` added
- [x] index reset in `ClearSession()`

---

## Part 2 — `play anim` / `loop anim`

Replace `PlayAnim` / `LoopAnim` (`FlaxActions.cs:815-863`) with one path:

```csharp
private sealed class AnimState
{
    public string Clip;                 // asset key currently playing
    public bool Loop;
    public bool OneShot;
    public double EndTime;              // one-shot only
    public AnimationGraph Graph;        // assigned to the model
    public AnimationGraph PreviousGraph;
    public string PreviousClip;
    public bool PreviousLoop;
}

private static readonly Dictionary<object, AnimState> AnimStates = new();

private static void PlayAnim(EventContext context, ActionSpec spec) => StartAnim(context, spec, false);
private static void LoopAnim(EventContext context, ActionSpec spec) => StartAnim(context, spec, true);

private static void StartAnim(EventContext context, ActionSpec spec, bool loop)
{
    var me = context.Me as Actor;
    if (me == null)
    {
        WarnOnce($"\"{(loop ? "loop anim" : "play anim")} \"{spec.Thing}\"\" needs to run on an actor's sheet — the global Game sheet has no model to animate.");
        return;
    }
    var model = me.FindActor<FlaxEngine.AnimatedModel>();
    if (model == null)
    {
        WarnOnce($"\"{(loop ? "loop anim" : "play anim")} \"{spec.Thing}\"\" needs an AnimatedModel on \"{me.Name}\".");
        return;
    }

    Animation anim = LoadAnimation(spec.Thing);
    if (anim == null)
    {
        WarnOnce($"I don't know the anim \"{spec.Thing}\". This project has: {AvailableAnimNames()}.");
        return;
    }

    string clip = AssetNames.Key(spec.Thing);
    AnimStates.TryGetValue(me, out AnimState state);

    bool sameClip = state != null && state.Clip == clip;
    bool stillPlaying = state != null && (!state.OneShot || Time.GameTime < state.EndTime);
    if (sameClip && stillPlaying)
        return;                          // held triggers fire every tick — never restart mid-clip

    var graph = Content.CreateVirtualAsset<AnimationGraph>();
    if (graph.InitAsAnimation(model.SkinnedModel, anim, loop, false))
    {
        // Docs: InitAsAnimation returns TRUE on failure.
        FlaxEngine.Object.Destroy(graph);
        WarnOnce($"Could not play \"{spec.Thing}\" on \"{me.Name}\" — the clip and the skinned model may use different skeletons.");
        return;
    }

    model.AnimationGraph = graph;
    if (state?.PreviousGraph != null)
        FlaxEngine.Object.Destroy(state.PreviousGraph);   // keep only two graphs alive

    AnimStates[me] = new AnimState
    {
        Clip = clip,
        Loop = loop,
        OneShot = !loop,
        EndTime = loop ? double.MaxValue : Time.GameTime + anim.Length,
        Graph = graph,
        PreviousGraph = state?.Graph,
        PreviousClip = state?.Clip,
        PreviousLoop = state?.Loop ?? true,
    };
}
```

Notes:

- A finished one-shot is restarted by the next `play anim` call because `stillPlaying` is false;
  a fresh virtual graph is created and assigned, which restarts the clip.
- `InitAsAnimation` uses speed 1.0; `Animation.Length` is seconds.
- Never destroy the graph currently assigned to a model.

- [x] `AnimState` + `AnimStates` added
- [x] `PlayAnim` / `LoopAnim` route through `StartAnim`
- [x] same-clip no-op guard added
- [x] virtual graph created + `InitAsAnimation` failure handled
- [x] old graph destroyed only after the new one is assigned

---

## Part 3 — `stop anim`

Replace `StopAnim` (`FlaxActions.cs:866-884`):

```csharp
private static void StopAnim(EventContext context, ActionSpec spec)
{
    var me = context.Me as Actor;
    if (me == null)
    {
        WarnOnce("\"stop anim\" needs to run on an actor's sheet — the global Game sheet has no model to animate.");
        return;
    }
    var model = me.FindActor<FlaxEngine.AnimatedModel>();
    if (model == null)
        return;

    AnimStates.TryGetValue(me, out AnimState state);
    if (state == null)
    {
        model.AnimationGraph = null;     // nothing tracked: go to bind pose
        return;
    }

    if (state.PreviousGraph != null)
    {
        model.AnimationGraph = state.PreviousGraph;
        if (state.Graph != null)
            FlaxEngine.Object.Destroy(state.Graph);
        AnimStates[me] = new AnimState
        {
            Clip = state.PreviousClip ?? "",
            Loop = state.PreviousLoop,
            OneShot = false,
            EndTime = double.MaxValue,
            Graph = state.PreviousGraph,
        };
        return;
    }

    model.AnimationGraph = null;
    if (state.Graph != null)
        FlaxEngine.Object.Destroy(state.Graph);
    AnimStates.Remove(me);
}
```

- [x] `stop anim` restores the previous graph
- [x] clears to bind pose when no previous exists
- [x] destroyed graphs are removed from the model first

---

## Part 4 — one-shot end (`when anim "…" ends`)

In `FlaxActions.Step(float deltaTime)`, after the sounds/moves cleanup, add:

```csharp
// Fire `when anim "<clip>" ends` once per one-shot.
List<(Actor Actor, string Clip)> ended = null;
foreach (var pair in AnimStates)
{
    AnimState s = pair.Value;
    if (!s.OneShot || Time.GameTime < s.EndTime)
        continue;
    s.OneShot = false;
    if (pair.Key is Actor actor)
        (ended ??= new List<(Actor, string)>()).Add((actor, s.Clip));
}
if (ended != null)
{
    foreach (var (actor, clip) in ended)
        EventsManager.Instance?.HandleAnimEnd(actor, clip);
}
```

Collect first, then fire: `HandleAnimEnd` pushes a trigger and the sheet may immediately play
another clip (mutating `AnimStates`). The clip name must be captured before firing.

Then delete the dead one-shot bookkeeping: `PreviousAnim`, `OneShotAnims`, `MarkOneShot`,
`ClearOneShot`, `IsOneShot` (`FlaxActions.cs:28-31`, `:886-911`), and update `ClearActor`.

- [x] end check added to `Step`
- [x] `HandleAnimEnd` receives the ended clip, not the replacement
- [x] `ClearActor` destroys that actor's graphs and removes its `AnimState`
- [x] `ClearSession` destroys all remaining graphs
- [x] old one-shot dictionaries removed

---

## Part 5 — `EventSheet.cs` cleanup

Delete the reflection-based animation-end plumbing, which can never work on 1.13:

- `_animEndSubscriptions` field (`:30`)
- `SubscribeAnimEnd(actor)` call in `SubscribeTree` (`:60`)
- `SubscribeAnimEnd`, `SubscribeAnimEndEvent`, `UnsubscribeAnimEndEvent`, `SubscribeEvent`,
  `UnsubscribeEvent`, `UnsubscribeAnimEnd` (`:93-155`)
- the `UnsubscribeAnimEnd()` call in `OnDestroy` (`:42`)

Keep the collider `TriggerEnter` / `TriggerExit` subscription untouched.

- [x] anim-end reflection removed
- [x] collider subscription still works
- [x] build has no unused-member warnings

---

## Part 6 — scene + events files

1. `Source/Game/GameFiles/dragon.events` — make the start rule loop so the dragon keeps moving:

   ```
   # --- dragon.events (char_animated_test idle verification) ---
   when game starts
     loop anim "walk"
   ```

2. `Content/scenes/char_animated_test.scene:205-207` — point the EventSheet at the dragon file:

   ```json
   "V": {
       "EventFile": "dragon"
   }
   ```

   (`SourceName()` strips `.events`, so `"dragon"` and `"dragon.events"` both work.)

3. Do **not** attach `Character.events` to the dragon: it is the armature character's sheet and
   its `"Idle"` / `"Shoot"` clips do not exist on the dragon. Keep it for the other character.

4. Clip-name collisions: `"walk"` exists in both `Content/chars/BGE Dragon_animeations walk.flax`
   and `Content/BGE Dragon 2.07/BGE Dragon 2.07 animation walk.flax`, so the plain word is a coin
   flip (first registry entry wins). **Resolved without deleting anything:** the alias
   `"animation walk"` exists only on the 2.07 clip, so `dragon.events` uses that. If the 2.07
   clips ever prove to be the wrong rig, rename the three clips in the Content browser to
   `walk`, `Run`, `idle Pose` (GUIDs survive a rename) or delete the obsolete
   `Content/chars/BGE Dragon_animeations *.flax` set.

5. `chars/Animation Graph.flax` is **not needed** by this plan. Leave it in place (it references
   the new walk clip) but do not assign it. Assigning it would override the virtual graphs.

6. Optional cleanup: `Content/char_test.scene` still references the deleted graph
   `f1440d044fc8419c0f21b48dfdcf0c9d`; delete the scene or re-point it to
   `chars/Animation Graph.flax`.

- [x] `dragon.events` uses `loop anim "animation walk"`
- [x] scene `EventFile` is `"dragon"`
- [x] clip name collision resolved by using the unambiguous alias (no assets deleted)
- [x] `Character.events` left for the other character

---

## Part 7 — tests + docs

### 7.1 Unit tests (`Tests/Program.cs`)

Pure `AssetNames` checks (test project only globs `Events/` + `Runtime/`):

```csharp
Check(AssetNames.Suffixes("bge dragon 2.07 animation walk").Contains("walk"),
    "a verbose clip path answers to its last word");
Check(AssetNames.Suffixes("bge dragon 2.07 animation idle pose").Contains("idle pose"),
    "multi-word clip names keep their tail");
Check(AssetNames.Suffixes("walk").Count() == 1,
    "a plain name yields only itself");
Check(AssetNames.Suffixes("").Count() == 0,
    "empty key yields nothing");
```

### 7.2 Docs

- `DESIGN.md` §"Armature characters": replace the `AnimatedModel.PlayState("Walk")` /
  "template graph with named states" text with the 1.13 reality:
  - clip name = `Animation` asset name (folder/extension/case-insensitive, suffix-friendly),
  - the bridge builds a virtual graph per clip via `AnimationGraph.InitAsAnimation`,
  - `play anim` = once + hold, `loop anim` = repeat, `stop anim` = previous clip,
  - no cross-blend yet (note as a known limitation).
- `DESIGN.md` word table: keep `play anim` / `loop anim` rows; adjust the `stop anim` note only if
  the implemented semantics differ.
- `TASKS.md`: add a log entry for this fix (root cause + files touched) and the verification list.

- [x] `AssetNames.Suffixes` tests added
- [x] `DESIGN.md` armature section updated
- [x] `TASKS.md` log entry added

---

## Implementation notes (2026-11-01)

Two things surfaced while implementing that were not in the original diagnosis:

1. **The sheet usually sits on the `AnimatedModel` itself.** In `char_animated_test.scene` the
   `EventSheet`'s parent is the dragon model actor, and `Actor.FindActor<T>()` only searches
   children. `ModelOf()` now checks `actor as AnimatedModel` first, then falls back to
   `FindActor<AnimatedModel>()`, so both layouts work.
2. **`when anim "..." ends` name matching.** The bridge pushes the clip name exactly as written in
   the event (not the normalized lookup key), and `EventRuntime.Matches` compares `AnimEnd`
   names case-insensitively. So `play anim "Walk"` answers `when anim "walk" ends`; message
   names keep their exact-match rule. Covered by a new unit test (257/257 green).

Also: `"walk"` is ambiguous (two dragon clip sets), so `dragon.events` uses the unambiguous alias
`"animation walk"` and no assets were deleted.

---

## Verification

Build / tests (repo root):

```
dotnet build "EventManager/EventManager/Source/Game.csproj"
dotnet run --project "EventManager/EventManager/Tests/EventManagerTests.csproj"
```

Editor checklist (human, play mode):

1. Play `char_animated_test`: the dragon is animating immediately (walk loop), not in bind pose.
2. Output log has **zero** `needs an AnimatedModel` / `I don't know the anim` lines.
3. Misspell once (`play anim "walked"`): exactly one warning listing the real clip names.
4. Held trigger: put `loop anim "walk"` under `when W is held`; the clip keeps playing smoothly
   (no per-frame restart/jitter).
5. One-shot: `play anim "walk"` then `when anim "walk" ends → print "ended"` fires once.
6. `stop anim` returns to the previous clip; with no previous it returns to bind pose.
7. No "different skeletons" warning for the chosen clip set.

---

## Risks / notes

- **Virtual asset lifetime:** keep the graph assigned to the model alive; destroy a graph only
  after another graph has replaced it (and only once). `ClearActor` / `ClearSession` must destroy
  leftovers.
- **`InitAsAnimation` returns `true` on failure** (verified in the 1.13 engine source), not false.
- **Assigning a virtual graph overrides any authored graph** on the model. That is intentional for
  the event words; document it.
- **Duplicate clip names:** suffix aliases make `"walk"` convenient but two assets can share the
  alias. First registry entry wins; rename/delete the loser if it picks the wrong rig.
- **Skeleton compatibility:** old `chars/BGE Dragon_animeations …` clips came from a different
  import than `BGE Dragon 2.07.glb`. If the animation looks broken, switch to the new
  `Content/BGE Dragon 2.07/` clips.
- **No cross-blend:** swapping `AnimationGraph` may pop between clips. If that matters later, ship
  one graph with an Animation Slot node and use
  `PlaySlotAnimation("Default", anim, 1, 0.15f, 0.15f, loop ? -1 : 0)`; that also gives
  `IsPlayingSlotAnimation` for end detection.
- **Restarting a finished one-shot:** every start creates a *new* virtual graph and assigns it, so
  the engine's graph-change handling (`AnimatedModel::OnGraphChanged` -> `GraphInstance.Clear()`)
  restarts the clip. `ResetAnimation()` was not needed and was deliberately not called.
- The asset registry (`Content.GetAllAssetsByType`) is populated in the editor / cooked builds;
  this project runs in editor play mode.
