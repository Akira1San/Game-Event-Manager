# TASK_FIX_SHOOTER.md — Make bullets actually kill enemies

**Status:** `[~]` code complete (Parts A–D + tests); editor acceptance pending
**Depends on:** Tasks 1–3, Task 4 Part A (example files + scene exist)
**Source of truth:** `DESIGN.md` §6–§7, `TASKS.md` Task 4
**Editor setup:** some steps are the user's (manager move, colliders). Code can land first.

> Fresh session: read `DESIGN.md` §6–§7, `TASKS.md` Task 4, then this file. Do the
> parts in order. Do not hardcode Flax types into the pure runtime project.

---

## Goal

In `Scene.scene`, firing a bullet at an enemy must eventually kill it and raise `Score`.
Getting touched by an enemy must cost a life, blink the player for 1 second, and still
resume. Win at 5 kills, Game Over at 0 lives.

The example must work **with no colliders** (distance contacts) and **with physics
colliders** (Flax trigger events), selectable at runtime so the user can compare. Final
target is physics colliders.

---

## Decisions already made

- Implement **both** contact methods and a `Contact Mode` switch on `EventsManager`:
  `Distance`, `Colliders`, `Both`.
- Default to **`Both`** while comparing; switch to **`Colliders`** after the physics
  setup is verified and keep that as the final configuration.
- `overlaps` stays **distance-based in every mode** (`DESIGN.md:260` /
  `DESIGN.md:782`). The Player therefore needs no collider.
- The **user moves `EventsManager`** off `Player` to a permanent scene-root actor in the
  editor. Do not edit `Scene.scene` for that.
- Physics colliders are the user's editor work (exact setup below). If the user later asks
  for it, the same nodes can be added to `Scene.scene`.
- `Enemy.events` sets health to 3, so **three bullets** kill an enemy. Do not silently
  change it to one-shot; mention it in the hand-off.

---

## Why it is broken today (do not re-diagnose from scratch)

1. **Wrong name source.** `EventRuntime.cs:108` and `:333-336` use `Actor.ToString()`.
   Flax `Actor::ToString()` is `"{name} ({type}; {id})"` (`Source/Engine/Level/Actor.cpp`),
   so `SheetInstance.ActorName` is `"Enemy (FlaxEngine.StaticModel; <guid>)"`.
   Consequences:
   - `me touches Enemy` never matches (`Matches`, `EventRuntime.cs:348-355`).
   - `send message "..." to <name>` finds no sheet (`:194-204`).
   - `FindNamedActor` / `FindActorSheet` fail (`:206-220`) → `exists`, `near`, name targets.
   - Tests miss it because `FakeActor.ToString()` returns exactly `Name`
     (`Tests/Program.cs:1150-1163`).
2. **No contacts without colliders.** `EventSheet.cs:146-154` pushes `Touches`/`Overlaps`
   only from `Collider.TriggerEnter`; `EventsManager.cs:23-29` is the only producer.
   The scene has no colliders, so nothing is pushed. `Player.events:49` uses
   `me overlaps Enemy`, which `DESIGN.md` defines as distance-based, but no distance
   polling exists.
3. **Manager on a hideable actor.** `Scene.scene:163-166` makes `EventsManager` a child of
   `Player`. `Player.events:54-57` does `hide me` → `Player.IsActive = false` → the
   manager's `OnUpdate` (`EventsManager.cs:326-338`) stops, so the pending
   `wait 1 second` / `show me` can never resume.
4. **Hit is broadcast.** `Bullet.events:6` sends `"hit"` with no target → every enemy takes
   damage (`EventRuntime.cs:196-199`).
5. **Collider owner attribution.** `EventSheet.OwnerOf` (`:156-164`) returns the immediate
   parent, which is a `RigidBody` in the usual nesting, not `Enemy`/`Bullet`.
6. **Tag bug.** `FlaxWorld.cs:49-55` compares `actor.Name == tag` instead of
   `actor.HasTag(tag)`.
7. **Not a bug:** `Enemy.events:2-3` sets health 3, so one hit is not a kill by design.

---

## Part A — Naming seam (fixes every name lookup)

- [x] Extend `Runtime/EventWorld.cs`:
  - `string NameOf(object actor);`
  - `bool IsTouching(object a, object b);`
  - `IReadOnlyList<object> Actors();`
  - `DefaultEventWorld`: `NameOf` → `actor?.ToString()`, `IsTouching` → `false`,
    `Actors()` → empty list (keeps pure tests safe by default).
