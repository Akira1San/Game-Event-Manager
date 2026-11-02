# Physics movement test scene

A small sandbox for **Task 4 Part C** (`Use Physics Movement`). It exercises all four paths:

| Actor | Body | What it proves |
|---|---|---|
| `PhysPlayer` | kinematic `RigidBody` | `move` goes through a swept `AddMovement` |
| `PhysBullet` | dynamic, gravity off, CCD on | `move toward` drives `LinearVelocity`; a fast trigger still hits |
| `PhysBall` | dynamic, gravity on | `move toward` + `stop moving` brakes horizontal velocity |
| `PhysCrate` | dynamic, gravity on | `push` is a real `AddForce(..., Impulse)` |

The event files already exist in `Source/Game/GameFiles/`:

- `PhysPlayer.events`
- `PhysBullet.events`
- `PhysTarget.events`
- `PhysBall.events`

Flax units here are centimetres (project gravity is `-981`). The values below assume a
1 m cube (100 units).

## 0. Before you press Play

1. **Rebuild the C# scripts** (`Build → Build Scripts`) so `EventSheet` shows the new
   `Use Physics Movement` checkbox.
2. In `Source/Game/GameFiles/`, rename **`Game.events` → `Game.events.off`**.
   `Game.events` is a global sheet: it would set `Lives`, spawn enemies, and restart the
   scene on 0 lives. Rename it back when you are done testing.

## 1. Scene skeleton

Create a new scene named `PhysicsTest` (`File → New Scene`). Keep the default `Sun`,
`Sky`, and `SkyLight`.

1. **Floor**
   - Add `Physics → Box Collider`, name it `Floor`.
   - Position `(0, -10, 0)`, Size `(2000, 20, 2000)`.
   - Leave `Is Trigger` **off**.
   - Optional: add a `Static Model` child using the same cube model as the floor in
     `Level1.scene`, scaled to `(20, 0.2, 20)`.
2. **Camera**
   - Add a `Camera`, name it `Camera`.
   - Position `(0, 800, -700)`, Rotation `(45, 0, 0)`.
3. **Manager**
   - Add an `Empty Actor`, name it `GameManager`.
   - Add script **`EventsManager`**.
   - Set **`Contact Mode` = `Colliders`** (the test uses trigger colliders, not
     distance checks).
   - This must be the **only** `EventsManager` in the scene, and it must not be on an
     actor that gets cloned.

## 2. `PhysPlayer` — kinematic body

1. Add `Physics → Rigid Body`, name it **`PhysPlayer`**, Position `(0, 50, 0)`.
2. On the body: `Is Kinematic` **on**, `Enable Gravity` **off**.
3. Add script **`EventSheet`** to the body:
   - `Event File` = `PhysPlayer`
   - `Use Physics Movement` **on**
4. Add children under the body (local position `0, 0, 0`):
   - `Physics → Box Collider`, Size `(100, 100, 100)`, `Is Trigger` **off**.
   - `Static Model` with the cube model, scale `(1, 1, 1)` (or `(100,100,100)` if the
     model is 1 unit).

The mesh and collider must be **children of the RigidBody**, not siblings: the body's
motion carries them.

## 3. `PhysBullet` — dynamic, CCD, trigger

1. Add `Physics → Rigid Body`, name it **`PhysBullet`**, Position `(-800, 50, 0)`.
2. On the body:
   - `Is Kinematic` **off**
   - `Enable Gravity` **off**
   - `Use CCD` **on**
   - `Override Mass` **on**, `Mass` = `0.1`
3. Add script **`EventSheet`**:
   - `Event File` = `PhysBullet`
   - `Use Physics Movement` **on**
4. Add children:
   - `Physics → Sphere Collider`, `Radius` = `10`, `Is Trigger` **on**.
   - Optional `Static Model` sphere, scale `(0.2, 0.2, 0.2)`.