- [x] `FlaxWorld`:
  - `NameOf` → `(actor as Actor)?.Name ?? actor?.ToString() ?? ""`.
  - `HasTag` → `actor.HasTag(tag)` (Flax `Tag` converts from `string`; verify at build).
  - `IsTouching` → false if either actor is null, deleted, or `!IsActive`; otherwise
    `Vector3.Distance(a.Position, b.Position) <= aRadius + bRadius`.
    Use `Actor.BoundingSphere` (radius in world units; confirm in the first playtest) with
    a small margin. If bounds turn out to be local, transform the center or fall back to a
    tuned constant and note it.
  - `Actors()` → every non-Scene actor in `Level.Scenes` (active only; templates are
    inactive and must not contact).
- [x] `EventRuntime`:
  - `AddSheet` sets `ActorName = World.NameOf(actor)`.
  - private `NameOf` uses `World.NameOf`.
  - 2-arg `AddSheet` source name may keep `ToString()` for tests; not used by the bridge.
- [x] Regression test: a fake whose `ToString()` is decorated but whose `Name` is clean;
      `FakeWorld.NameOf` returns `Name`. Prove `me touches Enemy`, `to Enemy`, `exists`,
      and `near` all work.

## Part B — Distance contacts (works with zero colliders)

- [x] `SheetInstance`: add one contact set per trigger, e.g.
      `HashSet<object>[] Contacts`, created in `ReadyTree` and cleared on a program swap.
- [x] `EventRuntime` public flags (bridge sets them):
      `bool PollTouches`, `bool PollOverlaps`.
- [x] In `TryPolled`, handle `Touches`/`Overlaps` with **enter-edge** semantics:
  - `Overlaps` is polled in **all** modes.
  - `Touches` is polled when `PollTouches` is true.
  - Iterate `World.Actors()`; skip self; name-match `World.NameOf(other)` against
    `descriptor.RequireName`; treat `RequireName == "me"` as “any actor”
    (this fixes `when other touches me`).
  - Only call `RunEvent(sheet, eventIndex, other)` when the pair is new, at most once per
    tick (`FiredThisTick`). Add the pair to the event's set; remove pairs that are no
    longer touching. New pairs blocked by `FiredThisTick` fire on the next tick.
- [x] When a pushed contact is processed in `Tick`, add `signal.Other` to the matching
      event's contact set so `Both` mode does not fire the same pair twice.

## Part C — Collider path + mode switch

- [x] `EventSheet.OwnerOf`: walk `collider → parents` until an actor has an
      `EventSheet` component; if none, return the topmost ancestor below `Scene`.
      This makes `Bullet → RigidBody → SphereCollider` resolve to `Bullet`, and the same
      for `Enemy`.
- [x] `EventSheet` already subscribes colliders in `SubscribeTree`; keep it. Keep
      `HandleTriggerEnter`/`Exit`; `HandleTouch` currently ignores exits.
- [x] `EventsManager`:
  - `public enum EventContactMode { Distance, Colliders, Both }`
  - `public EventContactMode ContactMode = EventContactMode.Both;` (visible in inspector)
  - In `OnStart`, wire:
    - `Distance`: `HandleTouch` no-op; `PollTouches = true`; `PollOverlaps = true`.
    - `Colliders`: `HandleTouch` pushes; `PollTouches = false`; `PollOverlaps = true`.
    - `Both`: pushes + `PollTouches = true`; `PollOverlaps = true`.
  - Collider pushes still emit both `Touches` and `Overlaps`; `Overlaps` is deduped by the
    contact sets.
- [ ] Physics setup (user, editor):

  ```
  Bullet (StaticModel, EventSheet)
  └─ RigidBody        IsKinematic = true, EnableGravity = false, UseCCD = false
     └─ SphereCollider   IsTrigger = true   (size to the sphere)

  Enemy (StaticModel, EventSheet)
  └─ RigidBody        IsKinematic = true, EnableGravity = false
     └─ BoxCollider     IsTrigger = false  (solid, size to the cube)
  ```

  - Set this on the **templates**; clones inherit the subtree.
  - Both actors must have colliding layers (defaults usually collide).
  - PhysX does not create static–static pairs, hence the kinematic `RigidBody`.
  - **Leave `UseCCD` off.** PhysX rejects CCD on kinematic bodies
    (`kinematic bodies with CCD enabled are not supported! CCD will be ignored`) — it is
    ignored anyway, so it only produces a startup error. CCD is for fast *dynamic* bodies.
  - Trigger + solid is the safe pair; Flax's filter shader lets trigger pairs through.
  - At speed 80 the bullet moves ~1.3 units/frame vs a ~28-unit enemy — no tunneling
    expected without CCD. If misses appear, lower the speed, raise the bullet collider's
    `Contact Offset`, or use `Both`.
- [ ] If the kinematic body visibly lags the transform-driven bullet, change movement for
      physics actors to `Actor.AddMovement` instead of setting `Position` (only then).

## Part D — Messages and example files

- [x] `ExecuteSend` (`EventRuntime.cs:479-488`):
  - `to me` → enqueue on the sheet bound to `context.Me`.
  - `to other` → enqueue on the sheet bound to `context.Other`
    (identity via `FindSheetFor`, not a name lookup).
  - `to <name>` → existing `SendRawMessage` (now correct after Part A).
  - No target → broadcast (unchanged).
- [x] `GameFiles/Bullet.events`:
  - `send message "hit" to other` (line 6).
  - Keep `when every 0 seconds` for continuous forward motion; the current `move`
    implementation is a per-tick actuator, so `when me appears` would move one frame only.
- [x] Confirm `Player.events` `me overlaps Enemy` now works through distance polling.
- [x] Leave `Enemy.events` health at 3 (three hits).

## Part E — Tests, build, editor acceptance

- [x] Unit tests (add to `Tests/Program.cs`):
  - Flax-like fake naming regression (decorated `ToString`, clean `Name`).
  - Distance touch: fires once on enter, not every tick; re-fires after separation and
    re-entry.
  - Overlaps polls in all modes/flags.
  - `Both` dedupe: a collider push followed by a distance poll in the same/next tick only
    runs the event once.
  - `send message "hit" to other`: only the touched enemy's sheet is affected; a second
    enemy's health/score is untouched.
  - Broadcast still reaches every matching sheet.
  - `to <name>` still works with a decorated fake.
  - `when other touches me` wildcard matches any toucher.
- [x] Commands:
  - `dotnet run --project "EventManager/EventManager/Tests/EventManagerTests.csproj"`
  - `dotnet build "EventManager/EventManager/Source/Game.csproj"`
- [ ] Editor acceptance (user):
  1. `EventsManager` on a permanent scene-root actor, exactly one in the scene.
  2. `Contact Mode = Distance`, no colliders: shoot an enemy 3× → it dies, `ScoreLabel`
     increments.
  3. Add the Bullet/Enemy physics setup; `Contact Mode = Colliders`: same result.
  4. `Both`: same result, no double hits/double score.
  5. Get touched: one life lost, player hides ~1 s, reappears; no freeze; `LivesLabel`
     updates.
  6. 5 kills → "You win!" then restart; 3 lives lost → "Game Over" then restart.
  7. Console: no duplicate-manager warning, no missing-file warning, no repeated
     "couldn't find" warnings.

---

## Files likely touched

| File | Change |
|---|---|
| `Source/Game/Runtime/EventWorld.cs` | `NameOf`, `IsTouching`, `Actors` |
| `Source/Game/Runtime/EventRuntime.cs` | name source, contact polling/edge sets, pronouns, wildcard `me` |
| `Source/Game/Bridge/FlaxWorld.cs` | `NameOf`, `HasTag`, `IsTouching`, `Actors` |
| `Source/Game/Bridge/EventSheet.cs` | `OwnerOf` ancestor walk |
| `Source/Game/Bridge/EventsManager.cs` | `ContactMode` + wiring, `HandleTouch` gating |
| `Source/Game/Bridge/FlaxActions.cs` | only if `AddMovement` is needed |
| `Source/Game/GameFiles/Bullet.events` | `to other` |
| `Tests/Program.cs` | regression + contact + message tests |
| `DESIGN.md` / `TASKS.md` | after verification: contact modes, manager rule, notes |

Do **not** change `Scene.scene` unless the user explicitly asks; the manager move and the
collider setup are theirs.

---

## Done when

- [ ] A fired bullet kills the touched enemy (3 hits by current stats), score increments.
- [ ] Other enemies are unaffected by that bullet.
- [ ] `Distance` mode works with no colliders.
- [ ] `Colliders` mode works with the Bullet/Enemy physics setup.
- [ ] `Both` mode does not double-fire.
- [ ] Player hurt → 1 s hide/show resumes; 3 hits → Game Over; 5 kills → win/restart.
- [x] Existing tests plus the new ones pass; `Source/Game.csproj` builds clean.
- [ ] `DESIGN.md` / `TASKS.md` notes updated (done); Task 4 Part B checked by the user
      (editor acceptance still pending).

---

## Watch-outs

- Flax `Actor.ToString()` is **not** the actor name. Never use it for logic.
- `Actor.IsActive = false` disables scripts on that actor (and its children). The event
  manager must never live on an actor that the game hides or spawns.
- `Clone()` copies the whole subtree, including `RigidBody`/`Collider`/`EventSheet`.
- The Enemy/Bullet templates are inactive; they must not be treated as contact targets.
- `when every 0 seconds` is intentional for per-tick bullet movement.
- `Level.FindActor("Enemy")` may return an active clone as well as the template; do not
  rely on it for staging spawns without checking `IsActive` if this becomes a problem.