5. Set the **template actor itself** `Is Active` **off** (checkbox at the top of the
   actor's properties). `spawn PhysBullet at me` clones it and activates the clone.

The trigger is what makes `when me touches PhysTarget` fire without bouncing the bullet.

`PhysBullet.events` marks the scene-start copy as the template (`when scene starts →
set my IsTemplate to true`) and skips the live logic for it. The manager still registers
the sheet on the inactive template so the clone inherits the `Use Physics Movement`
opt-in, but the template itself never moves and never destroys itself.

## 4. `PhysTarget` — the thing bullets hit

1. Add a `Static Model` (cube), name it **`PhysTarget`**, Position `(0, 50, 500)`.
2. Add script **`EventSheet`**:
   - `Event File` = `PhysTarget`
   - `Use Physics Movement` **off** (it never moves)
3. Add a child `Physics → Box Collider`, Size `(100, 100, 100)`, `Is Trigger` **off**.
4. Optional: duplicate it for a second target to prove `nearest` picks the closer one.
   Keep both named `PhysTarget`: `my Hits` is per-copy, and the HUD shows whichever
   one was hit last.

## 5. `PhysSpike` — hazard trigger

1. Add `Physics → Box Collider`, name it **`PhysSpike`**, Position `(-300, 50, 250)`.
2. Size `(100, 100, 100)`, **`Is Trigger` on**.
3. Optional: add a `Static Model` cube child so you can see it.

`PhysSpike` needs **no** `EventSheet`: the player's sheet receives the contact and
matches `when me touches PhysSpike`.

## 6. `PhysCrate` — the push target

1. Add `Physics → Rigid Body`, name it **`PhysCrate`**, Position `(300, 50, 300)`.
2. On the body: `Is Kinematic` **off**, `Enable Gravity` **on**,
   `Override Mass` **on**, `Mass` = `1`.
3. Add children:
   - `Physics → Box Collider`, Size `(100, 100, 100)`, `Is Trigger` **off**.
   - `Static Model` cube.
4. No `EventSheet` needed.

`Override Mass` matters: without it Flax derives ~1000 kg from the 1 m collider, and
`push ... strength 400` looks like nothing.

## 7. `PhysBall` — the dynamic mover

1. Add `Physics → Rigid Body`, name it **`PhysBall`**, Position `(-400, 50, 0)`.
2. On the body: `Is Kinematic` **off**, `Enable Gravity` **on**,
   `Override Mass` **on**, `Mass` = `1`.
3. Add script **`EventSheet`**:
   - `Event File` = `PhysBall`
   - `Use Physics Movement` **on**
4. Add children:
   - `Physics → Sphere Collider`, `Radius` = `50`, `Is Trigger` **off**.
   - Optional `Static Model` sphere.

## 8. HUD (optional but the events reference it)

1. Open `Level1.scene` and copy the **`ui canvas`** actor (it contains `ScoreLabel`).
2. Paste it into `PhysicsTest.scene`.
3. Rename `ScoreLabel` → **`LivesLabel`**.
4. Duplicate it and rename the copy **`HitsLabel`**; move it up ~40 px.

If you skip the HUD, the events still run; `set text of` only logs a warning.

## 9. Test checklist

Press Play with `PhysicsTest.scene` open and `Game.events.off` renamed.

- **WASD** moves the player smoothly (kinematic swept movement). Walking into the crate
  shoves it.
- **Walk into `PhysSpike`** → `Lives` drops by 1 and the label updates. The trigger only
  fires on enter, so step out and back in for another hit; the 1 second `Invulnerable`
  window ignores a hit if you re-enter too quickly.
- **Left Mouse** → a bullet appears and homes on the target; `Hits` increments and the
  target flashes for 0.15 s.
- **E** → the crate jumps up (about 0.8 m with `strength 400`, mass 1). Tune the number
  if you change the mass.
- **Hold T** → the ball rolls toward the crate and pushes it. **Release T** → the ball
  brakes (it does not keep sliding).
- The bullet's `when every 5 seconds → destroy me` keeps missed bullets from piling up.

## 10. Restore

Rename `Game.events.off` back to `Game.events` when you are done, so the shooter scenes
keep their global sheet.
